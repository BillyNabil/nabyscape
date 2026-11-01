using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal sealed partial class LyricsService
{
    private static IEnumerable<JsonElement> ObjectsWith(JsonElement node,string key,int depth=0)
    {
        if(depth>40)yield break;
        if(node.ValueKind==JsonValueKind.Object)
        {
            if(node.TryGetProperty(key,out var value))yield return value;
            foreach(var child in node.EnumerateObject())foreach(var found in ObjectsWith(child.Value,key,depth+1))yield return found;
        }
        else if(node.ValueKind==JsonValueKind.Array)foreach(var child in node.EnumerateArray())foreach(var found in ObjectsWith(child,key,depth+1))yield return found;
    }
    private static string YouTubeText(JsonElement node)
    {
        if(node.ValueKind==JsonValueKind.String)return node.GetString()??"";
        string simple=Text(node,"simpleText");if(simple.Length>0)return simple;
        if(node.ValueKind==JsonValueKind.Object && node.TryGetProperty("runs",out var runs) && runs.ValueKind==JsonValueKind.Array)return string.Concat(runs.EnumerateArray().Select(r=>Text(r,"text")));
        return "";
    }
    private async Task<string> YouTubeBody(HttpRequestMessage request,CancellationToken token)
    {
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=await input.ReadAsync(buffer,token))>0){if(output.Length+n>2*1024*1024)throw new InvalidDataException("YouTube response is too large.");output.Write(buffer,0,n);}
        return Encoding.UTF8.GetString(output.ToArray());
    }
    private async Task<LyricChoice[]> FetchYouTube(string source,string video,string title,string artist,double duration,CancellationToken token)
    {
        bool music=source=="YouTube Music";string origin=music?"https://music.youtube.com":"https://www.youtube.com";
        using var homeRequest=new HttpRequestMessage(HttpMethod.Get,origin+"/");string home=await YouTubeBody(homeRequest,token);
        var match=Regex.Match(home,"\"INNERTUBE_CLIENT_VERSION\"\\s*:\\s*\"([^\"]{1,80})\"",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(200));
        if(!match.Success)throw new InvalidDataException("YouTube client configuration is unavailable. This region or session may require account access.");
        var context=new {client=new {clientName=music?"WEB_REMIX":"WEB",clientVersion=match.Groups[1].Value,hl="en",gl="US"}};
        async Task<JsonDocument> Post(string method,object payload)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,origin+"/youtubei/v1/"+method+"?prettyPrint=false"){Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")};
            return JsonDocument.Parse(await YouTubeBody(request,token));
        }
        using var next=await Post("next",new{context,videoId=video});
        if(music)
        {
            JsonElement? endpoint=null;
            foreach(var tab in ObjectsWith(next.RootElement,"tabRenderer"))
            {
                if(YouTubeText(tab.TryGetProperty("title",out var label)?label:default).Equals("Lyrics",StringComparison.OrdinalIgnoreCase))
                {endpoint=ObjectsWith(tab,"browseEndpoint").Select(e=>(JsonElement?)e).FirstOrDefault();break;}
            }
            if(endpoint==null)return Array.Empty<LyricChoice>();
            string browseId=Text(endpoint.Value,"browseId"),parameters=Text(endpoint.Value,"params");if(browseId.Length==0)return Array.Empty<LyricChoice>();
            using var page=await Post("browse",new{context,browseId,@params=parameters});
            foreach(var shelf in ObjectsWith(page.RootElement,"musicDescriptionShelfRenderer"))
            {
                if(!shelf.TryGetProperty("description",out var description))continue;string plain=YouTubeText(description);
                if(plain.Length>0)return new[]{new LyricChoice(title,artist,"YouTube Music · plain lyrics",duration,"",plain)};
            }
            return Array.Empty<LyricChoice>();
        }
        string transcriptParams=ObjectsWith(next.RootElement,"getTranscriptEndpoint").Select(e=>Text(e,"params")).FirstOrDefault(p=>p.Length>0)??"";
        if(transcriptParams.Length==0)return Array.Empty<LyricChoice>();
        using var transcript=await Post("get_transcript",new{context,@params=transcriptParams});
        var lrc=new StringBuilder();
        foreach(var cue in ObjectsWith(transcript.RootElement,"transcriptCueRenderer").Take(5000))
        {
            if(!long.TryParse(Text(cue,"startOffsetMs"),NumberStyles.Integer,CultureInfo.InvariantCulture,out long ms) || ms<0 || ms>86400000 || !cue.TryGetProperty("cue",out var text))continue;
            long cs=ms/10;lrc.Append('[').Append((cs/6000).ToString("00",CultureInfo.InvariantCulture)).Append(':').Append(((cs%6000)/100d).ToString("00.00",CultureInfo.InvariantCulture)).Append(']').AppendLine(YouTubeText(text).Replace('\n',' '));
        }
        return lrc.Length==0?Array.Empty<LyricChoice>():new[]{new LyricChoice(title,artist,"YouTube captions",duration,lrc.ToString())};
    }
}
