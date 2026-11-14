namespace NabyScape;

public sealed partial class Settings
{
    // Auto media source follows an app when it starts playing. A source picked by hand stays pinned.
    public bool MediaFollowsPlayback {get;set;}=true;
    // Ignore browser media sessions in music detection, the source picker, lyrics and dock activity.
    public bool ExcludeBrowserMedia {get;set;}
}
