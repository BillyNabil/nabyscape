using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NabyScape;

internal sealed class UpdateSnapshot
{
    public string Id {get;set;}="";
    public string Version {get;set;}="";
    public string Hash {get;set;}="";
    public string ProfileDirectory {get;set;}="";
    public DateTimeOffset Created {get;set;}
    public Dictionary<string,string> Profiles {get;set;}=new(StringComparer.OrdinalIgnoreCase);
}

internal static class UpdateRecovery
{
    private static string Root=>Path.Combine(UpdateHandoff.Folder,"rollback");
    private static string Pointer=>Path.Combine(UpdateHandoff.Folder,"previous.json");
    internal static bool ValidId(string id)=>Regex.IsMatch(id,@"^[a-f0-9]{32}$");
    private static string DirectoryFor(string id)
    {
        if(!ValidId(id))throw new IOException("Invalid recovery identity.");
        string directory=Path.Combine(Root,id);
        foreach(string path in new[]{UpdateHandoff.Folder,Root,directory})
            if(Directory.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery folders must not be links.");
        return directory;
    }
    internal static string ProfileDirectory(string path)
    {
        string full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if(!new[]{"NabyScape","LumaIsland"}.Any(name=>string.Equals(full,Path.Combine(local,name),StringComparison.OrdinalIgnoreCase)))throw new IOException("Automatic recovery requires the standard per-user profile folder.");
        if(Directory.Exists(full) && (File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery profile folder must not be a link.");
        return full;
    }
    private static bool ValidName(string name)=>name.Length<=128 && !name.Equals("snapshot.json",StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(name,@"^[A-Za-z0-9][A-Za-z0-9._-]*\.json$");
    private static string Hash(string path)
    {using var file=File.OpenRead(path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));}
    internal static string Capture(string installed,string profile)
    {
        OfficialUpdates.RequireAutomaticInstallPath(installed);
        profile=ProfileDirectory(profile);
        var version=FileVersionInfo.GetVersionInfo(installed);
        var snapshot=new UpdateSnapshot{Id=Guid.NewGuid().ToString("N"),Version=new Version(version.FileMajorPart,version.FileMinorPart,version.FileBuildPart).ToString(),ProfileDirectory=profile,Created=DateTimeOffset.UtcNow};
        string directory=DirectoryFor(snapshot.Id),pointerTemp=Pointer+"."+snapshot.Id+".tmp";
        if(Directory.Exists(directory) || File.Exists(pointerTemp))throw new IOException("Recovery staging already exists.");
        var ownedFiles=new List<string>();bool directoryCreated=false,pointerTempCreated=false;
        try
        {
            Directory.CreateDirectory(directory);directoryCreated=true;
            string executable=Path.Combine(directory,"NabyScape-Previous.exe");ownedFiles.Add(executable);
            File.Copy(installed,executable);snapshot.Hash=Hash(executable);
            long total=0;
            foreach(string file in Directory.EnumerateFiles(profile,"*.json",SearchOption.TopDirectoryOnly))
            {
                string name=Path.GetFileName(file);var info=new FileInfo(file);
                if(!ValidName(name) || (info.Attributes&FileAttributes.ReparsePoint)!=0 || info.Length>32*1024*1024 || snapshot.Profiles.Count>=256 || (total+=info.Length)>128*1024*1024)throw new IOException("The profile cannot be backed up safely. Installation cancelled.");
                string target=Path.Combine(directory,name);ownedFiles.Add(target);File.Copy(file,target);snapshot.Profiles.Add(name,Hash(target));
            }
            string manifest=Path.Combine(directory,"snapshot.json");ownedFiles.Add(manifest);
            File.WriteAllText(manifest,JsonSerializer.Serialize(snapshot));
            // Publish atomically only after validation; failures leave the previous pointer intact.
            Read(snapshot.Id);
            pointerTempCreated=true;File.WriteAllText(pointerTemp,JsonSerializer.Serialize(snapshot.Id));File.Move(pointerTemp,Pointer,true);
            return snapshot.Id;
        }
        catch(Exception failure)
        {
            // At most 258 known snapshot files plus this capture's pointer temp; never recurse.
            bool cleaned=true;
            try
            {
                DirectoryFor(snapshot.Id);
                foreach(string file in ownedFiles)
                    try{File.Delete(file);}catch(Exception){cleaned=false;}
                if(pointerTempCreated)try{File.Delete(pointerTemp);}catch(Exception){cleaned=false;}
                if(directoryCreated)try{Directory.Delete(directory,false);}catch(Exception){cleaned=false;}
            }
            catch(Exception){cleaned=false;}
            if(!cleaned)throw new IOException("Snapshot capture failed and some staging files could not be removed from "+directory+". The previous recovery pointer is unchanged.",failure);
            throw;
        }
    }
    internal static UpdateSnapshot Read(string id,bool verify=true)
    {
        string directory=DirectoryFor(id),manifest=Path.Combine(directory,"snapshot.json");
        if(new FileInfo(manifest).Length>131072)throw new IOException("Invalid recovery manifest.");
        var snapshot=JsonSerializer.Deserialize<UpdateSnapshot>(File.ReadAllText(manifest))??throw new IOException("Missing recovery manifest.");
        if(snapshot.Id!=id || !Version.TryParse(snapshot.Version,out var version) || snapshot.Profiles==null || snapshot.Profiles.Count>256)throw new IOException("Invalid recovery manifest.");
        ProfileDirectory(snapshot.ProfileDirectory);
        if(!verify)return snapshot;
        OfficialUpdates.VerifyFile(Path.Combine(directory,"NabyScape-Previous.exe"),snapshot.Hash);OfficialUpdates.InspectSetup(Path.Combine(directory,"NabyScape-Previous.exe"),version);
        long total=0;
        foreach(var entry in snapshot.Profiles)
        {
            if(!ValidName(entry.Key))throw new IOException("Invalid recovery profile name.");
            string file=Path.Combine(directory,entry.Key);var info=new FileInfo(file);
            if((info.Attributes&FileAttributes.ReparsePoint)!=0 || info.Length>32*1024*1024 || (total+=info.Length)>128*1024*1024)throw new IOException("Invalid recovery profile size.");
            OfficialUpdates.VerifyFile(file,entry.Value);
        }
        return snapshot;
    }
    internal static string? PreviousId()
    {
        try
        {
            if(!File.Exists(Pointer) || new FileInfo(Pointer).Length>128)return null;
            string? id=JsonSerializer.Deserialize<string>(File.ReadAllText(Pointer));return id!=null && ValidId(id)?id:null;
        }
        catch(IOException){return null;}
        catch(JsonException){return null;}
        catch(UnauthorizedAccessException){return null;}
    }
    internal static string Description()
    {
        try{string? id=PreviousId();if(id==null)return "No previous version has been saved by the updater yet.";var s=Read(id,false);return "Previous version: "+s.Version+" · saved "+s.Created.ToLocalTime().ToString("g");}
        catch(Exception){return "The saved recovery copy is unavailable or could not be verified.";}
    }
    internal static void Restore(string id,string installed)
    {
        OfficialUpdates.RequireAutomaticInstallPath(installed);
        var snapshot=Read(id);string directory=DirectoryFor(id);
        // The previous executable uses the same normal installer path, including licenses and uninstall metadata.
        UpdateHandoff.RunInstaller(Path.Combine(directory,"NabyScape-Previous.exe"),recovery:true);
        OfficialUpdates.VerifyFile(installed,snapshot.Hash);
        string profile=ProfileDirectory(snapshot.ProfileDirectory);
        foreach(var entry in snapshot.Profiles)
        {
            string target=Path.Combine(profile,entry.Key),temp=target+".restore-"+Guid.NewGuid().ToString("N");
            try{File.Copy(Path.Combine(directory,entry.Key),temp,false);File.Move(temp,target,true);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        // A schema introduced after this snapshot must not label restored legacy data as migrated.
        // Remove only the sidecar for a payload actually restored, never a new user document.
        foreach(string name in snapshot.Profiles.Keys.Where(name=>!name.EndsWith(".schema.json",StringComparison.OrdinalIgnoreCase)))
        {
            string metadata=Path.GetFileNameWithoutExtension(name)+".schema.json";
            if(snapshot.Profiles.Keys.Any(saved=>string.Equals(saved,metadata,StringComparison.OrdinalIgnoreCase)))continue;
            string path=Path.Combine(profile,metadata);if(!File.Exists(path))continue;
            var info=new FileInfo(path);
            if((info.Attributes&FileAttributes.ReparsePoint)!=0 || info.Length>4096)throw new IOException("Recovery could not safely reset schema metadata.");
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            if(document.RootElement.ValueKind!=JsonValueKind.Object || !document.RootElement.TryGetProperty("SchemaVersion",out var schema) || !schema.TryGetInt32(out _)
                || document.RootElement.TryGetProperty("PendingSchemaVersion",out var pending) && pending.ValueKind!=JsonValueKind.Null && !pending.TryGetInt32(out _)
                || document.RootElement.EnumerateObject().Any(property=>property.Name is not ("SchemaVersion" or "PendingSchemaVersion")))throw new IOException("Recovery schema metadata is invalid; the recovery copy is preserved.");
            File.Delete(path);
        }
    }
    internal static void Prune(string keep)
    {
        // No recursive deletes: remove only this updater's fixed, verified snapshot files.
        if(!Directory.Exists(Root))return;
        foreach(string directory in Directory.EnumerateDirectories(Root))
        {
            string id=Path.GetFileName(directory);if(id==keep || !ValidId(id))continue;
            try
            {
                var old=Read(id);foreach(string name in old.Profiles.Keys)File.Delete(Path.Combine(directory,name));
                File.Delete(Path.Combine(directory,"NabyScape-Previous.exe"));File.Delete(Path.Combine(directory,"snapshot.json"));Directory.Delete(directory,false);
            }
            catch(Exception){ /* A backup that cannot be verified is preserved for manual inspection. */ }
        }
    }
}
