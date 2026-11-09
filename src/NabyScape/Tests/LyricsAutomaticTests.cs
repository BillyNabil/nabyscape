using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal static class LyricsAutomaticTests
{
    private static void Assert(bool value){if(!value)throw new Exception("Automatic lyrics assertion failed.");}
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body)};
    private static string Hits(string title,string artist,double duration=180)=>JsonSerializer.Serialize(new[]{new{trackName=title,artistName=artist,duration,syncedLyrics="[00:01]The little paper moon is waiting by the window."}});
    private sealed class Fake(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests=new();
        internal int Active,Maximum;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests.Enqueue(request.RequestUri!.AbsoluteUri);int active=Interlocked.Increment(ref Active);
            int observed;do{observed=Maximum;if(observed>=active)break;}while(Interlocked.CompareExchange(ref Maximum,active,observed)!=observed);
            try{return await action(request,token);}finally{Interlocked.Decrement(ref Active);}
        }
    }
    internal static void Run(Action<string,Action> test)
    {
        test("Automatic language filtering accepts English, Japanese and Korean and rejects the Spanish regression",()=>
        {
            const string spanish="[00:01]Es que, intentando olvidarlas, rememoras todo\n[00:05]Y le doy vueltas, y vueltas, y vueltas y no\n[00:10]Siento que el tiempo se ha parado y espero";
            Assert(LyricsLanguage.Detect(spanish)=="spa" && !LyricsLanguage.Allowed(spanish));
            Assert(LyricsLanguage.Allowed("[00:01]The little paper moon is waiting by the window."));
            Assert(LyricsLanguage.Detect("[00:01]君の声が聞こえる\n[00:04]今日はいい天気です")=="jpn");
            Assert(LyricsLanguage.Detect("[00:01]오늘도 너의 목소리를 듣고 있어요")=="kor");
            Assert(!LyricsLanguage.Allowed("[00:01]Je marche dans la rue et je regarde les lumières de la ville."));
            Assert(!LyricsLanguage.Allowed("[00:01]Я смотрю на огни города вечером") && !LyricsLanguage.Allowed("[00:01]أنا أنظر إلى أضواء المدينة"));
        });
        test("Identical song metadata cannot make a Spanish translation win over Japanese lyrics",()=>
        {
            var spanish=new LyricChoice("Nanairo Symphony - TV Size","COALAMODE.","",92,"[00:01]Es que, intentando olvidarlas, rememoras todo\n[00:05]Y le doy vueltas, y vueltas, y vueltas y no\n[00:10]Siento que el tiempo se ha parado y espero");
            var japanese=spanish with{Lrc="[00:01]君の声が聞こえる\n[00:05]今日はいい天気です"};
            Assert(LyricsMetadata.Best(new[]{spanish,japanese},new("Nanairo Symphony - TV Size","COALAMODE."),92,true)==japanese);
            Assert(LyricsMetadata.Best(new[]{spanish},new("Nanairo Symphony - TV Size","COALAMODE."),92,true)==null);
        });
                test("Automatic accepts Russian lyrics from LRCLIB",()=>Task.Run(async()=>
        {
            var fake=new Fake(async(request,token)=>
            {
                if(request.RequestUri!.Host=="lrclib.net")
                    return Json(JsonSerializer.Serialize(new[]{new{
                        trackName="Song",artistName="Artist",duration=180,
                        syncedLyrics="[00:01]Я смотрю на огни города вечером"
                    }}));
                if(request.RequestUri.Host=="lyrics-api.binimum.org")
                {
                    await Task.Delay(50,token);
                    return Json("{\"results\":[]}");
                }
                return Json("{}");
            });
            using var service=new LyricsService(fake,new AssistantMemoryKeys());
            await service.Load("Song","Artist",180,true,true);
            Assert(service.Result.Lines.Length==1 &&
                   service.Result.Lines[0].Text=="Я смотрю на огни города вечером" &&
                   service.Result.Status.Contains("LRCLIB",StringComparison.Ordinal));
        }).GetAwaiter().GetResult());
        test("Automatic public-provider access failures produce an empty result without escaping the fallback",()=>Task.Run(async()=>
        {
            var fake=new Fake((request,token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
            using var service=new LyricsService(fake,new AssistantMemoryKeys());await service.Load("Song","Artist",180,true,true);
            Assert(service.Result.Lines.Length==0 &&
                service.Result.Status==Localization.Text("No matching synced lyrics. Try another source or LRC."));
        }).GetAwaiter().GetResult());
        test("Browser lyric queries remove presentation labels but preserve real song versions",()=>
        {
            var query=LyricsMetadata.Queries("Artist - Song [Official Music Video] (Lyrics)","Artist - Topic")[0];Assert(query==new LyricsQuery("Song","Artist"));
            Assert(LyricsMetadata.Queries("Song (Live) [HD]","ArtistVEVO")[0]==new LyricsQuery("Song (Live)","Artist"));
            Assert(LyricsMetadata.CleanTitle("Song (Remix) (Acoustic)")=="Song (Remix) (Acoustic)" && LyricsMetadata.Key("Beyoncé")==LyricsMetadata.Key("Beyonce"));
            Assert(LyricsMetadata.Queries("歌手 - 歌曲 (Official Audio)","")[0]==new LyricsQuery("歌曲","歌手"));
            Assert(LyricsMetadata.Queries("Unidentified video","").Length==0);
        });
        test("Automatic lyrics reject wrong artists, versions and durations and rank the closest edition",()=>
        {
            LyricChoice Choice(string title,string artist,double duration)=>new(title,artist,"",duration,"[00:01]Fixture");
            var choices=new[]{Choice("Song","Other",180),Choice("Song (Live)","Artist",180),Choice("Song","Artist",210),Choice("Song","Artist",184),Choice("Song","Artist",180)};
            Assert(LyricsMetadata.Best(choices,new("Song","Artist"),180)==choices[4]);
            Assert(LyricsMetadata.Best(choices.Take(3),new("Song","Artist"),180)==null);
        });
        test("Automatic races bounded public sources, falls back after failures and cancels the loser",()=>Task.Run(async()=>
        {
            var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake=new Fake(async(request,token)=>
            {
                switch(request.RequestUri!.Host)
                {
                    case "lrclib.net":return Json("[]");
                    case "lyrics-api.binimum.org":return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    case "lyrics-api.boidu.dev":await Task.Delay(30,token);return Json("{\"lyrics\":\"[00:01]The little paper moon is waiting by the window.\"}");
                    default:try{await Task.Delay(5000,token);}catch(OperationCanceledException){cancelled.TrySetResult();throw;}return Json("{}");
                }
            });
            using var service=new LyricsService(fake,new AssistantMemoryKeys());await service.Load("Song (Official Video)","Artist - Topic",180,true,true);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert(service.Result.Lines.Length==1 && service.Result.Status.Contains("BetterLyrics") && fake.Maximum<=2);
            Assert(fake.Requests.All(r=>!r.Contains("youtube",StringComparison.OrdinalIgnoreCase) && !r.Contains("apikey",StringComparison.OrdinalIgnoreCase)));
        }).GetAwaiter().GetResult());
        test("Automatic uses cleaned browser metadata and reuses successful lyrics after dock collapse",()=>Task.Run(async()=>
        {
            var fake=new Fake((request,token)=>Task.FromResult(Json(request.RequestUri!.Host=="lrclib.net"?Hits("Song","Artist"):"{\"results\":[]}")));
            using var service=new LyricsService(fake,new AssistantMemoryKeys());var owner=new object();service.Acquire(owner);
            await service.Load("Artist - Song [Official Video]","Artist - Topic",180,true,true);Assert(service.Result.Lines.Length==1);
            Assert(fake.Requests.Any(r=>r.Contains("track_name=Song&artist_name=Artist",StringComparison.Ordinal)));
            int calls=fake.Requests.Count;service.Release(owner);Assert(service.Result.Lines.Length==0);
            service.Acquire(owner);await service.Load("Artist - Song [Official Video]","Artist - Topic",180,true,true);Assert(service.Result.Lines.Length==1 && fake.Requests.Count==calls);
            service.Retry();await service.Load("Artist - Song [Official Video]","Artist - Topic",180,true,true);Assert(fake.Requests.Count>calls);
        }).GetAwaiter().GetResult());
        test("Automatic metadata lookup respects consent and extracts missing artist only from explicit title structure",()=>Task.Run(async()=>
        {
            var fake=new Fake((request,token)=>Task.FromResult(Json(request.RequestUri!.Host=="lrclib.net"?Hits("Song","Artist"):"{\"results\":[]}")));
            using var service=new LyricsService(fake,new AssistantMemoryKeys());await service.Load("Artist - Song","",180,false,true);Assert(fake.Requests.Count==0);
            await service.Load("Artist - Song","",180,true,true);Assert(service.Result.Lines.Length==1);
            service.Retry();await service.Load("Artist - Song","chrome.exe",180,true,true,"chrome.exe");Assert(service.Result.Lines.Length==1);
            Assert(fake.Requests.All(r=>!r.Contains("chrome.exe",StringComparison.OrdinalIgnoreCase)));
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();int calls=fake.Requests.Count;bool rejected=false;
            try{await service.FindAutomatic("Song","Artist",180,cancelled.Token);}catch(OperationCanceledException){rejected=true;}
            Assert(rejected && fake.Requests.Count==calls);
        }).GetAwaiter().GetResult());
        test("A delayed automatic reply cannot overwrite a newer track",()=>Task.Run(async()=>
        {
            var oldResponse=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake=new Fake((request,token)=>request.RequestUri!.Host!="lrclib.net"?Task.FromResult(Json("{\"results\":[]}")):request.RequestUri.Query.Contains("Old",StringComparison.Ordinal)?oldResponse.Task:Task.FromResult(Json(Hits("New","Artist"))));
            using var service=new LyricsService(fake,new AssistantMemoryKeys());var old=service.Load("Old","Artist",180,true,true);
            await service.Load("New","Artist",180,true,true);var current=service.Result;Assert(current.Lines.Length==1);
            oldResponse.SetResult(Json(Hits("Old","Artist")));await old;Assert(ReferenceEquals(current,service.Result));
        }).GetAwaiter().GetResult());
    }
}
