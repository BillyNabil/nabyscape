using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private readonly DispatcherTimer closedLyricTick=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private readonly Border closedSummaryHost=new(){ClipToBounds=true,VerticalAlignment=VerticalAlignment.Center};
    private readonly TranslateTransform lyricHorizontal=new(),previousLyricMove=new();
    private readonly TextBlock previousLyric=new(){IsHitTestVisible=false,Opacity=0,TextWrapping=TextWrapping.NoWrap};
    private readonly Grid lyricLayers=new(){ClipToBounds=true};
    private string marqueeKey="",phraseKey="";
    private string[] lyricPhrases=Array.Empty<string>();
    private readonly object closedLyricConsumer=new();
    private bool closedLyricAttached,closedLyricsDisposed;
    private bool ClosedMediaActive=>app.Settings.ClosedMediaContent!="None" && WidgetOn("media") && app.Media.Available && app.Media.Playing;
    private bool WantsClosedLyrics=>!closedLyricsDisposed && app.Settings.ClosedMediaContent=="Lyrics" && ClosedMediaActive &&
        IsVisible && WindowState!=WindowState.Minimized && !userHidden && !IsTucked && !TuckRequested && !expanded && !peeking && !FeedbackVisible && app.Notifications.Current==null && !ClosedVoiceActivity && ActiveCountdown()==null;
    private int ClosedLyricIndex=>LyricTiming.CanUse(app.Media)?LyricTiming.VocalIndex(app.Lyrics.Result.Lines,app.Media.Timeline().elapsed+app.Settings.LyricsOffset):-1;
    private string ClosedMediaText
    {
        get
        {
            if(app.Settings.ClosedMediaContent!="Lyrics")return app.Media.Title;
            var lines=app.Lyrics.Result.Lines;int index=ClosedLyricIndex;
            if(index<0)return "";
            double end=index+1<lines.Length?lines[index+1].At:app.Media.Timeline().duration;
            return ClosedLyricPhrase(lines[index].Text,lines[index].At,end,app.Media.Timeline().elapsed+app.Settings.LyricsOffset);
        }
    }
    private void InitializeClosedLyrics()
    {
        closedSummaryHost.Child=null;lyricLayers.Children.Add(previousLyric);lyricLayers.Children.Add(summary);closedSummaryHost.Child=lyricLayers;
        previousLyric.RenderTransform=previousLyricMove;
        TextOptions.SetTextFormattingMode(summary,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(summary,TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(previousLyric,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(previousLyric,TextRenderingMode.Grayscale);
        closedSummaryHost.SizeChanged+=(_,_)=>{RefreshLyricMarquee();if(WantsClosedLyrics)PaintClosedLyrics();};
        closedLyricTick.Tick+=(_,_)=>{if(!WantsClosedLyrics){UpdateClosedLyricsDemand();return;}PaintClosedLyrics();};
        IsVisibleChanged+=(_,_)=>UpdateClosedLyricsDemand();app.SettingsChanged+=UpdateClosedLyricsDemand;
    }
    private void UpdateClosedLyricsDemand()
    {
        if(!WantsClosedLyrics)
        {
            ResetLyricMarquee();
            closedLyricTick.Stop();if(closedLyricAttached){closedLyricAttached=false;app.Lyrics.Changed-=PaintClosedLyrics;app.Lyrics.Release(closedLyricConsumer);}return;
        }
        if(!closedLyricAttached){closedLyricAttached=true;app.Lyrics.Acquire(closedLyricConsumer);app.Lyrics.Changed+=PaintClosedLyrics;}
        _=app.Lyrics.Load(app.Media.Title,app.Media.Artist,app.Media.Timeline().duration,true,LyricTiming.CanUse(app.Media));
        PaintClosedLyrics();if(app.Lyrics.Result.Lines.Length>0 && LyricTiming.CanUse(app.Media))closedLyricTick.Start();else closedLyricTick.Stop();
    }
    private void PaintClosedLyrics()
    {
        if(!WantsClosedLyrics)return;SetClosedSummary(ClosedMediaText);UpdateClosedMediaWaveform();
        if(app.Lyrics.Result.Lines.Length>0 && LyricTiming.CanUse(app.Media) && !closedLyricTick.IsEnabled)closedLyricTick.Start();
    }
    private void SetClosedSummary(string text)
    {
        if(summary.Text==text){RefreshLyricMarquee();return;}
        bool animate=WantsClosedLyrics && Motion.Enabled && !app.Settings.ReduceMotion && summary.Text.Length>0;
        if(animate)
        {
            previousLyric.Text=summary.Text;previousLyric.FontFamily=summary.FontFamily;previousLyric.FontSize=summary.FontSize;
            previousLyric.FontWeight=summary.FontWeight;previousLyric.Foreground=summary.Foreground;previousLyric.FlowDirection=summary.FlowDirection;
            previousLyric.Padding=summary.Padding;previousLyric.Width=summary.Width;previousLyric.HorizontalAlignment=summary.HorizontalAlignment;
            previousLyricMove.BeginAnimation(TranslateTransform.XProperty,null);previousLyricMove.X=lyricHorizontal.X;
            previousLyricMove.BeginAnimation(TranslateTransform.YProperty,null);previousLyricMove.Y=lyricHorizontal.Y;
            previousLyric.BeginAnimation(OpacityProperty,null);previousLyric.Opacity=summary.Opacity;
            Motion.Fade(previousLyric,0,280);Motion.To(previousLyricMove,TranslateTransform.YProperty,-2,320);
        }
        else{previousLyric.BeginAnimation(OpacityProperty,null);previousLyric.Opacity=0;previousLyric.Text="";}
        Motion.Text(summary,text);marqueeKey="";RefreshLyricMarquee();
        summary.BeginAnimation(OpacityProperty,null);lyricHorizontal.BeginAnimation(TranslateTransform.YProperty,null);
        if(animate){lyricHorizontal.Y=2;summary.Opacity=.18;Motion.To(lyricHorizontal,TranslateTransform.YProperty,0,340);Motion.Fade(summary,1,300);}
        else{summary.Opacity=1;lyricHorizontal.Y=0;}
    }

    private void ResetLyricMarquee()
    {
        marqueeKey="";lyricHorizontal.BeginAnimation(TranslateTransform.XProperty,null);lyricHorizontal.X=0;
        closedSummaryHost.OpacityMask=null;summary.Padding=new Thickness(0);summary.Width=double.NaN;summary.HorizontalAlignment=HorizontalAlignment.Stretch;summary.TextTrimming=TextTrimming.CharacterEllipsis;
    }
    private void RefreshLyricMarquee()
    {
        bool enabled=WantsClosedLyrics;
        double width=Math.Round(closedSummaryHost.ActualWidth*2)/2;
        string key=summary.Text+"|"+width+"|"+summary.FontSize+"|"+summary.FontWeight+"|"+VisualTreeHelper.GetDpi(summary).PixelsPerDip+"|"+enabled+"|"+Motion.Enabled+"|"+app.Settings.ReduceMotion;
        if(marqueeKey==key)return;ResetLyricMarquee();marqueeKey=key;
        summary.RenderTransform=lyricHorizontal;
        if(!enabled || width<2)return;
        summary.TextTrimming=TextTrimming.None;summary.HorizontalAlignment=HorizontalAlignment.Center;
        summary.Padding=new Thickness(6,0,6,0);
    }
    private string ClosedLyricPhrase(string line,double start,double end,double at)
    {
        double width=Math.Floor(closedSummaryHost.ActualWidth)-18;
        if(width<1)return "";
        string key=line+"|"+width+"|"+summary.FontFamily+"|"+summary.FontSize+"|"+summary.FontWeight+"|"+VisualTreeHelper.GetDpi(summary).PixelsPerDip;
        if(phraseKey!=key)
        {
            phraseKey=key;var phrases=new System.Collections.Generic.List<string>();string current="";
            double Measure(string value)
            {
                var formatted=new FormattedText(value,System.Globalization.CultureInfo.CurrentUICulture,summary.FlowDirection,new Typeface(summary.FontFamily,summary.FontStyle,summary.FontWeight,summary.FontStretch),summary.FontSize,summary.Foreground,VisualTreeHelper.GetDpi(summary).PixelsPerDip);
                return formatted.WidthIncludingTrailingWhitespace+Math.Max(0,formatted.OverhangTrailing)+2;
            }
            foreach(System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(line,@"\S+"))
            {
                string word=token.Value,candidate=current.Length==0?word:current+" "+word;
                if(Measure(candidate)<=width){current=candidate;continue;}
                if(current.Length>0){phrases.Add(current);current="";}
                if(Measure(word)<=width){current=word;continue;}
                // Unspaced scripts and unusually long words break only between text elements.
                var elements=System.Globalization.StringInfo.GetTextElementEnumerator(word);
                while(elements.MoveNext())
                {
                    string part=elements.GetTextElement();
                    if(current.Length>0 && Measure(current+part)>width){phrases.Add(current);current="";}
                    current+=part;
                }
            }
            if(current.Length>0)phrases.Add(current);lyricPhrases=phrases.ToArray();
        }
        if(lyricPhrases.Length==0)return "";if(lyricPhrases.Length==1)return lyricPhrases[0];
        int total=0;foreach(string phrase in lyricPhrases)total+=Math.Max(1,phrase.Length);
        double progress=Math.Clamp((at-start)/Math.Max(.1,end-start),0,.999999),target=progress*total;
        int cumulative=0;foreach(string phrase in lyricPhrases){cumulative+=Math.Max(1,phrase.Length);if(target<cumulative)return phrase;}
        return lyricPhrases[^1];
    }

    private void StopClosedLyrics()
    {
        closedLyricsDisposed=true;closedLyricTick.Stop();previousLyric.BeginAnimation(OpacityProperty,null);previousLyric.Opacity=0;previousLyric.Text="";ResetLyricMarquee();app.SettingsChanged-=UpdateClosedLyricsDemand;
        app.Lyrics.Changed-=PaintClosedLyrics;app.Lyrics.Release(closedLyricConsumer);closedLyricAttached=false;
    }
}
