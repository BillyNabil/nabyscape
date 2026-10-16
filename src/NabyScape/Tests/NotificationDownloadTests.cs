using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace NabyScape;
internal static class NotificationDownloadTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool pass){if(!pass)throw new Exception("Notification/download fixture failed.");}
        test("Notification defaults, routing, toggles, cap, grouping, delivery failure and timeout",()=>
        {
            var s=new Settings();Assert(s.NotificationDelivery=="Notch" && !s.DownloadsEnabled);
            Assert(InternalNotifications.Route("Notch",true)==(false,true));Assert(InternalNotifications.Route("Both",false)==(true,true));Assert(InternalNotifications.Route("Off",false)==(false,false));
            var manager=new InternalNotifications();var now=DateTimeOffset.UtcNow;int windows=0;
            void Post(string kind,bool hidden=false)=>manager.Post(s,new(kind,"title","detail"),hidden,_=>windows++,now);
            Post("timer",true);Assert(windows==1 && manager.Count==0);
            s.NotificationDelivery="Both";Post("timer");Assert(windows==2 && manager.Count==1);
            s.NotifyTimer=false;Post("timer");Assert(manager.Count==1 && windows==2);
            s.NotificationDelivery="Off";Post("failure");Assert(manager.Count==1 && windows==2);
            s.NotificationDelivery="Notch";for(int i=0;i<20;i++)Post("failure");Assert(manager.Count==10);
            manager.Tick(now.AddHours(1),true);Assert(manager.Count==10);manager.Tick(now.AddHours(1).AddSeconds(13),false);Assert(manager.Count==9);
            manager.Clear();manager.Post(s,new("downloads","one","inferred",new[]{"a"}),false,_=>{},now);manager.Post(s,new("downloads","two","inferred",new[]{"a","b"}),false,_=>{},now.AddSeconds(1));Assert(manager.Count==1 && manager.Current!.Files!.SequenceEqual(new[]{"a","b"}));
            manager.Tick(now.AddMinutes(1),true);manager.Post(s,new("downloads","later burst","inferred",new[]{"c"}),false,_=>{},now.AddMinutes(1));Assert(manager.Count==2);
            s.NotificationDelivery="Both";manager.Post(s,new("failure","failure","delivery"),false,_=>throw new IOException(),now);Assert(manager.Count==3);
            s.DownloadsFolder=@"C:\private-fixture";Assert(!CustomizationProfile.Export(s).Contains("private-fixture") && !CustomizationProfile.Export(s).Contains("DownloadsEnabled"));
        });
        test("Downloads temp folder baseline, partial rename, growing size, locks, races, burst, missing, folder change and disable cleanup",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-arrivals-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string other=Path.Combine(root,"other");Directory.CreateDirectory(other);
            var arrivals=new ConcurrentQueue<string>();var errors=new ConcurrentQueue<string>();
            using var watcher=new DownloadArrivals(paths=>{foreach(string path in paths)arrivals.Enqueue(path);},errors.Enqueue);
            bool Wait(Func<bool> condition,int ms=6500)=>SpinWait.SpinUntil(condition,ms);
            try
            {
                string baseline=Path.Combine(root,"existing.txt");File.WriteAllText(baseline,"baseline");watcher.Configure(true,root);Assert(watcher.Running);
                string partial=Path.Combine(root,"download.crdownload"),final=Path.Combine(root,"download.txt");File.WriteAllText(partial,"temporary");Thread.Sleep(600);Assert(arrivals.IsEmpty);File.Move(partial,final);
                Assert(Wait(()=>arrivals.Contains(final)) && !arrivals.Contains(baseline));
                for(int i=0;i<8;i++)File.SetLastWriteTimeUtc(final,DateTime.UtcNow);string renamed=Path.Combine(root,"renamed.txt");File.Move(final,renamed);Thread.Sleep(700);Assert(arrivals.Count==1);
                string growing=Path.Combine(root,"growing.txt");File.WriteAllText(growing,"a");Thread.Sleep(1100);File.AppendAllText(growing,"b");Thread.Sleep(1300);Assert(!arrivals.Contains(growing));Assert(Wait(()=>arrivals.Contains(growing)));
                string locked=Path.Combine(root,"locked.txt");using(var held=new FileStream(locked,FileMode.Create,FileAccess.Write,FileShare.None)){held.WriteByte(1);Thread.Sleep(3000);Assert(!arrivals.Contains(locked));}Assert(Wait(()=>arrivals.Contains(locked)));
                string cancel=Path.Combine(root,"cancel.txt");File.WriteAllText(cancel,"gone");File.Delete(cancel);
                string nested=Path.Combine(other,"nested.txt");File.WriteAllText(nested,"nested");
                for(int i=0;i<12;i++)File.WriteAllText(Path.Combine(root,$"burst-{i}.txt"),"x");Assert(Wait(()=>arrivals.Count(p=>Path.GetFileName(p).StartsWith("burst-"))==12));Assert(!arrivals.Contains(cancel) && !arrivals.Contains(nested));
                watcher.Configure(true,other);int count=arrivals.Count;Thread.Sleep(2600);Assert(arrivals.Count==count);string fresh=Path.Combine(other,"fresh.txt");File.WriteAllText(fresh,"new");Assert(Wait(()=>arrivals.Contains(fresh)));
                watcher.RequestReconcile();Thread.Sleep(700);Assert(arrivals.Count(p=>p==fresh)==1);
                for(int i=0;i<180;i++)File.WriteAllText(Path.Combine(other,$"capacity-{i}.txt"),"x");Assert(watcher.Candidates<=128);
                watcher.Configure(false,"");Assert(!watcher.Running && watcher.Candidates==0);count=arrivals.Count;File.WriteAllText(Path.Combine(other,"disabled.txt"),"x");Thread.Sleep(700);Assert(arrivals.Count==count && errors.IsEmpty);
                watcher.Configure(true,other);Directory.Delete(other,true);Assert(Wait(()=>!watcher.Running && errors.Count==1));watcher.Configure(false,"");Assert(watcher.Candidates==0);
                watcher.Configure(true,Path.Combine(root,"missing-folder"));Assert(!watcher.Running && errors.Count==2);
                Assert(!DownloadArrivals.FinalName("a.part") && !DownloadArrivals.FinalName("a.crdownload") && !DownloadArrivals.FinalName("~a.tmp") && DownloadArrivals.FinalName("a.pdf"));
            }
            finally{watcher.Dispose();Directory.Delete(root,true);}
        });
    }
}
