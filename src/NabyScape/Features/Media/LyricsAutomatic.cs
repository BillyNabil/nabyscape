using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;

internal readonly record struct LyricsQuery(string Title,string Artist);
internal static class LyricsMetadata
{
    private const string Labels=@"(?:official\s+(?:music\s+)?(?:video|audio|visuali[sz]er)|music\s+video|lyric(?:s|\s+video)?|visuali[sz]er|hd|4k)";
    private static string Replace(string text,string pattern,string replacement)=>Regex.Replace(text,pattern,replacement,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static string CleanTitle(string title)
    {
        string value=Replace(title,@"\s*[\[(]\s*"+Labels+@"\s*[\])]","");
        value=Replace(value,@"\s+[-–—|]\s*"+Labels+@"\s*$","");
        return Replace(value,@"\s+"," ").Trim();
    }
    internal static string CleanArtist(string artist)=>Replace(Replace(artist,@"\s*[-–—]\s*Topic\s*$",""),@"VEVO\s*$","").Trim();
    internal static LyricsQuery[] Queries(string title,string artist)
    {
        title=CleanTitle(title);artist=CleanArtist(artist);
        var queries=new List<LyricsQuery>();
        var split=Regex.Match(title,@"^(.{1,150}?)\s+[-–—]\s+(.{1,250})$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(split.Success && (artist.Length==0 || Key(split.Groups[1].Value)==Key(artist)))queries.Add(new(CleanTitle(split.Groups[2].Value),CleanArtist(split.Groups[1].Value)));
        if(title.Length>0 && artist.Length>0)queries.Add(new(title,artist));
        if(split.Success)queries.Add(new(CleanTitle(split.Groups[2].Value),CleanArtist(split.Groups[1].Value)));
        return queries.Where(q=>q.Title.Length>0 && q.Artist.Length>0).Distinct().Take(2).ToArray();
    }
    internal static string Key(string value)
    {
        var result=new StringBuilder();
        foreach(char c in value.Normalize(NormalizationForm.FormD))if(char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }
    internal static LyricChoice? Best(IEnumerable<LyricChoice> choices,LyricsQuery query,double duration,bool checkLanguage=false)
    {
        return choices.Where(c=>Key(CleanTitle(c.Title))==Key(query.Title) && Key(CleanArtist(c.Artist))==Key(query.Artist))
            .Where(c=>c.Duration<=0 || duration<=0 || Math.Abs(c.Duration-duration)<=Math.Clamp(duration*.04,5,12))
            .Where(c=>LyricTiming.Parse(c.Lrc).Length>0)
            .OrderBy(c=>c.Duration>0 && duration>0?Math.Abs(c.Duration-duration):double.MaxValue)
            .FirstOrDefault(c=>!checkLanguage || LyricsLanguage.Allowed(c.Lrc));
    }
}

internal sealed partial class LyricsService
{
    internal async Task<(LyricChoice Choice,string Source)?> FindAutomatic(string title,string artist,double duration,CancellationToken token)
    {
        var queries=LyricsMetadata.Queries(title,artist);if(queries.Length==0)return null;
        using var total=CancellationTokenSource.CreateLinkedTokenSource(token);total.CancelAfter(TimeSpan.FromSeconds(8));
        async Task<(LyricChoice Choice,string Source)?> Attempt(string source,CancellationToken race)
        {
            using var attempt=CancellationTokenSource.CreateLinkedTokenSource(race);attempt.CancelAfter(TimeSpan.FromMilliseconds(3500));
            try
            {
                foreach(var query in queries)
                {
                    var choices=source=="LRCLIB"?await Search(query.Title,query.Artist,attempt.Token):source=="BiniLyrics"?await SearchBini(query.Title,query.Artist,attempt.Token,2):await FetchProvider(source,query.Title,query.Artist,duration,attempt.Token);
                    var selected=await Task.Run(()=>LyricsMetadata.Best(choices,query,duration,false),attempt.Token);
                    if(selected!=null)return (selected,source);
                }
            }
            catch(Exception ex) when(ex is OperationCanceledException or HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException or System.Xml.XmlException or FormatException or InvalidOperationException or RegexMatchTimeoutException){}
            return null;
        }
        foreach(var sources in new[]{new[]{"LRCLIB","BiniLyrics"},new[]{"BetterLyrics","LyricsPlus"}})
        {
            token.ThrowIfCancellationRequested();if(total.IsCancellationRequested)break;
            using var race=CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            var tasks=sources.Select(s=>Attempt(s,race.Token)).ToList();
            while(tasks.Count>0)
            {
                var completed=await Task.WhenAny(tasks);tasks.Remove(completed);var result=await completed;
                if(result!=null){race.Cancel();return result;}
            }
        }
        token.ThrowIfCancellationRequested();return null;
    }
}
