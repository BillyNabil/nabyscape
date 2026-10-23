using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace NabyScape;

public sealed partial class AudioService
{
    private EndpointNotifications? notifications;
    private bool notificationsAttempted;
    private int deviceRefreshQueued;
    private int endpointChangeQueued;
    internal event Action? EndpointsChanged;
    private long nextAudioAudit;
    private MMDevice? microphoneDevice;
    private int microphoneGeneration;
    private string microphoneId="";
    private sealed class EndpointNotifications(AudioService owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string id,DeviceState state)=>owner.QueueDeviceRefresh(true);
        public void OnDeviceAdded(string id)=>owner.QueueDeviceRefresh();
        public void OnDeviceRemoved(string id)=>owner.QueueDeviceRefresh(true);
        public void OnDefaultDeviceChanged(DataFlow flow,Role role,string id)=>owner.QueueDeviceRefresh(true);
        public void OnPropertyValueChanged(string id,PropertyKey key)
        {if(id==owner.DefaultOutputId || id==owner.microphoneId)owner.QueueDeviceRefresh();}
    }
    private void EnsureDeviceNotifications()
    {
        if(notificationsAttempted)return;notificationsAttempted=true;
        try{var client=new EndpointNotifications(this);EndpointEnumerator.RegisterEndpointNotificationCallback(client);notifications=client;}
        catch(Exception ex){AudioError(ex);}
    }
    private void QueueDeviceRefresh(bool endpointChanged=false)
    {
        if(endpointChanged)Interlocked.Exchange(ref endpointChangeQueued,1);
        if(disposed || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref deviceRefreshQueued,1)!=0)return;
        try{dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,()=>
        {
            Interlocked.Exchange(ref deviceRefreshQueued,0);if(disposed)return;
            if(Interlocked.Exchange(ref endpointChangeQueued,0)!=0)EndpointsChanged?.Invoke();
            mediaSampleGate.Reset();mediaPeaks?.Dispose();mediaPeaks=null;Read();Changed?.Invoke();
        });}catch(InvalidOperationException){Interlocked.Exchange(ref deviceRefreshQueued,0);}
    }
    internal void Poll()
    {if(!disposed && Environment.TickCount64>=nextAudioAudit)Read();}
    private void ReleaseMicrophone()
    {
        microphoneGeneration++;var old=microphoneDevice;microphoneDevice=null;microphoneId="";
        if(old==null)return;try{old.AudioEndpointVolume.OnVolumeNotification-=MicrophoneChanged;}catch(Exception ex){AudioError(ex);}finally{try{old.Dispose();}catch(Exception ex){AudioError(ex);}}
    }
    private void MicrophoneChanged(AudioVolumeNotificationData data)
    {
        int generation=microphoneGeneration;if(disposed || dispatcher.HasShutdownStarted)return;
        try{dispatcher.BeginInvoke(()=>{if(disposed || generation!=microphoneGeneration)return;MicrophoneMuted=data.Muted;Changed?.Invoke();});}catch(InvalidOperationException){}
    }
    private void DisposeNotifications()
    {
        if(notifications!=null)try{enumerator?.UnregisterEndpointNotificationCallback(notifications);}catch(Exception ex){AudioError(ex);}finally{notifications=null;}
        ReleaseMicrophone();mediaSampleGate.Reset();
    }
}
