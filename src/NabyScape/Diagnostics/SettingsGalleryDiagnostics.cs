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

// Renders every Settings category of an isolated profile so layout reviews compare real pages.
internal static class SettingsGalleryDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();app.Settings.SettingsMaterial="Solid";app.Settings.ReduceMotion=true;app.ChangeSettings();
        var window=new SettingsWindow(app){Width=1100,Height=780};window.Show();await Task.Delay(300);
        var navigate=typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var categories=(System.Collections.IEnumerable)typeof(SettingsWindow).GetField("SettingsCategories",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        // Freeze monitor containment so tall full-page captures are not clipped to the screen.
        var fitting=typeof(SettingsWindow).GetField("fittingBounds",BindingFlags.Instance|BindingFlags.NonPublic)!;fitting.SetValue(window,true);window.MaxHeight=double.PositiveInfinity;
        try
        {
            foreach(var category in categories)
            {
                string id=(string)category.GetType().GetProperty("Id")!.GetValue(category)!;
                navigate.Invoke(window,new object[]{id});await Task.Delay(120);window.UpdateLayout();
                Save(window,window.ActualWidth,window.ActualHeight,Path.Combine(directory,"settings-page-"+id+".png"));
                var page=(FrameworkElement)typeof(SettingsWindow).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                Save(page,page.ActualWidth,page.ActualHeight,Path.Combine(directory,"settings-form-"+id+".png"),true);
                var form=Diagnostics.Descendants(window).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Settings form viewport");var origin=page.TransformToAncestor(form).Transform(new Point());
                check($"Settings gallery {id}: page {origin.X:0}+{page.ActualWidth:0}+{page.Margin.Right:0} within viewport {form.ViewportWidth:0} of {form.ActualWidth:0}",origin.X+page.ActualWidth<=form.ViewportWidth+.5);
            }
            // Narrow window: the sidebar becomes a category chooser above the page.
            foreach(var size in new[]{(640d,700d),(480d,420d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(120);navigate.Invoke(window,new object[]{"Appearance"});await Task.Delay(80);window.UpdateLayout();
                Save(window,window.ActualWidth,window.ActualHeight,Path.Combine(directory,$"settings-narrow-{size.Item1:0}.png"));
                var narrowPage=(FrameworkElement)typeof(SettingsWindow).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                var chooser=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Settings category");
                check($"Narrow {size.Item1:0}: chooser visible and page laid out (page {narrowPage.ActualWidth:0}, window {window.ActualWidth:0})",chooser.IsVisible && narrowPage.ActualWidth>0);
            }
            window.Width=1100;window.Height=780;await Task.Delay(80);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var settingsPage=(Panel)typeof(SettingsWindow).GetField("page",flags)!.GetValue(window)!;
            string[] Sections()=>settingsPage.Children.OfType<Border>().Select(b=>b.Tag as string).Where(t=>t!=null).ToArray()!;
            Button Nav(string id)=>((System.Collections.Generic.Dictionary<string,Button>)typeof(SettingsWindow).GetField("navigationButtons",flags)!.GetValue(window)!)[id];
            navigate.Invoke(window,new object[]{"Appearance"});window.UpdateLayout();
            check("Essentials: Look & colors shows shape and colors only",Sections().SequenceEqual(new[]{"Shape","Color & material"}));
            check("Essentials: detailed pages leave the sidebar",Nav("Motion").Visibility!=Visibility.Visible && Nav("Controls").Visibility!=Visibility.Visible && Nav("Appearance").Visibility==Visibility.Visible);
            check("Essentials: a disclosure card offers every setting",Diagnostics.Descendants(settingsPage).OfType<Button>().Any(b=>AutomationProperties.GetAutomationId(b)=="Show all settings"));
            navigate.Invoke(window,new object[]{"Content"});window.UpdateLayout();
            check("Essentials: clipboard capture stays reachable from widgets",Sections().Contains("Clipboard history"));
            var open=typeof(SettingsWindow).GetMethod("OpenSettingsSearchResult",flags)!;
            open.Invoke(window,new object[]{new SettingsSearchItem("Quiet hours","Notifications","Notification presentation","","quiet")});window.UpdateLayout();
            check("Essentials: search opens a detailed section directly",Sections().Contains("Notification presentation"));
            navigate.Invoke(window,new object[]{"Appearance"});window.UpdateLayout();
            check("Essentials: leaving a search result restores the essentials",Sections().Length==2);
            typeof(SettingsWindow).GetMethod("SetShowAllSettings",flags)!.Invoke(window,new object[]{true});window.UpdateLayout();
            check("Show all: every Look & colors section returns",Sections().Contains("Typography") && Sections().Contains("Surface") && Nav("Motion").Visibility==Visibility.Visible && app.Settings.SettingsShowAll);
            typeof(SettingsWindow).GetMethod("SetShowAllSettings",flags)!.Invoke(window,new object[]{false});window.UpdateLayout();
            check("Show essentials only returns to the short page",Sections().Length==2 && !app.Settings.SettingsShowAll);
        }
        finally{fitting.SetValue(window,false);window.Close();}
    }
    private static void Save(Visual visual,double width,double height,string path,bool brush=false)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(Math.Min(height,8000)),96,96,PixelFormats.Pbgra32);
        if(brush)
        {
            var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(UI.Brush("#111214"),null,new Rect(0,0,width,height));context.DrawRectangle(new VisualBrush(visual){Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top},null,new Rect(0,0,width,height));}
            bitmap.Render(drawing);
        }
        else bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
