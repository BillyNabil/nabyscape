using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace NabyScape;

internal sealed class AssistantLocalVoiceOptions
{
    public bool Enabled {get;set;}
    public bool Managed {get;set;}
    public bool OwnModels {get;set;}
    public string OwnAsrEngine {get;set;}="Whisper GGML file";
    public string OwnAsrPath {get;set;}="";
    public string OwnTtsEngine {get;set;}="Windows voice";
    public string OwnTtsPath {get;set;}="";
    public int OwnSpeaker {get;set;}
    public string ManagedAsr {get;set;}="base";
    public string ManagedTts {get;set;}="windows";
    public bool ManagedGpu {get;set;}
    public string SpeechPreference {get;set;}="Balanced";
    public string SpeechLanguage {get;set;}="en";
    public string Tier {get;set;}="Light · CPU";
    public string AsrProtocol {get;set;}="WhisperCpp";
    public string AsrUrl {get;set;}="http://127.0.0.1:8080/";
    public string AsrModel {get;set;}="tiny";
    public string TtsEngine {get;set;}="Windows voice";
    public string TtsUrl {get;set;}="http://127.0.0.1:8880/v1/";
    public string TtsModel {get;set;}="kokoro";
    public string Voice {get;set;}="af_heart";
    public double Sensitivity {get;set;}=.018;
    public int SilenceMs {get;set;}=800;
    public bool StreamReplies {get;set;}=true;
}

// Speech is either an installed native pack owned by this session, or an external local service.
internal sealed partial class LocalAssistantVoice : IAssistantVoice
{
    private readonly AssistantLocalVoiceOptions options;
    private readonly Func<string,Func<string,CancellationToken,Task>,Action<string>,CancellationToken,Task<string>> reply;
    private readonly object captureGate=new();
    private readonly MemoryStream utterance=new();
    private readonly HttpClient client=new(AssistantTransport.CreateHandler()){Timeout=Timeout.InfiniteTimeSpan};
    private AssistantVoiceAudio? audio;
    private readonly AssistantSpeechInstall? assets;
    private AssistantManagedSpeech? managed;
    private CancellationTokenSource? lifetime;
    private CancellationToken sessionToken;
    private Task activeTurn=Task.CompletedTask;
    private bool heard;
    private int quiet,voiced,processing,stopped,disposed;
    private volatile bool connected;
    public bool Connected=>connected;
    internal bool Processing=>Volatile.Read(ref processing)!=0;
    internal event Action<AssistantState>? Phase;
    internal event Action<string>? Caption;
    public event Action<string>? Status;
    public event Action<float,bool>? Level;
    public event Action<string,string>? Transcript {add{}remove{}}
    public event Action<AssistantCall>? ToolCall {add{}remove{}}
    internal LocalAssistantVoice(AssistantLocalVoiceOptions options,Func<string,Func<string,CancellationToken,Task>,Action<string>,CancellationToken,Task<string>> reply,AssistantSpeechInstall? assets=null)
    {this.options=JsonSerializer.Deserialize<AssistantLocalVoiceOptions>(JsonSerializer.Serialize(options))!;this.reply=reply;this.assets=assets;}
    internal static Uri Endpoint(string value)
    {
        var uri=AssistantTransport.ValidateEndpoint(value);
        if(!uri.IsLoopback)throw new InvalidOperationException("Local voice URLs must use localhost or a loopback IP. Chat can use your separately configured provider.");
        return uri;
    }
    public async Task Start(AssistantProvider provider,string? key,string instructions,IReadOnlyList<AssistantTool> tools,IReadOnlyList<AssistantMessage> history,CancellationToken token)
    {
        lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);sessionToken=lifetime.Token;
        if(options.Managed)
        {
            managed=new AssistantManagedSpeech(assets??throw new IOException("Managed speech is unavailable."),options);await managed.Start(message=>Status?.Invoke(message),sessionToken);sessionToken.ThrowIfCancellationRequested();if(managed.AsrUrl!=null){options.AsrProtocol="WhisperCpp";options.AsrUrl=managed.AsrUrl.AbsoluteUri;}
        }
        else{Endpoint(options.AsrUrl);if(options.TtsEngine!="Windows voice")Endpoint(options.TtsUrl);}
        sessionToken.ThrowIfCancellationRequested();audio=new();connected=true;
        try{audio.Start(16000,Capture,(peak,speaking)=>{if(connected && Processing)Level?.Invoke(peak,speaking);},()=>Fail("The local voice audio device stopped. End voice and check Windows sound settings."));}
        catch{connected=false;audio.Dispose();audio=null;throw;}
        Phase?.Invoke(AssistantState.Listening);Status?.Invoke("Listening locally · speak, then pause. ASR/TTS stay on this PC; chat uses the selected chat provider.");
    }
    private void Capture(byte[] data)
    {
        if(!connected || Processing)return;float peak=AssistantVoiceAudio.Peak(data,data.Length);Level?.Invoke(peak,false);
        byte[]? turn=null;
        lock(captureGate)
        {
            if(!connected || Processing)return;
            if(peak>=Math.Clamp(options.Sensitivity,.003,.15)){heard=true;quiet=0;voiced++;}
            else if(heard)quiet+=data.Length*1000/32000;
            if(!heard)return;
            utterance.Write(data);
            if(quiet>=Math.Clamp(options.SilenceMs,400,2000) || utterance.Length>=32000*25)
            {
                if(voiced>=3 && Interlocked.CompareExchange(ref processing,1,0)==0)turn=utterance.ToArray();
                utterance.SetLength(0);heard=false;quiet=voiced=0;
                if(turn!=null)activeTurn=Task.Run(()=>Recognize(turn));
            }
        }
    }
    private async Task Recognize(byte[] pcm)
    {
        var token=sessionToken;bool failed=false;
        try
        {
            Phase?.Invoke(AssistantState.Thinking);Status?.Invoke("Transcribing speech…");var recognitionClock=System.Diagnostics.Stopwatch.StartNew();
            using var wav=new MemoryStream();using(var writer=new WaveFileWriter(new IgnoreDisposeStream(wav),new WaveFormat(16000,16,1)))writer.Write(pcm,0,pcm.Length);
            string text;
            if(options.Managed && AssistantOwnSpeech.Asr(options).Native)text=await managed!.Recognize(wav.ToArray(),token);
            else
            {
            using var form=new MultipartFormDataContent();var file=new ByteArrayContent(wav.ToArray());file.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(file,"file","voice.wav");form.Add(new StringContent("json"),"response_format");
            bool cpp=options.AsrProtocol=="WhisperCpp";
            if(cpp)form.Add(new StringContent("auto"),"language");else form.Add(new StringContent(options.AsrModel),"model");
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(Endpoint(options.AsrUrl),cpp?"inference":"audio/transcriptions")){Content=form};
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(2));
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode)throw new IOException("Local ASR: "+await AssistantTransport.Failure(response,null,deadline.Token));
            using var json=JsonDocument.Parse(await AssistantTransport.ReadBounded(response,65536,deadline.Token));
            text=json.RootElement.TryGetProperty("text",out var item)?item.GetString()?.Trim()??"":"";
            }
            if(text.Length==0){Status?.Invoke("No speech recognized. Try again or adjust microphone sensitivity.");return;}
            await Turn(text[..Math.Min(8000,text.Length)],token,recognitionClock.ElapsedMilliseconds);
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested && connected){failed=true;Fail("Speech recognition timed out. Choose a lighter listening model or check your local service, then start voice again.");}
        catch(OperationCanceledException){}
        catch(Exception ex){failed=true;if(connected)Fail("Voice stopped. "+(ex is HttpRequestException?"Local speech service is unavailable. Check its port in Voice settings, then start voice again.":ex.Message));}
        finally{Interlocked.Exchange(ref processing,0);if(connected){Level?.Invoke(0,false);Phase?.Invoke(failed?AssistantState.Error:AssistantState.Listening);}}
    }
    private async Task<byte[]> Synthesize(string spoken,CancellationToken cancellationToken)
    {
        using var generationDeadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);generationDeadline.CancelAfter(TimeSpan.FromMinutes(2));var token=generationDeadline.Token;
        try
        {
            byte[] pcm;
            if(options.Managed && AssistantOwnSpeech.Tts(options).Id!="windows")pcm=ConvertWav(await managed!.Speak(spoken,token));
            else if(options.TtsEngine=="Windows voice" || options.Managed)pcm=await WindowsSpeech(spoken,token);
            else
            {
                var body=new{model=options.TtsModel,input=spoken,voice=options.Voice,response_format="wav",stream=false};
                using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(Endpoint(options.TtsUrl),"audio/speech")){Content=new StringContent(JsonSerializer.Serialize(body),System.Text.Encoding.UTF8,"application/json")};
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(2));
                using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                if(!response.IsSuccessStatusCode)throw new IOException("Local TTS: "+await AssistantTransport.Failure(response,null,deadline.Token));
                byte[] wav=await AssistantTransport.ReadBounded(response,12*1024*1024,deadline.Token);pcm=ConvertWav(wav);
            }
            return pcm;
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new IOException("Speech generation timed out. Choose a lighter voice or start voice again.");}
    }
    private static byte[] ConvertWav(byte[] wav)
    {
        using var stream=new MemoryStream(wav);using var reader=new WaveFileReader(stream);ISampleProvider samples=reader.ToSampleProvider();
        if(samples.WaveFormat.Channels==2)samples=new StereoToMonoSampleProvider(samples);if(samples.WaveFormat.Channels!=1)throw new IOException("TTS must return mono or stereo WAV audio.");
        var pcm=new SampleToWaveProvider16(new WdlResamplingSampleProvider(samples,24000));using var output=new MemoryStream();byte[] chunk=new byte[8192];int count;
        while((count=pcm.Read(chunk,0,chunk.Length))>0){if(output.Length+count>24000*2*120)throw new IOException("TTS reply exceeds two minutes.");output.Write(chunk,0,count);}return output.ToArray();
    }
    private static Task<byte[]> WindowsSpeech(string text,CancellationToken token)
    {
        var done=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            object? voice=null,stream=null,format=null;
            try
            {
                token.ThrowIfCancellationRequested();voice=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")??throw new IOException("No Windows speech engine is installed."));stream=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpMemoryStream")!);format=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpAudioFormat")!);
                dynamic v=voice!,s=stream!,f=format!;f.Type=26;s.Format=f;v.AudioOutputStream=s;v.Speak(text,1);
                while(!v.WaitUntilDone(40)){if(token.IsCancellationRequested){v.Speak("",3);token.ThrowIfCancellationRequested();}}
                done.TrySetResult((byte[])s.GetData());
            }
            catch(Exception ex){done.TrySetException(ex);}
            finally{foreach(var item in new[]{voice,stream,format})if(item!=null && Marshal.IsComObject(item))Marshal.FinalReleaseComObject(item);}
        }){IsBackground=true,Name="Naby local speech"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task;
    }
    public async Task SendText(string text,CancellationToken token)
    {
        if(Interlocked.CompareExchange(ref processing,1,0)!=0)throw new InvalidOperationException("Wait for the current spoken reply.");
        try
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,sessionToken);Task pending;
            lock(captureGate)
            {
                linked.Token.ThrowIfCancellationRequested();if(!connected)throw new OperationCanceledException(linked.Token);
                utterance.SetLength(0);heard=false;quiet=voiced=0;pending=activeTurn=Task.Run(()=>Turn(text,linked.Token),linked.Token);
            }
            await pending;
        }
        finally{Interlocked.Exchange(ref processing,0);if(connected)Phase?.Invoke(AssistantState.Listening);}
    }
    private void Fail(string message){connected=false;Phase?.Invoke(AssistantState.Error);Status?.Invoke(message);_ = Stop();}
    public CancellationToken CallToken(string id)=>lifetime?.Token??CancellationToken.None;
    public Task SendImage(string data,CancellationToken token)=>Task.CompletedTask;
    public Task SendImage(Func<CancellationToken,Task<string?>> capture,CancellationToken token)=>Task.CompletedTask;
    public Task SendToolResult(AssistantCall call,string result,CancellationToken token,Func<CancellationToken,Task<string?>>? image=null)=>Task.CompletedTask;
    public Task Stop()
    {
        if(Interlocked.Exchange(ref stopped,1)!=0)return Task.CompletedTask;
        connected=false;lifetime?.Cancel();lock(captureGate){utterance.SetLength(0);heard=false;quiet=voiced=0;}
        try{audio?.Dispose();}finally{audio=null;managed?.Dispose();}return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        try
        {
            await Stop();Task pending;lock(captureGate)pending=activeTurn;
            try{await pending.WaitAsync(TimeSpan.FromSeconds(8));}catch{/* Cancellation and engine shutdown already stop the turn. */}
        }
        finally{client.Dispose();lifetime?.Dispose();lock(captureGate)utterance.Dispose();}
    }
    private sealed class IgnoreDisposeStream(Stream inner) : Stream
    {
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>inner.CanWrite;public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override void Flush()=>inner.Flush();public override int Read(byte[] b,int o,int c)=>inner.Read(b,o,c);public override long Seek(long o,SeekOrigin s)=>inner.Seek(o,s);public override void SetLength(long v)=>inner.SetLength(v);public override void Write(byte[] b,int o,int c)=>inner.Write(b,o,c);
    }
}
