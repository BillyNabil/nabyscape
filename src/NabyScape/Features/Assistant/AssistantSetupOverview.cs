using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

internal sealed partial class AssistantProviderSettings
{
    private readonly Dictionary<string,StackPanel> setupPages=new();
    private readonly Dictionary<string,Button> setupTabs=new();
    private readonly TextBlock setupTitle=UI.Heading(Localization.Text("How would you like to use Naby?"),21),setupDescription=UI.Caption("",12,UI.Muted);
    private readonly TextBlock chatReadiness=UI.Caption("",12),chatDetail=UI.Caption("",11,UI.Muted),voiceReadiness=UI.Caption("",12),voiceDetail=UI.Caption("",11,UI.Muted);
    private Button nextSetup=null!;
    private TextBlock? setupMap;
    private bool setupSubscribed,setupRefreshQueued;
    private string setupSection="";
    internal void OpenSetupOverview()=>SelectSetupSection("Overview");
    internal void OpenChatSetup()=>SelectSetupSection("Chat");
    internal void OpenVoiceChecks(){OpenVoiceSetup();selectVoicePage?.Invoke("Audio");}
    internal void RevealSetupTarget(DependencyObject target)
    {
        for(DependencyObject? node=target;node!=null;node=LogicalTreeHelper.GetParent(node)??(node is Visual?VisualTreeHelper.GetParent(node):null))
        {
            var section=setupPages.FirstOrDefault(item=>ReferenceEquals(item.Value,node));
            if(section.Key!=null){SelectSetupSection(section.Key);if(section.Key=="Voice")revealVoiceTarget?.Invoke(target);return;}
        }
    }
    private void BuildSetupNavigation(StackPanel page)
    {
        var tabs=new WrapPanel{Margin=new(0,0,0,16)};
        foreach(var item in new[]{("Overview","nav-launch"),("Chat","chat"),("Voice","mic"),("Hermes","link"),("Advanced","settings")})
        {
            string section=item.Item1;var button=AssistantControls.Action(section=="Overview"?"Get started":section=="Advanced"?"More":section,item.Item2,()=>SelectSetupSection(section));
            button.Margin=new(0,0,4,4);setupTabs[section]=button;tabs.Children.Add(button);setupPages[section]=new StackPanel();
        }
        UI.LocalizedName(tabs,"AI settings categories");page.Children.Add(tabs);
        var overview=setupPages["Overview"];
        overview.Children.Add(setupTitle);setupDescription.Margin=new(0,6,0,16);overview.Children.Add(setupDescription);
        void Choice(string title,string description,string action,Action open)
        {
            var group=new StackPanel{Margin=new(0,0,0,24)};group.Children.Add(UI.Heading(title,17));var detail=UI.Text(description,13,UI.Muted);detail.Margin=new(0,7,0,12);group.Children.Add(detail);group.Children.Add(AssistantWindow.Pill(action,open));overview.Children.Add(group);
        }
        Choice("Chat with Naby","Ask questions, paste images and work with your files.","Set up chat",()=>SelectSetupSection("Chat"));
        Choice("Talk hands-free","Choose a voice, check your microphone and set a wake word.","Set up voice",OpenVoiceSetup);
        Choice("Use your Hermes agent","Bring your agent's memory and tools into Naby.","Connect Hermes",OpenHermesSetup);
        setupMap=UI.Text("",12,UI.Muted);var saved=new Expander{Header="Your current setup",Content=setupMap,IsExpanded=false,Margin=new(0,0,0,20)};overview.Children.Add(saved);
        nextSetup=AssistantWindow.Pill("Continue setup",NextSetup,true);nextSetup.HorizontalAlignment=HorizontalAlignment.Left;overview.Children.Add(nextSetup);
        foreach(var body in setupPages.Values)page.Children.Add(body);
        Loaded+=(_,_)=>{if(!setupSubscribed){setupSubscribed=true;ai.Changed+=QueueSetupRefresh;ai.Speech.Changed+=QueueSetupRefresh;}RefreshSetupOverview();};
        Unloaded+=(_,_)=>{setupSubscribed=false;ai.Changed-=QueueSetupRefresh;ai.Speech.Changed-=QueueSetupRefresh;};
    }
    private void SelectSetupSection(string section)
    {
        if(!setupPages.ContainsKey(section))return;if(setupSection!=section)CancelProviderRequest();setupSection=section;
        foreach(var item in setupPages)item.Value.Visibility=item.Key==section?Visibility.Visible:Visibility.Collapsed;
        foreach(var item in setupTabs)
        {
            bool selected=item.Key==section;
            item.Value.SetResourceReference(Control.BackgroundProperty,selected?"SelectionFill":"ControlFill");
            item.Value.SetResourceReference(Control.ForegroundProperty,selected?"SelectionText":"ControlText");
            if(item.Value.Content is StackPanel content && content.Children[1] is TextBlock label)label.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetHelpText(item.Value,selected?"Current category":"Open "+item.Key+" settings");
        }
        RefreshSetupOverview();
    }
    private void QueueSetupRefresh()
    {
        if(setupRefreshQueued || !setupSubscribed)return;setupRefreshQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{setupRefreshQueued=false;if(setupSubscribed)RefreshSetupOverview();}));
    }
    private void RefreshSetupOverview()
    {
        if(nextSetup==null)return;
        var ready=ai.Setup;
        setupTitle.Text=Localization.Text(ready.Chat.Verified?"Your companion is ready":"How would you like to use Naby?");
        setupDescription.Text=Localization.Text("Choose what you want to do. Your saved setup stays available when you switch between chat and voice.");
        chatReadiness.Text=ready.Chat.Title;chatDetail.Text=ready.Chat.Detail;
        if(voiceSetupSummary!=null)voiceSetupSummary.Text=ai.VoiceConnected?"Microphone on · voice connected":ai.Setup.Voice.Title;
        voiceReadiness.Text=ai.VoiceConnected?Localization.Text("Voice connected · microphone on"):ready.Voice.Title;voiceDetail.Text=ready.Voice.Detail;
        string nextCaption=!ai.Options.Enabled?"Enable AI companion":!ready.Chat.Configured?"Set up chat":!ready.Chat.Verified?"Check chat connection":"Open chat";nextSetup.Content=Localization.Text(nextCaption);UI.LocalizedName(nextSetup,nextCaption);
        if(setupMap!=null)setupMap.Text=ai.SetupSummary;
        nextSetup.IsEnabled=!providerBusy;
    }
    private async System.Threading.Tasks.Task CheckSelectedChat(){if(!await ConfirmProviderChanges())return;SelectSetupSection("Chat");Reload();await TestConnection();}
    private void NextSetup()
    {
        if(!ai.Options.Enabled){ai.Options.Enabled=true;SaveConfiguration();RefreshEnabled();RefreshSetupOverview();return;}
        if(!ai.Setup.Chat.Configured){SelectSetupSection("Chat");connectionEditor.IsExpanded=true;return;}
        if(!ai.Setup.Chat.Verified){_=CheckSelectedChat();return;}
        app.ShowAssistant();
    }
}
