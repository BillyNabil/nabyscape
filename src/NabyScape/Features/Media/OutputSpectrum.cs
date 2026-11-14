using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace NabyScape;

// Real log-spaced FFT bands. Only output samples are inspected, in memory.
internal sealed class SpectrumTransform
{
    private const int Size=2048;
    private readonly float[] ring=new float[Size];
    private readonly Complex[] fft=new Complex[Size];
    private readonly float[] window=new float[Size];
    private readonly int[] firstBin=new int[25],lastBin=new int[25];
    private readonly int rate;
    private int cursor,filled,since;
    internal readonly float[] Bands=new float[25];
    internal long Sequence {get;private set;}
    internal SpectrumTransform(int rate)
    {
        this.rate=rate;for(int i=0;i<Size;i++)window[i]=(float)(.5-.5*Math.Cos(2*Math.PI*i/(Size-1)));
        for(int band=0;band<25;band++)
        {
            double low=40*Math.Pow(400,band/25d),high=40*Math.Pow(400,(band+1)/25d);
            firstBin[band]=Math.Clamp((int)Math.Floor(low*Size/rate),1,Size/2-1);
            lastBin[band]=Math.Clamp((int)Math.Ceiling(high*Size/rate),firstBin[band]+1,Size/2);
        }
    }
    internal void Add(float sample)
    {
        ring[cursor]=float.IsFinite(sample)?Math.Clamp(sample,-1,1):0;cursor=(cursor+1)%Size;filled=Math.Min(Size,filled+1);since++;
        if(filled<Size || since<rate/30)return;since=0;
        for(int i=0;i<Size;i++){fft[i].X=ring[(cursor+i)%Size]*window[i];fft[i].Y=0;}
        FastFourierTransform.FFT(true,11,fft);
        for(int band=0;band<25;band++)
        {
            int first=firstBin[band],last=lastBin[band];
            double energy=0;for(int i=first;i<last;i++)energy+=fft[i].X*fft[i].X+fft[i].Y*fft[i].Y;
            double db=20*Math.Log10(Math.Max(1e-9,Math.Sqrt(energy)));
            Bands[band]=(float)Math.Clamp((db+68)/60,0,1);
        }
        Sequence++;
    }
}

// One shared capture per matching output, regardless of visible consumer count.
// Demand expires after the last waveform stops reading, including hidden windows.
internal sealed class OutputSpectrum : IDisposable
{
    private sealed class Stream : IDisposable
    {
        private readonly MMDevice device;
        private readonly WasapiLoopbackCapture capture;
        private readonly SpectrumTransform transform;
        private readonly object sync=new();
        private readonly int bytes,channels;
        private readonly bool floating;
        private long updated;
        private readonly long created=Environment.TickCount64;
        internal Exception? Failure {get;private set;}
        internal Stream(string id)
        {
            // Shared-mode loopback of the endpoint's own mix format; no exclusive access is requested.
            using var enumerator=new MMDeviceEnumerator();device=enumerator.GetDevice(id);
            try
            {
                capture=new WasapiLoopbackCapture(device);var format=capture.WaveFormat;
                bytes=format.BitsPerSample/8;channels=format.Channels;
                floating=format.Encoding==WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
                if(!AudioCaptureDiagnostics.SupportedFormat(format.BitsPerSample,channels,floating))throw new NotSupportedException("Unsupported output sample format.");
                transform=new(format.SampleRate);capture.DataAvailable+=Receive;capture.RecordingStopped+=Stopped;capture.StartRecording();
            }
            catch{try{capture?.Dispose();}catch{}device.Dispose();throw;}
        }
        private void Stopped(object? sender,StoppedEventArgs e){if(e.Exception!=null)Failure=e.Exception;}
        // A capture that silently stops delivering (seen after device or format changes) is replaced.
        internal bool Quiet(long now){lock(sync)return now-Math.Max(created,updated)>AudioCaptureDiagnostics.StallMs;}
        private void Receive(object? sender,WaveInEventArgs e)
        {
            lock(sync)
            {
                for(int offset=0;offset+bytes*channels<=e.BytesRecorded;offset+=bytes*channels)
                {
                    float mono=0;
                    for(int channel=0;channel<channels;channel++)
                    {
                        int p=offset+channel*bytes;float value=floating?BitConverter.ToSingle(e.Buffer,p):bytes switch
                        {2=>BitConverter.ToInt16(e.Buffer,p)/32768f,3=>((e.Buffer[p]|e.Buffer[p+1]<<8|e.Buffer[p+2]<<16)<<8)/2147483648f,_=>BitConverter.ToInt32(e.Buffer,p)/2147483648f};
                        mono+=value/channels;
                    }
                    transform.Add(mono);
                }
                if(transform.Sequence>0)updated=Environment.TickCount64;
            }
        }
        internal bool Merge(float[] target,long now)
        {lock(sync){if(transform.Sequence==0 || now-updated>200)return false;for(int i=0;i<25;i++)target[i]=Math.Max(target[i],transform.Bands[i]);return true;}}
        public void Dispose(){capture.DataAvailable-=Receive;capture.RecordingStopped-=Stopped;try{capture.StopRecording();}catch{}capture.Dispose();device.Dispose();}
    }
    private readonly Dictionary<string,Stream> streams=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(int Failures,long Retry)> backoff=new(StringComparer.Ordinal);
    private readonly DispatcherTimer expiry=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private readonly Action<string,Exception?>? report;
    private long lastDemand,lastDiscovery;
    private string source="";
    private string? reported;
    internal int Count=>streams.Count;
    // Last capture problem in plain words; cleared once bands arrive again.
    internal string? Status {get;private set;}
    internal OutputSpectrum(Action<string,Exception?>? report=null){this.report=report;expiry.Tick+=(_,_)=>{if(Environment.TickCount64-lastDemand>650)Stop();};}
    internal bool Read(string media,IEnumerable<string> outputs,float[] target,bool audible=false)
    {
        long now=Environment.TickCount64;lastDemand=now;
        if(source!=media){Stop();source=media;lastDiscovery=0;}
        foreach(var failed in streams.Where(pair=>pair.Value.Failure!=null).ToArray()){Fail(failed.Key,failed.Value.Failure!,now);failed.Value.Dispose();streams.Remove(failed.Key);}
        // Loopback delivers nothing for an idle output, so only all-quiet captures during audible playback count as stalled.
        if(audible && streams.Count>0 && streams.Values.All(stream=>stream.Quiet(now)))
            foreach(var quiet in streams.ToArray()){Fail(quiet.Key,new TimeoutException("Loopback capture delivered no samples."),now);quiet.Value.Dispose();streams.Remove(quiet.Key);}
        if(streams.Count==0 || now-lastDiscovery>2000)
        {
            lastDiscovery=now;var wanted=outputs.Take(4).ToHashSet(StringComparer.Ordinal);
            foreach(string id in streams.Keys.Where(id=>!wanted.Contains(id)).ToArray()){streams[id].Dispose();streams.Remove(id);}
            foreach(string id in wanted.Where(id=>!streams.ContainsKey(id) && !(backoff.TryGetValue(id,out var wait) && now<wait.Retry)))
                try{streams.Add(id,new Stream(id));}catch(Exception ex){Fail(id,ex,now);}
        }
        if(!expiry.IsEnabled)expiry.Start();Array.Clear(target);bool ready=false;
        foreach(var stream in streams){if(stream.Value.Merge(target,now)){ready=true;backoff.Remove(stream.Key);}}
        if(ready)Status=reported=null;
        return ready;
    }
    private void Fail(string id,Exception error,long now)
    {
        int failures=(backoff.TryGetValue(id,out var previous)?previous.Failures:0)+1;
        backoff[id]=(failures,now+AudioCaptureDiagnostics.RetryDelay(failures));
        Status=AudioCaptureDiagnostics.Describe(error);
        if(Status==reported)return;reported=Status;
        try{report?.Invoke(Status,error);}catch { /* A diagnostic sink must not stop the waveform fallback. */ }
    }
    internal void Stop(){expiry.Stop();foreach(var stream in streams.Values)stream.Dispose();streams.Clear();}
    public void Dispose()=>Stop();
}

public sealed partial class AudioService
{
    private readonly HashSet<string> mediaOutputs=new(StringComparer.Ordinal);
    private OutputSpectrum? outputSpectrum;
    private readonly float[] spectrumBands=new float[25];
    internal float[]? MediaSpectrum {get;private set;}
    internal int SpectrumCaptureCount=>outputSpectrum?.Count??0;
    private bool spectrumEnabled=true;
    internal bool SpectrumEnabled
    {get=>spectrumEnabled;set{if(spectrumEnabled==value)return;spectrumEnabled=value;if(!value){outputSpectrum?.Stop();MediaSpectrum=null;}}}
    private void SampleSpectrum(string? source)
    {
        if(!spectrumEnabled || DiagnosticPeak!=null || MediaMeterMuted || string.IsNullOrEmpty(source) || mediaOutputs.Count==0)
        {outputSpectrum?.Stop();MediaSpectrum=null;return;}
        outputSpectrum??=new(ReportSpectrumFailure);MediaSpectrum=outputSpectrum.Read(source,mediaOutputs,spectrumBands,CurrentMediaPeak>.02f)?spectrumBands:null;
    }
}
