using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private readonly List<Action> homeStatusUpdates=new();
    private void InitializeHomeStatuses()
    {
        app.Updated+=HomeStatusChanged;app.Assistant.Changed+=RefreshHomeStatuses;app.Assistant.Speech.Changed+=RefreshHomeStatuses;app.Lyrics.Changed+=RefreshHomeStatuses;
        Closed+=(_,_)=>{app.Updated-=HomeStatusChanged;app.Assistant.Changed-=RefreshHomeStatuses;app.Assistant.Speech.Changed-=RefreshHomeStatuses;app.Lyrics.Changed-=RefreshHomeStatuses;homeStatusUpdates.Clear();};
    }
    private void HomeStatusChanged(){if((app.CurrentUpdate&(RuntimeChange.Media|RuntimeChange.Settings))!=0)RefreshHomeStatuses();}
    private void RefreshHomeStatuses()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshHomeStatuses));return;}
        if(current!="Home" || !IsVisible)return;foreach(var update in homeStatusUpdates.ToArray())update();
    }
    private void HomeFeatureStatuses()
    {
        homeStatusUpdates.Clear();var group=new StackPanel();
        void Row(string title,Func<(string State,string Detail,string Action)> assess,Action open)
        {
            var row=new DockPanel{Margin=new(0,0,0,16)};var action=UI.CaptionButton("Open",open);action.MinWidth=116;action.VerticalAlignment=VerticalAlignment.Center;action.Margin=new(16,0,0,0);DockPanel.SetDock(action,Dock.Right);row.Children.Add(action);
            var text=new StackPanel();var state=UI.Text("",14);state.FontWeight=FontWeights.SemiBold;text.Children.Add(state);var detail=UI.Text("",12,UI.Muted);detail.Margin=new(0,5,0,0);text.Children.Add(detail);row.Children.Add(text);group.Children.Add(row);
            void Update(){var value=assess();Motion.Text(state,title+" · "+value.State);Motion.Text(detail,value.Detail);action.Content=Localization.Text(value.Action);UI.Named(action,value.Action);}
            homeStatusUpdates.Add(Update);Update();
        }
        Row("Music",()=>!app.Settings.MediaIntegrationEnabled?("Off","Music apps are disconnected. Playback in your music app is unaffected.","Enable music"):
            !app.Settings.Widgets.Any(w=>w.Enabled && w.Id=="media")?("Hidden","Enable the music widget to show playback controls.","Choose widgets"):
            !app.Media.Available?("Waiting for music","Play a song in your music app, then select its media session.","Music settings"):("Ready","Playback controls use the selected music session.","Music settings"),()=>
            {if(!app.Settings.MediaIntegrationEnabled){app.Settings.MediaIntegrationEnabled=true;app.ChangeSettings();}else Navigate(app.Settings.Widgets.Any(w=>w.Enabled && w.Id=="media")?"Audio":"Content");});
        Row("AI chat",()=>!app.Assistant.Options.Enabled?("Off","AI is optional. Connect a provider when you are ready.","Set up AI"):
            !app.Assistant.Setup.Chat.Configured?("Needs setup","Choose a provider or connect Hermes, then save your connection.","Connect AI"):
            app.Assistant.Setup.Chat.Title.Contains("attention",StringComparison.OrdinalIgnoreCase)?("Needs attention","The last connection check failed. Review the connection and try again.","Review connection"):
            !app.Assistant.Setup.Chat.Verified?("Saved · unchecked","Your connection is saved. Check it before starting a conversation.","Review connection"):("Ready","The selected chat connection has completed a reply.","Open chat"),()=>{if(app.Assistant.Options.Enabled && app.Assistant.Setup.Chat.Verified)app.ShowAssistant();else if(!app.Assistant.Options.Enabled)OpenAiSettings();else{Navigate("AI");aiSettings?.OpenChatSetup();}});
        Row("Voice",()=>app.Assistant.VoiceConnected?("Microphone on","An active voice conversation is using your microphone.","Voice settings"):
            !app.Assistant.Options.Enabled?("Off","Voice starts only after you choose to enable and configure AI.","Set up voice"):
            app.Assistant.Speech.Busy?("Downloading","Speech components are being prepared. Progress is in Voice settings.","View progress"):
            app.Assistant.Options.LocalVoice is {Enabled:true,Managed:true,OwnModels:false} && app.Assistant.Setup.Chat.Configured && !app.Assistant.Setup.Voice.Configured?("Model download required","Install the selected listening and speaking pack in Voice setup.","Install models"):
            !app.Assistant.Setup.Voice.Configured?("Needs setup","Choose a voice connection or install your listening and speaking models.","Set up voice"):
            ("Configured · unchecked","Your setup is saved. Check audio or start a conversation to verify it.",app.Assistant.Options.LocalVoice.Enabled?"Check audio":"Open voice"),()=>
            {
                if(!app.Assistant.Options.Enabled){OpenAiSettings();return;}
                if(app.Assistant.Setup.Voice.Configured && !app.Assistant.VoiceConnected && !app.Assistant.Speech.Busy)
                {
                    if(!app.Assistant.Options.LocalVoice.Enabled){app.OpenVoice();return;}
                    OpenAiVoiceSettings();Dispatcher.BeginInvoke(new Action(()=>aiSettings?.OpenVoiceChecks()));
                }
                else OpenAiVoiceSettings();
            });
        Row("Lyrics",()=>!app.Settings.LyricsEnabled && app.Settings.ClosedMediaContent!="Lyrics"?("Off","Enable panel lyrics or choose Live lyrics for the closed dock.","Choose lyrics"):
            !app.Media.Available?("Waiting for music","Play a song to look up timed lyrics.","Music settings"):
            !LyricTiming.CanUse(app.Media)?("Unavailable","This player has not provided enough track details or playback timing.","Music settings"):
            app.Lyrics.Result.Lines.Length>0?("Ready","Timed lyrics are available for the selected track.","Lyrics settings"):
            app.Lyrics.Result.Status.StartsWith("Finding",StringComparison.Ordinal)?("Finding lyrics","Looking up timed lyrics for this song.","Lyrics settings"):
            app.Lyrics.Result.Status.StartsWith("No synced",StringComparison.Ordinal) || app.Lyrics.Result.Status.StartsWith("Instrumental",StringComparison.Ordinal)?("Unavailable for this song","No timed lyrics were found. The closed dock uses the song title.","Lyrics settings"):
            app.Lyrics.Result.Status.Contains("unavailable",StringComparison.OrdinalIgnoreCase) || app.Lyrics.Result.Status.Contains("timed out",StringComparison.OrdinalIgnoreCase)?("Needs attention","Lyrics lookup could not complete. Open the lyrics panel to retry.","Lyrics settings"):
            ("Waiting for lyrics","Show the lyrics view to look up this song. Some songs have no timed lyrics.","Lyrics settings"),()=>Navigate("Audio"));
        Row("Weather",()=>WeatherService.Key(app.Settings).Length==0?("Needs a city","Choose your city to see the forecast.","Choose city"):
            app.Weather.Cached?.Key==WeatherService.Key(app.Settings)?("Forecast saved","Open Weather to view the forecast and its update time.","Open weather"):
            ("Needs a forecast","Your city is selected. Open Weather to load its forecast.","Open weather"),()=>Navigate("Weather"));
        page.Children.Add(Section("Your setup",group));
        page.Children.Add(UI.CaptionButton("Run quick setup again",app.ShowWelcome));
    }
}
