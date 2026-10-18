using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace NabyScape;

// A view has no session lifetime of its own: collapsing the island can keep
// voice running. The runtime owns the conversation, permissions and capture.
internal sealed class AssistantView : UserControl,IDisposable
{
    private readonly App app;
    private readonly AssistantRuntime ai;
    private readonly bool island;
    private readonly AssistantOrb orb=new();
    private readonly TextBlock status=UI.Text("",11,UI.Muted);
    private readonly StackPanel messages=new();
    private readonly ScrollViewer transcript;
    private readonly TextBox input=UI.Input("Message AI companion","");
    private readonly ComboBox provider=new(){MinHeight=32};
    private readonly TextBlock providerHint=UI.Text("Choose a provider",11,UI.Muted);
    private readonly ComboBox display=new(){MinHeight=30};
    private readonly Button send,voice,share,actions,computer;
    private readonly Button? closeButton;
    private readonly Border approval;
    private readonly TextBlock approvalTitle=UI.Text("",12),approvalText=UI.Text("",11,UI.Muted);
    private readonly Expander screenPreview=new(){Header="Shared display preview",IsExpanded=false};
    private readonly Image screenImage=new(){MaxHeight=130,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};
    private Window? hostWindow;
    private ScrollViewer? hostViewport;
    private bool syncing,subscribed,focusRequested,restoreScroll,followLatest,disposed;
    private double savedScrollOffset;
    private int rendered;
    private AssistantMessage? firstRendered;
    private string providerSignature="",lastFrame="";
    private string? reviewedId;
    internal TextBox Editor=>input;
    internal AssistantView(App app,bool island=false,Action? close=null)
    {
        this.app=app;ai=app.Assistant;this.island=island;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Features/Assistant/AssistantTheme.xaml",UriKind.Relative)});
        Foreground=Brushes.White;UseLayoutRounding=true;
        var page=new StackPanel{Margin=island?new Thickness(8,0,8,8):new Thickness(20,14,20,16)};
        var heading=new Grid();heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        if(island)orb.Width=orb.Height=72;
        heading.Children.Add(orb);var title=new StackPanel{Margin=new(island?8:12,0,0,0),VerticalAlignment=VerticalAlignment.Center};var titleText=UI.Heading("Companion",island?18:20);titleText.TextWrapping=TextWrapping.NoWrap;titleText.TextTrimming=TextTrimming.CharacterEllipsis;title.Children.Add(titleText);title.Children.Add(UI.Text(island?"Right here with you.":"Chat, talk, or work together.",11,UI.Muted));Grid.SetColumn(title,1);heading.Children.Add(title);
        if(island && close!=null){closeButton=Pill("Back to island",close);closeButton.ToolTip="Back to island";closeButton.Margin=new(6,0,0,0);closeButton.VerticalAlignment=VerticalAlignment.Center;closeButton.Padding=new(9,5,9,5);Grid.SetColumn(closeButton,2);heading.Children.Add(closeButton);}page.Children.Add(heading);
        var pick=new Grid{Margin=new(0,island?4:12,0,island?5:8)};pick.ColumnDefinitions.Add(new());pick.ColumnDefinitions.Add(new(){Width=GridLength.Auto});pick.Children.Add(provider);UI.Named(provider,"AI provider");
        providerHint.Margin=new(12,0,28,0);providerHint.VerticalAlignment=VerticalAlignment.Center;providerHint.IsHitTestVisible=false;pick.Children.Add(providerHint);
        var configure=Pill("Providers",app.ShowAssistantProviders);configure.Margin=new(8,0,0,0);Grid.SetColumn(configure,1);pick.Children.Add(configure);page.Children.Add(pick);
        provider.SelectionChanged+=(_,_)=>{if(!syncing && provider.SelectedItem is AssistantProvider p && p.Id!=ai.Options.SelectedProvider)ai.SelectProvider(p.Id);};
        status.Margin=new(0,0,0,island?5:10);page.Children.Add(status);
        transcript=new ScrollViewer{Content=messages,Height=island?124:260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};UI.Named(transcript,"AI conversation");page.Children.Add(transcript);
        var review=new StackPanel();approvalTitle.FontWeight=FontWeights.SemiBold;review.Children.Add(approvalTitle);approvalText.Margin=new(0,6,0,8);review.Children.Add(new ScrollViewer{Content=approvalText,MaxHeight=island?72:95,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});review.Children.Add(UI.Row(Pill("Run once",()=>ai.Decide(true),true),Pill("Decline",()=>ai.Decide(false))));
        approval=new Border{Background=UI.Brush("#202328"),BorderBrush=UI.Brush("#515661"),BorderThickness=new(1),CornerRadius=new(14),Padding=new(island?10:12),Margin=new(0,6,0,6),Child=review,Visibility=Visibility.Collapsed};page.Children.Add(approval);
        input.AcceptsReturn=true;input.TextWrapping=TextWrapping.Wrap;input.MinHeight=island?44:62;input.MaxHeight=island?88:120;input.Margin=new(0,island?6:10,0,island?5:8);input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;input.ToolTip="Enter to send · Shift+Enter for a new line · Escape to stop";page.Children.Add(input);
        input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){e.Handled=true;_=Send();}};
        send=Pill("Send",()=>_=Send(),true);voice=Pill("Start voice",()=>_=ToggleVoice());share=Pill("Share screen",()=>_=ToggleScreen());
        var bar=new WrapPanel();foreach(var b in new[]{send,voice,share,Pill("Stop",ai.StopSession),Pill("Clear",ai.Clear)}){if(island)b.Padding=new(9,5,9,5);bar.Children.Add(b);}page.Children.Add(bar);
        var permissions=new StackPanel();var options=new WrapPanel();actions=Pill("Actions off",()=>ai.SetActions(!ai.ActionsAllowed));computer=Pill("Computer off",()=>{try{ai.SetComputerControl(!ai.Desktop.ControlEnabled);}catch(Exception ex){status.Text=ex.Message;}});options.Children.Add(actions);options.Children.Add(computer);permissions.Children.Add(options);
        display.ItemsSource=ai.Desktop.Targets();display.SelectedIndex=0;UI.Named(display,"Display to share");permissions.Children.Add(display);permissions.Children.Add(UI.Text(island?"Actions require Run once. Only the selected display is shared. Voice uses Windows default input/output. Back to island keeps voice running; Stop ends the session.":"Actions require Run once. Screen sharing uses only this display. Voice uses Windows default input/output. Closing or minimizing stops the live session.",11,UI.Muted));page.Children.Add(new Expander{Header="Session controls",Content=permissions,Margin=new(0,island?0:8,0,0)});
        screenPreview.Content=screenImage;screenPreview.Margin=new(0,island?0:8,0,0);screenPreview.Expanded+=(_,_)=>RefreshScreen();page.Children.Add(screenPreview);
        Content=page;
        Loaded+=OnLoaded;Unloaded+=OnUnloaded;SizeChanged+=(_,_)=>AdjustLayout();
        IsVisibleChanged+=(_,_)=>{if(IsVisible){RefreshOrb();RefreshScreen();}};
        Refresh();
    }
    internal static Button Pill(string text,Action action,bool primary=false)
    {var button=UI.Button(text,action,primary);button.Tag=new CornerRadius(16);button.Padding=new(12,6,12,6);button.MinHeight=31;button.FontSize=11;button.Margin=new(0,0,6,6);return button;}

    internal void FocusComposer()
    {
        focusRequested=true;
        if(IsLoaded && IsVisible)Dispatcher.BeginInvoke(()=>{if(focusRequested && IsLoaded && IsVisible){input.Focus();focusRequested=false;}});
    }
    private void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(disposed || subscribed)return;
        subscribed=true;ai.Changed+=Refresh;ai.VisualChanged+=RefreshOrb;app.SettingsChanged+=RefreshOrb;
        hostWindow=Window.GetWindow(this);if(hostWindow!=null)hostWindow.SizeChanged+=HostSizeChanged;
        for(DependencyObject? ancestor=VisualTreeHelper.GetParent(this);ancestor!=null;ancestor=VisualTreeHelper.GetParent(ancestor))
            if(ancestor is ScrollViewer viewport){hostViewport=viewport;hostViewport.SizeChanged+=HostSizeChanged;break;}
        Refresh();AdjustLayout();if(focusRequested)FocusComposer();
    }
    private void OnUnloaded(object sender,RoutedEventArgs e)
        =>Detach();
    private void Detach()
    {
        if(!subscribed)return;
        savedScrollOffset=transcript.VerticalOffset;followLatest=transcript.ScrollableHeight-transcript.VerticalOffset<30;restoreScroll=true;
        subscribed=false;ai.Changed-=Refresh;ai.VisualChanged-=RefreshOrb;app.SettingsChanged-=RefreshOrb;
        if(hostWindow!=null)hostWindow.SizeChanged-=HostSizeChanged;if(hostViewport!=null)hostViewport.SizeChanged-=HostSizeChanged;
        hostWindow=null;hostViewport=null;screenImage.Source=null;lastFrame="";
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;focusRequested=false;Detach();Loaded-=OnLoaded;Unloaded-=OnUnloaded;Visibility=Visibility.Collapsed;
    }
    private void HostSizeChanged(object sender,SizeChangedEventArgs e)=>AdjustLayout();
    private void AdjustLayout()
    {
        if(closeButton!=null)
        {
            bool compact=ActualWidth>0 && ActualWidth<360;
            closeButton.Content=compact?"‹":"Back to island";closeButton.MinWidth=compact?31:0;closeButton.FontSize=compact?20:11;
        }
        double available=island?hostViewport?.ViewportHeight??0:hostWindow?.ActualHeight??0;
        double height=island?(available>0?Math.Clamp(available-285,110,150):124):Math.Max(72,(available>0?available:640)-(ai.Pending!=null?510:380));
        if(Math.Abs(transcript.Height-height)>.5)transcript.Height=height;
        input.MinHeight=island?44:available>0 && available<420?36:62;
    }
    private async System.Threading.Tasks.Task Send()
    {string text=input.Text.Trim();if(text.Length==0 || ai.Busy)return;input.Clear();bool success=await ai.Send(text);if(!success && !ai.LastSendHadActionResults && input.Text.Length==0)input.Text=text;}
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
            var owner=Window.GetWindow(this)??throw new InvalidOperationException("Open the companion before sharing a display.");
            ai.StartSharing(target.Id,new WindowInteropHelper(owner).Handle);await ai.Desktop.Capture(System.Threading.CancellationToken.None);screenPreview.IsExpanded=true;Refresh();
        }
        catch(Exception ex){ai.StopSession();status.Text=ex.Message;}
    }
    private void RefreshOrb(){if(!IsVisible)return;orb.State=ai.State;orb.Level=ai.Level;orb.Live=ai.VoiceConnected;orb.ReduceMotion=app.Settings.ReduceMotion;}
    private void Refresh()
    {
        syncing=true;
        try
        {
            string signature=string.Join("|",ai.Options.Providers.Select(p=>p.Id+":"+p.Name));
            // Saving profiles can replace their objects. Rebind under the same
            // guard so a stale selected clone cannot trigger a provider change.
            if(signature!=providerSignature || !provider.Items.Cast<AssistantProvider>().SequenceEqual(ai.Options.Providers)){providerSignature=signature;provider.ItemsSource=ai.Options.Providers.ToArray();}
            provider.SelectedItem=ai.Provider;providerHint.Visibility=ai.Provider==null?Visibility.Visible:Visibility.Collapsed;provider.IsEnabled=!ai.Busy && !ai.VoiceConnected;
            status.Text=(ai.Options.Enabled?"":"Companion is off · ")+ai.Status;
            send.IsEnabled=ai.Options.Enabled && !ai.Busy;voice.IsEnabled=ai.Options.Enabled && !ai.Busy;share.IsEnabled=ai.Options.Enabled && (!ai.Busy || ai.Desktop.Sharing);
            voice.Content=ai.VoiceConnected?"End voice":"Start voice";share.Content=ai.Desktop.Sharing?"Stop sharing":"Share screen";actions.Content=ai.ActionsAllowed?"Actions on":"Actions off";computer.Content=ai.Desktop.ControlEnabled?"Computer on":"Computer off";
            screenPreview.Visibility=ai.Desktop.Sharing?Visibility.Visible:Visibility.Collapsed;computer.IsEnabled=ai.Desktop.Sharing;actions.IsEnabled=ai.Options.Enabled;
            display.IsEnabled=!ai.Desktop.Sharing;
            approval.Visibility=ai.Pending!=null?Visibility.Visible:Visibility.Collapsed;
            if(ai.Pending is { } pending){approvalTitle.Text=pending.Name=="computer"?"Review computer action":"Review NabyScape action";approvalText.Text=pending.Arguments;if(reviewedId!=pending.Id){reviewedId=pending.Id;Dispatcher.BeginInvoke(()=>{if(IsLoaded && IsVisible && ai.Pending?.Id==pending.Id)approval.BringIntoView();});}}else reviewedId=null;
            bool restoring=restoreScroll,bottom=restoring?followLatest:transcript.ScrollableHeight-transcript.VerticalOffset<30;
            var history=ai.Conversation.Messages;
            if(history.Count<rendered || rendered>0 && !ReferenceEquals(firstRendered,history.FirstOrDefault())){messages.Children.Clear();rendered=0;}
            if(history.Count==0 && messages.Children.Count==0)
            {var welcome=UI.Text(island?"A little help, right here. Add a provider, then type or start voice. You choose when to share a screen or approve actions.":"A little help, right here.\n\nAdd a provider, then type a message. Start voice when you want to talk. Screen context and actions are yours to enable for each session.",island?12:13,UI.Muted);welcome.Margin=island?new Thickness(2,10,2,12):new Thickness(2,18,2,24);messages.Children.Add(welcome);}
            if(history.Count>0 && rendered==0)messages.Children.Clear();
            for(int i=rendered;i<history.Count;i++)
            {
                var message=history[i];if(message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Text) || message.Text.StartsWith("Updated shared screen after"))continue;
                var text=UI.Text(message.Text,island?12:13);text.Margin=island?new Thickness(10,7,10,7):new Thickness(12,9,12,9);
                var bubble=new Border{Child=text,CornerRadius=new(13),Background=message.Role=="user"?UI.Brush("#24272D"):Brushes.Transparent,Margin=new(message.Role=="user"?32:0,4,message.Role=="user"?0:20,5)};messages.Children.Add(bubble);
                if(IsLoaded && !app.Settings.ReduceMotion)Motion.Fade(bubble,1,180);
            }
            rendered=history.Count;firstRendered=history.FirstOrDefault();restoreScroll=false;
            if(bottom || restoring)Dispatcher.BeginInvoke(()=>{if(IsLoaded){if(bottom)transcript.ScrollToEnd();else transcript.ScrollToVerticalOffset(savedScrollOffset);}});
            AdjustLayout();RefreshOrb();RefreshScreen();
        }
        finally{syncing=false;}
    }
    private void RefreshScreen()
    {
        string? data=ai.Desktop.Frame?.DataUrl;
        if(!ai.Desktop.Sharing){screenImage.Source=null;lastFrame="";return;}
        if(!IsLoaded || !screenPreview.IsExpanded || data==null || data==lastFrame)return;lastFrame=data;
        try{var bytes=Convert.FromBase64String(data[(data.IndexOf(',')+1)..]);using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=360;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();screenImage.Source=bitmap;}catch{screenImage.Source=null;}
    }
}
