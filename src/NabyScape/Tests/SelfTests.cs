using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class SelfTests
{
    public static void Run(string[] args)
    {
        var checks=new List<string>();int failed=0;
        void Test(string name,Action test)
        {try{test();checks.Add("PASS  "+name);}catch(Exception ex){failed++;checks.Add("FAIL  "+name+": "+ex.Message);}}
        static void Assert(bool condition,string reason="Assertion failed") {if(!condition)throw new Exception(reason);}
        Test("App resources retain installer notices, theme and icon after source relocation",()=>
        {
            var assembly=typeof(App).Assembly;
            foreach(string name in new[]{"LICENSE","NAudio-LICENSE.txt","LUCIDE-LICENSE.txt","DOTNET-LICENSE.txt","DOTNET-ThirdPartyNotices.txt"})
            {using var notice=assembly.GetManifestResourceStream("NabyScape."+name);Assert(notice is {Length:>0},"Missing embedded notice: "+name);}
            using var packed=assembly.GetManifestResourceStream("NabyScape.g.resources");Assert(packed!=null,"Missing WPF resource bundle");
            using var reader=new System.Resources.ResourceReader(packed!);
            var keys=reader.Cast<System.Collections.DictionaryEntry>().Select(entry=>entry.Key.ToString()).ToArray();
            Assert(keys.Contains("theme.baml") && keys.Contains("assets/island.ico"),"WPF resource paths changed");
        });
        InteractionFeatureTests.Run((name,pass)=>Test(name,()=>Assert(pass)));
        WaveformMotionTests.Run((name,pass)=>Test(name,()=>Assert(pass)));
        ActivityActionsTests.Run((name,pass)=>Test(name,()=>Assert(pass)));
        DetachedPreviewTests.Run((name,pass)=>Test(name,()=>Assert(pass)));
        SmartClipboardTests.Run((name,pass)=>Test(name,()=>Assert(pass)));
        PerformanceTests.Run(Test);
        LauncherTests.Run(Test);
        SettingsUxTests.Run(Test);
        PersistenceTests.Run(Test);
        UpdateTests.Run(Test);
        Test("Customization profiles round-trip without personal data or startup changes",()=>
        {
            var original=new Settings{PanelWidth=520,PanelScale=1.1,Background="#182028",AutoStart=true,ClipboardHistory=true,Shortcuts=new(){new(){Name="Private",Target="https://private.example"}}};original.HoverViews.Reverse();var encoded=CustomizationProfile.Export(original);Assert(!encoded.Contains("Private") && !encoded.Contains("AutoStart") && !encoded.Contains("ClipboardHistory"));var target=new Settings();CustomizationProfile.Import(encoded,target);Assert(target.PanelWidth==520 && target.PanelScale==1.1 && target.Background=="#182028" && target.HoverViews[0].Id=="system" && !target.AutoStart && !target.ClipboardHistory && target.Shortcuts.Count==2);
        });
        Test("Partial customization profiles preserve omitted values and normalize supplied geometry",()=>
        {var target=new Settings{Width=331,Background="#182028"};CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{\"PanelWidth\":99999,\"AutoStart\":true}}",target);Assert(target.PanelWidth==640 && target.Width==331 && target.Background=="#182028" && !target.AutoStart);});
        Test("Invalid customization profile does not mutate current preferences",()=>
        {var target=new Settings{Width=331};bool failed=false;try{CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{}}",target);}catch{failed=true;}Assert(failed && target.Width==331);});
        Test("Layout settings repair non-finite geometry and preserve unique hover order",()=>
        {var s=new Settings{PanelWidth=double.NaN,PanelScale=9,HoverHeight=-1,SwipeDistance=0,HoverDelayMs=0,HoverViews=new(){new(){Id="notes",Enabled=false},new(){Id="notes"},new(){Id="unknown"}}};s.Normalize();Assert(s.PanelWidth==384 && s.PanelScale==1.25 && s.HoverHeight==184 && s.SwipeDistance==24 && s.HoverDelayMs==80 && s.HoverViews.Count==9 && s.HoverViews[0].Id=="notes" && !s.HoverViews[0].Enabled);});
        Test("Mixer includes paused sessions and rejects expired sessions",()=>Assert(AudioMixer.IsControllableSession(0) && AudioMixer.IsControllableSession(1) && !AudioMixer.IsControllableSession(2) && !AudioMixer.IsControllableSession(-1)));
        var now=new DateTimeOffset(2026,10,14,0,0,0,TimeSpan.Zero);
        Test("Timer keeps its absolute deadline while UI is closed",()=> {var c=new Countdown();c.Start(120,now);Assert(c.Remaining(now.AddSeconds(70))==50);});
        Test("Pause/resume preserves remaining time",()=> {var c=new Countdown();c.Start(120,now);c.Pause(now.AddSeconds(30));Assert(c.Remaining(now.AddHours(1))==90);c.Resume(now.AddHours(1));Assert(c.Remaining(now.AddHours(1).AddSeconds(20))==70);});
        Test("Continuous progress preserves duration across pause, restart and resume",()=> {var c=new Countdown();c.Start(120,now);Assert(Math.Abs(c.Progress(now.AddSeconds(30.5))-30.5/120)<.0001);c.Pause(now.AddSeconds(30));c=JsonSerializer.Deserialize<Countdown>(JsonSerializer.Serialize(c))!;Assert(c.Progress(now.AddHours(1))==.25);c.Resume(now.AddHours(1));Assert(c.Progress(now.AddHours(1).AddSeconds(30))==.5);});
        Test("Legacy sessions have bounded progress without a saved duration",()=> {var c=new Countdown{RemainingSeconds=60,Deadline=now.AddSeconds(30)};Assert(c.Progress(now)==.5 && c.Progress(now.AddHours(2))==1);});
        Test("Sleep past a deadline completes exactly once",()=> {var c=new Countdown();c.Start(60,now);Assert(c.Complete(now.AddHours(2)));Assert(!c.Complete(now.AddHours(3)));Assert(c.Remaining(now.AddHours(3))==0);});
        Test("Reset clears state",()=> {var c=new Countdown();c.Start(60,now,"work");c.Reset();Assert(!c.Running && c.RemainingSeconds==0 && c.Phase=="");});
        Test("Countdown display handles hours",()=> {var c=new Countdown();c.Start(3661,now);Assert(c.Display(now)=="1:01:01");});
        Test("Settings normalize dimensions, colors, widgets, and durations",()=>
        {
            var s=new Settings{Width=900,Height=-4,Opacity=2,Accent="invalid",FocusMinutes=0,Widgets=new(){new(){Id="media"},new(){Id="media"},new(){Id="invalid"}}};s.Normalize();
            Assert(s.Width==400 && s.Height==28 && s.Opacity==1 && s.Accent=="#FFFFFF" && s.FocusMinutes==1);
            Assert(s.Widgets.Count==Settings.Names.Count && s.Widgets.Select(w=>w.Id).Distinct().Count()==Settings.Names.Count);
        });
        Test("Hex colors reject malformed input",()=> {Assert(Settings.ValidColor("#abcDEF"));Assert(!Settings.ValidColor("red"));Assert(!Settings.ValidColor("#00000000"));Assert(!Settings.ValidColor(null));});
        Test("Enhancement settings validate unknown profiles and gestures",()=>{var s=new Settings{Performance="bad",WheelAction="bad",HotkeyKey="5",VolumeStep=999};s.Normalize();Assert(s.Performance=="Auto" && s.WheelAction=="volume" && s.HotkeyKey=="I" && s.VolumeStep==10 && !s.ClipboardHistory);});
        Test("Performance profiles adapt to battery, load and interaction",()=>{Assert(OverlayWindow.FrameLimitFor("Auto",180,true,false,20)==180);Assert(OverlayWindow.FrameLimitFor("Auto",180,false,false,20)==60);Assert(OverlayWindow.FrameLimitFor("Auto",180,true,true,20)==60);Assert(OverlayWindow.FrameLimitFor("Auto",180,true,false,90)==60);Assert(OverlayWindow.FrameLimitFor("Hemat",180,false,false,20)==30);Assert(OverlayWindow.FrameLimitFor("Maksimal",180,true,true,90)==180);});
        Test("Frame pacing preserves 60 Hz cadence on a 180 Hz callback stream",()=>{var p=new FramePacer();int accepted=0;for(int i=0;i<=180;i++)if(p.Accept(TimeSpan.FromSeconds(i/180d),60))accepted++;Assert(accepted is >=60 and <=62,$"Accepted {accepted}");});
        AutoTuckTests.Run((name,result)=>Test("Auto tuck: "+name,()=>Assert(result)));
        PeekInteractionTests.Run((name,result)=>Test("Peek: "+name,()=>Assert(result)));
        AudioDeviceTests.Run((name,result)=>Test("Audio: "+name,()=>Assert(result)));
        AudioMixerTests.Run((name,result)=>Test("Mixer: "+name,()=>Assert(result)));
        SystemMonitorTests.Run((name,result)=>Test("Monitor: "+name,()=>Assert(result)));
        UtilityServiceTests.Run((name,result)=>Test("Utilities: "+name,()=>Assert(result)));
        Test("Local event editor parses exact dates and rejects invalid date input",()=>{var parsed=OverlayWindow.EventTime("2026-10-14","09:30",TimeZoneInfo.Utc);Assert(parsed==new DateTimeOffset(2026,10,14,9,30,0,TimeSpan.Zero));bool rejected=false;try{OverlayWindow.EventTime("2026-02-30","09:30",TimeZoneInfo.Utc);}catch(ArgumentException){rejected=true;}Assert(rejected);});
        ProductivityTests.Run((name,result)=>Test("Productivity: "+name,()=>Assert(result)));
        Test("Notch has flared shoulders and transparent lower corners",()=>
        {
            var shape=IslandGeometry.Create(300,44,22,true);
            Assert(shape.FillContains(new System.Windows.Point(3,.1)));
            Assert(!shape.FillContains(new System.Windows.Point(3,20)));
            Assert(shape.FillContains(new System.Windows.Point(150,43)));
            Assert(!shape.FillContains(new System.Windows.Point(17,43)));
        });
        Test("Pill and notch share a symmetric closed contour",()=>
        {
            foreach(bool notch in new[]{true,false})
            {
                var shape=IslandGeometry.Create(300,44,22,notch);
                for(int x=1;x<150;x+=7)for(int y=1;y<44;y+=3)
                    Assert(shape.FillContains(new System.Windows.Point(x,y))==shape.FillContains(new System.Windows.Point(300-x,y)));
            }
        });
        var temp=Path.Combine(Path.GetTempPath(),"NabyScape-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(temp);
            Test("Legacy classic preferences retain panel size without rewriting the file",()=>
            {
                var file=Path.Combine(temp,"classic.json");const string json="{\"CompactLayout\":false,\"Background\":\"#182028\"}";File.WriteAllText(file,json);
                var read=store.Load<Settings>("classic");read.Normalize();Assert(!read.CompactLayout && read.PanelWidth==452 && read.PanelMaxHeight==634 && read.Background=="#182028");Assert(File.ReadAllText(file)==json);
            });
            Test("Legacy compact and unspecified layouts retain compact panel defaults",()=>
            {
                foreach(var json in new[]{"{\"CompactLayout\":true}","{}"})
                {File.WriteAllText(Path.Combine(temp,"compact.json"),json);var read=store.Load<Settings>("compact");Assert(read.CompactLayout && read.PanelWidth==384 && read.PanelMaxHeight==408);}
            });
            Test("Persisted classic panel dimensions migrate only missing keys",()=>
            {
                foreach(var sample in new[]{("{\"CompactLayout\":false,\"PanelWidth\":520,\"PanelMaxHeight\":700}",520d,700d),("{\"CompactLayout\":false,\"PanelWidth\":384}",384d,634d),("{\"CompactLayout\":false,\"PanelMaxHeight\":408}",452d,408d)})
                {File.WriteAllText(Path.Combine(temp,"dimensions.json"),sample.Item1);var read=store.Load<Settings>("dimensions");Assert(read.PanelWidth==sample.Item2 && read.PanelMaxHeight==sample.Item3);}
            });
            Test("New preferences retain compact defaults without creating a settings file",()=>
            {
                var fresh=new Settings();var read=store.Load<Settings>("new-settings");Assert(fresh.PanelWidth==384 && fresh.PanelMaxHeight==408 && read.CompactLayout && read.PanelWidth==384 && read.PanelMaxHeight==408);Assert(!File.Exists(Path.Combine(temp,"new-settings.json")));
            });
            Test("Partial classic profile imports preserve current panel dimensions",()=>
            {
                var target=new Settings{PanelWidth=520,PanelMaxHeight=700};CustomizationProfile.Import("{\"format\":\"luma.layout.v1\",\"values\":{\"CompactLayout\":false}}",target);Assert(!target.CompactLayout && target.PanelWidth==520 && target.PanelMaxHeight==700);
            });
            Test("Preferences round-trip ordering, disabled widgets and shortcuts",()=>
            {
                var s=new Settings{Preset="Mac",Monitor="test-monitor"};s.Widgets.Reverse();s.Widgets[0].Enabled=false;s.Shortcuts.Add(new(){Name="Contoh",Target="https://example.com"});
                store.Save("settings",s);var read=store.Load<Settings>("settings");read.Normalize();Assert(read.Preset=="Mac" && read.Monitor=="test-monitor" && read.Widgets[0].Id==s.Widgets[0].Id && !read.Widgets[0].Enabled && read.Shortcuts.Count==3);
            });
            Test("Running and paused timers survive process restart",()=>
            {
                var state=new SessionState();state.Timer.Start(120,now);state.Focus.Start(1500,now,"work");state.Focus.Pause(now.AddSeconds(30));store.Save("session",state);
                var read=store.Load<SessionState>("session");Assert(read.Timer.Remaining(now.AddSeconds(90))==30 && read.Focus.RemainingSeconds==1470 && !read.Focus.Running && read.Focus.Phase=="work");
            });
            Test("Malformed settings are backed up before recovery",()=>
            {
                File.WriteAllText(Path.Combine(temp,"broken.json"),"{bad json");var s=store.Load<Settings>("broken");Assert(s.Width==228 && store.Warning!=null && Directory.GetFiles(temp,"broken.json.backup-*").Length>0);
            });
            Test("Broken shortcut produces actionable failure",()=>
            {
                bool threw=false;try{ShortcutService.Open(new(){Target=Path.Combine(temp,"missing.exe")});}catch(FileNotFoundException){threw=true;}Assert(threw);
            });
            Test("Unsupported shortcut URI is rejected",()=>
            {bool threw=false;try{ShortcutService.Open(new(){Target="javascript:alert(1)"});}catch(ArgumentException){threw=true;}Assert(threw);});
        }
        finally { if(Directory.Exists(temp))Directory.Delete(temp,true); }
        checks.Add($"RESULT: {checks.Count-failed} passed, {failed} failed");
        var output=Array.IndexOf(args,"--report");
        if(output>=0 && output+1<args.Length)File.WriteAllLines(args[output+1],checks);
        Environment.ExitCode=failed==0?0:1;
    }
}
