using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class CountdownPanelDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;s.CompactLayout=true;s.CategoryStyle="Dots";s.PanelWidth=526;s.PanelMaxHeight=500;s.PanelPadding=26;s.PanelScale=1;s.ReduceMotion=true;s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;s.NotificationDelivery="Off";s.CountdownSound="Off";
        foreach(var widget in s.Widgets)widget.Enabled=widget.Id is "timer" or "focus" or "media";
        s.FocusMinutes=25;s.BreakMinutes=5;app.Session.Timer.Reset();app.Session.Focus.Reset();app.ChangeSettings();island.Reveal(false);island.SelectPage("focus",false);await Task.Delay(150);
        CountdownView View(bool focus)=>Diagnostics.Descendants(island).OfType<CountdownView>().Single(v=>Diagnostics.Descendants(v).OfType<Button>().Any(b=>AutomationProperties.GetAutomationId(b)==(focus?"focus-primary":"timer-primary")));
        Button Button(bool focus,string action="primary")=>Diagnostics.Descendants(View(focus)).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)==(focus?"focus-":"timer-")+action);
        TextBox Entry(bool focus,string name)=>Diagnostics.Descendants(View(focus)).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        void Capture(string name,double scale=1)
        {
            island.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth*scale),(int)Math.Ceiling(island.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(island);
            int w=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*scale)),h=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*scale));
            var crop=new CroppedBitmap(bitmap,new Int32Rect(Math.Max(0,(bitmap.PixelWidth-w)/2),0,w,h));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(crop));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        check("Timer and Focus retain their side-by-side layout",Math.Abs(View(false).TranslatePoint(new Point(),island).Y-View(true).TranslatePoint(new Point(),island).Y)<1);
        check("Only one main action and Reset exist per countdown",new[]{false,true}.All(f=>Diagnostics.Descendants(View(f)).OfType<Button>().Count()==2));
        Capture("ready");
        foreach(bool focus in new[]{false,true})
        {
            string label=focus?"Focus":"Timer";var state=focus?app.Session.Focus:app.Session.Timer;
            var entry=Entry(focus,focus?"Focus minutes":"Timer duration in minutes");var view=View(focus);
            entry.Text=focus?"31":"1.5";if(focus)entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(island),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            check(label+" starts with a clear Start action",Equals(Button(focus).Content,"Start") && Button(focus).IsEnabled);
            Click(Button(focus));double expected=focus?1860:90;var run=state.RunId;
            check(label+" uses the entered duration",state.Running && Math.Abs(state.DurationSeconds-expected)<.01 && (!focus || s.FocusMinutes==31));
            check(label+" changes Start to Pause immediately",Equals(Button(focus).Content,"Pause") && AutomationProperties.GetName(Button(focus))=="Pause "+label.ToLowerInvariant());
            check(label+" locks duration during the session",!entry.IsEnabled);
            Click(Button(focus));double paused=state.Remaining(DateTimeOffset.UtcNow);await Task.Delay(160);
            check(label+" pauses rather than restarting",!state.Running && state.RunId==run && state.DurationSeconds==expected && state.Remaining(DateTimeOffset.UtcNow)==paused && Equals(Button(focus).Content,"Resume"));
            if(!focus)Capture("timer-paused");
            Click(Button(focus));
            check(label+" resumes the same session",state.Running && state.RunId==run && state.DurationSeconds==expected && state.Remaining(DateTimeOffset.UtcNow)<=paused && Equals(Button(focus).Content,"Pause"));
            check(label+" keeps its controls across shared heartbeats",ReferenceEquals(view,View(focus)) && ReferenceEquals(entry,Entry(focus,focus?"Focus minutes":"Timer duration in minutes")));
        }
        Capture("running");Capture("running-150",1.5);Capture("running-200",2);
        foreach(bool focus in new[]{false,true})
        {
            Click(Button(focus,"reset"));var entry=Entry(focus,focus?"Focus minutes":"Timer duration in minutes");
            check((focus?"Focus":"Timer")+" Reset restores editable ready state",Equals(Button(focus).Content,"Start") && !Button(focus,"reset").IsEnabled && entry.IsEnabled);
            entry.Text=focus?"0":"NaN";check((focus?"Focus":"Timer")+" rejects invalid duration inline",!Button(focus).IsEnabled);
            entry.Text=focus?"25":"5";
        }
        var draft=Entry(false,"Timer duration in minutes");draft.Focus();draft.Text="7.5";draft.Select(0,2);app.SaveSession();await Task.Delay(60);
        check("Heartbeat preserves duration draft and selection",draft.Text=="7.5" && draft.SelectionStart==0 && draft.SelectionLength==2);
        int unchanged=View(false).Refreshes;app.PublishUpdate(RuntimeChange.Audio|RuntimeChange.Media);
        check("Audio and media updates do not refresh countdown controls",View(false).Refreshes==unchanged);
        app.Session.Timer.Start(90,DateTimeOffset.UtcNow.AddSeconds(-30));app.SaveSession();
        check("Progress reflects elapsed session time",Diagnostics.Descendants(View(false)).OfType<ProgressBar>().Single().Value is >.33 and <.35);
        check("Timer started from another surface shows its actual duration",Entry(false,"Timer duration in minutes").Text=="1.5" && Equals(Button(false).Content,"Pause"));
        Click(Button(false,"reset"));
        // Expiration uses the existing absolute-deadline service, without real notifications.
        app.Session.Timer.Start(60,DateTimeOffset.UtcNow.AddSeconds(-61));app.CheckCountdowns();
        check("Finished timer displays completion and can restart",Equals(Button(false).Content,"Start") && Button(false,"reset").IsEnabled && Diagnostics.Descendants(View(false)).OfType<TextBlock>().Any(t=>t.Text=="Complete"));
        app.Session.Focus.Start(1,DateTimeOffset.UtcNow.AddSeconds(-2),"work");var focusRun=app.Session.Focus.RunId;app.CheckCountdowns();
        check("Focus transitions to break with the same Pause action",app.Session.Focus.Phase=="break" && app.Session.Focus.Running && app.Session.Focus.RunId==focusRun && Equals(Button(true).Content,"Pause"));Capture("break");
        app.Session.Focus.Reset();app.Session.Timer.Reset();app.SaveSession();
        var previous=View(false);island.SelectPage("media",false);await Task.Delay(80);int refreshes=previous.Refreshes;app.SaveSession();
        check("Hidden countdown releases its heartbeat subscription",!previous.Subscribed && previous.Refreshes==refreshes);
        island.SelectPage("focus",false);await Task.Delay(80);check("Reopening countdown resumes visible updates",View(false).Subscribed);
        foreach(double width in new[]{300d,526d})
        {
            s.PanelWidth=width;app.ChangeSettings();await Task.Delay(90);island.SelectPage("focus",false);island.UpdateLayout();
            foreach(bool focus in new[]{false,true})
            {
                var view=View(focus);var bounds=new Rect(0,0,view.ActualWidth,view.ActualHeight);
                check((focus?"Focus":"Timer")+" actions fit at "+width+" DIP",Diagnostics.Descendants(view).OfType<Button>().All(b=>{var r=b.TransformToAncestor(view).TransformBounds(new Rect(0,0,b.ActualWidth,b.ActualHeight));return r.Left>=-.5 && r.Right<=bounds.Right+.5 && r.Bottom<=bounds.Bottom+.5;}));
            }
            if(width==300)check("Narrow panels stack Timer and Focus",View(true).TranslatePoint(new Point(),island).Y>View(false).TranslatePoint(new Point(),island).Y+100);
            Capture("ready-"+width);
        }
        s.CompactLayout=false;app.ChangeSettings();await Task.Delay(100);island.Reveal(false);
        foreach(bool focus in new[]{false,true}){Click(Button(focus));check("Classic "+(focus?"Focus":"Timer")+" uses the same state-driven action",Equals(Button(focus).Content,"Pause"));Click(Button(focus,"reset"));}
        Capture("classic");
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Isolated synthetic profile. No personal notes, clipboard, provider keys or network AI requests. 150/200% captures are WPF raster exports, not physical monitor DPI tests. Countdown completion delivery and sound disabled only in this fixture.");
    }
}
