using System;
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
internal static class PremiumDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        string snapshot=JsonSerializer.Serialize(app.Settings);SettingsWindow? settings=null;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Field(object target,string name)=>target.GetType().GetField(name,flags)?.GetValue(target);
        void Check(string name,bool ok)=>check("Settings 2.4.1: "+name,ok);
        void Capture(FrameworkElement element,string name,double scale=1)
        {
            element.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*scale),(int)Math.Ceiling(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bmp.Render(element);
            if(element==island){int width=(int)Math.Ceiling(island.PaintedWidth*scale),height=(int)Math.Ceiling(island.PaintedHeight*scale);var crop=new CroppedBitmap(bmp,new Int32Rect(Math.Max(0,(bmp.PixelWidth-width)/2),0,Math.Min(width,bmp.PixelWidth),Math.Min(height,bmp.PixelHeight)));Write(crop,name);}else Write(bmp,name);
        }
        void Write(BitmapSource bitmap,string name){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);}
        try
        {
            var s=app.Settings;s.CompactLayout=true;s.ReduceMotion=true;s.PanelWidth=514;s.PanelMaxHeight=504;s.PanelScale=1;s.LiveActivity=true;s.HideFullscreen=s.HideMaximized=false;
            foreach(var widget in s.Widgets)widget.Enabled=true;app.ChangeSettings();
            Check("closed and Status clocks use bold numerals",((TextBlock)Field(island,"compactClock")!).FontWeight==FontWeights.Bold && ((TextBlock)Field(island,"clock")!).FontWeight==FontWeights.Bold);
            foreach(double width in new[]{514d,340d})
            {
                s.PanelWidth=width;app.ChangeSettings();island.Reveal(false);
                foreach(string page in new[]{"media","focus","status"})
                {
                    island.SelectPage(page,false);await Task.Delay(180);island.UpdateLayout();
                    Capture(island,$"premium-{page}-{width}");
                    if(page=="status")
                    {
                        island.SelectMonitorTab("hardware");await Task.Delay(2300);Capture(island,$"premium-hardware-{width}");
                        Check("monitor hides scrollbar at width "+width,Tree(island).OfType<ScrollViewer>().Where(v=>v.IsVisible).All(v=>v.VerticalScrollBarVisibility==ScrollBarVisibility.Hidden));
                    }
                    else {Check(page+" controls fit at width "+width,PanelControlsFit(island));if(page=="focus")Check("Start labels stay readable at width "+width,Tree(island).OfType<Button>().Where(b=>b.IsVisible && b.Content?.ToString()=="Start").All(b=>b.ActualWidth>=44));}
                }
            }
            settings=new SettingsWindow(app);settings.Show();await Task.Delay(160);
            Check("Settings uses custom resizable chrome",settings.WindowStyle==WindowStyle.None && settings.ResizeMode==ResizeMode.CanResize && new[]{"Minimize Settings","Maximize Settings","Close Settings"}.All(name=>Tree(settings).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==name)));
            foreach(double width in new[]{1100d,640d,480d})
            {
                settings.Width=width;settings.Height=780;
                foreach(string category in new[]{"Appearance","Layout","Content","Controls","Audio","System","Launch","Application"})
                {
                    typeof(SettingsWindow).GetMethod("Navigate",flags)!.Invoke(settings,new object[]{category});await Task.Delay(60);settings.UpdateLayout();
                    Check(category+" has a section index and contextual illustrations at width "+width,Tree(settings).Any(n=>AutomationProperties.GetName(n)=="Settings section index") && Tree(settings).OfType<SettingsScene>().Any());
                    Check(category+" form controls fit at width "+width,SettingsFit(settings));
                    var index=Tree(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Settings section index");
                    var lastSection=(Border)((ComboBoxItem)index.Items[index.Items.Count-1]).Tag;
                    index.SelectedIndex=index.Items.Count-1;await Task.Delay(60);
                    var form=Tree(settings).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");
                    Check(category+" section picker navigates the form at width "+width,Math.Abs(form.VerticalOffset-Math.Min(form.ScrollableHeight,lastSection.TranslatePoint(new Point(),(StackPanel)form.Content).Y+((StackPanel)form.Content).Margin.Top))<2);
                    Check(category+" section picker remains visible at width "+width,index.TransformToAncestor(settings).Transform(new Point()).Y>=0 && index.TransformToAncestor(settings).Transform(new Point()).Y+index.ActualHeight<settings.ActualHeight);
                    index.SelectedIndex=1;await Task.Delay(80);
                    index.SelectedIndex=index.Items.Count-1;await Task.Delay(80);
                    Check(category+" can jump repeatedly",Math.Abs(form.VerticalOffset-Math.Min(form.ScrollableHeight,lastSection.TranslatePoint(new Point(),(StackPanel)form.Content).Y+((StackPanel)form.Content).Margin.Top))<2);
                    form.ScrollToVerticalOffset(Math.Min(220,form.ScrollableHeight));await Task.Delay(20);
                    double beforeSave=form.VerticalOffset;var pageInstance=form.Content;
                    var toggle=Tree(form).OfType<CheckBox>().FirstOrDefault(c=>AutomationProperties.GetName(c)!="Start automatically when signing in to Windows");
                    if(toggle!=null){bool? originalToggle=toggle.IsChecked;toggle.IsChecked=!originalToggle;toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));await Task.Delay(260);Check(category+" applying a toggle preserves scroll and controls",ReferenceEquals(pageInstance,form.Content) && Math.Abs(form.VerticalOffset-beforeSave)<2);toggle.IsChecked=originalToggle;toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));await Task.Delay(220);}
                    if(category=="Content")
                    {
                        var move=Tree(form).OfType<Button>().First(b=>AutomationProperties.GetName(b).StartsWith("Move down ") && b.IsEnabled);double beforeOrder=form.VerticalOffset;move.Focus();beforeOrder=form.VerticalOffset;move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                        Check("reordering Content preserves scroll at width "+width,Math.Abs(form.VerticalOffset-beforeOrder)<2);
                    }
                    if(category=="Launch")
                    {
                        var draft=Tree(form).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Shortcut name");draft.Text="Fixture draft";draft.Focus();draft.Select(4,3);await Task.Delay(80);double editOffset=form.VerticalOffset;
                        typeof(SettingsWindow).GetMethod("Save",flags)!.Invoke(settings,null);await Task.Delay(260);
                        Check("saving preserves shortcut draft, caret and focus at width "+width,draft.Text=="Fixture draft" && draft.SelectionStart==4 && draft.SelectionLength==3 && draft.IsKeyboardFocused && Math.Abs(form.VerticalOffset-editOffset)<2);
                    }
                    var slider=Tree(form).OfType<Slider>().FirstOrDefault();if(slider!=null){double originalValue=slider.Value,beforeSlider=form.VerticalOffset;slider.Value=Math.Min(slider.Maximum,slider.Value+slider.TickFrequency);await Task.Delay(230);Check(category+" slider updates preserve scroll",Math.Abs(form.VerticalOffset-beforeSlider)<2);slider.Value=originalValue;}
                    if(width==1100)
                    {
                        index=Tree(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Settings section index");
                        foreach(var option in index.Items.OfType<ComboBoxItem>().Where(i=>i.Tag is Border).ToArray())
                        {
                            index.SelectedItem=option;await Task.Delay(80);
                            var destination=(Border)option.Tag;var expected=Math.Min(form.ScrollableHeight,destination.TranslatePoint(new Point(),(StackPanel)form.Content).Y+((StackPanel)form.Content).Margin.Top);
                            Check(category+" jump aligns "+option.Content,Math.Abs(form.VerticalOffset-expected)<2);
                            if(option.Content?.ToString() is "Hover pages" or "Clipboard history" or "Typography" or "Closed media display" or "Detachable previews" or "Automatic tucking")Capture(settings,"settings-detail-"+option.Content.ToString()!.ToLowerInvariant().Replace(' ','-'));
                        }
                    }
                    form.ScrollToTop();await Task.Delay(30);
                    if(width==1100 || category is "Controls" or "Application")Capture(settings,$"premium-settings-{category.ToLowerInvariant()}-{width}");
                }
            }
            settings.Width=1100;settings.Height=780;
            foreach(double scale in new[]{1.5,2d})Capture(settings,"premium-settings-scale-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture),scale);
            settings.WindowState=WindowState.Maximized;await Task.Delay(100);Check("Settings maximizes and keeps a restore action",settings.WindowState==WindowState.Maximized && Tree(settings).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Restore Settings"));settings.WindowState=WindowState.Normal;
        }
        catch(Exception ex){Check("diagnostics completed: "+ex,false);}
        finally
        {
            settings?.Close();island.Collapse();var original=JsonSerializer.Deserialize<Settings>(snapshot)!;foreach(var p in typeof(Settings).GetProperties().Where(p=>p.CanRead && p.CanWrite))p.SetValue(app.Settings,p.GetValue(original));app.ChangeSettings();
        }
    }
    private static bool SettingsFit(SettingsWindow settings)
    {
        var viewport=Tree(settings).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");var presenter=Tree(viewport).OfType<ScrollContentPresenter>().First();
        return Tree(viewport).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e is Button or Slider or ComboBox or CheckBox or ColorField).All(e=>{var r=e.TransformToAncestor(presenter).TransformBounds(new Rect(e.RenderSize));return r.Left>=-.5 && r.Right<=presenter.ActualWidth+.5;});
    }
    private static bool PanelControlsFit(OverlayWindow island)
    {
        var body=(StackPanel)typeof(OverlayWindow).GetField("body",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
        return Tree(body).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e is Button or TextBox or Slider or ComboBox).All(e=>{var r=e.TransformToAncestor(body).TransformBounds(new Rect(e.RenderSize));return r.Left>=-.5 && r.Right<=body.ActualWidth+.5;});
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Tree(DependencyObject node)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var descendant in Tree(child))yield return descendant;}}
}
