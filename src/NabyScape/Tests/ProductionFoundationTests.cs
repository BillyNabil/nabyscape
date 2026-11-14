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
        Fixture("Update download preference matches the owner snapshot and preserves existing opt-out",store=>
        {
            Assert(!store.Load<Settings>("new-profile").AutoDownloadUpdates,"New profile did not match the owner snapshot.");
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
            Assert(!fallback.ClipboardHistory && !fallback.DownloadsEnabled,"Unreadable profiles must not enable background capture.");
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
            Assert(settings.ClipboardHistory && settings.UiLanguage=="en" && !settings.AutoDownloadUpdates,"Reset changed capture consent or missed application preferences.");
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
            Assert(LocalizationCatalog.Languages.Select(l=>l.Code).SequenceEqual(new[]{"en","es","id","ru","uk","ko"}),"Supported UI language codes changed.");
            Assert(LocalizationCatalog.Languages.Select(l=>l.Name).SequenceEqual(new[]{"English","Español","Bahasa Indonesia","Русский","Українська","한국어"}),"Native language names changed.");
            foreach(string language in new[]{"es","id","ru","uk","ko","en"}){s.UiLanguage=language;s.AccessibilityTextScale=9;s.Normalize();Assert(s.UiLanguage==language && s.AccessibilityTextScale==1.5,"Valid language or text scale bound changed.");}
            foreach(string invalid in new[]{"RU","ko-KR","ua",""," uk"}){s.UiLanguage=invalid;s.Normalize();Assert(s.UiLanguage=="en","Unsupported language code was kept: "+invalid);}
            Assert(LocalizationCatalog.Translations("xx").Count==0 && LocalizationCatalog.NativeName("xx")=="English","Unknown languages must fall back to English.");
        });
        test("Every UI string is translated in every language with matching placeholders",()=>
        {
            var placeholder=new System.Text.RegularExpressions.Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
            string Slots(string text)=>string.Join(",",placeholder.Matches(text).Select(m=>m.Groups[1].Value).OrderBy(v=>v,StringComparer.Ordinal));
            var arguments=Enumerable.Range(0,10).Select(i=>(object)i).ToArray();
            Assert(LocalizationCatalog.Keys.Count>=435,"The English catalog lost entries.");
            foreach(var (code,_,translations) in LocalizationCatalog.Languages.Skip(1))
            {
                var missing=LocalizationCatalog.Keys.Where(key=>!translations.TryGetValue(key,out var text) || string.IsNullOrWhiteSpace(text)).ToArray();
                Assert(missing.Length==0,$"{code} is missing {missing.Length} translation(s), e.g. \"{missing.FirstOrDefault()}\". Add them to LocalizationCatalog.{code}.cs.");
                var mismatched=translations.Where(pair=>Slots(pair.Key)!=Slots(pair.Value)).Select(pair=>pair.Key).ToArray();
                Assert(mismatched.Length==0,$"{code} placeholders differ from English in: {string.Join(" | ",mismatched.Take(3))}");
                foreach(var pair in translations){try{_=string.Format(System.Globalization.CultureInfo.InvariantCulture,pair.Value,arguments);}catch(FormatException){throw new Exception($"{code} translation is not a valid format string: {pair.Key}");}}
            }
            foreach(string code in new[]{"ru","uk","ko"})Assert(LocalizationCatalog.Translations(code).Count==LocalizationCatalog.Keys.Count,$"{code} catalog is incomplete.");
        });
    }
}
