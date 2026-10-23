using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

// Layout is independent of decoded pixel residency. Releasing an offscreen Source
// therefore cannot change the transcript extent or its saved scroll position.
internal sealed class AssistantPreviewImage : Image
{
    internal Size NaturalSize {get;set;}
    internal void SetNaturalSize(Size size,IReadOnlyList<ScrollViewer> viewports)
    {
        if(NaturalSize==size)return;
        // Resolve an unknown remote slot in one dispatcher turn, before rendering.
        // Keep the first visible content at its existing pixel offset, or follow the
        // bottom when already there. No scroll listener or delayed restore can race input.
        var anchors=viewports.Select(viewport=>
        {
            FrameworkElement? anchor=null;double top=0;
            if(viewport.Content is Panel panel)anchor=VisibleAnchor(panel,viewport,this);
            if(anchor!=null)top=anchor.TransformToAncestor(viewport).Transform(new Point()).Y;
            return (Viewport:viewport,Anchor:anchor,Top:top,Bottom:viewport.ScrollableHeight-viewport.VerticalOffset<30);
        }).ToArray();
        NaturalSize=size;InvalidateMeasure();
        foreach(var item in anchors)
        {
            var viewport=item.Viewport;viewport.UpdateLayout();
            if(item.Bottom)viewport.ScrollToEnd();
            else if(item.Anchor!=null)
            {
                double top=item.Anchor.TransformToAncestor(viewport).Transform(new Point()).Y;
                viewport.ScrollToVerticalOffset(viewport.VerticalOffset+top-item.Top);
            }
            viewport.UpdateLayout();
        }
    }
    private static FrameworkElement? VisibleAnchor(DependencyObject parent,ScrollViewer viewport,Image resizing)
    {
        // A message can contain several attachments. Its top may stay fixed while an
        // earlier image changes the position of a later visible image or action row.
        // Descend to the first visible leaf so that content, not its container, anchors.
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(ReferenceEquals(child,resizing))continue;
            if(child is FrameworkElement element)
            {
                if(!element.IsVisible || element.ActualWidth<=0 || element.ActualHeight<=0)continue;
                var bounds=element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
                if(bounds.Bottom<=0 || bounds.Top>=viewport.ActualHeight)continue;
                var visible=VisibleAnchor(child,viewport,resizing);if(visible!=null)return visible;
                // Do not fall back to a container whose only visible child is the
                // resizing image. Prefer a stable visible action/text/sibling leaf.
                if(VisualTreeHelper.GetChildrenCount(child)==0 || element is TextBlock or Image or System.Windows.Shapes.Shape)return element;
                continue;
            }
            var nested=VisibleAnchor(child,viewport,resizing);if(nested!=null)return nested;
        }
        return null;
    }
    protected override Size MeasureOverride(Size available)
    {
        if(NaturalSize.Width<=0 || NaturalSize.Height<=0)return base.MeasureOverride(available);
        double scale=Math.Min(1,Math.Min(available.Width/NaturalSize.Width,available.Height/NaturalSize.Height));
        return new Size(NaturalSize.Width*scale,NaturalSize.Height*scale);
    }
}
