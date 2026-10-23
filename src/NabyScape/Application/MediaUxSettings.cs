using System;

namespace NabyScape;

public sealed partial class Settings
{
    public bool BrowserMediaPreviews {get;set;}=true;
    public bool DockUnlocked {get;set;}
    public bool PanelAutoHeight {get;set;}=true;
    public string WaveformSource {get;set;}="Spectrum";
    public double WaveformSensitivity {get;set;}=1.35;
    public bool MediaIntegrationEnabled {get;set;}=true;
    public bool LyricsEnabled {get;set;}
    public string LyricsAnimation {get;set;}="Slide";
    public double LyricsOffset {get;set;}
    public string CountdownSound {get;set;}="Chime";
    public double CountdownSoundVolume {get;set;}=.7;
    public string CountdownSoundFile {get;set;}="";
    private void NormalizeMediaUx()
    {
        if(WaveformSource is not ("Spectrum" or "Envelope"))WaveformSource="Spectrum";
        if(LyricsAnimation is not ("Slide" or "Focus" or "Karaoke"))LyricsAnimation="Slide";
        if(CountdownSound is not ("Chime" or "Bell" or "Custom" or "Off"))CountdownSound="Chime";
        WaveformSensitivity=double.IsFinite(WaveformSensitivity)?Math.Clamp(WaveformSensitivity,.5,3):1.35;
        LyricsOffset=double.IsFinite(LyricsOffset)?Math.Clamp(LyricsOffset,-10,10):0;
        CountdownSoundVolume=double.IsFinite(CountdownSoundVolume)?Math.Clamp(CountdownSoundVolume,0,1):.7;
        CountdownSoundFile??="";
    }
}
