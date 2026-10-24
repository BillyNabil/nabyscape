using System;
using System.Collections.Generic;
using System.Diagnostics;
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

internal static class PerformanceDiagnostics
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow island,string directory)
    {
        string[] args=Environment.GetCommandLineArgs();
        int Option(string name,int fallback){int at=Array.IndexOf(args,name);return at>=0 && at+1<args.Length && int.TryParse(args[at+1],out int value)?Math.Max(1,value):fallback;}
        int duration=Option("--benchmark-seconds",60),runs=Option("--benchmark-runs",3);
        AssistantIdleMotion.SampleForDiagnostics=true;
        var results=new List<object>();var frames=new List<double>(20000);
        SettingsWindow? settings=null;
        var s=app.Settings;s.SmartActivities=false;s.HideFullscreen=s.HideMaximized=false;s.ReduceMotion=false;s.LiveActivity=true;s.CompactLayout=true;s.PanelWidth=514;s.PanelMaxHeight=504;s.AnimationMs=420;s.ClosedMediaIcon="Artwork";s.ClosedMediaContent="Waveform";
        foreach(var widget in s.Widgets)widget.Enabled=true;app.ChangeSettings();
        app.Audio.DiagnosticPeak=()=>{double t=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;return (float)(.23+.14*Math.Sin(t*9)+.06*Math.Sin(t*17));};
        island.DiagnosticPointerPresent=true;
        string[] scenarios={"hidden","closed-paused","closed-playing","hover-music","panel-media","settings-static","settings-moving","settings-slider","system-overview","system-hardware","system-network","system-storage","system-processes","mini-windows"};
        int filter=Array.IndexOf(args,"--benchmark-scenarios");if(filter>=0 && filter+1<args.Length)scenarios=scenarios.Where(v=>args[filter+1].Split(',').Contains(v)).ToArray();
        try
        {
            for(int run=1;run<=runs;run++)foreach(string scenario in scenarios)
            {
                AssistantOrb.ReferenceTransformsForDiagnostics=args.Contains("--benchmark-alternate-character") && run%2==1;
                foreach(var orb in Diagnostics.Descendants(island).OfType<AssistantOrb>())orb.InvalidateVisual();
                settings?.Close();settings=null;
                foreach(var window in Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray())window.Close();
                if(island.UserHidden)island.ToggleVisibility();island.Collapse();app.Media.DiagnosticPlayback(!scenario.EndsWith("paused") && scenario!="hidden" && scenario!="settings-static");
                if(scenario=="hidden")island.ToggleVisibility();
                else if(scenario=="hover-music"){island.SelectPeek("media");island.PreviewActivity();}
                else if(scenario=="panel-media"){island.Reveal(false);island.SelectPage("media",false);}
                else if(scenario.StartsWith("settings-"))
                {
                    settings=new SettingsWindow(app);settings.Show();
                    typeof(SettingsWindow).GetMethod("Navigate",Private)!.Invoke(settings,new object[]{scenario=="settings-static"?"Application":scenario=="settings-slider"?"Layout":"Audio"});
                    if(scenario=="settings-moving"){typeof(SettingsWindow).GetField("previewMode",Private)!.SetValue(settings,"Panel");typeof(SettingsWindow).GetMethod("RefreshPreview",Private)!.Invoke(settings,null);}
                }
                else if(scenario.StartsWith("system-")){island.Reveal(false);island.SelectPage("status",false);island.SelectMonitorTab(scenario[7..]);}
                else if(scenario=="mini-windows")foreach(string kind in new[]{"notes","media","system"})app.Previews.OpenAt(kind,new Point(140+Array.IndexOf(new[]{"notes","media","system"},kind)*370,180),false);
                await Task.Delay(3000);island.UpdateLayout();settings?.UpdateLayout();
                if(run==1)Capture(settings??(FrameworkElement)island,Path.Combine(directory,scenario+".png"));
                var screen=Native.ChooseScreen(s);uint refreshStart=Native.RefreshRate(screen);
                frames.Clear();FrameTelemetry.Observe=ms=>frames.Add(ms);
                using var process=Process.GetCurrentProcess();process.Refresh();double cpu=process.TotalProcessorTime.TotalMilliseconds;long allocated=GC.GetTotalAllocatedBytes(true),privateStart=process.PrivateMemorySize64;int handles=process.HandleCount;
                int[] collections={GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};long meter=app.Audio.DiagnosticMeterReads,characterTicks=AssistantIdleMotion.DiagnosticEvaluations;var watch=Stopwatch.StartNew();
                File.AppendAllText(Path.Combine(directory,"progress.txt"),$"START {run}/{runs} {scenario}\n");
                while(watch.Elapsed.TotalSeconds<duration)
                {
                    if(scenario=="settings-slider")
                    {
                        var slider=Diagnostics.Descendants(settings!).OfType<Slider>().Single(v=>AutomationProperties.GetName(v)=="Closed width");slider.Value=295+Math.Sin(watch.Elapsed.TotalSeconds)*20;
                        await Task.Delay(100);
                    }
                    else await Task.Delay(1000);
                }
                watch.Stop();FrameTelemetry.Observe=null;process.Refresh();
                var intervals=frames.OrderBy(v=>v).ToArray();double? Percentile(double fraction)=>intervals.Length==0?null:intervals[Math.Min(intervals.Length-1,(int)(intervals.Length*fraction))];
                results.Add(new{scenario,run,characterReference=AssistantOrb.ReferenceTransformsForDiagnostics,characterClockEvaluations=AssistantIdleMotion.DiagnosticEvaluations-characterTicks,frameLimit=Motion.FrameLimit,systemCpu=app.Usage.cpu,monitor=screen.DeviceName,refreshHzStart=refreshStart,refreshHzEnd=Native.RefreshRate(screen),dpiScale=Native.Scale(screen),seconds=watch.Elapsed.TotalSeconds,cpuPercent=(process.TotalProcessorTime.TotalMilliseconds-cpu)/watch.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,privateMiB=process.PrivateMemorySize64/1048576d,privateGrowthMiB=(process.PrivateMemorySize64-privateStart)/1048576d,workingSetMiB=process.WorkingSet64/1048576d,allocatedBytesPerSecond=(GC.GetTotalAllocatedBytes(true)-allocated)/watch.Elapsed.TotalSeconds,gen0=GC.CollectionCount(0)-collections[0],gen1=GC.CollectionCount(1)-collections[1],gen2=GC.CollectionCount(2)-collections[2],handles=process.HandleCount,handleGrowth=process.HandleCount-handles,meterReads=app.Audio.DiagnosticMeterReads-meter,renderCallbacks=frames.Count,p95Ms=Percentile(.95),p99Ms=Percentile(.99),gpuPercent=(double?)null});
                File.WriteAllText(Path.Combine(directory,"performance.json"),JsonSerializer.Serialize(new{version=typeof(App).Assembly.GetName().Version!.ToString(3),cores=Environment.ProcessorCount,renderTier=RenderCapability.Tier>>16,fixture="Deterministic amplitude and metadata; no native audio writes. GPU unavailable. WPF callbacks are not GPU presentation.",results},new JsonSerializerOptions{WriteIndented=true}));
                File.AppendAllText(Path.Combine(directory,"progress.txt"),$"DONE {run}/{runs} {scenario}\n");
            }
            if(!args.Contains("--benchmark-no-cycles"))
            {
                settings?.Close();settings=null;island.Collapse();app.Media.DiagnosticPlayback(false);
                foreach(var window in Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray())window.Close();
                var closedWindows=new List<WeakReference>();
                int Subscribers(string name)=>(typeof(App).GetField(name,Private)?.GetValue(app) as Delegate)?.GetInvocationList().Length??0;
                async Task Cycle(){island.Reveal(false);island.Collapse();var window=new SettingsWindow(app);closedWindows.Add(new(window));window.Show();await Task.Delay(30);window.Close();app.Previews.OpenAt("notes",new Point(180,180),false);foreach(var mini in Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray()){closedWindows.Add(new(mini));mini.Close();}await Task.Delay(30);}
                for(int warm=0;warm<5;warm++)await Cycle();
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(2000);
                using var process=Process.GetCurrentProcess();process.Refresh();int handles=process.HandleCount,updated=Subscribers("Updated"),changed=Subscribers("SettingsChanged");long heap=GC.GetTotalMemory(false);
                for(int i=0;i<100;i++)await Cycle();
                await Task.Delay(3000);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(2000);process.Refresh();
                File.WriteAllText(Path.Combine(directory,"retention.json"),JsonSerializer.Serialize(new{cycles=100,handleGrowth=process.HandleCount-handles,managedGrowthBytes=GC.GetTotalMemory(false)-heap,remainingSettings=Application.Current.Windows.OfType<SettingsWindow>().Count(),remainingMiniWindows=Application.Current.Windows.OfType<DetachedPreviewWindow>().Count(),retainedClosedWindows=closedWindows.Count(w=>w.IsAlive),updateSubscriberGrowth=Subscribers("Updated")-updated,settingsSubscriberGrowth=Subscribers("SettingsChanged")-changed,note="Forced GC only in this isolated retention diagnostic; not in runtime or timing measurements. Weak references include the five warm-up cycles."},new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        finally{FrameTelemetry.Observe=null;AssistantIdleMotion.SampleForDiagnostics=false;AssistantOrb.ReferenceTransformsForDiagnostics=false;settings?.Close();app.Audio.DiagnosticPeak=null;}
    }
    private static void Capture(FrameworkElement element,string path)
    {
        element.UpdateLayout();if(element.ActualWidth<1 || element.ActualHeight<1)return;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
}
