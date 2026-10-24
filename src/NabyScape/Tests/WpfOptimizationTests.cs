using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace NabyScape;
internal static class WpfOptimizationTests
{
    internal static void Run(Action<string,Action> test)
    {
        test("WPF: application contains no alternate renderer payload or settings switch",()=>
        {
            var assembly=typeof(App).Assembly;
            if(assembly.GetManifestResourceNames().Any(name=>name is "NabyScape.NativeHost.exe" or "NabyScape.CoreHost.exe" or "NabyScape.Native.dll") || typeof(Settings).GetProperty("RendererMode")!=null)
                throw new Exception("An alternate renderer is still present.");
        });
        test("WPF: retired host cleanup preserves the main executable and unrelated files",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-wpf-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string main=Path.Combine(root,"NabyScape.exe"),personal=Path.Combine(root,"personal.txt");File.WriteAllText(main,"main");File.WriteAllText(personal,"keep");
                foreach(string name in Installer.RetiredHostFiles)File.WriteAllText(Path.Combine(root,name),"retired");
                Installer.RemoveRetiredHostFiles(root,main);
                if(File.ReadAllText(main)!="main" || File.ReadAllText(personal)!="keep" || Installer.RetiredHostFiles.Any(name=>File.Exists(Path.Combine(root,name))))throw new Exception("Owned-file migration failed.");
            }
            finally{Directory.Delete(root,true);}
        });
        test("Removed features: routines and satellite have no runtime type or setting",()=>
        {
            foreach(string name in new[]{"ActivitySatellite","SatelliteSource","ActivitiesEnabled"})
                if(typeof(Settings).GetProperty(name)!=null)throw new Exception("Retired setting is exposed: "+name);
            foreach(string name in new[]{"NabyScape.ActivityActionsService","NabyScape.ActivityDefinition","NabyScape.SecondaryActivity"})
                if(typeof(App).Assembly.GetType(name)!=null)throw new Exception("Retired runtime is present: "+name);
            if(typeof(App).GetProperty("Activities",BindingFlags.Instance|BindingFlags.NonPublic)!=null)throw new Exception("The routine service still starts with the app.");
        });
        test("WPF: old renderer preference retires on save without losing unknown preferences",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-wpf-preferences-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new Store(root);string path=Path.Combine(root,"settings.json");const string original="{\"RendererMode\":\"Native\",\"ActivitySatellite\":true,\"SatelliteSource\":\"RAM\",\"ActivitiesEnabled\":true,\"Width\":371,\"FuturePreference\":{\"Keep\":7}}";File.WriteAllText(path,original);
                var settings=store.Load<Settings>("settings");if(settings.Width!=371 || File.ReadAllText(path)!=original)throw new Exception("Loading modified preferences.");
                store.Save("settings",settings);var saved=JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                if(new[]{"RendererMode","ActivitySatellite","SatelliteSource","ActivitiesEnabled"}.Any(saved.ContainsKey) || saved["FuturePreference"]!["Keep"]!.GetValue<int>()!=7 || saved["Width"]!.GetValue<double>()!=371)throw new Exception("Preference migration lost data.");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        });
    }
}
