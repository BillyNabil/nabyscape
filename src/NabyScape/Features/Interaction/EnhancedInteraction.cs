using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NabyScape;

internal sealed class MarqueeLabel : Border
{
    private readonly TextBlock text;private readonly TranslateTransform move=new();
    public MarqueeLabel(TextBlock text)
    {
        this.text=text;Child=text;ClipToBounds=true;text.RenderTransform=move;HorizontalAlignment=HorizontalAlignment.Stretch;
        MouseEnter+=(_,_)=>Start();MouseLeave+=(_,_)=>Reset();SizeChanged+=(_,_)=>{Reset();if(IsMouseOver)Start();};Unloaded+=(_,_)=>Reset();
    }
    private void Start()
    {
        if(!Motion.Enabled){Reset();return;}
        var formatted=new FormattedText(text.Text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,text.Foreground,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double overflow=formatted.WidthIncludingTrailingWhitespace-ActualWidth;if(overflow<2)return;
        text.TextTrimming=TextTrimming.None;text.Width=formatted.WidthIncludingTrailingWhitespace+2;
        double travel=overflow/28;var animation=new DoubleAnimationUsingKeyFrames{AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever};
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.65))));animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.65+travel))));animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4+travel))));move.BeginAnimation(TranslateTransform.XProperty,animation);
    }
    public void Reset(){move.BeginAnimation(TranslateTransform.XProperty,null);move.X=0;text.Width=double.NaN;text.TextTrimming=TextTrimming.CharacterEllipsis;}
}

public sealed partial class OverlayWindow
{
    private readonly DispatcherTimer detailDelay=new(){Interval=TimeSpan.FromMilliseconds(550)},activityDismiss=new(){Interval=TimeSpan.FromSeconds(3.8)};
    private bool peekDetailed,automaticPreview,hotkeyRegistered,visibilityHotkeyRegistered,tuckHotkeyRegistered;
    private string previewKind="",activityTitle="",activityDetail="",lastActivityTrack="";
    private DateTimeOffset? warnedTimer,warnedFocus;
    private readonly Dictionary<Image,Image> previousArtwork=new();
    private readonly Dictionary<TextBlock,TextBlock> previousText=new();
    private readonly Dictionary<TextBlock,MarqueeLabel> marqueeLabels=new();
    private ImageSource? tintedArtwork;
    private const int HotkeyId=0x4C55,VisibilityHotkeyId=0x4C56,TuckHotkeyId=0x4C57;
    internal string TuckHotkeyError {get;private set;}="";
    internal string VisibilityHotkeyError {get;private set;}="";
    internal bool HasVisibilityHotkey=>visibilityHotkeyRegistered;
    internal string HotkeyError {get;private set;}="";
    internal bool HasGlobalHotkey=>hotkeyRegistered;
    internal void TestAutomaticActivity()=>AutomaticActivity("timer","Test timer","Diagnostics profile only");
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window,int id);
    private void InitializeEnhancements()
    {
        InitializeUtilities();InitializePanelSwipe();
        detailDelay.Tick+=(_,_)=>{detailDelay.Stop();if(peeking){PromotePeek();}};
        activityDismiss.Tick+=(_,_)=>{activityDismiss.Stop();if(automaticPreview && !PointerInside()){Collapse();}else automaticPreview=false;};
        root.PreviewMouseWheel+=(_,e)=>
        {
            if(peeking){if(RoutePeekWheel(e.Delta,false,e.OriginalSource as DependencyObject))e.Handled=true;return;}
            if(expanded){if(RoutePanelWheel(e.Delta,Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),e.OriginalSource as DependencyObject))e.Handled=true;return;}
            if(app.Settings.WheelAction=="volume"){app.Audio.SetVolume(app.Audio.Volume+Math.Sign(e.Delta)*app.Settings.VolumeStep/100d);app.Audio.Read();Update();e.Handled=true;}
            else if(app.Settings.WheelAction=="pages"){Reveal();CyclePage(Math.Sign(e.Delta));e.Handled=true;}
        };
        root.PreviewMouseDown+=(_,e)=>{if(e.ChangedButton!=MouseButton.Middle || (expanded && e.GetPosition(this).Y>app.Settings.Height))return;if(app.Settings.MiddleAction=="toggle")_=app.Media.Command("toggle");else if(app.Settings.MiddleAction=="microphone" && !app.Audio.ToggleMicrophoneMute())app.Notify("Could not change microphone",app.Audio.LastError??"Device unavailable.");e.Handled=app.Settings.MiddleAction!="none";};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){if(commandOpen)CloseCommands();CyclePage(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?-1:1);e.Handled=true;}};

    }
    private void CyclePage(int direction)
    {var keys=compactTabs.Keys.ToArray();if(keys.Length==0)return;int i=Array.IndexOf(keys,activePage);SelectPage(keys[(i+direction+keys.Length)%keys.Length]);}
    private void ConfigureHotkey()
    {
        if(hwnd==IntPtr.Zero)return;
        if(hotkeyRegistered)UnregisterHotKey(hwnd,HotkeyId);
        if(visibilityHotkeyRegistered)UnregisterHotKey(hwnd,VisibilityHotkeyId);
        if(tuckHotkeyRegistered)UnregisterHotKey(hwnd,TuckHotkeyId);
        hotkeyRegistered=visibilityHotkeyRegistered=tuckHotkeyRegistered=false;HotkeyError=HotkeyPolicy.Conflict(app.Settings,"Panel");VisibilityHotkeyError=HotkeyPolicy.Conflict(app.Settings,"Visibility");TuckHotkeyError=HotkeyPolicy.Conflict(app.Settings,"Tuck");
        static uint Modifiers(string value)=>value switch{"Ctrl+Shift"=>6u,"Alt+Shift"=>5u,_=>3u};
        if(app.Settings.HotkeyEnabled && HotkeyError.Length==0){hotkeyRegistered=RegisterHotKey(hwnd,HotkeyId,Modifiers(app.Settings.HotkeyModifiers)|0x4000,(uint)app.Settings.HotkeyKey[0]);if(!hotkeyRegistered)HotkeyError="Open / close hotkey is in use. Choose another combination in Settings.";}
        if(app.Settings.VisibilityHotkeyEnabled && VisibilityHotkeyError.Length==0){visibilityHotkeyRegistered=RegisterHotKey(hwnd,VisibilityHotkeyId,Modifiers(app.Settings.VisibilityHotkeyModifiers)|0x4000,(uint)app.Settings.VisibilityHotkeyKey[0]);if(!visibilityHotkeyRegistered)VisibilityHotkeyError="Hide / show hotkey is in use. Choose another combination in Settings.";}
        if(app.Settings.TuckHotkeyEnabled && TuckHotkeyError.Length==0){tuckHotkeyRegistered=RegisterHotKey(hwnd,TuckHotkeyId,Modifiers(app.Settings.TuckHotkeyModifiers)|0x4000,(uint)app.Settings.TuckHotkeyKey[0]);if(!tuckHotkeyRegistered)TuckHotkeyError="Tuck hotkey is in use. Choose another combination in Settings.";}
    }

    private void AutomaticActivity(string kind,string heading,string detail)
    {
        if(!app.Settings.SmartActivities || expanded || userHidden || TuckRequested || !IsVisible || (peeking && !automaticPreview))return;
        if(!WidgetOn(kind) || kind=="media" && !MediaPreviewPolicy.AllowAutomatic(app.Settings,app.Media.Selected?.SourceAppUserModelId))return;
        if(!DiagnosticRun && app.Settings.HideFullscreen && Native.FullscreenOn(Native.ChooseScreen(app.Settings),hwnd))return;
        previewKind=kind;activityTitle=heading;activityDetail=detail;automaticPreview=true;
        if(!peeking)PreviewActivity();else{peekId=kind;LayoutPeek(0,false);UpdatePeek();}activityDismiss.Stop();activityDismiss.Start();
    }
    private void CheckSmartActivities()
    {
        string track=app.Media.Available?(app.Media.Selected?.SourceAppUserModelId+"|"+app.Media.Title):"";
        if(track!=lastActivityTrack){bool primed=lastActivityTrack!="";lastActivityTrack=track;if(primed && track!="" && app.Media.Playing)AutomaticActivity("media","","");}
        var now=DateTimeOffset.UtcNow;
        foreach(var (id,c) in new[]{("timer",app.Session.Timer),("focus",app.Session.Focus)})
        {
            if(!WidgetOn(id) || !c.Running || c.Remaining(now)>30 || c.Remaining(now)<=0)continue;
            if(id=="timer"){if(warnedTimer==c.Deadline)continue;warnedTimer=c.Deadline;}else{if(warnedFocus==c.Deadline)continue;warnedFocus=c.Deadline;}
            AutomaticActivity(id,(id=="timer"?"Timer":"Focus")+" · "+c.Display(now),"Ending soon");
        }
    }
    private FrameworkElement WrapArtwork(Image current)
    {
        var grid=new Grid();var old=new Image{Width=current.Width,Height=current.Height,Stretch=current.Stretch,Opacity=0,IsHitTestVisible=false};previousArtwork[current]=old;grid.Children.Add(old);grid.Children.Add(current);ControlFeedback.Artwork(grid);return grid;
    }
    private void SwapArtwork(Image current,ImageSource? source)
    {
        if(ReferenceEquals(current.Source,source))return;
        if(previousArtwork.TryGetValue(current,out var old)){old.Source=current.Source;old.BeginAnimation(OpacityProperty,null);old.Opacity=1;Motion.Fade(old,0,320);}
        current.Source=source;current.BeginAnimation(OpacityProperty,null);current.Opacity=Motion.Enabled && source!=null?0:1;Motion.Fade(current,1,320);
    }
    private FrameworkElement WrapTitle(TextBlock text)
    {
        var grid=new Grid();var old=UI.Text("",text.FontSize,text.Foreground);old.FontWeight=text.FontWeight;old.Margin=text.Margin;old.TextWrapping=TextWrapping.NoWrap;old.TextTrimming=TextTrimming.CharacterEllipsis;old.Opacity=0;old.IsHitTestVisible=false;previousText[text]=old;grid.Children.Add(old);
        var label=new MarqueeLabel(text);marqueeLabels[text]=label;grid.Children.Add(label);return grid;
    }
    private void SwapText(TextBlock text,string value)
    {
        if(text.Text==value)return;
        if(previousText.TryGetValue(text,out var old)){old.FontWeight=text.FontWeight;old.Text=text.Text;old.BeginAnimation(OpacityProperty,null);old.Opacity=1;Motion.Fade(old,0,220);text.BeginAnimation(OpacityProperty,null);text.Opacity=Motion.Enabled?0:1;}
        if(marqueeLabels.TryGetValue(text,out var label))label.Reset();text.Text=value;Motion.Fade(text,1,220);
    }
    private CoverPalette coverPalette;
    private void UpdateAmbientMotion()
    {
        var s=app.Settings;
        bool Includes(string id)=>AmbientPolicy.Includes(s,id);
        bool allowed=!noticeShowing && (assistantOpen || assistantPeeking?Includes("ai"):commandOpen || activitiesOpen?Includes("shortcuts"):expanded?
            PanelWidgets.Any(w=>(!s.CompactLayout || PageFor(w.Id)==activePage) && Includes(w.Id)):
            peeking?Includes(peekId):Includes("closed"));
        allowed &= (expanded || peeking || s.ClosedAmbient) && !IsTucked && !userHidden;
        bool quiet=IsTucked || !expanded && !peeking;
        surface.Fill=quiet?Brushes.Black:UI.Brush(s.Background);surface.GlassStyle=!quiet && s.GlassStyle;
        if(quiet)surface.AlbumTint=Colors.Transparent;
        bool moving=app.Media.Playing && s.LiveActivity && !IsTucked && !userHidden && IsVisible && WindowState!=WindowState.Minimized;
        surface.SetAmbient(AmbientPolicy.ColorsFor(s,app.AmbientAudio.CoverColors),allowed?s.AmbientStrength:0,s.AmbientSpread,s.AmbientMotion,moving,AmbientLook.From(s));
        app.AmbientAudio.Set(surface,allowed && moving);
    }
    private void UpdateAlbumTint()
    {
        if(!ReferenceEquals(tintedArtwork,app.Media.Artwork))
        {
            tintedArtwork=app.Media.Artwork;
            coverPalette=app.AmbientAudio.CoverColors;
        }
        var tint=coverPalette.Primary;surface.AlbumTint=app.Settings.AlbumAccent && tint.A>0?Color.FromArgb(24,tint.R,tint.G,tint.B):Colors.Transparent;
        UpdateAmbientMotion();
    }
}
