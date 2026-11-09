using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private TextBlock? voiceSetupSummary;
    private void BuildVoiceWorkspace(StackPanel host)
    {
        voiceSetupSummary=UI.Text("",13,UI.Muted);voiceSetupSummary.Margin=new(0,0,0,16);host.Children.Add(voiceSetupSummary);
        host.Children.Add(MicrophoneSettings());
        var navigation=new WrapPanel{Margin=new(0,0,0,20)};host.Children.Add(navigation);
        var pages=new Dictionary<string,FrameworkElement>();var buttons=new Dictionary<string,Button>();
        void Select(string id)
        {
            foreach(var item in pages)item.Value.Visibility=item.Key==id?Visibility.Visible:Visibility.Collapsed;
            foreach(var item in buttons)
            {
                bool active=item.Key==id;item.Value.SetResourceReference(Control.BackgroundProperty,active?"SelectionFill":"ControlFill");
                System.Windows.Automation.AutomationProperties.SetHelpText(item.Value,active?Localization.Text("Current voice settings page"):Localization.Format("Open {0}",Localization.Text(item.Key)));
            }
        }
        selectVoicePage=Select;
        revealVoiceTarget=target=>
        {
            for(DependencyObject? node=target;node!=null;node=LogicalTreeHelper.GetParent(node))
                foreach(var item in pages)if(ReferenceEquals(node,item.Value)){Select(item.Key);return;}
        };
        void Add(string id,string title,string icon,Func<FrameworkElement> build)
        {
            var button=AssistantControls.Action(title,icon,()=>Select(id));button.Margin=new(0,0,6,6);button.MinHeight=40;buttons[id]=button;navigation.Children.Add(button);
            var content=build();content.Visibility=Visibility.Collapsed;pages[id]=content;host.Children.Add(content);
        }
        FrameworkElement Direct(string title,string subtitle,FrameworkElement element)
        {
            var body=new StackPanel();body.Children.Add(UI.Heading(title,20));var hint=UI.Text(subtitle,13,UI.Muted);hint.Margin=new(0,6,0,18);body.Children.Add(hint);
            if(element is Expander expander)
            {
                // Keep the existing lifecycle and refresh handlers, without a second navigation gate.
                expander.IsExpanded=true;expander.Margin=new(0);
                var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetBinding(ContentPresenter.ContentProperty,new Binding("Content"){Source=expander});
                expander.Template=new ControlTemplate(typeof(Expander)){VisualTree=presenter};
            }
            body.Children.Add(element);return body;
        }
        Add("Setup",Localization.Text("Voice setup"),"mic",LocalVoiceSettings);
        Add("Wake",Localization.Text("Wake word"),"mic",()=>Direct(Localization.Text("Wake Naby with your voice"),Localization.Text("Choose a phrase and turn hands-free listening on or off."),WakeWordSettings()));
        Add("Models","Models","nav-launch",()=>Direct(Localization.Text("Models on this PC"),Localization.Text("See installed voices and remove models you no longer use."),InstalledSpeechSettings()));
        Add("Audio",Localization.Text("Check audio"),"settings",()=>Direct(Localization.Text("Check your audio"),Localization.Text("Check listening and speaking separately using your saved setup."),SpeechChecks()));
        Select("Setup");
    }
}
