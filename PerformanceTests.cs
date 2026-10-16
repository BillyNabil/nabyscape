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
