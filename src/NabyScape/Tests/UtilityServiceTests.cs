using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace NabyScape;

public static class UtilityServiceTests
{
    // All execution requests are injected: these checks never change Windows power settings.
    public static void Run(Action<string,bool> test)
    {
        void Check(string name,Action action)
        {
            bool passed;try { action(); passed=true; } catch { passed=false; }
            test("Utilities: "+name,passed);
        }
        void WithStore(string name,Action<Store> action) => Check(name,()=>
        {
            var path=Path.Combine(Path.GetTempPath(),"NabyScape-utilities-"+Guid.NewGuid().ToString("N"));
            try { action(new Store(path)); }
            finally { if(Directory.Exists(path))Directory.Delete(path,true); }
        });
        static void Assert(bool result) { if(!result)throw new InvalidOperationException("Utility assertion failed."); }
        static void Throws<T>(Action action) where T:Exception
        {
            try { action(); } catch(T) { return; }
            throw new InvalidOperationException("Expected "+typeof(T).Name);
        }
        var now=new DateTimeOffset(2026,10,14,12,0,0,TimeSpan.FromHours(7));

        Check("keep awake starts disabled, expires, resets flags, and releases on disposal",()=>
        {
            var flags=new List<uint>(); int changed=0;
            var service=new KeepAwakeService(value=>{flags.Add(value);return 1;});
            Assert(!service.Active && service.Until==null && !service.KeepDisplayOn && flags.Count==0);
            service.Changed+=()=>changed++;
            service.Start(TimeSpan.FromMinutes(15),true,now);
            Assert(service.Active && service.Until==now.AddMinutes(15) && service.KeepDisplayOn);
            service.Tick(now.AddMinutes(14)); Assert(flags.Count==1);
            service.Tick(now.AddMinutes(15));
            Assert(!service.Active && service.Until==null && !service.KeepDisplayOn);
            service.Start(null,false,now); service.Tick(now.AddDays(1)); Assert(service.Active && service.Until==null);
            service.Dispose();service.Dispose();
            Assert(flags.SequenceEqual(new uint[]{0x80000003,0x80000000,0x80000001,0x80000000}) && changed==4);
            Throws<ObjectDisposedException>(()=>service.Start(null,false,now));
        });
        Check("keep awake validates durations and preserves state on native failures",()=>
        {
            bool fail=true; int calls=0;
            var service=new KeepAwakeService(_=>{calls++;return fail?0u:1u;});
            Throws<ArgumentException>(()=>service.Start(TimeSpan.Zero,false,now));
            Throws<ArgumentException>(()=>service.Start(TimeSpan.FromHours(25),false,now));
            Assert(calls==0);
            Throws<InvalidOperationException>(()=>service.Start(null,true,now)); Assert(!service.Active);
            fail=false;service.Start(TimeSpan.FromHours(24),false,now);fail=true;
            Throws<InvalidOperationException>(()=>service.Start(null,true,now));
            Assert(service.Active && !service.KeepDisplayOn && service.Until==now.AddDays(1));
            Throws<InvalidOperationException>(()=>service.Tick(now.AddDays(1))); Assert(service.Active);
            Throws<InvalidOperationException>(service.Dispose); Assert(service.Active);
            fail=false;service.Dispose(); Assert(!service.Active);
        });
        Check("keep awake rejects calls from another thread before native access",()=>
        {
            int calls=0;using var service=new KeepAwakeService(_=>{calls++;return 1;});
            Exception? error=null;
            var thread=new Thread(()=>{try { service.Start(null,false,now); } catch(Exception ex) { error=ex; }});
            thread.Start();thread.Join();Assert(error is InvalidOperationException && calls==0);
        });
        Check("clean links preserve raw paths, escaping, fragments, order, and repeated functional keys",()=>
        {
            Assert(CleanLink.Clean("  https://EXAMPLE.com/a%2fb/../c?q=a%2Bb&q=two+words&utm_SOURCE=ad&empty=&%66bclid=x#part%2F?utm_x=keep  ")
                =="https://EXAMPLE.com/a%2fb/../c?q=a%2Bb&q=two+words&empty=#part%2F?utm_x=keep");
            Assert(CleanLink.Clean("https://example.com/?UTM_a=1&GCLID=2#hash")=="https://example.com/#hash");
            Assert(CleanLink.Clean("https://example.com/#?utm_a=keep")=="https://example.com/#?utm_a=keep");
            Assert(CleanLink.Clean("https://example.com/?a=1&a=2")=="https://example.com/?a=1&a=2");
            Assert(CleanLink.Clean("https://example.com/?dclid=x&msclkid=x&mc_cid=x&mc_eid=x&igshid=x&_ga=x&_gl=x")=="https://example.com/");
        });
        Check("signed links remain entirely unchanged including tracking parameters",()=>
        {
            foreach(var key in new[]{"X-Amz-Signature","x-goog-signature","Signature","%73ig","oauth_signature","hmac"})
            {
                string url="https://example.com/p%2f?utm_source=x&"+key+"=a%2Bb#f";
                Assert(CleanLink.Clean(" "+url+" ")==url);
            }
        });
        Check("clean links reject invalid schemes, credentials, oversized and malformed input",()=>
        {
            foreach(var value in new[]{"","abc","ftp://example.com","javascript:alert(1)","https://user:pass@example.com/","https://example.com/%zz","https://example.com/a b","https://example.com/"+new string('a',8192)})
                Throws<ArgumentException>(()=>CleanLink.Clean(value));
            Assert(CleanLink.Clean("HTTPS://example.com/?uTm_X=y")=="HTTPS://example.com/");
        });
        WithStore("calendar saves, reloads, preserves offsets and IDs, and removes",store=>
        {
            var calendar=new CalendarService(store); int changed=0;calendar.Changed+=()=>changed++;
            var item=calendar.Add("  Planning  ",now,now.AddHours(1)," https://example.com/?utm_source=preserved ");
            Assert(item.Title=="Planning" && calendar.Events.Count==1 && changed==1);
            var restored=new CalendarService(store);var saved=restored.Events.Single();
            Assert(saved.Id==item.Id && saved.Start==now && saved.Start.Offset==now.Offset && saved.End==now.AddHours(1) && saved.MeetingUrl=="https://example.com/?utm_source=preserved");
            restored.Remove("missing");Assert(restored.Events.Count==1);
            restored.Remove(item.Id);Assert(restored.Events.Count==0 && new CalendarService(store).Events.Count==0);
        });
        WithStore("calendar validates title, date range, URL and 200-event limit",store=>
        {
            var calendar=new CalendarService(store);
            foreach(var title in new[]{" ",new string('a',121)})Throws<ArgumentException>(()=>calendar.Add(title,now,now.AddHours(1),""));
            foreach(var end in new[]{now,now.AddMinutes(-1),now.AddDays(31).AddTicks(1)})Throws<ArgumentException>(()=>calendar.Add("a",now,end,""));
            foreach(var url in new[]{"relative","file:///C:/test","https://user@example.com", "https://example.com/"+new string('a',2048)})
                Throws<ArgumentException>(()=>calendar.Add("a",now,now.AddHours(1),url));
            Assert(calendar.Events.Count==0);
            calendar.Add(new string('t',120),now,now.AddDays(31),"");
            for(int i=1;i<200;i++)calendar.Add("Event "+i,now,now.AddHours(1),"");
            Throws<InvalidOperationException>(()=>calendar.Add("overflow",now,now.AddHours(1),""));
            Assert(calendar.Events.Count==200 && new CalendarService(store).Events.Count==200);
        });
        WithStore("calendar disk failures leave memory and change notifications untouched",store=>
        {
            var calendar=new CalendarService(store);var item=calendar.Add("existing",now,now.AddHours(1),"");
            int changed=0;calendar.Changed+=()=>changed++;
            using var locked=new FileStream(Path.Combine(store.DirectoryPath,"calendar.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
            Throws<IOException>(()=>calendar.Add("new",now,now.AddHours(1),""));
            Throws<IOException>(()=>calendar.Remove(item.Id));
            Assert(calendar.Events.Count==1 && calendar.Events[0].Id==item.Id && changed==0 && new CalendarService(store).Events.Count==1);
        });
        WithStore("calendar bounds and normalizes stored records without rewriting source",store=>
        {
            var entries=new List<CalendarEvent> { null!,new(){Id="keep",Title="  Existing  ",Start=now,End=now.AddHours(1)},
                new(){Id="keep",Title="duplicate",Start=now,End=now.AddHours(1)},new(){Title="bad dates",Start=now,End=now},
                new(){Title="bad URL",Start=now,End=now.AddHours(1),MeetingUrl="file:///C:/bad"},
                new(){Id="long",Title=new string('t',200),Start=now,End=now.AddHours(1)} };
            entries.AddRange(Enumerable.Range(0,220).Select(i=>new CalendarEvent {Id="id"+i,Title="event",Start=now,End=now.AddHours(1)}));
            store.Save("calendar",entries);string source=File.ReadAllText(Path.Combine(store.DirectoryPath,"calendar.json"));
            var calendar=new CalendarService(store);
            Assert(calendar.Events.Count==200 && calendar.Events[0].Id=="keep" && calendar.Events[0].Title=="Existing" && calendar.Events[1].Title.Length==120);
            Assert(calendar.Events.Select(e=>e.Id).Distinct().Count()==200 && source==File.ReadAllText(Path.Combine(store.DirectoryPath,"calendar.json")));
        });
        WithStore("calendar corrupt JSON is backed up and preserved until an explicit mutation",store=>
        {
            var path=Path.Combine(store.DirectoryPath,"calendar.json");File.WriteAllText(path,"{bad json");
            var calendar=new CalendarService(store);
            Assert(calendar.Events.Count==0 && store.Warning!=null && File.ReadAllText(path)=="{bad json" && Directory.GetFiles(store.DirectoryPath,"calendar.json.backup-*").Length==1);
        });
    }
}
