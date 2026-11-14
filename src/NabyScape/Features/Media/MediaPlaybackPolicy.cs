using System;
using System.Collections.Generic;
using System.Linq;

namespace NabyScape;

// What the dock may do with a session's timeline. Some players (for example Telegram Desktop)
// publish play/pause through Windows but no duration, so a 0:00 / 0:00 slider would be dead.
internal enum MediaTimelineState { None, ReadOnly, Seekable }

internal static class MediaTimelinePolicy
{
    internal static MediaTimelineState State(double duration,bool canSeek)=>double.IsFinite(duration) && duration>0?canSeek?MediaTimelineState.Seekable:MediaTimelineState.ReadOnly:MediaTimelineState.None;
}

// Players publish timeline snapshots at their own cadence, sometimes only on track or state
// changes. While playing, the position advances from the snapshot's LastUpdatedTime, or from
// when this snapshot was first seen when the player leaves that stamp unset or skewed. A player
// that does not publish its pause position keeps the last interpolated value instead of jumping back.
internal sealed class MediaTimelineClock
{
    private double raw=double.NaN,held=double.NaN;
    private DateTimeOffset stamp,observedAt;
    internal void Reset(){raw=held=double.NaN;stamp=observedAt=default;}
    internal (double elapsed,double duration) Sample(TimeSpan start,TimeSpan end,TimeSpan position,DateTimeOffset lastUpdated,bool playing,DateTimeOffset now)
    {
        double duration=(end-start).TotalSeconds;
        if(!double.IsFinite(duration) || duration<=0){Reset();return (0,0);}
        double snapshot=(position-start).TotalSeconds;if(!double.IsFinite(snapshot))snapshot=0;
        if(snapshot!=raw || lastUpdated!=stamp){raw=snapshot;stamp=lastUpdated;observedAt=now;held=double.NaN;}
        double elapsed=raw;
        if(playing)
        {
            double age=(now-lastUpdated).TotalSeconds;
            if(!(age>=0 && age<86400))age=Math.Max(0,(now-observedAt).TotalSeconds);
            elapsed=held=raw+age;
        }
        else if(double.IsFinite(held))elapsed=held;
        return (Math.Clamp(elapsed,0,duration),duration);
    }
}

// Auto source follows an app when it starts playing. A browser or unidentified session (often a
// hover preview) does not take over while the selected app is still playing.
internal static class MediaFollowPolicy
{
    internal static string[] Started(IReadOnlyCollection<string> before,IEnumerable<string> now)=>now.Where(id=>!before.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
    internal static string? Follow(bool enabled,bool automatic,string? selected,bool selectedPlaying,IReadOnlyList<string> started,string? current,bool browserPreviews)
    {
        if(!enabled || !automatic || started.Count==0)return null;
        static bool Incidental(string id)=>MediaPreviewPolicy.IsBrowser(id) || MediaPreviewPolicy.OpaqueIdentity(id);
        var candidates=started.Where(id=>id!=selected && (browserPreviews || !Incidental(id)) && (!selectedPlaying || !Incidental(id))).ToArray();
        if(candidates.Length==0)return null;
        return current!=null && candidates.Contains(current)?current:candidates[0];
    }
}
