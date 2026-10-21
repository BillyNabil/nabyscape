using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class MediaLyricsLayout
{
    internal const double MinimumBodyWidth=500;
    internal static double Width(Settings s,double available,bool lyrics)
    {
        double normal=Math.Min(s.PanelWidth,available);
        if(!lyrics)return normal;
        double extended=Math.Min(normal+280*s.PanelScale,available);
        return (extended-2*s.PanelPadding)/s.PanelScale>=MinimumBodyWidth?extended:normal;
    }
}

public sealed partial class OverlayWindow
{
    private Grid? mediaLyricsLayout;
    private Border? lyricsAside;
    private bool ShowLyricsLayout=>app.Settings.CompactLayout && activePage=="media" && !assistantOpen && !commandOpen && !activitiesOpen && mediaLyrics?.Visibility==Visibility.Visible;
    private FrameworkElement WrapMediaLyrics(FrameworkElement controls)
    {
        mediaLyricsLayout=new Grid();mediaLyricsLayout.ColumnDefinitions.Add(new());mediaLyricsLayout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});mediaLyricsLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});mediaLyricsLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        lyricsAside=new Border{Child=mediaLyrics,Visibility=Visibility.Collapsed,BorderBrush=UI.Brush("#303137")};mediaLyricsLayout.Children.Add(controls);mediaLyricsLayout.Children.Add(lyricsAside);ArrangeMediaLyrics();return mediaLyricsLayout;
    }
    private void ArrangeMediaLyrics()
    {
        if(mediaLyricsLayout==null || lyricsAside==null || mediaLyrics==null)return;
        bool visible=mediaLyrics.Visibility==Visibility.Visible,side=visible && bodyWidth>=MediaLyricsLayout.MinimumBodyWidth;
        lyricsAside.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumn(lyricsAside,side?1:0);Grid.SetRow(lyricsAside,side?0:1);
        lyricsAside.Width=side?256:double.NaN;lyricsAside.Margin=side?new Thickness(24,0,0,0):new Thickness(0,8,0,0);lyricsAside.Padding=side?new Thickness(20,0,0,0):new Thickness(0);lyricsAside.BorderThickness=side?new Thickness(1,0,0,0):new Thickness(0);
        mediaLyrics.Height=side?200:130;mediaLyrics.Margin=side?new Thickness(0):new Thickness(0,6,0,12);mediaLyrics.VerticalAlignment=VerticalAlignment.Center;
    }
}
