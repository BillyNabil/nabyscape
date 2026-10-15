using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

public sealed record InstalledApp(string Name,string Target,string? AppId=null)
{
    public string Identity=>AppId is null?Path.GetFullPath(Target):AppId;
    public Shortcut ToShortcut()=>new(){Name=Name,Target=Target,AppId=AppId};
}
internal interface IAppCatalog { Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken); }
internal static class ShellWorker
{
    private static readonly SemaphoreSlim gate=new(1,1);
    internal static async Task<T> Run<T>(Func<T> work)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread=new Thread(()=>{try{completion.SetResult(work());}catch(Exception ex){completion.SetException(ex);}}){IsBackground=true,Name="NabyScape native shell worker"};
            thread.SetApartmentState(ApartmentState.STA);thread.Start();return await completion.Task.ConfigureAwait(false);
        }
        finally{gate.Release();}
    }
    internal static void Release(object? value){if(value!=null && Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
}
internal sealed class WindowsAppCatalog:IAppCatalog
{
    internal static bool ValidAppId(string? id)=>!string.IsNullOrWhiteSpace(id) && id.Length<=256 && id.IndexOf('!')>0 && id.LastIndexOf('!')==id.IndexOf('!') && !id.EndsWith('!') && id.All(c=>char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '!');
    internal static IReadOnlyList<InstalledApp> Normalize(IEnumerable<InstalledApp> apps)=>apps.Where(a=>!string.IsNullOrWhiteSpace(a.Name) && (a.AppId!=null?ValidAppId(a.AppId):Path.IsPathFullyQualified(a.Target) && (Path.GetExtension(a.Target).Equals(".lnk",StringComparison.OrdinalIgnoreCase) || Path.GetExtension(a.Target).Equals(".url",StringComparison.OrdinalIgnoreCase))))
        .GroupBy(a=>a.Identity,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    public Task<IReadOnlyList<InstalledApp>> DiscoverAsync(CancellationToken cancellationToken)=>ShellWorker.Run<IReadOnlyList<InstalledApp>>(()=>
    {
        var found=new List<InstalledApp>();object? shell=null,folder=null,items=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);folder=((dynamic)shell!).NameSpace("shell:AppsFolder");
            if(folder!=null)
            {
                items=((dynamic)folder).Items();int count=((dynamic)items).Count;
                for(int i=0;i<count;i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();object? item=null;
                    try{item=((dynamic)items).Item(i);string id=Convert.ToString(((dynamic)item!).ExtendedProperty("System.AppUserModel.ID"))??"";if(ValidAppId(id))found.Add(new(Convert.ToString(((dynamic)item).Name)??id,"shell:AppsFolder\\"+id,id));}
                    finally{ShellWorker.Release(item);}
                }
            }
        }
        finally{ShellWorker.Release(items);ShellWorker.Release(folder);ShellWorker.Release(shell);}
        foreach(var path in new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)})
            Scan(path,found,cancellationToken);
        return Normalize(found);
    });
    private static void Scan(string directory,List<InstalledApp> found,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Directory.Exists(directory))return;
        try
        {
            foreach(var file in Directory.EnumerateFiles(directory)){token.ThrowIfCancellationRequested();if(Path.GetExtension(file).Equals(".lnk",StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file).Equals(".url",StringComparison.OrdinalIgnoreCase))found.Add(new(Path.GetFileNameWithoutExtension(file),file));}
            foreach(var child in Directory.EnumerateDirectories(directory))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)Scan(child,found,token);
        }
        catch(UnauthorizedAccessException){}catch(DirectoryNotFoundException){}catch(IOException){}
    }
}
