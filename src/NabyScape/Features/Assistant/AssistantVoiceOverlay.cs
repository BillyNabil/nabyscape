using System;
using System.Windows;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private void SizeVoiceViewport()
    {
        if(!VoiceRoomOpen || !compactPages.TryGetValue("voice",out var page) || page is not AssistantVoiceView voice)return;
        double reserved=0;
        foreach(FrameworkElement child in body.Children)if(child!=compactPageHost)
        {
            child.Measure(new Size(bodyWidth,double.PositiveInfinity));reserved+=child.DesiredSize.Height;
        }
        voice.Height=Math.Max(0,scroll.Height/app.Settings.PanelScale-reserved);
    }
}
