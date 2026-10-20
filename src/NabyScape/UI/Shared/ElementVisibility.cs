using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class ElementVisibility
{
    internal static bool InViewport(FrameworkElement element)
    {
        if(!element.IsLoaded || !element.IsVisible || element.ActualWidth<=0 || element.ActualHeight<=0 || Window.GetWindow(element)?.WindowState==WindowState.Minimized)return false;
        try
        {
            for(DependencyObject? parent=VisualTreeHelper.GetParent(element);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer viewer)
                {
                    var bounds=element.TransformToAncestor(viewer).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
                    if(!bounds.IntersectsWith(new Rect(0,0,Math.Max(0,viewer.ViewportWidth),Math.Max(0,viewer.ViewportHeight))))return false;
                }
            return true;
        }
        catch(InvalidOperationException){return false;}
    }
}
