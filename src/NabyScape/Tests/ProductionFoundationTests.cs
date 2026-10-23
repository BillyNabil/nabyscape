using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class ProductionFoundationTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        void Fixture(string name,Action<Store> run)=>test(name,()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-foundations-"+Guid.NewGuid().ToString("N"));
            try{run(new Store(root));}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        });
        Fixture("Welcome setup offers new profiles and remembers Skip",store=>
        {
            Assert(WelcomeSetup.ShouldOffer(store,false),"New profile did not receive setup.");
            Assert(WelcomeSetup.ShouldOffer(store,true),"Unfinished setup did not resume.");
            WelcomeSetup.Complete(store);
            Assert(!WelcomeSetup.ShouldOffer(store,false),"Completed or skipped setup reopened.");
        });
        Fixture("Welcome setup preserves existing profiles",store=>
        {
            store.Save("settings",new Settings{Width=333});
            string before=File.ReadAllText(Path.Combine(store.DirectoryPath,"settings.json"));
            Assert(!WelcomeSetup.ShouldOffer(store,true),"Upgrade unexpectedly showed setup.");
            WelcomeSetup.Complete(store);
            Assert(before==File.ReadAllText(Path.Combine(store.DirectoryPath,"settings.json")),"Setup marker changed preferences.");
        });
        Fixture("Update download default preserves existing and legacy opt-out",store=>
        {
            Assert(store.Load<Settings>("new-profile").AutoDownloadUpdates,"New profile did not enable update downloads.");
            store.Save("existing",new Settings{AutoDownloadUpdates=false});
            Assert(!store.Load<Settings>("existing").AutoDownloadUpdates,"Existing opt-out was overwritten.");
            File.WriteAllText(Path.Combine(store.DirectoryPath,"legacy.json"),"{}");
            Assert(!store.Load<Settings>("legacy").AutoDownloadUpdates,"Legacy profile unexpectedly opted into downloads.");
        });
        Fixture("Future schema preserves payload and metadata and refuses defaults overwrite",store=>
        {
            string payload=Path.Combine(store.DirectoryPath,"settings.json"),schema=Path.Combine(store.DirectoryPath,"settings"+Store.SchemaSidecarSuffix);
            string original="{\"Width\":333,\"FutureContent\":\"preserve\"}",metadata="{\"SchemaVersion\":999}";
            File.WriteAllText(payload,original);File.WriteAllText(schema,metadata);
            var fallback=store.Load<Settings>("settings");bool rejected=false;
            try{store.Save("settings",fallback);}catch(StoreDocumentException){rejected=true;}
            Assert(rejected && File.ReadAllText(payload)==original && File.ReadAllText(schema)==metadata,"Future profile was changed.");
            Assert(store.GetHealth().FutureSchemaDocuments==1,"Future schema status is missing.");
        });
        Fixture("Corrupt load stays protected until explicit successful reload",store=>
        {
            string path=Path.Combine(store.DirectoryPath,"settings.json");File.WriteAllText(path,"{broken");
            var fallback=store.Load<Settings>("settings");string repaired=JsonSerializer.Serialize(new Settings{Width=333});File.WriteAllText(path,repaired);
            bool rejected=false;try{store.Save("settings",fallback);}catch(StoreDocumentException){rejected=true;}
            Assert(rejected && File.ReadAllText(path)==repaired,"Fallback overwrote externally recovered content.");
            var loaded=store.Load<Settings>("settings");Assert(loaded.Width==333,"Explicit reload did not read recovered content.");
            loaded.Width=340;store.Save("settings",loaded);Assert(store.Load<Settings>("settings").Width==340,"Successful reload did not unblock saves.");
        });
        Fixture("Preference restore accepts nullable location and preserves capture consent",store=>
        {
            var settings=new Settings{Width=333,WeatherLatitude=null,WeatherLongitude=null,ClipboardHistory=true,UiLanguage="es",AutoDownloadUpdates=false};
            PreferenceReset.Reset(store,settings,null);
            Assert(settings.ClipboardHistory && settings.UiLanguage=="en" && settings.AutoDownloadUpdates,"Reset changed capture consent or missed application preferences.");
            settings.ClipboardHistory=false;PreferenceReset.Restore(store,settings);
            Assert(!settings.ClipboardHistory && settings.Width==333 && settings.WeatherLatitude==null && settings.WeatherLongitude==null && settings.UiLanguage=="es" && !settings.AutoDownloadUpdates,"Restore changed consent or nullable preferences.");
            var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(store.DirectoryPath,"preference-backup.json")))!;
            node.AsObject().Remove("WeatherLatitude");File.WriteAllText(Path.Combine(store.DirectoryPath,"preference-backup.json"),node.ToJsonString());
            bool rejected=false;try{PreferenceReset.Restore(store,settings);}catch(InvalidDataException){rejected=true;}
            Assert(rejected && settings.Width==333,"Missing required backup field was accepted.");
        });
        Fixture("Private diagnostics is bounded and excludes content paths and exception text",store=>
        {
            const string secret="fixture-private-content-do-not-export";
            store.Save("productivity",new ProductivityData{Note=secret});
            store.Log(new IOException(secret+" "+store.DirectoryPath));
            for(int i=0;i<100;i++)PrivateDiagnostics.Record(DiagnosticStage.Application,DiagnosticCode.Completed,TimeSpan.FromSeconds(1));
            string report=PrivateDiagnostics.CreateReport(store);using var json=JsonDocument.Parse(report);
            Assert(json.RootElement.GetProperty("RecentEvents").GetArrayLength()==64,"Diagnostic event bound changed.");
            Assert(!report.Contains(secret,StringComparison.Ordinal) && !report.Contains(store.DirectoryPath,StringComparison.OrdinalIgnoreCase),"Private content leaked to diagnostics.");
            Assert(json.RootElement.GetProperty("EventCapacity").GetInt32()==64,"Diagnostic schema capacity missing.");
        });
        test("Language accessibility preferences normalize without altering user content",()=>
        {
            var s=new Settings{UiLanguage="invalid",AccessibilityTextScale=double.NaN,AccessibilityHighContrast=true};s.Normalize();
            Assert(s.UiLanguage=="en" && s.AccessibilityTextScale==1 && s.AccessibilityHighContrast,"Invalid accessibility preferences were not normalized.");
            foreach(string language in new[]{"es","id","en"}){s.UiLanguage=language;s.AccessibilityTextScale=9;s.Normalize();Assert(s.UiLanguage==language && s.AccessibilityTextScale==1.5,"Valid language or text scale bound changed.");}
        });
    }
}
