using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal static class LyricsProviderTests
{
    private static void Assert(bool value){if(!value)throw new Exception("Lyrics provider assertion failed.");}
    private sealed class RedirectFake(string destination) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;Assert(request.Headers.Authorization==null);
            if(Calls==1){var response=new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);response.Headers.Location=new Uri(destination);return Task.FromResult(response);}
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"results\":[]}")});
        }
    }
    private sealed class Fake : HttpMessageHandler
    {
        internal int Calls;internal bool Authenticated;internal bool MissingSecondPax;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;Authenticated|=request.Headers.Authorization?.Parameter=="fixture-key";
            string host=request.RequestUri!.Host,path=request.RequestUri.AbsolutePath;
            string json="{}";
            if(host=="lyrics-api.binimum.org")json="{\"results\":[{\"track_name\":\"Fixture\",\"artist_name\":\"Artist\",\"lyricsUrl\":\"https://lrc.red/s/US123.ttml\"}]}";
            if(host=="lyrics-api.boidu.dev")json="{\"ttml\":\"<tt><p begin='1s'>Fixture</p></tt>\"}";
            if(host=="unison.boidu.dev")json="{\"success\":true,\"data\":{\"lyrics\":\"[00:01]Fixture\"}}";
            if(host=="lyricsplus.binimum.org")json="{\"lyrics\":[{\"time\":1500,\"text\":\"Fixture\"}]}";
            if(host=="lrc.red")json=path=="/search.json"?"{\"hits\":[{\"isrc\":\"US123\",\"title\":\"Fixture\",\"artist\":\"Artist\",\"album\":\"Album\"}]}":"<tt><p begin='1s'>Fixture</p></tt>";
            if(host=="lyrics.kugou.com")json=path=="/search"?"{\"candidates\":[{\"id\":123,\"accesskey\":\"fixture\",\"song\":\"Fixture\",\"singer\":\"Artist\"}]}":"{\"content\":\""+Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:01]Fixture"))+"\"}";
            if(host=="itunes.apple.com")json=MissingSecondPax?"{\"results\":[{\"trackId\":123},{\"trackId\":456}]}":"{\"results\":[{\"trackId\":123,\"trackName\":\"Fixture\",\"artistName\":\"Artist\",\"collectionName\":\"Album\"}]}";
            if(host=="lyrics.paxsenix.org" && MissingSecondPax && request.RequestUri.Query.Contains("456",StringComparison.Ordinal))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if(host=="lyrics.paxsenix.org")json="{\"ttmlContent\":\"<tt><p begin='1s'>Fixture</p></tt>\"}";
            if(host=="api.paxsenix.org")json=path=="/spotify/search"?"{\"data\":[{\"id\":\"0123456789012345678901\"}]}":"{\"data\":{\"lyrics\":\"[00:01]Fixture\"}}";
            if(host=="api.musixmatch.com")json="{\"message\":{\"header\":{\"status_code\":200},\"body\":{\"lyrics\":{\"lyrics_body\":\"Original fixture text\",\"lyrics_copyright\":\"Fixture credit\"}}}}";
            if(host.EndsWith("youtube.com",StringComparison.Ordinal))
            {
                if(path=="/")json="{\"INNERTUBE_CLIENT_VERSION\":\"fixture-version\"}";
                else if(path.EndsWith("/next",StringComparison.Ordinal))json="{\"tabs\":[{\"tabRenderer\":{\"title\":\"Lyrics\",\"browseEndpoint\":{\"browseId\":\"fixture\"}}}],\"getTranscriptEndpoint\":{\"params\":\"fixture\"}}";
                else if(path.EndsWith("/browse",StringComparison.Ordinal))json="{\"musicDescriptionShelfRenderer\":{\"description\":{\"runs\":[{\"text\":\"Original fixture text\"}]}}}";
                else json="{\"transcriptCueRenderer\":{\"startOffsetMs\":\"1250\",\"cue\":{\"simpleText\":\"Original caption\"}}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
    internal static void Run(Action<string,Action> test)
    {
        test("Bini follows only bounded trusted HTTPS redirects without credentials",()=>Task.Run(async()=>
        {
            var allowed=new RedirectFake("https://lrc.red/api/v1");using(var service=new LyricsService(allowed,new AssistantMemoryKeys()))Assert((await service.SearchBini("Fixture","Artist",CancellationToken.None)).Length==0 && allowed.Calls==2);
            foreach(string url in new[]{"http://lrc.red/api/v1","https://foreign.example/api","https://lrc.red:444/api"})
            {
                var blocked=new RedirectFake(url);using var service=new LyricsService(blocked,new AssistantMemoryKeys());bool rejected=false;
                try{await service.SearchBini("Fixture","Artist",CancellationToken.None);}catch(System.IO.InvalidDataException){rejected=true;}
                Assert(rejected && blocked.Calls==1);
            }
        }).GetAwaiter().GetResult());
        test("QQ lyrics preserve real line timestamps and remove word durations",()=>
        {
            string lrc=TimedLyricsDocument.ToLrc("<QrcInfos><LyricInfo><Lyric_1 LyricContent='[1000,1000]Hello(1000,200) world(1200,500) [3000,500]Again(3000,500)'/></LyricInfo></QrcInfos>");
            var lines=LyricTiming.Parse(lrc);Assert(lines.Length==2 && lines[0].At==1 && lines[0].Text=="Hello world" && lines[1].At==3);
        });
        test("All fourteen requested lyrics sources have unique identities",()=>Assert(LyricsProviders.Names.Length==14 && LyricsProviders.Names.Distinct().Count()==14));
        test("A failed PaxSenix catalogue edition preserves other successful matches",()=>Task.Run(async()=>
        {
            using var service=new LyricsService(new Fake{MissingSecondPax=true},new AssistantMemoryKeys());Assert((await service.FetchProvider("PaxSenix","Fixture","Artist",180,CancellationToken.None)).Length==1);
        }).GetAwaiter().GetResult());
        foreach(string source in new[]{"BiniLyrics","lrc.red","BetterLyrics","BetterLyrics Potato","Unison","LyricsPlus","KuGou","PaxSenix"})
        {
            string selected=source;
            test("Provider adapter routes and parses "+selected,()=>Task.Run(async()=>{using var service=new LyricsService(new Fake(),new AssistantMemoryKeys());var results=await service.FetchProvider(selected,"Fixture","Artist",180,CancellationToken.None);Assert(results.Length==1 && LyricTiming.Parse(results[0].Lrc).Length>0);}).GetAwaiter().GetResult());
        }
        test("PaxSenix credentials use only the authenticated API request",()=>Task.Run(async()=>
        {
            var keys=new AssistantMemoryKeys();keys.Write(LyricsProviders.PaxKey,"fixture-key");var fake=new Fake();using var service=new LyricsService(fake,keys);
            foreach(string source in new[]{"PaxSenix: Spotify","PaxSenix: Musixmatch"})Assert((await service.FetchProvider(source,"Fixture","Artist",180,CancellationToken.None)).Length==1);
            Assert(fake.Authenticated);
        }).GetAwaiter().GetResult());
        test("Musixmatch plain lyrics remain untimed and retain credit",()=>Task.Run(async()=>
        {
            var keys=new AssistantMemoryKeys();keys.Write(LyricsProviders.MusixmatchKey,"fixture-key");using var service=new LyricsService(new Fake(),keys);var result=(await service.FetchProvider("Musixmatch","Fixture","Artist",180,CancellationToken.None)).Single();Assert(result.Lrc=="" && result.Plain=="Original fixture text" && result.Album=="Fixture credit");
            service.ImportPlain("Fixture","Artist",180,result.Plain,"Fixture credit");Assert(service.Result.Lines.Length==0 && service.Result.Plain.Length>0);
        }).GetAwaiter().GetResult());
        test("YouTube video identity is tied to its track and real caption timestamps",()=>Task.Run(async()=>
        {
            using var service=new LyricsService(new Fake(),new AssistantMemoryKeys());service.ConfigureVideo("https://youtu.be/abcdefghijk","Fixture","Artist");Assert(service.HasVideo("Fixture","Artist") && !service.HasVideo("Other","Artist"));
            var captions=(await service.FetchProvider("YouTube captions","Fixture","Artist",180,CancellationToken.None)).Single();Assert(LyricTiming.Parse(captions.Lrc)[0].At==1.25);
            var music=(await service.FetchProvider("YouTube Music","Fixture","Artist",180,CancellationToken.None)).Single();Assert(music.Lrc=="" && music.Plain=="Original fixture text");
        }).GetAwaiter().GetResult());
        test("Provider document parser accepts timed text arrays without fabricating plain timing",()=>
        {
            var result=LyricsService.ProviderDocument("{\"content\":[{\"timestamp\":2500,\"text\":[{\"text\":\"Original\"},{\"text\":\"fixture\"}]}]}");Assert(LyricTiming.Parse(result)[0].Text=="Original fixture");Assert(LyricsService.ProviderDocument("{\"lyrics\":\"Untimed text\"}")=="");
        });
    }
}
