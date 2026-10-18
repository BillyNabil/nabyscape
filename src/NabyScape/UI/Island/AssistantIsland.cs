using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly Grid assistantCharacterHost=new(),assistantPeek=new();
    private readonly Border assistantHeaderSpace=new();
    private readonly AssistantOrb closedAssistant=new(){Width=32,Height=32,TrackPointerLocally=false},peekAssistant=new(){Width=72,Height=72,TrackPointerLocally=false};
    private readonly TextBlock assistantGreeting=UI.Text("Hey, I'm here.",17),assistantCaption=UI.Text("Chat or talk, right from your island.",11,UI.Muted);
    private readonly DispatcherTimer assistantHover=new();
    private readonly DispatcherTimer assistantReplyDismiss=new(){Interval=TimeSpan.FromSeconds(8)};
    private AssistantMessage? assistantLastReply;
    private Button assistantTalk=null!;
    private AssistantState? lastAssistantVisualState;
    private AssistantView? islandAssistantView;
    private Button assistantCharacterButton=null!;
    private bool assistantOpen,assistantPeeking;
    private int assistantPeekRevision;
    private Point? assistantLastPointer;
    private bool AssistantPointerTracking=>!DiagnosticRun && !userHidden && !IsTucked && !app.Settings.ReduceMotion && Motion.Enabled && (closedAssistant.IsVisible || peekAssistant.IsVisible);
    private void TrackAssistantPointer()
    {
        if(!AssistantPointerTracking){assistantLastPointer=null;ConfigureDisplayFollow();return;}
        if(!Native.CursorPoint(out var cursor))return;
        TrackAssistantPointerAt(new Point(cursor.X,cursor.Y));
    }
    private void TrackAssistantPointerAt(Point screenPoint)
    {
        if(assistantLastPointer==screenPoint)return;assistantLastPointer=screenPoint;
        void Track(AssistantOrb orb)
        {
            if(!orb.IsVisible)return;
            var local=orb.PointFromScreen(screenPoint);
            orb.LookAt((local.X-orb.ActualWidth/2)/360,(local.Y-orb.ActualHeight/2)/280);
        }
        Track(closedAssistant);Track(peekAssistant);
    }
    private bool AssistantCharacterAvailable=>app.Assistant.Options.IslandCharacter || app.Assistant.VoiceConnected || app.Assistant.Desktop.Sharing || app.Assistant.Busy;
    private bool AssistantSpotifyPlaying=>app.Media.Available && app.Media.Playing && (app.Media.Selected?.SourceAppUserModelId.Contains("Spotify",StringComparison.OrdinalIgnoreCase)==true);
    internal bool AssistantOpen=>assistantOpen && expanded;
    internal bool AssistantPeeking=>assistantPeeking;
    internal AssistantView? IslandAssistantView=>islandAssistantView;
    private double AssistantPeekHeight=>Math.Min(app.Settings.HoverHeight,190);
    private void LayoutAssistantPeek()
    {
        double width=Math.Min(app.Settings.HoverWidth,expandedWidth);var bounds=PeekLayout.SafeBounds(width,AssistantPeekHeight,app.Settings.Radius,surface.Notch);
        assistantPeek.Width=bounds.Width;assistantPeek.Height=bounds.Height;assistantPeek.Margin=new(0,bounds.Top,0,0);
    }
    private void InitializeAssistantIsland(Grid header)
    {
        header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetColumn(assistantHeaderSpace,5);header.Children.Add(assistantHeaderSpace);
        assistantCharacterHost.HorizontalAlignment=HorizontalAlignment.Center;assistantCharacterHost.VerticalAlignment=VerticalAlignment.Top;
        assistantCharacterButton=AssistantOrb.Launcher("AI character on island",()=>OpenAssistant(),closedAssistant);assistantCharacterButton.MinHeight=24;assistantCharacterButton.Margin=new(0,0,9,0);assistantCharacterButton.HorizontalAlignment=HorizontalAlignment.Right;assistantCharacterButton.VerticalAlignment=VerticalAlignment.Center;assistantCharacterButton.ToolTip="AI companion · hover to meet · click to chat";
        assistantCharacterButton.MouseEnter+=(_,_)=>{hoverDelay.Stop();assistantHover.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDelayMs);assistantHover.Start();};
        assistantCharacterButton.MouseLeave+=(_,_)=>assistantHover.Stop();
        assistantCharacterButton.PreviewMouseLeftButtonDown+=(_,_)=>assistantHover.Stop();
        assistantCharacterButton.MouseEnter+=(_,_)=>closedAssistant.SetHover(true);
        assistantCharacterButton.MouseLeave+=(_,_)=>{closedAssistant.SetHover(false);closedAssistant.Press(false);};
        assistantCharacterButton.PreviewMouseLeftButtonDown+=(_,_)=>closedAssistant.Press(true);
        assistantCharacterButton.PreviewMouseLeftButtonUp+=(_,_)=>closedAssistant.Press(false);
        assistantCharacterHost.Children.Add(assistantCharacterButton);visuals.Children.Add(assistantCharacterHost);
        assistantPeek.Visibility=Visibility.Collapsed;assistantPeek.Opacity=0;assistantPeek.HorizontalAlignment=HorizontalAlignment.Center;assistantPeek.VerticalAlignment=VerticalAlignment.Top;
        assistantPeek.RowDefinitions.Add(new());assistantPeek.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var hero=new Grid();hero.ColumnDefinitions.Add(new(){Width=GridLength.Auto});hero.ColumnDefinitions.Add(new());hero.Children.Add(peekAssistant);
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,0,0)};assistantGreeting.FontWeight=FontWeights.SemiBold;assistantGreeting.TextWrapping=TextWrapping.NoWrap;assistantGreeting.TextTrimming=TextTrimming.CharacterEllipsis;words.Children.Add(assistantGreeting);assistantCaption.Margin=new(0,5,0,0);assistantCaption.MaxHeight=46;words.Children.Add(assistantCaption);Grid.SetColumn(words,1);hero.Children.Add(words);assistantPeek.Children.Add(hero);
        var chat=AssistantWindow.Pill("Chat here",()=>OpenAssistant(),true);assistantTalk=AssistantWindow.Pill("Talk",()=>{if(app.Assistant.VoiceConnected)app.Assistant.StopSession();else{OpenAssistant();_=app.Assistant.StartVoice();}});
        var actions=new WrapPanel{Margin=new(0,6,0,0)};actions.Children.Add(chat);actions.Children.Add(assistantTalk);Grid.SetRow(actions,1);assistantPeek.Children.Add(actions);visuals.Children.Add(assistantPeek);
        assistantHover.Tick+=(_,_)=>{assistantHover.Stop();if(assistantCharacterButton.IsMouseOver && !expanded && !userHidden)PreviewAssistant();};
        assistantReplyDismiss.Tick+=(_,_)=>{assistantReplyDismiss.Stop();if(assistantPeeking && !PointerInside())Collapse();};
        app.Assistant.Changed+=AssistantIslandChanged;app.Assistant.VisualChanged+=RefreshAssistantVisuals;app.SettingsChanged+=RefreshAssistantCharacter;
        closedAssistant.IsVisibleChanged+=(_,_)=>{assistantLastPointer=null;ConfigureDisplayFollow();};
        peekAssistant.IsVisibleChanged+=(_,_)=>{assistantLastPointer=null;ConfigureDisplayFollow();};
        RefreshAssistantCharacter();
    }
    private void AssistantIslandChanged()
    {
        RefreshAssistantCharacter();RefreshAssistantVisuals();
        if(!AssistantCharacterAvailable && assistantPeeking)CloseAssistant();
        assistantGreeting.Text=app.Assistant.State switch{AssistantState.Listening=>"I'm listening.",AssistantState.Thinking=>"Let me think…",AssistantState.Speaking=>"Let's talk.",AssistantState.Approval=>"Your call.",AssistantState.Connecting=>"Connecting…",AssistantState.Error=>"Let's check that.",_=>"Hey, I'm here."};
        assistantCaption.Text=!app.Assistant.Options.Enabled?"Set up your companion in Settings → AI.":app.Assistant.Pending!=null?"Review this action in your island.":app.Assistant.State==AssistantState.Idle?"Chat or talk, right from your island.":app.Assistant.Status;
        assistantTalk.Content=app.Assistant.VoiceConnected?"End voice":"Talk";assistantTalk.IsEnabled=!app.Assistant.Busy;
        var reply=app.Assistant.Conversation.Messages.LastOrDefault(m=>m.Role=="assistant" && !string.IsNullOrWhiteSpace(m.Text) && !m.Text.StartsWith("Reviewed voice action outcome") && !m.Text.StartsWith("The reply was interrupted"));
        bool fresh=reply!=null && !ReferenceEquals(reply,assistantLastReply);assistantLastReply=reply;
        if(reply!=null && app.Assistant.Options.Enabled && app.Assistant.State is AssistantState.Idle or AssistantState.Listening or AssistantState.Speaking)assistantCaption.Text=reply.Text.Length>180?reply.Text[..180]+"…":reply.Text;
        if(fresh && app.Assistant.Options.Enabled && app.Assistant.Options.IslandCharacter && IsVisible && !userHidden && !expanded && !peeking && !TuckRequested && !FeedbackVisible && !fileDragActive && app.Notifications.Current==null){PreviewAssistant();assistantReplyDismiss.Start();}
    }
    private void RefreshAssistantVisuals()
    {
        bool spotify=AssistantSpotifyPlaying;
        void Apply(AssistantOrb orb){if(!orb.IsVisible)return;orb.State=app.Assistant.State;orb.Level=app.Assistant.Level;orb.Live=app.Assistant.VoiceConnected;orb.ReduceMotion=app.Settings.ReduceMotion;orb.Music=spotify;}
        Apply(closedAssistant);Apply(peekAssistant);
        if(lastAssistantVisualState==app.Assistant.State)return;lastAssistantVisualState=app.Assistant.State;
        assistantGreeting.Text=app.Assistant.State switch{AssistantState.Listening=>"I'm listening.",AssistantState.Thinking=>"Let me think…",AssistantState.Speaking=>"Let's talk.",AssistantState.Approval=>"Your call.",AssistantState.Connecting=>"Connecting…",AssistantState.Error=>"Let's check that.",_=>"Hey, I'm here."};
    }
    private void RefreshAssistantCharacter()
    {
        if(assistantCharacterButton==null)return;
        bool enabled=AssistantCharacterAvailable;
        assistantCharacterButton.ToolTip=app.Assistant.VoiceConnected?"Microphone on · click for voice controls":app.Assistant.Desktop.Sharing?"Screen sharing on · click for controls":"AI companion · hover to meet · click to chat";
        double size=Math.Clamp(app.Settings.Height-4,24,32);assistantCharacterButton.MinHeight=size;assistantCharacterButton.Width=assistantCharacterButton.Height=size;closedAssistant.Width=closedAssistant.Height=size;
        assistantHeaderSpace.Width=size+9;assistantHeaderSpace.Visibility=enabled && !FeedbackVisible?Visibility.Visible:Visibility.Collapsed;
        assistantCharacterHost.Width=Math.Max(120,Math.Min(app.Settings.Width,expandedWidth)-(surface.Notch?32:12));assistantCharacterHost.Height=app.Settings.Height;
        SyncAssistantVisibility();
        RefreshAssistantVisuals();
    }
    private void SyncAssistantVisibility(){assistantCharacterHost.Visibility=AssistantCharacterAvailable && !expanded && !peeking && !FeedbackVisible?Visibility.Visible:Visibility.Collapsed;if(closedAssistant.IsVisible)closedAssistant.Music=AssistantSpotifyPlaying;ConfigureDisplayFollow();}
    internal void PreviewAssistant()
    {
        if(expanded || userHidden || !AssistantCharacterAvailable || (TuckRequested && !tuckPreview))return;
        assistantPeekRevision++;
        HideNotice();EndPeekGesture();StopHoverTimers();assistantReplyDismiss.Stop();automaticPreview=false;activityDismiss.Stop();peeking=true;assistantPeeking=true;peekDetailed=true;Show();Dimensions();
        peekPanel.Visibility=Visibility.Collapsed;scroll.Visibility=Visibility.Collapsed;compact.IsHitTestVisible=false;Motion.Fade(compact,0,100);
        double width=Math.Min(app.Settings.HoverWidth,expandedWidth),height=AssistantPeekHeight;LayoutAssistantPeek();assistantPeek.Visibility=Visibility.Visible;assistantPeek.IsHitTestVisible=true;
        AssistantIslandChanged();Motion.Fade(assistantPeek,1,app.Settings.AnimationMs);Motion.Enter(peekAssistant,8);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);motion.Go(surface,IslandSurface.SurfaceHeightProperty,height,app.Settings.AnimationMs);motion.Go(surface,IslandSurface.GlassLiftProperty,1,app.Settings.AnimationMs);
        outside?.Dispose();outside=DiagnosticRun?null:new OutsideClick(this,Collapse,p=>VisiblePoint(p));UpdateSatellite();WakeLiveRendering();
    }
    private void HideAssistantPeek()
    {
        int revision=++assistantPeekRevision;
        assistantHover.Stop();assistantReplyDismiss.Stop();assistantPeeking=false;assistantPeek.IsHitTestVisible=false;
        Motion.Fade(assistantPeek,0,100,completed:()=>{if(!assistantPeeking && revision==assistantPeekRevision)assistantPeek.Visibility=Visibility.Collapsed;});
    }
    internal void OpenAssistant(bool activate=true)
    {
        activate&=!DiagnosticRun;
        FlushNotes();commandOpen=activitiesOpen=false;hoverPanelWidget=null;
        if(!assistantOpen){assistantOpen=true;bodyDirty=true;}
        Reveal(activate);ResizeToolPanel();RefreshAssistantCharacter();if(activate)Dispatcher.BeginInvoke(()=>islandAssistantView?.FocusComposer());
    }
    private void MountAssistantView()
    {
        islandAssistantView??=new AssistantView(app,true,CloseAssistant);
        if(islandAssistantView.Parent is Panel parent)parent.Children.Remove(islandAssistantView);
        body.Children.Clear();body.Children.Add(islandAssistantView);
    }
    private void LeaveAssistantPanel(){if(assistantOpen){assistantOpen=false;bodyDirty=true;}}
    internal void CloseAssistant(){LeaveAssistantPanel();Collapse();}
    private void CleanupAssistantIsland()
    {assistantHover.Stop();assistantReplyDismiss.Stop();islandAssistantView?.Dispose();app.Assistant.Changed-=AssistantIslandChanged;app.Assistant.VisualChanged-=RefreshAssistantVisuals;app.SettingsChanged-=RefreshAssistantCharacter;}
}
