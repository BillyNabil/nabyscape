using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace NabyScape;

public sealed partial class AudioService
{
    private PeakCache<MediaEndpoint>? mediaPeaks;
    private string mediaPeakSource="";
    internal bool MediaMeterMuted { get; private set; }
    private int meterSessions,meterAudible;
    private sealed record MediaEndpoint(MMDevice Device,List<AudioSessionControl> Sessions) : IDisposable
    {
        public void Dispose(){try{foreach(var session in Sessions)try{session.Dispose();}catch{}}finally{Device.Dispose();}}
    }
    private float ReadMediaEndpoint(MediaEndpoint endpoint)
    {
        float peak=0;bool outputMuted=endpoint.Device.AudioEndpointVolume.Mute;
        foreach(var session in endpoint.Sessions)
        {
            meterSessions++;
            if(outputMuted || session.SimpleAudioVolume.Mute || session.SimpleAudioVolume.Volume<=0)continue;
            meterAudible++;peak=Math.Max(peak,session.AudioMeterInformation.MasterPeakValue);
        }
        return peak;
    }
    internal static bool MediaProcessMatches(string source,string process)
    {
        source=source.ToLowerInvariant();process=process.ToLowerInvariant();
        return process.Length>2 && (source==process || source==process+".exe" || source.StartsWith(process+".") || source.StartsWith(process+"_") || source.Contains("."+process+"_") || source.EndsWith("!"+process));
    }
    internal float MediaPeak(string? source)
    {
        if(disposed || string.IsNullOrWhiteSpace(source))return 0;
        if(mediaPeaks==null || mediaPeakSource!=source)
        {
            mediaPeaks?.Dispose();mediaPeakSource=source;
            mediaPeaks=new PeakCache<MediaEndpoint>(()=>DiscoverMediaSessions(source),ReadMediaEndpoint,endpoint=>endpoint.Dispose(),ex=>AudioError(ex));
        }
        meterSessions=meterAudible=0;
        float peak=mediaPeaks.Read(DateTimeOffset.UtcNow);
        MediaMeterMuted=meterSessions>0 && meterAudible==0;
        return peak;
    }
    private IEnumerable<MediaEndpoint> DiscoverMediaSessions(string source)
    {
        var found=new List<MediaEndpoint>();
        try
        {
            foreach(var device in EndpointEnumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
            {
                var matches=new List<AudioSessionControl>();
                try
                {
                    var sessions=device.AudioSessionManager.Sessions;
                    for(int i=0;i<sessions.Count;i++)
                    {
                        var session=sessions[i];bool keep=false;
                        try
                        {
                            using var process=Process.GetProcessById((int)session.GetProcessID);
                            keep=MediaProcessMatches(source,process.ProcessName);
                            if(keep)matches.Add(session);
                        }
                        catch { /* Expired/system sessions have no readable process. */ }
                        finally{if(!keep)session.Dispose();}
                    }
                }
                catch(Exception ex){AudioError(ex);}
                if(matches.Count>0)found.Add(new(device,matches));else device.Dispose();
            }
        }
        catch(Exception ex){AudioError(ex);}
        return found;
    }
}
