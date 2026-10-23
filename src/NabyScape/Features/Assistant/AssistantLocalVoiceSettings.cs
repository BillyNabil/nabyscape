using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private FrameworkElement? voiceSetup;
    private Action<string>? selectVoicePage;
    private Action<DependencyObject>? revealVoiceTarget;
    private ComboBox? speechModePicker;
    private Button? speechEnable;
    private readonly TextBlock speechStatus=UI.Text("",11,UI.Muted);
    internal void OpenVoiceSetup()
    {
        if(voiceSetup==null)return;
        SelectSetupSection("Voice");connectionEditor.IsExpanded=false;selectVoicePage?.Invoke("Setup");
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>voiceSetup.BringIntoView(new Rect(0,0,Math.Max(1,voiceSetup.ActualWidth),40))));
    }
    private FrameworkElement LocalVoiceSettings()
    {
        var p=new StackPanel();var options=ai.Options.LocalVoice;
        var enabled=AssistantWindow.Pill(ai.Options.Enabled?"AI companion on":"Enable AI companion",()=>{});speechEnable=enabled;
        enabled.Click+=(_,_)=>{ai.Options.Enabled=!ai.Options.Enabled;SaveConfiguration();RefreshEnabled();enabled.Content=ai.Options.Enabled?"AI companion on":"Enable AI companion";};
        p.Children.Add(UI.Heading("How should Naby hear and speak?",20));
        var intro=UI.Text("Choose where your voice is processed. Your chat connection supplies the answers.",13,UI.Muted);intro.Margin=new(0,6,0,16);p.Children.Add(intro);
        var mode=new ComboBox{ItemsSource=new[]{"Online provider","On this PC","External server","My model files"},SelectedIndex=options.Enabled?(options.Managed?(options.OwnModels?3:1):2):0};speechModePicker=mode;
        var choices=new Grid{Margin=new(0,0,0,18)};choices.ColumnDefinitions.Add(new());choices.ColumnDefinitions.Add(new());
        var choiceButtons=new System.Collections.Generic.List<Button>();
        string[] titles={"Online provider","On this PC","External server","My model files"};
        string[] captions={"Use a realtime voice API","Install voices matched to your hardware","Connect an existing speech service","Import models you already have"};
        for(int i=0;i<4;i++)
        {
            if(i%2==0)choices.RowDefinitions.Add(new(){Height=GridLength.Auto});int index=i;
            var content=new StackPanel();content.Children.Add(UI.Heading(titles[i],15));var caption=UI.Text(captions[i],12,UI.Muted);caption.Margin=new(0,5,0,0);content.Children.Add(caption);
            var button=UI.Button(titles[i],()=>mode.SelectedIndex=index);button.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding("IsEnabled"){Source=mode});button.Content=content;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new(16);button.MinHeight=84;button.Margin=new(0,0,i%2==0?10:0,10);button.Tag=new CornerRadius(10);choiceButtons.Add(button);Grid.SetRow(button,i/2);Grid.SetColumn(button,i%2);choices.Children.Add(button);
        }
        UI.Named(choices,"Voice processing location");p.Children.Add(choices);
        var realtimeHelp=new StackPanel();realtimeHelp.Children.Add(UI.Heading("Online voice",17));Label(realtimeHelp,"Voice provider",voiceProvider);
        realtimeHelp.Children.Add(UI.Text("Uses the realtime model and voice saved for this provider. API usage may be billed by your provider.",12,UI.Muted));
        realtimeHelp.Children.Add(AssistantWindow.Pill("Configure provider",()=>{SelectSetupSection("Chat");connectionEditor.IsExpanded=true;}));p.Children.Add(realtimeHelp);
        var automatic=AutomaticSpeechSettings(mode);p.Children.Add(automatic);var own=OwnSpeechSettings(mode);p.Children.Add(own);
        var local=new StackPanel();
        void SpeechMode(){for(int i=0;i<choiceButtons.Count;i++){bool selected=i==mode.SelectedIndex;choiceButtons[i].SetResourceReference(Control.BackgroundProperty,selected?"SelectionFill":"ControlFill");if(choiceButtons[i].Content is StackPanel card && card.Children[0] is TextBlock heading)heading.Text=titles[i]+(selected?" · Selected":"");System.Windows.Automation.AutomationProperties.SetHelpText(choiceButtons[i],selected?"Selected voice source":"Choose this voice source");}own.Visibility=mode.SelectedIndex==3?Visibility.Visible:Visibility.Collapsed;local.Visibility=mode.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;automatic.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;realtimeHelp.Visibility=mode.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;}
        mode.SelectionChanged+=(_,_)=>SpeechMode();SpeechMode();
        var tier=new ComboBox{ItemsSource=new[]{"Light · CPU","Balanced · CPU / GPU","Quality · about 8 GB VRAM"},SelectedItem=options.Tier};Label(local,"Hardware template",tier);
        var info=UI.Text("Templates are starting points, not hardware guarantees. Speech services run separately and use additional RAM/VRAM. A local chat model shares that budget.",10,UI.Muted);local.Children.Add(info);
        var advanced=new StackPanel();
        var asrProtocol=new ComboBox{ItemsSource=new[]{"WhisperCpp","OpenAI-compatible"},SelectedItem=options.AsrProtocol};Label(advanced,"ASR server type",asrProtocol);
        var asrUrl=UI.Input("Local ASR URL",options.AsrUrl);Label(advanced,"ASR base URL · localhost",asrUrl);
        var asrModel=new ComboBox{IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=new[]{"tiny","base","small","medium","large-v3-turbo"},Text=options.AsrModel};Label(local,"Listening model · choose or paste an ASR model ID",asrModel);UI.Named(asrModel,"Local ASR model");
        local.Children.Add(UI.Text("Suggested model names; this list does not mean they are installed. The model must match the one loaded by your local server.",10,UI.Muted));
        var tts=new ComboBox{ItemsSource=new[]{"Windows voice","Kokoro / compatible","Piper / compatible"},SelectedItem=options.TtsEngine};Label(local,"Text-to-speech engine",tts);
        var ttsUrl=UI.Input("Local TTS URL",options.TtsUrl);Label(advanced,"TTS base URL · localhost",ttsUrl);
        var speechModels=new StackPanel();local.Children.Add(speechModels);
        var ttsModel=new ComboBox{IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=new[]{"kokoro"},Text=options.TtsModel};Label(speechModels,"Speaking model · paste the ID from your TTS server",ttsModel);UI.Named(ttsModel,"Local TTS model");
        var voice=new ComboBox{IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true,ItemsSource=new[]{"af_heart","af_bella","am_adam","bf_emma","bm_george"},Text=options.Voice};Label(speechModels,"Voice · choose or paste a voice ID",voice);UI.Named(voice,"Local TTS voice");
        speechModels.Children.Add(UI.Text("Voice suggestions are for Kokoro. For Piper or other servers, paste that server’s model and voice IDs.",10,UI.Muted));
        var windowsHelp=UI.Text("Uses your default installed Windows SAPI voice. No TTS server is needed. Voice and language availability depends on Windows.",10,UI.Muted);local.Children.Add(windowsHelp);
        var silence=new Slider{Minimum=400,Maximum=2000,Value=Math.Clamp(options.SilenceMs,400,2000),TickFrequency=100,IsSnapToTickEnabled=true};Label(advanced,"Pause before sending · milliseconds",silence);
        var sensitivity=new Slider{Minimum=.003,Maximum=.1,Value=Math.Clamp(options.Sensitivity,.003,.1)};Label(advanced,"Speech threshold · lower for a quiet microphone",sensitivity);
        void TtsFields(){bool enabled=tts.SelectedIndex!=0;ttsUrl.IsEnabled=enabled;speechModels.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;windowsHelp.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;}
        tts.SelectionChanged+=(_,_)=>TtsFields();TtsFields();
        tier.SelectionChanged+=(_,_)=>
        {
            if(tier.SelectedIndex==0){asrProtocol.SelectedItem="WhisperCpp";asrUrl.Text="http://127.0.0.1:8080/";asrModel.Text="tiny";tts.SelectedItem="Windows voice";}
            else{asrProtocol.SelectedItem="OpenAI-compatible";asrUrl.Text="http://127.0.0.1:8178/v1/";asrModel.Text=tier.SelectedIndex==1?"small":"large-v3-turbo";tts.SelectedItem="Kokoro / compatible";ttsUrl.Text="http://127.0.0.1:8880/v1/";ttsModel.Text="kokoro";voice.Text="af_heart";}
        };
        local.Children.Add(new Expander{Header="Server addresses & microphone tuning",Content=advanced,Margin=new(0,12,0,10)});
        local.Children.Add(UI.Text("2 · Start your local speech service",14));
        var setupHelp=UI.Text("",11,UI.Muted);setupHelp.Margin=new(0,7,0,8);local.Children.Add(setupHelp);
        void SetupHelp()
        {
            string modelId=asrModel.Text.Trim();
            setupHelp.Text=asrProtocol.SelectedItem as string=="WhisperCpp"
                ?"Install whisper.cpp and download the matching multilingual model. Run whisper-server with that model on port 8080. Choosing a model here does not change the model loaded by whisper.cpp. For the exportable Python option, change ASR server type to OpenAI-compatible and URL to http://127.0.0.1:8178/v1/."
                :"Save the ASR server template below. In your Python environment, run:\npython -m pip install faster-whisper fastapi python-multipart uvicorn\npython naby-local-asr.py --model "+modelId+" --device cpu --compute-type int8\nThe model downloads on first use. A compatible CUDA setup can use --device cuda --compute-type int8_float16. Restart the server when changing its model.";
            if(tts.SelectedIndex!=0)setupHelp.Text+="\nStart your TTS server separately. It must provide /audio/speech with WAV output; a standalone model file or Piper executable is not a server.";
        }
        asrModel.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>SetupHelp()));asrProtocol.SelectionChanged+=(_,_)=>SetupHelp();tts.SelectionChanged+=(_,_)=>SetupHelp();SetupHelp();
        local.Children.Add(AssistantWindow.Pill("Save local ASR server template…",SaveLocalServer));p.Children.Add(local);
        local.Children.Add(UI.Text("3 · Check services, then save",14));
        local.Children.Add(AssistantWindow.Pill("Check local speech services",async()=>
        {
            try
            {
                speechStatus.Text="Checking local speech services…";
                using var api=new HttpClient(AssistantTransport.CreateHandler()){Timeout=TimeSpan.FromSeconds(10)};
                var asr=LocalAssistantVoice.Endpoint(asrUrl.Text.Trim());
                using var response=await api.GetAsync(new Uri(asr,asrProtocol.SelectedItem as string=="WhisperCpp"?"":"models"),HttpCompletionOption.ResponseHeadersRead);
                if(!response.IsSuccessStatusCode)throw new IOException("ASR: "+await AssistantTransport.Failure(response,null,CancellationToken.None));
                if(tts.SelectedIndex!=0)
                {
                    using var speech=await api.GetAsync(new Uri(LocalAssistantVoice.Endpoint(ttsUrl.Text.Trim()),"models"),HttpCompletionOption.ResponseHeadersRead);
                    if(!speech.IsSuccessStatusCode)throw new IOException("TTS: "+await AssistantTransport.Failure(speech,null,CancellationToken.None));
                }
                else if(Type.GetTypeFromProgID("SAPI.SpVoice")==null)throw new IOException("Install a Windows speech voice, or choose a local TTS service.");
                speechStatus.Text="Speech services are reachable. No microphone or speech synthesis was started. Save voice setup, return to Voice mode and choose Start conversation.";
                app.Notify("Naby local voice","Speech services are reachable. Models and inference still depend on your server setup.");
            }
            catch(Exception ex){speechStatus.Text="Local speech connection failed: "+(ex is HttpRequestException?"Start the local server and check its port.":ex.Message);app.Notify("Naby local voice",speechStatus.Text);}
        }));
        var saveVoice=AssistantWindow.Pill("Save voice setup",()=>
        {
            if(mode.SelectedIndex==1){saveManagedPack?.Invoke();return;}if(mode.SelectedIndex==3){_=saveOwnModels?.Invoke();return;}
            try
            {
                var current=ai.Options.LocalVoice;var next=new AssistantLocalVoiceOptions{Enabled=mode.SelectedIndex==2,Managed=false,OwnModels=false,OwnAsrEngine=current.OwnAsrEngine,OwnAsrPath=current.OwnAsrPath,OwnTtsEngine=current.OwnTtsEngine,OwnTtsPath=current.OwnTtsPath,OwnSpeaker=current.OwnSpeaker,ManagedAsr=current.ManagedAsr,ManagedTts=current.ManagedTts,ManagedGpu=current.ManagedGpu,SpeechPreference=current.SpeechPreference,SpeechLanguage=current.SpeechLanguage,Tier=tier.SelectedItem as string??"Light · CPU",AsrProtocol=asrProtocol.SelectedItem as string??"WhisperCpp",AsrUrl=asrUrl.Text.Trim(),AsrModel=asrModel.Text.Trim(),TtsEngine=tts.SelectedItem as string??"Windows voice",TtsUrl=ttsUrl.Text.Trim(),TtsModel=ttsModel.Text.Trim(),Voice=voice.Text.Trim(),SilenceMs=(int)silence.Value,Sensitivity=sensitivity.Value};
                next.StreamReplies=current.StreamReplies;
                if(next.Enabled){LocalAssistantVoice.Endpoint(next.AsrUrl);if(next.TtsEngine!="Windows voice")LocalAssistantVoice.Endpoint(next.TtsUrl);if(next.AsrModel.Length>160 || next.TtsModel.Length>160 || next.Voice.Length>160)throw new InvalidOperationException("Model and voice IDs must be at most 160 characters.");}
                ai.Options.LocalVoice=next;if(!SaveConfiguration("Voice setup")){speechStatus.Text=status.Text;return;}speechStatus.Text="Voice setup saved. Open Voice mode and choose Start conversation.";
            }
            catch(Exception ex){speechStatus.Text=ex.Message;}
        },true);p.Children.Add(saveVoice);void SaveVisibility()=>saveVoice.Visibility=mode.SelectedIndex==1?Visibility.Collapsed:Visibility.Visible;mode.SelectionChanged+=(_,_)=>SaveVisibility();SaveVisibility();
        speechStatus.Margin=new(0,8,0,10);p.Children.Add(speechStatus);
        var streaming=new CheckBox{Content="Stream replies for faster speech",IsChecked=options.StreamReplies,Margin=new(0,6,0,8)};
        streaming.ToolTip="Uses streamed text when your chat endpoint supports it. Saving ends the current voice session.";
        var tuning=new StackPanel();tuning.Children.Add(streaming);tuning.Children.Add(AssistantWindow.Pill("Save streaming",()=>{try{ai.SaveVoiceStreaming(streaming.IsChecked==true);Feedback("Streaming replies saved · "+(streaming.IsChecked==true?"On":"Off")+".");}catch(Exception ex){speechStatus.Text=ex.Message;}}));
        p.Children.Add(new Expander{Header="Streaming preference",Content=tuning,Margin=new(0,12,0,8)});
        voiceSetup=p;return p;
    }
    private void SaveLocalServer()
    {
        try
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save optional local ASR server template",FileName="naby-local-asr.py",Filter="Python file (*.py)|*.py",AddExtension=true,DefaultExt=".py"};
            if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
            using var stream=typeof(App).Assembly.GetManifestResourceStream("NabyScape.LocalAsrServer")??throw new IOException("The setup template is unavailable.");using var reader=new StreamReader(stream);File.WriteAllText(dialog.FileName,reader.ReadToEnd());
            speechStatus.Text="Template saved. Follow the commands above using the filename you saved. It binds to 127.0.0.1 only; models download on first use.";
        }
        catch(Exception ex){speechStatus.Text=ex.Message;}
    }
}
