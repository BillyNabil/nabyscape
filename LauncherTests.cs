using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal sealed class FakeAppCatalog:IAppCatalog
{
    private readonly InstalledApp[] apps;
    internal FakeAppCatalog(params InstalledApp[] apps){this.apps=apps;}
    public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken token)
    {token.ThrowIfCancellationRequested();return Task.FromResult(WindowsAppCatalog.Normalize(apps));}
}
internal static class LauncherTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Check(bool value){if(!value)throw new Exception("Launcher assertion failed.");}
        test("Local shortcut icons resolve an isolated native shortcut and cache a frozen bitmap",()=>
        {
            string directory=Path.Combine(Path.GetTempPath(),"NabyScape-icon-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"Explorer.lnk");
            try
            {
                ShellWorker.Run(()=>
                {
                    object? shell=null,link=null;
                    try
                    {
                        shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);link=((dynamic)shell!).CreateShortcut(path);
                        ((dynamic)link).TargetPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
                        ((dynamic)link).Save();return true;
                    }
                    finally{ShellWorker.Release(link);ShellWorker.Release(shell);}
                }).GetAwaiter().GetResult();
                var shortcut=new Shortcut{Name="Isolated Explorer",Target=path};var first=AppIconCache.GetAsync(shortcut);var second=AppIconCache.GetAsync(shortcut);
                var icon=first.GetAwaiter().GetResult();Check(ReferenceEquals(first,second) && icon!=null && icon.IsFrozen && icon.PixelWidth>0 && icon.PixelHeight>0);
                Check(File.Exists(path));
            }
            finally{Directory.Delete(directory,true);}
        });
        test("Icon lookup rejects web and network paths and leaves missing local shortcuts absent",()=>
        {
            string missing=Path.Combine(Path.GetTempPath(),"NabyScape-missing-"+Guid.NewGuid().ToString("N")+".lnk");
            foreach(string path in new[]{"https://example.com/index.html","file://server/share/app.exe",@"\\server\share\app.lnk",Path.ChangeExtension(missing,".url"),missing})
                Check(AppIconCache.GetAsync(new Shortcut{Name="Rejected",Target=path}).GetAwaiter().GetResult()==null);
            Check(!File.Exists(missing));
        });
        test("Installed catalog deduplicates identities and excludes invalid apps without launches",()=>
        {
            IAppCatalog catalog=new FakeAppCatalog(new("Mail","shell:AppsFolder\\Vendor.Mail_abc!App","Vendor.Mail_abc!App"),new("Duplicate","ignored","vendor.mail_ABC!App"),new("Bad","ignored","bad! invalid"),new("Local",@"C:\Apps\Local.lnk"),new("Website",@"C:\Apps\Website.url"),new("Relative","local.lnk"));
            var apps=catalog.DiscoverAsync(CancellationToken.None).GetAwaiter().GetResult();Check(apps.Count==3 && apps.Count(a=>a.AppId!=null)==1 && apps.Any(a=>a.Name=="Website"));
            Check(AppPicker.Search(apps,"  MAIL ").Length==1 && AppPicker.Search(apps,"missing").Length==0);
        });
        test("Installed launch uses a validated single structured explorer argument",()=>
        {
            var info=ShortcutService.InstalledAppStartInfo("Vendor.App_abc!App");Check(!info.UseShellExecute && info.ArgumentList.Count==1 && info.ArgumentList[0]=="shell:AppsFolder\\Vendor.App_abc!App" && info.Arguments=="");
            foreach(var bad in new[]{"", "!", "Vendor!", "!App", "Vendor!!App", "Vendor!App extra", "Vendor!App\"", "Vendor!App;extra"}){bool rejected=false;try{ShortcutService.InstalledAppStartInfo(bad);}catch(ArgumentException){rejected=true;}Check(rejected);}
        });
        test("Installed app identity persists and old Name Target shortcuts remain compatible",()=>
        {
            var old=JsonSerializer.Deserialize<Shortcut>("{\"Name\":\"Browser\",\"Target\":\"https://example.com\"}")!;Check(old.AppId==null && old.Name=="Browser" && old.Target=="https://example.com");
            var shortcut=new InstalledApp("Mail","shell:AppsFolder\\Vendor.Mail_abc!App","Vendor.Mail_abc!App").ToShortcut();var read=JsonSerializer.Deserialize<Shortcut>(JsonSerializer.Serialize(shortcut))!;Check(read.AppId==shortcut.AppId && read.Target==shortcut.Target);
        });
        test("Category and launch label modes remain independent",()=>
        {
            var settings=new Settings{CategoryStyle="Icons",LaunchStyle="Text"};Check(!LauncherPresentation.ShowLabel(settings.CategoryStyle) && LauncherPresentation.ShowIcon(settings.CategoryStyle) && LauncherPresentation.ShowLabel(settings.LaunchStyle) && !LauncherPresentation.ShowIcon(settings.LaunchStyle));
            Check(LauncherPresentation.ShowLabel("IconsLabels") && LauncherPresentation.ShowIcon("IconsLabels"));
        });
        test("Semantic compact Timer and Focus headings toggle at size eleven without margins",()=>
        {
            var panel=new StackPanel();var timer=UI.Heading("Timer",11,UI.Muted);var focus=UI.Heading("Focus",11,UI.Muted);panel.Children.Add(timer);panel.Children.Add(focus);
            LauncherPresentation.ApplyHeadingTypography(panel,true);Check(timer.FontWeight==FontWeights.SemiBold && focus.FontWeight==FontWeights.SemiBold);
            LauncherPresentation.ApplyHeadingTypography(panel,false);Check(timer.FontWeight==FontWeights.Normal && focus.FontWeight==FontWeights.Normal);
        });
        test("Semantic monitor heading ignores altered section margin",()=>
        {
            var monitor=new StackPanel();var memory=UI.Label("MEMORY");memory.Margin=new(0,15,0,7);monitor.Children.Add(memory);
            LauncherPresentation.ApplyHeadingTypography(monitor,true);Check(memory.FontWeight==FontWeights.SemiBold);
            LauncherPresentation.ApplyHeadingTypography(monitor,false);Check(memory.FontWeight==FontWeights.Normal);
        });
        test("Classic card factory marks headings inside nested logical content",()=>
        {
            var card=UI.Card("Now playing",UI.Text("Track",20));var heading=(TextBlock)((StackPanel)card.Child).Children[0];
            LauncherPresentation.ApplyHeadingTypography(card,true);Check(UI.IsHeading(heading) && heading.FontWeight==FontWeights.SemiBold);
            LauncherPresentation.ApplyHeadingTypography(card,false);Check(heading.FontWeight==FontWeights.Normal);
        });
        test("Calendar heading in button content is styled before a window is shown",()=>
        {
            var heading=UI.Heading("October 2026",17);var button=new Button{Content=UI.Row(heading)};
            LauncherPresentation.ApplyHeadingTypography(button,true);Check(heading.FontWeight==FontWeights.SemiBold);
            LauncherPresentation.ApplyHeadingTypography(button,false);Check(heading.FontWeight==FontWeights.Normal);
        });
        test("Rebuilt tool and asynchronous monitor headings accept current settings",()=>
        {
            var panel=new StackPanel();panel.Children.Add(UI.Heading("Notes",19));LauncherPresentation.ApplyHeadingTypography(panel,true);
            panel.Children.Clear();var replacement=UI.Label("NETWORK");replacement.Margin=new(0,15,0,7);panel.Children.Add(replacement);
            LauncherPresentation.ApplyHeadingTypography(panel,true);Check(replacement.FontWeight==FontWeights.SemiBold);
            LauncherPresentation.ApplyHeadingTypography(panel,false);Check(replacement.FontWeight==FontWeights.Normal);
        });
        test("Typography leaves clocks countdowns artists media and content text unclassified",()=>
        {
            var panel=new StackPanel();var texts=new[]{UI.Text("12:00",32),UI.Text("25:00",29),UI.Text("Track",20),UI.Text("Artist",18),UI.Text("Event title",16),UI.Text("Body",11)};texts[5].Margin=new(0,0,0,10);
            foreach(var text in texts)panel.Children.Add(text);LauncherPresentation.ApplyHeadingTypography(panel,true);Check(texts.All(t=>t.FontWeight==FontWeights.Normal && !UI.IsHeading(t)));
            LauncherPresentation.ApplyHeadingTypography(panel,false);Check(texts.All(t=>t.FontWeight==FontWeights.Normal));
        });
        test("Current and outgoing media titles remain independent of opposite heading settings",()=>
        {
            foreach(bool mediaBold in new[]{false,true})
            {
                var panel=new StackPanel();var heading=UI.Heading("Now playing",11);panel.Children.Add(heading);
                foreach(int size in new[]{20,15})
                {
                    var title=UI.Text("New track",size);var outgoing=UI.Text("Previous track",size);var artist=UI.Text("Artist",18);var oldArtist=UI.Text("Previous artist",18);
                    panel.Children.Add(title);panel.Children.Add(outgoing);panel.Children.Add(artist);panel.Children.Add(oldArtist);
                    LauncherPresentation.ApplyMediaTypography(title,outgoing,mediaBold);LauncherPresentation.ApplyMediaTypography(artist,oldArtist,false);
                    LauncherPresentation.ApplyHeadingTypography(panel,!mediaBold);
                    Check(title.FontWeight==(mediaBold?FontWeights.SemiBold:FontWeights.Normal) && outgoing.FontWeight==title.FontWeight && artist.FontWeight==FontWeights.Normal && oldArtist.FontWeight==FontWeights.Normal);
                }
                Check(heading.FontWeight==(mediaBold?FontWeights.Normal:FontWeights.SemiBold));
            }
        });
        test("Fake installed app discovery honors cancellation",()=>
        {using var token=new CancellationTokenSource();token.Cancel();bool cancelled=false;try{new FakeAppCatalog().DiscoverAsync(token.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled);});
    }
}
