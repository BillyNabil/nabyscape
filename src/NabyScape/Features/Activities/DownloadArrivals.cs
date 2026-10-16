using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

// Folder arrival inference, not a browser completion API. One bounded worker per enabled folder.
internal sealed class DownloadArrivals : IDisposable
{
    private readonly object gate=new();
    private readonly Dictionary<string,(long Size,DateTime Stamp)> known=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(long Size,DateTimeOffset Stable,int Attempts)> pending=new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string[]> ready;
    private readonly Action<string> failed;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? stop;
    private Task? worker;
    private bool reconcile;
    internal string Folder {get;private set;}="";
    internal bool Running=>watcher!=null;
    internal int Candidates {get{lock(gate)return pending.Count;}}
    internal DownloadArrivals(Action<string[]> ready,Action<string> failed){this.ready=ready;this.failed=failed;}
    internal static bool FinalName(string path)
    {
        string name=Path.GetFileName(path);string ext=Path.GetExtension(name).ToLowerInvariant();
        return !name.StartsWith('.') && !name.StartsWith('~') && ext is not (".tmp" or ".temp" or ".part" or ".partial" or ".crdownload" or ".download" or ".opdownload");
    }
    internal static string KnownFolder()
    {
        var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");IntPtr path=IntPtr.Zero;
        try{Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out path));return Marshal.PtrToStringUni(path)??throw new IOException("Downloads folder unavailable.");}
        finally{if(path!=IntPtr.Zero)Marshal.FreeCoTaskMem(path);}
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    internal void Configure(bool enabled,string folder)
    {
        if(enabled && Running && string.Equals(Folder,folder,StringComparison.OrdinalIgnoreCase))return;
        Stop();if(!enabled)return;
        try
        {
            Folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            // Bound metadata too; overflow fails closed rather than announcing an untracked baseline.
            lock(gate){foreach(var file in Directory.EnumerateFiles(Folder).Take(10001)){if(known.Count==10000)throw new IOException("This folder has too many files to monitor safely (limit 10,000).");var info=new FileInfo(file);known[file]=(info.Length,info.LastWriteTimeUtc);}}
            watcher=new(Folder){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.Size|NotifyFilters.LastWrite,InternalBufferSize=16384};
            watcher.Created+=Changed;watcher.Changed+=Changed;watcher.Deleted+=Deleted;watcher.Renamed+=Renamed;watcher.Error+=Overflow;
            stop=new();watcher.EnableRaisingEvents=true;worker=Task.Run(()=>Work(stop.Token));
            lock(gate)reconcile=true;
        }
        catch(Exception ex){Stop();Failure(ex.Message);}
    }
    private void Candidate(string path)
    {
        if(!FinalName(path))return;
        lock(gate){if(watcher==null)return;if(known.ContainsKey(path) || pending.ContainsKey(path))return;if(pending.Count<128)pending[path]=(-1,DateTimeOffset.UtcNow,0);else reconcile=true;}
    }
    private void Changed(object sender,FileSystemEventArgs e){if(ReferenceEquals(sender,watcher))Candidate(e.FullPath);}
    private void Deleted(object sender,FileSystemEventArgs e){lock(gate){if(!ReferenceEquals(sender,watcher))return;pending.Remove(e.FullPath);known.Remove(e.FullPath);}}
    private void Renamed(object sender,RenamedEventArgs e)
    {
        lock(gate)
        {
            if(!ReferenceEquals(sender,watcher))return;
            pending.Remove(e.OldFullPath);
            // A completed final file renamed by the user is not another arrival.
            if(FinalName(e.OldFullPath) && known.Remove(e.OldFullPath,out var value)){known[e.FullPath]=value;return;}
            known.Remove(e.OldFullPath);
        }
        Candidate(e.FullPath);
    }
    internal void RequestReconcile(){lock(gate){if(watcher!=null)reconcile=true;}}
    private void Overflow(object sender,ErrorEventArgs e){if(ReferenceEquals(sender,watcher))RequestReconcile();}
    private void Failure(string message){try{failed(message);}catch{/* Delivery failures must never recurse or strand the worker. */}}
    private async Task Work(CancellationToken token)
    {
        try
        {
            while(true)
            {
                await Task.Delay(500,token);token.ThrowIfCancellationRequested();
                bool scan;lock(gate){scan=reconcile;reconcile=false;}
                if(scan)
                {
                    var files=Directory.EnumerateFiles(Folder).Take(10001).ToArray();if(files.Length>10000)throw new IOException("Download folder exceeds the 10,000-file monitoring limit.");
                    var present=files.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    lock(gate){foreach(var gone in known.Keys.Where(p=>!present.Contains(p)).ToArray())known.Remove(gone);}
                    foreach(var path in files)Candidate(path);
                }
                var batch=new List<string>();
                lock(gate)
                {
                    foreach(var path in pending.Keys.ToArray())
                    {
                        var item=pending[path];
                        try
                        {
                            var info=new FileInfo(path);if(!info.Exists){pending.Remove(path);continue;}
                            var signature=(info.Length,info.LastWriteTimeUtc);
                            if(known.TryGetValue(path,out var old) && old==signature){pending.Remove(path);continue;}
                            if(item.Attempts>=240){pending.Remove(path);continue;}
                            if(item.Size!=info.Length){pending[path]=(info.Length,DateTimeOffset.UtcNow,item.Attempts+1);continue;}
                            pending[path]=(item.Size,item.Stable,item.Attempts+1);
                            if(DateTimeOffset.UtcNow-item.Stable<TimeSpan.FromSeconds(2))continue;
                            using var readable=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                            if(known.Count>=10000){pending.Remove(path);continue;}
                            known[path]=signature;pending.Remove(path);batch.Add(path);
                        }
                        catch(IOException){if(item.Attempts>=240)pending.Remove(path);else pending[path]=(item.Size,item.Stable,item.Attempts+1);}
                        catch(UnauthorizedAccessException){pending.Remove(path);}
                    }
                }
                token.ThrowIfCancellationRequested();if(batch.Count>0)ready(batch.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray());
                if(!Directory.Exists(Folder))throw new DirectoryNotFoundException("Download folder disconnected. Toggle monitoring off and on to restart.");
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){ReleaseWatcher();lock(gate){pending.Clear();known.Clear();}Failure(ex.Message);}
    }
    private void ReleaseWatcher()
    {
        FileSystemWatcher? current;lock(gate){current=watcher;watcher=null;}
        if(current==null)return;current.EnableRaisingEvents=false;current.Created-=Changed;current.Changed-=Changed;current.Deleted-=Deleted;current.Renamed-=Renamed;current.Error-=Overflow;current.Dispose();
    }
    private void Stop()
    {
        stop?.Cancel();ReleaseWatcher();
        // Worker never dispatches synchronously to UI, so joining cannot deadlock the dispatcher.
        try{worker?.GetAwaiter().GetResult();}catch(OperationCanceledException){}worker=null;stop?.Dispose();stop=null;
        lock(gate){pending.Clear();known.Clear();reconcile=false;}Folder="";
    }
    public void Dispose()=>Stop();
}
