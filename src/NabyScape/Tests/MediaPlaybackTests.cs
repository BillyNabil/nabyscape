using System;
using System.IO;
using System.Linq;

namespace NabyScape;

// Timeline availability, sparse-position interpolation, Auto source following, browser exclusion
// (ported from PR #7) and output-capture diagnostics. No media session or audio device is opened.
internal static class MediaPlaybackTests
{
    private static void Assert(bool value,string reason="Media playback assertion failed."){if(!value)throw new Exception(reason);}
    internal static void Run(Action<string,Action> test)
    {
        test("Timeline availability hides unknown durations and separates read-only progress from seeking",()=>
        {
            foreach(double duration in new[]{0,-5,double.NaN,double.PositiveInfinity})Assert(MediaTimelinePolicy.State(duration,true)==MediaTimelineState.None && MediaTimelinePolicy.State(duration,false)==MediaTimelineState.None);
            Assert(MediaTimelinePolicy.State(180,false)==MediaTimelineState.ReadOnly && MediaTimelinePolicy.State(180,true)==MediaTimelineState.Seekable);
            using var media=new MediaService(ex=>throw ex){DiagnosticFixture=true};media.DiagnosticPlayback(true);Assert(media.TimelineState==MediaTimelineState.Seekable);
            media.SelectNone();Assert(media.TimelineState==MediaTimelineState.None && media.Timeline()==(0d,0d));
        });
        test("Timeline interpolates sparse player updates and survives a missing update stamp",()=>
        {
            var t0=new DateTimeOffset(2026,11,14,12,0,0,TimeSpan.Zero);var start=TimeSpan.Zero;var end=TimeSpan.FromSeconds(200);var clock=new MediaTimelineClock();
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(10),t0,true,t0.AddSeconds(5))==(15d,200d),"Fresh stamp did not advance.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(10),t0,true,t0.AddSeconds(30)).elapsed==40,"Sparse snapshot did not keep advancing.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(10),t0,false,t0.AddSeconds(31)).elapsed==40,"Pause without a new snapshot jumped back.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(42),t0.AddSeconds(31),false,t0.AddSeconds(32)).elapsed==42,"A published pause position was ignored.");
            var missing=new MediaTimelineClock();
            Assert(missing.Sample(start,end,TimeSpan.FromSeconds(20),default,true,t0).elapsed==20 && missing.Sample(start,end,TimeSpan.FromSeconds(20),default,true,t0.AddSeconds(3)).elapsed==23,"Unset stamp did not interpolate locally.");
            Assert(new MediaTimelineClock().Sample(start,end,TimeSpan.FromSeconds(20),t0.AddHours(1),true,t0).elapsed==20,"Future stamp moved the position.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(190),t0,true,t0.AddSeconds(60)).elapsed==200,"Position exceeded the duration.");
            Assert(new MediaTimelineClock().Sample(TimeSpan.FromSeconds(100),TimeSpan.FromSeconds(160),TimeSpan.FromSeconds(130),t0,false,t0)==(30d,60d),"Start offset was not applied.");
            Assert(new MediaTimelineClock().Sample(TimeSpan.Zero,TimeSpan.Zero,TimeSpan.Zero,t0,true,t0.AddSeconds(9))==(0d,0d),"A player without a timeline reported progress.");
        });
        test("Auto source follows an app that starts playing without stealing from pins or for browser previews",()=>
        {
            Assert(MediaFollowPolicy.Started(new[]{"Spotify.exe"},new[]{"Spotify.exe","Telegram.TelegramDesktop","Telegram.TelegramDesktop"}).SequenceEqual(new[]{"Telegram.TelegramDesktop"}));
            var telegram=new[]{"Telegram.TelegramDesktop"};
            Assert(MediaFollowPolicy.Follow(true,true,"Spotify.exe",false,telegram,null,true)=="Telegram.TelegramDesktop");
            Assert(MediaFollowPolicy.Follow(true,true,"Spotify.exe",true,telegram,null,true)=="Telegram.TelegramDesktop");
            Assert(MediaFollowPolicy.Follow(false,true,"Spotify.exe",false,telegram,null,true)==null && MediaFollowPolicy.Follow(true,false,"Spotify.exe",false,telegram,null,true)==null);
            Assert(MediaFollowPolicy.Follow(true,true,"Spotify.exe",true,new[]{"chrome"},null,true)==null && MediaFollowPolicy.Follow(true,true,"Spotify.exe",true,new[]{"308046B0AF4A39CB"},null,true)==null);
            Assert(MediaFollowPolicy.Follow(true,true,"Spotify.exe",false,new[]{"chrome"},null,true)=="chrome" && MediaFollowPolicy.Follow(true,true,"Spotify.exe",false,new[]{"chrome"},null,false)==null);
            Assert(MediaFollowPolicy.Follow(true,true,"Spotify.exe",false,new[]{"Spotify.exe"},null,true)==null && MediaFollowPolicy.Follow(true,true,"Spotify.exe",false,Array.Empty<string>(),null,true)==null);
            Assert(MediaFollowPolicy.Follow(true,true,null,false,new[]{"a.exe","b.exe"},"b.exe",true)=="b.exe" && MediaFollowPolicy.Follow(true,true,null,false,new[]{"a.exe","b.exe"},"c.exe",true)=="a.exe");
            var pinned=new MediaSourceSelection();pinned.Select("Spotify.exe");
            Assert(pinned.Resolve(new[]{"Spotify.exe","Telegram.TelegramDesktop"},"Telegram.TelegramDesktop","Spotify.exe")=="Spotify.exe" && pinned.Id!=MediaSourceSelection.AutoId);
            using var media=new MediaService(ex=>throw ex){DiagnosticFixture=true};Assert(media.FollowsPlayback && !media.BrowserMediaExcluded);
            media.Configure(new Settings{MediaFollowsPlayback=false,ExcludeBrowserMedia=true});Assert(!media.FollowsPlayback && media.BrowserMediaExcluded);
        });
        test("Browser exclusion rejects browsers and opaque identities while preserving music apps",()=>
        {
            foreach(string source in new[]{"chrome.exe","Microsoft.MSEdge_8weky","firefox.exe","brave.exe","opera.exe","vivaldi.exe","Arc.exe","Aside.exe","zen.exe","58F7597A8562782A","8e84ae57-22ff-4526-a705-a6249c1f5e60"})
                Assert(!MediaPreviewPolicy.AllowSession(true,source) && MediaPreviewPolicy.AllowSession(false,source),"Not excluded: "+source);
            Assert(!MediaPreviewPolicy.AllowSession(true,"com.vendor.player","Google Chrome") && !MediaPreviewPolicy.AllowSession(true,"com.vendor.player","Microsoft Edge"));
            foreach(string source in new[]{"Spotify.exe","AppleMusic.exe","vlc.exe","Music.UI.exe","Telegram.TelegramDesktop"})Assert(MediaPreviewPolicy.AllowSession(true,source),"Excluded: "+source);
            Assert(!new Settings().ExcludeBrowserMedia);
        });
        test("Filtered media sessions cannot restore a browser through current previous or pinned selection",()=>
        {
            string[] discovered={"chrome.exe","Spotify.exe","firefox.exe"};
            var sessions=discovered.Where(source=>MediaPreviewPolicy.AllowSession(true,source)).ToArray();
            var selection=new MediaSourceSelection();
            Assert(selection.Resolve(sessions,"chrome.exe","firefox.exe")=="Spotify.exe");
            selection.Select("chrome.exe");Assert(selection.Resolve(sessions,"chrome.exe")=="Spotify.exe" && selection.Id==MediaSourceSelection.AutoId);
            Assert(selection.Resolve(Array.Empty<string>(),"chrome.exe")==null);
            selection.Select(null);Assert(selection.Resolve(sessions,"Spotify.exe")==null && selection.IsNone);
            selection.Select(MediaSourceSelection.AutoId);Assert(selection.Resolve(discovered,"chrome.exe")=="chrome.exe");
        });
        test("Media source preferences default, persist and follow Audio reset and restore",()=>
        {
            var defaults=FactoryDefaults.Create();Assert(defaults.MediaFollowsPlayback && !defaults.ExcludeBrowserMedia && new Settings().MediaFollowsPlayback);
            Assert(PreferenceReset.Categories["Audio"].Contains(nameof(Settings.MediaFollowsPlayback)) && PreferenceReset.Categories["Audio"].Contains(nameof(Settings.ExcludeBrowserMedia)));
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-media-source-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new Store(root);store.Save("settings",new Settings{ExcludeBrowserMedia=true,MediaFollowsPlayback=false});
                var reloaded=new Store(root).Load<Settings>("settings");reloaded.Normalize();Assert(reloaded.ExcludeBrowserMedia && !reloaded.MediaFollowsPlayback,"Media source preferences did not survive reload.");
                PreferenceReset.Reset(store,reloaded,"Audio");Assert(!reloaded.ExcludeBrowserMedia && reloaded.MediaFollowsPlayback,"Audio reset kept media source preferences.");
                PreferenceReset.Restore(store,reloaded);Assert(reloaded.ExcludeBrowserMedia && !reloaded.MediaFollowsPlayback,"Restore lost media source preferences.");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        });
        test("Output capture failures are described, retried with backoff and formats validated",()=>
        {
            Exception Hr(uint code)=>new System.Runtime.InteropServices.COMException("fixture",unchecked((int)code));
            Assert(AudioCaptureDiagnostics.Describe(Hr(0x8889000A)).StartsWith("another app is using the output in exclusive mode") && AudioCaptureDiagnostics.Describe(Hr(0x8889000A)).EndsWith("(0x8889000A)"));
            Assert(AudioCaptureDiagnostics.Describe(Hr(0x88890004)).Contains("changed or was removed") && AudioCaptureDiagnostics.Describe(Hr(0x88890010)).Contains("audio service"));
            Assert(AudioCaptureDiagnostics.Describe(new NotSupportedException()).Contains("sample format") && AudioCaptureDiagnostics.Describe(new TimeoutException()).Contains("no samples") && AudioCaptureDiagnostics.Describe(new InvalidOperationException()).StartsWith("output capture could not start"));
            Assert(AudioCaptureDiagnostics.RetryDelay(1)==5000 && AudioCaptureDiagnostics.RetryDelay(2)==10000 && AudioCaptureDiagnostics.RetryDelay(5)==60000 && AudioCaptureDiagnostics.RetryDelay(50)==60000);
            Assert(AudioCaptureDiagnostics.SupportedFormat(32,2,true) && AudioCaptureDiagnostics.SupportedFormat(16,2,false) && AudioCaptureDiagnostics.SupportedFormat(24,6,false) && AudioCaptureDiagnostics.SupportedFormat(32,8,false));
            Assert(!AudioCaptureDiagnostics.SupportedFormat(64,2,true) && !AudioCaptureDiagnostics.SupportedFormat(16,2,true) && !AudioCaptureDiagnostics.SupportedFormat(8,2,false) && !AudioCaptureDiagnostics.SupportedFormat(16,0,false));
        });
        test("Packaged players match their audio session by package family",()=>
        {
            Assert(AudioService.MediaPackageMatches("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic","microsoft.zunemusic_8wekyb3d8bbwe"));
            Assert(!AudioService.MediaPackageMatches("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic","SpotifyAB.SpotifyMusic_zpdnekdrzrea0") && !AudioService.MediaPackageMatches("Spotify.exe","Spotify.exe") && !AudioService.MediaPackageMatches("A_1!App",null) && !AudioService.MediaPackageMatches("!App","x"));
            Assert(PackageIdentity.FamilyName(Environment.ProcessId)==null,"An unpackaged test process reported a package family.");
            using var audio=new AudioService(_=>{});Assert(!audio.MediaMeterFallback && audio.SpectrumStatus==null);
        });
    }
}
