using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class Diagnostics
{
    public static async Task Run(App app,OverlayWindow overlay,string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();int failures=0;
        // Ignore physical input only inside this temporary diagnostic application's UI.
        // Programmatic RaiseEvent/Focus and native geometry checks remain available.
        void IsolateInput(object sender,System.Windows.Input.PreProcessInputEventArgs e)
        {if(e.StagingItem.Input is System.Windows.Input.MouseEventArgs or System.Windows.Input.KeyEventArgs or System.Windows.Input.TextCompositionEventArgs)e.Cancel();}
        System.Windows.Input.InputManager.Current.PreProcessInput+=IsolateInput;
        void Check(string name,bool result) {report.Add((result?"PASS  ":"FAIL  ")+name);if(!result)failures++;}
        Button ActionButton(string name)=>Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        TextBox Entry(string name)=>Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
        void Click(string name)=>ActionButton(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task<bool> WaitUntil(Func<bool> condition,int timeoutMs=3000)
        {var wait=Stopwatch.StartNew();while(!condition() && wait.ElapsedMilliseconds<timeoutMs)await Task.Delay(50);return condition();}
        ScrollViewer OuterScroll(DependencyObject child)
        {for(var parent=VisualTreeHelper.GetParent(child);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is ScrollViewer viewer)return viewer;throw new InvalidOperationException("Missing island scroll viewer.");}
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(element);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(Path.Combine(directory,name+".png"));encoder.Save(stream);
        }
        void CaptureContour(string name)
        {
            overlay.UpdateLayout();var full=new RenderTargetBitmap((int)Math.Ceiling(overlay.ActualWidth),(int)Math.Ceiling(overlay.ActualHeight),96,96,PixelFormats.Pbgra32);full.Render(overlay);
            int width=Math.Min(full.PixelWidth,(int)Math.Ceiling(overlay.PaintedWidth)),height=Math.Min(full.PixelHeight,(int)Math.Ceiling(overlay.PaintedHeight));
            var cropped=new CroppedBitmap(full,new Int32Rect(Math.Max(0,(full.PixelWidth-width)/2),0,width,height));
            var drawingVisual=new DrawingVisual();using(var drawing=drawingVisual.RenderOpen()){drawing.DrawRectangle(UI.Brush("#3C4250"),null,new Rect(0,0,width+32,height+20));drawing.DrawImage(cropped,new Rect(16,0,width,height));}
            var bitmap=new RenderTargetBitmap(width+32,height+20,96,96,PixelFormats.Pbgra32);bitmap.Render(drawingVisual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        try
        {
            overlay.DiagnosticRun=true;app.Settings.SmartActivities=false;
            if(Environment.GetCommandLineArgs().Contains("--performance-checks-only")){await PerformanceChecks.Run(app,overlay,Check);return;}
            if(Environment.GetCommandLineArgs().Contains("--performance-benchmark")){await PerformanceDiagnostics.Run(app,overlay,directory);return;}
            if(Environment.GetCommandLineArgs().Contains("--tools240-only")){await Task.Delay(500);await Tools240Diagnostics.Run(app,overlay,directory,Check);return;}
            if(Environment.GetCommandLineArgs().Contains("--focus-only"))
            {
                overlay.Reveal();overlay.SelectPage("media",false);await Task.Delay(350);
                var header=(Button)typeof(OverlayWindow).GetField("compact",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
                Check("Expanded panel hides duplicate header",!header.IsVisible);
                foreach(var button in new[]{ActionButton("Media")})
                {
                    button.Focus();await Task.Delay(60);button.ApplyTemplate();
                    var focusFrame=(Border)button.Template.FindName("Frame",button);
                    Check("Click/programmatic focus leaves button outline unchanged: "+AutomationProperties.GetName(button),button.IsKeyboardFocused && focusFrame.BorderThickness==button.BorderThickness);
                    Check("Native keyboard focus visual remains available: "+AutomationProperties.GetName(button),button.FocusVisualStyle!=null);
                }
                CaptureContour("mouse-focus-without-stroke");return;
            }
            if(Environment.GetCommandLineArgs().Contains("--awake-only"))
            {
                overlay.Reveal(false);overlay.ShowTools("awake",false);await Task.Delay(200);var awakeWatch=Stopwatch.StartNew();
                try
                {
                    app.Awake.Start(TimeSpan.FromSeconds(2),false,DateTimeOffset.UtcNow);
                    Check("Keep awake starts with a stop action",app.Awake.Active && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Stop keep awake"));
                    Check("Keep awake expires and restores Start",await WaitUntil(()=>!app.Awake.Active && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Start keep awake"),6000));
                    report.Add($"INFO  Keep awake checked after {awakeWatch.Elapsed.TotalSeconds:0.00}s; active: {app.Awake.Active}; until: {app.Awake.Until:O}; commands: {overlay.CommandsOpen}");
                }
                finally{app.Awake.Stop();}return;
            }
            if(Environment.GetCommandLineArgs().Contains("--premium-only")){await Task.Delay(500);await PremiumDiagnostics.Run(app,overlay,directory,Check);return;}
            if(Environment.GetCommandLineArgs().Contains("--audio-only"))
            {
                await Task.Delay(1500);var samples=new List<float>();var outputs=new List<float>();
                for(int i=0;i<120;i++){samples.Add(app.Audio.MediaPeak(app.Media.Selected?.SourceAppUserModelId));outputs.Add(app.Audio.Peak());await Task.Delay(33);}
                report.Add($"INFO  Media source: {app.Media.Selected?.SourceAppUserModelId}; playing: {app.Media.Playing}; sessions: {app.Audio.MediaMeterSessions}; audible: {app.Audio.MediaAudibleSessions}; muted: {app.Audio.MediaMeterMuted}");
                report.Add($"INFO  Selected app peaks: {samples.Min():0.00000}..{samples.Max():0.00000}; distinct: {samples.Distinct().Count()}; all outputs: {outputs.Min():0.00000}..{outputs.Max():0.00000}");
                Check("Read-only meter samples are finite and bounded",samples.All(v=>float.IsFinite(v) && v>=0 && v<=1));return;
            }
            if(Environment.GetCommandLineArgs().Contains("--interactions-only"))
            {await Task.Delay(500);await InteractionDiagnostics.Run(app,overlay,directory,Check);await RefinementDiagnostics.Run(app,overlay,directory,Check);await PremiumDiagnostics.Run(app,overlay,directory,Check);return;}
            if(Environment.GetCommandLineArgs().Contains("--review-regressions"))
            {await Task.Delay(500);await InteractionDiagnostics.RunReviewRegressions(app,overlay,directory,Check);return;}
            await Task.Delay(2500);
            Check("Overlay starts without taking focus",Native.GetForegroundWindow()!=new WindowInteropHelper(overlay).Handle);
            Check("Collapsed painted contour matches configured dimensions",Math.Abs(overlay.PaintedWidth-app.Settings.Width)<1 && Math.Abs(overlay.PaintedHeight-app.Settings.Height)<1);
            double hostWidth=overlay.ActualWidth,hostHeight=overlay.ActualHeight;
            report.Add($"INFO  Selected monitor refresh rate reported by Windows: {Native.RefreshRate(Native.ChooseScreen(app.Settings))} Hz. No display setting was changed.");
            string originalHeader=overlay.HeaderLabel.Text;Motion.Text(overlay.HeaderLabel,"01:59");Motion.Text(overlay.HeaderLabel,"01:58");
            Check("Live text updates never reset opacity or translate the header",overlay.HeaderLabel.Opacity==1 && overlay.HeaderLabel.RenderTransform==Transform.Identity);
            Motion.Text(overlay.HeaderLabel,originalHeader);
            var signal=new ActivityVisual();signal.Sample(.5,.033,0,false,false,true,0);double attack=signal.Envelope;signal.Sample(0,.033,0,false,false,true,0);
            Check("Output envelope responds to amplitude and decays smoothly",attack>0 && signal.Envelope>0 && signal.Envelope<attack);
            signal.Sample(.8,.033,0,false,false,false,0);Check("Reduced motion immediately silences the waveform",signal.Envelope==0);
            Check("CoreAudio output meter returns a bounded sample",app.Audio.Peak() is >=0 and <=1);
            Capture(overlay,"island-compact");
            CaptureContour("compact-closed");
            // A neutral background makes the alpha contour reviewable without changing the actual surface.
            var notchFull=new RenderTargetBitmap((int)overlay.ActualWidth,(int)overlay.ActualHeight,96,96,PixelFormats.Pbgra32);notchFull.Render(overlay);
            BitmapSource notchBitmap=new CroppedBitmap(notchFull,new Int32Rect(((int)overlay.ActualWidth-(int)overlay.PaintedWidth)/2,0,(int)overlay.PaintedWidth,(int)overlay.PaintedHeight));
            var pixels=new byte[(int)notchBitmap.Width*(int)notchBitmap.Height*4];notchBitmap.CopyPixels(pixels,(int)notchBitmap.Width*4,0);
            int center=(((int)notchBitmap.Height-3)*(int)notchBitmap.Width+(int)notchBitmap.Width/2)*4;
            Check("Black glass remains dark, opaque, with transparent outside corners",pixels[center]<40 && pixels[center+1]<40 && pixels[center+2]<40 && pixels[center+3]==255 && pixels[((int)notchBitmap.Height-1)*(int)notchBitmap.Width*4+3]==0);
            var proofVisual=new DrawingVisual();using(var drawing=proofVisual.RenderOpen()){drawing.DrawRectangle(UI.Brush("#3C4250"),null,new Rect(0,0,420,100));drawing.DrawImage(notchBitmap,new Rect((420-notchBitmap.Width)/2,0,notchBitmap.Width,notchBitmap.Height));}
            var proof=new RenderTargetBitmap(420,100,96,96,PixelFormats.Pbgra32);proof.Render(proofVisual);var proofEncoder=new PngBitmapEncoder();proofEncoder.Frames.Add(BitmapFrame.Create(proof));using(var proofFile=File.Create(Path.Combine(directory,"notch-contour-proof.png")))proofEncoder.Save(proofFile);
            var intervals=new List<double>();TimeSpan? lastFrame=null;
            EventHandler frame=(_,args)=>{var time=((RenderingEventArgs)args).RenderingTime;if(lastFrame.HasValue && time>lastFrame.Value)intervals.Add((time-lastFrame.Value).TotalMilliseconds);lastFrame=time;};
            overlay.PreviewActivity();await Task.Delay(300);
            Check("First hover stage shows a light preview",overlay.PaintedHeight<=105 && overlay.PaintedHeight>75);
            bool hoverSettled=await WaitUntil(()=>overlay.IsPeeking && Math.Abs(overlay.PaintedHeight-192)<.1 && overlay.PaintedWidth<=350.1);
            Check("Roomier hover stays at 350 x 192 DIP without stealing focus",hoverSettled && Native.GetForegroundWindow()!=new WindowInteropHelper(overlay).Handle);
            int framesBefore=overlay.LiveFrames;var frameWatch=Stopwatch.StartNew();await Task.Delay(700);frameWatch.Stop();
            Check("Hover activity renders at compositor cadence above 30 FPS",overlay.DisplaySynchronized && overlay.LiveFrames-framesBefore>25);
            report.Add($"INFO  Hover render callbacks: {(overlay.LiveFrames-framesBefore)/frameWatch.Elapsed.TotalSeconds:0.0}/s over {frameWatch.Elapsed.TotalSeconds:0.00}s; WPF tier: {RenderCapability.Tier>>16}. This is callback cadence, not a GPU present trace.");
            double peekOpacity=overlay.PeekOpacity;overlay.PreviewActivity();
            Check("Repeated hover does not restart the preview fade",overlay.PeekOpacity==peekOpacity && peekOpacity>.99);
            Check("Hover keeps the same native backing surface",overlay.ActualWidth==hostWidth && overlay.ActualHeight==hostHeight);
            Capture(overlay,"island-hover");CaptureContour("compact-hover");
            overlay.SelectPeek("media");overlay.DragPeek(-92);overlay.FinishPeekSwipe(-92);await Task.Delay(350);
            Check("Dragging left advances to Timer while keeping the hover compact",overlay.PeekPage=="timer" && overlay.IsPeeking && !overlay.IsExpanded && overlay.PaintedHeight<=192.1);CaptureContour("hover-timer");
            overlay.DragPeek(20);overlay.FinishPeekSwipe(20);await Task.Delay(350);Check("A short swipe settles back on the same page",overlay.PeekPage=="timer");Check("Keyboard navigation excludes off-screen hover views",overlay.PeekTabSafe);
            Click("Start or pause preview timer");Check("Preview starts a five-minute timer",app.Session.Timer.Running && app.Session.Timer.Remaining(DateTimeOffset.UtcNow)>295);
            Click("Start or pause preview timer");var pausedRemaining=app.Session.Timer.Remaining(DateTimeOffset.UtcNow);Click("Add five minutes to preview timer");Check("Extending a paused preview timer adds five minutes without resuming",!app.Session.Timer.Running && Math.Abs(app.Session.Timer.Remaining(DateTimeOffset.UtcNow)-pausedRemaining-300)<1);
            Click("Reset preview timer");Check("Preview timer reset clears its saved deadline",app.Session.Timer.Remaining(DateTimeOffset.UtcNow)==0 && app.Store.Load<SessionState>("session").Timer.RemainingSeconds==0);
            overlay.NavigatePeek(1);await Task.Delay(350);Check("Preview arrow advances to Focus",overlay.PeekPage=="focus");Click("Start or pause preview focus");Check("Preview focus uses the configured work duration",app.Session.Focus.Running && app.Session.Focus.Phase=="work" && app.Session.Focus.Remaining(DateTimeOffset.UtcNow)>app.Settings.FocusMinutes*60-5);Click("Reset preview focus");CaptureContour("hover-focus");
            var previewEvent=app.Calendar.Add("Design review",DateTimeOffset.UtcNow.AddMinutes(15),DateTimeOffset.UtcNow.AddMinutes(45),"https://example.com/meeting");overlay.SelectPeek("calendar");await Task.Delay(350);Check("Calendar preview shows the upcoming local event and meeting action",Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="Design review") && ActionButton("Join preview meeting").IsVisible);CaptureContour("hover-calendar");app.Calendar.Remove(previewEvent.Id);
            overlay.SelectPeek("notes");await Task.Delay(350);Entry("Preview note draft").Text="Draft stays while swiping";Check("Swipe recognition leaves buttons, sliders and writing fields to native controls",OverlayWindow.PeekGestureBlocked(Entry("Preview note draft")) && OverlayWindow.PeekGestureBlocked(ActionButton("Next preview")) && OverlayWindow.PeekGestureBlocked(Descendants(overlay).OfType<Slider>().Single(s=>AutomationProperties.GetName(s)=="Preview volume")) && !OverlayWindow.PeekGestureBlocked(new TextBlock()));overlay.SelectPeek("files");overlay.SelectPeek("notes");Check("Swiping away from quick capture preserves its draft",Entry("Preview note draft").Text=="Draft stays while swiping");Click("Save preview note");Check("Shared preview notes save locally without clearing the document",app.Store.Load<ProductivityData>("productivity").Note.Contains("Draft stays while swiping") && Entry("Preview note draft").Text==app.Productivity.Note);CaptureContour("hover-note");
            overlay.SelectPeek("files");await Task.Delay(350);Check("Empty shelf preview provides a drop target and full-shelf action",ActionButton("Open full file shelf").IsVisible && Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="Drop files onto the notch"));CaptureContour("hover-shelf");
            overlay.DragPeek(-120);overlay.FinishPeekSwipe(-26.4);Check("Swiping beyond the last preview stays within the deck",overlay.PeekPage=="files");
            overlay.SelectPeek("media");overlay.DragPeek(120);overlay.FinishPeekSwipe(26.4);Check("Swiping beyond the first preview stays within the deck",overlay.PeekPage=="media");
            Check("Preview media exposes volume, output, and transport controls",Descendants(overlay).OfType<Slider>().Any(s=>AutomationProperties.GetName(s)=="Preview volume") && ActionButton("Choose preview audio output")!=null && ActionButton("Next track in preview")!=null);
            foreach(string wheelTarget in new[]{"header","footer","slider","button"})
            {
                overlay.Collapse();app.ChangeSettings();overlay.PreviewActivity();overlay.SelectPeek("media");overlay.DiagnosticPeekWheel(-30,wheelTarget);overlay.DiagnosticPeekWheel(-30,wheelTarget);overlay.DiagnosticPeekWheel(-40,wheelTarget);
                Check("Wheel browses without opening over "+wheelTarget,overlay.PeekPage=="timer" && overlay.IsPeeking && !overlay.IsExpanded);overlay.DiagnosticPeekWheel(-120,wheelTarget);Check("One wheel burst advances only once over "+wheelTarget,overlay.PeekPage=="timer");await Task.Delay(330);
            }
            double hoverWidth=app.Settings.HoverWidth,hoverHeight=app.Settings.HoverHeight,panelZoom=app.Settings.PanelScale;
            foreach(var size in new[]{(300d,184d,1.25),(350d,192d,1d),(300d,280d,.85)})
            {
                overlay.Collapse();app.Settings.HoverWidth=size.Item1;app.Settings.HoverHeight=size.Item2;app.Settings.PanelScale=size.Item3;app.ChangeSettings();overlay.PreviewActivity();
                foreach(string page in overlay.HoverDeckIds){overlay.SelectPeek(page);await WaitUntil(()=>overlay.HoverSettled,5000);overlay.DiagnosticLongPreview();overlay.UpdateLayout();string overflow=overlay.HoverOverflow();Check($"Hover bounds {page} at {size.Item1}/{size.Item2}/{size.Item3}",overflow.Length==0);if(overflow.Length>0)report.Add("INFO  Hover overflow: "+overflow);CaptureContour($"hover-{page}-{size.Item1}-{size.Item2}");}
            }
            string hoverPreset=app.Settings.Preset;overlay.Collapse();app.Settings.Preset="iPhone";app.Settings.HoverWidth=300;app.Settings.HoverHeight=184;app.ChangeSettings();overlay.PreviewActivity();
            foreach(string page in overlay.HoverDeckIds){overlay.SelectPeek(page);await WaitUntil(()=>overlay.HoverSettled,5000);overlay.DiagnosticLongPreview();Check("Pill hover bounds with long content: "+page,overlay.HoverOverflow().Length==0);CaptureContour("hover-pill-"+page);}
            overlay.Collapse();app.Settings.Preset=hoverPreset;app.Settings.HoverWidth=hoverWidth;app.Settings.HoverHeight=hoverHeight;app.Settings.PanelScale=panelZoom;app.ChangeSettings();
            app.Settings.ReduceMotion=true;app.ChangeSettings();overlay.PreviewActivity();overlay.SelectPeek("calendar");Check("Reduce Motion switches preview pages without animated translations",overlay.PeekPage=="calendar" && !overlay.PeekMoving);overlay.Collapse();app.Settings.ReduceMotion=false;app.ChangeSettings();
            overlay.Collapse();await Task.Delay(800);
            CompositionTarget.Rendering+=frame;
            double fixedHost=overlay.ActualWidth;double firstPaint=overlay.PaintedHeight;
            overlay.Reveal(false);bool morphStarted=overlay.IsAnimating;
            bool morphAdvanced=await WaitUntil(()=>overlay.PaintedHeight>firstPaint+.5);
            Check("Morph progresses inside a fixed native host",morphStarted && morphAdvanced && Math.Abs(overlay.ActualWidth-fixedHost)<.01);
            await Task.Delay(750);CompositionTarget.Rendering-=frame;
            overlay.UpdateLayout();
            Check("Morph settles and releases its frame subscription",!overlay.IsAnimating);
            if(intervals.Count>0){var samples=intervals.OrderBy(v=>v).ToArray();report.Add($"INFO  Morph rendering intervals: median {samples[samples.Length/2]:0.0}ms, p95 {samples[Math.Min(samples.Length-1,(int)(samples.Length*.95))]:0.0}ms, {samples.Length} samples.");}
            Check("Expanded panel remains within 384 x 408 DIP",overlay.PaintedWidth<=384.1 && overlay.PaintedHeight<=408.1 && overlay.PaintedHeight>150);
            var prev=Descendants(overlay).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="Previous track");var playButton=Descendants(overlay).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="Play or pause");
            Check("Playback controls have at least 18 DIP breathing room",playButton.TranslatePoint(new Point(0,0),overlay).X-prev.TranslatePoint(new Point(prev.ActualWidth,0),overlay).X>=17.9);
            var volumeControl=Descendants(overlay).OfType<Slider>().First(s=>AutomationProperties.GetName(s)=="Windows volume");
            Check("Music content fits without clipping the volume row",overlay.ContentFits && volumeControl.ActualWidth>80 && volumeControl.TranslatePoint(new Point(0,volumeControl.ActualHeight),overlay).Y<overlay.PaintedHeight-12);
            overlay.SelectPage("focus",false);bool timerControls=Descendants(overlay).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="Start");overlay.SelectPage("media",false);
            Check("Timer and media controls remain available on their selected pages",timerControls && Descendants(overlay).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="Next track"));
            Capture(overlay,"island-expanded");
            foreach(var page in new[]{"focus","status","access","media"}){overlay.SelectPage(page);await Task.Delay(650);Check("Compact tab remains bounded: "+page,overlay.ActivePage==page && overlay.PaintedHeight<=408.1);Capture(overlay,"tab-"+page);CaptureContour("compact-"+page);}
            overlay.Collapse();await Task.Delay(75);overlay.Reveal(false);await Task.Delay(75);overlay.Collapse();await Task.Delay(900);
            Check("Rapid open-close reversals settle without resizing the native surface",!overlay.IsExpanded && !overlay.IsAnimating && Math.Abs(overlay.PaintedWidth-app.Settings.Width)<.1 && overlay.ActualWidth==hostWidth && overlay.ActualHeight==hostHeight);
            for(int i=0;i<18;i++){if(i%3==0)overlay.PreviewActivity();else if(i%3==1)overlay.Reveal(false);else overlay.Collapse();await Task.Delay(35);}overlay.Collapse();await Task.Delay(1100);
            Check("Mixed hover/open/close stress settles without opacity loss",!overlay.IsAnimating && !overlay.IsExpanded && !overlay.IsPeeking && overlay.HeaderLabel.Opacity==1 && Math.Abs(overlay.PaintedHeight-app.Settings.Height)<.1);
            app.Settings.ReduceMotion=true;app.ChangeSettings();overlay.Reveal(false);
            Check("Reduce Motion uses immediate final geometry",!overlay.IsAnimating && Math.Abs(overlay.PaintedWidth-overlay.ActualWidth)<.1);
            Check("Reduce Motion stops continuous activity sampling",!overlay.LiveRunning);
            overlay.Collapse();app.Settings.ReduceMotion=false;app.ChangeSettings();
            app.Settings.GlassStyle=false;app.ChangeSettings();overlay.UpdateLayout();
            var solid=new RenderTargetBitmap((int)overlay.ActualWidth,(int)overlay.ActualHeight,96,96,PixelFormats.Pbgra32);solid.Render(overlay);var solidPixel=new byte[4];solid.CopyPixels(new Int32Rect((int)overlay.ActualWidth/2,(int)overlay.PaintedHeight-3,1,1),solidPixel,4,0);
            Check("Glass can be disabled to restore solid pure black",solidPixel[0]==0 && solidPixel[1]==0 && solidPixel[2]==0 && solidPixel[3]==255);
            app.Settings.GlassStyle=true;app.ChangeSettings();
            var fixtureFile=Path.Combine(directory,"shelf-fixture.txt");File.WriteAllText(fixtureFile,"NabyScape local shelf fixture");app.Productivity.AddFiles(new[]{fixtureFile});overlay.ShowTools("files",false);await Task.Delay(400);overlay.UpdateLayout();
            Check("File shelf exposes the staged local file",Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="shelf-fixture.txt"));CaptureContour("tools-files");
            overlay.ShowTools("notes",false);await Task.Delay(200);var noteBox=Descendants(overlay).OfType<TextBox>().First(t=>AutomationProperties.GetName(t)=="Quick notes");noteBox.Text="Local diagnostic note";
            Check("Notes editor saves through its debounce",await WaitUntil(()=>app.Store.Load<ProductivityData>("productivity").Note=="Local diagnostic note"));app.Productivity.AddTask("Diagnostic task");overlay.ShowTools("notes",false);await Task.Delay(150);CaptureContour("tools-notes");
            Check("Checklist editor exposes its persisted task",Descendants(overlay).OfType<CheckBox>().Any(c=>c.Content is TextBlock t && t.Text=="Diagnostic task"));
            overlay.ShowTools("files",false);await Task.Delay(200); // The same selector used by file DragEnter; no cross-app drag is performed.
            Check("File drag-in routes directly to shelf content",Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="shelf-fixture.txt"));
            Click("All tools");await Task.Delay(350);overlay.UpdateLayout();CaptureContour("tools-home");
            var launcherScroll=OuterScroll(ActionButton("File shelf"));report.Add($"INFO  Launcher content {launcherScroll.ExtentHeight:0.0} DIP; viewport {launcherScroll.ViewportHeight:0.0} DIP.");Check("All launcher tiles fit without scrolling",launcherScroll.ScrollableHeight<1);
            Check("Tool launcher uses six compact rows without marketing copy",new[]{"File shelf","Notes","Clipboard","Calendar","Keep awake","Clean links"}.All(name=>ActionButton(name).ActualWidth>=300 && ActionButton(name).ActualHeight==32) && !Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text.Contains("everyday essentials") || t.Text.Contains("Drop & collect")));
            Check("Launcher exposes all six tools without a dropdown",new[]{"File shelf","Notes","Clipboard","Calendar","Keep awake","Clean links"}.All(name=>Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==name)) && !Descendants(overlay).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Choose a tool"));
            Click("Notes");Check("Launcher returns to the writing editor",Entry("Quick notes").IsVisible);
            Click("Tasks");Check("Tasks view separates the checklist from writing",!Entry("Quick notes").IsVisible && Entry("New task").IsVisible);
            Entry("New task").Text="Unsubmitted task draft";Click("Write");Click("Tasks");Check("Switching notes segments preserves an unsubmitted task",Entry("New task").Text=="Unsubmitted task draft");
            var taskCheck=Descendants(overlay).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Diagnostic task");taskCheck.IsChecked=true;taskCheck.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("Completing a task preserves its control and checklist view",app.Productivity.Tasks.Any(t=>t.Text=="Diagnostic task" && t.Done) && Descendants(overlay).Contains(taskCheck) && Entry("New task").IsVisible);
            CaptureContour("tools-tasks");
            for(int i=0;i<12;i++)app.Productivity.AddTask("Scroll fixture "+i);overlay.ShowTools("notes",false);await Task.Delay(250);overlay.UpdateLayout();var outerScroll=OuterScroll(Entry("New task"));outerScroll.ScrollToEnd();await Task.Delay(100);
            Check("Long tools content provides a meaningful scroll-reset fixture",outerScroll.VerticalOffset>0);
            var chrome=outerScroll.Template.FindName("PART_VerticalScrollBar",outerScroll) as System.Windows.Controls.Primitives.ScrollBar;
            Check("Scrollbar has a dedicated gutter outside the content viewport",chrome!=null && chrome.Width==12 && chrome.Maximum>0 && Descendants(chrome).OfType<Border>().Any(b=>b.Name=="Grip" && b.Width==2) && Descendants(outerScroll).OfType<ScrollContentPresenter>().First().ActualWidth<=outerScroll.ActualWidth-13.9);
            overlay.ShowTools("clips",false);await Task.Delay(150);Check("Clipboard remains opt-in with an explicit enable action",!app.Productivity.ClipboardEnabled && Descendants(overlay).OfType<Button>().Any(b=>b.Content?.ToString()=="Enable history"));CaptureContour("tools-clips");
            Check("Switching tools returns the outer viewport to the top",outerScroll.VerticalOffset==0);
            // Exercise real Commit/Changed notifications with native clipboard APIs disabled by App.
            string clipboardFixture="Diagnostic refresh "+Guid.NewGuid().ToString("N");int productivityChanges=0;
            void CountProductivityChange()=>productivityChanges++;
            app.Productivity.Changed+=CountProductivityChange;
            try
            {
                app.Productivity.SetClipboardEnabled(true);
                overlay.ShowTools("calendar",false);Click("Add local event");
                var draft=new[]{("Event title","Unsaved meeting draft"),("Event start date","2031-06-14"),("Event start time","14:15"),("Event end date","2031-06-15"),("Event end time","15:45"),("Meeting URL","https://example.com/draft-meeting")}.Select(field=>(name:field.Item1,value:field.Item2,control:Entry(field.Item1))).ToArray();
                foreach(var field in draft)field.control.Text=field.value;
                int changesBefore=productivityChanges;
                Check("Calendar refresh fixture emits a real productivity change",app.Productivity.IngestText(clipboardFixture+" calendar") && productivityChanges==changesBefore+1);
                Check("Calendar draft controls and all entered values survive productivity changes",draft.All(field=>ReferenceEquals(Entry(field.name),field.control) && Entry(field.name).Text==field.value));
                overlay.ShowTools("links",false);var draftLink=Entry("Link to clean");var draftResult=Entry("Cleaned link");draftLink.Text="https://example.com/draft?sku=42&utm_source=diagnostic#details";
                const string previewStatus="Tracking parameters removed · check the result below";var draftStatus=Descendants(overlay).OfType<TextBlock>().Single(t=>t.Text==previewStatus);var draftCopy=ActionButton("Copy cleaned link");
                bool CleanerRetained()=>ReferenceEquals(Entry("Link to clean"),draftLink) && draftLink.Text=="https://example.com/draft?sku=42&utm_source=diagnostic#details" && ReferenceEquals(Entry("Cleaned link"),draftResult) && draftResult.Text=="https://example.com/draft?sku=42#details" && Descendants(overlay).Contains(draftStatus) && draftStatus.Text==previewStatus && ReferenceEquals(ActionButton("Copy cleaned link"),draftCopy) && draftCopy.IsEnabled;
                changesBefore=productivityChanges;
                Check("Clean-link refresh fixture emits a real productivity change",app.Productivity.IngestText(clipboardFixture+" links") && productivityChanges==changesBefore+1);
                Check("Clean-link live input, result, status and copy choice survive productivity changes",CleanerRetained());
                int clipCount=app.Productivity.Clips.Count;changesBefore=productivityChanges;
                Check("Duplicate clean-link fixture exercises Touch and emits Changed",!app.Productivity.IngestText(clipboardFixture+" links") && app.Productivity.Clips.Count==clipCount && productivityChanges==changesBefore+1);
                Check("Clean-link live preview also survives duplicate clipboard changes",CleanerRetained());
                overlay.ShowTools("awake",false);var draftDuration=Descendants(overlay).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Keep awake duration");var draftDisplay=Descendants(overlay).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="Keep the display on too");draftDuration.SelectedIndex=2;draftDisplay.IsChecked=true;
                changesBefore=productivityChanges;
                Check("Keep-awake refresh fixture emits a real productivity change",app.Productivity.IngestText(clipboardFixture+" awake") && productivityChanges==changesBefore+1);
                Check("Keep-awake live duration and display choices survive productivity changes",Descendants(overlay).Contains(draftDuration) && draftDuration.SelectedIndex==2 && Descendants(overlay).Contains(draftDisplay) && draftDisplay.IsChecked==true && !app.Awake.Active);
                overlay.ShowTools("clips",false);changesBefore=productivityChanges;
                Check("Clips refresh fixture emits a real productivity change",app.Productivity.IngestText(clipboardFixture+" visible clip") && productivityChanges==changesBefore+1);
                Check("Clips page still refreshes to show newly ingested text",Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text==clipboardFixture+" visible clip"));
            }
            finally{app.Productivity.Changed-=CountProductivityChange;app.Productivity.SetClipboardEnabled(false);}
            Check("Refresh regressions leave clipboard history off and fixtures cleared",!app.Settings.ClipboardHistory && !app.Productivity.ClipboardEnabled && app.Productivity.Clips.Count==0 && app.Store.Load<ProductivityData>("productivity").Clips.Count==0);
            overlay.ShowTools("notes",false);await Task.Delay(200);outerScroll.ScrollToEnd();await Task.Delay(100);
            overlay.ShowCommands(false);await Task.Delay(400);var search=Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Search commands");search.Text="clean links";await Task.Delay(200);
            Check("Opening commands resets a long tool's scroll offset",outerScroll.VerticalOffset==0);
            Check("Command bar filters existing actions inside compact bounds",overlay.CommandsOpen && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Clean links") && !Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Open Settings") && overlay.PaintedHeight<=408.1);CaptureContour("command-bar");
            search.Text="";await WaitUntil(()=>!overlay.IsAnimating);overlay.UpdateLayout();
            bool CommandsFit()
            {
                overlay.UpdateLayout();var host=OuterScroll(Entry("Search commands"));
                var controls=Descendants(host).OfType<FrameworkElement>().Where(e=>e is TextBox || e is Button b && (b.Content?.ToString()=="Back" || b.Content is StackPanel));
                return controls.All(e=>{var bounds=e.TransformToAncestor(host).TransformBounds(new Rect(e.RenderSize));bool fits=bounds.Left>=-0.5 && bounds.Right<=host.ActualWidth-7;if(!fits)report.Add($"INFO  Command overflow: {e.GetType().Name} {bounds.Left:0.##}..{bounds.Right:0.##}, viewport {host.ActualWidth:0.##}");return fits;});
            }
            Check("Unfiltered command controls leave a scrollbar gutter",CommandsFit());CaptureContour("command-bar-unfiltered");
            double commandWidth=app.Settings.PanelWidth,commandScale=app.Settings.PanelScale;
            var longCommand=new Shortcut{Name="Diagnostic long shortcut",Target="https://example.com/"+new string('w',240)};
            try
            {
                app.Settings.Shortcuts.Add(longCommand);app.Settings.PanelWidth=340;app.Settings.PanelScale=1.25;app.ChangeSettings();await WaitUntil(()=>!overlay.IsAnimating);
                Check("Long command details remain bounded at narrow width and large content scale",CommandsFit());CaptureContour("command-bar-narrow");
                var commandScroll=OuterScroll(Entry("Search commands"));commandScroll.ScrollToEnd();await Task.Delay(100);
                var lastCommand=ActionButton(longCommand.Name);var lastBounds=lastCommand.TransformToAncestor(commandScroll).TransformBounds(new Rect(lastCommand.RenderSize));
                Check("Last command can scroll fully into view",lastBounds.Top>=-0.5 && lastBounds.Bottom<=commandScroll.ActualHeight+0.5);
                app.Settings.PanelWidth=640;app.Settings.PanelScale=1.25;app.ChangeSettings();await WaitUntil(()=>!overlay.IsAnimating);Check("Wide enlarged command controls retain scrollbar clearance",CommandsFit());CaptureContour("command-bar-wide-scaled");
            }
            finally{app.Settings.Shortcuts.Remove(longCommand);app.Settings.PanelWidth=commandWidth;app.Settings.PanelScale=commandScale;app.ChangeSettings();}
            search=Entry("Search commands");search.Text="clean links";
            var shortcutWidget=app.Settings.Widgets.Single(w=>w.Id=="shortcuts");var toolsWidget=app.Settings.Widgets.Single(w=>w.Id=="tools");bool shortcutsEnabled=shortcutWidget.Enabled,toolsEnabled=toolsWidget.Enabled;var sentinel=new Shortcut{Name="Diagnostic shortcut sentinel",Target="https://example.com/never-open"};
            try
            {
                app.Settings.Shortcuts.Add(sentinel);search.Text="Diagnostic shortcut sentinel";Check("Enabled saved shortcuts remain searchable",Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==sentinel.Name));
                shortcutWidget.Enabled=false;toolsWidget.Enabled=false;app.ChangeSettings();Entry("Search commands").Text="Diagnostic shortcut sentinel";
                Check("Disabled shortcuts omit saved launch commands",!Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==sentinel.Name));
                Entry("Search commands").Text="clean links";Check("Disabled tools omit their commands",!Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Clean links"));
                Entry("Search commands").Text="Open Settings";Check("Settings remains searchable with shortcuts and tools disabled",Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Open Settings"));
            }
            finally{app.Settings.Shortcuts.Remove(sentinel);shortcutWidget.Enabled=shortcutsEnabled;toolsWidget.Enabled=toolsEnabled;app.ChangeSettings();Entry("Search commands").Text="clean links";}
            Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Clean links").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);
            Check("Command selection opens the requested tool",!overlay.CommandsOpen && overlay.ActivePage=="tools" && Descendants(overlay).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="Link to clean"));
            var linkInput=Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Link to clean");linkInput.Text="https://example.com/product?sku=42&utm_source=NabyScape#info";
            Check("Clean links previews preserved functional query and fragment without copying",Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Cleaned link").Text=="https://example.com/product?sku=42#info");CaptureContour("tools-links");
            await Task.Delay(350);overlay.UpdateLayout();var copyLink=ActionButton("Copy cleaned link");Check("Clean-link copy action fits the compact viewport",copyLink.TranslatePoint(new Point(0,copyLink.ActualHeight),overlay).Y<overlay.PaintedHeight-12);
            var textHost=(ScrollViewer)linkInput.Template.FindName("PART_ContentHost",linkInput);var glyphs=new FormattedText(linkInput.Text,System.Globalization.CultureInfo.CurrentCulture,linkInput.FlowDirection,new Typeface(linkInput.FontFamily,linkInput.FontStyle,linkInput.FontWeight,linkInput.FontStretch),linkInput.FontSize,linkInput.Foreground,VisualTreeHelper.GetDpi(linkInput).PixelsPerDip);
            Check("Clean-link input viewport fits its rendered text height",textHost.ViewportHeight>=glyphs.Height && linkInput.GetRectFromCharacterIndex(0).Height<=textHost.ViewportHeight);report.Add($"INFO  Clean-link text viewport {textHost.ViewportHeight:0.00} DIP; formatted text {glyphs.Height:0.00} DIP.");CaptureContour("tools-links-text-viewport");
            foreach(var state in new[]{("invalid","not a URL",false),("empty","",false),("unchanged","https://example.com/product?sku=42#info",true)})
            {linkInput.Text=state.Item2;await Task.Delay(350);overlay.UpdateLayout();double copyBottom=copyLink.TranslatePoint(new Point(0,copyLink.ActualHeight),overlay).Y;Check("Clean-link "+state.Item1+" state recalculates the compact copy viewport",copyLink.IsEnabled==state.Item3 && copyBottom<overlay.PaintedHeight-12 && copyLink.TranslatePoint(new Point(0,copyLink.ActualHeight),outerScroll).Y<=outerScroll.ActualHeight && overlay.PaintedHeight<=408.1 && outerScroll.VerticalOffset==0);report.Add($"INFO  Clean-link {state.Item1}: Copy bottom {copyBottom:0.00}, contour {overlay.PaintedHeight:0.00}, scroll offset {outerScroll.VerticalOffset:0.00} DIP.");CaptureContour("tools-links-"+state.Item1);}
            overlay.ShowTools("calendar",false);await Task.Delay(300);Check("Calendar exposes a local event action and remains compact",Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Add local event") && overlay.PaintedHeight<=408.1);CaptureContour("tools-calendar");
            Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Toggle calendar month").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);Check("Month grid reveals dates within the same compact panel",Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==DateTime.Today.ToString("dddd, MMMM d, yyyy")) && overlay.PaintedHeight<=408.1);CaptureContour("calendar-month");Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Toggle calendar month").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Add local event").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Event title").Text="Local calendar fixture";Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Meeting URL").Text="https://example.com/meeting";
            Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Save event").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
            Check("Calendar editor persists a local event and exposes a manual meeting action",app.Store.Load<List<CalendarEvent>>("calendar").Single().Title=="Local calendar fixture" && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Open meeting"));CaptureContour("calendar-event");
            Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Delete event Local calendar fixture").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Calendar delete persists without retaining the fixture",app.Calendar.Events.Count==0 && app.Store.Load<List<CalendarEvent>>("calendar").Count==0);
            foreach(var boundary in new[]{("High date fixture","9999-12-30","9999-12-31","Next calendar month"),("Low date fixture","0001-01-01","0001-01-02","Previous calendar month")})
            {
                Click("Add local event");Entry("Event title").Text=boundary.Item1;Entry("Event start date").Text=boundary.Item2;Entry("Event end date").Text=boundary.Item3;Click("Save event");await Task.Delay(250);overlay.UpdateLayout();
                Check("Calendar accepts UI date boundary and retains manual delete: "+boundary.Item2,app.Calendar.Events.Any(e=>e.Title==boundary.Item1) && ActionButton("Delete event "+boundary.Item1).IsEnabled && !ActionButton(boundary.Item4).IsEnabled);
                Click("Toggle calendar month");await Task.Delay(250);Check("Calendar boundary month grid renders without overflow: "+boundary.Item2,overlay.PaintedHeight<=408.1 && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==DateTime.ParseExact(boundary.Item3,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture).ToString("dddd, MMMM d, yyyy")));CaptureContour(boundary.Item1.StartsWith("High")?"calendar-high-date":"calendar-low-date");
                if(boundary.Item1.StartsWith("High")){Click(new DateTime(9999,12,31).ToString("dddd, MMMM d, yyyy"));Check("Last supported calendar day renders its overlapping agenda",ActionButton("Delete event "+boundary.Item1).IsEnabled);}else Click("Toggle calendar month");
                Click("Delete event "+boundary.Item1);Click("Today");
            }
            Check("Calendar boundary fixtures are deleted and Today restores current month",app.Calendar.Events.Count==0 && ActionButton("Previous calendar month").IsEnabled && ActionButton("Next calendar month").IsEnabled);
            overlay.ShowTools("awake",false);await Task.Delay(300);Check("Keep awake is off until explicitly started",!app.Awake.Active && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Start keep awake"));CaptureContour("tools-awake");
            try{app.Awake.Start(TimeSpan.FromSeconds(2),false,DateTimeOffset.UtcNow);Check("Native keep-awake request succeeds and provides a stop action",app.Awake.Active && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Stop keep awake"));Check("Keep-awake expiry releases the request and restores Start",await WaitUntil(()=>!app.Awake.Active && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Start keep awake"),6000));}finally{app.Awake.Stop();}
            overlay.Collapse();await Task.Delay(800);app.Settings.SmartActivities=true;var beforeAutomatic=Native.GetForegroundWindow();overlay.TestAutomaticActivity();await Task.Delay(800);
            Check("Automatic activity preview does not take focus",overlay.IsPeeking && !overlay.IsExpanded && (Native.GetForegroundWindow()!=new WindowInteropHelper(overlay).Handle || beforeAutomatic==new WindowInteropHelper(overlay).Handle));overlay.Reveal(false);overlay.TestAutomaticActivity();Check("Automatic activities do not replace an open panel",overlay.IsExpanded);overlay.Collapse();app.Settings.SmartActivities=false;
            app.Settings.Performance="Hemat";app.ChangeSettings();overlay.Reveal(false);await Task.Delay(300);Check("Economy profile caps interactive animation at 60",Motion.FrameLimit<=60);overlay.Collapse();app.Settings.Performance="Auto";app.ChangeSettings();
            Check("Global hotkey registers or reports a conflict",overlay.HasGlobalHotkey || overlay.HotkeyError.Length>0);
            app.Session.Timer.Start(2,DateTimeOffset.UtcNow);app.SaveSession();overlay.Collapse();await Task.Delay(3100);
            Check("Timer completes with panel closed",await WaitUntil(()=>!app.Session.Timer.Running && app.Session.Timer.RemainingSeconds==0));
            app.Session.Focus.Start(1,DateTimeOffset.UtcNow.AddSeconds(-2),"work");app.CheckCountdowns();
            Check("Work automatically enters remaining break",app.Session.Focus.Running && app.Session.Focus.Phase=="break");
            app.Session.Focus.Start(1,DateTimeOffset.UtcNow.AddHours(-2),"work");app.CheckCountdowns();
            Check("Sleep beyond work and break completes focus",!app.Session.Focus.Running && app.Session.Focus.Phase=="");
            app.Session.Focus.Start(1,DateTimeOffset.UtcNow.AddSeconds(-2),"break");app.CheckCountdowns();
            Check("Break completion returns to ready state",!app.Session.Focus.Running && app.Session.Focus.Phase=="");
            app.Settings.Monitor="nonexistent-monitor";app.ChangeSettings();
            Check("Missing monitor falls back to primary",Native.ChooseScreen(app.Settings).Primary);
            app.Settings.Preset="Mac";app.Settings.Top=0;app.ChangeSettings();await Task.Delay(250);Capture(overlay,"island-mac");
            Native.GetWindowRect(new WindowInteropHelper(overlay).Handle,out var bounds);
            Check("Mac notch attaches to top edge",Math.Abs(bounds.Top-Native.ChooseScreen(app.Settings).Bounds.Top)<=1);
            app.Settings.Preset="iPhone";app.Settings.Top=12;app.Settings.Monitor="";app.ChangeSettings();
            app.ShowSettings();await Task.Delay(400);
            var settings=Application.Current.Windows.OfType<SettingsWindow>().Single();Capture(settings,"settings-appearance");
            var settingsNav=Descendants(settings).Single(node=>AutomationProperties.GetName(node)=="Settings navigation");
            for(int revisit=0;revisit<3;revisit++)foreach(string section in new[]{"Controls","Appearance","Controls","System","Appearance"})Descendants(settingsNav).OfType<Button>().Single(b=>b.Content?.ToString()==section).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Settings status labels safely reattach on repeated Appearance and Controls visits",Descendants(settings).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Performance profile"));
            settings.UpdateLayout();Check("Appearance switches have visible concrete explanations",Descendants(settings).OfType<TextBlock>().Any(t=>t.Text==SettingsWindow.SettingDescription("Black glass")));

            bool SettingsControlsFit()
            {
                settings.UpdateLayout();var viewport=Descendants(settings).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");var presenter=Descendants(viewport).OfType<ScrollContentPresenter>().First();
                return Descendants(viewport).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e is Button or Slider or ColorField).All(e=>{var r=e.TransformToAncestor(presenter).TransformBounds(new Rect(e.RenderSize));bool fits=r.Left>=-.5 && r.Right<=presenter.ActualWidth+.5;if(!fits)report.Add($"INFO  Settings overflow {e.GetType().Name} {r} versus {presenter.RenderSize}");return fits;});
            }
            Check("Settings shape and color controls stay inside their reserved viewport",SettingsControlsFit());
            Check("Settings navigation is English",settings.Title=="NabyScape · Settings" && new[]{"Appearance","Content","Controls","Launch","Application"}.All(label=>Descendants(settings).OfType<Button>().Any(b=>b.Content?.ToString()==label)));
            Descendants(settings).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Island color color picker").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));settings.UpdateLayout();
            foreach(var expander in Descendants(settings).OfType<Expander>().Where(e=>e.Header?.ToString()=="Color values · HEX / RGB").ToArray())expander.IsExpanded=true;settings.UpdateLayout();
            var islandHex=Descendants(settings).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Island color hex");islandHex.Text="#182028";await Task.Delay(300);
            Check("Integrated color picker persists a valid hex color",app.Store.Load<Settings>("settings").Background=="#182028");islandHex.Text="#000000";await Task.Delay(300);Capture(settings,"settings-color-picker");
            Check("Color picker exposes hue and keyboard RGB controls",Descendants(settings).OfType<Slider>().Any(t=>AutomationProperties.GetName(t)=="Island color hue") && Descendants(settings).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="Island color red"));
            var profile=Descendants(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Performance profile");
            Check("Performance choices show English labels with the saved Auto selection",profile.Items.Cast<string>().SequenceEqual(new[]{"Auto","Eco","Maximum"}) && profile.SelectedIndex==0);
            profile.SelectedIndex=1;Check("English Eco choice preserves the existing saved profile ID",await WaitUntil(()=>app.Store.Load<Settings>("settings").Performance=="Hemat",3000));profile.SelectedIndex=0;await Task.Delay(300);
            var interactionButton=Descendants(settings).OfType<Button>().First(b=>b.Content?.ToString()=="Content");interactionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);Check("Hover page toggles and reorder controls remain inside Settings",SettingsControlsFit());Check("Hover visibility switches leave space before reorder actions",Descendants(settings).OfType<CheckBox>().Where(c=>AutomationProperties.GetName(c).EndsWith("in hover previews")).All(c=>{var grid=(Grid)c.Parent;var actions=grid.Children.OfType<StackPanel>().Single();return c.TransformToAncestor(grid).TransformBounds(new Rect(c.RenderSize)).Right+6<=actions.TransformToAncestor(grid).TransformBounds(new Rect(actions.RenderSize)).Left;}));Capture(settings,"settings-interaction");
            var widgetButton=Descendants(settings).OfType<Button>().First(b=>b.Content?.ToString()=="Content");widgetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);Capture(settings,"settings-widgets");
            Check("All widget switches are available",Settings.Names.Values.All(name=>Descendants(settings).OfType<CheckBox>().Any(c=>AutomationProperties.GetName(c)==name)));
            var pickedApps=new List<Shortcut>();var picker=new AppPicker(settings,item=>pickedApps.Add(item),new FakeAppCatalog(new InstalledApp("Fixture Mail","shell:AppsFolder\\Diagnostic.Mail_abc!App","Diagnostic.Mail_abc!App"),new InstalledApp("Fixture Editor",@"C:\Fixtures\Editor.lnk")));
            picker.Show();await Task.Delay(150);var pickerSearch=Descendants(picker).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Search installed apps");pickerSearch.Text="mail";await Task.Delay(80);
            Check("Installed-app picker searches injected entries without launching",Descendants(picker).OfType<ListBox>().Single().Items.Count==1);Capture(picker,"app-picker-fixture");
            Descendants(picker).OfType<Button>().Single(b=>b.Content?.ToString()=="Add selected app").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Installed picker returns the selected persistent app identity",pickedApps.Count==1 && pickedApps[0].AppId=="Diagnostic.Mail_abc!App");
            var explorerIcon=await AppIconCache.GetAsync(new Shortcut{Name="Explorer",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")});
            Check("Native application icons load as frozen bitmaps",explorerIcon!=null && explorerIcon.IsFrozen);
            var discoveredApps=await new WindowsAppCatalog().DiscoverAsync(System.Threading.CancellationToken.None);report.Add($"INFO  Read-only installed app discovery: {discoveredApps.Count} entries. No apps launched.");Check("Native installed catalog has safe distinct identities",discoveredApps.Select(item=>item.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count()==discoveredApps.Count);
            var nativePicker=new AppPicker(settings,_=>{},new FakeAppCatalog(discoveredApps.Take(30).ToArray()));nativePicker.Show();await Task.Delay(1300);Capture(nativePicker,"app-picker");nativePicker.Close();

            var firstUp=Descendants(settings).OfType<Button>().First(b=>AutomationProperties.GetName(b)=="Move down Media");firstUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);
            Check("Widget reorder is persisted",app.Store.Load<Settings>("settings").Widgets[1].Id=="media");
            Button SettingsAction(string name)=>Descendants(settings).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name || b.Content?.ToString()==name);
            SettingsAction("Layout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);foreach(var expander in Descendants(settings).OfType<Expander>().ToArray())expander.IsExpanded=true;settings.UpdateLayout();
            var panelWidth=Descendants(settings).OfType<Slider>().Single(control=>AutomationProperties.GetName(control)=="Panel width");panelWidth.Value=460;
            var contentSize=Descendants(settings).OfType<Slider>().Single(control=>AutomationProperties.GetName(control)=="Content size");contentSize.Value=110;await Task.Delay(400);
            Check("Settings provides editable numeric values beside layout sliders",Descendants(settings).OfType<TextBox>().Any(control=>AutomationProperties.GetName(control)=="Panel width value" && control.Text=="460"));
            Check("Panel width and content scale are persisted",app.Store.Load<Settings>("settings").PanelWidth==460 && Math.Abs(app.Store.Load<Settings>("settings").PanelScale-1.1)<.001);
            SettingsAction("Preview Panel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Capture(settings,"settings-layout");overlay.Reveal(false);await Task.Delay(800);Check("Custom panel size reaches the native overlay",Math.Abs(overlay.PaintedWidth-460)<.1 && overlay.PaintedHeight<=app.Settings.PanelMaxHeight+.1);
            app.Settings.PanelWidth=384;app.Settings.PanelScale=1;app.ChangeSettings();
            foreach(string mode in new[]{"IconsLabels","Icons","Text"})
            {
                app.Settings.CategoryStyle=mode;app.Settings.LaunchStyle=mode;app.ChangeSettings();overlay.SelectPage("access");await Task.Delay(400);CaptureContour("launch-"+mode);
                var mediaCategory=ActionButton("Media");Check("Category presentation remains accessible: "+mode,mediaCategory.ToolTip?.ToString()=="Media" && Descendants(mediaCategory).OfType<TextBlock>().Any()==(mode!="Icons"));
                var firstShortcut=ActionButton(app.Settings.Shortcuts[0].Name);Check("Launch presentation respects its label style: "+mode,Descendants(firstShortcut).OfType<TextBlock>().Any(t=>t.IsVisible)==(mode!="Icons" || !Descendants(firstShortcut).OfType<Image>().Any(i=>i.Source!=null)));
            }
            app.Settings.CategoryStyle="IconsLabels";app.Settings.LaunchStyle="IconsLabels";app.Settings.BoldMediaTitle=false;app.Settings.BoldHeadings=true;app.ChangeSettings();overlay.ShowCommands(false);await Task.Delay(100);
            Check("Bold heading option affects tool titles independently",Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="Command bar" && t.FontWeight==FontWeights.SemiBold));
            Click("Back");app.Settings.BoldMediaTitle=true;app.Settings.BoldHeadings=false;app.ChangeSettings();overlay.SelectPage("media");

            SettingsAction("System").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);Capture(settings,"settings-system");Check("Monitor and clock options are available",Descendants(settings).OfType<CheckBox>().Count()==6);
            SettingsAction("Audio").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);Capture(settings,"settings-audio");Check("Settings has a direct mixer action",Descendants(settings).OfType<Button>().Any(b=>b.Content?.ToString()=="Open volume mixer"));
            var generalButton=Descendants(settings).OfType<Button>().First(b=>b.Content?.ToString()=="Application");generalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);Capture(settings,"settings-application");
            var workArea=Native.ChooseScreen(app.Settings).WorkingArea;double workScale=Native.Scale(Native.ChooseScreen(app.Settings));
            Check("Settings window fits its work area",settings.ActualWidth<=workArea.Width/workScale && settings.ActualHeight<=workArea.Height/workScale);
            settings.Width=640;settings.Height=640;await Task.Delay(180);settings.UpdateLayout();Capture(settings,"settings-narrow");
            var previewAtNarrow=Descendants(settings).OfType<TextBlock>().First(t=>t.Text=="Live preview");
            var narrowForm=Descendants(settings).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");
            Check("Narrow Settings stacks preview above its controls",previewAtNarrow.TranslatePoint(new Point(),settings).Y<narrowForm.TranslatePoint(new Point(),settings).Y && narrowForm.ActualWidth>300);
            settings.Width=480;settings.Height=380;await Task.Delay(220);settings.UpdateLayout();Capture(settings,"settings-short");
            var formViewport=Descendants(settings).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");
            Check("Short Settings preserves a usable form viewport",formViewport.ActualHeight>=119 && formViewport.ActualWidth>=300);
            settings.Width=1100;settings.Height=780;
            settings.Close();
            var savedWidgets=app.Settings.Widgets.Select(w=>(w.Id,w.Enabled)).ToArray();
            app.Settings.CompactLayout=false;app.Settings.PanelWidth=520;app.Settings.PanelMaxHeight=320;app.ChangeSettings();
            overlay.OpenMixer();await Task.Delay(500);overlay.UpdateLayout();
            bool ClassicWidgetsVisible(OverlayWindow window)=>Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="Activity" && t.IsVisible);
            Check("Compact to classic mixer attaches a visible mixer without changing widget preferences",!app.Settings.CompactLayout && overlay.ActivePage=="mixer" && ActionButton("Back to widgets").IsVisible && savedWidgets.SequenceEqual(app.Settings.Widgets.Select(w=>(w.Id,w.Enabled))));
            Check("Classic mixer honors custom panel geometry",Math.Abs(overlay.PaintedWidth-520)<.1 && Math.Abs(overlay.PaintedHeight-320)<.1);CaptureContour("classic-mixer");
            Click("Back to widgets");await Task.Delay(500);overlay.UpdateLayout();
            Check("Mixer Back returns to classic scrolling widgets",ClassicWidgetsVisible(overlay) && overlay.ActivePage!="mixer" && Math.Abs(overlay.PaintedWidth-520)<.1 && Math.Abs(overlay.PaintedHeight-320)<.1);
            var classicScroll=Descendants(overlay).OfType<ScrollViewer>().First();classicScroll.ScrollToEnd();await Task.Delay(100);
            Check("Classic widgets remain scrollable at a custom height",classicScroll.ScrollableHeight>0 && classicScroll.VerticalOffset>0);
            overlay.OpenMixer();overlay.Collapse();overlay.Reveal(false);await Task.Delay(500);overlay.UpdateLayout();
            Check("Collapsing the mixer restores classic content on reopen",ClassicWidgetsVisible(overlay) && overlay.ActivePage!="mixer");
            overlay.OpenMixer();app.ChangeSettings();await Task.Delay(150);overlay.UpdateLayout();
            Check("Applying settings clears the transient classic mixer",ClassicWidgetsVisible(overlay) && overlay.ActivePage!="mixer");
            var freshClassic=new OverlayWindow(app){DiagnosticRun=true};
            try
            {
                freshClassic.Show();freshClassic.OpenMixer();await Task.Delay(500);freshClassic.UpdateLayout();
                var freshBack=Descendants(freshClassic).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Back to widgets");
                Check("A fresh classic overlay opens its attached mixer",freshClassic.ActivePage=="mixer" && freshBack.IsVisible && !app.Settings.CompactLayout);
                freshBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);freshClassic.UpdateLayout();
                Check("A fresh classic mixer returns to widgets",ClassicWidgetsVisible(freshClassic));freshClassic.Collapse();await Task.Delay(350);
            }
            finally{freshClassic.Cleanup();freshClassic.Hide();}
            app.Settings.CompactLayout=true;app.Settings.PanelWidth=384;app.Settings.PanelMaxHeight=408;app.ChangeSettings();
            overlay.OpenMixer();await Task.Delay(300);Check("Mixer opening animation settles before capture",await WaitUntil(()=>!overlay.IsAnimating));overlay.UpdateLayout();CaptureContour("audio-mixer");
            var nativeMixer=new AudioMixer().Snapshot();
            Check("Mixer renders app rows when native sessions are available",nativeMixer.Apps.Count==0 || Descendants(overlay).OfType<Slider>().Any(control=>AutomationProperties.GetName(control).StartsWith("App ")));
            Click("Show output volumes");await Task.Delay(200);Check("Mixer output animation settles before capture",await WaitUntil(()=>!overlay.IsAnimating));overlay.UpdateLayout();CaptureContour("audio-outputs");
            Check("Mixer renders output rows when native endpoints are available",nativeMixer.Outputs.Count==0 || Descendants(overlay).OfType<Slider>().Any(control=>AutomationProperties.GetName(control).StartsWith("Output ")));Click("Show app volumes");
            Check("Mixer omits bulk step controls",!Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b).StartsWith("All apps ") || AutomationProperties.GetName(b).StartsWith("All outputs ")));
            Check("Mixer opens without a live audio write",overlay.ActivePage=="mixer" && Descendants(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Back to media"));
            overlay.SelectPage("media");overlay.OpenAudioMenu();await Task.Delay(150);if(overlay.AudioPopup is ContextMenu audioPopup && audioPopup.ActualWidth>0){Capture(audioPopup,"audio-popup");Check("Audio popup uses the custom dark border without a native gutter",audioPopup.Template.FindName("PART_Popup",audioPopup)==null && audioPopup.Background is SolidColorBrush brush && brush.Color.R<50);audioPopup.IsOpen=false;}
            overlay.SelectPage("status");bool monitorReady=await WaitUntil(()=>overlay.MonitorReady,8000);Check("System monitor asynchronously produces real successive samples",monitorReady);
            await WaitUntil(()=>!overlay.IsAnimating);overlay.UpdateLayout();CaptureContour("system-monitor");Check("Detailed monitor exposes memory and uptime",Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="Memory") && Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="System uptime"));
            foreach(string monitorPage in new[]{"hardware","network","storage","processes"}){overlay.SelectMonitorTab(monitorPage);await Task.Delay(80);CaptureContour("system-"+monitorPage);if(monitorPage=="hardware"){overlay.DiagnosticScrollHardware(true);await Task.Delay(100);CaptureContour("system-hardware-details");overlay.DiagnosticScrollHardware(false);}}
            var monitorText=Descendants(overlay).OfType<TextBlock>().First(t=>t.Text.StartsWith("Working sets"));var monitorScroll=OuterScroll(monitorText);monitorScroll.ScrollToEnd();await Task.Delay(100);CaptureContour("system-details");
            Check("Monitor detail tabs stay outside the scrolling rows",Descendants(overlay).OfType<Button>().Where(b=>AutomationProperties.GetName(b).StartsWith("Monitor ")).Count()==5 && !Descendants(monitorScroll).OfType<Button>().Any(b=>AutomationProperties.GetName(b).StartsWith("Monitor ")));
            overlay.SelectMonitorTab("overview");
            async Task CheckMonitorStopped(string context)
            {
                Check(context+" immediately clears detailed polling and its baseline",!overlay.MonitorPolling && !overlay.MonitorReady);
                Check(context+" drains the previous worker",await WaitUntil(()=>!overlay.MonitorBusy,5000));
                int reads=overlay.MonitorReads,results=overlay.MonitorResults;
                await Task.Delay(2300);
                Check(context+" starts no hidden reads or renders",overlay.MonitorReads==reads && overlay.MonitorResults==results && !overlay.MonitorPolling);
            }
            var monitorGate=new TaskCompletionSource<bool>();
            try
            {
                overlay.MonitorDeliveryGate=monitorGate.Task;
                overlay.SelectPage("media");overlay.SelectPage("status");
                Check("Status starts a worker for the quick-return lifecycle check",await WaitUntil(()=>overlay.MonitorBusy));
                int results=overlay.MonitorResults,rejected=overlay.MonitorRejected,reads=overlay.MonitorReads;
                overlay.ShowCommands(false);
                Check("Commands immediately invalidate detailed Status polling",overlay.CommandsOpen && !overlay.MonitorPolling && !overlay.MonitorReady);
                Descendants(overlay).OfType<Button>().Single(b=>b.Content?.ToString()=="Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Check("Returning to Status keeps one worker and a fresh baseline",!overlay.CommandsOpen && overlay.ActivePage=="status" && overlay.MonitorBusy && overlay.MonitorReads==reads && !overlay.MonitorReady && overlay.MonitorResults==results);
                monitorGate.TrySetResult(true);overlay.MonitorDeliveryGate=null;
                Check("Quick return rejects the previous generation result",await WaitUntil(()=>overlay.MonitorRejected>rejected));
                Check("Status resumes with fresh successive samples after commands",await WaitUntil(()=>overlay.MonitorReady,8000));
            }
            finally{monitorGate.TrySetResult(true);overlay.MonitorDeliveryGate=null;}
            overlay.ShowCommands(false);await CheckMonitorStopped("Command bar");
            Descendants(overlay).OfType<Button>().Single(b=>b.Content?.ToString()=="Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Back from commands resumes detailed Status",await WaitUntil(()=>overlay.MonitorReady,8000));
            app.Settings.CompactLayout=false;app.ChangeSettings();await CheckMonitorStopped("Classic layout");
            Check("Classic layout retains its lightweight CPU and RAM card",Descendants(overlay).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text.StartsWith("CPU  ")) && Descendants(overlay).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text.StartsWith("RAM  ")));
            app.Settings.CompactLayout=true;app.ChangeSettings();
            Check("Returning to compact Status resumes successive samples",await WaitUntil(()=>overlay.MonitorReady,8000));
            overlay.Collapse();await CheckMonitorStopped("Collapsed panel");
            overlay.Reveal(false);Check("Reopening Status resumes successive samples",await WaitUntil(()=>overlay.MonitorReady,8000));
            overlay.Hide();await CheckMonitorStopped("Hidden window");
            overlay.Show();Check("Showing Status resumes successive samples",await WaitUntil(()=>overlay.MonitorReady,8000));
            overlay.Collapse();
            double feedbackWidth=app.Settings.Width;bool feedbackClockFormat=app.Settings.Clock24Hour;
            app.Settings.Width=180;app.Settings.Clock24Hour=false;app.ChangeSettings();await Task.Delay(100);
            overlay.DiagnosticFeedFeedback(false,100);await Task.Delay(250);Check("Minimum-width closed feedback preserves full percentage with a 12-hour clock",overlay.FeedbackTextFits && overlay.HeaderLabel.Text=="100%");CaptureContour("closed-feedback-narrow");
            overlay.DiagnosticFeedFeedback(false,0,true);await Task.Delay(1100);Check("Ordinary status updates do not squeeze minimum-width mute feedback",overlay.FeedbackTextFits && overlay.HeaderLabel.Text=="Muted");
            await Task.Delay(1200);bool feedbackExpired=await WaitUntil(()=>overlay.FeedbackHasExpired,4000);Check("Closed feedback expires and restores configured clock",feedbackExpired);if(!feedbackExpired)report.Add("INFO  "+overlay.FeedbackDiagnosticState);
            app.Settings.Width=feedbackWidth;app.Settings.Clock24Hour=feedbackClockFormat;app.ChangeSettings();
            overlay.DiagnosticFeedFeedback(false,42);await Task.Delay(200);Check("Closed volume feedback does not open a panel",!overlay.IsExpanded && !overlay.IsPeeking && overlay.HeaderLabel.Text=="42%");CaptureContour("closed-volume");
            await Task.Delay(1200);overlay.DiagnosticFeedFeedback(false,43);await Task.Delay(1100);Check("Repeated volume updates renew one closed indicator",overlay.HeaderLabel.Text=="43%" && !overlay.IsExpanded);
            overlay.DiagnosticFeedFeedback(true,70);await Task.Delay(200);CaptureContour("closed-brightness");Check("Brightness feedback uses injected real-value path without device writes",overlay.HeaderLabel.Text=="70%" && !overlay.IsExpanded);
            overlay.ToggleVisibility();overlay.DiagnosticFeedFeedback(false,99);Check("Manual hiding suppresses closed feedback",overlay.UserHidden && !overlay.IsVisible);overlay.ToggleVisibility();
            Check("Visibility shortcut registers or reports a conflict",overlay.HasVisibilityHotkey || overlay.VisibilityHotkeyError.Length>0);
            var originalViews=app.Settings.HoverViews.Select(view=>new WidgetSetting{Id=view.Id,Enabled=view.Enabled}).ToList();
            app.Settings.HoverViews=originalViews.OrderByDescending(view=>view.Id=="notes").ToList();app.ChangeSettings();overlay.PreviewActivity();overlay.SelectPeek("notes");overlay.NavigatePeek(1);Check("Custom hover ordering is used by navigation",overlay.HoverDeckIds[0]=="notes" && overlay.PeekPage=="media");overlay.Collapse();app.Settings.HoverViews=originalViews;app.ChangeSettings();

            foreach(string fixtureKind in new[]{"fullscreen","maximized"})
            {
                var fixtureScreen=Native.ChooseScreen(app.Settings);double fixtureScale=Native.Scale(fixtureScreen);var fixtureBounds=fixtureScreen.Bounds;
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
                foreach(string arg in new[]{"--window-fixture",fixtureKind,(fixtureBounds.Left/fixtureScale).ToString(System.Globalization.CultureInfo.InvariantCulture),(fixtureBounds.Top/fixtureScale).ToString(System.Globalization.CultureInfo.InvariantCulture),(fixtureBounds.Width/fixtureScale).ToString(System.Globalization.CultureInfo.InvariantCulture),(fixtureBounds.Height/fixtureScale).ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
                using var external=Process.Start(start)!;
                try
                {
                    bool ready=await WaitUntil(()=>{external.Refresh();return external.MainWindowHandle!=IntPtr.Zero;},5000);Check("External "+fixtureKind+" fixture starts",ready);
                    if(ready){await Task.Delay(200);external.Refresh();var handle=external.MainWindowHandle;Check("Automatic hide detects external "+fixtureKind+" on its display",Native.ShouldHideOn(fixtureScreen,handle,true,true));Check("Disabled automatic rules leave external "+fixtureKind+" visible",!Native.ShouldHideOn(fixtureScreen,handle,false,false));
                        bool savedMaximized=app.Settings.HideMaximized;app.Settings.HideMaximized=true;overlay.DiagnosticEnvironment(handle);overlay.CheckEnvironment();await Task.Delay(350);Check("Injected external "+fixtureKind+" tucks the island at the edge",overlay.EnvironmentHidden && overlay.IsVisible && overlay.IsTucked && overlay.VisibleBounds.Height<=4.1 && !overlay.LiveRunning);CaptureContour("auto-tucked-"+fixtureKind);
                        overlay.DiagnosticPointerPresent=true;overlay.RevealTuckedPreview();await Task.Delay(700);overlay.CheckEnvironment();Check("Tucked edge reveals a hover without opening the panel "+fixtureKind,overlay.IsVisible && overlay.IsPeeking && !overlay.IsExpanded && !overlay.IsTucked);CaptureContour("auto-revealed-"+fixtureKind);overlay.DiagnosticPointerPresent=false;overlay.Collapse();await Task.Delay(350);Check("Collapsing the automatic preview re-tucks "+fixtureKind,overlay.IsTucked && overlay.VisibleBounds.Height<=4.1);
                        overlay.ToggleVisibility();Check("Manual hide fully removes a tucked island "+fixtureKind,!overlay.IsVisible && overlay.UserHidden);overlay.ToggleVisibility();await Task.Delay(2100);overlay.CheckEnvironment();Check("Manual show overrides the current external "+fixtureKind,overlay.IsVisible && !overlay.IsExpanded && !overlay.IsTucked && overlay.VisibilityOverride==handle);app.Settings.HideMaximized=savedMaximized;foreach(var other in System.Windows.Forms.Screen.AllScreens.Where(d=>d.DeviceName!=fixtureScreen.DeviceName))Check("External "+fixtureKind+" does not hide another display",!Native.ShouldHideOn(other,handle,true,true));}
                }
                finally{overlay.DiagnosticPointerPresent=false;overlay.DiagnosticEnvironment(null);if(!external.HasExited){external.CloseMainWindow();if(!await Task.Run(()=>external.WaitForExit(1500)))external.Kill();}}
            }
            // A normal borderless app window provides a deterministic fullscreen target on the current monitor.
            var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);
            var full=new Window{Title="NabyScape fullscreen test fixture",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=screen.Bounds.Left/scale,Top=screen.Bounds.Top/scale,Width=screen.Bounds.Width/scale,Height=screen.Bounds.Height/scale,Background=Brushes.DimGray};
            full.Show();full.Activate();await Task.Delay(350);
            var fixtureHandle=new WindowInteropHelper(full).Handle;
            Check("Fullscreen rules exclude NabyScape process windows",!Native.IsFullscreenWindow(screen,fixtureHandle,new WindowInteropHelper(overlay).Handle));
            if(Native.GetForegroundWindow()==fixtureHandle)
            { overlay.CheckEnvironment();Check("Own fullscreen fixture does not hide NabyScape",overlay.IsVisible); }
            else report.Add("MANUAL  Fullscreen auto-hide UI: fixture could not acquire foreground; detection was checked directly.");
            full.Close();await Task.Delay(150);overlay.CheckEnvironment();Check("Overlay returns after fullscreen",overlay.IsVisible);
            foreach(var monitor in System.Windows.Forms.Screen.AllScreens)
            {
                app.Settings.Monitor=monitor.DeviceName;app.ChangeSettings();await Task.Delay(300);
                Native.GetWindowRect(new WindowInteropHelper(overlay).Handle,out var monitorRect);
                Check("Overlay remains inside selected monitor "+monitor.DeviceName,monitorRect.Left>=monitor.Bounds.Left && monitorRect.Right<=monitor.Bounds.Right && monitorRect.Top>=monitor.Bounds.Top && monitorRect.Bottom<=monitor.Bounds.Bottom);
            }
            app.Settings.Monitor="";app.ChangeSettings();
            await MediaFixture.Verify(app,directory,Check,line=>report.Add(line),async()=>
            {
                string originalIcon=app.Settings.ClosedMediaIcon,originalContent=app.Settings.ClosedMediaContent;var originalTimer=app.Session.Timer;var originalFocus=app.Session.Focus;var selected=app.Media.Selected;
                try
                {
                    app.Session.Timer=new();app.Session.Focus=new();app.Settings.ClosedMediaIcon="Artwork";app.ChangeSettings();overlay.Collapse();await Task.Delay(700);
                    Check("Closed island shows native session artwork without opening",overlay.ClosedCoverVisible && !overlay.IsExpanded && !overlay.IsPeeking);CaptureContour("closed-album-cover");
                    app.Settings.ClosedMediaContent="Waveform";app.ChangeSettings();await Task.Delay(700);overlay.UpdateLayout();Check("Compact waveform replaces the title while keeping album artwork",overlay.ClosedTitleWaveVisible && overlay.ClosedCoverVisible && overlay.ClosedTitleWaveWidth>40 && overlay.ClosedTitleWaveWidth<=72.1 && !overlay.IsExpanded);CaptureContour("closed-cover-waveform");
                    double savedWidth=app.Settings.Width;bool savedFormat=app.Settings.Clock24Hour;app.Settings.Width=180;app.Settings.Clock24Hour=false;app.ChangeSettings();await Task.Delay(120);overlay.UpdateLayout();Check("Closed title waveform stays usable at minimum width with a long clock",overlay.ClosedTitleWaveVisible && overlay.ClosedTitleWaveWidth>8);CaptureContour("closed-cover-waveform-narrow");app.Settings.Width=400;app.ChangeSettings();await Task.Delay(120);overlay.UpdateLayout();Check("Large closed islands do not stretch the compact waveform",overlay.ClosedTitleWaveVisible && overlay.ClosedTitleWaveWidth<=72.1);CaptureContour("closed-cover-waveform-wide");app.Settings.Width=savedWidth;app.Settings.Clock24Hour=savedFormat;
                    bool savedReduce=app.Settings.ReduceMotion;app.Settings.ReduceMotion=true;app.ChangeSettings();Check("Reduced motion keeps the selected waveform at a quiet baseline",overlay.ClosedTitleWaveVisible && overlay.ClosedTitleWaveLevel==0);app.Settings.ReduceMotion=savedReduce;app.ChangeSettings();
                    overlay.DiagnosticFeedFeedback(false,42);Check("Volume feedback temporarily replaces album artwork",!overlay.ClosedCoverVisible && !overlay.ClosedTitleWaveVisible && overlay.HeaderLabel.Text=="42%");await WaitUntil(()=>overlay.FeedbackHasExpired,4500);Check("Album artwork and title waveform return after feedback expires",overlay.ClosedCoverVisible && overlay.ClosedTitleWaveVisible);
                    app.Session.Timer.Start(60,DateTimeOffset.UtcNow);app.SaveSession();await Task.Delay(150);Check("Concurrent timer keeps music in the main island",overlay.ClosedCoverVisible && overlay.ClosedTitleWaveVisible);app.Session.Timer=new();app.SaveSession();
                    app.Settings.ClosedMediaContent="Title";app.Settings.ClosedMediaIcon="Waveform";app.ChangeSettings();Check("Song title and waveform icon remain selectable independently",!overlay.ClosedCoverVisible && !overlay.ClosedTitleWaveVisible);
                    app.Settings.ClosedMediaIcon="Artwork";app.Media.Select(null);await app.Media.Refresh();app.ChangeSettings();Check("Missing media falls back without a stale cover",!overlay.ClosedCoverVisible);
                }
                finally{app.Session.Timer=originalTimer;app.Session.Focus=originalFocus;app.Media.Select(selected);await app.Media.Refresh();app.Settings.ClosedMediaIcon=originalIcon;app.Settings.ClosedMediaContent=originalContent;app.ChangeSettings();}
            });
            if(app.Media.Sessions.Count==0)report.Add("MANUAL  Playback commands and session switching: no active media session during diagnostics.");
            else report.Add("INFO  Windows media sessions detected: "+app.Media.Sessions.Count+"; playback not altered.");
            report.Add("INFO  Audio endpoint: "+app.Audio.Device+"; volume="+Math.Round(app.Audio.Volume*100)+"%, mute="+app.Audio.Muted);
            await InteractionDiagnostics.Run(app,overlay,directory,Check);await RefinementDiagnostics.Run(app,overlay,directory,Check);await PremiumDiagnostics.Run(app,overlay,directory,Check);
            report.Add("INFO  Monitors attached: "+System.Windows.Forms.Screen.AllScreens.Length);
            report.Add("MANUAL  Physical monitor disconnect, alternate DPI, battery charging, and audio device hot-plug require hardware checks.");
            overlay.Collapse();
            await Task.Delay(3000); // Allow fullscreen/display transitions to settle before measuring steady idle.
            // Use a longer window to reduce sensitivity to transient post-fixture work.
            // Keep the same 2% budget and retain failed samples in the release history.
            var process=Process.GetCurrentProcess();var before=process.TotalProcessorTime;var watch=Stopwatch.StartNew();await Task.Delay(12000);process.Refresh();
            var percent=(process.TotalProcessorTime-before).TotalMilliseconds/watch.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
            report.Add($"INFO  Idle CPU over {watch.Elapsed.TotalSeconds:0.0}s: {percent:0.00}% of total machine; working set: {process.WorkingSet64/1048576.0:0.0} MiB.");
            Check("Idle CPU below 2% of total machine",percent<2);
            overlay.ToggleVisibility();process.Refresh();before=process.TotalProcessorTime;watch.Restart();await Task.Delay(6000);process.Refresh();
            Check("Hidden overlay stops its activity timer",!overlay.LiveRunning);
            percent=(process.TotalProcessorTime-before).TotalMilliseconds/watch.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
            report.Add($"INFO  Hidden CPU over {watch.Elapsed.TotalSeconds:0.0}s: {percent:0.00}% of total machine.");
        }
        catch(Exception ex) { failures++;report.Add("FAIL  Diagnostics exception: "+ex);app.Store.Log(ex); }
        finally
        {
            System.Windows.Input.InputManager.Current.PreProcessInput-=IsolateInput;
            report.Add("RESULT: "+failures+" failures");File.WriteAllLines(Path.Combine(directory,"integration-report.txt"),report);
            app.Quit();Environment.ExitCode=failures==0?0:1;
        }
    }
    internal static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
