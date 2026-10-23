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
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class VisualPolishDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,flags)!.GetValue(island)!;
        var geometry=new List<object>();
        void Capture(FrameworkElement element,string name,bool crop=false,double dpi=1)
        {
            element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*dpi),(int)Math.Ceiling(element.ActualHeight*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);image.Render(element);
            BitmapSource source=image;if(crop){int w=Math.Min(image.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*dpi)),h=Math.Min(image.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*dpi));source=new CroppedBitmap(image,new Int32Rect(Math.Max(0,(image.PixelWidth-w)/2),0,w,h));}
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        var s=app.Settings;s.CompactLayout=true;s.CategoryStyle="Dots";s.PanelWidth=526;s.PanelMaxHeight=417;s.PanelPadding=32;s.PanelScale=1.04;s.Preset="Mac";
        s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;s.SmartActivities=false;s.ReduceMotion=true;s.ClipboardHistory=true;
        foreach(var widget in s.Widgets)widget.Enabled=true;
        app.Assistant.Options.Enabled=true;app.ChangeSettings();app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>.28f;
        typeof(MediaService).GetProperty(nameof(MediaService.Title))!.SetValue(app.Media,"After the rain");
        typeof(MediaService).GetProperty(nameof(MediaService.Artist))!.SetValue(app.Media,"Original interface sample");
        var picture=new DrawingGroup();using(var draw=picture.Open()){draw.DrawRectangle(new LinearGradientBrush(Color.FromRgb(49,76,83),Color.FromRgb(160,173,163),45),null,new Rect(0,0,80,80));draw.DrawEllipse(UI.Brush("#E2D8BE"),null,new Point(49,29),19,19);draw.DrawRectangle(UI.Brush("#31464A"),null,new Rect(0,57,80,23));}
        picture.Freeze();var art=new DrawingImage(picture);art.Freeze();typeof(MediaService).GetProperty(nameof(MediaService.Artwork))!.SetValue(app.Media,art);
        app.Productivity.Note="Give the idea a little room.\nKeep the details that matter.\n\nA local sample for the visual comparison.";
        app.Productivity.AddTask("Review the cover artwork");app.Productivity.AddTask("Keep the next step clear");
        for(int i=0;i<12;i++)app.Productivity.Clips.Add(new ClipItem{Text=i==0?"https://example.com/design-notes":"Local design note "+(i+1),Timestamp=new DateTimeOffset(2026,10,20,12,0,0,TimeSpan.Zero).AddMinutes(-i)});
        app.Productivity.Save();island.Reveal(false);await Task.Delay(100);
        var tabs=Field<Dictionary<string,Button>>("compactTabs");string[] originalPages={"voice","media","focus","stopwatch","status","access","files","notes","tasks","clips","calendar","awake","links","weather"};
        check("Existing page groups and dot navigation are retained",tabs.Keys.SequenceEqual(originalPages) && tabs.Values.All(b=>b.Content is System.Windows.Shapes.Rectangle));
        foreach(double width in new[]{526d,300d})
        {
            s.PanelWidth=width;app.ChangeSettings();await Task.Delay(50);
            foreach(string id in originalPages)
            {
                if(Settings.ToolNames.ContainsKey(id))island.ShowTools(id,false);else island.SelectPage(id,false);
                await Task.Delay(60);island.UpdateLayout();var outer=Field<ScrollViewer>("scroll");
                check($"{id} fits horizontal viewport at {width} DIP",island.ActivePage==id && outer.ScrollableWidth<1);
                Capture(island,$"{id}-{width}",true);
                geometry.Add(new{Page=id,Width=width,Controls=Diagnostics.Descendants(island).OfType<Control>().Where(c=>c.IsVisible && AutomationProperties.GetName(c).Length>0).Select(c=>new{Name=AutomationProperties.GetName(c),Type=c.GetType().Name,X=Math.Round(c.TranslatePoint(new Point(),island).X,2),Y=Math.Round(c.TranslatePoint(new Point(),island).Y,2),Width=Math.Round(c.ActualWidth,2),Height=Math.Round(c.ActualHeight,2)}).ToArray()});
            }
        }
        s.PanelWidth=526;app.ChangeSettings();island.ShowTools("notes",false);await Task.Delay(60);
        var note=Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Quick notes");note.Focus();note.Select(3,7);await Task.Delay(50);note.UpdateLayout();
        Rect caret=note.GetRectFromCharacterIndex(0);
        check("Notes keeps the original editor height and its text inset",Math.Abs(note.Height-136)<.1 && !caret.IsEmpty && caret.Left>=note.Padding.Left && caret.Left<=note.Padding.Left+4);
        var original=note;s.Accent="#E8E8E8";app.ChangeSettings();await Task.Delay(50);
        check("Visual updates preserve the Notes editor and selection",ReferenceEquals(original,Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Quick notes")) && note.SelectionStart==3 && note.SelectionLength==7);
        Capture(island,"notes-focused",true);Capture(island,"notes-focused-150",true,1.5);Capture(island,"notes-focused-200",true,2);
        app.Assistant.Conversation.Record("user","Help me keep this interface simple.");app.Assistant.Conversation.Record("assistant","Keep the familiar controls where they are. Refine the spacing, contrast and feedback so each interaction feels clear.");
        island.OpenAssistant(false);await Task.Delay(80);Capture(island,"naby",true);island.CloseAssistant();
        var window=new SettingsWindow(app){ShowActivated=false};window.Show();await Task.Delay(80);
        try
        {
            var nav=(Dictionary<string,Button>)typeof(SettingsWindow).GetField("navigationButtons",flags)!.GetValue(window)!;
            check("All sixteen Settings categories retain the expected order",nav.Keys.SequenceEqual(new[]{"Home","Appearance","Ambience","Motion","Layout","Visibility","Controls","Content","Audio","Clipboard","Weather","System","Launch","AI","Notifications","Application"}));
            var navigate=typeof(SettingsWindow).GetMethod("Navigate",flags)!;
            foreach(string category in nav.Keys){navigate.Invoke(window,new object[]{category});await Task.Delay(40);Capture(window,"settings-"+category.ToLowerInvariant());}
            navigate.Invoke(window,new object[]{"Appearance"});await Task.Delay(40);
            check("Panel navigation uses dots with no obsolete style selector",app.Settings.CategoryStyle=="Dots" && !Diagnostics.Descendants(window).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="Category appearance"));
            navigate.Invoke(window,new object[]{"Layout"});window.Width=600;window.Height=520;await Task.Delay(60);Capture(window,"settings-compact");
        }
        finally{window.Close();}
        File.WriteAllText(Path.Combine(directory,"control-layout.json"),JsonSerializer.Serialize(geometry,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Synthetic local content and original abstract cover drawing. WPF offscreen captures; scaled raster exports do not substitute for physical DPI tests. No provider request, microphone, screen sharing or personal clipboard read/write was initiated.");
        check("Visual fixtures keep voice and screen sharing off",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
    }
}
