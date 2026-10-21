using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private readonly PeekWheelAccumulator panelWheel=new();
    private Point? panelDown;
    private bool panelSwiped;
    private static bool PanelGestureBlocked(DependencyObject? source)
    {for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is ButtonBase or TextBoxBase or Slider or Thumb or ScrollBar or ComboBox || node is FrameworkElement f && f.Tag as string is "file-drag" or "media-popover")return true;return false;}
    private bool CanPanelSwipe=>expanded && app.Settings.CompactLayout && app.Settings.PanelSwipe && !commandOpen && !activitiesOpen && !assistantOpen;
    internal bool RoutePanelWheel(int delta,bool horizontal,DependencyObject? source)
    {
        if(!CanPanelSwipe)return false;
        bool countdownEditor=false;for(var parent=source;parent!=null;parent=parent is Visual?VisualTreeHelper.GetParent(parent):LogicalTreeHelper.GetParent(parent))if(parent is CountdownView){countdownEditor=true;break;}
        if(mediaControls?.IsOpen==true && mediaControls.ContainsControl(source))return false;
        for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
        {
            if(!countdownEditor && node is (TextBoxBase or PasswordBox))return false;
            // The outer panel viewport must not steal wheel gestures from its navigation.
            // An inner content viewport (even at its end) owns vertical wheel input.
            if(!horizontal && node is FrameworkElement region && region.Tag as string=="panel-navigation")break;
            if(!horizontal && node is ScrollViewer viewer && (viewer.Tag as string=="panel-content-scroll" || viewer.ScrollableHeight>1 && !(viewer==scroll && activePage=="focus" && bodyWidth>=310 && viewer.ScrollableHeight<48)))return false;
        }
        int direction=panelWheel.Push(horizontal?delta:-delta,Environment.TickCount64);
        if(direction!=0)CyclePage(direction);return true;
    }
    private void InitializePanelSwipe()
    {
        root.PreviewMouseLeftButtonDown+=(_,e)=>{panelDown=null;panelSwiped=false;if(CanPanelSwipe && !PanelGestureBlocked(e.OriginalSource as DependencyObject))panelDown=e.GetPosition(root);};
        root.PreviewMouseMove+=(_,e)=>
        {
            if(panelDown==null || !CanPanelSwipe || e.LeftButton!=MouseButtonState.Pressed)return;
            var delta=e.GetPosition(root)-panelDown.Value;
            if(Math.Abs(delta.Y)>Math.Abs(delta.X) && Math.Abs(delta.Y)>10){panelDown=null;return;}
            if(Math.Abs(delta.X)<app.Settings.SwipeDistance)return;
            panelDown=null;panelSwiped=true;CyclePage(delta.X<0?1:-1);e.Handled=true;
        };
        root.PreviewMouseLeftButtonUp+=(_,e)=>{panelDown=null;if(panelSwiped){panelSwiped=false;e.Handled=true;}};
        root.LostMouseCapture+=(_,_)=>panelDown=null;
    }
}
