using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NAudio.Wave;

namespace NabyScape;

// One session owns its native children. Closing it (or the app) releases model memory.
internal sealed partial class AssistantManagedSpeech : IDisposable
{
    private readonly AssistantSpeechInstall assets;
    private readonly AssistantLocalVoiceOptions options;
    private readonly CancellationTokenSource lifetime=new();
    private readonly SafeFileHandle job;
    private Process? server;
    private AssistantSpeechWorker? ttsWorker;
    private string lastError="";
    private bool disposed;
    internal Uri? AsrUrl {get;private set;}
    internal AssistantManagedSpeech(AssistantSpeechInstall assets,AssistantLocalVoiceOptions options)
    {
        AssistantSpeechPlatform.RequireNativePacks();
        this.assets=assets;this.options=options;job=CreateJobObject(IntPtr.Zero,null);
        if(job.IsInvalid)throw new IOException("Could not create a managed speech session.");
        var limits=new JobLimits();limits.Basic.LimitFlags=0x2000;
        if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<JobLimits>())){job.Dispose();throw new IOException("Could not configure speech-process cleanup.");}
    }
    internal async Task Start(Action<string> status,CancellationToken token)
    {
        var model=AssistantOwnSpeech.Asr(options);var tts=AssistantOwnSpeech.Tts(options);
        if(!assets.Installed(model,tts))throw new IOException("Prepare your voice setup first: Voice mode → Choose ASR & TTS. Check model paths if you use your own files.");
        status("Checking available memory for local speech…");var hardware=await assets.Scan(token);
        if(!hardware.FitsPack(model,tts))throw new IOException("There is not enough available memory for this speech pack. Close other apps or install a smaller model/voice.");
        await StartListeningModel(status,model,hardware,token);
        await StartSpeakingModel(status,tts,token);
    }
    // Independent checks never load the other half of a speech pack.
    internal async Task StartCheck(bool listening,Action<string> status,CancellationToken token)
    {
        if(!AssistantSpeechInstall.CppRuntimePresent())throw new IOException("Prepare speech engines in Voice setup first.");
        var hardware=await assets.Scan(token);
        if(listening)
        {
            var model=AssistantOwnSpeech.Asr(options);
            if(!assets.HasAsrWeights(model) || !hardware.Fits(model))throw new IOException("Install or locate the listening model and check available memory.");
            await StartListeningModel(status,model,hardware,token);
        }
        else
        {
            var model=AssistantOwnSpeech.Tts(options);
            if(!assets.HasTtsWeights(model) || hardware.FreeGb<model.FreeGb)throw new IOException("Install or locate the speaking model and check available memory.");
            await StartSpeakingModel(status,model,token);
        }
    }
    private async Task StartSpeakingModel(Action<string> status,SpeechTtsModel tts,CancellationToken token)
    {
        if(tts.Package!=null)
        {
            status("Loading "+tts.Name+" · stays ready during this voice session…");ttsWorker=new();
            string library=Path.Combine(assets.SherpaRoot,SpeechCatalog.SherpaFolder,"lib","sherpa-onnx-c-api.dll");
            await ttsWorker.Start(new(library,assets.VoiceDirectory(tts),tts.Engine,options.SpeechLanguage,tts.Speaker,options.SpeechPreference=="Quality"?12:options.SpeechPreference=="Light"?4:8),Launch,token);
        }
    }
    private async Task StartListeningModel(Action<string> status,SpeechAsrModel model,SpeechHardware hardware,CancellationToken token)
    {
        if(model.Native){status(model.Name+" selected · the native model loads when you speak.");return;}
        bool gpu=options.ManagedGpu && hardware.UseGpu(model) && File.Exists(assets.WhisperExe(true));
        if(gpu)
        {
            try{status("Loading the local listening model on NVIDIA GPU…");await StartServer(true,model,token);return;}
            catch(OperationCanceledException){throw;}
            catch(Exception){StopServer();status("GPU runtime unavailable. Loading the CPU fallback…");}
        }
        else status("Loading the local listening model on CPU…");
        await StartServer(false,model,token);
    }
    private async Task StartServer(bool gpu,SpeechAsrModel model,CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);linked.CancelAfter(TimeSpan.FromSeconds(120));
        int port;using(var listener=new TcpListener(IPAddress.Loopback,0)){listener.Start();port=((IPEndPoint)listener.LocalEndpoint).Port;}
        string route="/"+Guid.NewGuid().ToString("N");
        var info=NativeInfo(assets.WhisperExe(gpu));info.ArgumentList.Add("-m");info.ArgumentList.Add(assets.AsrPath(model));info.ArgumentList.Add("--host");info.ArgumentList.Add("127.0.0.1");info.ArgumentList.Add("--port");info.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));info.ArgumentList.Add("--request-path");info.ArgumentList.Add(route);info.ArgumentList.Add("-l");info.ArgumentList.Add("auto");info.ArgumentList.Add("-t");info.ArgumentList.Add(Math.Clamp(Environment.ProcessorCount/2,1,8).ToString());if(!gpu)info.ArgumentList.Add("-ng");
        server=Launch(info);server.OutputDataReceived+=(_,_)=>{};server.ErrorDataReceived+=(_,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))lastError=e.Data.Length>300?e.Data[..300]:e.Data;};server.BeginOutputReadLine();server.BeginErrorReadLine();
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(1)};var url=new Uri($"http://127.0.0.1:{port}{route}/");
        try
        {
            while(true)
            {
                linked.Token.ThrowIfCancellationRequested();if(server.HasExited)throw new IOException("Local speech engine stopped ("+server.ExitCode+"). "+(lastError.Length>0?lastError:"Reinstall the speech pack; the Microsoft Visual C++ x64 runtime may be required."));
                try{using var health=await http.GetAsync(new Uri(url,"health"),linked.Token);if(health.IsSuccessStatusCode){AsrUrl=url;return;}}
                catch(HttpRequestException){}catch(OperationCanceledException){linked.Token.ThrowIfCancellationRequested();}
                await Task.Delay(250,linked.Token);
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested && !lifetime.IsCancellationRequested){throw new IOException("The local listening model took too long to load. Choose a smaller model or check the speech pack.");}
    }
    internal async Task<byte[]> Speak(string text,CancellationToken token)
    {
        var model=AssistantOwnSpeech.Tts(options);if(model.Id=="windows")throw new InvalidOperationException("Windows speech is handled by the built-in voice engine.");
        if(model.Package!=null)return await (ttsWorker??throw new IOException("The voice worker is not ready. End voice and start it again.")).Speak(text,token);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);linked.CancelAfter(TimeSpan.FromMinutes(2));
        var info=NativeInfo(assets.PiperExe);info.RedirectStandardInput=true;info.StandardInputEncoding=new UTF8Encoding(false);info.ArgumentList.Add("--model");info.ArgumentList.Add(assets.TtsPath(model));info.ArgumentList.Add("--output-raw");if(model.SourcePath.Length>0 && model.Speaker>0){info.ArgumentList.Add("--speaker");info.ArgumentList.Add(model.Speaker.ToString());}
        using var process=Launch(info);using var cancellation=linked.Token.Register(()=>{try{process.Kill(true);}catch{}});
        var errors=DrainErrors(process.StandardError,linked.Token);
        async Task<byte[]> Audio(){try{return await ReadAudio(process.StandardOutput.BaseStream,linked.Token);}catch{try{process.Kill(true);}catch{}throw;}}
        var output=Audio();byte[] pcm;
        try
        {
            await process.StandardInput.WriteLineAsync(text.Replace('\n',' ').Replace('\r',' ').AsMemory(),linked.Token);process.StandardInput.Close();
            await process.WaitForExitAsync(linked.Token);pcm=await output;await errors;
        }
        finally
        {
            try{if(!process.HasExited)process.Kill(true);}catch{}
            try{await Task.WhenAll(output,errors);}catch{} // Observe both readers even when writing/cancellation fails.
        }
        if(process.ExitCode!=0 || pcm.Length==0)throw new IOException("The installed Piper voice could not synthesize speech. Reinstall the selected voice or choose Windows voice.");
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync(assets.TtsPath(model)+".json",linked.Token));int sampleRate=config.RootElement.GetProperty("audio").GetProperty("sample_rate").GetInt32();
        if(sampleRate<8000 || sampleRate>48000)throw new IOException("The installed voice has an unsupported audio format.");
        using var wav=new MemoryStream();using(var writer=new WaveFileWriter(new KeepOpenStream(wav),new WaveFormat(sampleRate,16,1)))writer.Write(pcm,0,pcm.Length);return wav.ToArray();
    }
    private static async Task<byte[]> ReadAudio(Stream stream,CancellationToken token)
    {
        using var output=new MemoryStream();byte[] buffer=new byte[16384];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(output.Length+count>48000*2*120)throw new IOException("The spoken reply exceeds two minutes.");output.Write(buffer,0,count);}return output.ToArray();
    }
    private static async Task DrainErrors(StreamReader reader,CancellationToken token)
    {char[] buffer=new char[1024];while(await reader.ReadAsync(buffer,token)>0){}}
    private static ProcessStartInfo NativeInfo(string executable)=>new(executable){WorkingDirectory=Path.GetDirectoryName(executable)!,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
    private Process Launch(ProcessStartInfo info)
    {
        lifetime.Token.ThrowIfCancellationRequested();var process=Process.Start(info)??throw new IOException("Could not start the installed speech engine.");
        if(!AssignProcessToJobObject(job,process.Handle)){try{process.Kill(true);}catch{}process.Dispose();throw new IOException("Could not attach speech engine cleanup.");}return process;
    }
    private void StopServer(){var child=server;server=null;AsrUrl=null;if(child==null)return;try{if(!child.HasExited)child.Kill(true);}catch{}child.Dispose();lastError="";}
    public void Dispose(){if(disposed)return;disposed=true;lifetime.Cancel();ttsWorker?.Dispose();ttsWorker=null;job.Dispose();StopServer();lifetime.Dispose();}
    [StructLayout(LayoutKind.Sequential)]private struct BasicLimits{public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinWorkingSet,MaxWorkingSet;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]private struct IoCounters{public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]private struct JobLimits{public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetInformationJobObject(SafeFileHandle job,int infoClass,ref JobLimits info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    private sealed class KeepOpenStream(Stream inner) : Stream
    {
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>inner.CanWrite;public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override void Flush()=>inner.Flush();public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);public override void SetLength(long value)=>inner.SetLength(value);public override void Write(byte[] buffer,int offset,int count)=>inner.Write(buffer,offset,count);
    }
}
