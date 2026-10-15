using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NabyScape;

internal sealed record MonitorNetwork(string Id,string Name,long Received,long Sent);
internal sealed record MonitorDrive(string Name,long Free,long Total);
internal sealed record MonitorProcess(string Name,int Id,long WorkingSet);
internal sealed record MonitorSnapshot(long Timestamp,long? Idle,long? CpuTotal,ulong? Available,ulong? Total,
    TimeSpan Uptime,int? ProcessCount,int? ThreadCount,bool PartialThreads,
    IReadOnlyList<MonitorNetwork> Networks,IReadOnlyList<MonitorDrive> Drives,IReadOnlyList<MonitorProcess> Processes,
    string[] Errors,MonitorHardware? Hardware=null);

internal static class SystemMonitor
{
    [StructLayout(LayoutKind.Sequential)] private struct Memory
    {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref Memory memory);

    // Called only by the overlay's single background worker; all native handles are scoped here.
    internal static MonitorSnapshot Capture(bool network,bool storage,bool processes)
    {
        var errors=new List<string>();long? idle=null,totalCpu=null;
        if(GetSystemTimes(out long i,out long k,out long u)){idle=i;totalCpu=k+u;}else errors.Add("CPU unavailable");
        var memory=new Memory{Length=(uint)Marshal.SizeOf<Memory>()};
        bool hasMemory=GlobalMemoryStatusEx(ref memory);if(!hasMemory)errors.Add("Memory unavailable");
        var (count,threads,partial,top)=CaptureProcesses(processes,Process.GetProcesses);
        if(count==null)errors.Add("Process enumeration unavailable");
        var networks=new List<MonitorNetwork>();
        if(network)try
        {
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())try
            {
                if(adapter.OperationalStatus!=OperationalStatus.Up || adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
                var stats=adapter.GetIPStatistics();networks.Add(new(adapter.Id,adapter.Name,stats.BytesReceived,stats.BytesSent));
            }catch{errors.Add("An interface is unavailable");}
        }catch{errors.Add("Network unavailable");}
        var drives=new List<MonitorDrive>();
        if(storage)try
        {
            foreach(var drive in DriveInfo.GetDrives())try
            {
                if(drive.DriveType!=DriveType.Fixed || !drive.IsReady)continue;
                drives.Add(new(drive.Name,drive.TotalFreeSpace,drive.TotalSize));
            }catch{errors.Add("A volume is unavailable");}
        }catch{errors.Add("Storage unavailable");}
        return new(Stopwatch.GetTimestamp(),idle,totalCpu,hasMemory?memory.AvailablePhysical:null,hasMemory?memory.TotalPhysical:null,
            TimeSpan.FromMilliseconds(Environment.TickCount64),count,threads,partial,networks,drives,
            top,errors.Distinct().ToArray(),HardwareMonitor.Capture());
    }

    internal static (int? Count,int? Threads,bool Partial,MonitorProcess[] Top) CaptureProcesses(bool includeTop,Func<Process[]> enumerate)
    {
        int count=0,threads=0;bool partial=false;var top=new List<MonitorProcess>();
        try
        {
            foreach(var process in enumerate())
            {
                using(process)
                {
                    count++;
                    try{threads+=process.Threads.Count;}catch{partial=true;}
                    if(includeTop)try{top.Add(new(process.ProcessName,process.Id,process.WorkingSet64));}catch{}
                }
            }
        }
        catch{return (null,null,false,Array.Empty<MonitorProcess>());}
        return (count,threads,partial,top.OrderByDescending(p=>p.WorkingSet).Take(5).ToArray());
    }

    internal static string ProcessCounts(int? count,int? threads,bool partial)=>
        count is null || threads is null?"Unavailable":$"{count} / {(partial?"≥ ":"")}{threads}";

    internal static double? Rate(long current,long? previous,double seconds)=>previous.HasValue && current>=previous.Value && seconds>0 && double.IsFinite(seconds)
        ?(current-previous.Value)/seconds:null;
    internal static double? Cpu(MonitorSnapshot current,MonitorSnapshot? previous)
    {
        if(previous?.CpuTotal is not long before || previous.Idle is not long beforeIdle || current.CpuTotal is not long after || current.Idle is not long afterIdle)return null;
        long elapsed=after-before,idle=afterIdle-beforeIdle;
        return elapsed>0 && idle>=0 && idle<=elapsed?100d*(elapsed-idle)/elapsed:null;
    }
    internal static string Bytes(double bytes)
    {
        if(!double.IsFinite(bytes) || bytes<0)return "Unavailable";
        string[] units={"B","KiB","MiB","GiB","TiB","PiB"};int unit=0;
        while(bytes>=1024 && unit<units.Length-1){bytes/=1024;unit++;}
        return bytes.ToString(unit==0?"0":"0.#",CultureInfo.InvariantCulture)+" "+units[unit];
    }
    internal static string RateText(double? rate)=>rate.HasValue?Bytes(rate.Value)+"/s":"Sampling…";
    internal static string Uptime(TimeSpan value)=>$"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
}
