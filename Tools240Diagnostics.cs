using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal static class Tools240Diagnostics
{
    internal static async Task Run(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var p=app.Productivity;var s=app.Settings;overlay.DiagnosticPointerPresent=true;
        void Check(string name,bool ok)=>check("Tools 2.4: "+name,ok);
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        try
        {
            s.ReduceMotion=true;s.TwoStageHover=false;s.ClipboardHistory=true;s.HoverHeight=192;s.PanelWidth=514;s.HideMaximized=s.HideFullscreen=false;
            foreach(var view in s.HoverViews)view.Enabled=true;app.ChangeSettings();
            p.Note="Fixture note";p.AddTask(new string('W',140));p.AddTask("Review the draft");p.IngestText("Clipboard preview fixture");
            foreach(double width in new[]{300d,350d})
            {
                s.HoverWidth=width;app.ChangeSettings();overlay.Collapse();overlay.PreviewActivity();
                foreach(string page in new[]{"tasks","clips"})
                {overlay.SelectPeek(page);await Task.Delay(120);Check(page+" hover fits "+width,overlay.HoverOverflow().Length==0);Capture(overlay,page+"-hover-"+width);}
            }
            overlay.SelectPeek("tasks");var task=Diagnostics.Descendants(overlay).OfType<CheckBox>().First(c=>AutomationProperties.GetName(c).StartsWith("Preview task "));task.IsChecked=true;task.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check("hover task updates the shared task",p.Tasks[0].Done);
            s.TwoStageHover=true;app.ChangeSettings();overlay.Collapse();overlay.PreviewActivity();await Task.Delay(1500);
            Check($"two-stage Tasks reveals both items without unsupported detachment (open={overlay.IsPeeking}, detailed={overlay.PeekDetailed}, page={overlay.PeekPage})",Diagnostics.Descendants(overlay).OfType<CheckBox>().Count(c=>c.IsVisible && AutomationProperties.GetName(c).StartsWith("Preview task "))==2 && !Diagnostics.Descendants(overlay).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="Detach preview"));
            s.Tools.Single(t=>t.Id=="clips").Enabled=false;app.ChangeSettings();Check("disabled Clipboard is omitted from hover pages",!overlay.HoverDeckIds.Contains("clips") && overlay.HoverDeckIds.Contains("tasks"));
            overlay.ShowTools("notes",false);await Task.Delay(100);var note=Diagnostics.Descendants(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Quick notes");note.Focus();note.UpdateLayout();
            var caret=note.GetRectFromCharacterIndex(0);Check("Notes caret uses a single content inset",!caret.IsEmpty && Math.Abs(caret.Left-note.Padding.Left-1)<3);Capture(overlay,"notes-aligned");
            var header=(Button)typeof(OverlayWindow).GetField("compact",flags)!.GetValue(overlay)!;Check("expanded panel omits the duplicate media header",!header.IsVisible);overlay.Collapse();await Task.Delay(50);Check("closed island restores media header",header.Visibility==Visibility.Visible);
            p.SetClipboardEnabled(false);s.ClipboardHistory=false;Check("disabling clipboard clears the open hover preview",!Diagnostics.Descendants(overlay).OfType<TextBlock>().Any(t=>t.Text=="Clipboard preview fixture"));
            foreach(var tool in s.Tools)tool.Enabled=false;s.ActivitiesEnabled=false;app.ChangeSettings();overlay.Reveal(false);overlay.SelectPage("access",false);Check("disabled Activities disappears from Launch",!Diagnostics.Descendants(overlay).OfType<Button>().Any(b=>b.IsVisible && AutomationProperties.GetName(b)=="Activities"));
            overlay.ShowTools("home",false);Check("hidden tools leave a helpful empty state",Diagnostics.Descendants(overlay).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text.StartsWith("All tools are hidden")));Check("hiding features preserves notes and tasks",p.Note=="Fixture note" && p.Tasks.Count==2);
            foreach(var tool in s.Tools)tool.Enabled=true;s.ActivitiesEnabled=true;app.ChangeSettings();overlay.OpenMixer();await Task.Delay(200);Capture(overlay,"audio-icons");
            var settings=new SettingsWindow(app);settings.Show();
            try
            {
                foreach(string page in new[]{"Widgets","Controls"}){typeof(SettingsWindow).GetMethod("Navigate",flags)!.Invoke(settings,new object[]{page});await Task.Delay(100);Capture(settings,"settings-"+page);}
                Check("Controls exposes maximized tucking",Diagnostics.Descendants(settings).OfType<CheckBox>().Any(c=>AutomationProperties.GetName(c)=="Tuck above maximized apps"));
            }
            finally{settings.Close();}
        }
        catch(Exception ex){Check("suite completed: "+ex,false);}
    }
}
