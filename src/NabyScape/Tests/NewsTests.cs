using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal static class NewsTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now=DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class Pending : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Started.TrySetResult();await Task.Delay(Timeout.Infinite,token);throw new InvalidOperationException();}
    }
    private sealed class Network : HttpMessageHandler
    {
        internal int Calls;
        internal string Path="";
        internal string SourcesBody="{\"sources\":[]}";
        internal HttpStatusCode SourcesStatus=HttpStatusCode.OK;
        internal int SourceCalls;
        internal string SourceQuery="";
        internal HttpStatusCode Status=HttpStatusCode.OK;
        internal string Type="application/json";
        internal string Body="{\"title\":\"Главное\",\"items\":[{\"postId\":\"123\",\"head\":\"Новость\",\"notes\":[\"Полная сноска\"]}]}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Headers.Authorization!=null || request.Headers.Contains("Cookie"))throw new Exception("News requests must be anonymous");
            Calls++;Path=request.RequestUri!.AbsolutePath;bool sources=Path.EndsWith("/sources",StringComparison.Ordinal);if(sources){SourceCalls++;SourceQuery=request.RequestUri.Query;}
            return Task.FromResult(new HttpResponseMessage(sources?SourcesStatus:Status){Content=new StringContent(sources?SourcesBody:Body,Encoding.UTF8,Type)});
        }
    }
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool pass){if(!pass)throw new Exception("News invariant failed");}
        test("News accepts HTTPS and loopback, rejects credentials and unsafe addresses",()=>
        {
            Assert(NewsService.BaseUri("https://example.org/api").AbsoluteUri=="https://example.org/api/");
            Assert(NewsService.BaseUri("http://127.0.0.1:8766/api").IsLoopback);
            foreach(string input in new[]{"file:///tmp","http://example.org/api","https://user:secret@example.org","https://example.org?key=secret","javascript:alert(1)",""})
            {bool rejected=false;try{NewsService.BaseUri(input);}catch(ArgumentException){rejected=true;}Assert(rejected);}
            Assert(NewsService.SafeLink("file:///C:/secret")==null && NewsService.SafeLink("javascript:alert(1)")==null);
            Assert(NewsService.CoverUri("https://example.org/api","/media/a.jpg")!.AbsoluteUri=="https://example.org/media/a.jpg");
            Assert(NewsService.CoverUri("https://example.org/api","https://other.org/track.jpg")==null);
            Assert(NewsService.CoverUri("https://example.org/api","file:///secret")==null);
            Assert(NewsService.AppLink.Host=="t.me" && NewsService.AppLink.Query=="?startapp");
        });
        test("News preserves full legal notes, deduplicates stories and limits digest",()=>
        {
            string item="{\"postId\":123,\"head\":\"Новость\",\"notes\":[\"Не обрезать эту сноску\"]}";
            var parsed=NewsService.Parse(Encoding.UTF8.GetBytes("{\"items\":["+item+","+item+"]}"),DateTimeOffset.UtcNow);
            Assert(parsed.Items.Length==1 && parsed.Items[0].Notes[0]=="Не обрезать эту сноску" && parsed.Items[0].Url!.Query=="?startapp=post_123");
            bool rejected=false;try{NewsService.Parse(Encoding.UTF8.GetBytes("{\"error\":\"down\"}"),DateTimeOffset.UtcNow);}catch(InvalidDataException){rejected=true;}Assert(rejected);
        });
        test("News caches each API separately and never sends credentials",()=>Task.Run(async ()=>
        {
            var network=new Network();using var service=new NewsService(network);
            var first=await service.Get("https://example.org/api",false,CancellationToken.None);
            var second=await service.Get("https://example.org/api",false,CancellationToken.None);
            Assert(network.Calls==1 && ReferenceEquals(first,second) && network.Path=="/api/v2/digest");
            network.Body="{\"items\":[]}";await service.Get("https://example.org/other-api",false,CancellationToken.None);
            Assert(network.Calls==2 && network.Path=="/other-api/v2/digest");
            Assert(service.Saved("https://another.org/api")==null);
        }).GetAwaiter().GetResult());
        test("News retains cached stories after a failed refresh and limits retries",()=>Task.Run(async ()=>
        {
            var clock=new Clock();var network=new Network();using var service=new NewsService(network,clock);
            var first=await service.Get("https://example.org/api",false,CancellationToken.None);
            clock.Now+=TimeSpan.FromMinutes(6);network.Status=HttpStatusCode.ServiceUnavailable;bool failed=false;
            try{await service.Get("https://example.org/api",true,CancellationToken.None);}catch(HttpRequestException e){failed=e.StatusCode==HttpStatusCode.ServiceUnavailable;}
            Assert(failed && ReferenceEquals(first,service.Saved("https://example.org/api")));
            int calls=network.Calls;Assert(ReferenceEquals(first,await service.Get("https://example.org/api",true,CancellationToken.None)) && network.Calls==calls);
        }).GetAwaiter().GetResult());
        test("News cancels a pending request without caching incomplete data",()=>Task.Run(async ()=>
        {
            using var pending=new Pending();using var service=new NewsService(pending);using var cancel=new CancellationTokenSource();
            var request=service.Get("https://example.org/api",false,cancel.Token);await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();bool cancelled=false;
            try{await request;}catch(OperationCanceledException){cancelled=true;}Assert(cancelled && service.Saved("https://example.org/api")==null);
        }).GetAwaiter().GetResult());
        test("News source requests are bounded to twenty IDs in a single batch",()=>Task.Run(async ()=>
        {
            var network=new Network{Body=System.Text.Json.JsonSerializer.Serialize(new{items=Enumerable.Range(1,5).Select(i=>new{postId=i,head="Story "+i,srcs=Enumerable.Range(i*100,30).ToArray()})})};using var service=new NewsService(network);
            await service.Get("https://example.org/api",false,CancellationToken.None);
            Assert(network.SourceCalls==1 && Uri.UnescapeDataString(network.SourceQuery)[5..].Split(',').Length==20);
        }).GetAwaiter().GetResult());
        test("News module is opt-in and survives settings normalization",()=>
        {var settings=new Settings();settings.Normalize();Assert(!settings.Widgets.Find(w=>w.Id=="news")!.Enabled && !settings.NewsConnected);settings.Widgets.Find(w=>w.Id=="news")!.Enabled=true;settings.Normalize();Assert(settings.Widgets.Find(w=>w.Id=="news")!.Enabled);});
        test("News cover stays on its API origin and rejects oversized image responses",()=>Task.Run(async ()=>
        {
            var network=new Network{Type="image/jpeg",Body=new string('x',2*1024*1024+1)};using var service=new NewsService(network);
            Assert(await service.Cover("https://example.org/api","https://other.org/a.jpg",CancellationToken.None)==null && network.Calls==0);
            Assert(await service.Cover("https://example.org/api","/a.jpg",CancellationToken.None)==null);
            network.Body="small-image-fixture";var first=await service.Cover("https://example.org/api","/b.jpg",CancellationToken.None);int calls=network.Calls;
            Assert(first!=null && ReferenceEquals(first,await service.Cover("https://example.org/api","/b.jpg",CancellationToken.None)) && network.Calls==calls);
        }).GetAwaiter().GetResult());
        test("News resolves source avatars anonymously through the API without a local catalog",()=>Task.Run(async ()=>
        {
            var network=new Network{Body="{\"items\":[{\"postId\":\"123\",\"head\":\"Новость\",\"srcs\":[-1,\"missing\"]}]}",SourcesBody="{\"sources\":[{\"id\":\"-1\",\"name\":\"Редакция\",\"avatar\":\"/media/avatars/source.png\"}]}"};using var service=new NewsService(network);
            var snapshot=await service.Get("https://example.org/api",false,CancellationToken.None);
            var sources=service.SourcesFor(snapshot.Items[0],"https://example.org/api");Assert(sources.Length==1 && sources[0].Name=="Редакция" && sources[0].Avatar=="/media/avatars/source.png");
            Assert(network.SourceCalls==1 && network.SourceQuery=="?ids=-1" && service.SourcesFor(snapshot.Items[0],"https://another.org/api").Length==0);
            await service.Get("https://example.org/api",false,CancellationToken.None);Assert(network.SourceCalls==1);
        }).GetAwaiter().GetResult());
        test("News source API failures leave the summary readable",()=>Task.Run(async ()=>
        {
            foreach(var body in new[]{"{\"sources\":[]}","{\"wrong\":true}",new string('x',256*1024+1)})
            {
                var network=new Network{Body="{\"items\":[{\"postId\":\"123\",\"head\":\"Новость\",\"srcs\":[1]}]}",SourcesBody=body};using var service=new NewsService(network);
                var snapshot=await service.Get("https://example.org/api",false,CancellationToken.None);Assert(snapshot.Items[0].Title=="Новость" && snapshot.Items[0].Sources.Length==0);
            }
            var unavailable=new Network{Body="{\"items\":[{\"postId\":\"1\",\"head\":\"Новость\",\"srcs\":[1]}]}",SourcesStatus=HttpStatusCode.NotFound};using var fallback=new NewsService(unavailable);
            Assert((await fallback.Get("https://example.org/api",false,CancellationToken.None)).Items.Length==1);
        }).GetAwaiter().GetResult());
        test("News ignores unrequested source metadata and third-party avatar URLs",()=>Task.Run(async ()=>
        {
            var network=new Network{Body="{\"items\":[{\"postId\":\"1\",\"head\":\"Новость\",\"srcs\":[1]}]}",SourcesBody="{\"sources\":[{\"id\":\"2\",\"name\":\"Other\",\"avatar\":\"/a.png\"},{\"id\":\"1\",\"name\":\"Source\",\"avatar\":\"https://other.org/track.png\"}]}"};using var service=new NewsService(network);
            var snapshot=await service.Get("https://example.org/api",false,CancellationToken.None);Assert(snapshot.Items[0].Sources.Length==1 && snapshot.Items[0].Sources[0].Avatar=="");
            await service.Get("https://example.org/api",false,CancellationToken.None);Assert(network.SourceCalls==1);
            network.Body="{\"items\":[{\"postId\":\"3\",\"head\":\"Сводка\",\"srcs\":[1]}]}";
            await service.Get("https://example.org/other-api",false,CancellationToken.None);Assert(network.SourceCalls==2);
        }).GetAwaiter().GetResult());
    }
}
