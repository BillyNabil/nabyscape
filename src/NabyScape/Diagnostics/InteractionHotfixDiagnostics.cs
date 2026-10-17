using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class InteractionHotfixDiagnostics
{
    internal static async Task RunClassicMixer(App app,OverlayWindow island,Action<string,bool> check)
    {
        var s=app.Settings;s.CompactLayout=false;s.ReduceMotion=true;s.SmartActivities=false;s.ActivitySatellite=false;s.HideFullscreen=s.HideMaximized=false;
        foreach(var widget in s.Widgets)widget.Enabled=widget.Id=="volume";
        s.HoverViews.Single(v=>v.Id=="media").Enabled=true;s.HoverViews.Single(v=>v.Id=="timer").Enabled=true;app.ChangeSettings();island.Collapse();
        bool Visible(string name)=>Diagnostics.Descendants(island).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)==name);
        bool MixerClosed()=>!(bool)typeof(OverlayWindow).GetField("classicMixerOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(island)!;
        island.OpenFeaturePage("media");await Task.Delay(60);
        check("Interaction hotfix: classic hidden media opens independently of enabled volume widget",island.ActivePage=="media" && Visible("Play or pause"));
        island.OpenMixer();await Task.Delay(80);
        check("Interaction hotfix: classic mixer takes precedence over hidden media route",island.ActivePage=="mixer" && Visible("Back to widgets") && !Visible("Play or pause"));
        Diagnostics.Descendants(island).OfType<Button>().Single(b=>b.IsVisible && AutomationProperties.GetName(b)=="Back to widgets").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
        check("Interaction hotfix: classic mixer Back restores the hidden media route",MixerClosed() && island.ActivePage=="media" && Visible("Play or pause"));
        island.OpenMixer();island.OpenFeaturePage("media");await Task.Delay(60);
        check("Interaction hotfix: reopening the same hidden target exits classic mixer",MixerClosed() && island.ActivePage=="media" && Visible("Play or pause") && !Visible("Back to widgets"));
        island.OpenMixer();island.OpenFeaturePage("timer");await Task.Delay(60);
        check("Interaction hotfix: a different hidden target exits classic mixer",MixerClosed() && island.ActivePage=="focus" && Diagnostics.Descendants(island).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text=="Timer") && !Visible("Back to widgets"));
        island.OpenMixer();Diagnostics.Descendants(island).OfType<Button>().Single(b=>b.IsVisible && AutomationProperties.GetName(b)=="Back to widgets").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
        check("Interaction hotfix: classic mixer Back retains the selected timer route",MixerClosed() && island.ActivePage=="focus" && Diagnostics.Descendants(island).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text=="Timer"));
        island.Collapse();
    }
    internal static async Task RunTuckHide(App app,OverlayWindow island,Action<string,bool> check)
    {
        var s=app.Settings;s.ReduceMotion=true;s.AlwaysTucked=false;s.HideFullscreen=s.HideMaximized=false;s.SmartActivities=false;s.ActivitySatellite=false;s.LiveActivity=false;s.AnimationMs=700;
        app.ChangeSettings();island.DiagnosticEnvironment(IntPtr.Zero);island.DiagnosticPointerPresent=false;island.Collapse();
        s.ReduceMotion=false;app.ChangeSettings();island.ToggleTuck();await Task.Delay(100);island.ToggleTuck();await Task.Delay(140);
        check("Interaction hotfix: manual-hide regression begins during animated downward reveal",island.TuckMoving && !island.IsTucked && island.TuckOffset<0);
        island.ToggleVisibility();
        check("Interaction hotfix: hiding an animated reveal cancels its unchanged zero destination immediately",island.UserHidden && !island.IsVisible && !island.TuckMoving && island.TuckOffset==0);
        await Task.Delay(120);
        check("Interaction hotfix: manually hidden island retains no tuck callback",island.UserHidden && !island.TuckMoving);
        island.ToggleVisibility();s.ReduceMotion=true;app.ChangeSettings();island.Collapse();
    }
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        void Check(string name,bool value)=>check("Interaction hotfix: "+name,value);
        void Capture(FrameworkElement view,string name)
        {
            view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(view);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        bool TargetVisible(string id)=>Diagnostics.Descendants(island).OfType<FrameworkElement>().Any(v=>v.IsVisible && (id switch
        {
            "stopwatch"=>v is StopwatchView,
            "notes"=>v is TextBox t && AutomationProperties.GetName(t)=="Quick notes",
            "system"=>v is Button b && AutomationProperties.GetName(b)=="Monitor Overview",
            "media"=>v is Button b && AutomationProperties.GetName(b)=="Play or pause",
            "timer" or "focus"=>v is TextBlock t && t.Text==(id=="timer"?"Timer":"Focus"),
            _=>typeof(OverlayWindow).GetField("toolsMode",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(island)?.ToString()==id
        }));
        await RunTuckHide(app,island,check);
        await RunClassicMixer(app,island,check);
        var s=app.Settings;s.ReduceMotion=true;s.AnimationMs=700;s.SmartActivities=false;s.ActivitySatellite=false;s.LiveActivity=false;s.HideFullscreen=s.HideMaximized=false;s.CompactLayout=true;
        foreach(var widget in s.Widgets)widget.Enabled=false;foreach(var view in s.HoverViews)view.Enabled=true;foreach(var tool in s.Tools)tool.Enabled=true;
        app.ChangeSettings();island.Collapse();
        var settingsBefore=s.Widgets.Select(w=>(w.Id,w.Enabled)).ToArray();var hoverBefore=s.HoverViews.Select(w=>(w.Id,w.Enabled)).ToArray();
        Check("all ten configured pages remain available with every main widget hidden",island.HoverDeckIds.Length==10);
        foreach(string id in island.HoverDeckIds)
        {
            island.PreviewActivity();island.SelectPeek(id);await Task.Delay(40);
            Check("carousel selects "+id+" and excludes inactive pages from keyboard traversal",island.PeekPage==id && island.PeekTabSafe);
            island.OpenPeekPage();await Task.Delay(80);island.UpdateLayout();
            string target=id switch{"system"=>"status","timer" or "focus" or "stopwatch"=>"focus","media"=>"media",_=>"tools"};
            Check("click opens correct hidden full panel for "+id,island.IsExpanded && island.ActivePage==target && TargetVisible(id));
            if(id is "timer" or "system" or "notes")Capture(island,"hover-route-"+id);
            island.Collapse();await Task.Delay(40);
        }
        Check("all eight detachable kinds honor hover availability despite hidden main widgets",new[]{"media","timer","focus","stopwatch","system","calendar","notes","files"}.All(app.Previews.CanOpen));
        app.Previews.Open("timer");await Task.Delay(80);Check("hidden main timer can detach",Application.Current.Windows.Cast<Window>().Any(w=>w.Title=="NabyScape · Timer" && w.IsVisible));
        foreach(var window in Application.Current.Windows.Cast<Window>().Where(w=>w.Title=="NabyScape · Timer").ToArray())window.Close();
        island.PreviewActivity();island.SelectPeek("media");island.NavigatePeek(1);Check("arrow advances beyond first page",island.PeekPage==island.HoverDeckIds[1]);
        island.FinishPeekSwipe(-s.SwipeDistance-10);Check("swipe advances across the complete deck",island.PeekPage==island.HoverDeckIds[2]);
        island.Collapse();
        s.Tools.Single(t=>t.Id=="notes").Enabled=false;app.ChangeSettings();Check("individual tool off removes only its own hover page",island.HoverDeckIds.Length==9 && !island.HoverDeckIds.Contains("notes"));s.Tools.Single(t=>t.Id=="notes").Enabled=true;
        Check("navigation preserves saved widget order and toggles and never enables clipboard capture",settingsBefore.SequenceEqual(s.Widgets.Select(w=>(w.Id,w.Enabled))) && hoverBefore.SequenceEqual(s.HoverViews.Select(w=>(w.Id,w.Enabled))) && !s.ClipboardHistory && !app.Productivity.ClipboardEnabled);
        foreach(string id in new[]{"timer","stopwatch","system","notes"})
        {
            s.CompactLayout=false;app.ChangeSettings();island.PreviewActivity(); // Classic normally requires explicit tucked/drag preview; route selection was retained above.
            typeof(OverlayWindow).GetField("peekId",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(island,id);
            island.OpenPeekPage();await Task.Delay(50);Check("classic full panel opens hidden "+id,island.IsExpanded && TargetVisible(id));island.Collapse();
        }
        s.CompactLayout=true;s.ReduceMotion=false;app.ChangeSettings();island.DiagnosticEnvironment(IntPtr.Zero);island.DiagnosticPointerPresent=false;island.Collapse();await Task.Delay(1100);
        island.ToggleTuck();double start=island.TuckOffset;await Task.Delay(100);double middle=island.TuckOffset;
        Check("tuck has a real intermediate upward offset from current display",start==0 && middle<-.05 && middle>AutoTuckPolicy.Offset(s.Height,s.TuckEdge)+.05 && island.TuckMoving);
        Check("animated translation drives both clip and hit geometry",Math.Abs(island.VisibleBounds.Height-(s.Height+middle))<.2 && island.VisiblePoint(new Point(island.ActualWidth/2,1)) && !island.VisiblePoint(new Point(island.ActualWidth/2,island.VisibleBounds.Bottom+1)));
        island.CheckEnvironment();await Task.Delay(80);Check("repeated environment heartbeat continues the existing tuck",island.TuckOffset<middle);
        double beforeReverse=island.TuckOffset;island.ToggleTuck();Check("rapid reversal retains the current offset without a jump",Math.Abs(island.TuckOffset-beforeReverse)<.01);await Task.Delay(160);Check("reversal moves smoothly downward",island.TuckOffset>beforeReverse);
        await Task.Delay(1300);Check("reveal finishes at zero with no motion callback left",island.TuckOffset==0 && !island.TuckMoving);
        foreach(double edge in new[]{3d,6d,12d})
        {
            s.TuckEdge=edge;app.ChangeSettings();island.ToggleTuck();await Task.Delay(1600);
            Check("tuck settles at configured "+edge+" DIP and suspends live work",Math.Abs(island.VisibleBounds.Height-edge)<.1 && !island.TuckMoving && !island.LiveRunning);
            if(edge==6){Capture(island,"tuck-edge-6");int reads=island.MonitorReads,frames=island.LiveFrames;await Task.Delay(400);Check("settled tuck has no live frames or monitor reads",reads==island.MonitorReads && frames==island.LiveFrames && !island.MonitorPolling && !island.TuckMoving);}
            island.DiagnosticPointerPresent=true;island.RevealTuckedPreview();await Task.Delay(1200);Check("hover reveals preview from edge "+edge,island.IsPeeking && !island.IsExpanded && !island.IsTucked && island.TuckOffset==0);
            island.DiagnosticPointerPresent=false;island.Collapse();await Task.Delay(1600);island.ToggleTuck();await Task.Delay(1200);
        }
        s.ReduceMotion=true;app.ChangeSettings();island.ToggleTuck();Check("Reduce Motion reaches tuck immediately without callbacks",island.TuckOffset==AutoTuckPolicy.Offset(s.Height,s.TuckEdge) && !island.TuckMoving);
        island.ToggleVisibility();island.ToggleTuck();Check("manual hide dominates tuck and reversal",island.UserHidden && !island.IsVisible && !island.TuckMoving);island.ToggleVisibility();
        foreach(string kind in new[]{"maximized","fullscreen"})
        {
            var screen=Native.ChooseScreen(s);double scale=Native.Scale(screen);var b=screen.Bounds;
            var startInfo=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"NabyScape.exe")){UseShellExecute=false,CreateNoWindow=true};
            foreach(string arg in new[]{"--window-fixture",kind,(b.Left/scale).ToString(CultureInfo.InvariantCulture),(b.Top/scale).ToString(CultureInfo.InvariantCulture),(b.Width/scale).ToString(CultureInfo.InvariantCulture),(b.Height/scale).ToString(CultureInfo.InvariantCulture)})startInfo.ArgumentList.Add(arg);
            using var external=Process.Start(startInfo)!;
            try
            {
                var watch=Stopwatch.StartNew();while(external.MainWindowHandle==IntPtr.Zero && !external.HasExited && watch.ElapsedMilliseconds<5000){await Task.Delay(100);external.Refresh();}
                s.HideMaximized=s.HideFullscreen=true;app.ChangeSettings();island.DiagnosticEnvironment(external.MainWindowHandle);island.CheckEnvironment();
                Check(kind+" native rule tucks with correct geometry",external.MainWindowHandle!=IntPtr.Zero && island.IsTucked && Math.Abs(island.VisibleBounds.Height-s.TuckEdge)<.1);
            }
            finally{island.DiagnosticEnvironment(IntPtr.Zero);if(!external.HasExited){external.CloseMainWindow();if(!await Task.Run(()=>external.WaitForExit(1500)))external.Kill();}}
        }
        s.HideMaximized=s.HideFullscreen=false;app.ChangeSettings();island.CheckEnvironment();
        foreach(double width in new[]{193.6,302d})
        {
            var panel=new StackPanel{Width=width,Background=Brushes.Black};
            foreach(var item in new[]{new MixerItem(new(MixerScope.Apps,"fixture","local-icon"),"Explorer (local icon fixture)","Headphones",.75,false,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")),new MixerItem(new(MixerScope.Apps,"fixture","led"),"LEDKeeper","Headphones",.5,true),new MixerItem(new(MixerScope.Outputs,"fixture"),"Headphones","Headphones",1,false)})
            {var mute=new Button{Content=Icons.Create(item.Muted?"muted":"volume",16),MinHeight=28,Padding=new(3)};panel.Children.Add(OverlayWindow.CreateMixerRowLayout(item,mute).View);}
            var window=new Window{Content=panel,SizeToContent=SizeToContent.WidthAndHeight,WindowStyle=WindowStyle.None,ShowInTaskbar=false,Background=Brushes.Black};window.Show();await Task.Delay(400);Capture(panel,"mixer-width-"+width.ToString(CultureInfo.InvariantCulture));window.Close();
        }
        MixerRowLayoutTests.Run(Check);
        island.ToggleTuck();island.Cleanup();Check("cleanup removes tuck motion callback",!island.TuckMoving);
    }
}
