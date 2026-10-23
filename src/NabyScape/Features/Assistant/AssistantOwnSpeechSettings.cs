using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private Func<Task>? saveOwnModels;
    private FrameworkElement OwnSpeechSettings(ComboBox mode)
    {
        var original=ai.Options.LocalVoice;var service=ai.Speech;var p=new StackPanel();
        p.Children.Add(UI.Heading("Use your own model files",16));
        p.Children.Add(UI.Text("Choose models already on your PC. Files stay in their current location; Naby saves their paths. Only missing speech engines may need downloading.",11,UI.Muted));
        var asr=new ComboBox{ItemsSource=AssistantOwnSpeech.AsrEngines,SelectedItem=original.OwnAsrEngine};if(asr.SelectedIndex<0)asr.SelectedIndex=1;Label(p,"Listening model format",asr);
        var installedAsr=new ComboBox{ItemsSource=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(SpeechCatalog.Asr,service.HasAsrWeights))};var asrInstalledField=new StackPanel();Label(asrInstalledField,"Installed listening pack",installedAsr);p.Children.Add(asrInstalledField);
        installedAsr.SelectedItem=System.Linq.Enumerable.FirstOrDefault(installedAsr.Items.Cast<SpeechAsrModel>(),a=>a.Id==original.ManagedAsr);if(installedAsr.SelectedIndex<0 && installedAsr.Items.Count>0)installedAsr.SelectedIndex=0;
        var asrPath=UI.Input("Select a listening model",original.OwnAsrPath);var asrRow=PathRow(asrPath,()=>asr.SelectedItem as string=="Whisper GGML file","Whisper GGML (*.bin)|*.bin");var asrFileField=new StackPanel();Label(asrFileField,"Listening model file or folder",asrRow);p.Children.Add(asrFileField);
        var tts=new ComboBox{ItemsSource=AssistantOwnSpeech.TtsEngines,SelectedItem=original.OwnTtsEngine};if(tts.SelectedIndex<0)tts.SelectedIndex=1;Label(p,"Speaking model format",tts);
        var installedTts=new ComboBox{ItemsSource=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(SpeechCatalog.Tts,service.HasTtsWeights))};var ttsInstalledField=new StackPanel();Label(ttsInstalledField,"Installed speaking pack",installedTts);p.Children.Add(ttsInstalledField);
        installedTts.SelectedItem=System.Linq.Enumerable.FirstOrDefault(installedTts.Items.Cast<SpeechTtsModel>(),t=>t.Id==original.ManagedTts);if(installedTts.SelectedIndex<0 && installedTts.Items.Count>0)installedTts.SelectedIndex=0;
        var ttsPath=UI.Input("Select a speaking model",original.OwnTtsPath);var ttsRow=PathRow(ttsPath,()=>tts.SelectedItem as string=="Piper ONNX file","Piper model (*.onnx)|*.onnx");var ttsFileField=new StackPanel();Label(ttsFileField,"Speaking model file or folder",ttsRow);p.Children.Add(ttsFileField);
        string[] codes={"en","es","fr","de","pt","it","ja","ko","ru","ar","nl","tr","hi","vi","id"};var language=new ComboBox{ItemsSource=new[]{"English","Spanish","French","German","Portuguese","Italian","Japanese","Korean","Russian","Arabic","Dutch","Turkish","Hindi","Vietnamese","Indonesian"},SelectedIndex=Math.Max(0,Array.IndexOf(codes,original.SpeechLanguage))};Label(p,"Conversation language · must match your models",language);
        var speaker=UI.Input("Speaker ID",original.OwnSpeaker.ToString());speaker.MaxLength=3;Label(p,"Speaker ID · 0 for the default voice",speaker);
        var gpu=new CheckBox{Content=UI.Text("Use NVIDIA GPU for Whisper when available · CPU fallback included",11),IsChecked=original.ManagedGpu,Margin=new(0,12,0,8)};p.Children.Add(gpu);
        var help=UI.Text("",10,UI.Muted);help.Margin=new(0,10,0,8);p.Children.Add(new Expander{Header="Supported file layouts",Content=help,IsExpanded=true});
        p.Children.Add(UI.Text("An arbitrary ONNX, GGUF or safetensors file cannot run with every engine. Choose the matching exported format and support files. Model memory use and accuracy cannot be confirmed from the folder alone. For another runtime, use External ASR + TTS.",10,UI.Muted));
        var result=UI.Text("No models selected yet.",11,UI.Muted);result.Margin=new(0,10,0,8);p.Children.Add(result);var buttons=new WrapPanel();var check=AssistantWindow.Pill("Check files",()=>{});var prepare=AssistantWindow.Pill("Prepare engines & use models",()=>{},true);var cancel=AssistantWindow.Pill("Cancel setup",service.Cancel);buttons.Children.Add(check);buttons.Children.Add(prepare);buttons.Children.Add(cancel);p.Children.Add(buttons);
        bool working=false,subscribed=false;
        AssistantLocalVoiceOptions Read()
        {
            var next=JsonSerializer.Deserialize<AssistantLocalVoiceOptions>(JsonSerializer.Serialize(ai.Options.LocalVoice))!;
            next.Enabled=next.Managed=next.OwnModels=true;next.OwnAsrEngine=asr.SelectedItem as string??"";next.OwnTtsEngine=tts.SelectedItem as string??"";
            if(next.OwnAsrEngine=="Installed pack" && installedAsr.SelectedItem is not SpeechAsrModel)throw new InvalidOperationException("No listening pack is installed. Choose your own model or install a pack in Automatic setup first.");
            next.OwnAsrPath=asrPath.Text;next.OwnTtsPath=ttsPath.Text;next.ManagedAsr=(installedAsr.SelectedItem as SpeechAsrModel)?.Id??next.ManagedAsr;next.ManagedTts=(installedTts.SelectedItem as SpeechTtsModel)?.Id??next.ManagedTts;
            if(!int.TryParse(speaker.Text,out int id) || id<0 || id>255)throw new InvalidOperationException("Speaker ID must be a number from 0 to 255.");
            next.OwnSpeaker=id;next.SpeechLanguage=codes[Math.Max(0,language.SelectedIndex)];next.ManagedGpu=gpu.IsChecked==true;
            AssistantOwnSpeech.Asr(next);AssistantOwnSpeech.Tts(next);return next;
        }
        void Refresh()
        {
            if(!service.Busy)
            {
                var listeners=SpeechCatalog.Asr.Where(service.HasAsrWeights).ToArray();
                if(!installedAsr.Items.Cast<SpeechAsrModel>().SequenceEqual(listeners))
                {var previous=installedAsr.SelectedItem as SpeechAsrModel;installedAsr.ItemsSource=listeners;installedAsr.SelectedItem=listeners.FirstOrDefault(m=>m.Id==previous?.Id)??listeners.FirstOrDefault();}
                var voices=SpeechCatalog.Tts.Where(service.HasTtsWeights).ToArray();
                if(!installedTts.Items.Cast<SpeechTtsModel>().SequenceEqual(voices))
                {var previous=installedTts.SelectedItem as SpeechTtsModel;installedTts.ItemsSource=voices;installedTts.SelectedItem=voices.FirstOrDefault(m=>m.Id==previous?.Id)??voices.FirstOrDefault();}
            }
            bool busy=working || service.Busy;mode.IsEnabled=asr.IsEnabled=tts.IsEnabled=asrRow.IsEnabled=ttsRow.IsEnabled=installedAsr.IsEnabled=installedTts.IsEnabled=language.IsEnabled=speaker.IsEnabled=check.IsEnabled=prepare.IsEnabled=gpu.IsEnabled=!busy;
            cancel.Visibility=working && service.CanCancel?Visibility.Visible:Visibility.Collapsed;
            asrInstalledField.Visibility=asr.SelectedItem as string=="Installed pack"?Visibility.Visible:Visibility.Collapsed;asrFileField.Visibility=asrInstalledField.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
            ttsInstalledField.Visibility=tts.SelectedItem as string=="Installed pack"?Visibility.Visible:Visibility.Collapsed;ttsFileField.Visibility=tts.SelectedIndex<=1?Visibility.Collapsed:Visibility.Visible;
            gpu.Visibility=asr.SelectedItem as string=="Whisper GGML file" || asr.SelectedItem as string=="Installed pack"?Visibility.Visible:Visibility.Collapsed;
            string listen=(asr.SelectedItem as string) switch
            {
                "Whisper GGML file"=>"Whisper: a whisper.cpp GGML .bin file (not a GGUF chat model).",
                "Qwen3-ASR INT8 folder"=>"Qwen: conv_frontend.onnx, encoder.int8.onnx, decoder.int8.onnx and tokenizer/{vocab.json,merges.txt} in the Sherpa-ONNX export layout.",
                "Moonshine v2 folder"=>"Moonshine v2: encoder_model.ort/.onnx, decoder_model_merged.ort/.onnx and tokens.txt.",
                _=>"Installed listening pack: choose weights you have already installed in Automatic local setup."
            };
            string speak=(tts.SelectedItem as string) switch
            {
                "Piper ONNX file"=>"Piper: select a .onnx file, with its matching .onnx.json next to it.",
                "Kokoro folder" or "KittenTTS folder"=>"Kokoro/Kitten: model.int8.onnx or model.onnx, voices.bin, tokens.txt and the complete espeak-ng-data folder. This adapter uses English voices.",
                "Supertonic folder"=>"Supertonic INT8: duration_predictor.int8.onnx, text_encoder.int8.onnx, vector_estimator.int8.onnx, vocoder.int8.onnx, tts.json, unicode_indexer.bin and voice.bin.",
                "Windows voice"=>"Windows voice uses the default installed Windows voice; no speaking model file is needed.",
                _=>"Installed speaking pack: use a voice downloaded earlier in Automatic local setup."
            };
            help.Text=listen+"\n\n"+speak;
        }
        void Changed(){if(subscribed)Dispatcher.BeginInvoke(()=>{if(subscribed && p.IsVisible){if(working)result.Text=service.Status;Refresh();}});}
        check.Click+=(_,_)=>{try{Read();result.Text="Required file structure checked. This does not run inference or prove model compatibility. Prepare engines & use models to save.";}catch(Exception ex){result.Text=ex.Message;}};
        async Task Use()
        {
            if(working || service.Busy)return;
            try
            {
                var next=Read();working=true;ai.StopSession();Refresh();result.Text="Preparing the required engines. Your model weights are not downloaded or modified.";
                if(!await service.PrepareOwn(next)){result.Text=service.Status;return;}
                ai.UseOwnSpeech(next);RefreshEnabled();Feedback("Your local speech models are saved and selected.");result.Text="Your models are selected. Open Voice mode → Start conversation when ready.";
            }
            catch(Exception ex){result.Text="Could not use these models: "+ex.Message;Feedback(result.Text,true);}
            finally{working=false;Refresh();}
        }
        saveOwnModels=Use;prepare.Click+=async(_,_)=>await Use();asr.SelectionChanged+=(_,_)=>Refresh();tts.SelectionChanged+=(_,_)=>Refresh();
        p.Loaded+=(_,_)=>{if(subscribed)return;subscribed=true;service.Changed+=Changed;if(p.IsVisible)Refresh();};p.IsVisibleChanged+=(_,_)=>{if(p.IsVisible && p.IsLoaded)Refresh();};p.Unloaded+=(_,_)=>{subscribed=false;service.Changed-=Changed;};Refresh();
        return p;
    }
    private FrameworkElement PathRow(TextBox input,Func<bool> isFile,string filter)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(input);input.MaxLength=4096;
        var browse=AssistantWindow.Pill("Browse…",()=>
        {
            try
            {
                if(isFile()){var dialog=new OpenFileDialog{Title="Choose local speech model",Filter=filter,CheckFileExists=true};if(dialog.ShowDialog(Window.GetWindow(this))==true)input.Text=dialog.FileName;}
                else{var dialog=new OpenFolderDialog{Title="Choose extracted speech model folder",Multiselect=false};if(dialog.ShowDialog(Window.GetWindow(this))==true)input.Text=dialog.FolderName;}
            }
            catch(Exception ex){speechStatus.Text="Could not choose the model: "+ex.Message;}
        });browse.Margin=new(8,0,0,0);Grid.SetColumn(browse,1);row.Children.Add(browse);return row;
    }
}
