using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal sealed partial class AssistantProviderSettings
{
    private FrameworkElement InstalledSpeechSettings()
    {
        var service=ai.Speech;var body=new StackPanel();
        body.Children.Add(UI.Text("Remove listening or speaking models you no longer need. Matching download caches are removed too. Shared speech engines stay installed. You can reinstall a model from Automatic setup.",11,UI.Muted));
        var empty=UI.Text("No downloaded speech models on this PC.",12,UI.Muted);empty.Margin=new(0,12,0,8);body.Children.Add(empty);
        var result=UI.Text("",11,UI.Muted);Accessibility.Live(result);body.Children.Add(result);
        bool showReinstall=false;
        var sizes=new Dictionary<string,long>();
        var reinstall=AssistantWindow.Pill("Reinstall / choose another model",()=>{OpenVoiceSetup();speechModePicker!.SelectedIndex=1;});reinstall.Visibility=Visibility.Collapsed;body.Children.Add(reinstall);
        var rows=new List<(string Id,bool Tts,Border Row,TextBlock Detail,Button Remove)>();
        var expander=new Expander{Header="Installed models · manage storage",Content=body,Margin=new(0,14,0,10)};
        UI.Named(expander,"Installed speech models");
        foreach(var model in SpeechCatalog.Asr)Add(model.Id,model.Name,false);
        foreach(var model in SpeechCatalog.Tts.Where(m=>m.Id!="windows"))Add(model.Id,model.Name,true);
        body.Children.Add(UI.Text("Windows voices are managed in Windows Settings. Model files you imported from another folder stay in their original location and are not deleted here.",10,UI.Muted));
        void Add(string id,string name,bool tts)
        {
            var row=new StackPanel();row.Children.Add(UI.Text((tts?"TTS · ":"ASR · ")+name,12));
            var detail=UI.Text("",11,UI.Muted);detail.Margin=new(0,4,0,8);row.Children.Add(detail);
            var remove=AssistantWindow.Pill("Uninstall",()=>{});remove.HorizontalAlignment=HorizontalAlignment.Left;UI.Named(remove,"Uninstall "+name);
            remove.Click+=async(_,_)=>
            {
                if(service.Busy || ai.SpeechCheckBusy){Feedback("Finish the speech check before uninstalling.",true);return;}
                bool active=service.ModelInUse(ai.Options.LocalVoice,id,tts);
                string size;try{size=AssistantSpeechInstall.StorageSize(await System.Threading.Tasks.Task.Run(()=>service.ManagedModelBytes(id,tts)));}catch(Exception ex){Feedback("Could not inspect model storage · "+ex.Message,true);return;}
                string warning="Uninstall "+name+"?\n\nIts downloaded model files and matching cache will be deleted. About "+size+" will be freed. You can reinstall it later."+(active?"\n\nThis is your selected model. Local voice will be unavailable until you select an installed pack or reinstall this one.":"")+(ai.VoiceConnected || ai.Busy || active?"\n\nVoice and wake listening will stop.":"");
                if(MessageBox.Show(Window.GetWindow(this),warning,"Uninstall speech model",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                try{bool ok=await ai.UninstallSpeechModel(id,tts);result.Text=service.Status;Feedback(result.Text,!ok);if(ok)showReinstall=true;Refresh();}
                catch(Exception ex){result.Text="Could not uninstall this model. "+ex.Message;Feedback(result.Text,true);}
            };
            row.Children.Add(remove);var card=new Border{Child=row,Padding=new(12),Margin=new(0,6,0,0),CornerRadius=new(8),Background=UI.Brush("#22262D")};body.Children.Add(card);rows.Add((id,tts,card,detail,remove));
        }
        void Refresh()
        {
            if(!expander.IsExpanded || !expander.IsVisible)return;
            if(service.Busy){foreach(var row in rows)row.Remove.IsEnabled=false;result.Text=service.Status;return;}
            int count=0;
            foreach(var row in rows)
            {
                bool present=service.HasManagedModel(row.Id,row.Tts);row.Row.Visibility=present?Visibility.Visible:Visibility.Collapsed;row.Remove.IsEnabled=present;
                if(!present)continue;count++;
                bool ready=false;
                try{ready=row.Tts?service.HasTtsWeights(SpeechCatalog.TtsById(row.Id)):service.HasAsrWeights(SpeechCatalog.AsrById(row.Id));}
                catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
                row.Detail.Text=(ready?"Installed":"Cached or incomplete download")+(service.ModelInUse(ai.Options.LocalVoice,row.Id,row.Tts)?" · Currently selected":"");
                string sizeKey=(row.Tts?"tts:":"asr:")+row.Id;
                if(sizes.TryGetValue(sizeKey,out long measured) && measured>=0)row.Detail.Text+=" · "+AssistantSpeechInstall.StorageSize(measured)+" on disk, including cache";
                else if(!sizes.ContainsKey(sizeKey))Measure(row.Id,row.Tts,row.Detail,sizeKey);

            }
            reinstall.Visibility=showReinstall?Visibility.Visible:Visibility.Collapsed;
            empty.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
        }
        async void Measure(string id,bool tts,TextBlock label,string key)
        {
            sizes[key]=-1;
            try{long bytes=await System.Threading.Tasks.Task.Run(()=>service.ManagedModelBytes(id,tts));sizes[key]=bytes;if(label.IsVisible && !service.Busy)label.Text+=" · "+AssistantSpeechInstall.StorageSize(bytes)+" on disk, including cache";}
            catch{sizes.Remove(key);label.Text+=" · Storage unavailable";}
        }
        bool subscribed=false,queued=false;
        void StorageChanged(){if(!service.Busy)sizes.Clear();Changed();}
        void Changed(){if(!subscribed || queued || !expander.IsExpanded || !expander.IsVisible)return;queued=true;Dispatcher.BeginInvoke(()=>{queued=false;if(subscribed)Refresh();});}
        expander.Expanded+=(_,_)=>Refresh();expander.IsVisibleChanged+=(_,_)=>{if(expander.IsVisible)Refresh();};
        expander.Loaded+=(_,_)=>{if(subscribed)return;subscribed=true;service.Changed+=StorageChanged;ai.Changed+=Changed;Refresh();};
        expander.Unloaded+=(_,_)=>{subscribed=false;service.Changed-=StorageChanged;ai.Changed-=Changed;};
        return expander;
    }
}
