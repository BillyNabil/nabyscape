using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;

internal static class PremiumExperienceTests
{
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));}
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new Exception("Premium experience fixture failed.");}
        test("Premium preferences normalize nonfinite values and unknown modes",()=>
        {
            var s=new Settings{AmbientStrength=double.NaN,AmbientSpread=20,AmbientMotion="unknown",InteractionMotion="unknown",NotificationSeconds=double.PositiveInfinity,NotificationAnimation="unknown",QuietStart=-1,QuietEnd=30};s.Normalize();
            Assert(s.AmbientStrength==.5 && s.AmbientSpread==2.5 && s.AmbientMotion=="Drift" && s.InteractionMotion=="Subtle" && s.NotificationSeconds==12 && s.NotificationAnimation=="Lift" && s.QuietStart==0 && s.QuietEnd==23);
        });
        test("Quiet hours cover overnight boundaries without silencing the daytime",()=>
        {
            DateTimeOffset At(int hour)=>new(new DateTime(2026,10,21,hour,0,0),TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026,10,21,hour,0,0)));
            var s=new Settings{QuietHours=true,QuietStart=22,QuietEnd=7};Assert(NotificationPolicy.Quiet(s,At(22)) && NotificationPolicy.Quiet(s,At(0)) && NotificationPolicy.Quiet(s,At(6)) && !NotificationPolicy.Quiet(s,At(7)) && !NotificationPolicy.Quiet(s,At(21)));
            s.QuietStart=9;s.QuietEnd=17;Assert(NotificationPolicy.Quiet(s,At(9)) && !NotificationPolicy.Quiet(s,At(17)) && !NotificationPolicy.Quiet(s,At(22)));s.QuietStart=s.QuietEnd;Assert(!NotificationPolicy.Quiet(s,At(17)));
        });
        test("Snooze suppresses both routes and resumes without changing delivery preferences",()=>
        {
            var now=DateTimeOffset.UtcNow;var s=new Settings{NotificationDelivery="Both",NotificationsSnoozedUntil=now.AddMinutes(15)};var manager=new InternalNotifications();int windows=0;
            manager.Post(s,new("timer","Done","Fixture"),false,_=>windows++,now);Assert(manager.Count==0 && windows==0);
            manager.Post(s,new("timer","Done","Fixture"),false,_=>windows++,now.AddMinutes(16));Assert(manager.Count==1 && windows==1 && s.NotificationDelivery=="Both");
        });
        test("Notification lifetime is configurable and keeps busy details available",()=>
        {
            var now=DateTimeOffset.UtcNow;var manager=new InternalNotifications();manager.Post(new Settings(),new("timer","Done","Fixture"),false,_=>{},now);
            manager.Tick(now.AddSeconds(5),false,20);Assert(manager.Count==1);manager.Tick(now.AddSeconds(21),true,20);Assert(manager.Count==1);manager.Tick(now.AddSeconds(40),false,20);Assert(manager.Count==1);manager.Tick(now.AddSeconds(42),false,20);Assert(manager.Count==0);
        });
        test("Layout exports include ambient appearance and exclude snooze and quiet schedules",()=>
        {
            var source=new Settings{AmbientStrength=.82,AmbientMotion="Breathe",InteractionMotion="Expressive",NotificationsSnoozedUntil=DateTimeOffset.UtcNow.AddHours(1),QuietHours=true};var target=new Settings();string json=CustomizationProfile.Export(source);CustomizationProfile.Import(json,target);
            Assert(target.AmbientStrength==.82 && target.AmbientMotion=="Breathe" && target.InteractionMotion=="Expressive" && !json.Contains("NotificationsSnoozedUntil") && !json.Contains("QuietHours"));
        });
        test("Hermes preset is an agent backend without assumed voice, vision or local functions",()=>
        {
            var p=AssistantProviderCatalog.Create("Hermes Agent");Assert(p.Backend=="Hermes" && p.Protocol=="ChatCompletions" && p.BaseUrl=="http://127.0.0.1:8642/v1/" && p.Model=="hermes-agent" && !p.Tools && !p.Vision && p.RealtimeUrl=="");
            p.HermesSessionId=Guid.NewGuid().ToString("N");Assert(!JsonSerializer.Serialize(p).Contains(p.HermesSessionId));
        });
        test("Hermes chat isolates sessions, rotates on Clear and sends a unique admission key",()=>Task.Run(async()=>
        {
            var sessions=new System.Collections.Generic.List<string>();var admissions=new System.Collections.Generic.HashSet<string>();
            using var transport=new AssistantTransport(new Handler(request=>
            {
                Assert(request.RequestUri!.AbsolutePath=="/v1/chat/completions" && request.Headers.Authorization?.Parameter=="fixture-only-not-a-real-key");
                sessions.Add(request.Headers.GetValues("X-Hermes-Session-Id").Single());Assert(admissions.Add(request.Headers.GetValues("Idempotency-Key").Single()));
                return new(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"A Hermes response\"}}]}",Encoding.UTF8,"application/json")};
            }));
            using var conversation=new AssistantConversation(transport);var provider=AssistantProviderCatalog.Create("Hermes Agent");
            foreach(string text in new[]{"Hello","Continue"})await conversation.Send(provider,"fixture-only-not-a-real-key","Be concise",text,null,Array.Empty<AssistantTool>(),(_,_)=>Task.FromResult("unused"));
            conversation.Clear();await conversation.Send(provider,"fixture-only-not-a-real-key","Be concise","Hello",null,Array.Empty<AssistantTool>(),(_,_)=>Task.FromResult("unused"));
            Assert(sessions.Count==3 && sessions[0]==sessions[1] && sessions[2]!=sessions[0] && conversation.Messages.Last().Text=="A Hermes response");
        }).GetAwaiter().GetResult());
    }
}
