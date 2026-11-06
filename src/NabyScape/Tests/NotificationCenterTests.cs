using System;
using System.Linq;

namespace NabyScape;

internal static class NotificationCenterTests
{
    internal static void Run(Action<string,bool> test)
    {
        var now=new DateTimeOffset(2026,11,7,12,0,0,TimeSpan.Zero);
        var notch=new Settings{NotificationDelivery="Notch"};
        var queue=new InternalNotifications();
        queue.Post(notch,new("timer","Timer finished","Done"),false,_=>{},now);
        test("Every delivered alert is kept in the center as unread",queue.History.Count==1 && queue.Unread==1 && queue.Count==1);
        queue.MarkDisplayed();
        test("Showing the popup marks its center entry read",queue.Unread==0);
        queue.Dismiss();
        test("Dismissing the popup keeps the history entry",queue.Count==0 && queue.History.Count==1);
        int windows=0;
        queue.Post(new Settings{NotificationDelivery="Windows"},new("updates","Update ready","Restart to install"),false,_=>windows++,now.AddSeconds(5));
        test("Windows-only alerts still appear in the center",windows==1 && queue.History[0].Notice.Title=="Update ready" && queue.Count==0);
        queue.Post(new Settings{NotificationDelivery="Notch",NotifyTimer=false},new("timer","Muted","Off"),false,_=>{},now.AddSeconds(6));
        test("Turned-off alert kinds are not recorded",queue.History.All(e=>e.Notice.Title!="Muted"));
        queue.Post(new Settings{NotificationDelivery="Notch",NotificationsSnoozedUntil=now.AddHours(1)},new("focus","Focus complete","Break"),false,_=>{},now.AddSeconds(7));
        test("Snoozed alerts are kept for later without a popup",queue.History[0].Notice.Title=="Focus complete" && !queue.History[0].Read && queue.Count==0);
        queue.Post(notch,new("preview","Preview","Sample"),false,_=>{},now.AddSeconds(8));
        test("Settings previews never fill the center",queue.History.All(e=>e.Notice.Kind!="preview"));
        queue.Post(notch,new("downloads","Download ready","Downloads",new[]{"a.txt"}),false,_=>{},now.AddSeconds(20));
        queue.Post(notch,new("downloads","Download ready","Downloads",new[]{"b.txt"}),false,_=>{},now.AddSeconds(21));
        test("Download bursts merge into one center entry",queue.History[0].Notice.Files!.Length==2 && queue.History.Count(e=>e.Notice.Kind=="downloads")==1);
        queue.MarkAllRead();
        test("Mark all read clears the badge",queue.Unread==0);
        queue.Remove(queue.History[0]);
        test("Single entries can be removed",queue.History.All(e=>e.Notice.Kind!="downloads"));
        for(int i=0;i<70;i++)queue.Post(notch,new("failure","Alert "+i,""),false,_=>{},now.AddMinutes(i+1));
        test("History keeps the newest fifty alerts",queue.History.Count==InternalNotifications.HistoryLimit && queue.History[0].Notice.Title=="Alert 69");
        queue.ClearHistory();
        test("Clear empties the center",queue.History.Count==0 && queue.Unread==0);
        var mirror=new InternalNotifications();int forwarded=0;
        var external=new Settings{NotificationDelivery="Both",ExternalNotifications=true};
        mirror.Post(external,new("external","Your answer is ready","Claude finished",null,"Claude","Claude.App"),false,_=>forwarded++,now);
        test("Alerts from other apps reach the center and pop up without echoing to Windows",mirror.History.Count==1 && mirror.Count==1 && forwarded==0 && mirror.History[0].Notice.Source=="Claude");
        external.ExternalNotificationPopup=false;
        mirror.Post(external,new("external","New message","Hi",null,"Telegram","Telegram.App"),false,_=>forwarded++,now.AddSeconds(5));
        test("Pop-ups for other apps can be turned off while the center still records them",mirror.History.Count==2 && mirror.Count==1 && forwarded==0);
        mirror.Post(new Settings{ExternalNotifications=false},new("external","Hidden","x",null,"Discord",""),false,_=>{},now.AddSeconds(6));
        test("Other apps are ignored until the person turns the feature on",mirror.History.Count==2);
        var muted=new Settings{ExternalNotificationMuted={"Telegram"}};
        test("Muted apps are filtered by name, ignoring case",!ExternalNotificationPolicy.Allowed(muted,"telegram") && ExternalNotificationPolicy.Allowed(muted,"Claude"));
        test("NabyScape's own Windows balloons are never mirrored back",ExternalNotificationPolicy.IsOwn("NabyScape","") && ExternalNotificationPolicy.IsOwn("App","BillyNabil.NabyScape") && !ExternalNotificationPolicy.IsOwn("Claude","Claude"));
        test("First line becomes the title and the rest the message",ExternalNotificationPolicy.Split("Claude",new[]{"Done"," Your file is ready ","Open it"})==("Done","Your file is ready · Open it") && ExternalNotificationPolicy.Split("Claude",System.Array.Empty<string>())==("Claude",""));
        test("The first snapshot is a baseline; later snapshots report only new ids",ExternalNotificationPolicy.Fresh(new uint[]{1,2},null).Length==0 && ExternalNotificationPolicy.Fresh(new uint[]{1,2,3},new(){1,2}).SequenceEqual(new uint[]{3}));
        test("Relative times read naturally",NotificationCenterText.Ago(now,now.AddSeconds(-20))=="Just now" && NotificationCenterText.Ago(now,now.AddMinutes(-5))=="5 min ago" && NotificationCenterText.Ago(now,now.AddHours(-3))=="3 h ago");
    }
}
