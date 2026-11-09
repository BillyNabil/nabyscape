using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace NabyScape;

internal static class LocalizationTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        test("Russian build migration preserves data and subsequent explicit language choices",()=>
        {
            string folder=Path.Combine(Path.GetTempPath(),"NabyScape-ru-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new Store(folder);var settings=new Settings{UiLanguage="en",Width=333};store.Save("settings",settings);
                string original=File.ReadAllText(Path.Combine(folder,"settings.json"));
                RussianBuildProfile.Initialize(store,settings);
                Assert(settings.UiLanguage=="ru" && settings.Width==333,"Migration changed non-language settings.");
                Assert(File.ReadAllText(Path.Combine(folder,"settings.before-russian-build.json"))==original,"Original preferences were not backed up.");
                settings.UiLanguage="en";store.Save("settings",settings);RussianBuildProfile.Initialize(store,settings);
                Assert(store.Load<Settings>("settings").UiLanguage=="en","Later explicit language choice was overwritten.");
            }
            finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        });
        test("Russian build never checks the upstream release channel",()=>
        {
            string folder=Path.Combine(Path.GetTempPath(),"NabyScape-ru-update-"+Guid.NewGuid().ToString("N"));
            try
            {
                using var updates=new OfficialUpdates(new Store(folder));updates.Check(true).GetAwaiter().GetResult();
                Assert(!updates.ChannelEnabled && updates.Available==null && !updates.Busy,"Upstream update channel is enabled.");
            }
            finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        });
        test("Russian translations retain every formatting argument",()=>
        {
            foreach(var entry in RussianCatalog.Entries)
            {
                static string Args(string value)=>string.Join(",",Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})").Select(m=>m.Groups[1].Value).Order());
                Assert(Args(entry.Key)==Args(entry.Value),"Format arguments differ: "+entry.Key);
                if(Args(entry.Key).Length>0){CompositeFormat.Parse(entry.Key);CompositeFormat.Parse(entry.Value);}
            }
        });
        test("Russian language is accepted and survives a settings round trip",()=>
        {
            var s=new Settings{UiLanguage="ru"};s.Normalize();
            s=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;s.Normalize();
            Assert(s.UiLanguage=="ru","Russian preference was discarded.");
        });
        test("Russian captions are translated without changing user content",()=>
        {
            string before=Localization.Language;
            try
            {
                Localization.Configure(new Settings{UiLanguage="ru"});
                Assert(Localization.Text("Settings")=="Настройки","Settings caption is not Russian.");
                Assert(Localization.Text("User-defined private title")=="User-defined private title","Unknown content was changed.");
                Assert(UI.Text("Settings").Text=="Settings","User content passed to UI.Text was translated.");
                Assert(UI.CaptionInput("Settings","Settings").Text=="Settings","Editable user content was translated.");
                Assert(UI.Caption("Settings").Text=="Настройки","Owned caption was not translated.");
                Assert(Localization.Format("Selected: {0}","Private model")=="Выбрано: Private model","Format changed an inserted value.");
            }
            finally{Localization.Configure(new Settings{UiLanguage=before});}
        });
        test("Russian catalog covers every base caption and preserves format arguments",()=>
        {
            string before=Localization.Language;
            try
            {
                Localization.Configure(new Settings{UiLanguage="ru"});
                foreach(string key in LocalizationCatalog.English.Keys)
                {
                    string translated=Localization.Text(key);
                    Assert(translated!=key,"Untranslated base caption: "+key);
                    static string Args(string value)=>string.Join(",",Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})").Select(m=>m.Groups[1].Value).Order());
                    Assert(Args(key)==Args(translated),"Format arguments differ: "+key);
                }
            }
            finally{Localization.Configure(new Settings{UiLanguage=before});}
        });
    }
}
