using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;

namespace NabyScape;

internal enum MixerScope { Apps, Outputs }
internal sealed record MixerTarget(MixerScope Scope,string OutputId,string SessionId="");
internal sealed record MixerItem(MixerTarget Target,string Name,string OutputName,double Volume,bool Muted,string? ProcessPath=null);
internal sealed record MixerSnapshot(IReadOnlyList<MixerItem> Outputs,IReadOnlyList<MixerItem> Apps,IReadOnlyList<string> Errors);
internal sealed record MixerResult(int Changed,int Failed,IReadOnlyList<string> Errors)
{
    public string Summary=>$"{Changed} changed · {Failed} failed"+(Errors.Count>0?" · "+string.Join("; ",Errors):"");
}

internal sealed class AudioMixer
{
    private readonly Dictionary<string,string?> processPaths=new();
    internal static string? LocalProcessImage(uint pid)
    {
        if(pid==0)return null;
        try
        {
            using var process=OpenProcess(0x1000,false,pid); // Query limited information; works across bitness without module access.
            var path=new StringBuilder(32768);int length=path.Capacity;
            return !process.IsInvalid && QueryFullProcessImageName(process,0,path,ref length)?path.ToString():null;
        }
        catch{return null;}
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder name,ref int size);
    internal static bool IsControllableSession(int state)=>state is 0 or 1;
    public static double BoundedVolume(double value)=>double.IsFinite(value)?Math.Clamp(value,0,1):throw new ArgumentException("Volume must be finite.");

    public MixerSnapshot Snapshot()
    {
        var outputs=new List<MixerItem>();var apps=new List<MixerItem>();var errors=new List<string>();
        try
        {
            using var enumerator=new MMDeviceEnumerator();
            foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
            {
                using(device)
                {
                    string id="",name="Output";
                    try
                    {
                        id=device.ID;name=device.FriendlyName;
                        outputs.Add(new(new(MixerScope.Outputs,id),name,name,device.AudioEndpointVolume.MasterVolumeLevelScalar,device.AudioEndpointVolume.Mute));
                    }
                    catch(Exception ex){errors.Add(name+": "+ex.Message);}
                    if(id.Length==0)continue;
                    try
                    {
                        WithSessions(id,(session,volume)=>
                        {
                            try
                            {
                                Check(session.GetState(out int state));if(!IsControllableSession(state))return;
                                Check(session.GetSessionInstanceIdentifier(out string instance));
                                Check(session.GetDisplayName(out string display));Check(session.GetProcessId(out uint pid));
                                if(session.IsSystemSoundsSession()==0)display="System sounds";
                                else if(string.IsNullOrWhiteSpace(display))
                                {
                                    try{using var process=Process.GetProcessById((int)pid);display=process.ProcessName;}
                                    catch{display="App (PID "+pid+")";}
                                }
                                string pathKey=pid+"|"+instance;
                                if(!processPaths.TryGetValue(pathKey,out string? processPath))
                                {
                                    processPath=LocalProcessImage(pid);
                                    if(processPaths.Count>=256)processPaths.Clear();
                                    processPaths[pathKey]=processPath;
                                }
                                Check(volume.GetMasterVolume(out float level));Check(volume.GetMute(out bool mute));
                                apps.Add(new(new(MixerScope.Apps,id,instance),display,name,level,mute,processPath));
                            }
                            catch(Exception ex){errors.Add(name+" session: "+ex.Message);}
                        },ex=>errors.Add(name+" session: "+ex.Message));
                    }
                    catch(Exception ex){errors.Add(name+" apps: "+ex.Message);}
                }
            }
        }
        catch(Exception ex){errors.Add("Audio unavailable: "+ex.Message);}
        return new(outputs.OrderBy(x=>x.Name).ToArray(),apps.OrderBy(x=>x.Name).ThenBy(x=>x.OutputName).ToArray(),errors);
    }

    public MixerResult AdjustAll(MixerScope scope,double delta)
    {
        var snapshot=Snapshot();
        var result=Adjust(snapshot,scope,delta,(target,value)=>Set(target,value,null));
        return result with {Errors=snapshot.Errors.Concat(result.Errors).ToArray()};
    }

    // The injected writer keeps all-scope and stale-target tests entirely away from native audio writes.
    internal static MixerResult Adjust(MixerSnapshot snapshot,MixerScope scope,double delta,Func<MixerTarget,double,string?> write)
    {
        if(!double.IsFinite(delta))throw new ArgumentException("Adjustment must be finite.");
        int changed=0,failed=0;var errors=new List<string>();
        foreach(var item in (scope==MixerScope.Apps?snapshot.Apps:snapshot.Outputs).DistinctBy(x=>x.Target))
        {
            try
            {
                string? error=write(item.Target,BoundedVolume(item.Volume+delta));
                if(error==null)changed++;else{failed++;errors.Add(item.Name+": "+error);}
            }
            catch(Exception ex){failed++;errors.Add(item.Name+": "+ex.Message);}
        }
        return new(changed,failed,errors);
    }

    public string? Set(MixerTarget target,double? level,bool? muted)
    {
        try
        {
            if(level.HasValue)level=BoundedVolume(level.Value);
            using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(target.OutputId);
            if(device.State!=DeviceState.Active || device.DataFlow!=DataFlow.Render)throw new InvalidOperationException("Output disconnected. Refresh and try again.");
            if(target.Scope==MixerScope.Outputs)
            {
                if(level.HasValue)device.AudioEndpointVolume.MasterVolumeLevelScalar=(float)level.Value;
                if(muted.HasValue)device.AudioEndpointVolume.Mute=muted.Value;
            }
            else
            {
                bool found=false;
                WithSessions(target.OutputId,(session,volume)=>
                {
                    Check(session.GetSessionInstanceIdentifier(out string id));if(id!=target.SessionId)return;
                    Check(session.GetState(out int state));if(!IsControllableSession(state))return;
                    found=true;var context=Guid.Empty;
                    if(level.HasValue)Check(volume.SetMasterVolume((float)level.Value,ref context));
                    if(muted.HasValue)Check(volume.SetMute(muted.Value,ref context));
                },_=>{});
                if(!found)throw new InvalidOperationException("App session ended. Refresh and try again.");
            }
            return null;
        }
        catch(Exception ex){return ex.Message;}
    }

    private static void Check(int hr)=>Marshal.ThrowExceptionForHR(hr);
    private static void Release(object? value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}

    private static void WithSessions(string output,Action<ISession,IVolume> visit,Action<Exception>? onError=null)
    {
        object? enumerator=null,device=null,manager=null,collection=null;
        try
        {
            enumerator=Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            Check(((IDevices)enumerator).GetDevice(output,out var endpoint));device=endpoint;
            var iid=new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            Check(endpoint.Activate(ref iid,23,IntPtr.Zero,out var activated));manager=activated;
            Check(((ISessions)manager).GetSessionEnumerator(out var sessions));collection=sessions;
            Check(sessions.GetCount(out int count));
            for(int i=0;i<count;i++)
            {
                object? control=null;
                try
                {
                    try{Check(sessions.GetSession(i,out var session));control=session;}
                    catch(Exception ex) when(onError!=null){onError(ex);continue;}
                    visit((ISession)control,(IVolume)control);
                }
                finally{Release(control);}
            }
        }
        finally{Release(collection);Release(manager);Release(device);Release(enumerator);}
    }

    // Only the used prefixes are declared; order matches the Windows Core Audio ABI.
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevices
    {
        [PreserveSig]int EnumAudioEndpoints(int flow,int state,out IntPtr devices);
        [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IntPtr device);
        [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig]int Activate(ref Guid iid,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);
    }
    [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessions
    {
        [PreserveSig]int GetAudioSessionControl(ref Guid session,int flags,out IntPtr control);
        [PreserveSig]int GetSimpleAudioVolume(ref Guid session,int flags,out IntPtr volume);
        [PreserveSig]int GetSessionEnumerator(out ISessionList sessions);
    }
    [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionList
    {
        [PreserveSig]int GetCount(out int count);
        [PreserveSig]int GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)]out object session);
    }
    [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISession
    {
        [PreserveSig]int GetState(out int state);
        [PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);
        [PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr context);
        [PreserveSig]int GetIconPath(out IntPtr path);
        [PreserveSig]int SetIconPath(IntPtr path,IntPtr context);
        [PreserveSig]int GetGroupingParam(out Guid grouping);
        [PreserveSig]int SetGroupingParam(ref Guid grouping,IntPtr context);
        [PreserveSig]int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig]int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetProcessId(out uint id);
        [PreserveSig]int IsSystemSoundsSession();
    }
    [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVolume
    {
        [PreserveSig]int SetMasterVolume(float volume,ref Guid context);
        [PreserveSig]int GetMasterVolume(out float volume);
        [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);
        [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
    }
}
