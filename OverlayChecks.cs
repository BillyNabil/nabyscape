using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private IntPtr? diagnosticForeground;
    private IntPtr ExternalForegroundForRules()=>DiagnosticRun && diagnosticForeground.HasValue?(Native.IsExternalWindow(diagnosticForeground.Value)?diagnosticForeground.Value:IntPtr.Zero):Native.ExternalForegroundWindow();
    internal void DiagnosticEnvironment(IntPtr? foreground)
    {
        if(!DiagnosticRun)throw new InvalidOperationException("Environment injection requires diagnostics.");diagnosticForeground=foreground;
    }
    internal void DiagnosticLongPreview()
    {
        if(!DiagnosticRun)return;
        peekTitle.Text=new string('W',180);peekSubtitle.Text=new string('W',180);peekEventTitle.Text=new string('W',180);peekShelfName.Text=new string('W',180);UpdateLayout();
    }
    internal bool HoverSettled=>!IsAnimating && peekMoves.TryGetValue(peekId,out var move) && Math.Abs(move.X)<.01;
    internal string HoverOverflow()
    {
        if(!DiagnosticRun || !peeking)return "Preview is not open";UpdateLayout();
        foreach(var control in Diagnostics.Descendants(peekPanel).OfType<FrameworkElement>().Where(e=>e.IsVisible && e.ActualWidth>0 && e.ActualHeight>0 && e is Button or TextBox or TextBlock or Slider or MarqueeLabel))
        {
            // Scrolling glyphs deliberately exceed their clipped label. Validate that viewport
            // itself (included above), rather than reporting invisible offscreen glyphs as controls.
            if(control is TextBlock {Parent:MarqueeLabel {ClipToBounds:true},TextTrimming:TextTrimming.None} text && double.IsFinite(text.Width))continue;
            // Pages remain attached during slide-out; inspect the selected page and footer only.
            if(peekViews.Any(pair=>pair.Key!=peekId && (pair.Value==control || pair.Value.IsAncestorOf(control))))continue;
            if(peekViews.TryGetValue(peekId,out var selected) && selected.IsAncestorOf(control))
            {
                var local=control.TransformToAncestor(peekViewport).TransformBounds(new Rect(control.RenderSize));
                if(local.Left<-.5 || local.Top<-.5 || local.Right>peekViewport.ActualWidth+.5 || local.Bottom>peekViewport.ActualHeight+.5)return $"Viewport clips {control.GetType().Name} {System.Windows.Automation.AutomationProperties.GetName(control)} {local} versus {peekViewport.RenderSize}";
            }
            var bounds=control.TransformToAncestor(this).TransformBounds(new Rect(control.RenderSize));
            double left=(ActualWidth-PaintedWidth)/2;
            if(bounds.Left<left+8-.5 || bounds.Right>left+PaintedWidth-8+.5 || bounds.Top<-.5 || bounds.Bottom>PaintedHeight-8+.5)
                return $"{control.GetType().Name} {System.Windows.Automation.AutomationProperties.GetName(control)} {bounds} versus {PaintedWidth}x{PaintedHeight}";
        }
        return "";
    }
    internal void DiagnosticScrollHardware(bool end)
    {if(!DiagnosticRun)throw new InvalidOperationException("Diagnostic scroll requires diagnostics.");if(end)monitorDetailScroll?.ScrollToEnd();else monitorDetailScroll?.ScrollToTop();}
    internal bool FeedbackHasExpired=>!FeedbackVisible && summary.MinWidth==0 && compactClock.Visibility==(WidgetOn("clock")?Visibility.Visible:Visibility.Collapsed);
    internal string FeedbackDiagnosticState=>$"active={FeedbackVisible}; expiry={feedbackUntil:O}; now={DateTimeOffset.UtcNow:O}; header={summary.Text}; clock={compactClock.Visibility}; audio={app.Audio.Volume:0.00}/{app.Audio.Muted}/{app.Audio.Device}";
    internal bool FeedbackTextFits
    {
        get{UpdateLayout();return summary.ActualWidth>=40 && compactClock.Visibility==Visibility.Collapsed && awakeMark.Visibility==Visibility.Collapsed && summary.TextTrimming==TextTrimming.CharacterEllipsis && summary.DesiredSize.Width<=summary.ActualWidth+.5;}
    }
    internal void DiagnosticPeekWheel(int delta,string target)
    {
        if(!DiagnosticRun)return;
        FrameworkElement source=target switch{"slider"=>peekVolume,"button"=>peekNext,"footer"=>peekFooter,_=>peekPanel};
        var args=new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseWheelEvent,Source=source};source.RaiseEvent(args);
    }
}
