using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private Func<bool>? saveManagedPack;
    private FrameworkElement AutomaticSpeechSettings(ComboBox mode)
    {
        var service=ai.Speech;var p=new StackPanel();p.Children.Add(UI.Heading("Voice models for your PC",16));
        p.Children.Add(UI.Text("Choose a language, use the suggested models, then Install & use. Nothing downloads until you choose to install.",11,UI.Muted));
        var hardware=UI.Text("Hardware has not been scanned yet.",11,UI.Muted);hardware.Margin=new(0,10,0,6);p.Children.Add(hardware);
        var language=new ComboBox{ItemsSource=new[]{"English","Spanish","French","German","Portuguese","Italian","Japanese","Korean","Russian","Arabic","Dutch","Turkish","Hindi","Vietnamese","Indonesian"}};
        string[] codes={"en","es","fr","de","pt","it","ja","ko","ru","ar","nl","tr","hi","vi","id"};string selectedLanguage=(SpeechCatalog.Tts.FirstOrDefault(v=>v.Id==ai.Options.LocalVoice.ManagedTts)??SpeechCatalog.Tts[0]).Language;
        if(selectedLanguage is "any" or "multi")selectedLanguage=ai.Options.LocalVoice.SpeechLanguage;
        language.SelectedIndex=Math.Max(0,Array.IndexOf(codes,selectedLanguage));Label(p,"Language for spoken replies",language);
        var priority=new ComboBox{ItemsSource=new[]{"Light","Balanced","Quality"},SelectedItem=ai.Options.LocalVoice.SpeechPreference};if(priority.SelectedIndex<0)priority.SelectedIndex=1;Label(p,"Performance preference",priority);
        var recommendation=UI.Text("",12);recommendation.Margin=new(0,10,0,4);p.Children.Add(recommendation);
        var asr=new ComboBox{ItemsSource=SpeechCatalog.Asr,SelectedItem=SpeechCatalog.Asr.FirstOrDefault(m=>m.Id==ai.Options.LocalVoice.ManagedAsr)??SpeechCatalog.Asr[1]};Label(p,"Listening model",asr);
        var tts=new ComboBox();Label(p,"Speaking voice",tts);
        var gpu=new CheckBox{Content=UI.Text("Use NVIDIA GPU when supported · CPU fallback included",11),IsChecked=ai.Options.LocalVoice.ManagedGpu,Margin=new(0,12,0,6)};p.Children.Add(gpu);
        var extras=new StackPanel();
        var profileBody=new StackPanel();var profileList=new Expander{Header="Hardware profiles · CPU / RAM / VRAM",Content=profileBody,Margin=new(0,12,0,8)};extras.Children.Add(profileList);
        var profileRows=new System.Collections.Generic.List<(SpeechHardwareProfile Profile,TextBlock Text,Button Button)>();
        foreach(var profile in SpeechCatalog.Profiles)
        {
            var body=new StackPanel();body.Children.Add(UI.Text(profile.Name,12));var text=UI.Text("",10,UI.Muted);text.Margin=new(0,4,0,6);body.Children.Add(text);
            var choose=AssistantWindow.Pill("Use this profile",()=>{});body.Children.Add(choose);profileRows.Add((profile,text,choose));
            profileBody.Children.Add(new Border{Child=body,Padding=new(12),Margin=new(0,4,0,4),CornerRadius=new(10),Background=UI.Brush("#22262D")});
        }
        var detail=UI.Text("",11,UI.Muted);detail.Margin=new(0,6,0,8);extras.Children.Add(detail);
        var actions=new WrapPanel();var scan=AssistantWindow.Pill("Scan hardware",()=>{});var recommended=AssistantWindow.Pill("Use suggested models",()=>{});var install=AssistantWindow.Pill("Install & use",()=>{},true);var cancel=AssistantWindow.Pill("Cancel download",service.Cancel);var openVoice=AssistantWindow.Pill("Open voice mode",app.OpenVoice);
        foreach(var button in new[]{scan,recommended,install,cancel,openVoice})actions.Children.Add(button);p.Children.Add(actions);
        var progress=new ProgressBar{Minimum=0,Maximum=100,Height=3,Foreground=Brushes.White,Background=UI.Brush("#30343B"),BorderThickness=new(0),Margin=new(0,4,0,8)};p.Children.Add(progress);
        var message=UI.Text("",11,UI.Muted);p.Children.Add(message);
        var catalogueBody=new StackPanel();var catalogue=new Expander{Header="ASR & TTS library · curated October 2026",Content=catalogueBody,Margin=new(0,14,0,10)};
        var descriptions=new System.Collections.Generic.Dictionary<string,TextBlock>();
        var modelButtons=new System.Collections.Generic.Dictionary<string,Button>();
        foreach(var model in SpeechCatalog.Asr)
        {
            var entry=new StackPanel();entry.Children.Add(UI.Text(model.Name,12));var text=UI.Text("",10,UI.Muted);text.Margin=new(0,4,0,6);entry.Children.Add(text);descriptions[model.Id]=text;
            var choose=AssistantWindow.Pill("Choose model",()=>asr.SelectedItem=model);modelButtons[model.Id]=choose;entry.Children.Add(choose);catalogueBody.Children.Add(new Border{Child=entry,Padding=new(10),Margin=new(0,3,0,3),CornerRadius=new(8),Background=UI.Brush("#22262D")});
        }
        catalogueBody.Children.Add(UI.Text("Speaking models",13));
        foreach(var voice in SpeechCatalog.Tts)
        {
            catalogueBody.Children.Add(AssistantWindow.Pill(voice.Name,()=>{if(voice.Supports(Language()))tts.SelectedItem=voice;}));
            catalogueBody.Children.Add(UI.Text(SpeechCatalog.VoiceDetails(voice),10,UI.Muted));
        }
        extras.Children.Add(catalogue);
        var links=new WrapPanel();links.Children.Add(AssistantWindow.Pill("Model details",()=>Process.Start(new ProcessStartInfo(asr.SelectedItem is SpeechAsrModel a?SpeechCatalog.ModelLink(a):"https://github.com/ggml-org/whisper.cpp"){UseShellExecute=true})));
        var license=AssistantWindow.Pill("Voice details & license",()=>{if(tts.SelectedItem is SpeechTtsModel m && m.Id!="windows")Process.Start(new ProcessStartInfo(SpeechCatalog.VoiceLink(m)){UseShellExecute=true});});links.Children.Add(license);links.Children.Add(AssistantWindow.Pill("Open speech folder",service.OpenFolder));extras.Children.Add(links);
        extras.Children.Add(UI.Text("Suggestions are memory budgets, not measured speed or a quality ranking. Other apps and local chat share RAM/VRAM. New compatible pack versions arrive through Naby updates. Native TTS uses CPU to leave VRAM available for ASR. AMD/Intel GPUs use the CPU path. AI replies still use your selected chat provider or Hermes.",10,UI.Muted));
        p.Children.Add(new Expander{Header="Model details & alternatives",Content=extras,Margin=new(0,14,0,10)});
        bool scanning=false,subscribed=false,firstScan=true,refreshQueued=false;string? savedMessage=null;
        string Language()=>codes[Math.Max(0,language.SelectedIndex)];string Preference()=>priority.SelectedItem as string??"Balanced";
        void Voices()
        {
            string keepAsr=(asr.SelectedItem as SpeechAsrModel)?.Id??ai.Options.LocalVoice.ManagedAsr;var listeners=SpeechCatalog.Asr.Where(a=>a.Supports(Language())).ToArray();asr.ItemsSource=listeners;asr.SelectedItem=listeners.FirstOrDefault(a=>a.Id==keepAsr)??listeners[0];
            string keep=(tts.SelectedItem as SpeechTtsModel)?.Id??ai.Options.LocalVoice.ManagedTts;var choices=SpeechCatalog.Tts.Where(v=>v.Supports(Language())).ToArray();tts.ItemsSource=choices;tts.SelectedItem=choices.FirstOrDefault(v=>v.Id==keep)??choices[0];
        }
        void Refresh()
        {
            if(asr.SelectedItem is not SpeechAsrModel a || tts.SelectedItem is not SpeechTtsModel t)return;
            var h=service.Hardware;bool useGpu=h?.UseGpu(a)==true && gpu.IsChecked==true;bool busy=service.Busy || scanning,installed=!busy && service.Installed(a,t,useGpu);
            hardware.ToolTip=h?.Summary;hardware.Text=h==null?"Scan this PC to find models that fit.":"Hardware detected · suggestions available";
            var pack=h==null?null:SpeechCatalog.RecommendPack(h,Language(),Preference());var suggestion=pack?.Asr;recommendation.ToolTip=pack?.Reason;recommendation.Text=h==null?"Choose a language and scan your hardware.":!h.Supported?AssistantSpeechPlatform.NativePackUnavailable:!h.Fits(SpeechCatalog.Asr[0])?"Memory is low. Close other apps and scan again.":"Suggested pack: "+suggestion!.Name+" + "+pack!.Tts.Name;
            mode.IsEnabled=language.IsEnabled=priority.IsEnabled=asr.IsEnabled=tts.IsEnabled=scan.IsEnabled=!busy;recommended.IsEnabled=!busy && h!=null && h.FitsPack(suggestion!,pack!.Tts);gpu.IsEnabled=!busy && h?.UseGpu(a)==true;
            install.Content=installed?"Use installed pack":"Install & use";install.IsEnabled=!busy && h!=null && (installed || h.FitsPack(a,t));cancel.Visibility=service.CanCancel?Visibility.Visible:Visibility.Collapsed;catalogue.IsEnabled=profileList.IsEnabled=!busy;
            bool selected=ai.Options.Enabled && ai.Options.LocalVoice is {Enabled:true,Managed:true,OwnModels:false} active && active.ManagedAsr==a.Id && active.ManagedTts==t.Id && active.ManagedGpu==useGpu && active.SpeechLanguage==Language() && active.SpeechPreference==Preference();
            openVoice.IsEnabled=installed && selected && ai.Setup.Chat.Configured;openVoice.ToolTip=openVoice.IsEnabled?"Open voice mode. The microphone starts only when you choose Start conversation.":"Install and select this pack, then connect a chat model first.";
            long bytes=a.DownloadBytes+(a.Engine=="whisper"?SpeechCatalog.Cpu.Bytes+(useGpu?SpeechCatalog.Gpu.Bytes:0):0)+(a.Native || t.Package!=null?SpeechCatalog.Sherpa.Bytes:0)+(t.Package!=null?t.Bytes:t.Id=="windows"?0:t.Bytes+SpeechCatalog.Piper.Bytes)+(AssistantSpeechInstall.CppRuntimePresent()?0:SpeechCatalog.VisualCpp.Bytes);
            detail.Text=(installed?"Installed · ready to select":"Full pack download: "+(bytes/1000000d).ToString("0")+" MB")+" · "+(useGpu?"NVIDIA GPU with CPU fallback":"CPU")+"\nMemory budget: "+a.RamGb.ToString("0.#")+" GB total, "+SpeechHardware.PackFreeGb(a,t).ToString("0.#")+" GB available.";
            detail.Text+="\n"+SpeechCatalog.AsrDetails(a)+"\n"+SpeechCatalog.VoiceDetails(t);
            if(t.Engine=="supertonic")detail.Text+="\nSynthesis steps: "+(Preference()=="Quality"?12:Preference()=="Light"?4:8)+" · OpenRAIL-M model license (separate from the app's MIT license).";
            if(h?.Supported==true && !h.FitsPack(a,t))detail.Text+="\nClose other apps or choose a smaller combination before starting.";
            if(AssistantSpeechPlatform.NativePacksSupported && !AssistantSpeechInstall.CppRuntimePresent())detail.Text+="\nA missing Microsoft C++ runtime is installed too; Windows may request administrator approval.";
            license.Visibility=t.Id=="windows"?Visibility.Collapsed:Visibility.Visible;progress.Visibility=service.Busy?Visibility.Visible:Visibility.Collapsed;progress.Value=service.Progress;message.Text=savedMessage??service.Status;
            if(profileList.IsExpanded && !busy)foreach(var row in profileRows)
            {
                var candidate=h==null?null:SpeechCatalog.ProfilePack(h,row.Profile,Language(),Preference());row.Button.IsEnabled=candidate!=null;
                row.Text.Text=candidate!=null?candidate.Asr.Name+" + "+candidate.Tts.Name+"\nFits available memory. "+candidate.Reason:SpeechCatalog.AsrById(row.Profile.AsrId).Name+"\n"+(h==null?"Scan hardware to check availability.":"Needs matching hardware, a supported language and enough free RAM/VRAM.");
            }
            foreach(var model in SpeechCatalog.Asr)modelButtons[model.Id].IsEnabled=!busy && model.Supports(Language());
            if(catalogue.IsExpanded && !busy)foreach(var model in SpeechCatalog.Asr)descriptions[model.Id].Text=(model.DownloadBytes/1000000d).ToString("0")+" MB model · "+model.RamGb.ToString("0.#")+" GB RAM budget · "+SpeechCatalog.AsrDetails(model)+"\n"+(h==null?"Scan hardware to check fit.":h.FitsPack(model,t)?"Fits the current memory budget with this voice.":"Not enough available memory for this combination.");
        }
        void Changed(){if(!subscribed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Changed));return;}if(!p.IsVisible || refreshQueued)return;refreshQueued=true;Dispatcher.BeginInvoke(()=>{refreshQueued=false;if(subscribed && p.IsVisible)Refresh();});}
        void Recommend(){if(service.Hardware is not {} h)return;SelectPack(SpeechCatalog.RecommendPack(h,Language(),Preference()));}
        void SelectPack(SpeechPack pack){asr.SelectedItem=pack.Asr;tts.SelectedItem=pack.Tts;gpu.IsChecked=pack.Gpu;savedMessage=null;Refresh();}
        async System.Threading.Tasks.Task Scan()
        {
            if(scanning || service.Busy)return;scanning=true;savedMessage="Reading CPU, available RAM and GPU…";Refresh();
            try{await service.Scan();if(firstScan && !ai.Options.LocalVoice.Managed)Recommend();firstScan=false;savedMessage="Scan complete. Choose a model and voice, then Install & use.";}
            catch(Exception ex){savedMessage="Hardware scan failed: "+ex.Message;}
            finally{scanning=false;Refresh();}
        }
        bool UsePack()
        {
            try
            {
                if(service.Busy)throw new InvalidOperationException("Wait for installation to finish or cancel it first.");if(asr.SelectedItem is not SpeechAsrModel a || tts.SelectedItem is not SpeechTtsModel t)return false;
                if(!a.Supports(Language()) || !t.Supports(Language()))throw new InvalidOperationException("Choose listening and speaking models that support the selected language.");
                bool useGpu=service.Hardware?.UseGpu(a)==true && gpu.IsChecked==true;ai.UseSpeechPack(a,t,useGpu,Preference(),Language());RefreshEnabled();Feedback("Speech models saved · "+a.Name+" / "+t.Name+".");
                savedMessage=ai.Provider==null || string.IsNullOrWhiteSpace(ai.Provider.Model)?"Pack selected. Connect Hermes or choose a chat provider/model, then return to Voice mode.":"Ready. Return to Voice mode → Start conversation. End conversation closes the speech engines.";Refresh();return true;
            }
            catch(Exception ex){savedMessage=ex.Message;Feedback("Speech models not saved · "+ex.Message,true);Refresh();return false;}
        }
        saveManagedPack=UsePack;Voices();
        foreach(var row in profileRows)row.Button.Click+=(_,_)=>{if(service.Hardware is {} h && SpeechCatalog.ProfilePack(h,row.Profile,Language(),Preference()) is {} pack)SelectPack(pack);};
        scan.Click+=async(_,_)=>await Scan();recommended.Click+=(_,_)=>Recommend();asr.SelectionChanged+=(_,_)=>{savedMessage=null;Refresh();};tts.SelectionChanged+=(_,_)=>{savedMessage=null;Refresh();};language.SelectionChanged+=(_,_)=>{Voices();savedMessage=null;Refresh();};priority.SelectionChanged+=(_,_)=>{savedMessage=null;Refresh();};gpu.Checked+=(_,_)=>Refresh();gpu.Unchecked+=(_,_)=>Refresh();catalogue.Expanded+=(_,_)=>Refresh();profileList.Expanded+=(_,_)=>Refresh();
        install.Click+=async(_,_)=>{if(asr.SelectedItem is not SpeechAsrModel a || tts.SelectedItem is not SpeechTtsModel t)return;bool useGpu=service.Hardware?.UseGpu(a)==true && gpu.IsChecked==true;savedMessage=null;if(!service.Installed(a,t,useGpu)){ai.StopSession();if(!await service.Install(a,t,useGpu)){Refresh();return;}}UsePack();};
        p.Loaded+=async(_,_)=>{if(subscribed)return;subscribed=true;service.Changed+=Changed;if(p.IsVisible){Refresh();if(service.Hardware==null)await Scan();}};p.IsVisibleChanged+=async(_,_)=>{if(p.IsVisible && p.IsLoaded){Refresh();if(service.Hardware==null)await Scan();}};p.Unloaded+=(_,_)=>{subscribed=false;service.Changed-=Changed;};Refresh();
        return new Border{Child=p,Padding=new(16),CornerRadius=new(12),Background=UI.Brush("#191D23"),BorderBrush=UI.Brush("#303640"),BorderThickness=new(1),Margin=new(0,12,0,10)};
    }
}
