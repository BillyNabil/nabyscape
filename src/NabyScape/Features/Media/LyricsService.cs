using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed record LyricLine(double At,string Text);
internal sealed record LyricResult(LyricLine[] Lines,string Status,string Plain="");
internal static class LyricTiming
{
    private static readonly Regex stamps=new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static LyricLine[] Parse(string source)
    {
        if(source.Length>512*1024)throw new InvalidDataException(Localization.Text("Lyrics are too large."));
        var result=new List<LyricLine>();double offset=0;
        var match=Regex.Match(source,@"\[offset:([+-]?\d{1,6})\]",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
        if(match.Success)offset=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture)/1000d;
        foreach(string raw in source.Split('\n').Take(5000))
        {
            if(result.Count>=10000)break;
            var matches=stamps.Matches(raw);if(matches.Count==0)continue;string text=stamps.Replace(raw,"").Trim();if(text.Length>500)text=text[..500];
            foreach(Match time in matches)
            {
                int seconds=int.Parse(time.Groups[2].Value,CultureInfo.InvariantCulture);if(seconds>=60)continue;
                string fraction=time.Groups[3].Value;double at=int.Parse(time.Groups[1].Value,CultureInfo.InvariantCulture)*60+seconds+(fraction.Length==0?0:int.Parse(fraction,CultureInfo.InvariantCulture)/Math.Pow(10,fraction.Length))+offset;
                if(at>=0 && at<=86400)result.Add(new(at,text));
            }
        }
        return result.OrderBy(line=>line.At).DistinctBy(line=>(line.At,line.Text)).ToArray();
    }
    internal static int Index(IReadOnlyList<LyricLine> lines,double seconds)
    {int low=0,high=lines.Count-1,result=-1;while(low<=high){int middle=(low+high)/2;if(lines[middle].At<=seconds){result=middle;low=middle+1;}else high=middle-1;}return result;}
    // Use timestamped blank/section markers as breaks, never guess from line length
    // or audio level: a long sung line must remain visible until its next timestamp.
    internal static int VocalIndex(IReadOnlyList<LyricLine> lines,double seconds)
    {
        int index=Index(lines,seconds);
        if(index<0)return -1;
        string text=lines[index].Text.Trim();
        // Brief timestamp gaps between vocal lines are transitions, not instrumental breaks.
        if(text.Length==0 && index>0 && index+1<lines.Count && lines[index+1].At-lines[index].At<=1 && lines[index+1].Text.Trim().Length>0 && lines[index-1].Text.Trim().Length>0)
        {index--;text=lines[index].Text.Trim();}
        if(text.Length==0 || text.Trim('♪','♫','♬','♩',' ','\t').Length==0 || text is "🎵" or "🎶")return -1;
        string marker=text.Trim('[',']','(',')',' ','\t');
        if(marker.Equals("instrumental",StringComparison.OrdinalIgnoreCase) || marker.Equals("instrumental break",StringComparison.OrdinalIgnoreCase))return -1;
        bool section=text[0] is '[' or '(';
        if(section && (marker.Equals("music",StringComparison.OrdinalIgnoreCase) || marker.Equals("interlude",StringComparison.OrdinalIgnoreCase) || marker.Equals("intro",StringComparison.OrdinalIgnoreCase) || marker.Equals("outro",StringComparison.OrdinalIgnoreCase) || marker.Equals("solo",StringComparison.OrdinalIgnoreCase)))return -1;
        return index;
    }
    internal static bool CanUse(MediaService media)=>media.Available && !string.IsNullOrWhiteSpace(media.Title) && media.Timeline().duration>0 &&
        (!string.IsNullOrWhiteSpace(media.Artist) && media.Artist!=media.Selected?.SourceAppUserModelId || LyricsMetadata.Queries(media.Title,"").Length>0);
    internal static bool IsSpotify(string? source)=>source?.Contains("spotify",StringComparison.OrdinalIgnoreCase)==true;
}

// Explicitly enabled metadata lookup. No cookies, profile files or screen contents
// are read. Manual authenticated sources use Credential Manager; Automatic uses public sources.
internal sealed partial class LyricsService : IDisposable
{
    private readonly HttpClient client;
    private readonly IAssistantKeys lyricKeys;
    private readonly Dictionary<string,LyricResult> cache=new();
    private CancellationTokenSource? pending;
    private string key="";
    private bool disposed;
    private readonly HashSet<object> consumers=new();
    internal void Acquire(object owner)=>consumers.Add(owner);
    internal void Release(object owner)
    {
        if(!consumers.Remove(owner) || consumers.Count>0)return;
        Cancel();key="";Set(new(Array.Empty<LyricLine>(),Localization.Text("Lyrics are off.")));
    }
    internal LyricResult Result {get;private set;}=new(Array.Empty<LyricLine>(),Localization.Text("Lyrics are off."));
    internal event Action? Changed;
    private static string TrackKey(string title,string artist,double duration)=>title+"\n"+artist+"\n"+Math.Round(duration);
    internal LyricsService(HttpMessageHandler? handler=null,IAssistantKeys? providerKeys=null)
    {lyricKeys=providerKeys??new AssistantKeys();client=new(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=2*1024*1024};client.DefaultRequestHeaders.UserAgent.ParseAdd("NabyScape/2.7.0 (+https://github.com/BillyNabil/nabyscape)");}
    internal async Task Load(string title,string artist,double duration,bool enabled,bool compatible,string? mediaSource=null)
    {
        if(disposed)return;
        if(!enabled || !compatible){Cancel();key="";Set(new(Array.Empty<LyricLine>(),enabled?Localization.Text("Choose a music session with a title, artist and playback timeline."):Localization.Text("Lyrics are off.")));return;}
        string selectedSource=SelectedSource;string next=selectedSource+"\n"+TrackKey(title,artist,duration);if(next==key)return;Cancel();key=next;
        if(cache.TryGetValue(next,out var found)){Set(found);return;}
        var request=new CancellationTokenSource();pending=request;Set(new(Array.Empty<LyricLine>(),Localization.Text("Finding synced lyrics…")));
        try
        {
            if(selectedSource=="Automatic")
            {
                var match=await FindAutomatic(title,artist==mediaSource?"":artist,duration,request.Token);
                if(request.IsCancellationRequested || disposed || key!=next)return;
                var automatic=match==null?new LyricResult(Array.Empty<LyricLine>(),Localization.Text("No matching synced lyrics. Try another source or LRC.")):new LyricResult(LyricTiming.Parse(match.Value.Choice.Lrc),Localization.Text("Synced lyrics · ")+match.Value.Source+Localization.Text(" · Automatic"));
                if(automatic.Lines.Length>0){if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[next]=automatic;}
                Set(automatic);return;
            }
            if(string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))throw new InvalidDataException(Localization.Text("Track details are unavailable."));
            if(selectedSource!="LRCLIB")
            {
                var choices=await FetchProvider(selectedSource,title,artist,duration,request.Token);
                if(request.IsCancellationRequested || disposed || key!=next)return;
                var chosen=choices.FirstOrDefault();
                var providerResult=chosen==null?new LyricResult(Array.Empty<LyricLine>(),Localization.Text("No synced lyrics · ")+selectedSource):new LyricResult(LyricTiming.Parse(chosen.Lrc),(chosen.Plain.Length>0?Localization.Text("Plain lyrics · "):Localization.Text("Synced lyrics · "))+selectedSource,chosen.Plain);
                if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[next]=providerResult;Set(providerResult);return;
            }
            string query="https://lrclib.net/api/get?track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist);
            if(duration>=1 && duration<=3600)query+="&duration="+Math.Round(duration).ToString(CultureInfo.InvariantCulture);
            using var response=await client.GetAsync(query,HttpCompletionOption.ResponseHeadersRead,request.Token);
            LyricResult result;
            if(response.StatusCode==HttpStatusCode.NotFound)result=new(Array.Empty<LyricLine>(),Localization.Text("No synced lyrics for this track."));
            else
            {
                response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1024*1024)throw new InvalidDataException(Localization.Text("Lyrics response is too large."));
                await using var body=await response.Content.ReadAsStreamAsync(request.Token);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
                while((count=await body.ReadAsync(chunk,request.Token))>0){if(buffer.Length+count>1024*1024)throw new InvalidDataException(Localization.Text("Lyrics response is too large."));buffer.Write(chunk,0,count);}
                using var json=JsonDocument.Parse(buffer.ToArray());var data=json.RootElement;
                bool instrumental=data.TryGetProperty("instrumental",out var ins) && ins.ValueKind==JsonValueKind.True;
                string? lrc=data.TryGetProperty("syncedLyrics",out var value) && value.ValueKind==JsonValueKind.String?value.GetString():null;
                var lines=lrc==null?Array.Empty<LyricLine>():LyricTiming.Parse(lrc);
                result=new(lines,lines.Length>0?Localization.Text("Synced lyrics · LRCLIB"):instrumental?Localization.Text("Instrumental track"):Localization.Text("No synced lyrics for this track."));
            }
            if(request.IsCancellationRequested || disposed || key!=next)return;
            if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[next]=result;Set(result);
        }
        catch(OperationCanceledException){if(!request.IsCancellationRequested && !disposed && key==next)Set(new(Array.Empty<LyricLine>(),Localization.Text("Lyrics request timed out. Tap Retry.")));}
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException or JsonException or RegexMatchTimeoutException or System.Xml.XmlException or FormatException)
        {if(!request.IsCancellationRequested && !disposed && key==next)Set(new(Array.Empty<LyricLine>(),Localization.Text("Lyrics unavailable. Tap Retry.")));}
        finally{if(ReferenceEquals(pending,request))pending=null;request.Dispose();}
    }
    internal async Task<LyricChoice[]> Search(string title,string artist,CancellationToken token,Uri? endpoint=null)
    {
        var baseUri=endpoint??new Uri("https://lrclib.net/");
        if(!baseUri.IsAbsoluteUri || !(baseUri.Scheme=="https" || baseUri.Scheme=="http" && baseUri.IsLoopback) || baseUri.UserInfo.Length>0 || baseUri.Query.Length>0 || baseUri.Fragment.Length>0)throw new InvalidDataException(Localization.Text("Use an HTTPS API base URL, or a local HTTP server."));
        var url=new Uri(baseUri.AbsoluteUri.TrimEnd('/')+"/api/search?track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist));
        string json=await client.GetStringAsync(url,token);
        using var document=JsonDocument.Parse(json);var result=new List<LyricChoice>();
        if(document.RootElement.ValueKind!=JsonValueKind.Array)return result.ToArray();
        foreach(var entry in document.RootElement.EnumerateArray().Take(30))
        {
            if(entry.ValueKind!=JsonValueKind.Object)continue;
            string Text(string key)=>entry.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
            string lrc=Text("syncedLyrics");if(lrc.Length==0 || lrc.Length>512*1024)continue;
            double seconds=entry.TryGetProperty("duration",out var d) && d.ValueKind==JsonValueKind.Number && d.TryGetDouble(out double n)?n:0;
            result.Add(new(Text("trackName"),Text("artistName"),Text("albumName"),seconds,lrc));
        }
        return result.ToArray();
    }
    internal async Task<LyricChoice[]> SearchBini(string title,string artist,CancellationToken token,int limit=8)
    {
        string json=await ReadBiniDocument(new Uri("https://lyrics-api.binimum.org/getLyrics?track="+Uri.EscapeDataString(title)+"&artist="+Uri.EscapeDataString(artist)),token);
        using var doc=JsonDocument.Parse(json);var choices=new List<LyricChoice>();
        if(!doc.RootElement.TryGetProperty("results",out var entries) || entries.ValueKind!=JsonValueKind.Array)return choices.ToArray();
        foreach(var item in entries.EnumerateArray().Take(Math.Clamp(limit,1,8)))
        {
            if(item.ValueKind!=JsonValueKind.Object)continue;
            string Text(string key)=>item.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
            if(!Uri.TryCreate(Text("lyricsUrl"),UriKind.Absolute,out var url) || url.Scheme!="https" || url.UserInfo.Length>0 || !url.IsDefaultPort || !(url.Host=="lrc.red" || url.Host=="lyrics-api.binimum.org"))continue;
            string body=await ReadBiniDocument(url,token);string lrc=TimedLyricsDocument.ToLrc(body);
            if(lrc.Length==0)continue;
            double seconds=item.TryGetProperty("duration",out var d) && d.ValueKind==JsonValueKind.Number && d.TryGetDouble(out var n)?n:0;
            choices.Add(new(Text("track_name"),Text("artist_name"),Text("album_name"),seconds,lrc));
        }
        return choices.ToArray();
    }
    private async Task<string> ReadBiniDocument(Uri url,CancellationToken token)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(8));token=budget.Token;
        for(int redirects=0;redirects<=3;redirects++)
        {
            if(url.Scheme!="https" || !url.IsDefaultPort || url.UserInfo.Length>0 || url.Host is not ("lyrics-api.binimum.org" or "lrc.red"))throw new InvalidDataException(Localization.Text("Lyrics redirect is outside the trusted provider hosts."));
            using var request=new HttpRequestMessage(HttpMethod.Get,url);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if(response.Headers.Location==null)throw new InvalidDataException(Localization.Text("Lyrics redirect has no destination."));
                url=new Uri(url,response.Headers.Location);continue;
            }
            response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
            while((count=await stream.ReadAsync(chunk,token))>0){if(buffer.Length+count>2*1024*1024)throw new InvalidDataException(Localization.Text("Lyrics response is too large."));buffer.Write(chunk,0,count);}
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        throw new InvalidDataException(Localization.Text("Too many lyrics redirects."));
    }
    private void Set(LyricResult result){if(ReferenceEquals(originalResult,result))return;originalResult=result;Present(result);}
    internal void Import(string title,string artist,double duration,string lrc,string origin="Local LRC · this session")
    {
        var lines=LyricTiming.Parse(lrc);if(lines.Length==0)throw new InvalidDataException(Localization.Text("Choose an LRC file with [mm:ss] line timestamps."));
        Cancel();key=SelectedSource+"\n"+TrackKey(title,artist,duration);var result=new LyricResult(lines,origin=="Local LRC · this session"?Localization.Text("Local LRC · this session"):origin);if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[key]=result;Set(result);
    }
    internal void ImportPlain(string title,string artist,double duration,string text,string origin)
    {
        if(string.IsNullOrWhiteSpace(text) || text.Length>100000)throw new InvalidDataException(Localization.Text("Plain lyrics are empty or too large."));
        Cancel();key=SelectedSource+"\n"+TrackKey(title,artist,duration);var result=new LyricResult(Array.Empty<LyricLine>(),origin,text);if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[key]=result;Set(result);
    }
    internal void Retry(){cache.Remove(key);key="";}
    internal void Cancel(){if(pending!=null){pending.Cancel();pending=null;key="";}}
    public void Dispose(){disposed=true;Cancel();++textRevision;romanizing?.Cancel();romanizing=null;romanized.Clear();client.Dispose();cache.Clear();}
}

internal sealed record LyricChoice(string Title,string Artist,string Album,double Duration,string Lrc,string Plain="");
