using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed partial class AssistantSpeechInstall
{
    private bool removing;
    // Resolve catalog IDs only. Imported SourcePath files and shared engines are
    // never deletion targets; include the matching download cache to reclaim disk.
    private string[] RemovalPaths(string id,bool tts)
    {
        string[] targets;
        if(tts)
        {
            var model=SpeechCatalog.TtsById(id);
            if(model.Id=="windows" || model.SourcePath.Length>0)throw new InvalidOperationException("Windows voices and imported model files cannot be uninstalled here.");
            if(model.Package!=null)
            {
                string folder=VoiceRoot(model);targets=new[]{folder,Path.Combine(root,"downloads",Path.GetFileName(folder)+(model.Package.Url.EndsWith(".tar.bz2",StringComparison.OrdinalIgnoreCase)?".tar.bz2":".zip"))};
            }
            else {string file=TtsPath(model);targets=new[]{file,file+".json",file+".MODEL_CARD"};}
        }
        else
        {
            var model=SpeechCatalog.AsrById(id);
            if(model.SourcePath.Length>0)throw new InvalidOperationException("Imported model files cannot be uninstalled here.");
            targets=!model.Native?new[]{AsrPath(model)}:new[]{Path.Combine(root,"models",model.Id),Path.Combine(root,"downloads",model.Id)+(model.Files==null?(model.Asset.Url.EndsWith(".tar.bz2",StringComparison.OrdinalIgnoreCase)?".tar.bz2":".zip"):"")};
        }
        return targets.SelectMany(p=>new[]{p,p+".partial"}).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal bool HasManagedModel(string id,bool tts)
    {
        if(tts && id=="windows")return false;
        return RemovalPaths(id,tts).Any(p=>File.Exists(p) || Directory.Exists(p));
    }
    // Called on demand by the storage page, never by the live voice refresh loop.
    // Link validation is identical to uninstall: sizes never traverse external links.
    internal long ManagedModelBytes(string id,bool tts)
    {
        var paths=RemovalPaths(id,tts);long bytes=0;
        foreach(string path in paths)ValidateRemoval(path);
        foreach(string path in paths)
        {
            if(File.Exists(path))bytes=checked(bytes+new FileInfo(path).Length);
            else if(Directory.Exists(path))foreach(string file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories))bytes=checked(bytes+new FileInfo(file).Length);
        }
        return bytes;
    }
    internal static string StorageSize(long bytes)=>bytes<1024?bytes+" B":bytes<1024L*1024?(bytes/1024d).ToString("0.0")+" KB":bytes<1024L*1024*1024?(bytes/(1024d*1024)).ToString("0.0")+" MB":(bytes/(1024d*1024*1024)).ToString("0.00")+" GB";
    internal bool ModelInUse(AssistantLocalVoiceOptions options,string id,bool tts)
    {
        if(!options.Enabled || !options.Managed || tts && options.OwnModels && options.OwnTtsEngine=="Windows voice")return false;
        var paths=RemovalPaths(id,tts);
        bool Contains(string path)=>paths.Any(target=>string.Equals(Path.GetFullPath(path),target,StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        try
        {
            if(options.OwnModels && (tts?options.OwnTtsEngine:options.OwnAsrEngine)!="Installed pack")
            {string own=tts?options.OwnTtsPath:options.OwnAsrPath;return !string.IsNullOrWhiteSpace(own) && Contains(AssistantOwnSpeech.LocalPath(own));}
            if(tts){var model=SpeechCatalog.TtsById(options.ManagedTts);return model.Id!="windows" && Contains(model.Package!=null?VoiceRoot(model):TtsPath(model));}
            var asr=SpeechCatalog.AsrById(options.ManagedAsr);return Contains(asr.Native?Path.Combine(root,"models",asr.Id):AsrPath(asr));
        }
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(IOException){return false;}
    }
    private void ValidateRemoval(string path)
    {
        string boundary=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),target=Path.GetFullPath(path);
        if(!new[]{"models","voices","downloads"}.Any(area=>target.StartsWith(Path.Combine(boundary,area)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new IOException("The model path is outside managed speech storage.");
        // Check ancestors and every descendant before any recursive deletion. A
        // directory junction must never turn model removal into external deletion.
        for(string? current=target;current!=null;current=Path.GetDirectoryName(current))
        {
            CheckLink(current);if(string.Equals(current,boundary,StringComparison.OrdinalIgnoreCase))break;
        }
        var pending=new Stack<string>();if(Directory.Exists(target))pending.Push(target);
        while(pending.Count>0)foreach(string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
        {CheckLink(entry);if(Directory.Exists(entry))pending.Push(entry);}
    }
    private static void CheckLink(string path)
    {
        try{if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("A link was found in this model's storage. Remove the link manually before uninstalling.");}
        catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
    }
    internal async Task<bool> UninstallModel(string id,bool tts,Func<Task>? releaseVoice=null)
    {
        if(Busy || disposed)return false;
        var paths=RemovalPaths(id,tts);string name=tts?SpeechCatalog.TtsById(id).Name:SpeechCatalog.AsrById(id).Name;
        installation=new();removing=true;
        try
        {
            Report("Uninstalling "+name+"…");
            // Preflight before stopping voice or modifying preferences.
            await Task.Run(()=>{foreach(string path in paths)ValidateRemoval(path);});
            if(releaseVoice!=null)await releaseVoice();
            await Task.Run(()=>
            {
                foreach(string path in paths)ValidateRemoval(path);
                foreach(string path in paths){if(Directory.Exists(path))Directory.Delete(path,true);else if(File.Exists(path))File.Delete(path);}
            });
            Report(name+" uninstalled. You can install it again at any time.",100);return true;
        }
        catch(Exception ex){Report("Could not finish uninstalling "+name+". "+ex.Message+" Check the installed models list and retry.");return false;}
        finally{removing=false;installation.Dispose();installation=null;if(!disposed)Changed?.Invoke();}
    }
}
