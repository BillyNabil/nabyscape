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
internal sealed partial class AssistantView : UserControl,IDisposable
{
    private readonly App app;
    private readonly AssistantRuntime ai;
    private readonly bool island;
    private readonly AssistantOrb orb=new();
    private readonly Grid layout=new();
    private readonly TextBlock status=UI.Caption("",11,UI.Muted);
    private readonly StackPanel messages=new();
    private readonly ScrollViewer transcript;
    private readonly TextBox input=UI.Input("Message AI companion","");
    private readonly Button send,share,stop,clear,attach;
    private readonly WrapPanel draftFiles=new(){Orientation=Orientation.Horizontal};
    private AssistantAttachment[] shownFiles=Array.Empty<AssistantAttachment>();
    private readonly TextBlock connection;
    private readonly TextBlock context=UI.Caption("",11,UI.Muted);
    private readonly Button? closeButton;
    private readonly Expander screenPreview=new(){Header=Localization.Text("Shared display preview"),IsExpanded=false};
    private readonly Image screenImage=new(){MaxHeight=130,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};
    private Window? hostWindow;
    private ScrollViewer? hostViewport;
    private bool syncing,subscribed,focusRequested,restoreScroll,followLatest,disposed;
    private double savedScrollOffset;
    private int rendered;
    private AssistantMessage? firstRendered;
    private System.Collections.Generic.IReadOnlyList<AssistantMessage>? renderedHistory;
    private readonly TextBlock welcomeTitle=UI.Heading(Localization.Text("What’s on your mind?"),18),welcomeDescription=UI.Caption("",12,UI.Muted);
    private readonly StackPanel welcomePanel=new(){Margin=new(2,18,2,24)};
    private readonly Border statusHost=new(){CornerRadius=new(10),Padding=new(12,9,12,9),Margin=new(0,0,0,10)};
    private bool refreshQueued;
    private string lastFrame="";
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
        var title=new StackPanel{Margin=new(9,0,0,0),VerticalAlignment=VerticalAlignment.Center};title.Children.Add(UI.Heading(Localization.Text("Naby"),18));context.FontSize=10;context.Margin=new(0,2,0,0);title.Children.Add(context);Grid.SetColumn(title,1);heading.Children.Add(title);
        var headerActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        stop=IconAction("Stop session and all microphones","stop",ai.StopSession);clear=IconAction("Clear conversation","trash",ai.Clear);headerActions.Children.Add(stop);headerActions.Children.Add(clear);
        if(island && close!=null){closeButton=IconAction("Back to island","back",close);headerActions.Children.Add(closeButton);}
        Grid.SetColumn(headerActions,2);heading.Children.Add(headerActions);Row(heading,0);
        status.MaxHeight=48;status.TextTrimming=TextTrimming.CharacterEllipsis;statusHost.SetResourceReference(Border.BackgroundProperty,"ControlFill");var statusBody=new StackPanel();statusBody.Children.Add(status);statusBody.Children.Add(AssistantRecoveryControls.Build(app));statusHost.Child=statusBody;Row(statusHost,1);
        transcript=new ScrollViewer{Content=messages,MinHeight=90,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(0,2,0,10)};UI.LocalizedName(transcript,"AI conversation");Row(transcript,2);
        welcomePanel.Children.Add(welcomeTitle);welcomeDescription.Margin=new(0,7,0,14);welcomePanel.Children.Add(welcomeDescription);
        var welcomeActions=new WrapPanel();welcomeActions.Children.Add(Pill("AI setup",app.ShowAssistantProviders));welcomeActions.Children.Add(Pill("Voice mode",app.OpenVoice));welcomePanel.Children.Add(welcomeActions);
        input.AcceptsReturn=true;input.TextWrapping=TextWrapping.Wrap;input.MinHeight=36;input.MaxHeight=76;input.Margin=new(0);input.Padding=new(1,7,1,7);input.FontSize=13;input.VerticalContentAlignment=VerticalAlignment.Top;input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;input.ToolTip=Localization.Text("Enter to send · Shift+Enter for a new line · Escape to stop");
        input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){e.Handled=true;_=Send();}};
        ConfigureClipboard();
        AllowDrop=true;PreviewDragOver+=(_,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=ai.Busy || ai.ImportingFiles?DragDropEffects.None:DragDropEffects.Copy;e.Handled=true;}};
        PreviewDrop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths){e.Handled=true;_=ai.AttachFiles(paths);}};
        send=IconAction("Send","send",()=>_=Send(),true);share=IconAction("Share a window or display","screen",()=>_=ToggleScreen());attach=IconAction("Attach files","paperclip",PickFiles);attach.ToolTip=Localization.Text("Attach images, videos, PDFs and documents · or drop files / paste an image");
        send.VerticalAlignment=VerticalAlignment.Center;input.BorderThickness=new(0);input.Background=Brushes.Transparent;
        var editorHost=new Grid();editorHost.Children.Add(input);var hint=UI.Caption("Message Naby…",13,UI.Muted);hint.Margin=new(1,7,0,0);hint.VerticalAlignment=VerticalAlignment.Top;hint.IsHitTestVisible=false;editorHost.Children.Add(hint);input.TextChanged+=(_,_)=>hint.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        var composer=new Grid();for(int i=0;i<3;i++)composer.RowDefinitions.Add(new(){Height=GridLength.Auto});composer.ColumnDefinitions.Add(new());composer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetColumnSpan(editorHost,2);Grid.SetRow(editorHost,1);composer.Children.Add(editorHost);
        var attachments=new ScrollViewer{Content=draftFiles,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=106};Grid.SetColumnSpan(attachments,2);composer.Children.Add(attachments);
        var bar=new DockPanel{Margin=new(0,3,0,0)};DockPanel.SetDock(send,Dock.Right);bar.Children.Add(send);var tools=new StackPanel{Orientation=Orientation.Horizontal};tools.Children.Add(attach);tools.Children.Add(share);bar.Children.Add(tools);Grid.SetRow(bar,2);Grid.SetColumnSpan(bar,2);composer.Children.Add(bar);
        var composerFrame=new Border{Child=composer,BorderThickness=new(1),CornerRadius=new(12),Padding=new(11,4,6,5),Margin=new(0,4,0,3)};
        composerFrame.SetResourceReference(Border.BackgroundProperty,"ControlFill");composerFrame.SetResourceReference(Border.BorderBrushProperty,"ControlBorder");Row(composerFrame,4);
        var session=new StackPanel();
        screenPreview.Content=screenImage;screenPreview.Margin=new(0,3,0,0);screenPreview.Expanded+=(_,_)=>RefreshScreen();session.Children.Add(screenPreview);session.Children.Add(AssistantControls.Connection(app,out connection));Row(session,5);
        input.TextChanged+=(_,_)=>RefreshSendState();
        Content=page;
        Loaded+=OnLoaded;Unloaded+=OnUnloaded;SizeChanged+=(_,_)=>AdjustLayout();
        PreviewMouseMove+=(_,e)=>{var p=e.GetPosition(orb);orb.LookAt((p.X-orb.ActualWidth/2)/Math.Max(100,ActualWidth*.45),(p.Y-orb.ActualHeight/2)/Math.Max(90,ActualHeight*.45));};
        MouseLeave+=(_,_)=>orb.LookAt(0,0);
        IsVisibleChanged+=OnVisibilityChanged;
        Refresh();
    }
    internal static Button Pill(string text,Action action,bool primary=false)
    {var button=UI.CaptionButton(text,action,primary);button.Tag=new CornerRadius(8);button.Padding=new(12,7,12,7);button.MinHeight=34;button.FontSize=12;button.Margin=new(0,0,6,6);return button;}

    internal void FocusComposer()
    {
        focusRequested=true;
        if(IsLoaded && IsVisible)Dispatcher.BeginInvoke(()=>{if(focusRequested && IsLoaded && IsVisible){input.Focus();focusRequested=false;}});
    }
    private void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(disposed || subscribed || !IsVisible)return;
        subscribed=true;ai.Changed+=QueueRefresh;ai.VisualChanged+=RefreshOrb;app.SettingsChanged+=RefreshOrb;
        hostWindow=Window.GetWindow(this);if(hostWindow!=null)hostWindow.SizeChanged+=HostSizeChanged;
        for(DependencyObject? ancestor=VisualTreeHelper.GetParent(this);ancestor!=null;ancestor=VisualTreeHelper.GetParent(ancestor))
            if(ancestor is ScrollViewer viewport){hostViewport=viewport;hostViewport.SizeChanged+=HostSizeChanged;break;}
        Refresh();AdjustLayout();if(focusRequested)FocusComposer();
    }
    private void OnUnloaded(object sender,RoutedEventArgs e)
        =>Detach();
    private void OnVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(!IsVisible){Detach();return;}
        if(IsLoaded)OnLoaded(this,new RoutedEventArgs());
    }
    private void Detach()
    {
        if(!subscribed)return;
        savedScrollOffset=transcript.VerticalOffset;followLatest=transcript.ScrollableHeight-transcript.VerticalOffset<30;restoreScroll=true;
        subscribed=false;ai.Changed-=QueueRefresh;ai.VisualChanged-=RefreshOrb;app.SettingsChanged-=RefreshOrb;
        if(hostWindow!=null)hostWindow.SizeChanged-=HostSizeChanged;if(hostViewport!=null)hostViewport.SizeChanged-=HostSizeChanged;
        hostWindow=null;hostViewport=null;screenImage.Source=null;lastFrame="";
        messages.Children.Clear();rendered=0;firstRendered=null;renderedHistory=null;
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;focusRequested=false;Detach();renderedHistory=null;Loaded-=OnLoaded;Unloaded-=OnUnloaded;IsVisibleChanged-=OnVisibilityChanged;Visibility=Visibility.Collapsed;
    }
    private void HostSizeChanged(object sender,SizeChangedEventArgs e)=>AdjustLayout();
    internal static Size PanelSize(Settings settings,bool voice=false)
    {
        var screen=Native.ChooseScreen(settings);double scale=Native.Scale(screen);
        return new Size(Math.Max(240,Math.Min(Math.Max(voice?520:720,settings.PanelWidth),screen.WorkingArea.Width/scale-32)),Math.Max(240,Math.Min(Math.Max(voice?540:680,settings.PanelMaxHeight),screen.WorkingArea.Height/scale-32)));
    }
    private void AdjustLayout()
    {
        foreach(var card in draftFiles.Children.OfType<AssistantAttachmentView>())card.Width=Math.Min(240,Math.Max(100,ActualWidth-32));

        var panel=PanelSize(app.Settings);
        double height=Math.Max(170,island?(panel.Height-32)/app.Settings.PanelScale:(hostWindow?.ActualHeight>0?hostWindow.ActualHeight:640)-78);
        if(Math.Abs(layout.Height-height)>.5 || double.IsNaN(layout.Height))layout.Height=height;
        transcript.MinHeight=Math.Max(44,height*.2);
        input.MinHeight=36;
        double width=island?(panel.Width-app.Settings.PanelPadding*2)/app.Settings.PanelScale:Math.Max(240,(hostWindow?.ActualWidth??600)-64);
        if(width>0 && Math.Abs(layout.Width-width)>.5 || double.IsNaN(layout.Width))layout.Width=Math.Max(180,width);
    }
    private static Button IconAction(string name,string icon,Action action,bool primary=false)
    {
        var button=Icons.Button(icon,name,action,primary,32);button.MinWidth=32;button.Margin=new(3,0,0,0);button.Padding=new(0);button.Content=Icons.Create(icon,18,primary?Brushes.Black:Brushes.White);button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;button.Tag=new CornerRadius(primary?16:8);if(!primary)button.Background=Brushes.Transparent;return button;
    }
    private async System.Threading.Tasks.Task Send()
    {string text=input.Text.Trim();if(text.Length==0 && ai.DraftFiles.Count==0 || ai.Busy || ai.ImportingFiles)return;int generation=ai.Conversation.Epoch;input.Clear();bool success=await ai.Send(text);if(!success && generation==ai.Conversation.Epoch && input.Text.Length==0)input.Text=text;}
    private void PickFiles()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Title="Attach files to Naby",Multiselect=true,CheckFileExists=true,Filter="Supported attachments|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.pdf;*.mp4;*.webm;*.mov;*.mkv;*.avi;*.mp3;*.wav;*.m4a;*.ogg;*.flac;*.docx;*.pptx;*.xlsx;*.odt;*.txt;*.md;*.csv;*.tsv;*.json;*.xml;*.py;*.cs;*.js;*.ts;*.log|All files|*.*"};
        if(dialog.ShowDialog(Window.GetWindow(this))==true)_=ai.AttachFiles(dialog.FileNames);
    }
    private void RefreshSendState()
    {send.IsEnabled=ai.Options.Enabled && !ai.Busy && !ai.ImportingFiles && (!string.IsNullOrWhiteSpace(input.Text) || ai.DraftFiles.Count>0);attach.IsEnabled=ai.Options.Enabled && !ai.Busy && !ai.ImportingFiles;}
    private async System.Threading.Tasks.Task ToggleScreen()
    {
        if(ai.Desktop.Sharing){ai.StopSharing();return;}
        share.IsEnabled=false;
        try
        {
            var owner=Window.GetWindow(this)??throw new InvalidOperationException("Open Naby before choosing a sharing source.");
            if(await ai.ChooseSharing(new WindowInteropHelper(owner).Handle)){await ai.Desktop.Capture(System.Threading.CancellationToken.None);Refresh();}
        }
        catch(Exception ex){ai.StopSharing();statusHost.Visibility=status.Visibility=Visibility.Visible;Accessibility.Announce(status,ex.Message);}
        finally{share.IsEnabled=ai.Options.Enabled && (!ai.Busy || ai.Desktop.Sharing);}
    }
    private void RefreshOrb(){if(!IsVisible)return;orb.State=ai.State;orb.Level=ai.Level;orb.Live=ai.VoiceConnected;orb.ReduceMotion=app.Settings.ReduceMotion;}
    private void QueueRefresh()
    {
        if(refreshQueued || !subscribed)return;refreshQueued=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>{refreshQueued=false;if(subscribed && !disposed)Refresh();}));
    }
    private void Refresh()
    {
        if(syncing)return;syncing=true;
        try
        {
            AssistantControls.RefreshConnection(connection,ai);context.Text=ai.CaptureSummary;context.ToolTip=ai.SetupSummary;
            status.Visibility=!ai.Options.Enabled || ai.Provider==null || ai.State!=AssistantState.Idle || ai.ImportingFiles?Visibility.Visible:Visibility.Collapsed;
            statusHost.Visibility=status.Visibility;status.Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:ai.State==AssistantState.Error?UI.Brush("#EAB3AD"):UI.Muted;
            Accessibility.Announce(status,!ai.Options.Enabled?Localization.Text("Enable your companion in Settings → AI."):Localization.Text(ai.Status.Replace("Configure a realtime endpoint and model in Providers.","Set up voice in Settings → AI.").Replace("in provider settings","in Settings → AI")));
            welcomeDescription.Text=Localization.Text(!ai.Options.Enabled?"Enable your companion in AI setup to begin.":!ai.Setup.Chat.Configured?"Connect your provider or Hermes to start chatting.":"Ask a question, attach a file, or switch to voice.");
            stop.Visibility=ai.Busy || ai.ImportingFiles || ai.VoiceConnected || ai.WakeArmed || ai.Desktop.Sharing?Visibility.Visible:Visibility.Collapsed;clear.Visibility=ai.Conversation.Messages.Count>0 || ai.DraftFiles.Count>0?Visibility.Visible:Visibility.Collapsed;
            RefreshSendState();share.IsEnabled=ai.Options.Enabled && (!ai.Busy || ai.Desktop.Sharing);
            if(!shownFiles.SequenceEqual(ai.DraftFiles)){draftFiles.Children.Clear();shownFiles=ai.DraftFiles.ToArray();foreach(var file in shownFiles)draftFiles.Children.Add(new AssistantAttachmentView(ai,file,true));}
            draftFiles.IsEnabled=!ai.Busy && !ai.ImportingFiles;
            draftFiles.Visibility=ai.Busy?Visibility.Collapsed:Visibility.Visible;
            share.ToolTip=ai.Desktop.Sharing?"Stop sharing "+ai.Desktop.SourceName:"Share a window or display";if(ai.Desktop.Sharing){share.SetResourceReference(Control.BackgroundProperty,"SelectionFill");share.SetResourceReference(Control.ForegroundProperty,"SelectionText");}else{share.Background=Brushes.Transparent;share.SetResourceReference(Control.ForegroundProperty,"ControlText");}
            screenPreview.Header="Sharing · "+ai.Desktop.SourceName;screenPreview.Visibility=ai.Desktop.Sharing?Visibility.Visible:Visibility.Collapsed;
            bool restoring=restoreScroll,bottom=restoring?followLatest:transcript.ScrollableHeight-transcript.VerticalOffset<30;
            var history=ai.Conversation.Messages;
            if(ReferenceEquals(renderedHistory,history) && !restoring){RefreshOrb();RefreshScreen();return;}
            if(history.Count<rendered || rendered>0 && !ReferenceEquals(firstRendered,history.FirstOrDefault())){messages.Children.Clear();rendered=0;}
            if(history.Count==0 && messages.Children.Count==0)
            {messages.Children.Add(welcomePanel);}
            if(history.Count>0 && rendered==0)messages.Children.Clear();
            for(int i=rendered;i<history.Count;i++)
            {
                var message=history[i];if(message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Text) && message.Attachments is not {Count:>0} || message.Text.StartsWith("Updated shared screen after"))continue;
                var body=new StackPanel();if(!string.IsNullOrWhiteSpace(message.Text))body.Children.Add(message.Role=="assistant"?AssistantMarkdown.Render(message.Text):UI.Text(message.Text,13));
                if(message.Attachments!=null)foreach(var file in message.Attachments)body.Children.Add(new AssistantAttachmentView(ai,file));FrameworkElement text=body;text.Margin=message.Role=="user"?new Thickness(12,9,12,9):new Thickness(2,10,2,10);
                var menu=new ContextMenu();var copy=new MenuItem{Header="Copy message"};copy.Click+=(_,_)=>{try{Clipboard.SetText(message.Text);}catch{}};menu.Items.Add(copy);text.ContextMenu=menu;
                var bubble=new Border{Child=text,CornerRadius=new(12),Background=Brushes.Transparent,Margin=new(message.Role=="user"?42:0,3,message.Role=="user"?0:12,5)};if(message.Role=="user")bubble.SetResourceReference(Border.BackgroundProperty,"ControlFill");messages.Children.Add(bubble);
                if(IsLoaded && !app.Settings.ReduceMotion && !restoring){bubble.Opacity=0;Motion.Fade(bubble,1,180);}
            }
            rendered=history.Count;firstRendered=history.FirstOrDefault();renderedHistory=history;restoreScroll=false;
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
