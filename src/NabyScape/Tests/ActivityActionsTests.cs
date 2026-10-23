using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NabyScape;

public static class ActivityActionsTests
{
    public static void Run(Action<string,bool> test)
    {
        var now=new DateTimeOffset(2026,10,16,10,0,0,TimeSpan.Zero);
        void Check(string name,Action<Store> action)
        {
            var path=Path.Combine(Path.GetTempPath(),"NabyScape-activities-"+Guid.NewGuid().ToString("N"));bool passed=false;
            try{action(new Store(path));passed=true;}catch{}finally{if(Directory.Exists(path))Directory.Delete(path,true);}
            test("Activities: "+name,passed);
        }
        static void Assert(bool value){if(!value)throw new InvalidOperationException("Activity assertion failed.");}
        static ActivityStep Open(string name)=>new(){Target=new(){Name=name,Target="https://example.com/"+name}};
        Check("ordered launch failures continue; duplicate and reentrant starts are blocked",store=>
        {
            using var awake=new KeepAwakeService(_=>1);var focus=new Countdown();var launches=new List<string>();ActivityActionsService? service=null;bool duplicate=true;
            using var owned=service=new(store,focus,awake,()=>{},_=>{},s=>{launches.Add(s.Name);duplicate=service!.Start(service.Definitions[0].Id);if(s.Name=="bad")throw new IOException("Unavailable");},()=>now);
            var item=new ActivityDefinition{Name="Workspace",Steps=new(){Open("first"),Open("bad"),Open("last"),new(){Kind="focus",Minutes=20},new(){Kind="awake",Minutes=30}}};
            Assert(service.Save(item));Assert(service.Start(item.Id));Assert(!duplicate && !service.Start(item.Id));
            Assert(launches.SequenceEqual(new[]{"first","bad","last"}) && service.Results.Count==5 && !service.Results[1].Success);
            Assert(focus.Deadline==now.AddMinutes(20) && awake.Until==now.AddMinutes(30) && service.LastError=="Unavailable");service.Stop();Assert(!focus.Running && !awake.Active && service.ActiveActivity==null && service.LastError==null);
        });
        Check("Stop ends the owned automatic break without changing step results",store=>
        {
            using var awake=new KeepAwakeService(_=>1);var focus=new Countdown();int saves=0;
            using var service=new ActivityActionsService(store,focus,awake,()=>saves++,_=>{},_=>{},()=>now);
            var item=new ActivityDefinition{Name="Study",Steps=new(){new(){Kind="focus",Minutes=25}}};Assert(service.Save(item) && service.Start(item.Id));
            var identity=focus.RunId;Assert(focus.Complete(now.AddMinutes(25)));
            focus.Start(300,now.AddMinutes(25),"break",preserveRunId:true);
            Assert(focus.RunId==identity && focus.Running && focus.Phase=="break");
            service.Stop();Assert(!focus.Running && focus.Phase=="" && service.ActiveActivity==null && saves==2 && service.Results.Single().Success);
        });
        Check("replacing Focus needs confirmation; Stop preserves later user replacements",store=>
        {
            using var awake=new KeepAwakeService(_=>1);var focus=new Countdown();using var service=new ActivityActionsService(store,focus,awake,()=>{},_=>{},_=>{},()=>now);
            var item=new ActivityDefinition{Name="Study",Steps=new(){new(){Kind="focus",Minutes=25},new(){Kind="awake",Minutes=30}}};Assert(service.Save(item));
            focus.Start(600,now,"work");var previous=focus.RunId;Assert(!service.Start(item.Id) && focus.RunId==previous && service.ActiveActivity==null);
            Assert(service.Start(item.Id,true));focus.Start(1500,now,"work");var replacement=focus.RunId;
            awake.Start(TimeSpan.FromMinutes(30),false,now);service.Stop();Assert(focus.RunId==replacement && focus.Running && awake.Active);
        });
        Check("persisted definitions reload without execution or active state",store=>
        {
            using var awake=new KeepAwakeService(_=>1);var focus=new Countdown();int launches=0;
            using(var service=new ActivityActionsService(store,focus,awake,()=>{},_=>{},_=>launches++,()=>now))
            {var item=new ActivityDefinition{Name="Saved",Steps=new(){Open("site")}};Assert(service.Save(item));item.Name="Unsaved edit";Assert(service.Start(item.Id));}
            using var reloaded=new ActivityActionsService(store,focus,awake,()=>{},_=>{},_=>launches++,()=>now);
            Assert(launches==1 && reloaded.ActiveActivity==null && reloaded.Results.Count==0 && reloaded.Definitions.Single().Name=="Saved");
        });
        Check("invalid targets, template without target, durations, and duplicate focus rejected",store=>
        {
            using var awake=new KeepAwakeService(_=>1);using var service=new ActivityActionsService(store,new Countdown(),awake,()=>{},_=>{},_=>{},()=>now);
            Assert(!service.Save(new(){Name="Template",Steps=new(){new(){Kind="open"},new(){Kind="focus"}}}));
            foreach(var value in new[]{"cmd /c calc","javascript:alert(1)","mailto:person@example.com","https://user:pass@example.com"})
                Assert(!service.Save(new(){Name="Invalid",Steps=new(){new(){Target=new(){Name="Bad",Target=value}}}}));
            Assert(!service.Save(new(){Name="Invalid",Steps=new(){new(){Kind="focus",Minutes=0}}}));
            Assert(!service.Save(new(){Name="Invalid",Steps=new(){new(){Kind="focus"},new(){Kind="focus"}}}));
            Assert(!service.Save(new(){Name="Invalid",Steps=new(){new(){Kind="awake",Minutes=1441}}}));
            Assert(service.Definitions.Count==0);
        });
        Check("saving failure preserves previous definitions",store=>
        {
            using var awake=new KeepAwakeService(_=>1);using var service=new ActivityActionsService(store,new Countdown(),awake,()=>{},_=>{},_=>{},()=>now);
            var item=new ActivityDefinition{Name="Keep",Steps=new(){Open("site")}};Assert(service.Save(item));
            using var locked=new FileStream(Path.Combine(store.DirectoryPath,"activities.json"),FileMode.Open,FileAccess.Read,FileShare.Read);item.Name="Changed";Assert(!service.Save(item));Assert(service.Definitions.Single().Name=="Keep");
        });
        Check("expired or manually replaced awake is not stopped; failed releases can retry",store=>
        {
            bool fail=false;using var awake=new KeepAwakeService(_=>fail?0u:1u);using var service=new ActivityActionsService(store,new Countdown(),awake,()=>{},_=>{},_=>{},()=>now);
            var item=new ActivityDefinition{Name="Awake",Steps=new(){new(){Kind="awake",Minutes=1}}};Assert(service.Save(item) && service.Start(item.Id));
            fail=true;service.Stop();Assert(service.ActiveActivity!=null && awake.Active && service.LastError!=null);fail=false;service.Stop();Assert(service.ActiveActivity==null && !awake.Active && service.LastError==null);
            Assert(service.Start(item.Id));awake.Tick(now.AddMinutes(1));awake.Start(TimeSpan.FromMinutes(5),true,now);service.Stop();Assert(awake.Active && awake.KeepDisplayOn);
        });
    }
}
