using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace NabyScape;

internal static class StartupTrace
{
    internal static void Attach(Window window,string[] args,Action<Exception> log)
    {
        int at=Array.IndexOf(args,"--startup-trace");if(at<0 || at+1>=args.Length)return;
        string path=Path.GetFullPath(args[at+1]);EventHandler? rendered=null;
        rendered=(_,_)=>
        {
            DateTime contentRenderedUtc=DateTime.UtcNow;window.ContentRendered-=rendered;
            try
            {
                using var process=Process.GetCurrentProcess();DateTime processStartUtc=process.StartTime.ToUniversalTime();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,JsonSerializer.Serialize(new{version=typeof(App).Assembly.GetName().Version!.ToString(3),processStartUtc,contentRenderedUtc,contentRenderedMs=(contentRenderedUtc-processStartUtc).TotalMilliseconds,note="First WPF ContentRendered event, not GPU presentation."},new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(Exception ex){log(ex);}
        };
        window.ContentRendered+=rendered;
    }
}
