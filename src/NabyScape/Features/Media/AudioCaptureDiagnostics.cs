using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NabyScape;

// Output (loopback) capture failures are reported once per reason instead of being swallowed,
// and each output retries with a growing delay instead of on every frame.
internal static class AudioCaptureDiagnostics
{
    internal const int StallMs=3000;
    internal static int RetryDelay(int failures)=>(int)Math.Min(60000,5000*Math.Pow(2,Math.Clamp(failures-1,0,4)));
    internal static bool SupportedFormat(int bitsPerSample,int channels,bool floating)=>channels is >=1 and <=32 && (floating?bitsPerSample==32:bitsPerSample is 16 or 24 or 32);
    internal static string Describe(Exception error)
    {
        string reason=(uint)error.HResult switch
        {
            0x8889000A=>"another app is using the output in exclusive mode",
            0x88890004 or 0x88890026=>"the output device changed or was removed",
            0x88890008=>"Windows reported an unsupported output format",
            0x8889000F or 0x80070490=>"Windows could not open the output device",
            0x88890010=>"the Windows audio service is not running",
            0x80070005=>"Windows denied access to output capture",
            _=>error switch
            {
                NotSupportedException=>"the output sample format is not supported",
                TimeoutException=>"the output delivered no samples while audio was playing",
                UnauthorizedAccessException=>"Windows denied access to output capture",
                _=>"output capture could not start"
            }
        };
        return reason+" (0x"+((uint)error.HResult).ToString("X8")+")";
    }
}

internal static class PackageIdentity
{
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder? name);
    // Packaged players (for example Media Player) report a package AUMID while their audio
    // session belongs to a differently named process of the same package.
    internal static string? FamilyName(int processId)
    {
        var handle=OpenProcess(0x1000,false,processId);if(handle==IntPtr.Zero)return null;
        try{uint length=256;var name=new StringBuilder((int)length);return GetPackageFamilyName(handle,ref length,name)==0?name.ToString():null;}
        catch(EntryPointNotFoundException){return null;}
        finally{CloseHandle(handle);}
    }
}

public sealed partial class AudioService
{
    // True when the selected app has no matching audio session and the waveform follows the default output.
    internal bool MediaMeterFallback {get;private set;}
    internal string? SpectrumStatus=>outputSpectrum?.Status;
    internal static bool MediaPackageMatches(string source,string? packageFamily)
    {
        int bang=source.IndexOf('!');
        return bang>0 && !string.IsNullOrEmpty(packageFamily) && source.AsSpan(0,bang).Equals(packageFamily,StringComparison.OrdinalIgnoreCase);
    }
    private float DefaultOutputPeak()
    {
        try{var device=meterDevice;if(device==null || device.AudioEndpointVolume.Mute)return 0;float value=device.AudioMeterInformation.MasterPeakValue;return float.IsFinite(value)?Math.Clamp(value,0,1):0;}
        catch(Exception ex){AudioError(ex);return 0;}
    }
    private void ReportSpectrumFailure(string reason,Exception? error)
    {
        var report=new InvalidOperationException("Output spectrum capture unavailable: "+reason+". The waveform uses measured amplitude instead.",error);
        try{if(log!=null)log(report);else Trace.TraceWarning(report.ToString());}catch { /* Diagnostics must not break audio. */ }
    }
}
