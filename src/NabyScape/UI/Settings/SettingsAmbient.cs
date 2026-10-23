using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private readonly IslandSurface ambientPreview=new(){Fill=System.Windows.Media.Brushes.Transparent,Notch=false,IsHitTestVisible=false};
    private void InitializeAmbientPreview()
    {
        ambientPreview.SizeChanged+=(_,_)=>{ambientPreview.BeginUpdate();ambientPreview.SurfaceWidth=ambientPreview.ActualWidth;ambientPreview.SurfaceHeight=ambientPreview.ActualHeight;ambientPreview.EndUpdate();};
        IsVisibleChanged+=(_,_)=>RefreshAmbientPreview();StateChanged+=(_,_)=>RefreshAmbientPreview();previewStage.IsVisibleChanged+=(_,_)=>RefreshAmbientPreview();
        Closed+=(_,_)=>app.AmbientAudio.Remove(ambientPreview);
    }
    private void RefreshAmbientPreview()
    {
        var s=app.Settings;
        string widget=previewMode=="Closed"?"closed":previewContent switch{"Music"=>"media","Timer"=>"timer","Focus"=>"focus","Stopwatch"=>"stopwatch","System"=>"system","Note"=>"notes","Tasks"=>"tasks","Calendar"=>"calendar","Shelf"=>"files","Smart clipboard"=>"clips","AI"=>"ai","Launch"=>"shortcuts",_=>"preview"};
        bool allowed=AmbientPolicy.Includes(s,widget),moving=app.Media.Playing && s.LiveActivity && IsVisible && WindowState!=WindowState.Minimized && previewStage.IsVisible;
        ambientPreview.Radius=s.Radius;
        ambientPreview.SetAmbient(AmbientPolicy.ColorsFor(s,app.AmbientAudio.CoverColors),allowed?s.AmbientStrength:0,s.AmbientSpread,s.AmbientMotion,moving,AmbientLook.From(s));
        app.AmbientAudio.Set(ambientPreview,allowed && moving);
    }
    private FrameworkElement PremiumChoice(string name,string value,string[] values,Action<string> change)
    {
        var choice=new ComboBox{ItemsSource=values,SelectedItem=value,MinHeight=32,Margin=new(0,4,0,10)};UI.Named(choice,name);
        choice.SelectionChanged+=(_,_)=>{if(choice.SelectedItem is string selected){change(selected);Save();}};
        if(name.StartsWith("Quiet hours"))return choice;
        string label=name switch{"Ambient scope"=>"Where the light appears","Ambient color source"=>"Color source","Cover palette"=>"Album colors","Ambient audio response"=>"React to music","Ambient movement"=>"Light movement","Interaction feedback"=>"Hover & click feedback","Dock side"=>"Dock position",_=>name};
        var field=new StackPanel();var caption=UI.Text(label,11,UI.Muted);caption.Margin=new(0,4,0,0);field.Children.Add(caption);field.Children.Add(choice);return field;
    }
    private void AmbientSettings()
    {
        var s=app.Settings;var controls=new StackPanel();
        controls.Children.Add(Check("Ambient light",s.CoverAmbient,v=>s.CoverAmbient=v));
        controls.Children.Add(Check("Show ambience on closed dock",s.ClosedAmbient,v=>s.ClosedAmbient=v));
        var widgets=new StackPanel();
        foreach(var pair in Settings.Names.Where(p=>p.Key!="tools").Concat(new[]{new System.Collections.Generic.KeyValuePair<string,string>("ai","AI companion")}))
        {
            string id=pair.Key;
            widgets.Children.Add(Check(pair.Value+" ambient",s.AmbientWidgets.Contains(id),v=>{if(v){if(!s.AmbientWidgets.Contains(id))s.AmbientWidgets.Add(id);}else s.AmbientWidgets.Remove(id);}));
        }
        var selected=new Expander{Header="Choose widgets",Content=widgets,Margin=new(0,0,0,12),Visibility=s.AmbientScope=="Selected widgets"?Visibility.Visible:Visibility.Collapsed};
        controls.Children.Add(PremiumChoice("Ambient scope",s.AmbientScope,new[]{"All widgets","Music only","Selected widgets"},v=>{s.AmbientScope=v;selected.Visibility=v=="Selected widgets"?Visibility.Visible:Visibility.Collapsed;}));controls.Children.Add(selected);
        var custom=new StackPanel{Visibility=s.AmbientColorSource.StartsWith("Custom")?Visibility.Visible:Visibility.Collapsed};
        ColorControl(custom,"Ambient custom color",s.AmbientCustomColor,v=>s.AmbientCustomColor=v,new[]{"#7AA8DC","#D9A46D","#B4A0DC","#73BFA6"});
        var secondColor=new StackPanel{Visibility=s.AmbientColorSource=="Custom blend"?Visibility.Visible:Visibility.Collapsed};
        ColorControl(secondColor,"Second ambient color",s.AmbientSecondaryColor,v=>s.AmbientSecondaryColor=v,new[]{"#CB72D9","#50BBD7","#E6AF62","#76C18E"});custom.Children.Add(secondColor);
        controls.Children.Add(PremiumChoice("Ambient color source",s.AmbientColorSource,new[]{"Album cover","Accent color","Custom color","Custom blend"},v=>{s.AmbientColorSource=v;custom.Visibility=v.StartsWith("Custom")?Visibility.Visible:Visibility.Collapsed;secondColor.Visibility=v=="Custom blend"?Visibility.Visible:Visibility.Collapsed;}));controls.Children.Add(custom);
        controls.Children.Add(UI.Text("Album color rises from the bottom and fades into the dark surface. Without artwork, your accent color is used.",11,UI.Muted));
        AddSlider(controls,"Ambient intensity",s.AmbientStrength*100,0,100,v=>s.AmbientStrength=v/100,"%",1);
        controls.Children.Add(PremiumChoice("Cover palette",s.AmbientPalette,new[]{"Single color","Cover blend"},v=>s.AmbientPalette=v));
        var fine=new StackPanel();
        fine.Children.Add(PremiumChoice("Light direction",s.AmbientDirection,new[]{"Bottom","Top","Left","Right","Surround"},v=>s.AmbientDirection=v));
        AddSlider(fine,"Color balance",s.AmbientBalance*100,0,100,v=>s.AmbientBalance=v/100,"%",1);
        AddSlider(fine,"Light reach",s.AmbientHeight*100,25,200,v=>s.AmbientHeight=v/100,"%",1);
        AddSlider(fine,"Ambient softness",s.AmbientSoftness*100,0,100,v=>s.AmbientSoftness=v/100,"%",1);
        fine.Children.Add(UI.Text("Single color uses the cover's dominant hue. Cover blend mixes two album colors. Custom blend uses both chosen colors. Spread and height can extend light across the whole panel; closed-dock light has its own switch. Idle and tucked docks always stay black.",11,UI.Muted));
        AddSlider(fine,"Ambient spread",s.AmbientSpread*100,25,250,v=>s.AmbientSpread=v/100,"%",1);
        controls.Children.Add(Advanced("Fine-tune light",fine));
        controls.Children.Add(PremiumChoice("Ambient audio response",s.AmbientAudioResponse,new[]{"Off","Soft","Responsive","Punchy"},v=>s.AmbientAudioResponse=v));
        AddSlider(controls,"Ambient sensitivity",s.AmbientSensitivity*100,50,300,v=>s.AmbientSensitivity=v/100,"%",5);
        controls.Children.Add(UI.Text("Light follows the selected app's measured volume. Soft fades gently; Responsive follows transients; Punchy reacts faster. Sensitivity helps quiet tracks. Your microphone is never used.",11,UI.Muted));
        controls.Children.Add(PremiumChoice("Ambient movement",s.AmbientMotion,new[]{"Still","Breathe","Drift"},v=>s.AmbientMotion=v));
        controls.Children.Add(UI.Text("Still keeps the glow in place. Breathe and Drift add gentle motion during playback. Audio response works with all three. Motion stops when paused, hidden, tucked, or Reduce motion is on; Live activity also controls audio response.",11,UI.Muted));
        page.Children.Add(Section("Ambient light",controls));
    }
    private void NotificationPresentationSettings()
    {
        var s=app.Settings;var controls=new StackPanel();
        controls.Children.Add(PremiumChoice("Notification animation",s.NotificationAnimation,new[]{"Lift","Fade","Soft scale"},v=>s.NotificationAnimation=v));
        AddSlider(controls,"Notification lifetime",s.NotificationSeconds,4,30,v=>s.NotificationSeconds=v," s",1);
        controls.Children.Add(UI.Text("Compact indicators expire after this time. Open details and active interactions keep the current notification available.",11,UI.Muted));
        controls.Children.Add(UI.Button("Preview notch notification",()=>{app.Notifications.Post(new Settings{NotificationDelivery="Notch"},new("preview","A little update, right here","This is a preview. Your files and running sessions stay as they are."),false,_=>{},DateTimeOffset.UtcNow);app.OpenNotifications();}));
        controls.Children.Add(Check("Scheduled quiet hours",s.QuietHours,v=>s.QuietHours=v));
        string[] hours=Enumerable.Range(0,24).Select(h=>h.ToString("00")+":00").ToArray();
        var schedule=new Grid();schedule.ColumnDefinitions.Add(new());schedule.ColumnDefinitions.Add(new());
        var from=new StackPanel{Margin=new(0,0,6,0)};from.Children.Add(UI.Text("From · local time",11,UI.Muted));from.Children.Add(PremiumChoice("Quiet hours start",hours[s.QuietStart],hours,v=>s.QuietStart=Array.IndexOf(hours,v)));
        var to=new StackPanel{Margin=new(6,0,0,0)};to.Children.Add(UI.Text("Until",11,UI.Muted));to.Children.Add(PremiumChoice("Quiet hours end",hours[s.QuietEnd],hours,v=>s.QuietEnd=Array.IndexOf(hours,v)));Grid.SetColumn(to,1);schedule.Children.Add(from);schedule.Children.Add(to);controls.Children.Add(schedule);
        controls.Children.Add(UI.Text("Quiet hours silence Naby notifications and completion sounds. A matching start and end disables the schedule. Windows notifications from other apps are unaffected.",11,UI.Muted));
        var snoozeStatus=UI.Text("",11,UI.Muted);void Status()=>snoozeStatus.Text=s.NotificationsSnoozedUntil is {} until && until>DateTimeOffset.UtcNow?"Snoozed until "+until.ToLocalTime().ToString("HH:mm"):"Notifications are not snoozed.";Status();controls.Children.Add(snoozeStatus);
        var actions=new WrapPanel();
        foreach(int minutes in new[]{15,30,60})actions.Children.Add(UI.Button("Snooze "+minutes+" min",()=>{s.NotificationsSnoozedUntil=DateTimeOffset.UtcNow.AddMinutes(minutes);app.Notifications.Clear();Save();Status();}));
        actions.Children.Add(UI.Button("Resume notifications",()=>{s.NotificationsSnoozedUntil=null;Save();Status();}));controls.Children.Add(actions);
        controls.Children.Add(UI.Text("Snooze ends automatically. It does not stop Timer or Focus. Resume removes the snooze; any scheduled quiet hours still apply.",11,UI.Muted));
        page.Children.Add(Section("Notification presentation",controls));
    }
}
