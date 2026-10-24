using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NabyScape;

public sealed partial class Store
{
    private static string ResolveMigration(string legacy,string destination,out string? warning)
    {
        legacy=Path.GetFullPath(legacy);destination=Path.GetFullPath(destination);warning=null;
        if(Directory.Exists(destination))
        {
            if(Directory.Exists(legacy))warning="Both NabyScape and LumaIsland data exist. NabyScape data is in use; review the preserved LumaIsland backup before removing it.";
            return destination;
        }
        if(!Directory.Exists(legacy))return destination;
        string staging=destination+".migration-"+Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(legacy,staging);
            foreach(string file in Directory.EnumerateFiles(staging,"*.json",SearchOption.AllDirectories))
            {using var json=JsonDocument.Parse(File.ReadAllText(file));if(json.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))throw new InvalidDataException("Invalid stored document: "+Path.GetFileName(file));}
            string settings=Path.Combine(staging,"settings.json");
            if(File.Exists(settings))
            {var preferences=JsonSerializer.Deserialize<Settings>(File.ReadAllText(settings))??throw new InvalidDataException("Settings are empty.");preferences.Normalize();}
            ValidateDocument<ProductivityData>(staging,"productivity");ValidateDocument<SessionState>(staging,"session");ValidateDocument<System.Collections.Generic.Dictionary<string,System.Text.Json.JsonElement>>(staging,"activities");ValidateDocument<System.Collections.Generic.List<CalendarEvent>>(staging,"calendar");
            string productivity=Path.Combine(staging,"productivity.json");
            if(File.Exists(productivity))
            {
                var data=JsonNode.Parse(File.ReadAllText(productivity))!;
                if(data["Clips"] is JsonArray clips)foreach(var clip in clips)
                {
                    if(clip?["Kind"]?.GetValue<string>()!="image")continue;
                    string path=clip["ImagePath"]?.GetValue<string>()??"";
                    string cache=Path.GetFullPath(Path.Combine(legacy,"clipboard-images"))+Path.DirectorySeparatorChar;
                    if(!Path.IsPathFullyQualified(path) || !Path.GetFullPath(path).StartsWith(cache,StringComparison.OrdinalIgnoreCase))continue;
                    string relative=Path.GetRelativePath(legacy,path);
                    if(!File.Exists(Path.Combine(staging,relative)))throw new InvalidDataException("A saved clipboard image is missing.");
                    clip["ImagePath"]=Path.Combine(destination,relative);
                }
                File.WriteAllText(productivity,data.ToJsonString(Options));
            }
            // A same-volume directory rename publishes only the fully copied and validated tree.
            Directory.Move(staging,destination);
            warning="Local data moved to NabyScape. The original LumaIsland folder is preserved as a backup.";
            return destination;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            warning="Data migration could not complete; the original LumaIsland data remains in use. Review storage permissions and retry. "+ex.Message;
            return legacy;
        }
    }
    private static void ValidateDocument<T>(string directory,string name)
    {
        string file=Path.Combine(directory,name+".json");
        if(File.Exists(file) && JsonSerializer.Deserialize<T>(File.ReadAllText(file))==null)throw new InvalidDataException(name+" data is empty.");
    }
    private static void CopyDirectory(string source,string destination)
    {
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Data folders cannot be links during migration.");
        Directory.CreateDirectory(destination);
        foreach(string file in Directory.EnumerateFiles(source))
        {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Data files cannot be links during migration.");
            if((File.GetAttributes(file)&FileAttributes.ReadOnly)!=0)throw new UnauthorizedAccessException("A legacy data file is read-only: "+Path.GetFileName(file));
            File.Copy(file,Path.Combine(destination,Path.GetFileName(file)));
        }
        foreach(string directory in Directory.EnumerateDirectories(source))CopyDirectory(directory,Path.Combine(destination,Path.GetFileName(directory)));
    }
}
