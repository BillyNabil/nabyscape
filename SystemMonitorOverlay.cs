using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly StackPanel monitorRows=new();
    private string monitorTab="overview";
    private ScrollViewer? monitorDetailScroll;
    private readonly Dictionary<string,Button> monitorTabs=new();
    internal string MonitorTab=>monitorTab;
    internal void SelectMonitorTab(string id)
    {
        if(!monitorTabs.ContainsKey(id))return;monitorTab=id;RenderSystemMonitor();monitorDetailScroll?.ScrollToTop();
        foreach(var pair in monitorTabs){pair.Value.Background=pair.Key==id?UI.Brush("#35383F"):Brushes.Transparent;pair.Value.Foreground=pair.Key==id?Brushes.White:UI.Muted;}
    }
    private readonly Queue<double?> monitorCpuHistory=new();
    private MonitorSnapshot? monitorPrevious,monitorLatest;
    internal bool MonitorPolling=>monitorActive;
    internal bool MonitorReady=>monitorLatest!=null && monitorPrevious!=null;
    internal bool MonitorBusy=>monitorBusy;
    internal int MonitorReads {get;private set;}
    internal int MonitorResults {get;private set;}
    internal int MonitorRejected {get;private set;}
    internal Task? MonitorDeliveryGate {get;set;}
    private bool monitorBusy,monitorActive,monitorClosed;
    private int monitorGeneration;
    private long monitorReadAt;

    internal static void DetachMonitorRows(StackPanel rows)
    {
        if(rows.Parent is ScrollViewer owner)owner.Content=null;
        else if(rows.Parent is Panel parent)parent.Children.Remove(rows);
    }

    private FrameworkElement BuildSystemMonitor()
    {
        DetachMonitorRows(monitorRows);
        var panel=new Grid();panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new());
        var tabs=new WrapPanel{Margin=new(0,0,0,10)};monitorTabs.Clear();
        foreach(var (id,label) in new[]{("overview","Overview"),("hardware","Hardware"),("network","Network"),("storage","Storage"),("processes","Processes")})
        {
            if(id=="network" && !app.Settings.MonitorNetwork || id=="storage" && !app.Settings.MonitorStorage || id=="processes" && !app.Settings.MonitorProcesses)continue;
            var button=UI.Button(label,()=>SelectMonitorTab(id));button.FontSize=10;button.Padding=new(7,5,7,5);button.MinHeight=28;button.Margin=new(0,0,3,3);UI.Named(button,"Monitor "+label);monitorTabs[id]=button;tabs.Children.Add(button);
        }
        panel.Children.Add(tabs);monitorDetailScroll=UI.Scroll(monitorRows);monitorDetailScroll.Padding=new(0,0,12,0);
        tabs.Measure(new Size(Math.Max(1,bodyWidth),double.PositiveInfinity));
        double reserved=(WidgetOn("clock")?80:0)+49+tabs.DesiredSize.Height;
        monitorDetailScroll.Height=Math.Clamp((app.Settings.PanelMaxHeight-app.Settings.Height-32)/app.Settings.PanelScale-reserved,92,240);
        Grid.SetRow(monitorDetailScroll,1);panel.Children.Add(monitorDetailScroll);SelectMonitorTab(monitorTabs.ContainsKey(monitorTab)?monitorTab:"overview");return panel;
    }

    private bool SystemMonitorVisible=>!monitorClosed && expanded && app.Settings.CompactLayout && !commandOpen && activePage=="status" && WidgetOn("system") && IsVisible && monitorRows.IsVisible && monitorRows.IsDescendantOf(body);

    private void InvalidateSystemMonitor()
    {
        monitorActive=false;monitorGeneration++;monitorPrevious=null;monitorLatest=null;monitorCpuHistory.Clear();monitorReadAt=0;HardwareMonitor.ResetSampling();
    }

    private async void UpdateSystemMonitor()
    {
        if(!SystemMonitorVisible)
        {
            if(monitorActive)InvalidateSystemMonitor();
            return;
        }
        if(!monitorActive)HardwareMonitor.ResetSampling();
        monitorActive=true;
        if(monitorBusy || (monitorReadAt!=0 && Stopwatch.GetElapsedTime(monitorReadAt).TotalSeconds<2))return;
        monitorBusy=true;MonitorReads++;monitorReadAt=Stopwatch.GetTimestamp();int generation=monitorGeneration;
        bool network=app.Settings.MonitorNetwork,storage=app.Settings.MonitorStorage,processes=app.Settings.MonitorProcesses;
        try
        {
            var snapshot=await Task.Run(()=>SystemMonitor.Capture(network,storage,processes));
            // Diagnostics can hold delivery to exercise navigation during an in-flight read.
            if(DiagnosticRun && MonitorDeliveryGate is Task gate)await Task.WhenAny(gate,Task.Delay(3000));
            // A quick return to Status must not revive a read from the detached body.
            if(generation!=monitorGeneration || !SystemMonitorVisible){MonitorRejected++;return;}
            MonitorResults++;
            monitorCpuHistory.Enqueue(SystemMonitor.Cpu(snapshot,monitorLatest));while(monitorCpuHistory.Count>30)monitorCpuHistory.Dequeue();
            monitorPrevious=monitorLatest;monitorLatest=snapshot;RenderSystemMonitor();Dimensions();if(Math.Abs(PaintedHeight-expandedHeight)>.5)motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));
        }
        catch
        {
            if(generation==monitorGeneration && SystemMonitorVisible){monitorRows.Children.Clear();monitorRows.Children.Add(UI.Text("System information unavailable. Retrying…",11,UI.Muted));}
        }
        finally{monitorBusy=false;}
    }

    private void StopSystemMonitor(){monitorClosed=true;InvalidateSystemMonitor();}

    private void RenderSystemMonitor()
    {
        monitorRows.Children.Clear();var s=monitorLatest;
        if(s==null){monitorRows.Children.Add(UI.Text("Sampling system information…",11,UI.Muted));return;}
        if(monitorTab=="overview")
        {
        double? cpu=SystemMonitor.Cpu(s,monitorPrevious);
        MonitorRow("CPU",cpu.HasValue?$"{cpu:0.#}%":"Sampling…",true);
        var chart=new Canvas{Height=42,ClipToBounds=true,Margin=new(0,4,0,2)};
        void DrawHistory()
        {
            chart.Children.Clear();double width=chart.ActualWidth;if(width<=0)return;
            chart.Children.Add(new Line{X1=0,X2=width,Y1=41,Y2=41,Stroke=UI.Brush("#28FFFFFF"),StrokeThickness=1});
            var values=monitorCpuHistory.ToArray();Polyline? line=null;
            for(int n=0;n<values.Length;n++)
            {
                if(values[n] is not double value){line=null;continue;}
                if(line==null){line=new Polyline{Stroke=UI.Brush(app.Settings.Accent),StrokeThickness=1.6};chart.Children.Add(line);}
                line.Points.Add(new Point(width*(30-values.Length+n)/29,40-value*.38));
            }
        }
        chart.SizeChanged+=(_,_)=>DrawHistory();monitorRows.Children.Add(chart);DrawHistory();
        monitorRows.Children.Add(UI.Text("CPU · 60 second window · 2 second samples",10,UI.Muted));
        if(s.Total is ulong total && s.Available is ulong available)
        {
            MonitorRow("Memory",SystemMonitor.Bytes(total-available)+" / "+SystemMonitor.Bytes(total),true);
            var track=new Grid{Height=3,Margin=new(0,4,0,8),Background=UI.Brush("#28FFFFFF")};
            track.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(1,total-available),GridUnitType.Star)});
            track.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(1,available),GridUnitType.Star)});
            track.Children.Add(new Border{Background=UI.Brush(app.Settings.Accent)});monitorRows.Children.Add(track);
        }
        else MonitorRow("Physical memory","Unavailable");
        MonitorRow("System uptime",SystemMonitor.Uptime(s.Uptime));
        }
        if(monitorTab=="network" && app.Settings.MonitorNetwork)
        {
            MonitorSection("NETWORK · RECEIVE / SEND");
            double seconds=monitorPrevious==null?0:(s.Timestamp-monitorPrevious.Timestamp)/(double)Stopwatch.Frequency;
            foreach(var net in s.Networks)
            {
                var before=monitorPrevious?.Networks.FirstOrDefault(n=>n.Id==net.Id);
                MonitorRow(net.Name,SystemMonitor.RateText(SystemMonitor.Rate(net.Received,before?.Received,seconds))+" ↓");
                MonitorRow("",SystemMonitor.RateText(SystemMonitor.Rate(net.Sent,before?.Sent,seconds))+" ↑");
            }
            if(s.Networks.Count==0)MonitorRow("Active interfaces","None available");
        }
        if(monitorTab=="storage" && app.Settings.MonitorStorage)
        {
            MonitorSection("FIXED VOLUMES · FREE / TOTAL");
            foreach(var drive in s.Drives)MonitorRow(drive.Name,SystemMonitor.Bytes(drive.Free)+" / "+SystemMonitor.Bytes(drive.Total));
            if(s.Drives.Count==0)MonitorRow("Volumes","None available");
        }
        if(monitorTab=="processes" && app.Settings.MonitorProcesses)
        {
            MonitorRow("Processes / threads",SystemMonitor.ProcessCounts(s.ProcessCount,s.ThreadCount,s.PartialThreads));
            MonitorSection("TOP PROCESSES · WORKING SET");
            foreach(var process in s.Processes)MonitorRow(process.Name+" · "+process.Id,SystemMonitor.Bytes(process.WorkingSet));
            if(s.Processes.Count==0)MonitorRow("Processes","Unavailable");
            var note=UI.Text("Working sets can share memory; they do not sum to total RAM usage.",10,UI.Muted);note.Margin=new(0,6,0,0);monitorRows.Children.Add(note);
        }
        if(monitorTab=="hardware")RenderHardware(s.Hardware);
        ApplyHeadingTypography(monitorRows);
        if(s.Errors.Length>0){var errors=UI.Text(string.Join(" · ",s.Errors),10,UI.Muted);errors.Margin=new(0,8,0,0);monitorRows.Children.Add(errors);}
    }

    private void RenderHardware(MonitorHardware? hardware)
    {
        if(hardware==null){MonitorRow("Hardware","Unavailable");return;}
        MonitorSection("PROCESSOR");
        foreach(var cpu in hardware.Cpus)
        {
            MonitorRow("CPU model",cpu.Name.Length>0?cpu.Name:"Unavailable");
            MonitorRow("Cores / logical processors",(cpu.Cores?.ToString()??"Unavailable")+" / "+(cpu.Threads?.ToString()??"Unavailable"));
            MonitorRow("Maximum reported clock",cpu.MaxClock is double mhz && mhz>0?$"{mhz:0} MHz":"Unavailable");
        }
        if(hardware.Cpus.Count==0)MonitorRow("CPU details","Unavailable");
        MonitorSection("GRAPHICS");
        foreach(string model in hardware.GpuModels)MonitorRow("GPU model",model);
        if(hardware.GpuModels.Count==0)MonitorRow("GPU model","Unavailable");
        for(int i=0;i<hardware.Gpus.Count;i++)
        {
            var gpu=hardware.Gpus[i];
            // WMI controller names have no reliable LUID mapping: keep counter identities explicit.
            MonitorRow("Counter adapter "+(i+1),gpu.Id);
            MonitorRow("GPU utilization",HardwareMonitor.Percent(gpu.Utilization),true);
            MonitorRow("Dedicated memory used",gpu.Dedicated is ulong dedicated?SystemMonitor.Bytes(dedicated):"Unavailable");
            MonitorRow("Shared memory used",gpu.Shared is ulong shared?SystemMonitor.Bytes(shared):"Unavailable");
        }
        if(hardware.Gpus.Count==0)
        {
            MonitorRow("GPU utilization","Unavailable");MonitorRow("Dedicated / shared memory","Unavailable");
        }
        var gpuNote=UI.Text("Utilization is the busiest GPU engine. Adapter counter IDs are independent of the model list.",10,UI.Muted);gpuNote.Margin=new(0,6,0,0);monitorRows.Children.Add(gpuNote);
        MonitorSection("TEMPERATURES & FANS");
        foreach(var sensor in hardware.Sensors)MonitorRow(sensor.Name,HardwareMonitor.SensorValue(sensor));
        if(!hardware.Sensors.Any(s=>s.Type=="Temperature"))MonitorRow("Temperature","Unavailable");
        if(!hardware.Sensors.Any(s=>s.Type=="Fan"))MonitorRow("Fan speed","Unavailable");
        var source=hardware.Sensors.Count>0?"Sensor source: "+string.Join(" / ",hardware.Sensors.Select(s=>s.Provider).Distinct()):"Requires an already running LibreHardwareMonitor or OpenHardwareMonitor WMI provider exposing these sensors.";
        var note=UI.Text(source,10,UI.Muted);note.Margin=new(0,6,0,0);monitorRows.Children.Add(note);
        if(hardware.Status!=null){var status=UI.Text(hardware.Status,10,UI.Muted);status.Margin=new(0,6,0,0);monitorRows.Children.Add(status);}
    }

    private void MonitorSection(string title){var label=UI.Label(title);label.Margin=new(0,15,0,7);monitorRows.Children.Add(label);}
    private void MonitorRow(string label,string value,bool prominent=false)
    {
        var row=new Grid{Margin=new(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var name=UI.Text(label,11,UI.Muted);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=label;name.Margin=new(0,0,8,0);row.Children.Add(name);
        var text=UI.Text(value,prominent?15:11);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.MaxWidth=Math.Max(90,bodyWidth*.61);text.ToolTip=value;Grid.SetColumn(text,1);row.Children.Add(text);monitorRows.Children.Add(row);
    }
}
