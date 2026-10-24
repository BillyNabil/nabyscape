using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

/// <summary>2.3.4 UI checks using only the temporary diagnostic profile and injected media/clipboard fixtures.</summary>
internal static class RefinementDiagnostics
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        void Check(string name,bool value)=>check("Refinement 2.3.4: "+name,value);
        // Do not even construct a fixture unless clipboard ingestion is detached from Windows.
        if(typeof(ProductivityService).GetField("_nativeClipboard",Private)?.GetValue(app.Productivity) is not false)
        {Check("requires native-free diagnostic profile",false);return;}
        if(Application.Current.Windows.OfType<DetachedPreviewWindow>().Any())
        {Check("requires no existing detached windows",false);return;}
        var output=Path.Combine(directory,"refinement234");Directory.CreateDirectory(output);
        var settings=JsonSerializer.Serialize(app.Settings);var p=app.Productivity;
        bool hidden=overlay.UserHidden,diagnostic=overlay.DiagnosticRun;
        bool pointer=overlay.DiagnosticPointerPresent;
        bool clipboardEnabled=p.ClipboardEnabled,paused=p.ClipboardPaused,motion=Motion.Enabled;
        var clips=p.Clips.ToArray();var shelf=p.Shelf.ToArray();string note=p.Note;
        var selection=(HashSet<string>)Field(overlay,"shelfSelection")!;var selectedFiles=selection.ToArray();
        var selectedClip=Field(overlay,"selectedClipId");
        var savedFiles=new[]{"settings.json","productivity.json","detached-notes.json","detached-system.json"}
            .ToDictionary(n=>Path.Combine(app.Store.DirectoryPath,n),n=>File.Exists(Path.Combine(app.Store.DirectoryPath,n))?File.ReadAllBytes(Path.Combine(app.Store.DirectoryPath,n)):null);
        var cache=Path.Combine(app.Store.DirectoryPath,"clipboard-images");
        var images=Directory.Exists(cache)?Directory.GetFiles(cache,"*.png").ToDictionary(f=>f,File.ReadAllBytes):new Dictionary<string,byte[]>();
        SettingsWindow? settingsWindow=null;var created=new List<Window>();
        async Task Settle(int milliseconds=360){await Task.Delay(milliseconds);overlay.UpdateLayout();}
        void Fits(string name,FrameworkElement element){string overflow=HorizontalOverflow(element);Check(name+(overflow.Length==0?"":": "+overflow),overflow.Length==0);}
        void ResetWheel()=>((PeekWheelAccumulator)Field(overlay,"panelWheel")!).Reset();
        void Click(DependencyObject root,string name)=>Tree(root).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            overlay.DiagnosticRun=true;overlay.DiagnosticPointerPresent=false;
            if(hidden)overlay.ToggleVisibility();overlay.Collapse();
            var s=app.Settings;s.ReduceMotion=true;s.CompactLayout=true;s.PanelSwipe=true;
            s.HideFullscreen=false;s.HideMaximized=false;s.DetachablePreviews=true;s.HoverPeek=true;s.Horizontal=50;s.Width=240;s.PanelWidth=460;s.PanelMaxHeight=520;
            foreach(var widget in s.Widgets)widget.Enabled=true;foreach(var view in s.HoverViews)view.Enabled=true;
            s.ReduceMotion=true;app.ChangeSettings();Motion.Enabled=false;overlay.Reveal(false);overlay.SelectPage("media",false);await Settle();
            var normalPanelText=Tree(overlay).OfType<TextBlock>().First(t=>t.IsVisible && t.ActualWidth>0);
            string before=overlay.ActivePage;ResetWheel();bool handled=overlay.RoutePanelWheel(-120,false,normalPanelText);
            Check("normal wheel on fitting panel changes page",handled && overlay.ActivePage!=before);
            before=overlay.ActivePage;ResetWheel();for(int i=0;i<4;i++)overlay.RoutePanelWheel(30,true,new TextBlock());
            Check("horizontal trackpad deltas accumulate into one page change",overlay.ActivePage!=before);
            overlay.OpenFeaturePage("notes");await Settle();
            var editor=Tree(overlay).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Quick notes");editor.Focus();before=overlay.ActivePage;ResetWheel();
            Check("focused text editor keeps its vertical wheel",!overlay.RoutePanelWheel(-120,false,editor) && overlay.ActivePage==before);
            var rows=new StackPanel();for(int i=0;i<24;i++)rows.Children.Add(new TextBlock{Text="Native scrolling row "+i,Height=24});
            var longList=new ScrollViewer{Width=240,Height=96,Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            longList.Measure(new Size(240,96));longList.Arrange(new Rect(0,0,240,96));longList.UpdateLayout();before=overlay.ActivePage;ResetWheel();
            bool nativeScroll=longList.ScrollableHeight>1 && !overlay.RoutePanelWheel(-120,false,rows.Children[0]) && overlay.ActivePage==before;
            longList.LineDown();longList.UpdateLayout();Check("long list preserves native vertical scrolling",nativeScroll && longList.VerticalOffset>0);
            ResetWheel();Check("horizontal gesture can leave a long list",overlay.RoutePanelWheel(120,true,rows.Children[0]) && overlay.ActivePage!=before);
            s.PanelSwipe=false;before=overlay.ActivePage;ResetWheel();Check("PanelSwipe off leaves wheel unhandled",!overlay.RoutePanelWheel(-120,false,new TextBlock()) && !overlay.RoutePanelWheel(120,true,new TextBlock()) && overlay.ActivePage==before);s.PanelSwipe=true;
            var dragChild=new TextBlock();var dragTile=new Border{Tag="file-drag",Child=dragChild};
            var blocked=typeof(OverlayWindow).GetMethod("PanelGestureBlocked",BindingFlags.Static|BindingFlags.NonPublic)!;
            Check("file drag tile and descendant block panel drag",(bool)blocked.Invoke(null,new object[]{dragTile})! && (bool)blocked.Invoke(null,new object[]{dragChild})! && !(bool)blocked.Invoke(null,new object[]{new TextBlock()})!);

            // Compact source-of-truth shelf and clipboard surfaces, without drag/drop, copy or open actions.
            var fixture=Path.Combine(output,"Review notes with a longer filename.txt");File.WriteAllText(fixture,"Local diagnostic shelf fixture.");
            var imageFile=Path.Combine(output,"Color sample.png");File.WriteAllBytes(imageFile,SamplePng());p.Shelf.Clear();p.AddFiles(new[]{fixture,imageFile});
            overlay.ShowTools("files",false);await Settle();
            var selector=Tree(overlay).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Select "+Path.GetFileName(fixture));selector.IsChecked=true;
            Check("shelf uses TaskToggle and selecting updates shared selection",ReferenceEquals(selector.Style,app.FindResource("TaskToggle")) && selection.Contains(fixture));
            var actualDragTile=Tree(overlay).OfType<Border>().First(b=>b.Tag as string=="file-drag");
            Check("rendered shelf drag surface blocks panel drag",(bool)blocked.Invoke(null,new object[]{actualDragTile})!);
            Fits("shelf rows fit their horizontal viewport",overlay);Capture(overlay,Path.Combine(output,"shelf-selected.png"));
            s.ClipboardHistory=true;app.ChangeSettings();p.ClipboardPaused=false;p.ClearClips();p.IngestText("A thought worth keeping for the next session.");p.IngestText("https://example.com/design?keep=yes&utm_source=review#draft");p.IngestText("#8E9AAF");p.IngestImage(SamplePng());
            overlay.ShowTools("clips",false);await Settle();
            var pause=Tree(overlay).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Pause capture");
            Check("clipboard capture uses SettingsSwitch",ReferenceEquals(pause.Style,app.FindResource("SettingsSwitch")));
            pause.IsChecked=true;pause.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check("pause switch prevents fixture ingestion",p.ClipboardPaused && !p.IngestText("must not be captured"));
            pause=Tree(overlay).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Pause capture");pause.IsChecked=false;pause.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check("pause switch resumes capture",!p.ClipboardPaused);
            Check("clipboard has text link color and image rows",new[]{"View Text","View Link","View Color","View Image"}.All(name=>Tree(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==name)));
            Fits("clipboard rows fit their horizontal viewport",overlay);Capture(overlay,Path.Combine(output,"clipboard-rows.png"));
            Click(overlay,"View Image");await Settle();Capture(overlay,Path.Combine(output,"clipboard-image-detail.png"));
            Check("image detail exposes save without touching native clipboard",Tree(overlay).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Save as PNG…"));

            var screen=Native.ChooseScreen(s);var work=screen.WorkingArea;var anchor=new Point(work.Left+work.Width*.55,work.Top+work.Height*.3);
            foreach(string kind in new[]{"notes","system"})
            {
                app.Previews.OpenAt(kind,anchor,false);var window=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single(w=>w.Title=="NabyScape · "+DetachedPreviewWindow.NameFor(kind));created.Add(window);await Settle();
                Check(kind+" OpenAt uses accessible custom resizable chrome",window.WindowStyle==WindowStyle.None && window.ResizeMode==ResizeMode.CanResize && AutomationProperties.GetName(window)==window.Title && Tree(window).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Close "+DetachedPreviewWindow.NameFor(kind)+" · Alt+F4"));
                var origin=window.PointToScreen(new Point());var dpi=VisualTreeHelper.GetDpi(window);
                var actual=new Rect(origin.X,origin.Y,window.ActualWidth*dpi.DpiScaleX,window.ActualHeight*dpi.DpiScaleY);
                Check(kind+" OpenAt stays near supplied anchor within monitor work area",actual.Left>=work.Left-2 && actual.Top>=work.Top-2 && actual.Right<=work.Right+2 && actual.Bottom<=work.Bottom+2 && actual.Contains(anchor));
                Fits(kind+" default controls fit scroll viewport",window);Capture(window,Path.Combine(output,"detached-"+kind+"-default.png"));
                window.Width=window.MinWidth;window.Height=window.MinHeight;await Settle();Fits(kind+" minimum controls fit scroll viewport",window);Capture(window,Path.Combine(output,"detached-"+kind+"-minimum.png"));
                // An edge anchor exercises physical clamping on the current monitor.
                app.Previews.OpenAt(kind,new Point(work.Right-4,work.Bottom-4),false);await Settle();origin=window.PointToScreen(new Point());dpi=VisualTreeHelper.GetDpi(window);
                Check(kind+" edge anchor is clamped onscreen",origin.X>=work.Left-2 && origin.Y>=work.Top-2 && origin.X+window.ActualWidth*dpi.DpiScaleX<=work.Right+2 && origin.Y+window.ActualHeight*dpi.DpiScaleY<=work.Bottom+2);
                window.Close();
            }
            settingsWindow=new SettingsWindow(app);settingsWindow.Show();Diagnostics.NavigateSettings(settingsWindow,"Appearance");await Settle();
            Fits("Appearance controls fit their scrolling form",settingsWindow);Capture(settingsWindow,Path.Combine(output,"settings-appearance.png"));
            var form=Tree(settingsWindow).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");
            var controlsHeading=Tree(settingsWindow).OfType<TextBlock>().Single(t=>t.Text=="Buttons & input fields");controlsHeading.BringIntoView();await Settle();Capture(settingsWindow,Path.Combine(output,"settings-control-colors.png"));
            form.ScrollToTop();Click(settingsWindow,"Preview Panel");
            var examples=Tree(settingsWindow).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Preview content");
            foreach(string example in new[]{"Control colors","Typography","Panel navigation","Drop zone","Detached preview","Cover gestures","Smart clipboard"})
            {
                examples.SelectedItem=example;await Settle();var preview=(Border)Field(settingsWindow,"preview")!;
                Check("Settings preview selects and renders "+example,examples.SelectedItem?.ToString()==example && preview.Child!=null && preview.ActualWidth>0);
                Fits("Settings "+example+" controls fit horizontal viewport",settingsWindow);
                Capture(settingsWindow,Path.Combine(output,"settings-example-"+example.ToLowerInvariant().Replace(' ','-')+".png"));
            }
        }
        catch(Exception ex){Check("suite completed without exception: "+ex,false);app.Store.Log(ex);}
        finally
        {
            settingsWindow?.Close();foreach(var window in created.Where(w=>Application.Current.Windows.Cast<Window>().Contains(w)).ToArray())window.Close();
            overlay.Collapse();overlay.DiagnosticPointerPresent=pointer;
            var restored=JsonSerializer.Deserialize<Settings>(settings)!;foreach(var property in typeof(Settings).GetProperties().Where(prop=>prop.CanRead && prop.CanWrite))property.SetValue(app.Settings,property.GetValue(restored));
            app.ChangeSettings();p.SetClipboardEnabled(clipboardEnabled);p.ClipboardPaused=paused;p.Clips.Clear();
            if(Directory.Exists(cache))foreach(var file in Directory.GetFiles(cache,"*.png").Where(file=>!images.ContainsKey(file)))File.Delete(file);
            if(images.Count>0)Directory.CreateDirectory(cache);foreach(var image in images)File.WriteAllBytes(image.Key,image.Value);
            p.Clips.AddRange(clips);p.Shelf.Clear();p.Shelf.AddRange(shelf);p.Note=note;p.Save();selection.Clear();foreach(var path in selectedFiles)selection.Add(path);typeof(OverlayWindow).GetField("selectedClipId",Private)!.SetValue(overlay,selectedClip);
            if(overlay.UserHidden!=hidden)overlay.ToggleVisibility();overlay.DiagnosticRun=diagnostic;Motion.Enabled=motion;
            foreach(var file in savedFiles)if(file.Value==null){if(File.Exists(file.Key))File.Delete(file.Key);}else File.WriteAllBytes(file.Key,file.Value);
        }
    }
    private static object? Field(object target,string name)=>target.GetType().GetField(name,Private)!.GetValue(target);
    private static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private)!.Invoke(target,args);
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)=>Diagnostics.Descendants(root);
    // Vertical overflow is expected inside scrolling content; check width against the actual presenter,
    // including its reserved scrollbar gutter, rather than mistaking offscreen rows for clipped controls.
    private static string HorizontalOverflow(FrameworkElement root)
    {
        var failures=new List<string>();
        foreach(var control in Tree(root).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e is Button or TextBox or CheckBox or ComboBox or Slider))
        {
            FrameworkElement host=root;
            for(var parent=VisualTreeHelper.GetParent(control);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is ScrollContentPresenter presenter){host=presenter;break;}
            var bounds=control.TransformToAncestor(host).TransformBounds(new Rect(control.RenderSize));
            if(host is ScrollContentPresenter && host.TemplatedParent is ScrollViewer horizontal && horizontal.HorizontalScrollBarVisibility!=ScrollBarVisibility.Disabled)continue;
            if(bounds.Left< -1 || bounds.Right>host.ActualWidth+1)failures.Add(AutomationProperties.GetName(control)+" "+bounds+" / "+host.ActualWidth);
        }
        return string.Join("; ",failures);
    }
    private static byte[] SamplePng()
    {
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{175,154,142,255,105,87,64,255,64,87,105,255,142,154,175,255},8);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    private static void Capture(FrameworkElement target,string path)
    {
        target.UpdateLayout();var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(target.ActualWidth)),Math.Max(1,(int)Math.Ceiling(target.ActualHeight)),96,96,PixelFormats.Pbgra32);image.Render(target);BitmapSource result=image;
        if(target is OverlayWindow overlay){int width=Math.Min(image.PixelWidth,Math.Max(1,(int)Math.Ceiling(overlay.PaintedWidth))),height=Math.Min(image.PixelHeight,Math.Max(1,(int)Math.Ceiling(overlay.PaintedHeight)));result=new CroppedBitmap(image,new Int32Rect((image.PixelWidth-width)/2,0,width,height));}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));using var stream=File.Create(path);encoder.Save(stream);
    }
}
