using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private Action? testSelectedMicrophone;
    private FrameworkElement SpeechChecks()
    {
        var body=new StackPanel();body.Children.Add(UI.Text("Tests use your saved setup. Microphone tests record four seconds on this PC; transcription is sent only to your configured speech service, never to the chat model. Play sample speaks a fixed sentence. Nothing is added to chat or saved as an audio file.",11,UI.Muted));
        var feedback=UI.Text("Not tested on this visit.",11,UI.Muted);Accessibility.Live(feedback);var actions=new WrapPanel();CancellationTokenSource? pending=null;
        async Task Run(string kind)
        {
            if(pending!=null || ai.SpeechCheckBusy || ai.Speech.Busy || ai.VoiceConnected || ai.Busy){feedback.Text="End the current conversation and finish model setup before testing.";return;}
            if(kind!="microphone" && !ai.Options.LocalVoice.Enabled){feedback.Text="Realtime voice is tested by Start conversation. These individual tests need a saved local or external ASR/TTS setup.";return;}
            pending=new();pending.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                ai.BeginSpeechCheck(()=>pending?.Cancel());
                await ai.WaitForMicrophoneRelease();pending.Token.ThrowIfCancellationRequested();
                foreach(Button button in actions.Children)button.IsEnabled=button.Content?.ToString()=="Cancel test";
                var options=JsonSerializer.Deserialize<AssistantLocalVoiceOptions>(JsonSerializer.Serialize(ai.Options.LocalVoice))!;
                await using var test=new LocalAssistantVoice(options,(_,_,_,_)=>throw new InvalidOperationException("Speech checks cannot send chat."),ai.Speech,ai.Options.MicrophoneId);
                feedback.Text=kind=="sample"?"Preparing the saved speaking voice…":"Microphone on for four seconds. Say a short sentence.";
                feedback.Text=await test.Check(kind,message=>Dispatcher.BeginInvoke(()=>{if(pending is {IsCancellationRequested:false})feedback.Text=message;}),pending.Token);Feedback(feedback.Text);
            }
            catch(Exception ex){feedback.Text=ex is OperationCanceledException?"Test cancelled or timed out. Microphone and test engines are closed.":"Test needs attention · "+ex.Message;Feedback(feedback.Text,true);}
            finally{ai.EndSpeechCheck();pending.Dispose();pending=null;foreach(Button button in actions.Children)button.IsEnabled=true;}
        }
        foreach(var item in new[]{("Test microphone","microphone"),("Test transcription","transcription"),("Play sample","sample")}){string kind=item.Item2;actions.Children.Add(AssistantWindow.Pill(item.Item1,()=>_=Run(kind)));}
        actions.Children.Add(AssistantWindow.Pill("Cancel test",()=>pending?.Cancel()));body.Children.Add(actions);body.Children.Add(feedback);
        testSelectedMicrophone=()=>_=Run("microphone");
        body.Unloaded+=(_,_)=>pending?.Cancel();body.IsVisibleChanged+=(_,_)=>{if(!body.IsVisible)pending?.Cancel();};var expander=new Expander{Header="Test microphone & speech",Content=body,Margin=new(0,12,0,8)};UI.Named(expander,"Speech checks");return expander;
    }
}
internal sealed partial class LocalAssistantVoice
{
    internal async Task<string> Check(string kind,Action<string> status,CancellationToken token)
    {
        if(kind=="sample")
        {
            await PrepareCheck(false,status,token);status("Playing sample · your microphone is off.");byte[] pcm=await Synthesize("Hello. This is Naby. Your speaking voice is ready.",token);
            using var stream=new RawSourceWaveStream(new MemoryStream(pcm),new WaveFormat(24000,16,1));using var output=new WaveOutEvent();output.Init(stream);output.Play();
            try{while(output.PlaybackState==PlaybackState.Playing)await Task.Delay(40,token);}finally{output.Stop();}
            return "Speaking sample finished. If you heard it, your selected voice and speaker work. Listening was not tested.";
        }
        using var captured=new MemoryStream();var gate=new object();float peak=0;Exception? captureError=null;
        using(var mic=new WaveInEvent{DeviceNumber=AssistantMicrophones.Resolve(microphoneId),WaveFormat=new WaveFormat(16000,16,1),BufferMilliseconds=50})
        {
            mic.DataAvailable+=(_,e)=>{lock(gate){captured.Write(e.Buffer,0,e.BytesRecorded);peak=Math.Max(peak,AssistantVoiceAudio.Peak(e.Buffer,e.BytesRecorded));status("Microphone on · input level "+Math.Round(AssistantVoiceAudio.Peak(e.Buffer,e.BytesRecorded)*100)+"% · testing for four seconds…");}};mic.RecordingStopped+=(_,e)=>captureError=e.Exception;
            mic.StartRecording();try{await Task.Delay(4000,token);}finally{mic.StopRecording();}
        }
        if(captureError!=null)throw new IOException("The microphone stopped. Check Windows microphone permissions.",captureError);
        if(captured.Length==0 || peak<.003)return "Microphone opened, but no audible input was detected. Check your input device and volume, then retry.";
        if(kind=="microphone")return "Microphone checked · audio detected. Transcription and speaking were not tested. Microphone is now off.";
        status("Microphone off · testing the saved listening model…");await PrepareCheck(true,status,token);
        using var wav=new MemoryStream();using(var writer=new WaveFileWriter(new IgnoreDisposeStream(wav),new WaveFormat(16000,16,1))){byte[] pcm=captured.ToArray();writer.Write(pcm,0,pcm.Length);}
        string text;
        if(options.Managed && AssistantOwnSpeech.Asr(options).Native)text=await managed!.Recognize(wav.ToArray(),token);
        else
        {
            using var form=new MultipartFormDataContent();var audio=new ByteArrayContent(wav.ToArray());audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(audio,"file","sample.wav");form.Add(new StringContent("json"),"response_format");bool cpp=options.AsrProtocol=="WhisperCpp";form.Add(new StringContent(cpp?"auto":options.AsrModel),cpp?"language":"model");
            using var response=await client.PostAsync(new Uri(Endpoint(options.AsrUrl),cpp?"inference":"audio/transcriptions"),form,token);if(!response.IsSuccessStatusCode)throw new IOException("Listening check: "+await AssistantTransport.Failure(response,null,token));
            using var json=JsonDocument.Parse(await AssistantTransport.ReadBounded(response,65536,token));text=json.RootElement.TryGetProperty("text",out var value)?value.GetString()??"":"";
        }
        return string.IsNullOrWhiteSpace(text)?"Listening model replied but recognized no words. Try speaking clearly or choose another model.":"Transcription checked: “"+text[..Math.Min(240,text.Length)].Trim()+"”. This sample was not sent to chat.";
    }
    private async Task PrepareCheck(bool listening,Action<string> status,CancellationToken token)
    {
        if(!options.Managed || !listening && AssistantOwnSpeech.Tts(options).Id=="windows")return;managed=new AssistantManagedSpeech(assets!,options);await managed.StartCheck(listening,status,token);if(managed.AsrUrl!=null){options.AsrProtocol="WhisperCpp";options.AsrUrl=managed.AsrUrl.AbsoluteUri;}
    }
}
