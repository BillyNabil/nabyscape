using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class Stage1Diagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        void Check(string name,bool pass)=>check("Stage 1: "+name,pass);
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;
        TextBox Entry(string name)=>Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
        bool HasEntry(string name)=>Diagnostics.Descendants(island).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)==name);
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,"stage1-"+name+".png"));encoder.Save(file);
        }
        var s=app.Settings;s.HideFullscreen=s.HideMaximized=false;s.ReduceMotion=true;s.SmartActivities=false;s.ActivitySatellite=false;
        foreach(var widget in s.Widgets)widget.Enabled=true;app.ChangeSettings();
        app.Media.DiagnosticFixture=false;island.Collapse();app.ChangeSettings();await Task.Delay(150);
        Check("idle has no brand label or waveform",island.HeaderLabel.Text=="" && Field<ActivityVisual>("activity").Visibility==Visibility.Collapsed && !island.ClosedTitleWaveVisible);
        Check("idle retains enabled clock",Field<TextBlock>("compactClock").Visibility==Visibility.Visible);
        s.Widgets.Single(w=>w.Id=="clock").Enabled=false;app.ChangeSettings();await Task.Delay(100);
        Check("idle respects disabled clock",island.HeaderLabel.Text=="" && Field<TextBlock>("compactClock").Visibility==Visibility.Collapsed);
        s.Widgets.Single(w=>w.Id=="clock").Enabled=true;app.ChangeSettings();
        island.ShowTools("notes");await Task.Delay(100);
        Check("Notes contains only note editor without subtabs",HasEntry("Quick notes") && !HasEntry("New task") && !Diagnostics.Descendants(island).OfType<Button>().Any(b=>b.Content?.ToString() is "Write" or "Tasks"));
        var note=Entry("Quick notes");note.Text=string.Join("\n",Enumerable.Range(0,40).Select(i=>"Retained note line "+i));note.Focus();note.Select(4,7);note.ScrollToVerticalOffset(100);island.UpdateLayout();double noteScroll=note.VerticalOffset;
        s.CategoryStyle="Text";app.ChangeSettings();await Task.Delay(120);note=Entry("Quick notes");
        Check($"content rebuild retains note focus, selection and editor scroll (focus={note.IsKeyboardFocusWithin}, selection={note.SelectionStart}/{note.SelectionLength}, scroll={note.VerticalOffset}/{noteScroll})",note.IsKeyboardFocusWithin && note.SelectionStart==4 && note.SelectionLength==7 && Math.Abs(note.VerticalOffset-noteScroll)<1);
        typeof(OverlayWindow).GetMethod("EnsurePeekDeck",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(island,null);
        var mini=new DetachedPreviewView(app,"notes",()=>{});mini.SetVisible(true);
        try
        {
            var miniNote=Diagnostics.Descendants(mini.Content).OfType<TextBox>().FirstOrDefault();
            // The detached view can be inspected before it is attached to a window through its logical content.
            if(miniNote==null && mini.Content is ScrollViewer {Content:StackPanel panel})miniNote=panel.Children.OfType<TextBox>().FirstOrDefault();
            Check("detached note shares the existing document",miniNote?.Text==app.Productivity.Note);
            miniNote!.Text="Shared from detached";app.Productivity.Save();await Task.Delay(100);
            Check("detached edits synchronize main and hover note editors",Entry("Quick notes").Text=="Shared from detached" && Field<TextBox>("peekNote").Text=="Shared from detached");
            Field<TextBox>("peekNote").Text="Shared from hover";app.Productivity.Save();await Task.Delay(100);
            Check("hover edits synchronize main and detached note editors",Entry("Quick notes").Text=="Shared from hover" && miniNote.Text=="Shared from hover");
        }
        finally{mini.Dispose();}
        island.ShowTools("tasks");await Task.Delay(100);
        Check("Tasks contains only checklist without subtabs",HasEntry("New task") && !HasEntry("Quick notes") && !Diagnostics.Descendants(island).OfType<Button>().Any(b=>b.Content?.ToString() is "Write" or "Tasks"));
        Entry("New task").Text="Retained task draft";island.ShowTools("notes");island.ShowTools("tasks");
        Check("separate tools retain shared task draft",Entry("New task").Text=="Retained task draft");
        for(int i=0;i<18;i++)app.Productivity.AddTask("Stage 1 task "+i);
        island.ShowTools("tasks");await Task.Delay(120);var draft=Entry("New task");draft.Focus();draft.Select(2,5);var scroll=Field<ScrollViewer>("scroll");scroll.ScrollToVerticalOffset(140);island.UpdateLayout();double offset=scroll.VerticalOffset;
        s.CategoryStyle="IconsLabels";app.ChangeSettings();await Task.Delay(150);draft=Entry("New task");
        Check($"settings rebuild retains task draft focus, selection and outer scroll (focus={draft.IsKeyboardFocusWithin}, selection={draft.SelectionStart}/{draft.SelectionLength}, scroll={scroll.VerticalOffset}/{offset})",offset>0 && draft.Text=="Retained task draft" && draft.IsKeyboardFocusWithin && draft.SelectionStart==2 && draft.SelectionLength==5 && Math.Abs(scroll.VerticalOffset-offset)<1);
        using var settingsScope=new SettingsScope(new SettingsWindow(app));var settings=settingsScope.Window;settings.Show();await Task.Delay(120);
        settings.Width=480;settings.Height=300;await Task.Delay(150);settings.UpdateLayout();
        var chooser=Diagnostics.Descendants(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Settings category");
        var preview=Diagnostics.Descendants(settings).OfType<Expander>().Single(e=>AutomationProperties.GetName(e)=="Settings live preview");
        var form=Diagnostics.Descendants(settings).OfType<ScrollViewer>().Single(c=>AutomationProperties.GetName(c)=="Settings form viewport");
        Check("480x300 uses category chooser and collapsed preview with usable form",chooser.IsVisible && !preview.IsExpanded && form.ActualWidth>=300 && form.ActualHeight>=80);
        foreach(string category in new[]{"Appearance","Layout","Content","Controls","Audio","System","Launch","Application"})
        {chooser.SelectedItem=category;settings.UpdateLayout();Check("compact category is reachable: "+category,form.ExtentHeight>0 && form.ActualWidth>=300);}
        Capture(settings,"settings-480x300");
        settings.Width=900;settings.Height=680;await Task.Delay(100);Check("narrow preview can be expanded",!preview.IsExpanded);preview.IsExpanded=true;settings.UpdateLayout();Check("expanded narrow preview leaves scrolling form",preview.IsExpanded && form.ActualHeight>=80);Capture(settings,"settings-narrow-preview");
        settings.Width=1100;settings.Height=780;await Task.Delay(100);settings.UpdateLayout();
        Check("wide Settings displays sidebar, form and preview",!chooser.IsVisible && preview.IsExpanded && form.ActualWidth>=300);Capture(settings,"settings-wide");
        chooser.SelectedItem="Content";settings.UpdateLayout();Capture(settings,"widget-order");
    }
    private sealed class SettingsScope(SettingsWindow window):IDisposable
    {
        internal SettingsWindow Window=>window;
        public void Dispose()=>window.Close();
    }
}
