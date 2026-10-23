using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal sealed partial class AssistantProviderSettings
{
    private sealed record WakeRecognizerChoice(string Id,string Label){public override string ToString()=>Label;}
    private FrameworkElement WakeWordSettings()
    {
        var p=new StackPanel();var current=ai.Options.WakeWord;
        var enabled=new CheckBox{Content="Enable wake word on this PC",IsChecked=current.Enabled,Margin=new(0,4,0,8)};p.Children.Add(enabled);
        p.Children.Add(UI.Text("Say a phrase to open Naby voice and start your selected voice connection. Detection uses an installed Windows speech recognizer, separately from your ASR/TTS models.",11,UI.Muted));
        var phrase=UI.Input("Wake phrase",current.Phrase);phrase.MaxLength=64;Label(p,"Wake phrase",phrase);
        var recognizer=new ComboBox();Label(p,"Windows recognition language",recognizer);UI.Named(recognizer,"Wake recognition language");
        var feedback=UI.Text("",11,UI.Muted);feedback.Margin=new(0,8,0,8);
        void Languages()
        {
            string selected=(recognizer.SelectedItem as WakeRecognizerChoice)?.Id??current.RecognizerId;
            try{var installed=AssistantWakeWord.Recognizers();recognizer.ItemsSource=new[]{new WakeRecognizerChoice("","Automatic · prefer English")}.Concat(installed.Select(r=>new WakeRecognizerChoice(r.Id,r.Description+" · "+r.Culture.DisplayName))).ToArray();recognizer.SelectedItem=recognizer.Items.Cast<WakeRecognizerChoice>().FirstOrDefault(r=>r.Id==selected)??recognizer.Items[0];if(installed.Length==0)feedback.Text="No Windows speech recognizer found. Install a Speech language in Windows settings, then refresh.";}
            catch{feedback.Text="Windows speech recognizers could not be listed. Check installed speech languages and microphone permissions.";}
        }
        var languageActions=new WrapPanel();languageActions.Children.Add(AssistantWindow.Pill("Refresh recognizers",Languages));languageActions.Children.Add(AssistantWindow.Pill("Windows speech settings",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:speech"){UseShellExecute=true});}catch{feedback.Text="Open Windows Settings → Time & language → Speech.";}}));p.Children.Add(languageActions);
        var threshold=new Slider{Minimum=.55,Maximum=.95,TickFrequency=.01,IsSnapToTickEnabled=true,Value=Math.Clamp(current.Confidence,.55,.95)};var sensitivity=UI.Text("",11,UI.Muted);void ThresholdLabel()=>sensitivity.Text="Activation confidence · "+Math.Round(threshold.Value*100)+"%";ThresholdLabel();threshold.ValueChanged+=(_,_)=>ThresholdLabel();sensitivity.Margin=new(0,10,0,4);p.Children.Add(sensitivity);p.Children.Add(threshold);UI.Named(threshold,"Wake word activation confidence");p.Children.Add(UI.Text("Higher confidence reduces accidental activation. If your phrase is missed, lower it or try a phrase suited to the selected Windows language.",10,UI.Muted));
        var actions=new WrapPanel{Margin=new(0,8,0,0)};actions.Children.Add(AssistantWindow.Pill("Save & apply wake word",()=>{try{ai.SaveWakeWord(new(){Enabled=enabled.IsChecked==true,Phrase=phrase.Text,RecognizerId=(recognizer.SelectedItem as WakeRecognizerChoice)?.Id??"",Confidence=threshold.Value});feedback.Text=ai.WakeStatus;Feedback("Wake word saved · "+(ai.Options.WakeWord.Enabled?"On":"Off")+". "+ai.WakeStatus);}catch(Exception ex){feedback.Text=ex.Message;Feedback("Wake word not saved · "+ex.Message,true);}},true));
        var pause=AssistantWindow.Pill("Resume wake word",ai.ToggleWakeListening);actions.Children.Add(pause);p.Children.Add(actions);p.Children.Add(feedback);
        var live=UI.Text("",11,UI.Muted);p.Children.Add(live);
        void Refresh(){live.Text=ai.WakeStatus;pause.Content=ai.WakeArmed?"Pause wake word":"Resume wake word";pause.IsEnabled=ai.Options.Enabled && ai.Options.WakeWord.Enabled;}
        bool subscribed=false;p.Loaded+=(_,_)=>{if(!subscribed){ai.Changed+=Refresh;subscribed=true;}Refresh();};p.Unloaded+=(_,_)=>{if(subscribed){ai.Changed-=Refresh;subscribed=false;}};
        p.Children.Add(UI.Text("Enabling this keeps the default microphone listening locally while NabyScape runs. After activation, voice uses your chosen provider. End conversation returns to wake listening when enabled. Stop, Hide, screen lock and sleep pause it; resume from Options or the tray. Wake word is off by default.",10,UI.Muted));Languages();Refresh();
        var expander=new Expander{Header="Wake word · hands-free voice",Content=p,Margin=new(0,12,0,14)};UI.Named(expander,"Wake word settings");return expander;
    }
    private FrameworkElement WidgetNavigationSettings()
    {
        var p=new StackPanel();var toggle=AssistantWindow.Pill("",()=>{});
        void Caption()=>toggle.Content=ai.Options.WidgetNavigation?"AI widget navigation on":"AI widget navigation off";
        toggle.Click+=(_,_)=>{try{ai.SetWidgetNavigation(!ai.Options.WidgetNavigation);Caption();Feedback("AI widget navigation saved · "+(ai.Options.WidgetNavigation?"On":"Off")+".");}catch(Exception ex){status.Text=ex.Message;}};Caption();p.Children.Add(toggle);
        p.Children.Add(UI.Text("Ask “Open Weather”, “Show my tasks” or “Next widget page”. A tool-capable chat or voice model can list enabled pages and open one. Navigation changes the visible page only; it does not read or edit widget contents or start activities.",11,UI.Muted));
        var hermes=new StackPanel();hermes.Children.Add(UI.Text("1 · Keep NabyScape running with AI and widget navigation enabled.\n2 · Merge the copied nabyscape entry under mcp_servers in your Hermes config.yaml. Keep your other server entries.\n3 · Restart Hermes or reload its MCP tools. Ask Hermes to use naby_list_widget_pages or naby_navigate_widget.",11,UI.Muted));
        var buttons=new WrapPanel();
        void Copy(bool wsl){try{string executable=WidgetMcpExecutable();if(wsl){if(executable.Length<3 || executable[1]!=':')throw new IOException("Use the Windows setup for this installation path.");executable="/mnt/"+char.ToLowerInvariant(executable[0])+executable[2..].Replace('\\','/');}string text="mcp_servers:\n  nabyscape:\n    command: "+JsonSerializer.Serialize(executable)+"\n    args: [\"--widget-mcp\"]\n";Clipboard.SetText(text);status.Text="Hermes MCP setup copied. Merge it into config.yaml, then restart Hermes.";}catch(Exception ex){status.Text=ex.Message;}}
        buttons.Children.Add(AssistantWindow.Pill("Copy Hermes setup",()=>Copy(false)));buttons.Children.Add(AssistantWindow.Pill("Copy WSL setup",()=>Copy(true)));buttons.Children.Add(AssistantWindow.Pill("Save MCP config…",()=>
        {
            try{var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save NabyScape MCP setup",FileName="nabyscape-mcp.json",Filter="JSON (*.json)|*.json",DefaultExt=".json",AddExtension=true};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(new{mcpServers=new{nabyscape=new{command=WidgetMcpExecutable(),args=new[]{"--widget-mcp"}}}},new JsonSerializerOptions{WriteIndented=true}));status.Text="MCP config saved. Import or merge it in your local MCP client.";}catch(Exception ex){status.Text=ex.Message;}
        }));hermes.Children.Add(buttons);hermes.Children.Add(UI.Text("The connector uses stdio and a pipe restricted to your Windows account. There is no listening network port. The Windows setup is for Hermes on this PC; the WSL setup requires Windows executable interop in WSL on the same PC.",10,UI.Muted));p.Children.Add(new Expander{Header="Connect Hermes & other MCP clients",Content=hermes,Margin=new(0,12,0,0)});
        return new Expander{Header="Widget navigation · AI tools",Content=p,Margin=new(0,14,0,8)};
    }
    private static string WidgetMcpExecutable()
    {string installed=Path.Combine(Installer.InstallDirectory,"NabyScape.exe");return File.Exists(installed)?installed:Environment.ProcessPath??throw new IOException("NabyScape executable path is unavailable.");}
}

internal sealed partial class AssistantRuntime
{
    internal void SetWidgetNavigation(bool value)
    {
        bool previous=Options.WidgetNavigation;Options.WidgetNavigation=value;
        try{app.Store.Save("assistant",Options);}catch{Options.WidgetNavigation=previous;throw;}
        StopSession();Conversation.Clear();Status=value?"AI widget navigation enabled. Start a new chat or voice conversation.":"AI widget navigation is off.";Notify();
    }
}
