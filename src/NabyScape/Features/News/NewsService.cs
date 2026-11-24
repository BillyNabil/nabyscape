using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

public sealed partial class Settings
{
    public string NewsApiBaseUrl {get;set;}="https://news.anyway-community.ru/api";
    public bool NewsConnected {get;set;}
}

internal sealed record NewsItem(string Id,string Title,string Source,int SourceCount,Uri? Url,string[] Notes,string Cover="")
{
    internal string[] SourceIds {get;init;}=Array.Empty<string>();
    internal NewsSource[] Sources {get;init;}=Array.Empty<NewsSource>();
    internal string SourcesApi {get;init;}="";
}
internal sealed record NewsSource(string Id,string Name,string Avatar);
internal sealed record NewsSnapshot(string Title,NewsItem[] Items,DateTimeOffset FetchedAt);

// Public read-only API: never reuse Telegram sessions, bot tokens or dev/admin auth.
internal sealed class NewsService : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,NewsSnapshot> cache=new();
    private readonly Dictionary<string,DateTimeOffset> attempts=new();
    private readonly Dictionary<string,byte[]> images=new();
    private readonly Dictionary<string,(DateTimeOffset At,NewsSource? Source)> sources=new();
    internal NewsSource[] SourcesFor(NewsItem item,string address)
        =>item.SourcesApi==BaseUri(address).AbsoluteUri?item.Sources:Array.Empty<NewsSource>();
    internal static readonly Uri AppLink=new("https://t.me/anyway_news_bot?startapp");
    internal NewsService(HttpMessageHandler? handler=null,TimeProvider? timeProvider=null)
    {
        clock=timeProvider??TimeProvider.System;
        http=new(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(15)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NabyScape-Anyway/1.0");
    }
    internal static Uri BaseUri(string value)
    {
        if(!Uri.TryCreate(value?.Trim(),UriKind.Absolute,out var uri) ||
           uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback) ||
           uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0)
            throw new ArgumentException(Localization.Text("Use an HTTPS API address or HTTP on localhost, without credentials or parameters."));
        return new Uri(uri.AbsoluteUri.TrimEnd('/')+"/");
    }
    internal NewsSnapshot? Saved(string address)
    {try{return cache.GetValueOrDefault(BaseUri(address).AbsoluteUri);}catch(ArgumentException){return null;}}
    internal async Task<NewsSnapshot> Get(string address,bool force,CancellationToken token)
    {
        var root=BaseUri(address);string key=root.AbsoluteUri;
        await gate.WaitAsync(token);
        try
        {
            var now=clock.GetUtcNow();
            if(cache.TryGetValue(key,out var saved) && (!force && now-saved.FetchedAt<TimeSpan.FromMinutes(5)))return saved;
            if(attempts.TryGetValue(key,out var last) && now-last<TimeSpan.FromSeconds(15))
            {if(saved!=null)return saved;throw new HttpRequestException(Localization.Text("Try again in a few seconds."));}
            attempts[key]=now;
            var sourceToken=token;using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));token=timeout.Token;
            var endpoint=new Uri(root,"v2/digest");
            using var response=await http.GetAsync(endpoint,HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode)throw new HttpRequestException(Localization.Format("News API is unavailable ({0}). Try again later.",(int)response.StatusCode),null,response.StatusCode);
            using var stream=await response.Content.ReadAsStreamAsync(token);
            using var data=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await stream.ReadAsync(buffer,token))>0)
            {if(data.Length+count>1024*1024)throw new InvalidDataException(Localization.Text("The news response is too large."));await data.WriteAsync(buffer.AsMemory(0,count),token);}
            var snapshot=Parse(data.ToArray(),clock.GetUtcNow());
            snapshot=await LoadSourceDetails(root,snapshot,sourceToken);cache[key]=snapshot;return snapshot;
        }
        finally{gate.Release();}
    }
    private async Task<NewsSnapshot> LoadSourceDetails(Uri root,NewsSnapshot snapshot,CancellationToken token)
    {
        var ids=snapshot.Items.SelectMany(i=>i.SourceIds).Where(id=>long.TryParse(id,out _)).Distinct().Take(20).ToArray();
        string key=root.AbsoluteUri;var now=clock.GetUtcNow();
        var missing=ids.Where(id=>!sources.TryGetValue(key+id,out var entry) || now-entry.At>=TimeSpan.FromMinutes(10)).ToArray();
        if(missing.Length>0)
        {
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var uri=new Uri(root,"v2/integrations/news/sources?ids="+string.Join(',',missing.Select(Uri.EscapeDataString)));
                using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
                using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
                while((read=await stream.ReadAsync(buffer,timeout.Token))>0)
                {if(bytes.Length+read>256*1024)throw new InvalidDataException("Source metadata response is too large.");await bytes.WriteAsync(buffer.AsMemory(0,read),timeout.Token);}
                using var doc=JsonDocument.Parse(bytes.ToArray());
                if(doc.RootElement.ValueKind!=JsonValueKind.Object || !doc.RootElement.TryGetProperty("sources",out var entries) || entries.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Unknown source metadata format.");
                var found=new Dictionary<string,NewsSource>();
                foreach(var entry in entries.EnumerateArray().Take(20))
                {
                    if(entry.ValueKind!=JsonValueKind.Object || !entry.TryGetProperty("id",out var id) || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))continue;
                    string sid=id.ToString();if(!missing.Contains(sid) || !entry.TryGetProperty("name",out var name) || name.ValueKind!=JsonValueKind.String)continue;
                    string label=name.GetString()??"",avatar=entry.TryGetProperty("avatar",out var image) && image.ValueKind==JsonValueKind.String?image.GetString()??"":"";
                    found[sid]=new(sid,label.Length>200?label[..200]:label,avatar.Length<=2000 && CoverUri(key,avatar)!=null?avatar:"");
                }
                foreach(string id in missing)
                {if(sources.Count>=256)sources.Remove(sources.Keys.First());sources[key+id]=(now,found.GetValueOrDefault(id));}
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested){/* Source metadata must not hide the news. */}
            catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException or JsonException or ArgumentException){/* Keep cached labels or neutral circles. */}
        }
        return snapshot with{Items=snapshot.Items.Select(item=>item with{
            SourcesApi=key,Sources=item.SourceIds.Where(id=>sources.TryGetValue(key+id,out var entry) && entry.Source!=null).Select(id=>sources[key+id].Source!).Take(2).ToArray()
        }).ToArray()};
    }
    internal static Uri? SafeLink(string? value)
        =>Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length==0?uri:null;
    internal static Uri? CoverUri(string address,string cover)
    {
        if(string.IsNullOrWhiteSpace(cover))return null;
        var root=BaseUri(address);
        if(!Uri.TryCreate(root,cover,out var uri) || uri.Scheme!=root.Scheme || uri.Authority!=root.Authority || uri.UserInfo.Length>0)return null;
        return uri;
    }
    internal async Task<byte[]?> Cover(string address,string cover,CancellationToken token)
    {
        var uri=CoverUri(address,cover);if(uri==null)return null;
        if(images.TryGetValue(uri.AbsoluteUri,out var saved))return saved;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));token=timeout.Token;
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/",StringComparison.OrdinalIgnoreCase)!=true)return null;
        using var source=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await source.ReadAsync(buffer,token))>0)
        {if(bytes.Length+read>2*1024*1024)return null;await bytes.WriteAsync(buffer.AsMemory(0,read),token);}
        var data=bytes.ToArray();if(images.Count>=32)images.Remove(images.Keys.First());images[uri.AbsoluteUri]=data;return data;
    }
    internal static NewsSnapshot Parse(byte[] json,DateTimeOffset fetchedAt)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("items",out var items) || items.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException(Localization.Text("The news API returned an unknown format."));
        static string Text(JsonElement e,string name,int limit=8000)
        {if(!e.TryGetProperty(name,out var v))return "";var s=v.ValueKind==JsonValueKind.String?v.GetString()??"":v.ValueKind==JsonValueKind.Number?v.GetRawText():"";return s.Length>limit?s[..limit]:s;}
        var result=new List<NewsItem>();var seen=new HashSet<string>();
        foreach(var item in items.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)continue;
            string id=Text(item,"postId",80),title=Text(item,"head",2000);
            if(title.Length==0 || !seen.Add(id.Length>0?id:title))continue;
            var notes=item.TryGetProperty("notes",out var n) && n.ValueKind==JsonValueKind.Array?n.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).ToArray():Array.Empty<string>();
            string source=Text(item,"source",200);
            int sourceCount=item.TryGetProperty("nsrc",out var ns) && ns.ValueKind==JsonValueKind.Number && ns.TryGetInt32(out int count)?Math.Max(0,count):0;
            var url=SafeLink(Text(item,"url"));
            if(id.Length>0 && id.All(char.IsAsciiDigit))url=new Uri("https://t.me/anyway_news_bot/app?startapp=post_"+id);
            var sourceIds=item.TryGetProperty("srcs",out var ids) && ids.ValueKind==JsonValueKind.Array?ids.EnumerateArray().Where(v=>v.ValueKind is JsonValueKind.String or JsonValueKind.Number).Select(v=>v.ToString()).Take(4).ToArray():Array.Empty<string>();
            result.Add(new(id,title,source,sourceCount,url,notes,Text(item,"cover",2000)){SourceIds=sourceIds});
            if(result.Count==5)break;
        }
        return new(Text(root,"title",200),result.ToArray(),fetchedAt);
    }
    public void Dispose(){http.Dispose();}
}
