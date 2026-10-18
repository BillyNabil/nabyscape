using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;

namespace NabyScape;

internal sealed class AssistantWindow : Window
{
    private readonly App app;
    private readonly AssistantRuntime ai;
    private readonly AssistantOrb orb=new();
    private readonly TextBlock status=UI.Text("",11,UI.Muted);
    private readonly StackPanel messages=new();
    private readonly ScrollViewer transcript;
    private readonly TextBox input=UI.Input("Message AI companion","");
    private readonly ComboBox provider=new(){MinHeight=32};
    private readonly ComboBox display=new(){MinHeight=30};
    private readonly Button send,voice,share,actions,computer;
    private readonly Border approval;
    private readonly TextBlock approvalTitle=UI.Text("",12),approvalText=UI.Text("",11,UI.Muted);
    private readonly Expander screenPreview=new(){Header="Shared display preview",IsExpanded=false};
    private readonly Image screenImage=new(){MaxHeight=130,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly DispatcherTimer screenTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private bool syncing,frameBusy,closed;
    private int rendered;
    private AssistantMessage? firstRendered;
    private string providerSignature="",lastFrame="";
    internal AssistantWindow(App app)
    {
        this.app=app;ai=app.Assistant;Title="NabyScape · Companion";
        AssistantWindowChrome.Place(this,app.Settings,480,690);
        var page=new StackPanel{Margin=new(20,14,20,16)};
        var heading=new DockPanel();var title=new StackPanel{Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};title.Children.Add(UI.Heading("Companion",20));title.Children.Add(UI.Text("Chat, talk, or work together.",11,UI.Muted));heading.Children.Add(orb);heading.Children.Add(title);page.Children.Add(heading);
        var pick=new Grid{Margin=new(0,12,0,8)};pick.ColumnDefinitions.Add(new());pick.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pick.Children.Add(provider);
        var configure=Pill("Providers",app.ShowAssistantProviders);configure.Margin=new(8,0,0,0);Grid.SetColumn(configure,1);pick.Children.Add(configure);page.Children.Add(pick);
        provider.SelectionChanged+=(_,_)=>{if(!syncing && provider.SelectedItem is AssistantProvider p && p.Id!=ai.Options.SelectedProvider)ai.SelectProvider(p.Id);};
        status.Margin=new(0,0,0,10);page.Children.Add(status);
        transcript=new ScrollViewer{Content=messages,Height=260,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};UI.Named(transcript,"AI conversation");page.Children.Add(transcript);
        var review=new StackPanel();approvalTitle.FontWeight=FontWeights.SemiBold;review.Children.Add(approvalTitle);approvalText.Margin=new(0,6,0,10);review.Children.Add(new ScrollViewer{Content=approvalText,MaxHeight=95,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});review.Children.Add(UI.Row(Pill("Run once",()=>ai.Decide(true),true),Pill("Decline",()=>ai.Decide(false))));
        approval=new Border{Background=UI.Brush("#202328"),BorderBrush=UI.Brush("#515661"),BorderThickness=new(1),CornerRadius=new(14),Padding=new(12),Margin=new(0,8,0,8),Child=review,Visibility=Visibility.Collapsed};page.Children.Add(approval);
        input.AcceptsReturn=true;input.TextWrapping=TextWrapping.Wrap;input.MinHeight=62;input.MaxHeight=120;input.Margin=new(0,10,0,8);input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;input.ToolTip="Enter to send · Shift+Enter for a new line · Escape to stop";page.Children.Add(input);
        AddHandler(Keyboard.KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==Key.Escape){ai.StopSession();e.Handled=true;}}),true);
        input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){e.Handled=true;_=Send();}};
        send=Pill("Send",()=>_=Send(),true);voice=Pill("Start voice",()=>_=ToggleVoice());share=Pill("Share screen",()=>_=ToggleScreen());
        var bar=new WrapPanel();foreach(var b in new[]{send,voice,share,Pill("Stop",ai.StopSession),Pill("Clear",ai.Clear)})bar.Children.Add(b);page.Children.Add(bar);
        var permissions=new StackPanel();var options=new WrapPanel();actions=Pill("Actions off",()=>ai.SetActions(!ai.ActionsAllowed));computer=Pill("Computer off",()=>{try{ai.SetComputerControl(!ai.Desktop.ControlEnabled);}catch(Exception ex){status.Text=ex.Message;}});options.Children.Add(actions);options.Children.Add(computer);permissions.Children.Add(options);
        display.ItemsSource=ai.Desktop.Targets();display.SelectedIndex=0;UI.Named(display,"Display to share");permissions.Children.Add(display);permissions.Children.Add(UI.Text("Actions require Run once. Screen sharing uses only this display. Voice uses Windows default input/output. Closing or minimizing stops the live session.",11,UI.Muted));page.Children.Add(new Expander{Header="Session controls",Content=permissions,Margin=new(0,8,0,0)});
        screenPreview.Content=screenImage;screenPreview.Margin=new(0,8,0,0);screenPreview.Expanded+=(_,_)=>RefreshScreen();page.Children.Add(screenPreview);
        var outer=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Content=AssistantWindowChrome.Frame(this,"NabyScape · AI companion",outer);
        SizeChanged+=(_,_)=>{transcript.Height=Math.Max(72,ActualHeight-(ai.Pending!=null?510:380));input.MinHeight=ActualHeight<420?36:62;};
        ai.Changed+=Refresh;ai.VisualChanged+=RefreshOrb;app.SettingsChanged+=RefreshOrb;
        screenTimer.Tick+=async(_,_)=>{if(frameBusy || !ai.VoiceConnected || !ai.Desktop.Sharing)return;frameBusy=true;try{await ai.ShareVoiceFrame();}finally{frameBusy=false;}};
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)ai.StopSession();};IsVisibleChanged+=(_,_)=>{if(!IsVisible)ai.StopSession();};
        Closed+=(_,_)=>{closed=true;screenTimer.Stop();ai.Changed-=Refresh;ai.VisualChanged-=RefreshOrb;app.SettingsChanged-=RefreshOrb;ai.StopSession();screenImage.Source=null;};
        Refresh();
    }
    internal static Button Pill(string text,Action action,bool primary=false)
    {var button=UI.Button(text,action,primary);button.Tag=new CornerRadius(16);button.Padding=new(12,6,12,6);button.MinHeight=31;button.FontSize=11;button.Margin=new(0,0,6,6);return button;}
    private async System.Threading.Tasks.Task Send()
    {string text=input.Text.Trim();if(text.Length==0 || ai.Busy)return;input.Clear();bool success=await ai.Send(text);if(!closed && !success && !ai.LastSendHadActionResults && input.Text.Length==0)input.Text=text;}
    private async System.Threading.Tasks.Task ToggleVoice()
    {if(ai.VoiceConnected)ai.StopSession();else await ai.StartVoice();}
    private async System.Threading.Tasks.Task ToggleScreen()
    {
        if(ai.Desktop.Sharing){ai.StopSession();return;}
        try
        {
            string? selectedId=(display.SelectedItem as AssistantScreenTarget)?.Id;
            display.ItemsSource=ai.Desktop.Targets();display.SelectedItem=display.Items.Cast<AssistantScreenTarget>().FirstOrDefault(t=>t.Id==selectedId);if(display.SelectedIndex<0)display.SelectedIndex=0;
            if(display.SelectedItem is not AssistantScreenTarget target)throw new InvalidOperationException("No display is available.");
            ai.StartSharing(target.Id,new WindowInteropHelper(this).Handle);await ai.Desktop.Capture(System.Threading.CancellationToken.None);screenPreview.IsExpanded=true;Refresh();
        }
        catch(Exception ex){ai.StopSession();status.Text=ex.Message;}
    }
    private void RefreshOrb(){orb.State=ai.State;orb.Level=ai.Level;orb.ReduceMotion=app.Settings.ReduceMotion;}
    private void Refresh()
    {
        if(closed)return;syncing=true;
        try
        {
            string signature=string.Join("|",ai.Options.Providers.Select(p=>p.Id+":"+p.Name));
            if(signature!=providerSignature || !provider.Items.Cast<AssistantProvider>().SequenceEqual(ai.Options.Providers)){providerSignature=signature;provider.ItemsSource=ai.Options.Providers.ToArray();}
            provider.SelectedItem=ai.Provider;provider.IsEnabled=!ai.Busy && !ai.VoiceConnected;
            status.Text=(ai.Options.Enabled?"":"Companion is off · ")+ai.Status;
            send.IsEnabled=ai.Options.Enabled && !ai.Busy;voice.IsEnabled=ai.Options.Enabled && !ai.Busy;share.IsEnabled=ai.Options.Enabled && (!ai.Busy || ai.Desktop.Sharing);
            voice.Content=ai.VoiceConnected?"End voice":"Start voice";share.Content=ai.Desktop.Sharing?"Stop sharing":"Share screen";actions.Content=ai.ActionsAllowed?"Actions on":"Actions off";computer.Content=ai.Desktop.ControlEnabled?"Computer on":"Computer off";
            screenPreview.Visibility=ai.Desktop.Sharing?Visibility.Visible:Visibility.Collapsed;computer.IsEnabled=ai.Desktop.Sharing;actions.IsEnabled=ai.Options.Enabled;
            display.IsEnabled=!ai.Desktop.Sharing;
            if(ai.VoiceConnected && ai.Desktop.Sharing && IsVisible){if(!screenTimer.IsEnabled)screenTimer.Start();}else screenTimer.Stop();
            approval.Visibility=ai.Pending!=null?Visibility.Visible:Visibility.Collapsed;
            if(ai.Pending is { } pending){approvalTitle.Text=pending.Name=="computer"?"Review computer action":"Review NabyScape action";approvalText.Text=pending.Arguments;}
            bool bottom=transcript.ScrollableHeight-transcript.VerticalOffset<30;
            var history=ai.Conversation.Messages;
            if(history.Count<rendered || rendered>0 && !ReferenceEquals(firstRendered,history.FirstOrDefault())){messages.Children.Clear();rendered=0;}
            if(history.Count==0 && messages.Children.Count==0)
            {var welcome=UI.Text("A little help, right here.\n\nAdd a provider, then type a message. Start voice when you want to talk. Screen context and actions are yours to enable for each session.",13,UI.Muted);welcome.Margin=new(2,18,2,24);messages.Children.Add(welcome);}
            if(history.Count>0 && rendered==0)messages.Children.Clear();
            for(int i=rendered;i<history.Count;i++)
            {
                var message=history[i];if(message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Text) || message.Text.StartsWith("Updated shared screen after"))continue;
                var text=UI.Text(message.Text,13);text.Margin=new(12,9,12,9);
                var bubble=new Border{Child=text,CornerRadius=new(13),Background=message.Role=="user"?UI.Brush("#24272D"):Brushes.Transparent,Margin=new(message.Role=="user"?32:0,4,message.Role=="user"?0:20,5)};messages.Children.Add(bubble);
                if(!app.Settings.ReduceMotion)Motion.Fade(bubble,1,180);
            }
            rendered=history.Count;firstRendered=history.FirstOrDefault();if(bottom)Dispatcher.BeginInvoke(()=>transcript.ScrollToEnd());
            transcript.Height=Math.Max(72,ActualHeight-(ai.Pending!=null?510:380));RefreshOrb();RefreshScreen();
        }
        finally{syncing=false;}
    }
    private void RefreshScreen()
    {
        string? data=ai.Desktop.Frame?.DataUrl;
        if(!ai.Desktop.Sharing){screenImage.Source=null;lastFrame="";return;}
        if(!screenPreview.IsExpanded || data==null || data==lastFrame)return;lastFrame=data;
        try{var bytes=Convert.FromBase64String(data[(data.IndexOf(',')+1)..]);using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=360;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();screenImage.Source=bitmap;}catch{screenImage.Source=null;}
    }
}
