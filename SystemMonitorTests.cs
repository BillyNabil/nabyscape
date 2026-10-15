using System;
using System.Diagnostics;

namespace NabyScape;

internal static class SystemMonitorTests
{
    internal static void Run(Action<string,bool> check)
    {
        HardwareMonitorTests.Run(check);
        var unavailable=SystemMonitor.CaptureProcesses(false,()=>throw new InvalidOperationException("Enumeration failed"));
        check("Failed process enumeration leaves both counts unavailable",unavailable.Count==null && unavailable.Threads==null && unavailable.Top.Length==0);
        check("Unavailable process counts render explicitly",SystemMonitor.ProcessCounts(unavailable.Count,unavailable.Threads,unavailable.Partial)=="Unavailable");
        var empty=SystemMonitor.CaptureProcesses(false,()=>Array.Empty<Process>());
        check("Successful empty enumeration remains a measured zero",empty.Count==0 && empty.Threads==0 && !empty.Partial && SystemMonitor.ProcessCounts(empty.Count,empty.Threads,empty.Partial)=="0 / 0");
        // Unassociated Process objects cannot expose Threads: no native process inspection is needed.
        var partial=SystemMonitor.CaptureProcesses(false,()=>new[]{new Process()});
        check("Per-process thread failure retains available process count",partial.Count==1 && partial.Threads==0 && partial.Partial);
        check("Partial thread count retains its lower-bound marker",SystemMonitor.ProcessCounts(partial.Count,partial.Threads,partial.Partial)=="1 / ≥ 0");
        check("Network baseline is sampling",SystemMonitor.Rate(100,null,2)==null);
        check("Network delta uses elapsed seconds",SystemMonitor.Rate(1100,100,2)==500);
        check("Unchanged network counter is a real zero",SystemMonitor.Rate(100,100,2)==0);
        check("Counter reset starts a new baseline",SystemMonitor.Rate(10,100,2)==null);
        check("Invalid elapsed intervals do not produce rates",SystemMonitor.Rate(100,10,0)==null && SystemMonitor.Rate(100,10,double.NaN)==null);
        check("Binary byte units are explicit",SystemMonitor.Bytes(0)=="0 B" && SystemMonitor.Bytes(1024)=="1 KiB" && SystemMonitor.Bytes(1073741824)=="1 GiB");
        check("Unprimed rate is never shown as zero",SystemMonitor.RateText(null)=="Sampling…" && SystemMonitor.RateText(0)=="0 B/s");
        check("Uptime retains days",SystemMonitor.Uptime(TimeSpan.FromMinutes(1501))=="1d 1h 1m");
        MonitorSnapshot Sample(long idle,long total)=>new(0,idle,total,1,2,TimeSpan.Zero,0,0,false,Array.Empty<MonitorNetwork>(),Array.Empty<MonitorDrive>(),Array.Empty<MonitorProcess>(),Array.Empty<string>());
        check("CPU requires a baseline",SystemMonitor.Cpu(Sample(10,20),null)==null);
        check("CPU excludes idle time",SystemMonitor.Cpu(Sample(40,200),Sample(10,100))==70);
        check("CPU reset or zero interval is unavailable",SystemMonitor.Cpu(Sample(1,2),Sample(10,100))==null && SystemMonitor.Cpu(Sample(10,100),Sample(10,100))==null);
    }
}
