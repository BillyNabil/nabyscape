using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class PersistenceTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
        void Fixture(string name,Action<string> run)=>test(name,()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-persistence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{run(root);}finally{foreach(string file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);Directory.Delete(root,true);}
        });
        Fixture("Migration fresh copy rebases only internal clipboard images and retains legacy",root=>
        {
            string legacy=Path.Combine(root,"LumaIsland"),next=Path.Combine(root,"NabyScape");var old=new Store(legacy);
            string images=Path.Combine(legacy,"clipboard-images");Directory.CreateDirectory(images);string image=Path.Combine(images,"test.png");File.WriteAllBytes(image,new byte[]{1,2,3});
            old.Save("settings",new Settings{Width=311,AutoStart=true,ClipboardHistory=true,Shortcuts=new(){new(){Name="External",Target=image}}});
            old.Save("productivity",new ProductivityData{Note="private",Tasks=new(){new(){Text="keep"}},Shelf=new(){new(){Path=image}},Clips=new(){new(){Kind="image",ImagePath=image},new(){Kind="image",ImagePath=Path.Combine(root,"outside.png")}}});
            old.Save("activities",new ActivityActionsData{Activities=new(){new(){Name="Keep",Steps=new(){new(){Target=new(){Name="Keep",Target=image}}}}}});
            var migrated=new Store(legacy,next,true);var data=migrated.Load<ProductivityData>("productivity");var settings=migrated.Load<Settings>("settings");
            Assert(migrated.DirectoryPath==next && Directory.Exists(legacy) && File.Exists(image) && settings.Width==311 && settings.AutoStart && settings.ClipboardHistory);
            Assert(data.Clips[0].ImagePath==Path.Combine(next,"clipboard-images","test.png") && data.Clips[1].ImagePath==Path.Combine(root,"outside.png") && data.Shelf[0].Path==image && settings.Shortcuts[0].Target==image && data.Note=="private" && data.Tasks.Count==1);
            Assert(File.ReadAllText(Path.Combine(next,"activities.json"))==File.ReadAllText(Path.Combine(legacy,"activities.json")));
        });
        Fixture("Migration ignores interrupted staging and never merges existing destination",root=>
        {
            string legacy=Path.Combine(root,"legacy"),next=Path.Combine(root,"next");new Store(legacy).Save("settings",new Settings{Width=301});Directory.CreateDirectory(next+".migration-interrupted");File.WriteAllText(Path.Combine(next+".migration-interrupted","settings.json"),"{broken");
            var migrated=new Store(legacy,next,true);Assert(migrated.Load<Settings>("settings").Width==301);
            new Store(next).Save("settings",new Settings{Width=322});var existing=new Store(legacy,next,true);Assert(existing.Load<Settings>("settings").Width==322 && existing.Warning!.Contains("Both"));
        });
        Fixture("Migration invalid JSON or blocked destination falls back with warning",root=>
        {
            string legacy=Path.Combine(root,"legacy"),next=Path.Combine(root,"next");var old=new Store(legacy);old.Save("settings",new Settings{Width=301});File.WriteAllText(Path.Combine(legacy,"broken.json"),"{broken");
            var failed=new Store(legacy,next,true);Assert(failed.DirectoryPath==legacy && failed.Warning!=null && !Directory.Exists(next) && failed.Load<Settings>("settings").Width==301);
            File.Delete(Path.Combine(legacy,"broken.json"));File.WriteAllText(Path.Combine(legacy,"productivity.json"),"{\"Tasks\":\"invalid\"}");failed=new Store(legacy,next,true);Assert(failed.DirectoryPath==legacy && failed.Warning!=null && !Directory.Exists(next));File.Delete(Path.Combine(legacy,"productivity.json"));File.SetAttributes(Path.Combine(legacy,"settings.json"),FileAttributes.ReadOnly);failed=new Store(legacy,next,true);Assert(failed.DirectoryPath==legacy && failed.Warning!=null && !Directory.Exists(next));File.SetAttributes(Path.Combine(legacy,"settings.json"),FileAttributes.Normal);File.WriteAllText(next,"blocked");File.SetAttributes(next,FileAttributes.ReadOnly);
            failed=new Store(legacy,next,true);Assert(failed.DirectoryPath==legacy && failed.Warning!=null && failed.Load<Settings>("settings").Width==301);
        });
        Fixture("Explicit Store path does not migrate sibling legacy data",root=>
        {string legacy=Path.Combine(root,"LumaIsland"),next=Path.Combine(root,"NabyScape");new Store(legacy).Save("settings",new Settings{Width=301});var isolated=new Store(next);Assert(isolated.Load<Settings>("settings").Width==228 && !File.Exists(Path.Combine(next,"settings.json")));});
        Fixture("Reset category all and restore preserve capture startup shortcuts and personal files",root=>
        {
            var store=new Store(root);var settings=new Settings{Width=333,Accent="#112233",AutoStart=true,ClipboardHistory=true,Shortcuts=new(){new(){Name="Keep",Target="external"}}};
            store.Save("productivity",new ProductivityData{Note="keep",Tasks=new(){new(){Text="keep"}},Clips=new(){new(){Text="keep"}}});string before=File.ReadAllText(Path.Combine(root,"productivity.json"));
            PreferenceReset.Reset(store,settings,"Appearance");Assert(settings.Width==333 && settings.Accent=="#FFFFFF" && settings.AutoStart && settings.ClipboardHistory && settings.Shortcuts[0].Target=="external");
            PreferenceReset.Restore(store,settings);Assert(settings.Accent=="#112233" && settings.Width==333);
            PreferenceReset.Reset(store,settings,null);Assert(settings.Width==228 && settings.Accent=="#FFFFFF" && settings.AutoStart && settings.ClipboardHistory && settings.Shortcuts[0].Target=="external" && before==File.ReadAllText(Path.Combine(root,"productivity.json")));
            settings.ClipboardHistory=false;settings.AutoStart=false;PreferenceReset.Restore(store,settings);Assert(settings.Width==333 && !settings.ClipboardHistory && !settings.AutoStart);
        });
        Fixture("Backup save errors abort reset and invalid restore leaves settings intact",root=>
        {
            var store=new Store(root);var settings=new Settings{Width=333};store.Save("settings",settings);string before=File.ReadAllText(Path.Combine(root,"settings.json"));Directory.CreateDirectory(Path.Combine(root,"preference-backup.json"));
            bool failed=false;try{PreferenceReset.Reset(store,settings,null);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failed=true;}Assert(failed && settings.Width==333 && before==File.ReadAllText(Path.Combine(root,"settings.json")));
            Directory.Delete(Path.Combine(root,"preference-backup.json"));File.WriteAllText(Path.Combine(root,"preference-backup.json"),"{\"Width\":299}");failed=false;try{PreferenceReset.Restore(store,settings);}catch(InvalidDataException){failed=true;}Assert(failed && settings.Width==333 && before==File.ReadAllText(Path.Combine(root,"settings.json")));
        });
        test("Reset category map covers all resettable preferences exactly once",()=>
        {var excluded=new[]{"AutoStart","ClipboardHistory","Shortcuts"};var keys=PreferenceReset.Categories.Values.SelectMany(v=>v).ToArray();Assert(keys.Distinct().Count()==keys.Length && typeof(Settings).GetProperties().Where(p=>p.CanWrite && !excluded.Contains(p.Name)).All(p=>keys.Contains(p.Name)));});
        test("Tuck defaults bounds profile and all shortcut conflicts",()=>
        {
            var s=new Settings();Assert(!s.AlwaysTucked && s.TuckEdge==6 && s.HideFullscreen && !s.HideMaximized && s.TuckHotkeyKey=="T");
            s.TuckEdge=1;s.Normalize();Assert(s.TuckEdge==3 && AutoTuckPolicy.Offset(32,s.TuckEdge)==-29);s.TuckEdge=99;s.Normalize();Assert(s.TuckEdge==12 && AutoTuckPolicy.Offset(32,s.TuckEdge)==-20);
            s.AlwaysTucked=true;var other=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(s),other);Assert(other.AlwaysTucked && other.TuckEdge==12);
            s.TuckHotkeyKey="I";Assert(HotkeyPolicy.Conflict(s,"Tuck").Contains("Panel") && HotkeyPolicy.Conflict(s,"Panel").Contains("Tuck"));s.HotkeyEnabled=false;s.TuckHotkeyKey="H";Assert(HotkeyPolicy.Conflict(s,"Tuck").Contains("Visibility"));s.TuckHotkeyEnabled=false;Assert(HotkeyPolicy.Conflict(s,"Visibility")=="");s.HotkeyEnabled=true;s.HotkeyKey="H";Assert(HotkeyPolicy.Conflict(s,"Visibility").Contains("Panel"));
        });
    }
}
