using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

// Native scrolling and keyboard behavior remain owned by WPF; only its chrome fades.
public static class ScrollChrome
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ScrollChrome),new PropertyMetadata(false,Changed));
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(ScrollChrome));
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {if(target is not ScrollViewer viewer)return;if((bool)args.NewValue){if(viewer.GetValue(StateProperty)==null)viewer.SetValue(StateProperty,new State(viewer));}else if(viewer.GetValue(StateProperty) is State state){state.Dispose();viewer.ClearValue(StateProperty);}}
    private sealed class State : IDisposable
    {
        private readonly ScrollViewer viewer;
        private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromMilliseconds(750)};
        private System.Windows.Controls.Primitives.ScrollBar? Bar=>viewer.Template?.FindName("PART_VerticalScrollBar",viewer) as System.Windows.Controls.Primitives.ScrollBar;
        public State(ScrollViewer viewer)
        {this.viewer=viewer;viewer.ScrollChanged+=Scrolled;viewer.MouseLeave+=Left;viewer.LostMouseCapture+=Left;viewer.Unloaded+=Unloaded;viewer.IsVisibleChanged+=VisibilityChanged;idle.Tick+=Idle;}
        private void Show(){if(!viewer.IsLoaded || !viewer.IsVisible)return;if(Bar is {} bar)Motion.Fade(bar,.65,120);idle.Stop();idle.Start();}
        private void Scrolled(object sender,ScrollChangedEventArgs args){if(ReferenceEquals(args.OriginalSource,viewer) && args.VerticalChange!=0)Show();}
        private void Left(object sender,System.Windows.Input.MouseEventArgs args){idle.Stop();idle.Start();}
        private void Idle(object? sender,EventArgs args){idle.Stop();if(viewer.IsMouseCaptureWithin)return;if(Bar is {} bar)Motion.Fade(bar,0,280);}
        private void Hide(){idle.Stop();if(viewer.IsLoaded && Bar is {} bar){bar.BeginAnimation(UIElement.OpacityProperty,null);bar.Opacity=0;}}
        private void Unloaded(object sender,RoutedEventArgs args)=>Hide();
        private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){if(!(bool)args.NewValue)Hide();}
        public void Dispose(){Hide();viewer.ScrollChanged-=Scrolled;viewer.MouseLeave-=Left;viewer.LostMouseCapture-=Left;viewer.Unloaded-=Unloaded;viewer.IsVisibleChanged-=VisibilityChanged;idle.Tick-=Idle;}
    }
}
