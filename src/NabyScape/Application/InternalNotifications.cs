using System;
using System.Collections.Generic;
using System.Linq;

namespace NabyScape;

internal sealed record InternalNotice(string Kind,string Title,string Detail,string[]? Files=null);

// Ephemeral and dispatcher-owned. Delivery errors never feed back into the queue.
internal sealed class InternalNotifications
{
    private readonly List<(InternalNotice Notice,DateTimeOffset At,bool Displayed)> queue=new();
    private DateTimeOffset lastDownloadAt;
    internal int Count=>queue.Count;
    internal InternalNotice? Current=>queue.FirstOrDefault().Notice;
    internal void MarkDisplayed(){if(queue.Count>0)queue[0]=(queue[0].Notice,queue[0].At,true);}
    internal event Action? Changed;
    private void Publish(){try{Changed?.Invoke();}catch{/* Never notify about notification-delivery failures. */}}
    internal static bool Enabled(Settings s,string kind)=>kind switch{"timer"=>s.NotifyTimer,"focus"=>s.NotifyFocus,"downloads"=>s.NotifyDownloads,"updates"=>s.NotifyUpdates,_=>s.NotifyFailures};
    internal static (bool Notch,bool Windows) Route(string mode,bool hidden)=>mode switch{"Windows"=>(false,true),"Both"=>(!hidden,true),"Off"=>(false,false),_=>(!hidden,hidden)};
    internal void Post(Settings settings,InternalNotice notice,bool hidden,Action<InternalNotice> windows,DateTimeOffset now)
    {
        if(!Enabled(settings,notice.Kind))return;
        // Watcher iterations can deliver up to 128 arrivals. Bound each payload before
        // either delivery route, so every original remains accessible after dismissal.
        if(notice.Kind=="downloads" && notice.Files is {Length:>40} arrivals)
        {
            foreach(var files in arrivals.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(40))
                Post(settings,new("downloads",files.Length==1?"Download ready":$"{files.Length} downloads ready",notice.Detail,files),hidden,windows,now);
            return;
        }
        var route=Route(settings.NotificationDelivery,hidden);
        if(route.Windows)try{windows(notice);}catch{/* Do not recurse on delivery failures. */}
        if(!route.Notch)return;
        if(notice.Kind=="downloads" && queue.LastOrDefault() is var last && last.Notice?.Kind=="downloads" && !last.Displayed && (last.Notice.Files??Array.Empty<string>()).Concat(notice.Files??Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Count()<=40 && now-lastDownloadAt<TimeSpan.FromSeconds(3))
        {
            var files=(last.Notice.Files??Array.Empty<string>()).Concat(notice.Files??Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
            queue[^1]=(new("downloads",files.Length==1?"Download ready":$"{files.Length} downloads ready",notice.Detail,files),now,false);
        }
        else {if(queue.Count==10)queue.RemoveAt(9);queue.Add((notice,now,false));}
        if(notice.Kind=="downloads")lastDownloadAt=now;
        Publish();
    }
    internal void Tick(DateTimeOffset now,bool busy)
    {if(busy){if(queue.Count>0)queue[0]=(queue[0].Notice,now,queue[0].Displayed);return;}if(queue.Count>0 && now-queue[0].At>TimeSpan.FromSeconds(12)){queue.RemoveAt(0);Publish();}}
    internal void Dismiss(){if(queue.Count>0){queue.RemoveAt(0);Publish();}}
    internal void Clear(){queue.Clear();Publish();}
}
