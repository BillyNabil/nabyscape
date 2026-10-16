using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NabyScape;

internal static class MixerRowLayoutTests
{
    internal static void Run(Action<string,bool> check)
    {
        const string longName="An exceptionally long application name that cannot fit inside the mixer header";
        var item=new MixerItem(new(MixerScope.Apps,"output","layout-only-session"),longName,"A long headphone output name",1,false);
        // Exercise the same stacked row at narrow and default widths, including repeated resizing.
        var mute=new Button{Content=Icons.Create("volume",14),MinHeight=28,Padding=new Thickness(3)};
        var (row,slider,percent)=OverlayWindow.CreateMixerRowLayout(item,mute);
        foreach(double width in new[]{193.6,302d,193.6})
        {
            row.Measure(new Size(width,double.PositiveInfinity));
            row.Arrange(new Rect(0,0,width,row.DesiredSize.Height));row.UpdateLayout();
            Rect Bounds(FrameworkElement child)=>child.TransformToAncestor(row).TransformBounds(new Rect(child.RenderSize));
            bool Inside(FrameworkElement child)
            {
                var bounds=Bounds(child);
                return child.Visibility==Visibility.Visible && bounds.Width>0 && bounds.Height>0 && bounds.Left>=-.01 && bounds.Top>=-.01 && bounds.Right<=row.ActualWidth+.01 && bounds.Bottom<=row.ActualHeight+.01;
            }
            var icon=(FrameworkElement)row.Children[0];var header=(StackPanel)row.Children[1];var label=(TextBlock)header.Children[0];
            bool compact=true;
            check($"WPF mixer row at {width} DIP keeps icon, percentage, mute and usable slider inside bounds",
                new FrameworkElement[]{icon,header,slider,percent,mute}.All(Inside) && slider.ActualWidth>=70 && percent.Text=="100%" && mute.ActualWidth>=28 && percent.ActualWidth>=38);
            var sliderBounds=Bounds(slider);var headerBounds=Bounds(header);
            check($"WPF mixer row at {width} DIP reflows without overlapping header or controls",
                compact?sliderBounds.Top>=Math.Max(headerBounds.Bottom,Math.Max(Bounds(percent).Bottom,Bounds(mute).Bottom))-.01:
                headerBounds.Right<=sliderBounds.Left+.01 && sliderBounds.Right<=Bounds(percent).Left+.01 && Bounds(percent).Right<=Bounds(mute).Left+.01);
            check($"WPF mixer row at {width} DIP truncates long names with full accessible identity",
                label.Text==longName && label.TextTrimming==TextTrimming.CharacterEllipsis && label.TextWrapping==TextWrapping.NoWrap && label.ActualWidth<longName.Length*5 &&
                label.ToolTip?.ToString()?.Contains(longName)==true && AutomationProperties.GetName(label).Contains(longName) && AutomationProperties.GetName(slider).Contains(longName));
        }
    }
}
