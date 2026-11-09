using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class RussianUiDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        app.Settings.UiLanguage="ru";app.Settings.SettingsShowAll=true;app.Settings.ReduceMotion=true;
        Localization.Configure(app.Settings);Localization.CollectMissing=true;app.ChangeSettings();island.Hide();
        var window=new SettingsWindow(app){Width=1200,Height=850,ShowActivated=false};
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var navigate=typeof(SettingsWindow).GetMethod("Navigate",flags)!;
        var categories=(System.Collections.IEnumerable)typeof(SettingsWindow).GetField("SettingsCategories",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            window.Show();await Task.Delay(150);
            foreach(var category in categories)
            {
                string id=(string)category.GetType().GetProperty("Id")!.GetValue(category)!;
                navigate.Invoke(window,new object[]{id});await Task.Delay(75);window.UpdateLayout();
                Save(window,Path.Combine(directory,"russian-settings-"+id+".png"));
                var page=(FrameworkElement)typeof(SettingsWindow).GetField("page",flags)!.GetValue(window)!;
                Save(page,Path.Combine(directory,"russian-form-"+id+".png"),true);
                check("Russian settings renders "+id,page.ActualWidth>250 && page.ActualHeight>0);
                if(id=="AI")
                {
                    var editor=typeof(SettingsWindow).GetField("aiSettings",flags)!.GetValue(window)!;
                    foreach(string method in new[]{"OpenChatSetup","OpenVoiceSetup","OpenVoiceChecks"})
                    {
                        editor.GetType().GetMethod(method,flags)!.Invoke(editor,null);await Task.Delay(70);window.UpdateLayout();
                        Save(window,Path.Combine(directory,"russian-ai-"+method+".png"));
                        Save(page,Path.Combine(directory,"russian-ai-form-"+method+".png"),true);
                    }
                }
            }
        }
        finally{window.Close();}
        foreach(var sample in new (string Name,Window Window)[]{("installer",Installer.CreateWindow(Array.Empty<string>())),("welcome",new WelcomeWindow(app))})
        {
            try{sample.Window.Show();await Task.Delay(80);Save(sample.Window,Path.Combine(directory,"russian-"+sample.Name+".png"));}
            finally{sample.Window.Close();}
        }
        foreach(string id in new[]{"timer","focus","notes","tasks","calendar","weather","system","clips","awake","links","voice","power"})
        {
            island.Show();island.Reveal(false);island.ShowTools(id,false);await Task.Delay(100);island.UpdateLayout();
            Save(island,Path.Combine(directory,"russian-widget-"+id+".png"));
            check("Russian widget renders "+id,island.ActualWidth>0);island.Hide();
        }
        Localization.CollectMissing=false;
        File.WriteAllText(Path.Combine(directory,"russian-missing-captions.json"),JsonSerializer.Serialize(Localization.MissingCaptions.Order().ToArray(),new JsonSerializerOptions{WriteIndented=true}));
        check("Russian UI retains configured language",app.Settings.UiLanguage=="ru" && Localization.Language=="ru");
    }
    private static void Save(FrameworkElement element,string path,bool brush=false)
    {
        element.UpdateLayout();int width=(int)Math.Ceiling(element.ActualWidth),height=(int)Math.Ceiling(Math.Min(element.ActualHeight,14000));
        if(width<1 || height<1)return;
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        if(brush)
        {
            var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(UI.Brush("#111214"),null,new Rect(0,0,width,height));context.DrawRectangle(new VisualBrush(element){Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top},null,new Rect(0,0,width,height));}bitmap.Render(drawing);
        }
        else bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
