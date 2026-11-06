using System;
using System.Collections.Generic;
using System.Linq;

namespace NabyScape;

// Source/AppId describe alerts mirrored from other Windows apps (Kind "external").
internal sealed record InternalNotice(string Kind,string Title,string Detail,string[]? Files=null,string? Source=null,string? AppId=null);
// One row in the notification center. Session-only: nothing here is written to disk.
internal sealed class NoticeEntry(InternalNotice notice,DateTimeOffset at)
{
    internal InternalNotice Notice { get; set; }=notice;
    internal DateTimeOffset At { get; set; }=at;
    internal bool Read { get; set; }
}

// Ephemeral and dispatcher-owned. Delivery errors never feed back into the queue.
internal sealed class InternalNotifications
{
    private readonly List<(InternalNotice Notice,DateTimeOffset At,bool Displayed)> queue=new();
    private DateTimeOffset lastDownloadAt;
    internal const int HistoryLimit=50;
    private readonly List<NoticeEntry> history=new();
    // Newest first. Includes alerts delivered only to Windows, muted by quiet hours or already dismissed.
    internal IReadOnlyList<NoticeEntry> History=>history;
    internal int Unread=>history.Count(e=>!e.Read);
    internal void MarkAllRead(){if(history.All(e=>e.Read))return;foreach(var entry in history)entry.Read=true;Publish();}
    internal void Remove(NoticeEntry entry){if(history.Remove(entry))Publish();}
    internal void ClearHistory(){if(history.Count==0)return;history.Clear();Publish();}
    private void Record(InternalNotice notice,DateTimeOffset now,bool read)
    {
        // Download bursts already merge into one popup; keep the center equally tidy.
        if(notice.Kind=="downloads" && history.FirstOrDefault() is {Notice.Kind:"downloads"} head && now-head.At<TimeSpan.FromSeconds(3))
        {
            var files=(head.Notice.Files??Array.Empty<string>()).Concat(notice.Files??Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(files.Length<=40){head.Notice=new("downloads",files.Length==1?"Download ready":$"{files.Length} downloads ready",notice.Detail,files);head.At=now;head.Read&=read;return;}
        }
        history.Insert(0,new(notice,now){Read=read});
        if(history.Count>HistoryLimit)history.RemoveRange(HistoryLimit,history.Count-HistoryLimit);
    }
    internal int Count=>queue.Count;
    internal InternalNotice? Current=>queue.FirstOrDefault().Notice;
    internal void MarkDisplayed()
    {
        if(queue.Count==0)return;queue[0]=(queue[0].Notice,queue[0].At,true);
        foreach(var entry in history.Where(e=>ReferenceEquals(e.Notice,queue[0].Notice) || e.Notice==queue[0].Notice))entry.Read=true;
    }
    internal event Action? Changed;
    private void Publish(){try{Changed?.Invoke();}catch{/* Never notify about notification-delivery failures. */}}
    internal static bool Enabled(Settings s,string kind)=>kind switch{"external"=>s.ExternalNotifications,"timer"=>s.NotifyTimer,"focus"=>s.NotifyFocus,"downloads"=>s.NotifyDownloads,"updates"=>s.NotifyUpdates,_=>s.NotifyFailures};
    internal static (bool Notch,bool Windows) Route(string mode,bool hidden)=>mode switch{"Windows"=>(false,true),"Both"=>(!hidden,true),"Off"=>(false,false),_=>(!hidden,hidden)};
    internal void Post(Settings settings,InternalNotice notice,bool hidden,Action<InternalNotice> windows,DateTimeOffset now)
    {
        if(!Enabled(settings,notice.Kind))return;
        if(NotificationPolicy.Quiet(settings,now)){if(notice.Kind!="preview"){Record(notice,now,false);Publish();}return;}
        // Watcher iterations can deliver up to 128 arrivals. Bound each payload before
        // either delivery route, so every original remains accessible after dismissal.
        if(notice.Kind=="downloads" && notice.Files is {Length:>40} arrivals)
        {
            foreach(var files in arrivals.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(40))
                Post(settings,new("downloads",files.Length==1?"Download ready":$"{files.Length} downloads ready",notice.Detail,files),hidden,windows,now);
            return;
        }
        var route=Route(settings.NotificationDelivery,hidden);
        // Mirrored alerts already appeared in Windows: never send them back, and pop up only on request.
        if(notice.Kind=="external")route=(route.Notch && settings.ExternalNotificationPopup && !hidden,false);
        if(notice.Kind!="preview")Record(notice,now,false);
        if(route.Windows)try{windows(notice);}catch{/* Do not recurse on delivery failures. */}
        if(!route.Notch){Publish();return;}
        if(notice.Kind=="downloads" && queue.LastOrDefault() is var last && last.Notice?.Kind=="downloads" && !last.Displayed && (last.Notice.Files??Array.Empty<string>()).Concat(notice.Files??Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Count()<=40 && now-lastDownloadAt<TimeSpan.FromSeconds(3))
        {
            var files=(last.Notice.Files??Array.Empty<string>()).Concat(notice.Files??Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
            queue[^1]=(new("downloads",files.Length==1?"Download ready":$"{files.Length} downloads ready",notice.Detail,files),now,false);
        }
        else {if(queue.Count==10)queue.RemoveAt(9);queue.Add((notice,now,false));}
        if(notice.Kind=="downloads")lastDownloadAt=now;
        Publish();
    }
    internal void Tick(DateTimeOffset now,bool busy,double seconds=12)
    {if(busy){if(queue.Count>0)queue[0]=(queue[0].Notice,now,queue[0].Displayed);return;}if(queue.Count>0 && now-queue[0].At>TimeSpan.FromSeconds(seconds)){queue.RemoveAt(0);Publish();}}
    internal void Dismiss(){if(queue.Count>0){queue.RemoveAt(0);Publish();}}
    internal void Clear(){queue.Clear();Publish();}
}
