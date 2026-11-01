using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal static class LyricsProviders
{
    internal static readonly string[] Names={"lrc.red","BiniLyrics","BetterLyrics","BetterLyrics Potato","PaxSenix","PaxSenix: Spotify","PaxSenix: Musixmatch","LyricsPlus","Unison","YouTube captions","YouTube Music","KuGou","LRCLIB","Musixmatch"};
    internal const string PaxKey="e8c75c4cbdf942a287e83dfb7d132497";
    internal const string MusixmatchKey="f81603ae56d94e67a516a047ca2ed735";
    internal static bool NeedsConnector(string source)=>false;
    internal static bool NeedsVideo(string source)=>source is "YouTube captions" or "YouTube Music";
    internal static string KeyFor(string source)=>source=="Musixmatch"?MusixmatchKey:PaxKey;
    internal static bool NeedsKey(string source)=>source is "PaxSenix: Spotify" or "PaxSenix: Musixmatch" or "Musixmatch";
}
internal sealed partial class LyricsService
{
    internal string SelectedSource {get;set;}="Automatic";
    private readonly Dictionary<string,Uri> connectors=new();
    private string videoTrack="",videoId="";
    internal bool HasVideo(string title,string artist)=>videoTrack==title+"\n"+artist && videoId.Length==11;
    internal void ConfigureVideo(string input,string title,string artist)
    {
        string id=input.Trim();
        if(Uri.TryCreate(id,UriKind.Absolute,out var uri))
        {
            if(uri.Host=="youtu.be")id=uri.AbsolutePath.Trim('/');
            else if(uri.Host is "youtube.com" or "www.youtube.com" or "music.youtube.com")
            {var match=System.Text.RegularExpressions.Regex.Match(uri.Query,@"(?:[?&])v=([A-Za-z0-9_-]{11})(?:&|$)",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));id=match.Success?match.Groups[1].Value:"";}
            else id="";
        }
        if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{11}\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("Paste a YouTube watch link or an 11-character video ID.");
        videoTrack=title+"\n"+artist;videoId=id;
    }
    internal void ConfigureConnector(string source,Uri endpoint)
    {
        if(endpoint.Scheme!="https" || endpoint.UserInfo.Length>0 || endpoint.Query.Length>0 || endpoint.Fragment.Length>0)throw new InvalidDataException("Enter an HTTPS connector base URL without a key in the URL.");
        connectors[source]=endpoint;
    }
    internal bool HasConnector(string source)=>connectors.ContainsKey(source);
    internal async Task<LyricChoice[]> FetchProvider(string source,string title,string artist,double duration,CancellationToken token)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));token=budget.Token;
        if(source=="LRCLIB")return await Search(title,artist,token);
        if(source=="BiniLyrics")return await SearchBini(title,artist,token);
        if(LyricsProviders.NeedsVideo(source))
        {
            if(!HasVideo(title,artist))throw new InvalidDataException("Add the YouTube video link for this track first.");
            return await FetchYouTube(source,videoId,title,artist,duration,token);
        }
        if(source=="Musixmatch")
        {
            string? apiKey=lyricKeys.Read(LyricsProviders.MusixmatchKey);
            if(string.IsNullOrWhiteSpace(apiKey))throw new InvalidDataException("Add your Musixmatch developer API key first.");
            string json=await client.GetStringAsync("https://api.musixmatch.com/ws/1.1/matcher.lyrics.get?q_track="+Uri.EscapeDataString(title)+"&q_artist="+Uri.EscapeDataString(artist)+"&apikey="+Uri.EscapeDataString(apiKey),token);
            using var mxm=JsonDocument.Parse(json);
            var message=mxm.RootElement.GetProperty("message");int code=message.GetProperty("header").GetProperty("status_code").GetInt32();
            if(code!=200)throw new InvalidDataException("Musixmatch returned status "+code+". Check your API key and account access.");
            if(!message.GetProperty("body").TryGetProperty("lyrics",out var lyrics))return Array.Empty<LyricChoice>();
            string plain=Text(lyrics,"lyrics_body");return plain.Length==0?Array.Empty<LyricChoice>():new[]{new LyricChoice(title,artist,Text(lyrics,"lyrics_copyright"),duration,"",plain)};
        }
        if(LyricsProviders.NeedsConnector(source))
        {
            if(!connectors.TryGetValue(source,out var endpoint))throw new InvalidDataException("Connect an authorized LRCLIB-compatible lyrics server for "+source+" first.");
            return await Search(title,artist,token,endpoint);
        }
        string E(string s)=>Uri.EscapeDataString(s);
        async Task<string> Get(string url,bool authenticated=false)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,url);
            if(authenticated)
            {
                string? key=lyricKeys.Read(LyricsProviders.PaxKey);
                if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("Add your PaxSenix API key first.");
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
            }
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode==401 || (int)response.StatusCode==403)throw new InvalidDataException("This provider requires a valid connection or API key.");
            response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync(token);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
            while((count=await stream.ReadAsync(chunk,token))>0){if(buffer.Length+count>2*1024*1024)throw new InvalidDataException("Provider response is too large.");buffer.Write(chunk,0,count);}
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        LyricChoice[] One(string body)
        {
            string lrc=ProviderDocument(body);
            lrc=System.Text.RegularExpressions.Regex.Replace(lrc,@"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>","",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            return lrc.Length==0?Array.Empty<LyricChoice>():new[]{new LyricChoice(title,artist,source,duration,lrc)};
        }
        if(source is "BetterLyrics" or "BetterLyrics Potato")return One(await Get("https://lyrics-api.boidu.dev/"+(source=="BetterLyrics"?"getLyrics":"qq/getLyrics")+"?s="+E(title)+"&a="+E(artist)+"&d="+Math.Round(duration).ToString(CultureInfo.InvariantCulture)));
        if(source=="Unison")return One(await Get("https://unison.boidu.dev/lyrics/search?song="+E(title)+"&artist="+E(artist)));
        if(source=="LyricsPlus")return One(await Get("https://lyricsplus.binimum.org/v2/lyrics/get?title="+E(title)+"&artist="+E(artist)+"&duration="+Math.Round(duration).ToString(CultureInfo.InvariantCulture)));
        if(source=="PaxSenix: Musixmatch")return One(await Get("https://api.paxsenix.org/lyrics/musixmatch?t="+E(title)+"&a="+E(artist)+"&d="+Math.Round(duration).ToString(CultureInfo.InvariantCulture),true));
        if(source=="lrc.red")
        {
            using var doc=JsonDocument.Parse(await Get("https://lrc.red/search.json?q="+E(title+" "+artist)));var choices=new List<LyricChoice>();
            if(!doc.RootElement.TryGetProperty("hits",out var hits))return choices.ToArray();
            foreach(var hit in hits.EnumerateArray().Take(5))
            {
                string id=Text(hit,"isrc");if(id.Length>32 || id.Length==0 || !id.All(char.IsLetterOrDigit))continue;
                string lrc=TimedLyricsDocument.ToLrc(await Get("https://lrc.red/s/"+id+".ttml"));if(lrc.Length>0)choices.Add(new(Text(hit,"title"),Text(hit,"artist"),Text(hit,"album"),duration,lrc));
            }
            return choices.ToArray();
        }
        if(source=="KuGou")
        {
            using var doc=JsonDocument.Parse(await Get("https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword="+E(title+" "+artist)));var choices=new List<LyricChoice>();
            if(!doc.RootElement.TryGetProperty("candidates",out var candidates))return choices.ToArray();
            foreach(var hit in candidates.EnumerateArray().Take(5))
            {
                using var download=JsonDocument.Parse(await Get("https://lyrics.kugou.com/download?fmt=lrc&charset=utf8&client=pc&ver=1&id="+E(Text(hit,"id"))+"&accesskey="+E(Text(hit,"accesskey"))));
                string content=Text(download.RootElement,"content");if(content.Length==0)continue;
                string lrc=Encoding.UTF8.GetString(Convert.FromBase64String(content));if(LyricTiming.Parse(lrc).Length>0)choices.Add(new(Text(hit,"song"),Text(hit,"singer"),source,duration,lrc));
            }
            return choices.ToArray();
        }
        if(source=="PaxSenix")
        {
            using var doc=JsonDocument.Parse(await Get("https://itunes.apple.com/search?entity=song&limit=5&term="+E(title+" "+artist)));var choices=new List<LyricChoice>();
            if(!doc.RootElement.TryGetProperty("results",out var results))return choices.ToArray();
            HttpRequestException? unavailable=null;
            foreach(var hit in results.EnumerateArray().Take(5))
            {
                string id=Text(hit,"trackId");if(id.Length==0)continue;
                try
                {
                    string lrc=ProviderDocument(await Get("https://lyrics.paxsenix.org/apple-music/lyrics?ttml=true&id="+E(id)));
                    if(lrc.Length>0)choices.Add(new(Text(hit,"trackName"),Text(hit,"artistName"),Text(hit,"collectionName"),duration,lrc));
                }
                catch(HttpRequestException ex){unavailable=ex;}
            }
            if(choices.Count==0 && unavailable!=null)throw unavailable;
            return choices.ToArray();
        }
        if(source=="PaxSenix: Spotify")
        {
            using var doc=JsonDocument.Parse(await Get("https://api.paxsenix.org/spotify/search?q="+E(title+" "+artist),true));
            string? FindId(JsonElement node,int depth=0)
            {
                if(depth>8)return null;
                if(node.ValueKind==JsonValueKind.Object){string id=Text(node,"id");if(id.Length==22 && id.All(char.IsLetterOrDigit))return id;foreach(var child in node.EnumerateObject()){var found=FindId(child.Value,depth+1);if(found!=null)return found;}}
                else if(node.ValueKind==JsonValueKind.Array)foreach(var child in node.EnumerateArray().Take(20)){var found=FindId(child,depth+1);if(found!=null)return found;}return null;
            }
            string? track=FindId(doc.RootElement);return track==null?Array.Empty<LyricChoice>():One(await Get("https://api.paxsenix.org/lyrics/spotify?id="+E(track),true));
        }
        throw new InvalidDataException("Unknown lyrics provider.");
    }
    private static string Text(JsonElement element,string key)=>element.ValueKind==JsonValueKind.Object && element.TryGetProperty(key,out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():"";
    internal static string ProviderDocument(string body)
    {
        if(body.Length>2*1024*1024)throw new InvalidDataException("Lyrics response is too large.");
        if(body.TrimStart().StartsWith("<",StringComparison.Ordinal) || body.TrimStart().StartsWith("[0",StringComparison.Ordinal))return TimedLyricsDocument.ToLrc(body);
        using var doc=JsonDocument.Parse(body);
        string Find(JsonElement node,int depth)
        {
            if(depth>10)return "";
            if(node.ValueKind==JsonValueKind.String){string s=node.GetString()??"";if(s.Length>1024*1024)return "";if(s.TrimStart().StartsWith("<",StringComparison.Ordinal))return TimedLyricsDocument.ToLrc(s);return LyricTiming.Parse(s).Length>0?s:"";}
            if(node.ValueKind==JsonValueKind.Object)
            {
                foreach(string key in new[]{"ttml","ttmlContent","syncedLyrics","lrc","elrc","lyrics","data","content","result"})if(node.TryGetProperty(key,out var value)){string found=Find(value,depth+1);if(found.Length>0)return found;}
            }
            if(node.ValueKind==JsonValueKind.Array)
            {
                var lines=new StringBuilder();
                foreach(var row in node.EnumerateArray().Take(5000))
                {
                    string text=Text(row,"text");if(text.Length==0 && row.ValueKind==JsonValueKind.Object && row.TryGetProperty("text",out var words) && words.ValueKind==JsonValueKind.Array)text=string.Join(" ",words.EnumerateArray().Select(w=>Text(w,"text")));string time=Text(row,"time");if(time.Length==0)time=Text(row,"timestamp");if(time.Length==0)time=Text(row,"startTimeMs");
                    if(text.Length>0 && double.TryParse(time,NumberStyles.Float,CultureInfo.InvariantCulture,out double ms) && double.IsFinite(ms) && ms>=0 && ms<=86400000){long cs=(long)Math.Round(ms/10);lines.Append('[').Append((cs/6000).ToString("00",CultureInfo.InvariantCulture)).Append(':').Append(((cs%6000)/100d).ToString("00.00",CultureInfo.InvariantCulture)).Append(']').AppendLine(text);}
                    else {string found=Find(row,depth+1);if(found.Length>0)return found;}
                }
                return lines.ToString();
            }
            return "";
        }
        return Find(doc.RootElement,0);
    }
}
