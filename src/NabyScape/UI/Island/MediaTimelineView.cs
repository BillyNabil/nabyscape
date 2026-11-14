using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

// Marks the hover progress hit area so carousel swipes and click-to-open ignore it.
internal sealed class PeekSeekStrip : Border {}

public sealed partial class OverlayWindow
{
    private readonly TextBlock timelineNotice=UI.Caption("This player doesn't share its position",11,UI.Muted);
    private readonly PeekSeekStrip peekSeek=new(){Background=Brushes.Transparent,Cursor=Cursors.Hand,IsHitTestVisible=false};
    private bool peekSeeking;
    private void InitializeMediaTimeline()
    {
        // The notice takes the place of the labels and slider so the panel height never jumps.
        timelineNotice.TextWrapping=TextWrapping.NoWrap;timelineNotice.TextTrimming=TextTrimming.CharacterEllipsis;timelineNotice.VerticalAlignment=VerticalAlignment.Center;timelineNotice.Visibility=Visibility.Collapsed;
        Grid.SetRowSpan(timelineNotice,2);timelinePanel.Children.Add(timelineNotice);
        peekSeek.ToolTip=Localization.Text("Click or drag to seek");UI.LocalizedName(peekSeek,"Seek in the hover preview");
        Grid.SetRow(peekSeek,1);Grid.SetColumnSpan(peekSeek,3);peekMedia.Children.Add(peekSeek);
        peekSeek.MouseLeftButtonDown+=(_,e)=>{peekSeeking=true;peekSeek.CaptureMouse();PreviewPeekSeek(e);e.Handled=true;};
        peekSeek.MouseMove+=(_,e)=>{if(peekSeeking)PreviewPeekSeek(e);};
        peekSeek.MouseLeftButtonUp+=(_,e)=>{if(!peekSeeking)return;double target=PreviewPeekSeek(e);peekSeeking=false;peekSeek.ReleaseMouseCapture();_=CommitSeek(target);e.Handled=true;};
        peekSeek.LostMouseCapture+=(_,_)=>peekSeeking=false;
    }
    private double PreviewPeekSeek(MouseEventArgs e)
    {
        double width=peekProgress.ActualWidth,target=width>0?Math.Clamp(e.GetPosition(peekProgress).X/width,0,1):0;
        peekProgress.Value=target;return target;
    }
    private async Task CommitSeek(double fraction)
    {
        // Continue interpolating from the requested point until the player publishes its new position.
        if(timelineDuration>0){timelineAt=DateTimeOffset.UtcNow;timelineElapsed=fraction*timelineDuration;}
        await app.Media.Seek(fraction);
    }
    // None: no dead 0:00 slider. ReadOnly: progress at full contrast without seeking. Seekable: both.
    private void ApplyTimelineState(MediaTimelineState state,bool media)
    {
        bool shown=state!=MediaTimelineState.None,seekable=state==MediaTimelineState.Seekable;
        var visibility=shown?Visibility.Visible:Visibility.Hidden;
        foreach(var labels in timelinePanel.Children.OfType<DockPanel>())labels.Visibility=visibility;
        seekSlider.Visibility=visibility;seekSlider.IsEnabled=shown;seekSlider.IsHitTestVisible=seekSlider.Focusable=seekable;
        timelineNotice.Visibility=!shown && media?Visibility.Visible:Visibility.Collapsed;
        string help=state==MediaTimelineState.ReadOnly?Localization.Text("This player doesn't allow seeking"):"";
        timelinePanel.ToolTip=help.Length>0?help:null;AutomationProperties.SetHelpText(seekSlider,help);
    }
    private void ApplyPeekSeek(bool media,double duration)
    {
        bool seekable=media && duration>0 && app.Media.CanSeek;
        peekSeek.IsHitTestVisible=seekable;if(!seekable && peekSeeking){peekSeeking=false;peekSeek.ReleaseMouseCapture();}
    }
}
