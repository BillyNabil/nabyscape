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
    private readonly Grid layout=new();
    private readonly TextBlock status=UI.Text("",11,UI.Muted);
    private readonly StackPanel messages=new();
    private readonly ScrollViewer transcript;
    private readonly TextBox input=UI.Input("Message AI companion","");
    private readonly ComboBox display=new(){MinHeight=30};
    private readonly Button send,voice,share,stop,clear,actions,computer;
    private readonly TextBlock context=UI.Text("",11,UI.Muted);
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
    private string lastFrame="";
    private string? reviewedId;
    internal TextBox Editor=>input;
    internal AssistantView(App app,bool island=false,Action? close=null)
    {
        this.app=app;ai=app.Assistant;this.island=island;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Features/Assistant/AssistantTheme.xaml",UriKind.Relative)});
        input.Style=(Style)FindResource("AssistantComposer");
        Foreground=Brushes.White;UseLayoutRounding=true;
        var page=layout;page.Margin=island?new Thickness(0,2,0,4):new Thickness(20,12,20,12);
        for(int i=0;i<7;i++)page.RowDefinitions.Add(new(){Height=i==2?new GridLength(1,GridUnitType.Star):GridLength.Auto});
        void Row(UIElement child,int row){Grid.SetRow(child,row);page.Children.Add(child);}
        var heading=new Grid{Margin=new(0,0,0,10)};heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        orb.Width=orb.Height=island?34:40;heading.Children.Add(orb);
        var title=new StackPanel{Margin=new(9,0,0,0),VerticalAlignment=VerticalAlignment.Center};title.Children.Add(UI.Heading("Naby",18));context.FontSize=10;context.Margin=new(0,2,0,0);title.Children.Add(context);Grid.SetColumn(title,1);heading.Children.Add(title);
        var headerActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        stop=IconAction("Stop","stop",ai.StopSession);clear=IconAction("Clear","trash",ai.Clear);headerActions.Children.Add(stop);headerActions.Children.Add(clear);
        headerActions.Children.Add(IconAction("AI Settings","settings",app.ShowAssistantProviders));
        if(island && close!=null){closeButton=IconAction("Back to island","chevron",close);closeButton.Content=Icons.Create("chevron",13);closeButton.Content=new System.Windows.Controls.Viewbox{Width=13,Height=13,Child=Icons.Create("chevron",13),RenderTransformOrigin=new(.5,.5),RenderTransform=new RotateTransform(90)};headerActions.Children.Add(closeButton);}
        Grid.SetColumn(headerActions,2);heading.Children.Add(headerActions);Row(heading,0);
        status.Margin=new(2,0,2,8);status.MaxHeight=48;status.TextTrimming=TextTrimming.CharacterEllipsis;Row(status,1);
        transcript=new ScrollViewer{Content=messages,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(0,2,0,10)};UI.Named(transcript,"AI conversation");Row(transcript,2);
        var review=new StackPanel();approvalTitle.FontWeight=FontWeights.SemiBold;review.Children.Add(approvalTitle);approvalText.Margin=new(0,6,0,8);review.Children.Add(new ScrollViewer{Content=approvalText,MaxHeight=68,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden});review.Children.Add(UI.Row(Pill("Run once",()=>ai.Decide(true),true),Pill("Decline",()=>ai.Decide(false))));
        approval=new Border{Background=UI.Brush("#1B1E23"),BorderBrush=UI.Brush("#41464F"),BorderThickness=new(1),CornerRadius=new(10),Padding=new(10),Margin=new(0,6,0,6),Child=review,Visibility=Visibility.Collapsed};Row(approval,3);
        input.AcceptsReturn=true;input.TextWrapping=TextWrapping.Wrap;input.MinHeight=44;input.MaxHeight=90;input.Margin=new(0);input.Padding=new(1,7,1,7);input.FontSize=13;input.VerticalContentAlignment=VerticalAlignment.Top;input.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;input.ToolTip="Enter to send · Shift+Enter for a new line · Escape to stop";
        input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){e.Handled=true;_=Send();}};
        send=IconAction("Send","send",()=>_=Send(),true);voice=IconAction("Start voice","mic",()=>_=ToggleVoice());share=IconAction("Share screen","screen",()=>_=ToggleScreen());
        send.VerticalAlignment=VerticalAlignment.Center;input.BorderThickness=new(0);input.Background=Brushes.Transparent;
        var editorHost=new Grid();editorHost.Children.Add(input);var hint=UI.Text("Message Naby…",13,UI.Muted);hint.Margin=new(1,7,0,0);hint.VerticalAlignment=VerticalAlignment.Top;hint.IsHitTestVisible=false;editorHost.Children.Add(hint);input.TextChanged+=(_,_)=>hint.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        var composer=new Grid();composer.RowDefinitions.Add(new(){Height=GridLength.Auto});composer.RowDefinitions.Add(new(){Height=GridLength.Auto});composer.ColumnDefinitions.Add(new());composer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});composer.Children.Add(editorHost);Grid.SetColumn(send,1);composer.Children.Add(send);
        var bar=new DockPanel{Margin=new(0,3,0,0)};var keys=UI.Text("Enter ↵",9,UI.Muted);keys.Opacity=.65;DockPanel.SetDock(keys,Dock.Right);bar.Children.Add(keys);var tools=new StackPanel{Orientation=Orientation.Horizontal};tools.Children.Add(voice);tools.Children.Add(share);bar.Children.Add(tools);Grid.SetRow(bar,1);Grid.SetColumnSpan(bar,2);composer.Children.Add(bar);
        Row(new Border{Child=composer,Background=UI.Brush("#17191D"),BorderBrush=UI.Brush("#30343B"),BorderThickness=new(1),CornerRadius=new(13),Padding=new(11,4,6,5),Margin=new(0,4,0,3)},4);
        var permissions=new StackPanel();var options=new WrapPanel();actions=Pill("Actions off",()=>ai.SetActions(!ai.ActionsAllowed));computer=Pill("Computer off",()=>{try{ai.SetComputerControl(!ai.Desktop.ControlEnabled);}catch(Exception ex){status.Text=ex.Message;status.Visibility=Visibility.Visible;}});options.Children.Add(actions);options.Children.Add(computer);permissions.Children.Add(options);
        display.ItemsSource=ai.Desktop.Targets();display.SelectedIndex=0;UI.Named(display,"Display to share");permissions.Children.Add(display);permissions.Children.Add(UI.Text("Actions ask for approval. Share only the selected display. Stop ends voice and sharing.",10,UI.Muted));Row(new Expander{Header="Permissions",Content=permissions,Margin=new(1,0,0,0)},5);
        screenPreview.Content=screenImage;screenPreview.Margin=new(0);screenPreview.Expanded+=(_,_)=>RefreshScreen();Row(screenPreview,6);
        Content=page;
        Loaded+=OnLoaded;Unloaded+=OnUnloaded;SizeChanged+=(_,_)=>AdjustLayout();
        PreviewMouseMove+=(_,e)=>{var p=e.GetPosition(orb);orb.LookAt((p.X-orb.ActualWidth/2)/Math.Max(100,ActualWidth*.45),(p.Y-orb.ActualHeight/2)/Math.Max(90,ActualHeight*.45));};
        MouseLeave+=(_,_)=>orb.LookAt(0,0);
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
        double available;
        if(island){var screen=Native.ChooseScreen(app.Settings);available=screen.WorkingArea.Height/Native.Scale(screen)-55;}
        else available=(hostWindow?.ActualHeight>0?hostWindow.ActualHeight:640)-78;
        double height=Math.Max(170,island?Math.Min(app.Settings.PanelMaxHeight-38,available):available);
        if(Math.Abs(layout.Height-height)>.5 || double.IsNaN(layout.Height))layout.Height=height;
        input.MinHeight=height<300?36:44;
    }
    private static Button IconAction(string name,string icon,Action action,bool primary=false)
    {
        var button=Icons.Button(icon,name,action,primary,30);button.Margin=new(1,0,1,0);button.Padding=new(7);button.Tag=new CornerRadius(primary?15:8);if(!primary)button.Background=Brushes.Transparent;return button;
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
        if(syncing)return;syncing=true;
        try
        {
            context.Text=ai.VoiceConnected?"Live voice":ai.Desktop.Sharing?"Display shared":"Your companion";
            status.Text=!ai.Options.Enabled?"Enable your companion in Settings → AI.":ai.Status.Replace("Configure a realtime endpoint and model in Providers.","Set up voice in Settings → AI.").Replace("in provider settings","in Settings → AI");
            status.Visibility=!ai.Options.Enabled || ai.Provider==null || ai.State!=AssistantState.Idle?Visibility.Visible:Visibility.Collapsed;
            stop.Visibility=ai.Busy || ai.VoiceConnected || ai.Desktop.Sharing || ai.Pending!=null?Visibility.Visible:Visibility.Collapsed;clear.Visibility=ai.Conversation.Messages.Count>0?Visibility.Visible:Visibility.Collapsed;
            send.IsEnabled=ai.Options.Enabled && !ai.Busy;voice.IsEnabled=ai.Options.Enabled && !ai.Busy;share.IsEnabled=ai.Options.Enabled && (!ai.Busy || ai.Desktop.Sharing);
            voice.Content=Icons.Create(ai.VoiceConnected?"mic-off":"mic",16);voice.ToolTip=ai.VoiceConnected?"End voice":"Start voice";voice.Background=ai.VoiceConnected?UI.Brush("#343940"):Brushes.Transparent;share.Content=Icons.Create("screen",16);share.ToolTip=ai.Desktop.Sharing?"Stop sharing":"Share screen";share.Background=ai.Desktop.Sharing?UI.Brush("#343940"):Brushes.Transparent;actions.Content=ai.ActionsAllowed?"Actions on":"Actions off";computer.Content=ai.Desktop.ControlEnabled?"Computer on":"Computer off";
            screenPreview.Visibility=ai.Desktop.Sharing?Visibility.Visible:Visibility.Collapsed;computer.IsEnabled=ai.Desktop.Sharing;actions.IsEnabled=ai.Options.Enabled;
            display.IsEnabled=!ai.Desktop.Sharing;
            approval.Visibility=ai.Pending!=null?Visibility.Visible:Visibility.Collapsed;
            if(ai.Pending is { } pending){approvalTitle.Text=pending.Name=="computer"?"Review computer action":"Review NabyScape action";approvalText.Text=pending.Arguments;if(reviewedId!=pending.Id){reviewedId=pending.Id;Dispatcher.BeginInvoke(()=>{if(IsLoaded && IsVisible && ai.Pending?.Id==pending.Id)approval.BringIntoView();});}}else reviewedId=null;
            bool restoring=restoreScroll,bottom=restoring?followLatest:transcript.ScrollableHeight-transcript.VerticalOffset<30;
            var history=ai.Conversation.Messages;
            if(history.Count<rendered || rendered>0 && !ReferenceEquals(firstRendered,history.FirstOrDefault())){messages.Children.Clear();rendered=0;}
            if(history.Count==0 && messages.Children.Count==0)
            {var welcome=UI.Text(island?"What’s on your mind?":"What’s on your mind?",island?12:13,UI.Muted);welcome.Margin=island?new Thickness(2,10,2,12):new Thickness(2,18,2,24);messages.Children.Add(welcome);}
            if(history.Count>0 && rendered==0)messages.Children.Clear();
            for(int i=rendered;i<history.Count;i++)
            {
                var message=history[i];if(message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Text) || message.Text.StartsWith("Updated shared screen after"))continue;
                var text=UI.Text(message.Text,13);text.Margin=message.Role=="user"?new Thickness(12,9,12,9):new Thickness(2,10,2,10);
                var menu=new ContextMenu();var copy=new MenuItem{Header="Copy message"};copy.Click+=(_,_)=>{try{Clipboard.SetText(message.Text);}catch{}};menu.Items.Add(copy);text.ContextMenu=menu;
                var bubble=new Border{Child=text,CornerRadius=new(11),Background=message.Role=="user"?UI.Brush("#25282E"):Brushes.Transparent,Margin=new(message.Role=="user"?42:0,3,message.Role=="user"?0:12,5)};messages.Children.Add(bubble);
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
