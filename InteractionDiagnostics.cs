using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

/// <summary>Temporary-profile UI diagnostics. Does not launch targets or access the native clipboard.</summary>
internal static class InteractionDiagnostics
{
    internal static async Task Run(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        await RunReviewRegressions(app,overlay,directory,check);
        const BindingFlags privateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        var kinds=new[]{"media","timer","focus","calendar","notes","files","system"};
        var output=Path.Combine(directory,"interactions");Directory.CreateDirectory(output);
        var notes=app.Productivity.Note;var timer=Snapshot(app.Session.Timer);var focus=Snapshot(app.Session.Focus);
        var settings=JsonSerializer.Serialize(app.Settings);bool hidden=overlay.UserHidden;
        bool clipboardEnabled=app.Productivity.ClipboardEnabled,clipboardPaused=app.Productivity.ClipboardPaused;
        var clips=app.Productivity.Clips.ToArray();var originalShelf=app.Productivity.Shelf.ToArray();
        var cache=Path.Combine(app.Store.DirectoryPath,"clipboard-images");
        var images=Directory.Exists(cache)?Directory.GetFiles(cache,"*.png").ToDictionary(p=>p,File.ReadAllBytes):new Dictionary<string,byte[]>();
        var fileNames=kinds.Select(k=>"detached-"+k+".json").Concat(new[]{"settings.json","session.json","productivity.json","activities.json","calendar.json"});
        var savedFiles=fileNames.ToDictionary(n=>Path.Combine(app.Store.DirectoryPath,n),n=>File.Exists(Path.Combine(app.Store.DirectoryPath,n))?File.ReadAllBytes(Path.Combine(app.Store.DirectoryPath,n)):null);
        string? calendarId=null,activityId=null;bool activityStarted=false;
        var created=new List<Window>();
        void Check(string name,bool value)=>check("Interactions: "+name,value);
        IEnumerable<DependencyObject> Tree(DependencyObject root)=>Diagnostics.Descendants(root);
        TextBox Entry(DependencyObject root,string name)=>Tree(root).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
        Button FindButton(DependencyObject root,string name)=>Tree(root).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        Window WindowFor(string kind)=>Application.Current.Windows.Cast<Window>().Single(w=>w.Title=="NabyScape · "+DetachedPreviewWindow.NameFor(kind));
        int Subscribers()
        {
            var handlers=typeof(App).GetField("Updated",privateInstance)?.GetValue(app) as Delegate;
            return handlers?.GetInvocationList().Count(d=>d.Target is DetachedPreviewView)??0;
        }
        string ControlOverflow(Window window)
        {
            var failures=new List<string>();
            foreach(var control in Tree(window).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e is Button or TextBox or Slider or ComboBox or CheckBox))
            {
                FrameworkElement host=window;
                for(var parent=VisualTreeHelper.GetParent(control);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                    if(parent is ScrollContentPresenter presenter){host=presenter;break;}
                var rect=control.TransformToAncestor(host).TransformBounds(new Rect(control.RenderSize));
                if(rect.Left< -1 || rect.Right>host.ActualWidth+1)failures.Add(AutomationProperties.GetName(control)+" "+rect+" / "+host.ActualWidth);
            }
            return string.Join("; ",failures);
        }
        async Task Settle(){await Task.Delay(420);overlay.UpdateLayout();}
        void Capture(FrameworkElement target,string name,string? label=null)
        {
            target.UpdateLayout();int width=Math.Max(1,(int)Math.Ceiling(target.ActualWidth)),height=Math.Max(1,(int)Math.Ceiling(target.ActualHeight));
            var raw=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);raw.Render(target);BitmapSource result=raw;
            if(target is OverlayWindow island)
            {
                width=Math.Max(1,Math.Min(raw.PixelWidth,(int)Math.Ceiling(island.PaintedWidth)));height=Math.Max(1,Math.Min(raw.PixelHeight,(int)Math.Ceiling(island.PaintedHeight)));
                result=new CroppedBitmap(raw,new Int32Rect(Math.Max(0,(raw.PixelWidth-width)/2),0,width,height));
            }
            if(label!=null)
            {
                var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
                {
                    draw.DrawRectangle(UI.Brush("#151515"),null,new Rect(0,0,width,height+48));draw.DrawImage(result,new Rect(0,48,width,height));
                    draw.DrawText(new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.White,1){MaxTextWidth=Math.Max(1,width-16)},new Point(8,7));
                }
                var labeled=new RenderTargetBitmap(width,height+48,96,96,PixelFormats.Pbgra32);labeled.Render(visual);result=labeled;
            }
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        try
        {
            // The diagnostic App constructs this service with nativeClipboard=false. Check before any clipboard fixture.
            bool nativeFree=typeof(ProductivityService).GetField("_nativeClipboard",privateInstance)?.GetValue(app.Productivity) is false;
            Check("clipboard fixture cannot touch the system clipboard",nativeFree);
            if(!nativeFree)throw new InvalidOperationException("Interaction diagnostics require the native-free diagnostic profile.");
            if(Application.Current.Windows.OfType<DetachedPreviewWindow>().Any())throw new InvalidOperationException("Interaction diagnostics require no pre-existing detached windows.");
            if(hidden)overlay.ToggleVisibility();app.Settings.DetachablePreviews=true;app.Settings.ReduceMotion=true;app.Settings.HideFullscreen=false;app.Settings.HideMaximized=false;app.Settings.CompactLayout=true;
            foreach(var widget in app.Settings.Widgets)widget.Enabled=true;foreach(var page in app.Settings.HoverViews)page.Enabled=true;app.ChangeSettings();
            app.Productivity.Note="Ideas for today.\nFinish the draft. Plan the next session.";
            var shelfFile=Path.Combine(output,"shelf-fixture.txt");File.WriteAllText(shelfFile,"Local diagnostic shelf fixture.");app.Productivity.AddFiles(new[]{shelfFile,Path.Combine(output,"missing-file.txt")});
            calendarId=app.Calendar.Add("Local diagnostic review",DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now.AddMinutes(40),"https://example.com/meeting").Id;
            foreach(var kind in kinds)
            {
                app.Previews.Open(kind);var window=WindowFor(kind);created.Add(window);await Settle();
                Check(kind+" opens a visible native resizable window",window.IsVisible && window.ResizeMode==ResizeMode.CanResize && app.Previews.AnyVisible(kind));
                Check(kind+" starts with Always on top off",!window.Topmost && Tree(window).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Always on top").IsChecked==false);
                Check(kind+" default geometry respects minimum dimensions",window.ActualWidth>=window.MinWidth-.1 && window.ActualHeight>=window.MinHeight-.1);
                var overflow=ControlOverflow(window);Check(kind+" default controls clear the scrolling viewport "+overflow,overflow.Length==0);
                Capture(window,"detached-"+kind+"-default");
                var width=window.Width;var height=window.Height;window.Width=window.MinWidth;window.Height=window.MinHeight;await Settle();
                Check(kind+" minimum resize preserves window bounds",Math.Abs(window.ActualWidth-window.MinWidth)<1 && Math.Abs(window.ActualHeight-window.MinHeight)<1);
                overflow=ControlOverflow(window);Check(kind+" minimum controls clear the scrolling viewport "+overflow,overflow.Length==0);
                Capture(window,"detached-"+kind+"-minimum");window.Width=width;window.Height=height;
                app.Previews.Open(kind);Check(kind+" repeated Open reuses the same window",ReferenceEquals(window,WindowFor(kind)) && Application.Current.Windows.Cast<Window>().Count(w=>w.Title==window.Title)==1);
            }
            // Button.Click is the activation path shared by Enter/Space and pointer input.
            // Re-query after every activation: mini-calendar controls are replaced by rendering.
            var calendarWindow=WindowFor("calendar");calendarWindow.Activate();
            foreach(string direction in new[]{"Next month","Previous month"})
            {
                var navigation=FindButton(calendarWindow,direction);navigation.Focus();
                for(int activation=1;activation<=2;activation++)
                {
                    FindButton(calendarWindow,direction).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(40);
                    Check(direction+" activation "+activation+" keeps keyboard focus on replacement button",FindButton(calendarWindow,direction).IsKeyboardFocused);
                }
            }
            var lastDate=Tree(calendarWindow).OfType<Button>().Where(b=>AutomationProperties.GetName(b).StartsWith("Select date ",StringComparison.Ordinal)).OrderBy(b=>AutomationProperties.GetName(b),StringComparer.Ordinal).Last();
            string selectedName=AutomationProperties.GetName(lastDate);lastDate.Focus();lastDate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(40);
            Check("date activation retains keyboard focus on selected replacement day",FindButton(calendarWindow,selectedName).IsKeyboardFocused);
            var selectedDay=DateTime.ParseExact(selectedName.Substring("Select date ".Length),"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var nextDayName="Select date "+selectedDay.AddDays(1).ToString("yyyy-MM-dd");
            FindButton(calendarWindow,selectedName).RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(calendarWindow),0,System.Windows.Input.Key.Right){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});await Task.Delay(40);
            Check("arrow after date activation crosses month and focuses new selected day",FindButton(calendarWindow,nextDayName).IsKeyboardFocused && FindButton(calendarWindow,nextDayName).IsTabStop);
            FindButton(calendarWindow,nextDayName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(40);
            Check("repeated selected-date activation retains current keyboard focus",FindButton(calendarWindow,nextDayName).IsKeyboardFocused);
            Check("all seven visible previews have one Updated subscription each",Subscribers()==7);
            overlay.ToggleVisibility();await Settle();Check("manual hide before explicit Reveal releases preview subscriptions",overlay.UserHidden && Subscribers()==0 && kinds.All(k=>!app.Previews.AnyVisible(k)));
            overlay.Reveal(false);await Settle();Check("explicit Reveal restores all seven open previews and subscriptions",!overlay.UserHidden && kinds.All(app.Previews.AnyVisible) && Subscribers()==7);overlay.Collapse();
            overlay.ToggleVisibility();await Settle();Check("manual overlay hide hides all seven previews",overlay.UserHidden && kinds.All(k=>!app.Previews.AnyVisible(k)) && created.All(w=>!w.IsVisible));
            Check("hidden previews have no Updated subscriptions",Subscribers()==0);
            overlay.ToggleVisibility();await Settle();Check("manual show restores all previously open previews",kinds.All(app.Previews.AnyVisible) && Subscribers()==7);
            var system=WindowFor("system");system.WindowState=WindowState.Minimized;await Settle();Check("minimized preview releases its Updated subscription",Subscribers()==6);app.Previews.Open("system");await Settle();Check("reopening minimized preview restores its subscription",system.WindowState==WindowState.Normal && Subscribers()==7);
            var music=WindowFor("media");music.Close();overlay.ToggleVisibility();overlay.ToggleVisibility();await Settle();Check("manual show does not reopen a closed preview",!app.Previews.AnyVisible("media") && !Application.Current.Windows.Cast<Window>().Any(w=>w.Title=="NabyScape · Music"));
            app.Previews.Open("media");created.Add(WindowFor("media"));
            app.Session.Timer.Start(300,DateTimeOffset.UtcNow);app.SaveSession();WindowFor("timer").Close();Check("closing countdown preview leaves shared countdown running",app.Session.Timer.Running && app.Session.Timer.Remaining(DateTimeOffset.UtcNow)>295);app.Previews.Open("timer");created.Add(WindowFor("timer"));
            var enabled=app.Settings.Widgets.Single(w=>w.Id=="system");enabled.Enabled=false;app.ChangeSettings();Check("disabling a widget closes its preview",!app.Previews.AnyVisible("system") && !Application.Current.Windows.Cast<Window>().Any(w=>w.Title=="NabyScape · System"));enabled.Enabled=true;app.ChangeSettings();app.Previews.Open("system");created.Add(WindowFor("system"));
            overlay.OpenFeaturePage("notes");await Settle();var main=Entry(overlay,"Quick notes");var hover=Entry(overlay,"Preview note draft");var detached=Entry(WindowFor("notes"),"Shared notes document");
            main.Text="Shared document from the main notes editor, with enough text for selection.";await Settle();Check("main note edits synchronize hover and detached document",app.Productivity.Note==main.Text && hover.Text==main.Text && detached.Text==main.Text);
            detached.Select(7,3);main.Text+=" Appended in main.";Check("main edits preserve detached note selection",detached.Text==main.Text && detached.SelectionStart==7 && detached.SelectionLength==3);
            main.Select(5,2);detached.Text+=" Appended detached.";Check("detached note edits synchronize main without destroying its caret",main.Text==detached.Text && main.SelectionStart==5 && main.SelectionLength==2 && hover.Text==detached.Text);
            hover.Select(3,2);detached.Text+=" More text.";Check("detached edits preserve hover note selection",hover.Text==detached.Text && hover.SelectionStart==3 && hover.SelectionLength==2);
            hover.Text="Shared note edited through hover, preserved in every view.";Check("hover note edits synchronize main and detached document",main.Text==hover.Text && detached.Text==hover.Text && app.Productivity.Note==hover.Text);
            Capture(overlay,"notes-main-synchronized");Capture(WindowFor("notes"),"notes-detached-synchronized");overlay.Collapse();overlay.PreviewActivity();overlay.SelectPeek("notes");await Settle();Capture(overlay,"notes-hover-synchronized");
            // Layout scaling is simulated inside WPF. The OS monitor DPI is never changed.
            var noteWindow=WindowFor("notes");var content=(FrameworkElement)noteWindow.Content;var oldTransform=content.LayoutTransform;double oldWidth=noteWindow.Width,oldHeight=noteWindow.Height;
            try
            {
                foreach(double scale in new[]{1d,1.5,2d})
                {
                    content.LayoutTransform=new ScaleTransform(scale,scale);noteWindow.Width=350*scale;noteWindow.Height=330*scale;await Settle();
                    Check($"simulated {scale*100:0}% WPF layout stays finite",double.IsFinite(content.ActualWidth) && double.IsFinite(content.ActualHeight) && content.ActualWidth>0 && content.ActualHeight>0);
                    Capture(noteWindow,$"simulated-layout-{scale*100:0}-percent",$"SIMULATED WPF layout scale {scale*100:0}% · OS monitor DPI unchanged");
                }
            }
            finally{content.LayoutTransform=oldTransform;noteWindow.Width=oldWidth;noteWindow.Height=oldHeight;}
            // Focus-only activity cannot launch an application, open a URL, write audio or hold display awake.
            if(app.Activities.ActiveActivity!=null)throw new InvalidOperationException("Diagnostic activity requires no pre-existing active activity.");
            app.Session.Focus.Reset();var definition=new ActivityDefinition{Name="Study session",Steps=new(){new(){Kind="focus",Minutes=2}}};activityId=definition.Id;
            Check("local focus-only activity definition saves",app.Activities.Save(definition) && app.Store.Load<ActivityActionsData>("activities").Activities.Any(a=>a.Id==definition.Id && a.Steps.All(s=>s.Kind=="focus")));overlay.ShowActivities(false);await Settle();Capture(overlay,"activity-definition");
            FindButton(overlay,"Start activity Study session").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));activityStarted=app.Activities.ActiveActivity?.Id==definition.Id;await Settle();
            Check("activity Start starts shared Focus and reports its step",activityStarted && app.Session.Focus.Running && app.Activities.Results.Count==1 && app.Activities.Results[0].Success);Capture(overlay,"activity-running");
            FindButton(overlay,"Stop activity").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));activityStarted=false;Check("activity Stop releases its owned Focus",app.Activities.ActiveActivity==null && !app.Session.Focus.Running);Capture(overlay,"activity-stopped");
            var p=app.Productivity;p.SetClipboardEnabled(true);p.ClearClips();p.IngestText("Diagnostic plain text");p.IngestText("https://example.com/review?keep=yes&utm_source=diagnostic#section");p.IngestText("#2A6F9B");
            var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{155,111,42,255,42,111,155,255,42,155,111,255,111,42,155,255},8);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var bytes=new MemoryStream()){png.Save(bytes);p.IngestImage(bytes.ToArray());}
            Check("native-free clipboard fixtures classify Text Link Image Color",p.Clips.Select(c=>SmartClipboard.Classify(c).Kind).Distinct().Count()==4);
            overlay.ShowTools("clips",false);await Settle();Capture(overlay,"smart-clipboard-list");
            foreach(var kind in new[]{SmartClipKind.Text,SmartClipKind.Link,SmartClipKind.Image,SmartClipKind.Color})
            {
                FindButton(overlay,"View "+kind).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();Capture(overlay,"smart-clipboard-"+kind.ToString().ToLowerInvariant());
                if(kind==SmartClipKind.Link)Check("cleaned link is previewed before any copy action",Entry(overlay,"Original link").Text.Contains("utm_source=diagnostic") && Entry(overlay,"Cleaned link preview").Text=="https://example.com/review?keep=yes#section");
                if(kind==SmartClipKind.Color)Check("color details expose canonical HEX and RGB",Tree(overlay).OfType<TextBlock>().Any(t=>t.Text=="#2A6F9B · rgb(42, 111, 155)"));
                Check("clipboard "+kind+" exposes contextual details",Tree(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==(kind==SmartClipKind.Image?"Save as PNG…":kind==SmartClipKind.Link?"Copy cleaned link":kind==SmartClipKind.Color?"Copy HEX":"Copy plain text")));
            }
            p.SetClipboardEnabled(false);await Settle();Check("disabling clipboard clears open content and selected detail",p.Clips.Count==0 && !Tree(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b).StartsWith("View ",StringComparison.Ordinal)) && Tree(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Enable history"));Capture(overlay,"smart-clipboard-disabled");
            foreach(var window in created.Distinct().Where(w=>Application.Current.Windows.Cast<Window>().Contains(w)).ToArray())window.Close();Check("closed previews release all Updated subscriptions",Subscribers()==0);
        }
        catch(Exception ex){Check("diagnostic suite completed without exception: "+ex,false);app.Store.Log(ex);}
        finally
        {
            foreach(var window in created.Distinct().Where(w=>Application.Current.Windows.Cast<Window>().Contains(w)).ToArray())window.Close();
            if(activityStarted)app.Activities.Stop();if(activityId!=null)app.Activities.Remove(activityId);if(calendarId!=null)app.Calendar.Remove(calendarId);
            Restore(app.Session.Timer,timer);Restore(app.Session.Focus,focus);app.SaveSession();
            app.Productivity.Note=notes;app.Productivity.SetClipboardEnabled(clipboardEnabled);app.Productivity.ClipboardPaused=clipboardPaused;app.Productivity.Clips.Clear();
            if(images.Count>0)Directory.CreateDirectory(cache);foreach(var image in images)File.WriteAllBytes(image.Key,image.Value);app.Productivity.Clips.AddRange(clips);app.Productivity.Shelf.Clear();app.Productivity.Shelf.AddRange(originalShelf);app.Productivity.Save();
            var original=JsonSerializer.Deserialize<Settings>(settings)!;foreach(var property in typeof(Settings).GetProperties().Where(p=>p.CanRead && p.CanWrite))property.SetValue(app.Settings,property.GetValue(original));app.ChangeSettings();
            if(overlay.UserHidden!=hidden)overlay.ToggleVisibility();overlay.Collapse();
            foreach(var file in savedFiles)if(file.Value==null){if(File.Exists(file.Key))File.Delete(file.Key);}else File.WriteAllBytes(file.Key,file.Value);
        }
    }
    internal static async Task RunReviewRegressions(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var saved=JsonSerializer.Serialize(app.Settings);var timer=Snapshot(app.Session.Timer);var focus=Snapshot(app.Session.Focus);
        bool hidden=overlay.UserHidden;bool diagnostic=overlay.DiagnosticRun;
        object? Field(string name)=>typeof(OverlayWindow).GetField(name,flags)!.GetValue(overlay);
        void Invoke(string name,params object[] args)=>typeof(OverlayWindow).GetMethod(name,flags)!.Invoke(overlay,args);
        void Check(string name,bool value)=>check("Review regression: "+name,value);
        TextBox Entry(string name)=>Diagnostics.Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
        Button Button(string name)=>Diagnostics.Descendants(overlay).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        void Drag(){Invoke("BeginFileDrop");Invoke("ShowShelfDropPreview",(object)new[]{@"C:\fixture-a.txt",@"C:\fixture-b.txt",@"C:\fixture-c.txt",@"C:\fixture-d.txt"});}
        void Cancel()=>Invoke("FinishFileDrop",false);
        try
        {
            overlay.DiagnosticRun=true;if(hidden)overlay.ToggleVisibility();overlay.Collapse();
            app.Settings.ReduceMotion=true;app.Settings.CompactLayout=true;app.Settings.HoverPeek=true;app.Settings.DropReveal=true;app.Settings.HideFullscreen=false;app.Settings.HideMaximized=false;
            foreach(var widget in app.Settings.Widgets)widget.Enabled=true;foreach(var view in app.Settings.HoverViews)view.Enabled=true;app.ChangeSettings();
            foreach(var mode in new[]{"notes","calendar","files"})
            {
                overlay.ShowActivities(false);overlay.OpenFeaturePage(mode);overlay.UpdateLayout();
                Check("Activities to detached "+mode+" route mounts normal Tools body",!overlay.ActivitiesOpen && overlay.ActivePage=="tools" && Diagnostics.Descendants(overlay).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="All tools"));
            }
            foreach(bool compact in new[]{true,false})
            {
                overlay.Collapse();app.Settings.CompactLayout=compact;app.Settings.HoverPeek=false;app.Settings.HoverViews.Single(v=>v.Id=="files").Enabled=false;app.ChangeSettings();Drag();overlay.UpdateLayout();
                var target=(Border?)Field("shelfDropPreview");
                Check("DropReveal independent of HoverPeek and shelf hover toggle, layout "+compact,overlay.IsPeeking && overlay.PeekPage=="files" && target?.IsVisible==true && !app.Settings.HoverPeek);
                Check("Drop target renders three thumbnails and four-item count, layout "+compact,target!=null && Diagnostics.Descendants(target).OfType<Image>().Count()==3 && Diagnostics.Descendants(target).OfType<TextBlock>().Any(t=>t.Text=="Drop 4 items onto your shelf"));
                Cancel();Check("Cancelled closed drag restores closed island, layout "+compact,!overlay.IsExpanded && !overlay.IsPeeking);
            }
            overlay.ToggleVisibility();Drag();Check("File drag preserves manual hide",overlay.UserHidden && !overlay.IsVisible && Field("fileDragActive") is false);Cancel();overlay.ToggleVisibility();
            app.Settings.CompactLayout=true;app.Settings.HoverPeek=true;foreach(var view in app.Settings.HoverViews)view.Enabled=true;app.ChangeSettings();
            overlay.ShowCommands(false);var command=Entry("Search commands");command.Text="unfinished search";command.Select(3,5);Drag();Cancel();
            Check("Cancelled expanded drag retains command editor and selection",overlay.CommandsOpen && ReferenceEquals(command,Entry("Search commands")) && command.Text=="unfinished search" && command.SelectionStart==3 && command.SelectionLength==5);
            overlay.ShowActivities(false);var activityBody=Field("activityContent");Drag();Cancel();Check("Cancelled drag retains Activities body",overlay.ActivitiesOpen && ReferenceEquals(activityBody,Field("activityContent")));
            overlay.ShowTools("calendar",false);Button("Add local event").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));var title=Entry("Event title");title.Text="Unsaved local draft";title.Select(2,4);var start=Entry("Event start time");start.Text="13:45";Drag();Cancel();
            Check("Cancelled drag retains calendar form instances, draft and caret",ReferenceEquals(title,Entry("Event title")) && title.Text=="Unsaved local draft" && title.SelectionStart==2 && title.SelectionLength==4 && ReferenceEquals(start,Entry("Event start time")) && start.Text=="13:45" && Field("calendarEditing") is true);
            // Inject availability only; no media command or external session is created.
            overlay.Collapse();app.Session.Timer.Start(300,DateTimeOffset.UtcNow);app.Session.Focus.Reset();overlay.DiagnosticSatelliteMedia=true;app.Settings.ActivitySatellite=true;app.ChangeSettings();Invoke("UpdateSatellite");
            var satellite=(Window?)Field("satelliteWindow");Check("Satellite fixture has room and a visible click target",satellite?.IsVisible==true);
            if(satellite!=null)
            {
                var before=Native.GetForegroundWindow();var bounds=new Rect(satellite.Left,satellite.Top,satellite.Width,satellite.Height);
                overlay.DiagnosticSatellitePointerPresent=true;satellite.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent});
                Invoke("UpdateSatellite");await Task.Delay(1100);Invoke("UpdateSatellite");
                Check("Satellite remains visible and stationary across own hover preview and shared update",overlay.IsPeeking && overlay.PeekPage=="timer" && satellite.IsVisible && bounds==new Rect(satellite.Left,satellite.Top,satellite.Width,satellite.Height));
                Check("Satellite hover preserves foreground and uses non-activating window",before==Native.GetForegroundWindow() && !satellite.ShowActivated);
                // Simulate the crossing into the main preview, then departure from both surfaces.
                overlay.DiagnosticPointerPresent=true;overlay.DiagnosticSatellitePointerPresent=false;satellite.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent});await Task.Delay(400);
                Check("Satellite-to-preview crossing keeps preview open",overlay.IsPeeking);
                overlay.DiagnosticPointerPresent=false;((System.Windows.Threading.DispatcherTimer)Field("leaveDelay")!).Start();await Task.Delay(400);Check("Leaving satellite and preview dismisses orphan preview",!overlay.IsPeeking);
                Invoke("UpdateSatellite");Invoke("OpenSatellite",false);Invoke("UpdateSatellite");Diagnostics.Descendants(satellite).OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check("Satellite click after hover opens countdown panel",overlay.IsExpanded && overlay.ActivePage=="focus");
            }
            overlay.DiagnosticSatelliteMedia=false;overlay.Collapse();app.Settings.CompactLayout=false;app.ChangeSettings();overlay.Reveal(false);overlay.UpdateLayout();
            Check("Classic Quick launch exposes Activities",Diagnostics.Descendants(overlay).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="Activities"));
            Check("Classic visible system card requests shared samples",overlay.SystemSummaryVisible && Diagnostics.Descendants(overlay).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text=="System"));
            overlay.ShowCommands(false);Check("Classic commands exclude hidden system summary",!overlay.SystemSummaryVisible);overlay.ShowActivities(false);Check("Classic Activities exclude hidden system summary",!overlay.SystemSummaryVisible);
            overlay.OpenFeaturePage("notes");Check("Classic Tools route restores actual system card visibility",!overlay.ActivitiesOpen && overlay.SystemSummaryVisible);
            overlay.Collapse();Check("Collapsed island stops system-summary sample demand",!overlay.SystemSummaryVisible);
        }
        finally
        {
            Cancel();overlay.DiagnosticSatelliteMedia=false;overlay.DiagnosticSatellitePointerPresent=false;overlay.DiagnosticPointerPresent=false;overlay.Collapse();
            Restore(app.Session.Timer,timer);Restore(app.Session.Focus,focus);
            var original=JsonSerializer.Deserialize<Settings>(saved)!;foreach(var property in typeof(Settings).GetProperties().Where(p=>p.CanRead && p.CanWrite))property.SetValue(app.Settings,property.GetValue(original));app.ChangeSettings();
            if(hidden!=overlay.UserHidden)overlay.ToggleVisibility();overlay.DiagnosticRun=diagnostic;
        }
    }
    private sealed record CountdownSnapshot(DateTimeOffset? Deadline,double Remaining,double Duration,string Phase,Guid RunId);
    private static CountdownSnapshot Snapshot(Countdown value)=>new(value.Deadline,value.RemainingSeconds,value.DurationSeconds,value.Phase,value.RunId);
    private static void Restore(Countdown value,CountdownSnapshot saved)
    {value.Deadline=saved.Deadline;value.RemainingSeconds=saved.Remaining;value.DurationSeconds=saved.Duration;value.Phase=saved.Phase;typeof(Countdown).GetProperty(nameof(Countdown.RunId))!.SetValue(value,saved.RunId);}
}
