using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly StackPanel assistantPeekWords=new(){VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,0,0)};
    private readonly WrapPanel assistantPeekActions=new(){Margin=new(0,6,0,0)};
    private readonly DockPanel assistantReplyActions=new(){Margin=new(0,6,0,0),Visibility=Visibility.Collapsed};
    private AssistantMessage? assistantPendingReply,assistantDisplayedReply;
    private bool assistantReplyShowing;
    private long assistantReplyUntil,assistantReplyQueuedUntil;
    private double assistantReplyReadingMs;
    private string assistantReplyPlain="";
    private long assistantVoiceReplySeen;
    private bool assistantVoicePreview;
    // The reply owns its reading time; the normal hover-leave timer must not dismiss it.
    private bool AssistantReplyVisible=>assistantPeeking && assistantReplyShowing;

    private AssistantMessage? LatestAssistantReply()=>app.Assistant.Conversation.Messages.LastOrDefault(m=>m.Role=="assistant" && (m.Calls==null || m.Calls.Count==0) && !string.IsNullOrWhiteSpace(m.Text) && !m.Text.StartsWith("Reviewed voice action outcome") && !m.Text.StartsWith("The reply was interrupted"));

    private void InitializeAssistantReply()
    {
        var dismiss=Icons.Button("x","Dismiss reply",Collapse,size:28);dismiss.Content=Icons.Create("x",14);dismiss.Margin=new(8,0,0,0);DockPanel.SetDock(dismiss,Dock.Right);assistantReplyActions.Children.Add(dismiss);
        var open=AssistantWindow.Pill("Open chat",()=>OpenAssistant());open.HorizontalAlignment=HorizontalAlignment.Left;open.MinHeight=open.Height=28;open.Padding=new(10,3,10,3);open.Margin=new(0);assistantReplyActions.Children.Add(open);
        Grid.SetRow(assistantReplyActions,1);assistantPeek.Children.Add(assistantReplyActions);
        AutomationProperties.SetName(assistantPeek,"Naby reply");
        AutomationProperties.SetLiveSetting(assistantCaption,AutomationLiveSetting.Polite);
    }

    private void ApplyAssistantPeekMode()
    {
        assistantPeekActions.Visibility=assistantReplyShowing?Visibility.Collapsed:Visibility.Visible;
        assistantReplyActions.Visibility=assistantReplyShowing?Visibility.Visible:Visibility.Collapsed;
        peekAssistant.Width=peekAssistant.Height=assistantReplyShowing?64:72;
        assistantGreeting.FontSize=assistantReplyShowing?11:17;
        assistantGreeting.Foreground=assistantReplyShowing?UI.Muted:UI.Brush("#F2F3F5");
        assistantCaption.FontSize=assistantReplyShowing?13:11;
        assistantCaption.Foreground=assistantReplyShowing?UI.Brush("#F2F3F5"):UI.Muted;
        assistantCaption.LineHeight=assistantReplyShowing?18:double.NaN;
        assistantCaption.MaxHeight=assistantReplyShowing?90:46;
    }

    private void RefreshAssistantReplyText()
    {
        if(!assistantReplyShowing || assistantDisplayedReply==null)return;
        assistantGreeting.Text=app.Assistant.State==AssistantState.Speaking?"Naby · Speaking":"Naby · Reply";
        Motion.Text(assistantCaption,assistantReplyPlain);
    }

    private void UpdateAssistantReply(AssistantMessage? reply,bool fresh)
    {
        long serial=app.Assistant.VoiceReplySerial;
        bool localVoice=app.Assistant.VoiceConnected && serial>0 && app.Assistant.VoiceTiming!=null;
        if(localVoice && fresh)
        {
            if(assistantVoiceReplySeen==serial)
            {
                // Upgrade an early spoken preview to the completed reply without
                // reopening a panel the user has already dismissed for this turn.
                if(assistantVoicePreview && reply!=null)
                {
                    assistantVoicePreview=false;
                    if(assistantPendingReply!=null)assistantPendingReply=reply;
                    if(AssistantReplyVisible){SetAssistantReplyContent(reply);RefreshAssistantReplyText();}
                }
                fresh=false;
            }
            else{assistantVoiceReplySeen=serial;assistantVoicePreview=false;}
        }
        else if(localVoice && app.Assistant.State==AssistantState.Speaking && app.Assistant.VoiceCaption.Length>0)
        {
            if(assistantVoiceReplySeen!=serial)
            {assistantVoiceReplySeen=serial;assistantVoicePreview=true;reply=new("assistant",app.Assistant.VoiceCaption);fresh=true;}
            else if(assistantVoicePreview)
            {
                var preview=new AssistantMessage("assistant",app.Assistant.VoiceCaption);
                if(assistantPendingReply!=null)assistantPendingReply=preview;
                if(AssistantReplyVisible){SetAssistantReplyContent(preview);RefreshAssistantReplyText();}
                reply=preview;
            }
        }
        if(!app.Assistant.Options.Enabled || reply==null)
        {
            assistantPendingReply=null;assistantReplyDismiss.Stop();
            if(AssistantReplyVisible)Collapse();
            return;
        }
        if(expanded || userHidden || !IsVisible){assistantPendingReply=null;assistantReplyDismiss.Stop();return;}
        RefreshAssistantReplyText();
        if(fresh && (app.Assistant.VoiceConnected || app.Assistant.Options.IslandCharacter))
        {
            assistantPendingReply=reply;assistantReplyQueuedUntil=Environment.TickCount64+60000;
            assistantReplyDismiss.Start();TryShowAssistantReply();
        }
    }

    private void TryShowAssistantReply()
    {
        if(assistantPendingReply==null || expanded || userHidden || !IsVisible || TuckRequested || IsTucked || dockEditing || FeedbackVisible || fileDragActive || PeekInteractionActive || PointerInside() || app.Notifications.Current!=null || (peeking && !AssistantReplyVisible))return;
        SetAssistantReplyContent(assistantPendingReply);assistantPendingReply=null;
        assistantReplyUntil=Environment.TickCount64+(long)assistantReplyReadingMs;
        if(AssistantReplyVisible){RefreshAssistantReplyText();Motion.Enter(assistantPeekWords,4);}
        else PreviewAssistant(true);
    }
    private void SetAssistantReplyContent(AssistantMessage reply)
    {
        assistantDisplayedReply=reply;
        string plain=AssistantMarkdown.Spoken(reply.Text);
        plain=string.Join(" ",plain.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        if(plain.Length==0)plain="Your reply is ready. Open chat to read it.";
        assistantReplyPlain=plain.Length>560?plain[..560].TrimEnd()+"…":plain;
        assistantReplyReadingMs=Math.Clamp(plain.Length*55,7000,18000);
    }

    private void AssistantReplyTick()
    {
        if(!app.Assistant.Options.Enabled || expanded || userHidden || !IsVisible)
        {assistantPendingReply=null;assistantReplyDismiss.Stop();return;}
        long now=Environment.TickCount64;
        if(assistantPendingReply!=null && now>=assistantReplyQueuedUntil)assistantPendingReply=null;
        TryShowAssistantReply();
        if(AssistantReplyVisible)
        {
            if(app.Assistant.State==AssistantState.Speaking || app.Assistant.Busy || PointerInside() || assistantPeek.IsKeyboardFocusWithin || PeekInteractionActive || fileDragActive)
                assistantReplyUntil=now+(long)assistantReplyReadingMs;
            else if(now>=assistantReplyUntil)Collapse();
        }
        if(assistantPendingReply==null && !AssistantReplyVisible)assistantReplyDismiss.Stop();
    }
}
