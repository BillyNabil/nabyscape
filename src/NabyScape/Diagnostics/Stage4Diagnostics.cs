using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;
internal static class Stage4Diagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        void Capture(FrameworkElement element,string name)
        {element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
        async Task Settled(){for(int i=0;i<50 && island.IsAnimating;i++)await Task.Delay(50);island.UpdateLayout();}
        bool Padded(Border card)
        {
            var bounds=card.TransformToAncestor(island).TransformBounds(new Rect(0,0,card.ActualWidth,card.ActualHeight));var contour=island.Contour;
            return new[]{bounds.TopLeft,bounds.TopRight,bounds.BottomLeft,bounds.BottomRight,new Point(bounds.Left-12,bounds.Top+20),new Point(bounds.Right+12,bounds.Top+20),new Point(bounds.Left+bounds.Width/2,bounds.Top-12),new Point(bounds.Left+bounds.Width/2,bounds.Bottom+12)}.All(p=>contour.FillContains(p));
        }
        string folder=Path.Combine(directory,"download-fixture");Directory.CreateDirectory(folder);string one=Path.Combine(folder,"Quarterly report with a deliberately long completion filename to verify narrow card trimming.txt"),two=Path.Combine(folder,"Reference notes.txt");File.WriteAllText(one,"fixture");File.WriteAllText(two,"fixture");
        var settings=app.Settings;settings.NotificationDelivery="Notch";settings.SmartActivities=false;app.Notifications.Clear();island.Collapse();app.ChangeSettings();
        var handle=new WindowInteropHelper(island).Handle;
        var before=Native.GetForegroundWindow();
        app.PostNotice(new("downloads","2 downloads ready","Folder arrival inferred · originals stay in place",new[]{one,two}));await Task.Delay(150);
        check("Stage4 arrival remains compact and does not take focus",!island.IsExpanded && !island.IsPeeking && Native.GetForegroundWindow()==before);
        check("Stage4 compact indicator names arrivals",island.HeaderLabel.Text.Contains("downloads ready"));Capture(island,"stage4-compact");
        island.DiagnosticFeedFeedback(false,65);await Task.Delay(100);check("Stage4 volume indication takes priority",island.HeaderLabel.Text.Contains("65%"));
        island.OpenNotice();check("Stage4 notification preview waits for feedback",!island.IsPeeking);await Task.Delay(2200);island.DiagnosticPointerPresent=true;
        var hoverForeground=Native.GetForegroundWindow();var hoverStarted=DateTimeOffset.UtcNow;island.OpenNotice();var hoverAfter=Native.GetForegroundWindow();await Settled();
        check($"Stage4 hover opens compact completion view (peeking={island.IsPeeking}, expanded={island.IsExpanded}, elapsed={(DateTimeOffset.UtcNow-hoverStarted).TotalMilliseconds:0}ms)",island.IsPeeking && !island.IsExpanded);
        check($"Stage4 hover does not activate (before={hoverForeground:X}, after={hoverAfter:X}, island={handle:X}, ShowActivated={island.ShowActivated})",hoverForeground==hoverAfter && hoverAfter!=handle && !island.ShowActivated);
        var actions=Diagnostics.Descendants(island).OfType<Button>().ToArray();
        foreach(string name in new[]{"Show in folder","Add to shelf","Dismiss"})check("Stage4 keyboard action "+name,actions.Any(b=>AutomationProperties.GetName(b)==name && b.Focusable && b.IsTabStop));
        var card=(Border)typeof(OverlayWindow).GetField("noticeCard",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;check("Stage4 completion card retains notch shoulder padding",!island.IsAnimating && Padded(card));
        Capture(island,"stage4-download-burst");
        island.Collapse();settings.HoverWidth=300;app.ChangeSettings();island.OpenNotice();await Settled();
        var narrowCard=(Border)typeof(OverlayWindow).GetField("noticeCard",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;
        check("Stage4 minimum width actions remain inside padded card",Padded(narrowCard) && Diagnostics.Descendants(narrowCard).OfType<Button>().All(b=>{var at=b.TransformToAncestor(narrowCard).Transform(new Point());return at.X>=-1 && at.X+b.ActualWidth<=narrowCard.ActualWidth+1;}));Capture(island,"stage4-download-minimum");
        actions=Diagnostics.Descendants(island).OfType<Button>().ToArray();
        actions.Single(b=>AutomationProperties.GetName(b)=="Add to shelf").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("Stage4 shelf handoff dedupes originals",app.Productivity.Shelf.Count(p=>p.Path==one || p.Path==two)==2 && File.Exists(one) && File.Exists(two));
        actions.Single(b=>AutomationProperties.GetName(b)=="Add to shelf").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("Stage4 repeated shelf handoff remains deduped",app.Productivity.Shelf.Count(p=>p.Path==one || p.Path==two)==2);
        const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Instance;
        object? Field(string name)=>typeof(OverlayWindow).GetField(name,fields)!.GetValue(island);
        void Invoke(string name,params object[] args)=>typeof(OverlayWindow).GetMethod(name,fields)!.Invoke(island,args);
        Button Action(string name)=>Diagnostics.Descendants((Border)Field("noticeCard")!).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        island.Collapse();app.Notifications.Clear();app.Productivity.RemoveFile(one);app.Productivity.RemoveFile(two);
        app.PostNotice(new("downloads","First arrival","Displayed A",new[]{one}));island.OpenNotice();await Settled();
        var firstCard=Field("noticeCard");var firstNotice=app.Notifications.Current;
        // Model the modal outgoing OLE drag boundary without accessing an external drop target.
        typeof(OverlayWindow).GetField("fileDragActive",fields)!.SetValue(island,true);
        app.PostNotice(new("downloads","Second arrival","Queued B during outgoing drag",new[]{two}));
        check("Stage4 outgoing drag retains displayed snapshot and queues later arrival",ReferenceEquals(firstCard,Field("noticeCard")) && ReferenceEquals(firstNotice,app.Notifications.Current) && app.Notifications.Count==2 && firstNotice!.Files!.SequenceEqual(new[]{one}));
        typeof(OverlayWindow).GetField("fileDragActive",fields)!.SetValue(island,false);
        Action("Add to shelf").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("Stage4 first displayed handoff contains A only",app.Productivity.Shelf.Any(p=>p.Path==one) && !app.Productivity.Shelf.Any(p=>p.Path==two));
        Action("Dismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));island.OpenNotice();await Settled();
        Action("Add to shelf").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("Stage4 dismissing A preserves B for a second handoff",app.Notifications.Count==1 && app.Notifications.Current!.Files!.SequenceEqual(new[]{two}) && app.Productivity.Shelf.Any(p=>p.Path==two));
        var pending=app.Notifications.Current;
        Invoke("BeginFileDrop");Invoke("ShowShelfDropPreview",(object)new[]{one});await Settled();
        check("Stage4 external drag takes notice priority and exposes shelf target",Field("noticeShowing") is false && island.IsPeeking && island.PeekPage=="files" && Field("shelfDropPreview") is Border {IsVisible:true} && Field("peekPanel") is Grid {IsVisible:true});
        Invoke("FinishFileDrop",false);await Settled();
        check("Stage4 cancelled external drag restores pending notice and actions",Field("noticeShowing") is true && ReferenceEquals(pending,app.Notifications.Current) && Action("Add to shelf").IsVisible && !island.IsExpanded);
        Invoke("BeginFileDrop");Invoke("ShowShelfDropPreview",(object)new[]{one});app.Productivity.AddFiles(new[]{one});Invoke("FinishFileDrop",true);await Settled();
        check("Stage4 accepted external drag exposes Files and retains pending notice",Field("noticeShowing") is false && island.IsPeeking && island.PeekPage=="files" && Field("peekPanel") is Grid {IsVisible:true} && Field("shelfDropPreview")==null && ReferenceEquals(pending,app.Notifications.Current));
        island.Collapse();app.Notifications.Clear();settings.DownloadsEnabled=true;
        var epoch=typeof(App).GetField("downloadEpoch",fields)!;var failure=typeof(App).GetMethod("DownloadFailed",fields)!;
        failure.Invoke(app,new object[]{"Old folder failure"});epoch.SetValue(app,(int)epoch.GetValue(app)!+1);await Task.Delay(100);
        check("Stage4 stale failure callback after folder change is suppressed",app.Notifications.Count==0);
        failure.Invoke(app,new object[]{"Current folder failure"});await Task.Delay(100);
        check("Stage4 current configuration failure still notifies",app.Notifications.Current?.Title=="Downloads unavailable");
        app.Notifications.Clear();failure.Invoke(app,new object[]{"Disabled folder failure"});settings.DownloadsEnabled=false;await Task.Delay(100);
        check("Stage4 disabling downloads rejects queued failure",app.Notifications.Count==0);
        island.DiagnosticPointerPresent=false;
        island.Collapse();app.Notifications.Clear();settings.AlwaysTucked=true;app.ChangeSettings();app.PostNotice(new("timer","Timer finished","Time is up"));for(int frame=0;frame<80 && island.TuckMoving;frame++)await Task.Delay(25);
        check("Stage4 tucked arrival stays at edge",island.IsTucked && !island.IsPeeking && !island.IsExpanded && Math.Abs(island.VisibleBounds.Height-settings.TuckEdge)<1);
        Capture(island,"stage4-tucked");island.DiagnosticPointerPresent=true;island.RevealTuckedPreview();await Settled();check("Stage4 tucked hover reveals notice only",island.IsPeeking && !island.IsExpanded);island.DiagnosticPointerPresent=false;
        island.Collapse();app.Notifications.Clear();island.ToggleVisibility();app.PostNotice(new("failure","Fixture failure","Hidden fallback"));await Task.Delay(100);
        check("Stage4 manually hidden stays hidden with Windows fallback",island.ManuallyHidden && !island.IsVisible && app.Notifications.Count==0);
        island.ToggleVisibility();settings.AlwaysTucked=false;app.ChangeSettings();app.Notifications.Clear();
        island.ShowTools("notes");await Task.Delay(100);var note=Diagnostics.Descendants(island).OfType<TextBox>().Single(b=>AutomationProperties.GetName(b)=="Quick notes");note.Text="Stage4 editor fixture";note.Select(3,6);note.Focus();var focused=System.Windows.Input.Keyboard.FocusedElement;
        app.PostNotice(new("focus","Focus finished","Take a break"));await Task.Delay(100);
        check("Stage4 arrival preserves editor, selection and expanded panel",island.IsExpanded && !island.IsPeeking && note.Text=="Stage4 editor fixture" && note.SelectionStart==3 && note.SelectionLength==6 && System.Windows.Input.Keyboard.FocusedElement==focused);
        island.ShowCommands(false);var command=Diagnostics.Descendants(island).OfType<Button>().FirstOrDefault(b=>AutomationProperties.GetName(b)=="Notifications");
        // The same public action used by command search opens the pending completion card.
        app.OpenNotifications();await Task.Delay(200);check("Stage4 requested notification replaces command panel with compact card",island.IsPeeking && !island.IsExpanded);
        island.Collapse();app.Notifications.Clear();settings.NotificationDelivery="Both";app.PostNotice(new("updates","Update available","Fixture version"));check("Stage4 Both queues exactly one island notice",app.Notifications.Count==1);
        settings.NotificationDelivery="Off";app.ChangeSettings();app.PostNotice(new("timer","Off fixture","Suppressed"));check("Stage4 Off clears pending and suppresses notices",app.Notifications.Count==0);
        settings.NotificationDelivery="Notch";app.ChangeSettings();app.PostNotice(new("failure","Timeout","Ephemeral"));app.Notifications.Tick(DateTimeOffset.UtcNow.AddSeconds(13),false);check("Stage4 pending timeout leaves no history",app.Notifications.Count==0);
        var window=new SettingsWindow(app);
        try
        {
            window.Show();typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,new object[]{"Application"});await Task.Delay(150);
            check("Stage4 distinct notification and download scenes",Diagnostics.Descendants(window).OfType<SettingsScene>().Count(s=>s.Section is "Downloads" or "Notifications")==2);
            foreach(string title in new[]{"Notifications","Downloads"})
            {
                var scene=Diagnostics.Descendants(window).OfType<SettingsScene>().Single(s=>s.Section==title);scene.BringIntoView();await Task.Delay(100);Capture(window,"stage4-settings-"+title.ToLowerInvariant());
            }
            window.Width=580;window.Height=700;await Task.Delay(150);var folderButton=Diagnostics.Descendants(window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Choose folder…");folderButton.BringIntoView();await Task.Delay(150);Capture(window,"stage4-settings-narrow");
            var origin=folderButton.TransformToAncestor(window).Transform(new Point());check("Stage4 narrow folder controls contained",origin.X>=0 && origin.X+folderButton.ActualWidth<=window.ActualWidth+1);
        }
        finally{window.Close();island.Collapse();app.Notifications.Clear();}
    }
}
