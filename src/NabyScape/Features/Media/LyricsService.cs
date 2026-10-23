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
internal sealed record LyricResult(LyricLine[] Lines,string Status);
internal static class LyricTiming
{
    private static readonly Regex stamps=new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static LyricLine[] Parse(string source)
    {
        if(source.Length>512*1024)throw new InvalidDataException("Lyrics are too large.");
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
    internal static bool CanUse(MediaService media)=>media.Available && !string.IsNullOrWhiteSpace(media.Title) && !string.IsNullOrWhiteSpace(media.Artist) && media.Artist!=media.Selected?.SourceAppUserModelId && media.Timeline().duration>0;
    internal static bool IsSpotify(string? source)=>source?.Contains("spotify",StringComparison.OrdinalIgnoreCase)==true;
}

// Explicitly enabled external metadata lookup. No cookies, provider keys, profile
// files or screen contents are read. Memory-only, bounded per-session cache.
internal sealed class LyricsService : IDisposable
{
    private readonly HttpClient client;
    private readonly Dictionary<string,LyricResult> cache=new();
    private CancellationTokenSource? pending;
    private string key="";
    private bool disposed;
    private readonly HashSet<object> consumers=new();
    internal void Acquire(object owner)=>consumers.Add(owner);
    internal void Release(object owner)
    {
        if(!consumers.Remove(owner) || consumers.Count>0)return;
        Cancel();key="";cache.Clear();Set(new(Array.Empty<LyricLine>(),"Lyrics are off."));
    }
    internal LyricResult Result {get;private set;}=new(Array.Empty<LyricLine>(),"Lyrics are off.");
    internal event Action? Changed;
    private static string TrackKey(string title,string artist,double duration)=>title+"\n"+artist+"\n"+Math.Round(duration);
    internal LyricsService(HttpMessageHandler? handler=null)
    {client=new(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(8)};client.DefaultRequestHeaders.UserAgent.ParseAdd("NabyScape/2.7.0 (+https://github.com/BillyNabil/nabyscape)");}
    internal async Task Load(string title,string artist,double duration,bool enabled,bool compatible)
    {
        if(disposed)return;
        if(!enabled || !compatible){Cancel();key="";Set(new(Array.Empty<LyricLine>(),enabled?"Choose a music session with a title, artist and playback timeline.":"Lyrics are off."));return;}
        string next=TrackKey(title,artist,duration);if(next==key)return;Cancel();key=next;
        if(cache.TryGetValue(next,out var found)){Set(found);return;}
        var request=new CancellationTokenSource();pending=request;Set(new(Array.Empty<LyricLine>(),"Finding synced lyrics…"));
        try
        {
            if(string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))throw new InvalidDataException("Track details are unavailable.");
            string query="https://lrclib.net/api/get?track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist);
            if(duration>=1 && duration<=3600)query+="&duration="+Math.Round(duration).ToString(CultureInfo.InvariantCulture);
            using var response=await client.GetAsync(query,HttpCompletionOption.ResponseHeadersRead,request.Token);
            LyricResult result;
            if(response.StatusCode==HttpStatusCode.NotFound)result=new(Array.Empty<LyricLine>(),"No synced lyrics for this track.");
            else
            {
                response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1024*1024)throw new InvalidDataException("Lyrics response is too large.");
                await using var body=await response.Content.ReadAsStreamAsync(request.Token);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
                while((count=await body.ReadAsync(chunk,request.Token))>0){if(buffer.Length+count>1024*1024)throw new InvalidDataException("Lyrics response is too large.");buffer.Write(chunk,0,count);}
                using var json=JsonDocument.Parse(buffer.ToArray());var data=json.RootElement;
                bool instrumental=data.TryGetProperty("instrumental",out var ins) && ins.ValueKind==JsonValueKind.True;
                string? lrc=data.TryGetProperty("syncedLyrics",out var value) && value.ValueKind==JsonValueKind.String?value.GetString():null;
                var lines=lrc==null?Array.Empty<LyricLine>():LyricTiming.Parse(lrc);
                result=new(lines,lines.Length>0?"Synced lyrics · LRCLIB":instrumental?"Instrumental track":"No synced lyrics for this track.");
            }
            if(request.IsCancellationRequested || disposed || key!=next)return;
            if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[next]=result;Set(result);
        }
        catch(OperationCanceledException){if(!request.IsCancellationRequested && !disposed && key==next)Set(new(Array.Empty<LyricLine>(),"Lyrics request timed out. Tap Retry."));}
        catch(Exception ex) when(ex is HttpRequestException or IOException or JsonException or RegexMatchTimeoutException)
        {if(!request.IsCancellationRequested && !disposed && key==next)Set(new(Array.Empty<LyricLine>(),"Lyrics unavailable. Tap Retry."));}
        finally{if(ReferenceEquals(pending,request))pending=null;request.Dispose();}
    }
    private void Set(LyricResult result){if(Result==result)return;Result=result;Changed?.Invoke();}
    internal void Import(string title,string artist,double duration,string lrc)
    {
        var lines=LyricTiming.Parse(lrc);if(lines.Length==0)throw new InvalidDataException("Choose an LRC file with [mm:ss] line timestamps.");
        Cancel();key=TrackKey(title,artist,duration);var result=new LyricResult(lines,"Local LRC · this session");if(cache.Count>=20)cache.Remove(cache.Keys.First());cache[key]=result;Set(result);
    }
    internal void Retry(){cache.Remove(key);key="";}
    internal void Cancel(){if(pending!=null){pending.Cancel();pending=null;key="";}}
    public void Dispose(){disposed=true;Cancel();client.Dispose();cache.Clear();}
}
