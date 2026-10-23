using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
namespace NabyScape;
internal static class Stage5Diagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        void Capture(FrameworkElement element,string name)
        {element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
        var s=app.Settings;s.ReduceMotion=true;s.LiveActivity=false;s.SmartActivities=false;s.HideFullscreen=s.HideMaximized=false;s.Widgets.Single(w=>w.Id=="stopwatch").Enabled=true;s.HoverViews.Single(w=>w.Id=="stopwatch").Enabled=true;app.ChangeSettings();
        foreach(double width in new[]{384d,340d})
        {
            s.PanelWidth=width;app.ChangeSettings();island.Reveal(false);island.SelectPage("stopwatch",false);await Task.Delay(250);island.UpdateLayout();
            check("Stage5 dedicated Stopwatch fits at "+width,Diagnostics.Descendants(island).OfType<StopwatchView>().Any(v=>v.IsVisible && v.ActualWidth>140));
            Capture(island,"stage5-stopwatch-"+width);
        }
        var main=Diagnostics.Descendants(island).OfType<StopwatchView>().Single(v=>v.IsVisible);
        int builds=island.BodyBuilds;

        app.Previews.Open("stopwatch");await Task.Delay(200);var detached=Application.Current.Windows.Cast<Window>().Single(w=>w.Title=="NabyScape · Stopwatch");var mini=Diagnostics.Descendants(detached).OfType<StopwatchView>().Single();var location=new Point(detached.Left,detached.Top);
        Diagnostics.Descendants(main).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Start, pause or resume stopwatch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(1200);check("Stage5 visible main and detached stopwatch update without body rebuild",main.Refreshes>1 && mini.Refreshes>1 && app.Session.Stopwatch.Running && island.BodyBuilds==builds);
        island.SelectPage("focus",false);await Task.Delay(100);
        var editor=Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>t.IsVisible && AutomationProperties.GetName(t)=="Timer duration in minutes");editor.Text="12.5";editor.Focus();editor.Select(1,2);
        int hiddenRefreshes=main.Refreshes,miniRefreshes=mini.Refreshes;builds=island.BodyBuilds;await Task.Delay(1200);
        check("Stage5 detached updates preserve focused countdown editor and stop hidden stopwatch work",mini.Refreshes>miniRefreshes && main.Refreshes==hiddenRefreshes && island.BodyBuilds==builds && ReferenceEquals(editor,Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>t.IsVisible && AutomationProperties.GetName(t)=="Timer duration in minutes")) && editor.Text=="12.5" && editor.SelectionStart==1 && editor.SelectionLength==2);
        island.SelectPage("stopwatch",false);await Task.Delay(100);check("Stage5 returning Stopwatch retains its view and shared running state",ReferenceEquals(main,Diagnostics.Descendants(island).OfType<StopwatchView>().Single(v=>v.IsVisible)) && app.Session.Stopwatch.Running);
        Capture(detached,"stage5-detached-stopwatch");
        var saved=app.Store.Load<SessionState>("session");check("Stage5 running Stopwatch persisted in isolated fixture",saved.Stopwatch.Running && saved.Stopwatch.UtcAnchor.HasValue);
        app.Session.Stopwatch.Toggle();app.SaveSession();double paused=app.Session.Stopwatch.Elapsed;await Task.Delay(1100);check("Stage5 pause shared and stable",!app.Session.Stopwatch.Running && app.Session.Stopwatch.Elapsed==paused);
        s.DisplayMode="Follow";app.ChangeSettings();check("Stage5 changing source policy does not relocate detached windows",location==new Point(detached.Left,detached.Top));
        detached.Close();island.Collapse();island.PreviewActivity();island.SelectPeek("stopwatch");await Task.Delay(200);Capture(island,"stage5-hover-stopwatch");check("Stage5 Stopwatch hover shares controls",island.PeekPage=="stopwatch" && Diagnostics.Descendants(island).OfType<StopwatchView>().Any(v=>v.IsVisible));
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object Field(string name)=>typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        void Invoke(string name,params object[] args)=>typeof(OverlayWindow).GetMethod(name,flags)!.Invoke(island,args);
        bool Blocked()=>(bool)typeof(OverlayWindow).GetProperty("FollowInteraction",flags)!.GetValue(island)!;
        // Change the effective width at the same Dimensions/ReflowDisplay entry used by
        // display events. This exercises real WPF layout, without claiming monitor/DPI emulation.
        s.HoverWidth=420;s.PanelWidth=600;Invoke("ReflowDisplay");island.SelectPeek("notes");island.UpdateLayout();
        var note=(TextBox)Field("peekNote");note.Text=string.Join("\n",Enumerable.Range(0,40).Select(i=>"Reflow note line "+i));island.Activate();note.Focus();int noteSelection=note.Text.Length-12;note.Select(noteSelection,5);note.UpdateLayout();note.ScrollToEnd();
        await Task.Delay(100);island.UpdateLayout();
        var viewport=(Grid)Field("peekViewport");var panel=(Grid)Field("peekPanel");var views=(System.Collections.Generic.Dictionary<string,FrameworkElement>)Field("peekViews");var noteView=views["notes"];int reflowBuilds=island.BodyBuilds;double noteScroll=note.VerticalOffset;
        string noteDraft=note.Text;
        check($"Stage5 note baseline (focus={note.IsKeyboardFocusWithin}, selection={note.SelectionStart}/{note.SelectionLength}, scroll={noteScroll:0.0}, draft={noteDraft.Length}, builds={reflowBuilds}, page={island.PeekPage})",note.IsKeyboardFocusWithin && note.SelectionStart==noteSelection && note.SelectionLength==5 && noteScroll>0 && island.PeekPage=="notes");
        foreach(double width in new[]{300d,340d,600d})
        {
            s.PanelWidth=width;Invoke("ReflowDisplay");island.UpdateLayout();
            var surface=(IslandSurface)Field("surface");var safe=PeekLayout.SafeBounds(surface.SurfaceWidth,s.HoverHeight,s.Radius,surface.Notch);
            check("Stage5 display reflow fits actual hover viewport and children at "+width,Math.Abs(panel.ActualWidth-safe.Width)<.1 && Math.Abs(viewport.ActualWidth-safe.Width)<.1 && views.Values.All(v=>Math.Abs(v.Width-safe.Width)<.1) && Math.Abs(noteView.ActualWidth-safe.Width)<.1);
            check($"Stage5 display reflow retains editor draft and body at {width} (draft={note.Text.Length}/{noteDraft.Length}, builds={island.BodyBuilds}/{reflowBuilds}, page={island.PeekPage})",ReferenceEquals(note,Field("peekNote")) && ReferenceEquals(noteView,views["notes"]) && note.Text==noteDraft && island.BodyBuilds==reflowBuilds && island.PeekPage=="notes");
            check($"Stage5 display reflow retains editor focus at {width} (focus={note.IsKeyboardFocusWithin})",note.IsKeyboardFocusWithin);
            check($"Stage5 display reflow retains selection at {width} ({note.SelectionStart}/{note.SelectionLength}, expected {noteSelection}/5)",note.SelectionStart==noteSelection && note.SelectionLength==5);
            check($"Stage5 display reflow retains scroll at {width} ({note.VerticalOffset:0.0}/{noteScroll:0.0})",Math.Abs(note.VerticalOffset-noteScroll)<.1);
        }
        Capture(island,"stage5-hover-reflow");
        s.PanelWidth=340;Invoke("ReflowDisplay");
        // Exercise the production interaction predicate with actual WPF focus/capture/menu
        // state and the file-drop entry point. Hiding only this fixture removes pointer and
        // island-focus blockers so each baseline can prove the tested state is decisive.
        island.Reveal(false);island.Hide();Keyboard.ClearFocus();
        check("Stage5 follow wiring has an unblocked baseline",!Blocked());
        Invoke("BeginFileDrop");check("Stage5 real file-drop entry blocks follow",(bool)Field("fileDragActive") && Blocked());Invoke("FinishFileDrop",false);
        check("Stage5 file-drop completion clears follow blocker",!Blocked());
        var input=new TextBox{Text="Follow wiring editor"};var slider=new Slider{Minimum=0,Maximum=100};var inputs=new StackPanel();inputs.Children.Add(input);inputs.Children.Add(slider);var host=new Window{Title="Stage5 follow wiring fixture",Content=inputs,Width=240,Height=120};host.Show();host.Activate();input.Focus();await Task.Delay(100);
        check("Stage5 external editor focus blocks follow",input.IsKeyboardFocusWithin && Blocked());
        slider.Focus();check("Stage5 external slider focus alone leaves follow unblocked",slider.IsKeyboardFocusWithin && !Blocked());
        bool captured=slider.CaptureMouse();check("Stage5 actual slider mouse capture blocks follow",captured && Mouse.Captured==slider && Blocked());slider.ReleaseMouseCapture();check("Stage5 capture release clears follow blocker",!Blocked());
        Invoke("OpenAudioMenu");await Task.Delay(100);var menu=(ContextMenu)Field("audioMenu");check("Stage5 actual audio menu popup blocks follow",menu.IsOpen && Blocked());menu.IsOpen=false;await Task.Delay(100);check("Stage5 menu close clears follow blocker",!Blocked());host.Close();island.Show();island.Collapse();island.PreviewActivity();island.SelectPeek("stopwatch");
        island.ToggleVisibility();int updates=main.Refreshes;int frames=island.LiveFrames;await Task.Delay(1100);check("Stage5 manual hide stops stopwatch view updates and visual frames",main.Refreshes==updates && island.LiveFrames==frames);island.ToggleVisibility();
        var settings=new SettingsWindow(app);settings.Show();typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Content"});await Task.Delay(200);Capture(settings,"stage5-settings-content");check("Stage5 Settings has exact shared Stopwatch controls",Diagnostics.Descendants(settings).OfType<StopwatchView>().Any());typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Layout"});await Task.Delay(200);foreach(var details in Diagnostics.LogicalDescendants(settings).OfType<Expander>().Where(e=>Diagnostics.LogicalDescendants(e).OfType<TextBlock>().Any(t=>t.Text.Contains("Follow cursor slides after 250 ms"))))details.IsExpanded=true;settings.UpdateLayout();Capture(settings,"stage5-settings-follow-default");check($"Stage5 Layout explains follow delay and preserves explicit Follow selection (mode={s.DisplayMode}, selected={Diagnostics.Descendants(settings).OfType<ComboBox>().FirstOrDefault(box=>AutomationProperties.GetName(box)=="Display mode")?.SelectedIndex}, help={Diagnostics.Descendants(settings).OfType<TextBlock>().Any(t=>t.Text.Contains("250 ms"))})",Diagnostics.Descendants(settings).OfType<ComboBox>().Any(box=>AutomationProperties.GetName(box)=="Display mode" && box.SelectedIndex==2) && Diagnostics.Descendants(settings).OfType<TextBlock>().Any(t=>t.Text.Contains("Follow cursor slides after 250 ms")));settings.Width=760;settings.Height=600;await Task.Delay(150);Capture(settings,"stage5-settings-follow-small");settings.Close();
        app.Session.Stopwatch.Reset();app.SaveSession();
    }
}

