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
    internal static string InstallDirectory=>InstallPaths.Current;
    internal static string ShortcutPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"NabyScape.lnk");
    private const string UninstallKey=InstallPaths.RegistryKey;
    public static void Run(string[] args)
    {
        bool silent=args.Contains("--silent");
        if(silent)
        {
            try {string target=InstallPaths.FromArguments(args,InstallDirectory);Install(target);if(args.Contains("--launch"))Launch(target);}
            catch(Exception ex) {File.WriteAllText(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!,"install-error.txt"),ex.ToString());Environment.ExitCode=1;}
            return;
        }
        var application=new Application();application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Theme.xaml",UriKind.Relative)});
        application.Run(CreateWindow(args));
    }
    internal static Window CreateWindow(string[] args)
    {
        var window=new Window{Title="Install NabyScape · "+RuntimePlatform.Rid,Width=620,Height=535,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico"));window.SourceInitialized+=(_,_)=>Native.DarkTitleBar(window);
        var panel=new StackPanel{Margin=new(36)};panel.Children.Add(UI.Text("NabyScape",36,UI.Brush("#B4F272")));panel.Children.Add(UI.Text("A little space. More possibilities.",15,UI.Muted));
        panel.Children.Add(new Border{Height=26});panel.Children.Add(UI.Text("Music, focus, and your everyday tools — always close.\n\nInstalls for this Windows user only. Choose your folder and startup preference below.",14));
        var folderLabel=UI.Text("Install folder",12,UI.Muted);folderLabel.Margin=new(0,20,0,6);panel.Children.Add(folderLabel);
        string suggested=string.Equals(InstallDirectory,InstallPaths.Legacy,StringComparison.OrdinalIgnoreCase)?InstallPaths.Default:InstallDirectory;
        var folder=new TextBox{Text=InstallPaths.FromArguments(args,suggested),MinHeight=36,VerticalContentAlignment=VerticalAlignment.Center};UI.Named(folder,"Installation folder");
        var row=new DockPanel{Margin=new(0,0,0,6)};
        var browse=UI.Button("Browse…",()=>
        {
            var picker=new OpenFolderDialog{Title="Choose where to install NabyScape"};
            if(picker.ShowDialog(window)==true)folder.Text=string.Equals(Path.GetFileName(picker.FolderName),"NabyScape",StringComparison.OrdinalIgnoreCase)?picker.FolderName:Path.Combine(picker.FolderName,"NabyScape");
        });DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);row.Children.Add(folder);panel.Children.Add(row);
        var state=UI.Text("Choose a location you can write to. Your personal settings are kept separately.",11,UI.Muted);state.Margin=new(0,0,0,12);panel.Children.Add(state);
        var startupChoice=new CheckBox{Content="Start NabyScape with Windows",IsChecked=HasSavedPreferences?StartupService.Enabled():FactoryDefaults.Create().AutoStart,Margin=new(0,0,0,8)};panel.Children.Add(startupChoice);
        var launch=new CheckBox{Content="Open NabyScape after installation",IsChecked=true};panel.Children.Add(launch);
        var button=UI.Button("Install NabyScape",()=>{},true);button.Click+=(_,_)=>
        {
            button.IsEnabled=false;
            try {string target=InstallPaths.Validate(folder.Text.Trim());Install(target,startupChoice.IsChecked==true);if(launch.IsChecked==true)Launch(target);window.Close();}
            catch(Exception ex) {state.Text="Installation failed: "+ex.Message;button.IsEnabled=true;}
        };panel.Children.Add(button);window.Content=panel;return window;
    }
    private static void Launch(string folder)=>Process.Start(new ProcessStartInfo(Path.Combine(folder,"NabyScape.exe")){UseShellExecute=true});
    internal static void Install(string? directory=null,bool? startWithWindows=null)
    {
        ValidateArchitecture(Environment.ProcessPath??throw new IOException("Installer executable is unavailable."));
        string previous=InstallDirectory;string target=InstallPaths.Validate(directory??previous);
        foreach(string processName in new[]{"LumaIsland","BillyNabilaIsland","NabyScape"})
        foreach(var process in Process.GetProcessesByName(processName))
        {
            using(process)
            {
                try {if(process.Id!=Environment.ProcessId && new[]{target,previous}.Any(folder=>new[]{"LumaIsland.exe","BillyNabilaIsland.exe","NabyScape.exe"}.Any(name=>string.Equals(process.MainModule?.FileName,Path.Combine(folder,name),StringComparison.OrdinalIgnoreCase))))throw new IOException("Quit NabyScape from the system tray before updating.");}
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
            object uninstallLink=dynamicShell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Uninstall NabyScape.lnk"));
            try{dynamic uninstall=uninstallLink;uninstall.TargetPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");uninstall.Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(target,"Uninstall.ps1")+"\"";uninstall.WorkingDirectory=target;uninstall.Description="Remove NabyScape and optionally all downloaded speech models and personal data";uninstall.IconLocation=executable+",0";uninstall.Save();}
            finally{if(Marshal.IsComObject(uninstallLink))Marshal.FinalReleaseComObject(uninstallLink);}
            foreach(string legacyName in new[]{"Luma Island.lnk","BillyNabila Island.lnk"})
            {
            string legacyShortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),legacyName);
            if(File.Exists(legacyShortcut))
            {
                object old=dynamicShell.CreateShortcut(legacyShortcut);
                try {dynamic previousLink=old;string oldTarget=Path.GetFullPath((string)previousLink.TargetPath);if(new[]{"LumaIsland.exe","BillyNabilaIsland.exe","NabyScape.exe"}.Any(name=>string.Equals(oldTarget,Path.Combine(previous,name),StringComparison.OrdinalIgnoreCase)))File.Delete(legacyShortcut);}
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
        using var startup=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        bool startupEnabled=startWithWindows??(StartupService.Enabled() || !HasSavedPreferences && FactoryDefaults.Create().AutoStart);
        if(startupEnabled)
        {
            startup.SetValue("NabyScape","\""+executable+"\" --background");
        }
        else startup.DeleteValue("NabyScape",false);
        startup.DeleteValue("LumaIsland",false);startup.DeleteValue("BillyNabilaIsland",false);
        bool removeLegacyKey;
        using(var legacyKey=Registry.CurrentUser.OpenSubKey(InstallPaths.LegacyRegistryKey))
            removeLegacyKey=legacyKey?.GetValue("InstallLocation") is string oldLocation && string.Equals(oldLocation,previous,StringComparison.OrdinalIgnoreCase);
        if(removeLegacyKey)Registry.CurrentUser.DeleteSubKeyTree(InstallPaths.LegacyRegistryKey,false);
        if(!string.Equals(previous,target,StringComparison.OrdinalIgnoreCase))RemovePreviousFiles(previous,source);
        foreach(string legacyName in new[]{"LumaIsland.exe","BillyNabilaIsland.exe"})
        {
        string legacyExecutable=Path.Combine(target,legacyName);
        if(File.Exists(legacyExecutable) && !string.Equals(source,legacyExecutable,StringComparison.OrdinalIgnoreCase))File.Delete(legacyExecutable);
        }
    }
    private static void RemovePreviousFiles(string folder,string source)
    {
        // Moving an installation removes only shipped files, never unrelated files.
        foreach(string name in new[]{"NabyScape.exe","LumaIsland.exe","BillyNabilaIsland.exe","Uninstall.ps1","LICENSE","LUCIDE-LICENSE.txt","NAudio-LICENSE.txt","System.Speech-LICENSE.txt","DOTNET-LICENSE.txt","DOTNET-ThirdPartyNotices.txt"})
        {
            string path=Path.Combine(folder,name);if(string.Equals(path,source,StringComparison.OrdinalIgnoreCase))continue;
            try{if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        try{if(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){}
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
    internal static string Uninstaller
    {
        get
        {
            using var stream=typeof(Installer).Assembly.GetManifestResourceStream("NabyScape.Uninstall.ps1")??throw new IOException("The bundled uninstaller is missing.");
            using var reader=new StreamReader(stream);return reader.ReadToEnd();
        }
    }
    internal static bool HasSavedPreferences=>new[]{"NabyScape","LumaIsland"}.Any(name=>File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),name,"settings.json")));
    internal static void OpenUninstaller()
    {
        if(!OfficialUpdates.IsInstalled(Environment.ProcessPath!))throw new IOException("This is a portable copy. Remove its folder, or use Windows Installed apps for the installed copy.");
        string script=Path.Combine(InstallDirectory,"Uninstall.ps1");
        InstallPaths.Validate(InstallDirectory);
        if(!File.Exists(script) || (File.GetAttributes(script)&FileAttributes.ReparsePoint)!=0)throw new IOException("Run the latest installer to repair the uninstall entry.");
        _=Process.Start(new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"-NoProfile","-ExecutionPolicy","Bypass","-File",script}})??throw new IOException("Could not open the uninstaller.");
    }
}
