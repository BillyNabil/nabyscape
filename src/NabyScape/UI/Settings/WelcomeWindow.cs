using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal sealed class WelcomeWindow : Window
{
    private readonly App app;
    private readonly StackPanel body=new(){Margin=new(28,20,28,20)};
    private readonly TextBlock progress=UI.Text("",12,UI.Muted);
    private readonly TextBlock error=UI.Text("",12,UI.Muted);
    private readonly Button back,next;
    private readonly Dictionary<string,bool> widgets;
    private readonly HashSet<string> changedWidgets=new();
    private bool edgeChanged;
    private string edge;
    private int step;
    private string purpose="Custom";

    internal WelcomeWindow(App app)
    {
        this.app=app;edge=app.Settings.DockEdge;
        widgets=app.Settings.Widgets.ToDictionary(w=>w.Id,w=>w.Enabled);
        Title="Welcome to NabyScape";Width=580;Height=620;MinWidth=420;MinHeight=360;
        MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=Accessibility.HighContrast?SystemColors.WindowBrush:UI.Brush("#111214");
        Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#F3F4F6");
        FontFamily=new FontFamily("Segoe UI Variable, Segoe UI");FontSize=13;
        var layout=new Grid();layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var scroll=UI.Scroll(body);layout.Children.Add(scroll);
        var footer=new WrapPanel{Margin=new(24,12,24,20)};
        var skip=UI.CaptionButton("Skip setup",Skip);footer.Children.Add(skip);
        back=UI.CaptionButton("Back",()=>{step--;Render();});next=UI.CaptionButton("Next",()=>{if(step<2){step++;Render();}else Finish(purpose=="AI");},true);
        footer.Children.Add(back);footer.Children.Add(next);Grid.SetRow(footer,1);layout.Children.Add(footer);
        Content=layout;Render();
    }

    private void Render()
    {
        body.Children.Clear();progress.Text=$"GET STARTED  /  {step+1} OF 3";body.Children.Add(progress);
        back.Visibility=step==0?Visibility.Collapsed:Visibility.Visible;
        next.Content=step==2?(purpose=="AI"?"Continue to AI setup":"Finish setup"):"Next";
        body.Children.Add(UI.Text(step==0?"What brings you here?":step==1?"Make it yours.":"Your dock is ready.",28));
        if(step==0)
        {
            body.Children.Add(UI.Text("Choose a starting point. You can change every choice later.",13,UI.Muted));
            foreach(var choice in new[]{("Music","Playback, volume, clock and weather"),("Productivity","Focus, timer, notes, tasks and calendar"),("AI","Naby voice, clock and notes · connect your provider next"),("Custom","Recommended NabyScape setup · keep the complete default experience")})
            {
                var content=new StackPanel();content.Children.Add(UI.Text((choice.Item1=="Custom"?"Recommended":choice.Item1)+(purpose==choice.Item1?" · Selected":""),16));var hint=UI.Text(choice.Item2,12,UI.Muted);hint.Margin=new(0,5,0,0);content.Children.Add(hint);
                var button=UI.CaptionButton(choice.Item1,()=>{purpose=choice.Item1;ChoosePurpose();Render();});button.Content=content;button.Padding=new(16);button.Margin=new(0,10,0,0);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Background=UI.Brush(purpose==choice.Item1?app.Settings.SelectionFill:app.Settings.ControlFill);body.Children.Add(button);
            }
            body.Children.Add(UI.Text("Choose a purpose to change widgets, or keep Recommended to use the complete default setup. Microphone listening starts only when you choose it.",12,UI.Muted));
        }
        else if(step==1)
        {
            body.Children.Add(UI.Text("Dock position",16));var edges=new WrapPanel();
            foreach(string side in new[]{"Top","Bottom","Left","Right"})
            {
                var option=new RadioButton{Content=side,GroupName="Dock position",IsChecked=edge==side,Margin=new(0,10,16,14),Padding=new(6)};
                UI.Named(option,"Dock position: "+side);option.Checked+=(_,_)=>{edge=side;edgeChanged=true;};edges.Children.Add(option);
            }
            body.Children.Add(edges);body.Children.Add(UI.Text("Your widgets",16));
            foreach(var choice in app.Settings.Widgets.Where(w=>purpose=="Custom" || widgets[w.Id]))
            {
                var id=choice.Id;var toggle=new CheckBox{Content=Settings.Names.GetValueOrDefault(id)??id,IsChecked=widgets[id],Margin=new(0,12,0,3),Padding=new(6)};
                UI.Named(toggle,"Enable "+id);toggle.Click+=(_,_)=>{widgets[id]=toggle.IsChecked==true;changedWidgets.Add(id);};body.Children.Add(toggle);
            }
            body.Children.Add(UI.Text("All widgets remain available in Settings. Weather asks for a city; AI asks for a connection.",12,UI.Muted));
        }
        else
        {
            body.Children.Add(UI.Text("Click the dock to open your widgets. Hover for a quick look. Right-click the tray icon to open Settings.",14,UI.Muted));
            body.Children.Add(UI.Text(string.Join(" · ",widgets.Where(w=>w.Value).Select(w=>Settings.Names.GetValueOrDefault(w.Key)??w.Key)),13));
            if(purpose!="AI")body.Children.Add(UI.Card("AI is optional",UI.Text("Connect your own provider or Hermes whenever you're ready. Voice starts only when you choose it.",13,UI.Muted),UI.CaptionButton("Set up AI",()=>Finish(true))));
            body.Children.Add(UI.Text("Clipboard history: "+(app.Settings.ClipboardHistory?"On":"Off")+" · Download alerts: "+(app.Settings.DownloadsEnabled?"On":"Off")+". Change these in Settings at any time.",12,UI.Muted));
            body.Children.Add(UI.Text("You can reopen this guide from Settings > General.",12,UI.Muted));
        }
        body.Children.Add(error);
    }

    private void ChoosePurpose()
    {
        if(purpose=="Custom"){foreach(var widget in app.Settings.Widgets)widgets[widget.Id]=widget.Enabled;changedWidgets.Clear();return;}
        string[] enabled=purpose switch
        {
            "Music"=>new[]{"media","volume","clock","weather"},
            "Productivity"=>new[]{"timer","focus","clock","notes","tasks","calendar"},
            _=>new[]{"voice","clock","notes"}
        };
        foreach(string id in widgets.Keys.ToArray()){widgets[id]=enabled.Contains(id);changedWidgets.Add(id);}
    }
    private bool MarkComplete()
    {
        try{WelcomeSetup.Complete(app.Store);return true;}
        catch(Exception ex){app.Store.Log(ex);error.Text="Could not save setup. Check your settings folder and try again.";return false;}
    }

    private void Skip(){if(MarkComplete())Close();}

    private void Finish(bool setupAi)
    {
        // Normalize a detached copy so a failed write cannot change live preferences.
        Settings candidate;
        try
        {
            candidate=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(app.Settings))!;
            if(edgeChanged)candidate.DockEdge=edge;
            foreach(var widget in candidate.Widgets)if(changedWidgets.Contains(widget.Id))widget.Enabled=widgets[widget.Id];
            if(purpose!="Custom")candidate.HoverFollowsWidgets=true;
            candidate.Normalize();app.Store.Save("settings",candidate);
        }
        catch(Exception ex)
        {
            app.Store.Log(ex);error.Text="Could not save your choices. Your previous settings are unchanged.";return;
        }
        app.Settings.DockEdge=candidate.DockEdge;app.Settings.HoverFollowsWidgets=candidate.HoverFollowsWidgets;
        // Open Settings controls retain these objects; update their saved values in place.
        foreach(var widget in app.Settings.Widgets)if(candidate.Widgets.FirstOrDefault(w=>w.Id==widget.Id) is {} savedWidget)widget.Enabled=savedWidget.Enabled;
        foreach(var tool in app.Settings.Tools)if(candidate.Tools.FirstOrDefault(t=>t.Id==tool.Id) is {} savedTool)tool.Enabled=savedTool.Enabled;
        app.ChangeSettings();if(!MarkComplete())return;Close();if(setupAi)app.ShowAssistantProviders();
    }
}
