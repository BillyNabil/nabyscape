using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed record HardwareCpu(string Name,int? Cores,int? Threads,double? MaxClock);
internal sealed record HardwareSensor(string Name,string Type,double Value,string Provider);
internal sealed record GpuEngine(string Name,ulong Counter,ulong Timestamp);
internal sealed record HardwareGpu(string Id,double? Utilization,ulong? Dedicated,ulong? Shared);
internal sealed record MonitorHardware(IReadOnlyList<HardwareCpu> Cpus,IReadOnlyList<string> GpuModels,
    IReadOnlyList<HardwareGpu> Gpus,IReadOnlyList<HardwareSensor> Sensors,string? Status);

internal sealed class HardwareCaptureWorker(Func<int,Task<MonitorHardware>> read,Func<string,MonitorHardware> unavailable,Func<long> now,
    Func<Task<MonitorHardware>,bool>? wait=null)
{
    private readonly object sync=new();
    private Task<MonitorHardware>? pending;
    private int generation,pendingGeneration;
    private long startedAt;
    // A delayed sample may be delivered once, for five seconds from the read's start.
    // Measuring age from start also rejects a formerly stuck provider's old measurements.
    private const int FreshnessMilliseconds=5000;
    internal void ResetSampling(){lock(sync)generation++;}
    internal MonitorHardware Capture()
    {
        Task<MonitorHardware> work;
        lock(sync)
        {
            if(pending is {IsCompleted:false})return unavailable("Hardware provider busy · retrying");
            if(pending!=null)
            {
                var completed=TakeCompleted();
                if(completed!=null)return completed;
            }
            pendingGeneration=generation;startedAt=now();
            pending=work=read(generation);
        }
        try
        {
            if(!(wait?.Invoke(work)??work.Wait(1200)))return unavailable("Hardware query timed out · retrying");
        }
        catch { /* Faults are observed and cleared by TakeCompleted below. */ }
        lock(sync)
        {
            if(ReferenceEquals(pending,work) && work.IsCompleted)
                return TakeCompleted()??unavailable("Hardware sample expired · retrying");
            return unavailable("Hardware sample reset · retrying");
        }
    }
    // Called under sync. Consuming completion atomically prevents duplicate delivery.
    private MonitorHardware? TakeCompleted()
    {
        var completed=pending!;pending=null;
        if(completed.IsFaulted){_=completed.Exception;return null;}
        if(completed.IsCanceled || pendingGeneration!=generation || now()-startedAt>FreshnessMilliseconds)return null;
        return completed.GetAwaiter().GetResult();
    }
}

// Read-only WMI queries run only when requested by the visible monitor worker.
// A stuck provider can occupy one background operation, never an unbounded queue or the UI thread.
internal static class HardwareMonitor
{
    private static readonly Reader reader=new(Query,()=>Environment.TickCount64);
    private static bool gpuOnly;
    private static readonly HardwareCaptureWorker worker=new(generation=>{bool gpu=gpuOnly;return Task.Run(()=>reader.Read(generation,gpu));},Unavailable,()=>Environment.TickCount64);
    internal static void ResetSampling(){reader.RequestRetry();worker.ResetSampling();}
    internal static MonitorHardware Unavailable(string reason)=>reader.Unavailable(reason);
    internal static MonitorHardware Capture(bool onlyGpu=false){gpuOnly=onlyGpu;return worker.Capture();}

    internal sealed class Reader(Func<string,string,List<Dictionary<string,object?>>> query,Func<long> now)
    {
        private HardwareCpu[]? cpus;
        private string[]? models;
        private readonly MetadataRetry cpuRetry=new(),modelRetry=new();
        private readonly Dictionary<string,MetadataRetry> sensorRetries=new(){["LibreHardwareMonitor"]=new(),["OpenHardwareMonitor"]=new()};
        private volatile bool retryRequested;
        internal void RequestRetry()=>retryRequested=true;
        private GpuEngine[] previous=Array.Empty<GpuEngine>();
        private int previousGeneration=-1;
        internal MonitorHardware Unavailable(string reason)=>new(cpus??Array.Empty<HardwareCpu>(),models??Array.Empty<string>(),Array.Empty<HardwareGpu>(),Array.Empty<HardwareSensor>(),reason);
        internal MonitorHardware Read(int generation,bool onlyGpu=false)
        {
            if(retryRequested){retryRequested=false;cpuRetry.Reset();modelRetry.Reset();foreach(var retry in sensorRetries.Values)retry.Reset();}
            if(!onlyGpu && cpus==null && cpuRetry.Due(now()))
            {
                var found=query("root\\cimv2","SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor")
                    .Select(r=>new HardwareCpu(Text(r,"Name"),Integer(r,"NumberOfCores"),Integer(r,"NumberOfLogicalProcessors"),Number(r,"MaxClockSpeed"))).Where(c=>c.Name.Length>0).ToArray();
                if(found.Length>0)cpus=found;else cpuRetry.Failed(now());
            }
            if(!onlyGpu && models==null && modelRetry.Due(now()))
            {
                var found=query("root\\cimv2","SELECT Name FROM Win32_VideoController").Select(r=>Text(r,"Name")).Where(n=>n.Length>0).ToArray();
                if(found.Length>0)models=found;else modelRetry.Failed(now());
            }
            var engines=query("root\\cimv2","SELECT Name,UtilizationPercentage,Timestamp_Sys100NS FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine")
                .Where(r=>Unsigned(r,"UtilizationPercentage")!=null && Unsigned(r,"Timestamp_Sys100NS")!=null)
                .Select(r=>new GpuEngine(Text(r,"Name"),Unsigned(r,"UtilizationPercentage")!.Value,Unsigned(r,"Timestamp_Sys100NS")!.Value)).ToArray();
            var usage=Utilization(engines,previousGeneration==generation?previous:Array.Empty<GpuEngine>());previous=engines;previousGeneration=generation;
            var memory=query("root\\cimv2","SELECT Name,DedicatedUsage,SharedUsage FROM Win32_PerfRawData_GPUPerformanceCounters_GPUAdapterMemory");
            var ids=engines.Select(e=>AdapterId(e.Name)).Concat(memory.Select(r=>AdapterId(Text(r,"Name")))).Where(id=>id!=null).Distinct().ToArray();
            var gpus=ids.Select(id=>
            {
                var row=memory.FirstOrDefault(r=>AdapterId(Text(r,"Name"))==id);
                return new HardwareGpu(id!,usage.TryGetValue(id!,out double value)?value:null,row==null?null:Unsigned(row,"DedicatedUsage"),row==null?null:Unsigned(row,"SharedUsage"));
            }).ToArray();
            var sensors=new List<HardwareSensor>();
            if(!onlyGpu)foreach(var entry in sensorRetries)
            {
                if(!entry.Value.Due(now()))continue;
                var rows=query("root\\"+entry.Key,"SELECT Name,SensorType,Value FROM Sensor");
                if(rows.Count==0){entry.Value.Failed(now());continue;}
                foreach(var row in rows)
                {
                    string type=Text(row,"SensorType");double? value=Number(row,"Value");
                    if(type is "Temperature" or "Fan" && value is double v && double.IsFinite(v))sensors.Add(new(Text(row,"Name"),type,v,entry.Key));
                }
            }
            return new(cpus??Array.Empty<HardwareCpu>(),models??Array.Empty<string>(),gpus,sensors,gpus.Length==0?"GPU performance counters unavailable":null);
        }
    }

    // Empty discoveries (including caught WMI failures) are retried, never cached as success.
    private sealed class MetadataRetry
    {
        private long next;
        private int delay=5000;
        internal bool Due(long now)=>now>=next;
        internal void Reset(){next=0;delay=5000;}
        internal void Failed(long now){next=now+delay;delay=Math.Min(delay*2,30000);}
    }

    // Windows engine instances include the process ID, adapter LUID and physical engine.
    // Sum processes sharing an engine, then take the busiest engine (Task Manager semantics).
    internal static string? AdapterId(string name)
    {
        var match=Regex.Match(name,@"luid_0x[0-9a-f]+_0x[0-9a-f]+_phys_\d+",RegexOptions.IgnoreCase);
        return match.Success?match.Value.ToLowerInvariant():null;
    }
    internal static Dictionary<string,double> Utilization(IEnumerable<GpuEngine> current,IEnumerable<GpuEngine> before)
    {
        var old=before.GroupBy(e=>e.Name).ToDictionary(g=>g.Key,g=>g.First());
        var totals=new Dictionary<(string Adapter,string Engine),double>();
        foreach(var e in current)
        {
            string? id=AdapterId(e.Name);var engine=Regex.Match(e.Name,@"_eng_\d+",RegexOptions.IgnoreCase);
            if(id==null || !engine.Success || !old.TryGetValue(e.Name,out var prior) || e.Timestamp<=prior.Timestamp || e.Counter<prior.Counter)continue;
            double percentage=100d*(e.Counter-prior.Counter)/(e.Timestamp-prior.Timestamp);
            var key=(id,engine.Value.ToLowerInvariant());totals[key]=totals.GetValueOrDefault(key)+percentage;
        }
        return totals.GroupBy(p=>p.Key.Adapter).ToDictionary(g=>g.Key,g=>Math.Clamp(g.Max(p=>p.Value),0,100));
    }
    internal static string Percent(double? value)=>value is double v && double.IsFinite(v) && v>=0 && v<=100?v.ToString("0.#",CultureInfo.InvariantCulture)+"%":"Unavailable / sampling";
    internal static string SensorValue(HardwareSensor sensor)=>sensor.Value.ToString("0.#",CultureInfo.InvariantCulture)+(sensor.Type=="Fan"?" RPM":" °C");

    private static List<Dictionary<string,object?>> Query(string scope,string query)
    {
        var rows=new List<Dictionary<string,object?>>();
        try
        {
            using var search=new ManagementObjectSearcher(new ManagementScope(scope),new ObjectQuery(query),new EnumerationOptions{Timeout=TimeSpan.FromMilliseconds(350),ReturnImmediately=false});
            using var results=search.Get();
            foreach(ManagementBaseObject item in results)
            {
                using(item)
                {
                    var row=new Dictionary<string,object?>();foreach(PropertyData p in item.Properties)row[p.Name]=p.Value;rows.Add(row);
                }
                // Never present a truncated process-engine total as a complete measurement.
                if(rows.Count>=4096)return new();
            }
        }
        catch { return new(); /* Unsupported classes and absent optional sensor providers are normal. */ }
        return rows;
    }
    private static string Text(Dictionary<string,object?> row,string key)=>Convert.ToString(row.GetValueOrDefault(key),CultureInfo.InvariantCulture)?.Trim()??"";
    private static ulong? Unsigned(Dictionary<string,object?> row,string key)=>ulong.TryParse(Text(row,key),out var n)?n:null;
    private static int? Integer(Dictionary<string,object?> row,string key)=>int.TryParse(Text(row,key),out var n)&&n>0?n:null;
    private static double? Number(Dictionary<string,object?> row,string key)=>double.TryParse(Text(row,key),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:null;
}
