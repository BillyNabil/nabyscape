using System;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
// Keeps the indicator beside short artist names while reserving space when text truncates.
internal sealed class InlineMediaLabel : Panel
{
    internal InlineMediaLabel(UIElement text,UIElement meter){Children.Add(text);Children.Add(meter);ClipToBounds=true;UseLayoutRounding=true;}
    protected override Size MeasureOverride(Size available)
    {
        var text=Children[0];var meter=Children[1];meter.Measure(new Size(22,12));double reserve=meter.DesiredSize.Width>0?meter.DesiredSize.Width+8:0;
        text.Measure(new Size(Math.Max(0,available.Width-reserve),double.PositiveInfinity));
        return new Size(Math.Min(available.Width,text.DesiredSize.Width+reserve),Math.Max(text.DesiredSize.Height,meter.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size size)
    {
        var text=Children[0];var meter=Children[1];double mw=Math.Min(size.Width,meter.DesiredSize.Width),gap=mw>0 && text.DesiredSize.Width>0?8:0;
        double tw=Math.Min(text.DesiredSize.Width,Math.Max(0,size.Width-mw-gap));text.Arrange(new Rect(0,0,tw,size.Height));
        meter.Arrange(new Rect(Math.Min(size.Width-mw,tw+gap),Math.Max(0,(size.Height-meter.DesiredSize.Height)/2),mw,Math.Min(size.Height,meter.DesiredSize.Height)));return size;
    }
}
public sealed partial class OverlayWindow
{
    private FrameworkElement WrapArtistLabel(TextBlock text)
    {previousText.Remove(text);var label=new MarqueeLabel(text);marqueeLabels[text]=label;return label;}
    private FrameworkElement ArtistWithMeter(TextBlock text)
    {
        if(musicWave.Parent is Panel previous)previous.Children.Remove(musicWave);
        text.Margin=new(0);musicWave.Margin=new(0);musicWave.Visibility=app.Settings.LiveActivity?Visibility.Visible:Visibility.Collapsed;
        return new InlineMediaLabel(WrapArtistLabel(text),musicWave){Margin=new(0,4,0,0)};
    }
}
