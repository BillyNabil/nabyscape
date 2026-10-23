using System;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
internal static class AssistantRecoveryControls
{
    internal static FrameworkElement Build(App app)
    {
        var row=new WrapPanel{Margin=new(0,6,0,0)};var settings=AssistantControls.Action("Check connection","settings",app.ShowAssistantProviders);var voice=AssistantControls.Action("Voice settings","mic",app.ShowVoiceSettings);var mic=AssistantControls.Action("Microphone settings","mic",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone"){UseShellExecute=true});}catch{app.Notify("Microphone settings","Open Windows Settings → Privacy → Microphone.");}});
        var wake=AssistantControls.Action("Resume wake word","mic",app.Assistant.ToggleWakeListening);foreach(var b in new[]{settings,voice,mic,wake})row.Children.Add(b);
        void Refresh()
        {
            bool error=app.Assistant.State==AssistantState.Error;row.Visibility=error || !app.Assistant.Options.Enabled || app.Assistant.Provider==null?Visibility.Visible:Visibility.Collapsed;
            voice.Visibility=error?Visibility.Visible:Visibility.Collapsed;mic.Visibility=error && (app.Assistant.Status.Contains("microphone",StringComparison.OrdinalIgnoreCase) || app.Assistant.Status.Contains("audio device",StringComparison.OrdinalIgnoreCase))?Visibility.Visible:Visibility.Collapsed;
            wake.Visibility=app.Assistant.Options.WakeWord.Enabled && !app.Assistant.WakeArmed?Visibility.Visible:Visibility.Collapsed;
        }
        bool subscribed=false;row.Loaded+=(_,_)=>{if(!subscribed){subscribed=true;app.Assistant.Changed+=Refresh;}Refresh();};row.Unloaded+=(_,_)=>{if(subscribed){subscribed=false;app.Assistant.Changed-=Refresh;}};return row;
    }
}
