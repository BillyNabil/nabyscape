using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace NabyScape;

public sealed record AudioOutput(string Id,string Name);

internal enum AudioOperation { ReadOutput,ListOutputs,SetOutput,SetVolume,ToggleMute,ReadMicrophone,ToggleMicrophoneMute }

public sealed partial class AudioService
{
    public string DefaultOutputId { get; private set; }="";
    public bool MicrophoneAvailable { get; private set; }
    public bool MicrophoneMuted { get; private set; }
    public string MicrophoneName { get; private set; }="No microphone";

    public IReadOnlyList<AudioOutput> Outputs()
    {
        if(!CheckAudioActive(AudioOperation.ListOutputs))return Array.Empty<AudioOutput>();
        try
        {
            var outputs=new List<AudioOutput>();
            foreach(var device in EndpointEnumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
            {
                using(device)outputs.Add(new(device.ID,device.FriendlyName));
            }
            LastError=null;
            return outputs.OrderBy(d=>d.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch(Exception ex){AudioError(ex,AudioOperation.ListOutputs);return Array.Empty<AudioOutput>();}
    }

    public bool SetOutput(string id)
    {
        if(!CheckAudioActive(AudioOperation.SetOutput))return false;
        try
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Choose an audio output device.",nameof(id));
            // Reject stale or capture endpoint IDs before invoking Windows policy configuration.
            using(var device=EndpointEnumerator.GetDevice(id))
                if(device.DataFlow!=DataFlow.Render || device.State!=DeviceState.Active)
                    throw new InvalidOperationException("The audio output device is inactive.");
            AudioOutputPolicy.SetDefault(id);
            Read();Changed?.Invoke();return true;
        }
        catch(Exception ex){Read();AudioError(ex,AudioOperation.SetOutput);return false;}
    }

    private MMDevice MicrophoneEndpoint()
    {
        // HasDefault avoids treating an absent Communications endpoint as an error.
        var role=EndpointEnumerator.HasDefaultAudioEndpoint(DataFlow.Capture,Role.Communications)
            ? Role.Communications : Role.Multimedia;
        return EndpointEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture,role);
    }

    private void ReadMicrophone()
    {
        try
        {
            MMDevice? current=MicrophoneEndpoint();
            try
            {
                if(microphoneDevice==null || microphoneDevice.ID!=current.ID){ReleaseMicrophone();microphoneDevice=current;microphoneId=current.ID;current=null;microphoneDevice.AudioEndpointVolume.OnVolumeNotification+=MicrophoneChanged;}
                MicrophoneName=microphoneDevice.FriendlyName;MicrophoneMuted=microphoneDevice.AudioEndpointVolume.Mute;MicrophoneAvailable=true;
            }
            finally{current?.Dispose();}
        }
        catch(Exception ex)
        {
            ReleaseMicrophone();MicrophoneAvailable=false;MicrophoneMuted=false;MicrophoneName="No microphone";AudioError(ex,AudioOperation.ReadMicrophone);
        }
    }

    public bool ToggleMicrophoneMute()
    {
        if(!CheckAudioActive(AudioOperation.ToggleMicrophoneMute))return false;
        try
        {
            // Endpoint volume only: this never opens a recording stream.
            using(var device=MicrophoneEndpoint())device.AudioEndpointVolume.Mute=!device.AudioEndpointVolume.Mute;
            LastError=null;ReadMicrophone();Changed?.Invoke();return MicrophoneAvailable;
        }
        catch(Exception ex){ReadMicrophone();AudioError(ex,AudioOperation.ToggleMicrophoneMute);return false;}
    }
}

internal static class AudioOutputPolicy
{
    // IPolicyConfig is an undocumented Windows interface; failures are reported to the UI.
    internal static void SetDefault(string id)
    {
        object? client=null;
        try
        {
            client=new PolicyConfigClient();
            var policy=(IPolicyConfig)client;
            ApplyRoles(role=>Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id,role)));
        }
        finally{if(client!=null && Marshal.IsComObject(client))Marshal.ReleaseComObject(client);}
    }

    internal static void ApplyRoles(Action<Role> apply)
    {
        var errors=new List<Exception>();
        foreach(var role in new[]{Role.Console,Role.Multimedia,Role.Communications})
        {
            try{apply(role);}
            catch(Exception ex){errors.Add(new InvalidOperationException($"Could not change audio output for {role}: {ex.Message}",ex));}
        }
        if(errors.Count>0)throw new AggregateException("Some audio output roles could not be changed.",errors);
    }

    [ComImport,Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    // Methods preceding SetDefaultEndpoint preserve the native IPolicyConfig vtable order.
    [ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id,[MarshalAs(UnmanagedType.Bool)] bool defaultFormat,IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr endpointFormat,IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id,[MarshalAs(UnmanagedType.Bool)] bool defaultPeriod,IntPtr period,IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr key,IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr key,IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id,Role role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id,[MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
