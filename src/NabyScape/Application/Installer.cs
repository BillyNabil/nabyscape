using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NabyScape;

internal static class Installer
{
    internal static string InstallDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LumaIsland");
    internal static string ShortcutPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"NabyScape.lnk");
    private const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\LumaIsland";
    public static void Run(string[] args)
    {
        bool silent=args.Contains("--silent");
        if(silent)
        {
            try {Install();if(args.Contains("--launch"))Launch();}
            catch(Exception ex) {File.WriteAllText(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!,"install-error.txt"),ex.ToString());Environment.ExitCode=1;}
            return;
        }
        var application=new Application();application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Theme.xaml",UriKind.Relative)});
        var window=new Window{Title="Install NabyScape · "+RuntimePlatform.Rid,Width=560,Height=440,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico"));window.SourceInitialized+=(_,_)=>Native.DarkTitleBar(window);
        var panel=new StackPanel{Margin=new(36)};panel.Children.Add(UI.Text("NabyScape",36,UI.Brush("#B4F272")));panel.Children.Add(UI.Text("A little space. More possibilities.",15,UI.Muted));
        panel.Children.Add(new Border{Height=26});panel.Children.Add(UI.Text("Music, focus, and your everyday tools — always close.\n\nInstalls for this Windows user only. Auto-start is off by default.",14));
        var state=UI.Text("Location: "+InstallDirectory,11,UI.Muted);state.Margin=new(0,20,0,12);panel.Children.Add(state);
        var launch=new CheckBox{Content="Open NabyScape after installation",IsChecked=true};panel.Children.Add(launch);
        var button=UI.Button("Install NabyScape",()=>{},true);button.Click+=(_,_)=>
        {
            button.IsEnabled=false;
            try {Install();if(launch.IsChecked==true)Launch();window.Close();}
            catch(Exception ex) {state.Text="Installation failed: "+ex.Message;button.IsEnabled=true;}
        };panel.Children.Add(button);window.Content=panel;application.Run(window);
    }
    private static void Launch()=>Process.Start(new ProcessStartInfo(Path.Combine(InstallDirectory,"NabyScape.exe")){UseShellExecute=true});
    internal static void Install()
    {
        ValidateArchitecture(Environment.ProcessPath??throw new IOException("Installer executable is unavailable."));
        var target=Path.GetFullPath(InstallDirectory);var expected=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LumaIsland"));
        if(!string.Equals(target,expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid installation location.");
        foreach(string processName in new[]{"LumaIsland","BillyNabilaIsland","NabyScape"})
        foreach(var process in Process.GetProcessesByName(processName))
        {
            using(process)
            {
                try {if(new[]{"LumaIsland.exe","BillyNabilaIsland.exe","NabyScape.exe"}.Any(name=>string.Equals(process.MainModule?.FileName,Path.Combine(target,name),StringComparison.OrdinalIgnoreCase)))throw new IOException("Quit NabyScape from the system tray before updating.");}
                catch(System.ComponentModel.Win32Exception) { }
            }
        }
        Directory.CreateDirectory(target);
        string source=Environment.ProcessPath!;string executable=Path.Combine(target,"NabyScape.exe");
        if(!Path.GetFileName(source).StartsWith("NabyScape",StringComparison.OrdinalIgnoreCase))throw new IOException("Use NabyScape-Setup.exe to install the app.");
        if(!string.Equals(source,executable,StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source,executable+".new",true);File.Move(executable+".new",executable,true);
        }
        File.WriteAllText(Path.Combine(target,"Uninstall.ps1"),Uninstaller);
        foreach(var name in new[]{"LICENSE","LUCIDE-LICENSE.txt","NAudio-LICENSE.txt","System.Speech-LICENSE.txt","DOTNET-LICENSE.txt","DOTNET-ThirdPartyNotices.txt"})
        {
            using var license=typeof(Installer).Assembly.GetManifestResourceStream("NabyScape."+name);
            if(license!=null){using var reader=new StreamReader(license);File.WriteAllText(Path.Combine(target,name),reader.ReadToEnd());}
        }
        object? shell=null,shortcut=null;
        try
        {
            var type=Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("The Windows shortcut service is unavailable.");
            shell=Activator.CreateInstance(type);dynamic dynamicShell=shell!;shortcut=dynamicShell.CreateShortcut(ShortcutPath);dynamic link=shortcut;
            link.TargetPath=executable;link.WorkingDirectory=target;link.Description="NabyScape for Windows";link.IconLocation=executable+",0";link.Save();
            foreach(string legacyName in new[]{"Luma Island.lnk","BillyNabila Island.lnk"})
            {
            string legacyShortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),legacyName);
            if(File.Exists(legacyShortcut))
            {
                object old=dynamicShell.CreateShortcut(legacyShortcut);
                try {dynamic previous=old;string oldTarget=Path.GetFullPath((string)previous.TargetPath);if(new[]{"LumaIsland.exe","BillyNabilaIsland.exe","NabyScape.exe"}.Any(name=>string.Equals(oldTarget,Path.Combine(target,name),StringComparison.OrdinalIgnoreCase)))File.Delete(legacyShortcut);}
                finally{if(Marshal.IsComObject(old))Marshal.FinalReleaseComObject(old);}
            }
            }
        }
        finally {if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        using var key=Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName","NabyScape");key.SetValue("DisplayVersion",typeof(App).Assembly.GetName().Version!.ToString(3));key.SetValue("Publisher","NabyScape");key.SetValue("InstallLocation",target);key.SetValue("DisplayIcon",executable);
        key.SetValue("UninstallString","powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(target,"Uninstall.ps1")+"\"");
        key.SetValue("QuietUninstallString","powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(target,"Uninstall.ps1")+"\" -Quiet");
        key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);key.SetValue("EstimatedSize",(int)(new FileInfo(executable).Length/1024),RegistryValueKind.DWord);
        // On update, retain autostart preference while updating a prior executable path to the installed copy.
        using var startup=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);
        if(startup?.GetValue("LumaIsland")!=null || startup?.GetValue("BillyNabilaIsland")!=null || startup?.GetValue("NabyScape")!=null)
        {
            startup!.SetValue("NabyScape","\""+executable+"\" --background");startup.DeleteValue("LumaIsland",false);startup.DeleteValue("BillyNabilaIsland",false);
        }
        foreach(string legacyName in new[]{"LumaIsland.exe","BillyNabilaIsland.exe"})
        {
        string legacyExecutable=Path.Combine(target,legacyName);
        if(File.Exists(legacyExecutable) && !string.Equals(source,legacyExecutable,StringComparison.OrdinalIgnoreCase))File.Delete(legacyExecutable);
        }
    }
    private static void ValidateArchitecture(string source)
    {
        var process=RuntimeInformation.ProcessArchitecture;var system=RuntimeInformation.OSArchitecture;
        if(process is not (Architecture.X64 or Architecture.Arm64) || system is not (Architecture.X64 or Architecture.Arm64) || process==Architecture.Arm64 && system!=Architecture.Arm64)
            throw new PlatformNotSupportedException("Use the NabyScape setup matching your Windows architecture (x64 or ARM64).");
        string name=Path.GetFileName(source);
        foreach(string rid in new[]{"win-x64","win-arm64"})
            if(name.Contains(rid,StringComparison.OrdinalIgnoreCase) && rid!=RuntimePlatform.Rid)throw new IOException("The setup filename does not match its executable architecture. Download the correct official setup.");
        using var stream=File.OpenRead(source);using var reader=new BinaryReader(stream);
        if(stream.Length<64 || reader.ReadUInt16()!=0x5A4D)throw new IOException("Invalid setup executable.");
        stream.Position=0x3C;int offset=reader.ReadInt32();
        if(offset<64 || offset>stream.Length-6)throw new IOException("Invalid setup executable header.");
        stream.Position=offset;
        if(reader.ReadUInt32()!=0x00004550 || reader.ReadUInt16()!=RuntimePlatform.PeMachine)throw new IOException("Setup architecture verification failed. Download the correct official setup.");
    }
    private const string Uninstaller="""
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$islandTarget = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\LumaIsland'))
$scriptFolder = [IO.Path]::GetFullPath($PSScriptRoot)
if ($scriptFolder -ne $islandTarget) { throw 'Invalid uninstaller location.' }
if (-not $Quiet) {
    $answer = [Windows.Forms.MessageBox]::Show('Uninstall NabyScape? Your personal preferences will be kept.', 'NabyScape', 'YesNo', 'Question')
    if ($answer -ne 'Yes') { exit 0 }
}
Get-Process -Name LumaIsland,BillyNabilaIsland,NabyScape -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -and @((Join-Path $islandTarget 'LumaIsland.exe'), (Join-Path $islandTarget 'BillyNabilaIsland.exe'), (Join-Path $islandTarget 'NabyScape.exe')) -contains [IO.Path]::GetFullPath($_.Path)) { Stop-Process -Id $_.Id -Force }
}
Start-Sleep -Milliseconds 700
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($valueName in @('LumaIsland','BillyNabilaIsland','NabyScape')) {
    $startupValue = (Get-ItemProperty -LiteralPath $startupKey -Name $valueName -ErrorAction SilentlyContinue).$valueName
    if ($startupValue -and $startupValue.Contains($islandTarget)) { Remove-ItemProperty -LiteralPath $startupKey -Name $valueName }
}
$shortcutFile = Join-Path ([Environment]::GetFolderPath('Programs')) 'NabyScape.lnk'
if (Test-Path -LiteralPath $shortcutFile) { Remove-Item -LiteralPath $shortcutFile -Force }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LumaIsland'
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Force }
# Only the exact dedicated application directory may be removed. Preferences live elsewhere.
if ($islandTarget -ne [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\LumaIsland'))) { throw 'Invalid removal location.' }
Remove-Item -LiteralPath $islandTarget -Recurse -Force
if (-not $Quiet) { [Windows.Forms.MessageBox]::Show('NabyScape has been uninstalled.','NabyScape') | Out-Null }
""";
}
