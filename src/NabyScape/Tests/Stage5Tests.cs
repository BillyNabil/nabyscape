using System;
using System.Text.Json;
namespace NabyScape;
internal static class Stage5Tests
{
    internal static void Run(Action<string,bool> test)
    {
        var wall=new DateTimeOffset(2026,10,17,0,0,0,TimeSpan.Zero);long steady=1000;
        var watch=new StopwatchState();watch.UseClock(()=>wall,()=>steady);watch.Toggle();
        steady+=65000;wall=wall.AddSeconds(-100);
        test("running elapsed uses monotonic time across backwards wall clock",watch.Elapsed==65 && watch.Display=="01:05");
        watch.Toggle();steady+=4000;wall=wall.AddHours(1);test("pause freezes elapsed across sleep",watch.Elapsed==65 && !watch.Running);
        watch.Toggle();steady+=3600000;wall=wall.AddHours(1);watch.Checkpoint();
        test("uptime includes sleep while running and formats hours",watch.Elapsed==3665 && watch.Display=="1:01:05");
        var json=JsonSerializer.Serialize(watch);var recovered=JsonSerializer.Deserialize<StopwatchState>(json)!;
        wall=wall.AddSeconds(25);recovered.UseClock(()=>wall,()=>steady);recovered.Recover();
        test("persisted running stopwatch recovers UTC elapsed once",recovered.Elapsed==3690);recovered.Recover();test("recovery is idempotent",recovered.Elapsed==3690);
        var backwards=JsonSerializer.Deserialize<StopwatchState>(json)!;wall=wall.AddDays(-2);backwards.UseClock(()=>wall,()=>steady);backwards.Recover();test("backwards persisted UTC never subtracts elapsed",backwards.Elapsed==3665);
        watch.Reset();test("reset clears running and persisted anchor",watch.Elapsed==0 && !watch.Running && watch.UtcAnchor==null && watch.Display=="00:00");
        watch.ElapsedSeconds=double.NaN;watch.UseClock(()=>wall,()=>steady);watch.Recover();test("corrupt elapsed recovers safely",watch.Elapsed==0);
        var legacy=new Settings{Monitor="screen-B"};legacy.Normalize();test("legacy nonempty monitor maps Selected",legacy.DisplayMode=="Selected");
        var primary=new Settings();primary.Normalize();test("legacy empty monitor maps Primary",primary.DisplayMode=="Primary");
        CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{\"Monitor\":\"screen-C\"}}",primary);test("old visual profile maps Selected without a new default override",primary.DisplayMode=="Selected" && primary.Monitor=="screen-C");
        legacy.DisplayMode="Follow";CustomizationProfile.Import(CustomizationProfile.Export(legacy),primary);test("visual profile preserves Follow mode",primary.DisplayMode=="Follow");
        CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{\"Monitor\":null}}",primary);test("legacy null monitor profile normalizes to Primary",primary.DisplayMode=="Primary" && primary.Monitor=="");
        test("mixed negative landscape and portrait identities resolve without coordinate assumptions",MonitorFollowPolicy.Target(new[]{"left-negative","portrait-high-DPI"},"left-negative","Follow","left-negative","portrait-high-DPI")=="portrait-high-DPI");
        test("hotplug falls back to primary without mutating stored selection",MonitorFollowPolicy.Target(new[]{"A"},"A","Selected","B",null)=="A" && MonitorFollowPolicy.Target(new[]{"A"},"A","Follow","B","C")=="A");
        test("reconnected selected monitor is resolved again",MonitorFollowPolicy.Target(new[]{"A","B"},"A","Selected","B",null)=="B");
        test("Primary ignores stored selected monitor",MonitorFollowPolicy.Target(new[]{"A","B"},"A","Primary","B",null)=="A");
        foreach(var bounds in new[]{new System.Drawing.Rectangle(-1920,0,1920,1080),new System.Drawing.Rectangle(0,-640,1080,1920),new System.Drawing.Rectangle(1920,80,1280,720)})
        foreach(double scale in new[]{1d,1.5,2})foreach(double horizontal in new[]{0d,50d,100d})
        {
            var settings=new Settings{Horizontal=horizontal,Top=150};int width=(int)Math.Min(640*scale,bounds.Width),height=(int)Math.Min(408*scale,bounds.Height);
            var point=MonitorFollowPolicy.Place(settings,bounds,scale,width,height,false);
            test($"mixed bounds placement contains island {bounds.X}/{bounds.Y}/{scale}/{horizontal}",bounds.Contains(new System.Drawing.Rectangle(point.X,point.Y,width,height)));
        }
        var policy=new MonitorFollowPolicy();
        test("follow first observation waits",policy.Observe("A","B",1000,false,false)==null);
        test("follow waits through 249 ms",policy.Observe("A","B",1249,false,false)==null);
        test("follow moves at 250 ms",policy.Observe("A","B",1250,false,false)=="B");
        policy.Observe("A","B",2000,false,false);policy.Observe("A","C",2200,false,false);test("monitor identity change restarts hysteresis",policy.Observe("A","C",2449,false,false)==null && policy.Observe("A","C",2450,false,false)=="C");
        policy.Observe("A","B",3000,false,false);test("blocked interaction policy resets pending target",policy.Observe("A","B",3400,true,false)==null && policy.Observe("A","B",3500,false,false)==null && policy.Observe("A","B",3750,false,false)=="B");
        policy.Observe("A","B",4000,false,false);test("manual hide cancels pending movement",policy.Observe("A","B",4500,false,true)==null);test("show resolves pointer immediately",policy.Observe("A","B",4600,false,false,true)=="B");
        policy.Observe("A","B",5000,false,false);test("return to source cancels pending movement",policy.Observe("A","A",5300,false,false)==null && policy.Observe("A","B",5400,false,false)==null);
        test("blocked show waits until interaction ends",policy.Observe("A","B",6000,true,false,true)==null);
        test("old widgets retain enabled state and add stopwatch option",legacy.Widgets.Exists(w=>w.Id=="timer" && w.Enabled) && legacy.Widgets.Exists(w=>w.Id=="stopwatch"));
    }
}
