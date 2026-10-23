using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class SpeechStorageDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();var ai=app.Assistant;var asr=SpeechCatalog.Asr[0];var tts=SpeechCatalog.Tts.First(m=>m.Package==null && m.Id!="windows");
        void Put(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"synthetic fixture, not real weights");}
        Put(ai.Speech.AsrPath(asr));Put(ai.Speech.TtsPath(tts));Put(ai.Speech.TtsPath(tts)+".json");Put(ai.Speech.TtsPath(tts)+".MODEL_CARD");
        string engine=Path.Combine(app.Store.DirectoryPath,"local-speech","engines","keep.txt"),external=Path.Combine(app.Store.DirectoryPath,"user-model.onnx");Put(engine);Put(external);
        ai.Options.Enabled=true;ai.Options.LocalVoice.Enabled=ai.Options.LocalVoice.Managed=true;ai.Options.LocalVoice.ManagedAsr=asr.Id;ai.Options.LocalVoice.ManagedTts=tts.Id;
        var provider=new AssistantProvider{Name="Synthetic provider",Model="fixture",RealtimeModel="fixture",RealtimeUrl="wss://fixture.invalid"};ai.Options.Providers.Add(provider);ai.Options.SelectedProvider=ai.Options.SelectedVoiceProvider=provider.Id;
        app.Store.Save("assistant",ai.Options);string before=File.ReadAllText(Path.Combine(app.Store.DirectoryPath,"assistant.json"));
        var window=new SettingsWindow(app){ShowActivated=false};
        try
        {
            window.Show();Diagnostics.NavigateSettings(window,"AI");await Task.Delay(80);
            var editor=(AssistantProviderSettings)typeof(SettingsWindow).GetField("aiSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            editor.OpenVoiceSetup();((Action<string>)editor.GetType().GetField("selectVoicePage",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!)("Models");window.UpdateLayout();await Task.Delay(100);
            var storage=Diagnostics.Descendants(window).OfType<Expander>().Single(e=>e.Header?.ToString()=="Installed models · manage storage");
            storage.IsExpanded=true;storage.BringIntoView();await Task.Delay(100);window.UpdateLayout();
            Button[] Buttons()=>Diagnostics.Descendants(storage).OfType<Button>().Where(b=>AutomationProperties.GetName(b).StartsWith("Uninstall ") && b.IsVisible).ToArray();
            check("Speech storage lists downloaded ASR and TTS models separately",Buttons().Length==2 && Buttons().All(b=>b.IsEnabled && b.Focusable));
            check("Speech storage marks the currently selected models",Diagnostics.Descendants(storage).OfType<TextBlock>().Count(t=>t.IsVisible && t.Text.Contains("Currently selected"))==2);
            Capture(window,"speech-storage-wide",directory);
            window.Width=520;window.Height=640;storage.BringIntoView();await Task.Delay(120);window.UpdateLayout();Capture(window,"speech-storage-narrow",directory);
            check("Speech storage remains scrollable and reachable on narrow windows",storage.ActualWidth>200 && Buttons().All(b=>b.ActualWidth>0));
            var voice=new AssistantDiagnostics.FixtureVoice();typeof(AssistantRuntime).GetField("voice",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai,voice);
            check("Speech storage active ASR removal succeeds",await ai.UninstallSpeechModel(asr.Id,false));await Task.Delay(100);window.UpdateLayout();
            check("Speech storage releases voice before removing an active model",voice.Stopped && voice.Disposed && !ai.VoiceConnected && !ai.WakeArmed);
            check("Speech storage refresh removes the ASR row and preserves TTS",Buttons().Length==1 && File.Exists(ai.Speech.TtsPath(tts)));
            check("Speech storage keeps local mode and selection rather than falling back to cloud",ai.Options.LocalVoice.Enabled && ai.Options.LocalVoice.Managed && ai.Options.LocalVoice.ManagedAsr==asr.Id && !ai.Setup.Voice.Configured);
            await ai.StartVoice();check("Missing local weights cannot start a microphone or cloud voice",!ai.VoiceConnected && ai.State==AssistantState.Error);
            check("Speech storage removes TTS independently",await ai.UninstallSpeechModel(tts.Id,true));await Task.Delay(100);window.UpdateLayout();
            check("Speech storage empty state updates after uninstall",Buttons().Length==0 && Diagnostics.Descendants(storage).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text=="No downloaded speech models on this PC."));
            check("Speech storage preserves shared engines, imported files and saved preferences",File.Exists(engine) && File.Exists(external) && File.ReadAllText(Path.Combine(app.Store.DirectoryPath,"assistant.json"))==before);
        }
        finally{window.Close();}
    }
    private static void Capture(FrameworkElement element,string name,string directory)
    {
        var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(directory,name+".png"));encoder.Save(stream);
    }
}
