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
        async Task Settle(){await Task.Delay(420);overlay.UpdateLayout();}
        void Capture(FrameworkElement target,string name,string? label=null)
        {
            target.UpdateLayout();int width=Math.Max(1,(int)Math.Ceiling(target.ActualWidth)),height=Math.Max(1,(int)Math.Ceiling(target.ActualHeight));
            var raw=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);raw.Render(target);BitmapSource result=raw;
            if(label!=null)
            {
                var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
                {
                    draw.DrawRectangle(UI.Brush("#151515"),null,new Rect(0,0,width,height+48));draw.DrawImage(raw,new Rect(0,48,width,height));
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
            app.Productivity.Note="A shared diagnostic note.\nSecond line for caret preservation.";
            var shelfFile=Path.Combine(output,"shelf-fixture.txt");File.WriteAllText(shelfFile,"Local diagnostic shelf fixture.");app.Productivity.AddFiles(new[]{shelfFile,Path.Combine(output,"missing-file.txt")});
            calendarId=app.Calendar.Add("Local diagnostic review",DateTimeOffset.Now.AddMinutes(10),DateTimeOffset.Now.AddMinutes(40),"https://example.com/meeting").Id;
            foreach(var kind in kinds)
            {
                app.Previews.Open(kind);var window=WindowFor(kind);created.Add(window);await Settle();
                Check(kind+" opens a visible native resizable window",window.IsVisible && window.ResizeMode==ResizeMode.CanResize && app.Previews.AnyVisible(kind));
                Check(kind+" starts with Always on top off",!window.Topmost && Tree(window).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Always on top").IsChecked==false);
                Check(kind+" default geometry respects minimum dimensions",window.ActualWidth>=window.MinWidth-.1 && window.ActualHeight>=window.MinHeight-.1);
                Capture(window,"detached-"+kind+"-default");
                var width=window.Width;var height=window.Height;window.Width=window.MinWidth;window.Height=window.MinHeight;await Settle();
                Check(kind+" minimum resize preserves window bounds",Math.Abs(window.ActualWidth-window.MinWidth)<1 && Math.Abs(window.ActualHeight-window.MinHeight)<1);
                Capture(window,"detached-"+kind+"-minimum");window.Width=width;window.Height=height;
                app.Previews.Open(kind);Check(kind+" repeated Open reuses the same window",ReferenceEquals(window,WindowFor(kind)) && Application.Current.Windows.Cast<Window>().Count(w=>w.Title==window.Title)==1);
            }
            Check("all seven visible previews have one Updated subscription each",Subscribers()==7);
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
            app.Session.Focus.Reset();var definition=new ActivityDefinition{Name="Diagnostic focus workflow",Steps=new(){new(){Kind="focus",Minutes=2}}};activityId=definition.Id;
            Check("local focus-only activity definition saves",app.Activities.Save(definition) && app.Store.Load<ActivityActionsData>("activities").Activities.Any(a=>a.Id==definition.Id && a.Steps.All(s=>s.Kind=="focus")));overlay.ShowActivities(false);await Settle();Capture(overlay,"activity-definition");
            FindButton(overlay,"Start activity Diagnostic focus workflow").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));activityStarted=app.Activities.ActiveActivity?.Id==definition.Id;await Settle();
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
    private sealed record CountdownSnapshot(DateTimeOffset? Deadline,double Remaining,double Duration,string Phase,Guid RunId);
    private static CountdownSnapshot Snapshot(Countdown value)=>new(value.Deadline,value.RemainingSeconds,value.DurationSeconds,value.Phase,value.RunId);
    private static void Restore(Countdown value,CountdownSnapshot saved)
    {value.Deadline=saved.Deadline;value.RemainingSeconds=saved.Remaining;value.DurationSeconds=saved.Duration;value.Phase=saved.Phase;typeof(Countdown).GetProperty(nameof(Countdown.RunId))!.SetValue(value,saved.RunId);}
}

