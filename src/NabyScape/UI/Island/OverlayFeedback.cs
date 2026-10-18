using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private bool feedbackBrightness;
    private readonly TextBlock feedbackIcon=new(){FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,Foreground=Brushes.White,Width=27,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
    private readonly ProgressBar feedbackLevel=new(){Width=34,Height=3,Minimum=0,Maximum=1,Margin=new(7,0,0,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
    private readonly DispatcherTimer feedbackTick=new(){Interval=TimeSpan.FromMilliseconds(80)};
    private void InitializeFeedback(){app.Brightness.Changed+=BrightnessChanged;feedbackTick.Tick+=(_,_)=>{if(!FeedbackVisible)feedbackTick.Stop();Update();};}
    private void BrightnessChanged(int percent)
    {
        if(Dispatcher.HasShutdownStarted)return;
        Dispatcher.BeginInvoke(()=>{ShowFeedback(true,percent/100d,false);Update();});
    }
    private void ShowFeedback(bool brightness,double level,bool muted)
    {
        if(!app.Settings.ReactiveFeedback || !(brightness?app.Settings.BrightnessFeedback:app.Settings.VolumeFeedback) || expanded || peeking || !IsVisible || TuckRequested || userHidden)return;
        feedbackBrightness=brightness;
        feedbackText=muted?"Muted":$"{Math.Clamp(level,0,1)*100:0}%";
        feedbackIcon.Text=brightness?"\uE706":muted?"\uE74F":"\uE767";
        feedbackIcon.ToolTip=brightness?"Brightness":"Volume";
        feedbackUntil=DateTimeOffset.UtcNow.AddSeconds(2);
        double previous=feedbackLevel.Value;feedbackLevel.BeginAnimation(ProgressBar.ValueProperty,null);feedbackLevel.Value=muted?0:level;
        if(Motion.Enabled)feedbackLevel.BeginAnimation(ProgressBar.ValueProperty,new DoubleAnimation(previous,feedbackLevel.Value,TimeSpan.FromMilliseconds(170)){FillBehavior=FillBehavior.Stop});
        feedbackTick.Start();RefreshFeedback();
    }
    private void RefreshFeedback()
    {
        bool visible=FeedbackVisible;feedbackIcon.Visibility=feedbackLevel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;UpdateClosedMediaIcon();
        // At the minimum notch width, optional widgets would squeeze out the percentage.
        summary.MinWidth=visible?40:0;RefreshCompactOptionalVisibility();
        if(!visible){feedbackUntil=default;feedbackTick.Stop();}
    }
    private void RefreshCompactOptionalVisibility()
    {
        RefreshAssistantCharacter();bool feedback=FeedbackVisible;
        compactClock.Visibility=!feedback && System.Linq.Enumerable.Any(app.Settings.Widgets,w=>w.Id=="clock" && w.Enabled)?Visibility.Visible:Visibility.Collapsed;
        awakeMark.Visibility=!feedback && app.Awake?.Active==true?Visibility.Visible:Visibility.Collapsed;
    }
    private void ClearFeedback(){feedbackUntil=default;feedbackTick.Stop();RefreshFeedback();}
    private void StopFeedback(){feedbackTick.Stop();app.Brightness.Changed-=BrightnessChanged;}
    internal void DiagnosticFeedFeedback(bool brightness,int percent,bool muted=false)
    {
        if(!DiagnosticRun)throw new InvalidOperationException("Feedback injection is only available in diagnostics.");
        ShowFeedback(brightness,Math.Clamp(percent,0,100)/100d,muted);Update();
    }
}
