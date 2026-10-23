using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace NabyScape;

// A dedicated voice widget shares the same runtime, permissions and Stop action as chat.
internal sealed class AssistantVoiceView : UserControl
{
    private readonly App app;
    private readonly AssistantRuntime ai;
    private readonly AssistantOrb orb=new(){Width=140,Height=140};
    private readonly TextBlock phase=UI.Caption("Ready to talk",12,UI.Muted),caption=UI.Caption("Speak naturally. Naby will reply.",17),heard=UI.Caption("",11,UI.Muted),error=UI.Caption("",10,UI.Muted);
    private readonly Ellipse halo=new(){Width=172,Height=172,Stroke=UI.Brush("#849FBE"),StrokeThickness=1.2,Opacity=.18,IsHitTestVisible=false};
    private readonly ScaleTransform pulse=new(1,1);
    private readonly TextBlock connection;
    private readonly TextBlock privacy=UI.Caption("Your microphone stays off until you start.",10,UI.Muted);
    private readonly Button primary;
    private readonly Grid layout=new(),details=new(),stage=new(){Margin=new(0,8,0,6)};
    private readonly StackPanel controls=new();
    private readonly FrameworkElement footer;
    private readonly ScrollViewer pageViewport,detailsViewport;
    private bool subscribed,refreshQueued;
    internal AssistantVoiceView(App app)
    {
        this.app=app;ai=app.Assistant;var p=details;for(int i=0;i<7;i++)p.RowDefinitions.Add(new(){Height=GridLength.Auto});int row=0;void Add(UIElement child){Grid.SetRow(child,row++);p.Children.Add(child);}
        var head=UI.Heading(Localization.Text("Naby voice"),18);head.Margin=new(0,0,0,8);Add(head);
        halo.RenderTransform=pulse;halo.RenderTransformOrigin=new(.5,.5);var hero=new Grid{Width=180,Height=180};hero.Children.Add(halo);hero.Children.Add(orb);stage.Children.Add(new Viewbox{Child=hero,Stretch=Stretch.Uniform});Add(stage);
        phase.HorizontalAlignment=HorizontalAlignment.Center;phase.Margin=new(0,0,0,6);Add(phase);caption.TextAlignment=TextAlignment.Center;caption.MaxHeight=46*Accessibility.TextScale;caption.LineHeight=23*Accessibility.TextScale;Add(caption);heard.TextAlignment=TextAlignment.Center;heard.MaxHeight=19*Accessibility.TextScale;heard.Margin=new(0,4,0,5);Add(heard);
        primary=AssistantControls.Action("Start conversation","mic",()=>_=Toggle(),true);primary.MinHeight=40;primary.MaxWidth=260;primary.HorizontalAlignment=HorizontalAlignment.Stretch;primary.Margin=new(0,12,0,6);
        // A finite grid lets the action label wrap at high text/panel scales.
        if(primary.Content is StackPanel action && action.Children[1] is TextBlock label){var glyph=action.Children[0];action.Children.Clear();var content=new Grid();content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});content.ColumnDefinitions.Add(new());content.Children.Add(glyph);label.TextWrapping=TextWrapping.Wrap;label.TextAlignment=TextAlignment.Center;Grid.SetColumn(label,1);content.Children.Add(label);primary.Content=content;}
        error.Margin=new(0,2,0,4);error.TextAlignment=TextAlignment.Center;error.MaxHeight=30*Accessibility.TextScale;Add(error);privacy.TextAlignment=TextAlignment.Center;privacy.Margin=new(0,4,0,0);Add(privacy);
        footer=AssistantControls.Connection(app,out connection,true);controls.Children.Add(primary);controls.Children.Add(footer);layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var viewport=detailsViewport=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};UI.LocalizedName(viewport,"Voice conversation details");layout.Children.Add(viewport);Grid.SetRow(controls,1);layout.Children.Add(controls);
        Content=pageViewport=new ScrollViewer{Content=layout,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>Unsubscribe();IsVisibleChanged+=(_,_)=>{if(IsVisible)Subscribe();else Unsubscribe();};
    }
    protected override Size MeasureOverride(Size constraint)
    {
        // The island's outer StackPanel measures pages with infinite height. Its
        // host supplies this page's finite viewport after measuring navigation.
        // Reserve the actual text/actions first; the decorative stage yields.
        double pageWidth=ContentWidth(pageViewport,constraint.Width);
        var textConstraint=new Size(ContentWidth(detailsViewport,pageWidth),double.PositiveInfinity);double reserved=stage.Margin.Top+stage.Margin.Bottom;
        foreach(FrameworkElement child in details.Children)if(child!=stage){child.Measure(textConstraint);reserved+=child.DesiredSize.Height;}
        controls.Measure(new Size(pageWidth,double.PositiveInfinity));reserved+=controls.DesiredSize.Height;
        stage.Height=double.IsFinite(constraint.Height)?Math.Clamp(constraint.Height-reserved,48,190):190;
        // Keep session controls pinned while descriptions scroll on short screens.
        // Below their combined natural height the whole page has a scroll fallback.
        layout.Height=double.IsFinite(constraint.Height)?Math.Max(constraint.Height,controls.DesiredSize.Height+48):double.NaN;
        return base.MeasureOverride(constraint);
    }
    private static double ContentWidth(ScrollViewer viewport,double width)
    {
        viewport.ApplyTemplate();
        // The app's scroll chrome reserves its gutter even when Auto is quiet.
        // Measure text at the same width it receives inside both presenters.
        if(viewport.Template.FindName("ScrollGutter",viewport) is ColumnDefinition gutter)
            width-=gutter.Width.IsAbsolute?gutter.Width.Value:gutter.ActualWidth;
        return Math.Max(0,width-viewport.Padding.Left-viewport.Padding.Right);
    }
    private void Subscribe(){if(subscribed || !IsLoaded || !IsVisible)return;subscribed=true;ai.Changed+=QueueRefresh;ai.VisualChanged+=Visual;app.SettingsChanged+=Visual;Refresh();}
    private void Unsubscribe(){if(!subscribed)return;subscribed=false;ai.Changed-=QueueRefresh;ai.VisualChanged-=Visual;app.SettingsChanged-=Visual;}
    private void QueueRefresh()
    {
        if(refreshQueued || !subscribed)return;refreshQueued=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>{refreshQueued=false;if(subscribed)Refresh();}));
    }
    private async System.Threading.Tasks.Task Toggle()
    {
        if(ai.VoiceConnected || ai.Busy){await ai.EndVoice();return;}
        if(!ai.Options.Enabled || !ai.Setup.Voice.Configured){app.ShowVoiceSettings();return;}
        await ai.StartVoice();
    }
    private void Refresh()
    {
        if(!IsVisible)return;AssistantControls.SetActionText(primary,ai.VoiceConnected || ai.Busy?"End conversation":!ai.Options.Enabled || !ai.Setup.Voice.Configured?"Set up voice":ai.State==AssistantState.Error?"Try again":"Start conversation");AssistantControls.RefreshConnection(connection,ai,true);privacy.Text=ai.VoiceConnected?(ai.Options.WakeWord.Enabled?"End conversation returns to local wake listening. Stop all microphones from the tray to pause both.":Localization.Text("End conversation turns off the microphone.")):ai.Options.WakeWord.Enabled?ai.WakeStatus:Localization.Text("Your microphone stays off until you start.");privacy.MaxHeight=32*Accessibility.TextScale;privacy.Text=ai.CaptureSummary+"\n"+privacy.Text;privacy.ToolTip=privacy.Text;
        Accessibility.Announce(phase,ai.WakeListening?Localization.Format("Waiting for “{0}”",ai.Options.WakeWord.Phrase):ai.State switch{AssistantState.Listening=>Localization.Text("Listening"),AssistantState.Speaking=>Localization.Text("Speaking"),AssistantState.Thinking=>ai.Status,AssistantState.Connecting=>Localization.Text("Connecting"),AssistantState.Error=>Localization.Text("Connection needs attention"),_=>Localization.Text("Ready to talk")});phase.TextTrimming=TextTrimming.CharacterEllipsis;phase.TextWrapping=TextWrapping.NoWrap;phase.ToolTip=ai.Status;
        string answer=ai.VoiceCaption.Length>0 && ai.State==AssistantState.Speaking?ai.VoiceCaption:ai.Conversation.Messages.LastOrDefault(m=>m.Role=="assistant")?.Text??Localization.Text("Speak naturally. Naby will reply.");
        answer=AssistantMarkdown.Spoken(answer);if(answer.Length>220)answer=answer[..217]+"…";
        caption.ToolTip=answer;
        if(caption.Text!=answer){caption.Text=answer;if(Motion.Enabled){caption.Opacity=0;Motion.Fade(caption,1,240);}}
        var user=ai.Conversation.Messages.LastOrDefault(m=>m.Role=="user");heard.Visibility=user==null?Visibility.Collapsed:Visibility.Visible;heard.Text=user==null?"":user.Text.Length>110?user.Text[..107]+"…":user.Text;
        error.Text=!ai.Options.Enabled?Localization.Text("Open Voice settings to enable AI and choose a speech connection."):!ai.Setup.Voice.Configured?ai.Setup.Voice.Detail:ai.State is AssistantState.Error or AssistantState.Connecting?ai.Status:ai.VoiceTiming?.Summary??"";error.Visibility=error.Text.Length>0?Visibility.Visible:Visibility.Collapsed;error.ToolTip=!ai.Setup.Voice.Configured?ai.Setup.Voice.Detail:ai.State is AssistantState.Error or AssistantState.Connecting?ai.Status:ai.VoiceTiming?.Detail;InvalidateMeasure();Visual();
    }
    private void Visual()
    {
        if(!IsVisible)return;orb.State=ai.WakeListening?AssistantState.Listening:ai.State;orb.Live=ai.VoiceConnected || ai.WakeListening;orb.Level=ai.Level;orb.ReduceMotion=app.Settings.ReduceMotion || !Motion.Enabled;
        double level=Math.Clamp(ai.Level,0,1);pulse.ScaleX=pulse.ScaleY=app.Settings.ReduceMotion || !Motion.Enabled?1:1+level*.12;halo.Opacity=ai.VoiceConnected?.2+level*.45:.12;
    }
}
