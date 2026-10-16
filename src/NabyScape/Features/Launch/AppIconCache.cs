using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class AppIconCache
{
    private static readonly Dictionary<string,Task<BitmapSource?>> cache=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> order=new();
    internal static Task<BitmapSource?> GetAsync(Shortcut shortcut)
    {
        string target=Environment.ExpandEnvironmentVariables(shortcut.Target);
        if(shortcut.AppId==null && target.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase))target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
        if(shortcut.AppId==null && target.StartsWith("ms-settings:",StringComparison.OrdinalIgnoreCase))target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"ImmersiveControlPanel","SystemSettings.exe");
        string path=shortcut.AppId is null?target:"shell:AppsFolder\\"+shortcut.AppId;
        if(shortcut.AppId!=null && !WindowsAppCatalog.ValidAppId(shortcut.AppId) || shortcut.AppId==null && !LocalPath(path))return Task.FromResult<BitmapSource?>(null);
        lock(cache)
        {
            if(cache.TryGetValue(path,out var value))return value;
            while(cache.Count>=256)cache.Remove(order.Dequeue());
            var task=ShellWorker.Run(()=>Extract(path));cache[path]=task;order.Enqueue(path);return task;
        }
    }
    private static BitmapSource? Extract(string path)
    {
        IShellItemImageFactory? factory=null;IntPtr bitmap=IntPtr.Zero;
        try
        {
            if(!path.StartsWith("shell:AppsFolder\\",StringComparison.Ordinal))
            {
                if(Path.GetExtension(path).Equals(".url",StringComparison.OrdinalIgnoreCase))return null;
                if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase))path=LocalLinkTarget(path);
                if(!LocalPath(path))return null;
            }
            var iid=typeof(IShellItemImageFactory).GUID;if(SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out factory)!=0 || factory==null)return null;
            if(factory.GetImage(new SizeNative{Width=32,Height=32},0x104, out bitmap)!=0 || bitmap==IntPtr.Zero)return null;
            var source=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();return source;
        }
        catch{return null;}
        finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);ShellWorker.Release(factory);}
    }
    // Never ask the Shell to resolve web URLs, UNC paths or remote shortcut icons.
    private static bool LocalPath(string path)=>Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\") && path.Length>2 && path[1]==':' && DriveInfo(path);
    private static bool DriveInfo(string path)
    {try{return new System.IO.DriveInfo(Path.GetPathRoot(path)!).DriveType is DriveType.Fixed or DriveType.Removable or DriveType.CDRom;}catch{return false;}}
    private static string LocalLinkTarget(string path)
    {
        if(!LocalPath(path) || !Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))return "";
        object? shell=null,link=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link=((dynamic)shell!).CreateShortcut(path);return Convert.ToString(((dynamic)link).TargetPath)??"";
        }
        finally{ShellWorker.Release(link);ShellWorker.Release(shell);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct SizeNative{public int Width,Height;}
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory{[PreserveSig]int GetImage(SizeNative size,uint flags,out IntPtr bitmap);}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]private static extern int SHCreateItemFromParsingName(string path,IntPtr bind,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItemImageFactory? item);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
}
