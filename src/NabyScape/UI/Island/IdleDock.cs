using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly bool idleDockReady;
    private readonly TextBlock idleDate=new(){FontSize=11,FontWeight=FontWeights.Medium,Foreground=UI.Muted,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.NoWrap,ClipToBounds=true,Visibility=Visibility.Collapsed};
    private DateTime idleDateDay;
    private bool idleClosed;
    private double lastClosedWidth=double.NaN;
    private bool? idleLayoutState;
    private bool ClosedVoiceActivity=>app.Assistant.VoiceConnected || app.Assistant.Busy || app.Assistant.Desktop.Sharing;
    private bool IdleContent=>!FeedbackVisible && app.Notifications.Current==null && !ClosedVoiceActivity &&
        !(ClosedMediaActive) &&
        !(WidgetOn("focus") && app.Session.Focus.Remaining(DateTimeOffset.UtcNow)>0) &&
        !(WidgetOn("timer") && app.Session.Timer.Remaining(DateTimeOffset.UtcNow)>0);
    private double ClosedSurfaceWidth=>ClosedLyricsWidth;
    private double ClosedContentWidth=>ClosedClockLayout.ContentWidth(ClosedSurfaceWidth,expandedWidth,surface.Notch);
    private ClosedClockSlot? clockSlot;
    // Applies the Settings > Layout > Closed island clock position (see ClosedClockLayout for the rules).
    private void PlaceClosedClock()
    {
        var slot=ClosedClockLayout.Slot(app.Settings.ClosedClockPosition,idleClosed);
        if(clockSlot!=slot && compactClock.Parent is Grid header)
        {
            clockSlot=slot;Grid.SetColumn(compactClock,ClosedClockLayout.Column(slot));Grid.SetColumnSpan(compactClock,slot==ClosedClockSlot.Center?header.ColumnDefinitions.Count:1);
            compactClock.Margin=new(ClosedClockLayout.Gap(slot),0,0,0);compactClock.HorizontalAlignment=slot switch{ClosedClockSlot.Leading=>HorizontalAlignment.Left,ClosedClockSlot.Center=>HorizontalAlignment.Center,_=>HorizontalAlignment.Stretch};
        }
        double inset=ClosedClockLayout.LeadingInset(slot,idleClosed,compactClock.Visibility==Visibility.Visible,Math.Max(compactClock.ActualWidth,compactClock.DesiredSize.Width));
        if(Math.Abs(activity.Margin.Left-inset)>.5){activity.Margin=new(inset,0,9,0);closedCover.Margin=new(inset,0,9,0);feedbackIcon.Margin=new(inset,0,0,0);}
    }
    private string ClosedVoiceLabel=>app.Assistant.State switch
    {
        AssistantState.Listening=>"Listening",AssistantState.Thinking=>"Thinking…",AssistantState.Speaking=>"Speaking",AssistantState.Connecting=>"Connecting…",AssistantState.Error=>"Voice needs attention",_=>app.Assistant.Desktop.Sharing?"Screen sharing":"Naby voice"
    };
    private void RefreshIdleDock()
    {
        if(!idleDockReady)return;
        idleClosed=IdleContent && !TuckRequested && !dockEditing;
        if(idleLayoutState!=idleClosed)
        {
            idleLayoutState=idleClosed;
            Accessibility.SetFontSize(compactClock,idleClosed?11:12);compactClock.FontWeight=idleClosed?FontWeights.Medium:FontWeights.Bold;
            if(idleClosed){activity.Visibility=closedTitleWave.Visibility=closedCover.Visibility=summary.Visibility=Visibility.Collapsed;}
            else summary.Visibility=closedTitleWave.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
            UpdateAmbientMotion();
        }
        PlaceClosedClock();
        if(idleClosed)summary.Visibility=Visibility.Collapsed;
        // Equal side clearance keeps the date on the dock's center, independent of the clock or character;
        // a centered clock takes the middle, so the date moves to the left edge.
        var dateRoom=ClosedClockLayout.Date(clockSlot??ClosedClockSlot.Leading,ClosedContentWidth-compact.Padding.Left-compact.Padding.Right,compactClock.ActualWidth);
        idleDate.MaxWidth=dateRoom.MaxWidth;idleDate.HorizontalAlignment=dateRoom.Leading?HorizontalAlignment.Left:HorizontalAlignment.Center;idleDate.TextAlignment=dateRoom.Leading?TextAlignment.Left:TextAlignment.Center;
        idleDate.Visibility=idleClosed && app.Settings.ShowDate && compactClock.Visibility==Visibility.Visible && dateRoom.Visible?Visibility.Visible:Visibility.Collapsed;
        if(idleDate.Visibility==Visibility.Visible && idleDateDay!=DateTime.Today)
        {
            idleDateDay=DateTime.Today;var culture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
            idleDate.Inlines.Clear();
            idleDate.Inlines.Add(new Run(idleDateDay.ToString("ddd",culture).ToUpperInvariant()){FontSize=9*Accessibility.TextScale,Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#9DA3AD")});
            idleDate.Inlines.Add(new Run("  ·  "){FontSize=9*Accessibility.TextScale,Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#626873")});
            idleDate.Inlines.Add(new Run(idleDateDay.ToString("dd",culture)){FontSize=12*Accessibility.TextScale,FontWeight=FontWeights.SemiBold,Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#E5E8ED")});
            idleDate.Inlines.Add(new Run(" "+idleDateDay.ToString("MMM",culture).ToUpperInvariant()){FontSize=10*Accessibility.TextScale,Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#B7BDC7")});
            idleDate.ToolTip=idleDateDay.ToString("dddd, dd MMMM yyyy",culture);
        }
        compact.Width=ClosedContentWidth;assistantCharacterHost.Width=ClosedContentWidth;
        if(!expanded && !peeking && !userHidden && !TuckRequested && !dockEditing)
        {
            double target=ClosedSurfaceWidth;
            if(lastClosedWidth!=target || !motion.Active && Math.Abs(surface.SurfaceWidth-target)>.5)
            {lastClosedWidth=target;motion.Go(surface,IslandSurface.SurfaceWidthProperty,target,app.Settings.AnimationMs);WakeLiveRendering();}
        }
    }
}
