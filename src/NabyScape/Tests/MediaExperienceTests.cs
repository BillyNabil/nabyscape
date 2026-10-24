using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal static class MediaExperienceTests
{
    private static void Assert(bool value){if(!value)throw new Exception("Media experience assertion failed.");}
    private sealed class Fake : HttpMessageHandler
    {
        internal int Requests;internal Uri? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests++;LastUri=request.RequestUri;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"syncedLyrics\":\"[00:01.00]Original fixture line\\n[00:03.00]Another sample line\",\"instrumental\":false}")});}
    }
    private sealed class Delayed : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<HttpResponseMessage> First=new(TaskCreationOptions.RunContinuationsAsynchronously);private int count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>++count==1?First.Task:Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
    internal static void Run(Action<string,Action> test)
    {
        test("None survives session enumeration and external current-source changes until explicitly reselected",()=>
        {
            var selection=new MediaSourceSelection();var sessions=new[]{"Spotify.exe","chrome.exe"};
            Assert(selection.Id==MediaSourceSelection.AutoId && selection.Resolve(sessions,"Spotify.exe")=="Spotify.exe");
            selection.Select(null);Assert(selection.IsNone && selection.Resolve(sessions,"chrome.exe","Spotify.exe")==null);
            Assert(selection.Resolve(new[]{"chrome.exe","new-app.exe"},"new-app.exe")==null && selection.Resolve(Array.Empty<string>(),null)==null);
            selection.Select("chrome.exe");Assert(selection.Resolve(sessions,"Spotify.exe")=="chrome.exe");
            selection.Select(MediaSourceSelection.AutoId);Assert(selection.Resolve(sessions,"Spotify.exe")=="Spotify.exe" && selection.Resolve(sessions,"chrome.exe")=="chrome.exe");
            selection.Select("gone.exe");Assert(selection.Resolve(sessions,"chrome.exe")=="chrome.exe" && selection.Id==MediaSourceSelection.AutoId);
            Assert(new MediaSourceSelection().Id==MediaSourceSelection.AutoId);
        });
        test("None clears displayed media and timeline without sending playback commands",()=>
        {
            using var media=new MediaService(ex=>throw ex){DiagnosticFixture=true};media.DiagnosticPlayback(true);media.SelectNone();
            Assert(media.SourceDisabled && !media.Available && !media.Playing && media.Artwork==null && media.Title=="Nothing playing" && !media.CanToggle && !media.CanSeek && media.Timeline()==(0d,0d));
            media.SelectAutomatic();Assert(!media.SourceDisabled && media.Available && media.SourceSelectionId==MediaSourceSelection.AutoId);
        });
        test("Legacy tool order expands in place and keeps disabled choices",()=>
        {
            var s=new Settings{Widgets=new(){new(){Id="media"},new(){Id="tools"},new(){Id="focus"}},Tools=new(){new(){Id="notes"},new(){Id="files",Enabled=false}}};s.Normalize();
            Assert(s.Widgets[1].Id=="notes" && s.Widgets[2].Id=="files" && !s.ToolEnabled("files") && s.ToolEnabled("notes") && s.Widgets.All(w=>w.Id!="tools"));
            var ids=s.Widgets.Select(w=>(w.Id,w.Enabled)).ToArray();s.Normalize();Assert(ids.SequenceEqual(s.Widgets.Select(w=>(w.Id,w.Enabled))));
        });
        test("Widget switches now independently control tools and preserve layout export",()=>
        {
            var s=new Settings{CategoryStyle="Dots",PanelAutoHeight=false,LyricsEnabled=true,LyricsAnimation="Focus",CountdownSoundFile=@"C:\private\sound.wav"};s.Normalize();s.Widgets.Single(w=>w.Id=="notes").Enabled=false;
            Assert(!s.ToolEnabled("notes") && s.ToolEnabled("tasks"));string json=CustomizationProfile.Export(s);Assert(!json.Contains("private") && !json.Contains("LyricsEnabled"));var target=new Settings();CustomizationProfile.Import(json,target);Assert(target.CategoryStyle=="Dots" && !target.PanelAutoHeight && target.LyricsAnimation=="Focus" && !target.LyricsEnabled && !target.ToolEnabled("notes"));
        });
        test("Browser auto-preview switch leaves Spotify and manual playback policy intact",()=>
        {var s=new Settings{BrowserMediaPreviews=false};Assert(!MediaPreviewPolicy.AllowAutomatic(s,"chrome.exe") && !MediaPreviewPolicy.AllowAutomatic(s,"Microsoft.MSEdge_8weky") && !MediaPreviewPolicy.AllowAutomatic(s,"58F7597A8562782A") && MediaPreviewPolicy.AllowAutomatic(s,"Spotify.exe"));s.BrowserMediaPreviews=true;Assert(MediaPreviewPolicy.AllowAutomatic(s,"chrome.exe"));});
        test("LRC parsing handles multi-stamps, offsets and Unicode without inventing word times",()=>
        {
            var lines=LyricTiming.Parse("[offset:-100]\n[00:01.00][00:03.125]朝のサンプル\n[00:62.00]invalid\n[ar:fixture]\n[00:04.5]Evening sample");
            Assert(lines.Length==3 && Math.Abs(lines[0].At-.9)<.001 && lines[0].Text=="朝のサンプル" && Math.Abs(lines[1].At-3.025)<.001 && LyricTiming.Index(lines,.1)==-1 && LyricTiming.Index(lines,3.1)==1 && LyricTiming.Index(lines,100)==2);
        });
        test("Closed lyrics use waveform for missing lines, intro, blank breaks and instrumental markers",()=>
        {
            Assert(LyricTiming.VocalIndex(Array.Empty<LyricLine>(),10)==-1);
            var lines=LyricTiming.Parse("[00:05]First vocal line\n[00:10]\n[00:15][Instrumental]\n[00:20]Next vocal line\n[00:25]♪ ♫\n[00:30](Music)\n[00:35]Intro");
            Assert(LyricTiming.VocalIndex(lines,0)==-1 && LyricTiming.VocalIndex(lines,5)==0 && LyricTiming.VocalIndex(lines,9.99)==0);
            foreach(double at in new[]{10d,14.99,15,19.99,25,30})Assert(LyricTiming.VocalIndex(lines,at)==-1);
            Assert(LyricTiming.VocalIndex(lines,20)==3 && LyricTiming.VocalIndex(lines,24.99)==3 && LyricTiming.VocalIndex(lines,35)==6);
            Assert(LyricTiming.VocalIndex(lines,7)==0); // Seeking backward restores vocals.
            foreach(string marker in new[]{"Instrumental","[instrumental break]","(Interlude)","[Intro]","[Outro]","[Solo]","🎵","🎶"})
                Assert(LyricTiming.VocalIndex(new[]{new LyricLine(0,marker)},1)==-1);
            Assert(LyricTiming.VocalIndex(new[]{new LyricLine(0,"A long vocal line")},80)==0);
        });
        test("Lyrics require consent and usable metadata, and cache without repeated requests",()=>Task.Run(async()=>
        {
            var fake=new Fake();using var service=new LyricsService(fake);await service.Load("Sample title","Sample artist",180,false,true);await service.Load("Sample title","Sample artist",180,true,false);Assert(fake.Requests==0);
            await service.Load("Sample title","Sample artist",180,true,true);await service.Load("Sample title","Sample artist",180,true,true);Assert(fake.Requests==1 && service.Result.Lines.Length==2 && fake.LastUri!.Host=="lrclib.net" && !fake.LastUri.Query.Contains("key"));
            service.Retry();await service.Load("Sample title","Sample artist",180,true,true);Assert(fake.Requests==2);
            service.Import("Sample title","Sample artist",180,"[00:01.0]Original offline sample");await service.Load("Sample title","Sample artist",180,true,true);Assert(fake.Requests==2 && service.Result.Lines[0].Text=="Original offline sample");
        }).GetAwaiter().GetResult());
        test("Shared lyric consumers retain a result until the last view releases it",()=>Task.Run(async()=>
        {
            var fake=new Fake();using var service=new LyricsService(fake);var dock=new object();var panel=new object();service.Acquire(dock);service.Acquire(panel);
            await service.Load("Track","Artist",180,true,true);service.Release(dock);Assert(service.Result.Lines.Length==2);
            service.Release(panel);Assert(service.Result.Lines.Length==0);service.Acquire(dock);await service.Load("Track","Artist",180,true,true);Assert(fake.Requests==2);
        }).GetAwaiter().GetResult());
        test("All closed dock modes and optional icon survive normalization",()=>
        {foreach(string mode in new[]{"Title","Waveform","Lyrics","None"}){var s=new Settings{ClosedMediaContent=mode,ClosedMediaIcon="None"};s.Normalize();Assert(s.ClosedMediaContent==mode && s.ClosedMediaIcon=="None" && s.MediaIntegrationEnabled);}});
        test("Real FFT separates bass from treble with finite bounded bands",()=>
        {
            float[] Bands(double hz){var fft=new SpectrumTransform(48000);for(int i=0;i<16000;i++)fft.Add((float)(.3*Math.Sin(2*Math.PI*hz*i/48000)));return fft.Bands;}
            var bass=Bands(110);var treble=Bands(4000);int max1=Array.IndexOf(bass,bass.Max()),max2=Array.IndexOf(treble,treble.Max());Assert(max2-max1>10 && bass.All(v=>float.IsFinite(v) && v>=0 && v<=1) && treble[max2]>.7);
        });
        test("Cancelled lyric replies cannot overwrite a new track or hide an offline state",()=>Task.Run(async()=>
        {
            var delayed=new Delayed();using var service=new LyricsService(delayed);var old=service.Load("Old sample","Fixture",180,true,true);await service.Load("New sample","Fixture",180,true,true);Assert(service.Result.Status.Contains("unavailable",StringComparison.OrdinalIgnoreCase));
            delayed.First.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"syncedLyrics\":\"[00:01.0]Old sample line\"}")});await old;Assert(service.Result.Lines.Length==0 && service.Result.Status.Contains("unavailable",StringComparison.OrdinalIgnoreCase));
        }).GetAwaiter().GetResult());
        test("Dock visible edges can reach monitor sides despite its larger transparent host",()=>
        {var s=new Settings{Width=228,Horizontal=0,Top=700,Preset="iPhone"};var bounds=new System.Drawing.Rectangle(0,0,1280,800);var point=MonitorFollowPolicy.Place(s,bounds,1,760,400,false,228);Assert(point.X+(760-228)/2==0 && point.Y==400);s.Horizontal=100;point=MonitorFollowPolicy.Place(s,bounds,1,760,400,false,228);Assert(point.X+(760+228)/2==1280);});
        test("Spectrum has fast independent attack, measured silence and bounded release",()=>
        {
            var bands=new float[25];bands[3]=1;var wave=new ActivityVisual();wave.Sample(0,.016,0,false,false,true,0,true,true,bands);Assert(wave.WaveLevel(3)>.7 && wave.WaveLevel(9)==0);
            Array.Clear(bands);for(int i=0;i<25;i++)wave.Sample(0,.016,0,false,false,true,i*.016,true,true,bands);Assert(wave.WaveLevel(3)<.02 && wave.WaveLevel(9)==0);
        });
        test("New preference limits and sound generation stay valid",()=>
        {var s=new Settings{WaveformSensitivity=double.NaN,LyricsOffset=99,CountdownSoundVolume=-1,CountdownSound="bad",WaveformSource="bad",LyricsAnimation="bad",Top=2900};s.Normalize();Assert(s.WaveformSensitivity==1.35 && s.LyricsOffset==10 && s.CountdownSoundVolume==0 && s.CountdownSound=="Chime" && s.WaveformSource=="Spectrum" && s.LyricsAnimation=="Slide" && s.Top==2900);Assert(CountdownAudio.Tone("Chime").Length==88200 && CountdownAudio.Tone("Bell").Any(b=>b!=0));});
        test("Side lyrics expand only within the working area and restore the saved panel width",()=>
        {var s=new Settings{PanelWidth=526,PanelPadding=26,PanelScale=1.04};Assert(MediaLyricsLayout.Width(s,1900,true)>800 && MediaLyricsLayout.Width(s,1900,false)==526 && s.PanelWidth==526);Assert(MediaLyricsLayout.Width(s,600,true)==600);Assert(MediaLyricsLayout.Width(s,480,true)==480);});
        test("Lyrics avoid squeezing two columns into a narrow scaled working area",()=>
        {foreach(double scale in new[]{1d,1.5,2d}){var s=new Settings{PanelWidth=526,PanelPadding=26,PanelScale=scale};double width=MediaLyricsLayout.Width(s,560,true);Assert(width==526 || (width-52)/scale>=MediaLyricsLayout.MinimumBodyWidth);}});
    }
}
