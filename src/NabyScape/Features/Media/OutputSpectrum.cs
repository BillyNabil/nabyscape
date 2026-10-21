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
        internal bool Failed {get;private set;}
        internal Stream(string id)
        {
            using var enumerator=new MMDeviceEnumerator();device=enumerator.GetDevice(id);
            try
            {
                capture=new WasapiLoopbackCapture(device);var format=capture.WaveFormat;
                bytes=format.BitsPerSample/8;channels=format.Channels;
                floating=format.Encoding==WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
                if(bytes is not (2 or 3 or 4) || channels<1)throw new NotSupportedException("Unsupported output sample format.");
                transform=new(format.SampleRate);capture.DataAvailable+=Receive;capture.RecordingStopped+=Stopped;capture.StartRecording();
            }
            catch{try{capture?.Dispose();}catch{}device.Dispose();throw;}
        }
        private void Stopped(object? sender,StoppedEventArgs e){if(e.Exception!=null)Failed=true;}
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
    private readonly DispatcherTimer expiry=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private long lastDemand,lastDiscovery,retry;
    private string source="";
    internal int Count=>streams.Count;
    internal OutputSpectrum(){expiry.Tick+=(_,_)=>{if(Environment.TickCount64-lastDemand>650)Stop();};}
    internal bool Read(string media,IEnumerable<string> outputs,float[] target)
    {
        long now=Environment.TickCount64;lastDemand=now;
        if(source!=media){Stop();source=media;lastDiscovery=0;retry=0;}
        if(now>=retry && (streams.Count==0 || now-lastDiscovery>2000 || streams.Values.Any(stream=>stream.Failed)))
        {
            lastDiscovery=now;var wanted=outputs.Take(4).ToHashSet(StringComparer.Ordinal);
            foreach(string id in streams.Keys.Where(id=>!wanted.Contains(id) || streams[id].Failed).ToArray()){streams[id].Dispose();streams.Remove(id);}
            foreach(string id in wanted.Where(id=>!streams.ContainsKey(id)))try{streams.Add(id,new Stream(id));}catch{retry=now+5000;}
        }
        if(!expiry.IsEnabled)expiry.Start();Array.Clear(target);bool ready=false;
        foreach(var stream in streams.Values)ready|=stream.Merge(target,now);
        return ready;
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
        outputSpectrum??=new();MediaSpectrum=outputSpectrum.Read(source,mediaOutputs,spectrumBands)?spectrumBands:null;
    }
}
