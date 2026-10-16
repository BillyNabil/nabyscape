using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly ActivityVisual closedTitleWave=new(){Width=double.NaN,MaxWidth=72,Height=12,WaveOnly=true,Slender=true,HorizontalAlignment=HorizontalAlignment.Stretch,Visibility=Visibility.Collapsed};
    internal double ClosedTitleWaveWidth=>closedTitleWave.ActualWidth;
    internal double ClosedTitleWaveLevel=>closedTitleWave.Envelope;
    internal bool ClosedTitleWaveVisible=>closedTitleWave.Visibility==Visibility.Visible && summary.Visibility==Visibility.Collapsed;
    private readonly Image closedCoverImage=new(){Width=20,Height=20,Stretch=Stretch.UniformToFill};
    private readonly Border closedCover=new(){Width=20,Height=20,CornerRadius=new(5),Clip=new RectangleGeometry(new Rect(0,0,20,20),5,5),Margin=new(0,0,9,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,IsHitTestVisible=true};
    internal bool ClosedCoverVisible=>closedCover.Visibility==Visibility.Visible && closedCoverImage.Source!=null;
    private void UpdateClosedMediaIcon()
    {
        compact.Visibility=expanded?Visibility.Collapsed:Visibility.Visible;
        var now=DateTimeOffset.Now;
        bool countdown=!ShowMediaPrimary && (WidgetOn("focus") && app.Session.Focus.Remaining(now)>0 || WidgetOn("timer") && app.Session.Timer.Remaining(now)>0);
        bool cover=!FeedbackVisible && !countdown && app.Settings.ClosedMediaIcon=="Artwork" && WidgetOn("media") && app.Media.Available && app.Media.Artwork!=null;
        bool waveform=!expanded && !peeking && !FeedbackVisible && !countdown && WidgetOn("media") && app.Media.Available && app.Settings.ClosedMediaContent=="Waveform";
        closedTitleWave.Visibility=waveform?Visibility.Visible:Visibility.Collapsed;summary.Visibility=waveform?Visibility.Collapsed:Visibility.Visible;
        compact.ToolTip=waveform?app.Media.Title+" · click to open · Esc to close":"Click to open · Esc to close";
        if(!app.Media.Playing || app.Audio.MediaMeterMuted || app.Settings.ReduceMotion || !app.Settings.LiveActivity)closedTitleWave.Sample(0,1,0,false,false,false,0);
        closedCover.Visibility=cover?Visibility.Visible:Visibility.Collapsed;
        activity.Visibility=FeedbackVisible || cover || waveform || !(countdown || WidgetOn("media") && app.Media.Available)?Visibility.Collapsed:Visibility.Visible;
        if(cover)SwapArtwork(closedCoverImage,app.Media.Artwork);
    }
}
