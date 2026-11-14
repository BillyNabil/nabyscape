using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    // Media source behavior: Auto follows playback; browser sessions can be ignored entirely.
    // Browser exclusion ported from PR #7 by minseok (minseokk7).
    private FrameworkElement MediaSourceBehavior()
    {
        var s=app.Settings;var panel=new StackPanel{Margin=new(0,0,0,10)};
        panel.Children.Add(Check("Switch to the app that starts playing",s.MediaFollowsPlayback,value=>s.MediaFollowsPlayback=value));
        panel.Children.Add(UI.Caption("When the media source is Auto, the dock moves to an app as soon as it starts playing, for example Telegram after Spotify. A source you pick in the media panel stays pinned. Browsers do not take over while your selected app is still playing.",11,UI.Muted));
        panel.Children.Add(Check("Exclude browser media sessions",s.ExcludeBrowserMedia,value=>s.ExcludeBrowserMedia=value));
        panel.Children.Add(UI.Caption("Exclude browsers from music detection, the source picker, lyrics and dock media activity. Unknown hashed session identities are excluded too. Music apps with identifiable sessions remain available; browser playback is not stopped.",11,UI.Muted));
        return panel;
    }
    // The dock waveform only animates while Motion is enabled; say why when Windows keeps it still.
    private static string? AudioSignalBlocker()=>!SystemParameters.ClientAreaAnimation?Localization.Text("Windows animation effects are off · the dock waveform stays still"):!Motion.Enabled?Localization.Text("Animation duration is 0 ms · the dock waveform stays still"):null;
}
