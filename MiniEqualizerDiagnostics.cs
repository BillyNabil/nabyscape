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
internal static class MiniEqualizerDiagnostics
{
    internal static async Task Run(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;string settings=JsonSerializer.Serialize(app.Settings);
        object? Field(string name)=>typeof(OverlayWindow).GetField(name,flags)!.GetValue(overlay);
        void Check(string name,bool pass)=>check("Mini equalizer: "+name,pass);
        void Capture(string name)
        {
            overlay.UpdateLayout();int w=(int)Math.Ceiling(overlay.ActualWidth),h=(int)Math.Ceiling(overlay.ActualHeight);var bmp=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bmp.Render(overlay);
            int cw=Math.Min(w,(int)Math.Ceiling(overlay.PaintedWidth)),ch=Math.Min(h,(int)Math.Ceiling(overlay.PaintedHeight));var cropped=new CroppedBitmap(bmp,new Int32Rect((w-cw)/2,0,cw,ch));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(cropped));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        void MeterCheck(string field,string name)
        {
            var meter=(ActivityVisual)Field(field)!;
            for(int i=0;i<45;i++)meter.Sample(i%7<3?.8:.08,1/60d,0,false,false,true,i/60d,i%2==0);
            overlay.UpdateLayout();Check(name+" sits inside the artist line",meter.Parent is InlineMediaLabel);
            if(meter.Parent is InlineMediaLabel row)
            {var rect=meter.TransformToAncestor(row).TransformBounds(new Rect(meter.RenderSize));Check(name+" stays within metadata and keeps compact dimensions",rect.Left>=0 && rect.Right<=row.ActualWidth+.5 && rect.Top>=0 && rect.Bottom<=row.ActualHeight+.5 && meter.ActualWidth<=22 && meter.ActualHeight<=12);}
        }
        try
        {
            var s=app.Settings;s.CompactLayout=true;s.LiveActivity=true;s.ReduceMotion=true;s.Width=228;s.PanelWidth=384;s.HoverWidth=350;s.HoverHeight=192;s.PanelScale=1;s.HideFullscreen=s.HideMaximized=false;s.SmartActivities=false;
            foreach(var w in s.Widgets)w.Enabled=true;foreach(var v in s.HoverViews)v.Enabled=true;app.ChangeSettings();
            foreach(double width in new[]{350d,300d})
            {
                s.HoverWidth=width;app.ChangeSettings();overlay.Collapse();overlay.PreviewActivity();overlay.SelectPeek("media");await Task.Delay(150);overlay.UpdateLayout();
                MeterCheck("peekWave","Hover "+width);Check("Hover "+width+" retains safe control bounds",overlay.HoverOverflow().Length==0);Capture("inline-equalizer-hover-"+width);
            }
            overlay.Reveal(false);overlay.SelectPage("media",false);await Task.Delay(150);MeterCheck("musicWave","Open media");Capture("inline-equalizer-panel");
            s.PanelWidth=340;s.PanelScale=1.25;app.ChangeSettings();overlay.Reveal(false);overlay.SelectPage("media",false);await Task.Delay(150);((TextBlock)Field("artist")!).Text=new string('W',150);MeterCheck("musicWave","Narrow enlarged long artist");Capture("inline-equalizer-panel-narrow");
            check("INFO  Inline equalizer captures use bounded local level fixtures; no audio or clipboard was changed.",true);
        }
        finally
        {
            overlay.Collapse();var original=JsonSerializer.Deserialize<Settings>(settings)!;foreach(var property in typeof(Settings).GetProperties().Where(p=>p.CanRead && p.CanWrite))property.SetValue(app.Settings,property.GetValue(original));app.ChangeSettings();
        }
    }
}
