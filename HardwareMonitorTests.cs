using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NabyScape;

internal static class HardwareMonitorTests
{
    internal static void Run(Action<string,bool> check)
    {
        const string adapter="luid_0x00000000_0x00000001_phys_0";
        string A(int pid,int engine)=>$"pid_{pid}_{adapter}_eng_{engine}_engtype_3D";
        GpuEngine E(int pid,int engine,ulong value,ulong time)=>new(A(pid,engine),value,time);
        var prior=new[]{E(1,0,100,1000),E(2,0,0,1000),E(1,1,0,1000)};
        var current=new[]{E(1,0,140,1100),E(2,0,30,1100),E(1,1,50,1100)};
        var values=HardwareMonitor.Utilization(current,prior);
        check("GPU sums processes per engine and uses busiest engine",values.Count==1 && values[adapter]==70);
        check("GPU initial read is unavailable, never fabricated zero",HardwareMonitor.Utilization(current,Array.Empty<GpuEngine>()).Count==0);
        check("GPU reset counters invalidate the baseline",HardwareMonitor.Utilization(new[]{E(1,0,1,1100)},prior).Count==0);
        check("GPU elapsed timestamp must increase",HardwareMonitor.Utilization(new[]{E(1,0,140,1000)},prior).Count==0);
        check("GPU unchanged counters are measured zero",HardwareMonitor.Utilization(new[]{E(1,0,100,1100)},prior)[adapter]==0);
        check("GPU aggregate is bounded to one hundred percent",HardwareMonitor.Utilization(new[]{E(1,0,300,1100)},prior)[adapter]==100);
        check("Malformed adapter names cannot become measurements",HardwareMonitor.AdapterId("_Total")==null && HardwareMonitor.AdapterId(A(1,0))==adapter);
        check("GPU formatting preserves unavailable states",HardwareMonitor.Percent(null)=="Unavailable / sampling" && HardwareMonitor.Percent(double.NaN)=="Unavailable / sampling" && HardwareMonitor.Percent(0)=="0%");
        check("Sensor units distinguish temperatures and fans",HardwareMonitor.SensorValue(new("CPU","Temperature",42.5,"test"))=="42.5 °C" && HardwareMonitor.SensorValue(new("CPU fan","Fan",1200,"test"))=="1200 RPM");
        var unavailable=HardwareMonitor.Unavailable("Test unavailable");
        check("Unavailable hardware includes no invented readings",unavailable.Gpus.Count==0 && unavailable.Sensors.Count==0 && unavailable.Status=="Test unavailable");
        const string second="luid_0x00000000_0x00000002_phys_0";
        var separate=HardwareMonitor.Utilization(new[]{E(1,0,140,1100),new GpuEngine("pid_3_"+second+"_eng_0_engtype_Copy",20,1100)},new[]{E(1,0,100,1000),new GpuEngine("pid_3_"+second+"_eng_0_engtype_Copy",0,1000)});
        check("Independent GPU adapters retain independent utilization",separate.Count==2 && separate[adapter]==40 && separate[second]==20);
        CheckDelivery(check);
        CheckMetadataRecovery(check);
    }

    private static void CheckDelivery(Action<string,bool> check)
    {
        long now=0;
        int reads=0,waits=0;
        var delayed=new TaskCompletionSource<MonitorHardware>();
        MonitorHardware Sample(string label)=>new(Array.Empty<HardwareCpu>(),Array.Empty<string>(),Array.Empty<HardwareGpu>(),new[]{new HardwareSensor(label,"Temperature",42,"test")},null);
        var late=Sample("late");var fresh=Sample("fresh");
        MonitorHardware Missing(string reason)=>new(Array.Empty<HardwareCpu>(),Array.Empty<string>(),Array.Empty<HardwareGpu>(),Array.Empty<HardwareSensor>(),reason);
        var worker=new HardwareCaptureWorker(_=>{reads++;return reads==1?delayed.Task:Task.FromResult(fresh);},Missing,()=>now,
            task=>{waits++;now+=1200;return task.IsCompleted;});
        check("Delayed hardware read respects bounded caller wait",worker.Capture().Status=="Hardware query timed out · retrying" && reads==1 && waits==1);
        check("Busy hardware provider does not queue a second read",worker.Capture().Status=="Hardware provider busy · retrying" && reads==1 && waits==1);
        now=1500;delayed.SetResult(late);now=2000;
        check("Successful read completed after wait is delivered on next capture",ReferenceEquals(worker.Capture(),late) && reads==1);
        check("Completed hardware result is delivered only once",ReferenceEquals(worker.Capture(),fresh) && reads==2);

        var resetRead=new TaskCompletionSource<MonitorHardware>();reads=0;now=0;
        worker=new HardwareCaptureWorker(_=>{reads++;return reads==1?resetRead.Task:Task.FromResult(fresh);},Missing,()=>now,task=>task.IsCompleted);
        worker.Capture();worker.ResetSampling();
        check("Reset keeps a single old-generation read in flight",worker.Capture().Status=="Hardware provider busy · retrying" && reads==1);
        now=1500;resetRead.SetResult(late);now=2000;
        check("Next capture rejects delayed completion after generation reset",ReferenceEquals(worker.Capture(),fresh) && reads==2);

        var staleRead=new TaskCompletionSource<MonitorHardware>();reads=0;now=0;
        worker=new HardwareCaptureWorker(_=>{reads++;return reads==1?staleRead.Task:Task.FromResult(fresh);},Missing,()=>now,task=>task.IsCompleted);
        worker.Capture();now=6000;staleRead.SetResult(late);
        check("Hardware completion older than freshness window is discarded",ReferenceEquals(worker.Capture(),fresh) && reads==2);

        now=0;
        worker=new HardwareCaptureWorker(_=>Task.FromResult(late),Missing,()=>now,task=>{worker!.ResetSampling();return true;});
        check("Reset during caller wait also rejects completed hardware sample",worker.Capture().Sensors.Count==0);
    }

    private static void CheckMetadataRecovery(Action<string,bool> check)
    {
        long now=0;int cpuReads=0,modelReads=0;
        List<Dictionary<string,object?>> Query(string scope,string query)
        {
            if(query.Contains("Win32_Processor"))
            {
                cpuReads++;
                if(cpuReads>=3)return new(){new(){["Name"]="Recovered CPU",["NumberOfCores"]=8,["NumberOfLogicalProcessors"]=16}};
            }
            if(query.Contains("Win32_VideoController"))
            {
                modelReads++;
                if(modelReads>=2)return new(){new(){["Name"]="Recovered GPU"}};
            }
            return new(); // Same empty outcome used by the production query for WMI failures.
        }
        var reader=new HardwareMonitor.Reader(Query,()=>now);
        var first=reader.Read(0);
        check("Initial metadata failure remains unavailable",first.Cpus.Count==0 && first.GpuModels.Count==0);
        now=4999;reader.Read(0);
        check("Failed metadata discovery backs off before retry",cpuReads==1 && modelReads==1);
        now=5000;var second=reader.Read(0);
        check("GPU metadata recovers without restarting monitor",second.GpuModels.Count==1 && second.GpuModels[0]=="Recovered GPU" && modelReads==2);
        now=14999;reader.Read(0);
        check("Repeated metadata failures increase bounded retry interval",cpuReads==2 && modelReads==2);
        now=15000;var recovered=reader.Read(1);
        check("CPU metadata recovers with core details after failed initial reads",recovered.Cpus.Count==1 && recovered.Cpus[0].Name=="Recovered CPU" && recovered.Cpus[0].Cores==8 && recovered.Cpus[0].Threads==16);
        now=100000;reader.Read(2);
        check("Successful metadata is cached across generation resets",cpuReads==3 && modelReads==2);

        int failedAttempts=0;now=0;
        var absent=new HardwareMonitor.Reader((scope,query)=>{if(query.Contains("Win32_Processor"))failedAttempts++;return new();},()=>now);
        long[] retryTimes={0,5000,15000,35000,75000,135000,195000};
        bool bounded=true;
        for(int i=0;i<retryTimes.Length;i++)
        {
            if(i>0){now=retryTimes[i]-1;absent.Read(0);bounded&=failedAttempts==i;}
            now=retryTimes[i];absent.Read(0);bounded&=failedAttempts==i+1;
        }
        check("Unavailable metadata retries cap backoff at sixty seconds",bounded);

        ulong counter=100,timestamp=1000;
        const string engine="pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D";
        var sampling=new HardwareMonitor.Reader((scope,query)=>query.Contains("GPUEngine")
            ?new(){new(){["Name"]=engine,["UtilizationPercentage"]=counter,["Timestamp_Sys100NS"]=timestamp}}
            :new(),()=>now);
        var baseline=sampling.Read(0);counter=140;timestamp=1100;
        var measured=sampling.Read(0);counter=180;timestamp=1200;
        var reset=sampling.Read(1);
        check("Hardware reader generation reset preserves unavailable GPU baseline",baseline.Gpus[0].Utilization==null && measured.Gpus[0].Utilization==40 && reset.Gpus[0].Utilization==null);
    }
}
