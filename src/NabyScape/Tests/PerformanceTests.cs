using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class PerformanceTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new InvalidOperationException("Performance invariant failed");}
        test("Audio consumers share one monotonic 30 Hz lease",()=>
        {
            var gate=new MediaSampleGate();long interval=(long)Math.Ceiling(Stopwatch.Frequency/30d);
            Assert(gate.Accept("spotify",0));for(int i=0;i<12;i++)Assert(!gate.Accept("spotify",interval-1));Assert(gate.Sequence==1);Assert(gate.Accept("spotify",interval));Assert(!gate.Accept("spotify",interval));Assert(gate.Sequence==2);
        });
        test("Media source changes bypass the old source lease",()=>
        {var gate=new MediaSampleGate();gate.Accept("spotify",100);Assert(gate.Accept("browser",100) && gate.Sequence==2);gate.Reset();Assert(gate.Accept("browser",100));});
        test("Audio cadence stays at 30 Hz through render timestamp jitter",()=>
        {
            var gate=new MediaSampleGate();int count=0;for(int frame=0;frame<600;frame++){long stamp=(long)(frame*Stopwatch.Frequency/60d)+(frame%2==0?-1000:1000);if(gate.Accept("spotify",stamp))count++;Assert(!gate.Accept("spotify",stamp));}Assert(count is >=299 and <=301);
        });
        test("Theme and behavior changes preserve content and geometry signatures",()=>
        {var settings=new Settings();var before=OverlaySettingsState.Capture(settings);settings.Background="#182028";settings.BoldMediaTitle=false;settings.VolumeStep=8;settings.AnimationMs=240;Assert(before==OverlaySettingsState.Capture(settings));});
        test("Closed dimensions do not require rebuilding panel controls",()=>
        {var settings=new Settings();var before=OverlaySettingsState.Capture(settings);settings.Width=310;settings.Radius=14;var next=OverlaySettingsState.Capture(settings);Assert(before.Content==next.Content && before.Reflow==next.Reflow && before.Geometry!=next.Geometry);});
        test("Panel dimensions reflow controls without changing content identity",()=>
        {var settings=new Settings();var before=OverlaySettingsState.Capture(settings);settings.PanelWidth=520;settings.PanelScale=1.1;var next=OverlaySettingsState.Capture(settings);Assert(before.Content==next.Content && before.Reflow!=next.Reflow);});
        test("Widget and tool changes invalidate only the content signature",()=>
        {var settings=new Settings();var before=OverlaySettingsState.Capture(settings);settings.Tools[0].Enabled=false;var next=OverlaySettingsState.Capture(settings);Assert(before.Content!=next.Content && before.Reflow==next.Reflow && before.Geometry==next.Geometry);});
        test("Surface dimension updates publish one final contour",()=>
        {
            var surface=new IslandSurface();surface.Refresh();int calls=0;surface.ContourChanged+=()=>calls++;
            surface.BeginUpdate();surface.SurfaceWidth=420;surface.SurfaceHeight=180;Assert(calls==0);surface.EndUpdate();Assert(calls==1);var contour=surface.Contour;surface.Refresh();Assert(calls==1 && ReferenceEquals(contour,surface.Contour));
        });
        test("Repeated brushes and icon geometry share frozen resources",()=>
        {
            Assert(ReferenceEquals(UI.Brush("#101114"),UI.Brush("#101114")) && UI.Brush("#101114").IsFrozen);
            var first=(System.Windows.Shapes.Path)((Canvas)((Viewbox)Icons.Create("nav-media")).Child).Children[0];var second=(System.Windows.Shapes.Path)((Canvas)((Viewbox)Icons.Create("nav-media")).Child).Children[0];Assert(ReferenceEquals(first.Data,second.Data) && first.Data.IsFrozen);
        });
        test("Thumbnail memory and entry count remain bounded",()=>
        {
            var cache=new ThumbnailCache(1024);var pixels=new byte[8*8*4];var bitmap=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,pixels,32);bitmap.Freeze();
            for(int i=0;i<100;i++)cache.Add(i.ToString(),bitmap);Assert(cache.Count==4 && cache.Bytes==1024 && cache.Get("0")==null && cache.Get("99")!=null);cache.Clear();Assert(cache.Count==0 && cache.Bytes==0);
        });
        test("Thumbnail LRU keeps recent entries and expires only unused cache references",()=>
        {
            long now=0;var cache=new ThumbnailCache(512,()=>now);var pixels=new byte[256];var bitmap=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,pixels,32);bitmap.Freeze();
            cache.Add("clip",bitmap);cache.Add("shelf",bitmap);now=10;Assert(ReferenceEquals(cache.Get("clip"),bitmap));cache.Add("new",bitmap);
            Assert(cache.Get("shelf")==null && cache.Get("clip")!=null);now=30005;cache.TrimIdle();Assert(cache.Count==2);
            now=30010;cache.TrimIdle();Assert(cache.Count==0 && cache.Bytes==0 && bitmap.PixelWidth==8);
        });
        test("Portrait and landscape previews bound the long edge without modifying originals",()=>
        {
            foreach(var size in new[]{(16,2048),(2048,16),(128,128)})
            {
                var source=BitmapSource.Create(size.Item1,size.Item2,96,96,PixelFormats.Bgra32,null,new byte[size.Item1*size.Item2*4],size.Item1*4);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var stream=new System.IO.MemoryStream();encoder.Save(stream);byte[] original=stream.ToArray();
                stream.Position=0;var preview=DisplayThumbnail.Decode(stream,64);Assert(preview.IsFrozen && preview.PixelWidth<=64 && preview.PixelHeight<=64 && Math.Max(preview.PixelWidth,preview.PixelHeight)==64);
                Assert(System.Linq.Enumerable.SequenceEqual(original,stream.ToArray()));stream.Position=0;var full=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];Assert(full.PixelWidth==size.Item1 && full.PixelHeight==size.Item2);
            }
        });
        test("Single-clock mascot pose matches the original four sine channels",()=>
        {
            var ease=new System.Windows.Media.Animation.SineEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut};
            double Wave(double seconds,double period){double phase=seconds%(period*2)/period;return ease.Ease(phase>1?2-phase:phase);}
            foreach(bool music in new[]{false,true})for(int i=0;i<500;i++)
            {
                double t=i*.137,scale=1+.035*Wave(t,music?.7:2.4),angle=(music?-5:-3)*Wave(t,music?1.1:3.4),y=-3.5*Wave(t,music?.65:2.1);
                var original=new TransformGroup{Children={new ScaleTransform(scale,scale,50,50),new RotateTransform(angle,50,50),new TranslateTransform(0,y)}};
                var pose=AssistantIdleMotion.Pose(t,music);foreach(var point in new[]{new Point(0,0),new Point(50,50),new Point(83,83)})Assert((pose.Transform(point)-original.Transform(point)).Length<1e-9);
            }
        });
        test("CPU overview queries GPU counters without sensor or process metadata",()=>
        {
            var queries=new List<string>();var reader=new HardwareMonitor.Reader((scope,query)=>{queries.Add(scope+"|"+query);return new();},()=>0);reader.Read(0,true);
            Assert(queries.Count==2 && queries.TrueForAll(q=>q.Contains("GPUPerformanceCounters")));
        });
        test("Absent sensor providers back off and explicit reopen retries immediately",()=>
        {
            int reads=0;long now=0;var reader=new HardwareMonitor.Reader((scope,query)=>{if(query.Contains("FROM Sensor"))reads++;return new();},()=>now);
            reader.Read(0);Assert(reads==2);now=1000;reader.Read(0);Assert(reads==2);now=5000;reader.Read(0);Assert(reads==4);reader.RequestRetry();reader.Read(1);Assert(reads==6);
        });
    }
}
