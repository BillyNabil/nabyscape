using System;
using System.Management;

namespace NabyScape;

// Only subscribes to Windows' own brightness events. External monitors without
// this WMI provider remain unsupported; there are no writes or polling fallback.
public sealed class BrightnessService : IDisposable
{
    private bool attempted;
    private ManagementEventWatcher? watcher;
    public string Status {get;private set;}="Brightness feedback unavailable on this display";
    public event Action<int>? Changed;
    public void Start(bool enabled=true)
    {
        if(!enabled){Dispose();attempted=false;Status="Brightness feedback off";return;}
        if(watcher!=null || attempted)return;attempted=true;
        try
        {
            using var query=new ManagementObjectSearcher(@"root\WMI","SELECT InstanceName FROM WmiMonitorBrightness");
            using var results=query.Get();
            if(results.Count==0)return;
            watcher=new ManagementEventWatcher(@"root\WMI","SELECT * FROM WmiMonitorBrightnessEvent");
            watcher.EventArrived+=OnBrightness;watcher.Start();Status="Brightness events active";
        }
        catch(ManagementException){Dispose();}
        catch(System.Runtime.InteropServices.COMException){Dispose();}
        catch(UnauthorizedAccessException){Dispose();}
    }
    private void OnBrightness(object sender,EventArrivedEventArgs args)
    {
        try{Changed?.Invoke(Math.Clamp(Convert.ToInt32(args.NewEvent["Brightness"]),0,100));}
        catch(ManagementException){}
        finally{args.NewEvent.Dispose();}
    }
    public void Dispose()
    {
        var old=watcher;watcher=null;
        if(old==null)return;
        old.EventArrived-=OnBrightness;try{old.Stop();}catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}finally{old.Dispose();}
    }
}
