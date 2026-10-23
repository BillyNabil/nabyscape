using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace NabyScape;

internal static class InstallPaths
{
    internal const string RegistryKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NabyScape";
    internal const string LegacyRegistryKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\LumaIsland";
    internal static string Default=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","NabyScape");
    internal static string Legacy=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LumaIsland");
    internal static string Current
    {
        get
        {
            foreach(string name in new[]{RegistryKey,LegacyRegistryKey})
            {
                using var key=Registry.CurrentUser.OpenSubKey(name);
                if(key?.GetValue("InstallLocation") is string path)
                    try{return Validate(path);}catch(IOException){}catch(ArgumentException){}
            }
            return File.Exists(Path.Combine(Legacy,"NabyScape.exe"))?Legacy:Default;
        }
    }
    internal static string Validate(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Choose a local folder with a full drive path.");
        string full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if(!string.Equals(full,Legacy,StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetFileName(full),"NabyScape",StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a dedicated NabyScape folder, such as D:\\Apps\\NabyScape.");
        for(var directory=new DirectoryInfo(full);directory!=null;directory=directory.Parent)
            if(directory.Exists && (directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a folder outside symbolic links or junctions.");
        if(string.Equals(full,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NabyScape"),StringComparison.OrdinalIgnoreCase))throw new IOException("This folder holds your personal data. Choose a different installation folder.");
        return full;
    }
    internal static string FromArguments(string[] args,string fallback)
    {
        int index=Array.IndexOf(args,"--install-dir");
        if(index<0)return Validate(fallback);
        if(index+1>=args.Length || args.Count(a=>a=="--install-dir")!=1)throw new IOException("--install-dir needs one installation folder.");
        return Validate(args[index+1]);
    }
    internal static bool OwnsExecutable(string path)
    {
        string full=Path.GetFullPath(path);
        return new[]{"NabyScape.exe","LumaIsland.exe","BillyNabilaIsland.exe"}.Any(name=>string.Equals(full,Path.Combine(Current,name),StringComparison.OrdinalIgnoreCase));
    }
}
