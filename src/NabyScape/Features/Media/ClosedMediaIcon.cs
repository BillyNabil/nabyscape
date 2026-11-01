using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly ActivityVisual closedTitleWave=new(){Width=double.NaN,MaxWidth=104,Height=22,WaveOnly=true,Slender=true,HorizontalAlignment=HorizontalAlignment.Stretch,Visibility=Visibility.Collapsed};
    internal double ClosedTitleWaveWidth=>closedTitleWave.ActualWidth;
    internal double ClosedTitleWaveLevel=>closedTitleWave.Envelope;
    private bool? closedWaveTarget;
    private int closedWaveTransition;
    internal bool ClosedTitleWaveVisible=>closedWaveTarget==true;
    private readonly Image closedCoverImage=new(){Width=20,Height=20,Stretch=Stretch.UniformToFill};
    private readonly Border closedCover=new(){Width=20,Height=20,CornerRadius=new(5),Clip=new RectangleGeometry(new Rect(0,0,20,20),5,5),Margin=new(0,0,9,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,IsHitTestVisible=true};
    internal bool ClosedCoverVisible=>closedCover.Visibility==Visibility.Visible && closedCoverImage.Source!=null;
    private bool UpdateClosedMediaWaveform()
    {
        bool waveform=app.Notifications.Current==null && !expanded && !peeking && !FeedbackVisible && ActiveCountdown()==null && !ClosedVoiceActivity && ClosedMediaActive &&
            (app.Settings.ClosedMediaContent=="Waveform" || app.Settings.ClosedMediaContent=="Lyrics" && ClosedLyricIndex<0);
        bool changed=closedWaveTarget!=waveform;
        bool crossfade=app.Settings.ClosedMediaContent=="Lyrics" && ClosedMediaActive && !expanded && !peeking && !FeedbackVisible && ActiveCountdown()==null && !ClosedVoiceActivity && app.Notifications.Current==null && Motion.Enabled && !app.Settings.ReduceMotion;
        if(changed || !crossfade)
        {
            bool initialized=closedWaveTarget.HasValue;closedWaveTarget=waveform;int revision=++closedWaveTransition;
            double waveOpacity=closedTitleWave.Opacity,textOpacity=closedSummaryHost.Opacity;
            closedTitleWave.BeginAnimation(OpacityProperty,null);closedSummaryHost.BeginAnimation(OpacityProperty,null);
            closedTitleWave.Opacity=waveOpacity;closedSummaryHost.Opacity=textOpacity;
            if(crossfade && initialized)
            {
                closedTitleWave.Visibility=Visibility.Visible;summary.Visibility=Visibility.Visible;
                void Fade(FrameworkElement element,double target)
                {
                    var animation=new System.Windows.Media.Animation.DoubleAnimation(element.Opacity,target,TimeSpan.FromMilliseconds(300)){EasingFunction=new System.Windows.Media.Animation.SineEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut}};
                    System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(animation,Motion.FrameLimit);
                    animation.Completed+=(_,_)=>{if(revision!=closedWaveTransition)return;element.BeginAnimation(OpacityProperty,null);element.Opacity=target;if(ReferenceEquals(element,closedTitleWave) && !waveform)closedTitleWave.Visibility=Visibility.Collapsed;};
                    element.BeginAnimation(OpacityProperty,animation);
                }
                Fade(closedTitleWave,waveform?1:0);Fade(closedSummaryHost,waveform?0:1);
            }
            else
            {
                closedTitleWave.Opacity=waveform?1:0;closedSummaryHost.Opacity=waveform?0:1;
                closedTitleWave.Visibility=waveform?Visibility.Visible:Visibility.Collapsed;summary.Visibility=waveform?Visibility.Collapsed:Visibility.Visible;
            }
        }
        compact.ToolTip=waveform?app.Media.Title+" · click to open · Esc to close":"Click to open · Esc to close";
        if(changed)ConfigureLiveActivity();
        return waveform;
    }
    private void UpdateClosedMediaIcon()
    {
        compact.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        var now=DateTimeOffset.Now;
        bool countdown=(WidgetOn("focus") && app.Session.Focus.Remaining(now)>0 || WidgetOn("timer") && app.Session.Timer.Remaining(now)>0);
        bool cover=!FeedbackVisible && !countdown && !ClosedVoiceActivity && app.Settings.ClosedMediaIcon=="Artwork" && ClosedMediaActive && app.Media.Artwork!=null;
        bool waveform=UpdateClosedMediaWaveform();
        if(!app.Media.Playing || app.Audio.MediaMeterMuted || app.Settings.ReduceMotion || !app.Settings.LiveActivity)closedTitleWave.Sample(0,1,0,false,false,false,0);
        closedCover.Visibility=cover?Visibility.Visible:Visibility.Collapsed;
        activity.Visibility=FeedbackVisible || cover || waveform || ClosedVoiceActivity || ((app.Settings.ClosedMediaContent is "Lyrics" or "None" || app.Settings.ClosedMediaIcon=="None") && !countdown) || !(countdown || ClosedMediaActive)?Visibility.Collapsed:Visibility.Visible;
        if(cover)SwapArtwork(closedCoverImage,app.Media.Artwork);
        RefreshIdleDock();UpdateClosedLyricsDemand();
    }
}
