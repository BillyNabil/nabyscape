using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace NabyScape;

internal enum StoreDocumentState { Missing, Legacy, Current, RecoveryRequired, FutureSchema }
internal sealed record StoreHealth(int ObservedDocuments, int LegacyDocuments, int CurrentDocuments,
    int RecoveryRequiredDocuments, int FutureSchemaDocuments, bool HasStorageWarning);

internal sealed class StoreDocumentException : IOException
{
    internal DiagnosticCode Code { get; }
    internal StoreDocumentException(DiagnosticCode code) : base("The stored document requires recovery before it can be changed.") => Code=code;
}

public sealed partial class Store
{
    internal const int CurrentSchemaVersion=1;
    internal const string SchemaSidecarSuffix=".schema.json";
    private const int MaxDocumentBytes=32*1024*1024;
    private static readonly object DocumentGate=new();
    private readonly Dictionary<string,StoreDocumentState> documentStates=new(StringComparer.OrdinalIgnoreCase);
    // Only a successful Load can replace fallback state with a trustworthy document (or confirmed absence).
    // Save failures do not set this latch: callers may safely retry a pre-commit failure with their own values.
    private readonly HashSet<string> fallbackLoads=new(StringComparer.OrdinalIgnoreCase);

    internal StoreHealth GetHealth()
    {
        lock(DocumentGate)return new(documentStates.Count,
            documentStates.Values.Count(s=>s==StoreDocumentState.Legacy),
            documentStates.Values.Count(s=>s==StoreDocumentState.Current),
            documentStates.Values.Count(s=>s==StoreDocumentState.RecoveryRequired),
            documentStates.Values.Count(s=>s==StoreDocumentState.FutureSchema),Warning!=null);
    }

    private string DocumentPath(string name)
    {
        // Internal identifiers only; reject paths, extensions, Windows device names and ADS syntax.
        if(string.IsNullOrEmpty(name) || name.Length>64 || !char.IsAsciiLetterOrDigit(name[0]) ||
            name.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_') ||
            new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(name,StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("A safe internal document identifier is required.",nameof(name));
        return Path.Combine(DirectoryPath,name+".json");
    }

    private static bool Exists(string file)
    {
        try
        {
            var attributes=File.GetAttributes(file);
            if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)
                throw new StoreDocumentException(DiagnosticCode.InvalidDocument);
            return true;
        }
        catch(FileNotFoundException){return false;}
        catch(DirectoryNotFoundException){return false;}
    }

    private static byte[] ReadBounded(string file,int limit=MaxDocumentBytes)
    {
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>limit)throw new StoreDocumentException(DiagnosticCode.LimitExceeded);
        using var bytes=new MemoryStream();
        var buffer=new byte[8192];int read;
        while((read=stream.Read(buffer,0,buffer.Length))>0)
        {
            if(bytes.Length+read>limit)throw new StoreDocumentException(DiagnosticCode.LimitExceeded);
            bytes.Write(buffer,0,read);
        }
        return bytes.ToArray();
    }

    private static JsonNode ParsePayload(byte[] bytes)
    {
        // Accept the UTF-8 BOM used by older tools, but never replace invalid UTF-8 silently.
        int offset=bytes.Length>=3 && bytes[0]==0xef && bytes[1]==0xbb && bytes[2]==0xbf?3:0;
        var node=JsonNode.Parse(new UTF8Encoding(false,true).GetString(bytes,offset,bytes.Length-offset));
        if(node is not (JsonObject or JsonArray))throw new StoreDocumentException(DiagnosticCode.InvalidDocument);
        return node;
    }

    private static (int Version,JsonObject Metadata) ReadSchema(string file)
    {
        if(!Exists(file))return (0,new JsonObject { ["SchemaVersion"]=0 });
        if(ParsePayload(ReadBounded(file,64*1024)) is not JsonObject metadata ||
            metadata["SchemaVersion"] is not JsonValue versionValue || !versionValue.TryGetValue<int>(out int version) || version<0)
            throw new StoreDocumentException(DiagnosticCode.InvalidDocument);
        int pending=version;
        if(metadata.TryGetPropertyValue("PendingSchemaVersion",out var pendingNode) &&
            (pendingNode is not JsonValue pendingValue || !pendingValue.TryGetValue<int>(out pending) || pending<version))
            throw new StoreDocumentException(DiagnosticCode.InvalidDocument);
        // Check even when the payload is absent or Save is called without a preceding Load.
        if(version>CurrentSchemaVersion || pending>CurrentSchemaVersion)
            throw new StoreDocumentException(DiagnosticCode.UnsupportedSchema);
        return (version,metadata);
    }

    private static JsonNode MigratePayload<T>(JsonNode source,int version)
    {
        var migrated=source.DeepClone();
        // Explicit schema 0 -> 1 migration. Idempotent if interrupted before the final sidecar commit.
        // Preserve the classic layout that predates configurable panel dimensions; other document shapes are unchanged.
        if(version==0 && typeof(T)==typeof(Settings) && migrated is JsonObject settings &&
            settings[nameof(Settings.CompactLayout)] is JsonValue compact && compact.TryGetValue<bool>(out bool enabled) && !enabled)
        {
            if(!settings.ContainsKey(nameof(Settings.PanelWidth)))settings[nameof(Settings.PanelWidth)]=452;
            if(!settings.ContainsKey(nameof(Settings.PanelMaxHeight)))settings[nameof(Settings.PanelMaxHeight)]=634;
        }
        return migrated;
    }

    private static T DeserializePayload<T>(JsonNode payload)
    {
        var value=payload.Deserialize<T>(Options);
        if(value is null)throw new StoreDocumentException(DiagnosticCode.InvalidDocument);
        return value;
    }

    private T LoadDocument<T>(string name) where T:new()
    {
        string file=DocumentPath(name),schema=Path.Combine(DirectoryPath,name+SchemaSidecarSuffix);
        lock(DocumentGate)
        {
            try
            {
                for(int attempt=0;attempt<3;attempt++)
                {
                    var metadata=ReadSchema(schema);
                    if(!Exists(file))
                    {
                        var fresh=Fresh<T>();
                        documentStates[name]=StoreDocumentState.Missing;
                        fallbackLoads.Remove(name);
                        return fresh;
                    }
                    var bytes=ReadBounded(file);
                    var after=ReadSchema(schema);
                    // An external writer may publish a version barrier while we read. Do not use a
                    // new payload with stale metadata (or miss a future pending-schema barrier).
                    if(!JsonNode.DeepEquals(metadata.Metadata,after.Metadata))continue;
                    var payload=MigratePayload<T>(ParsePayload(bytes),metadata.Version);
                    var value=DeserializePayload<T>(payload);
                    documentStates[name]=metadata.Version==0?StoreDocumentState.Legacy:StoreDocumentState.Current;
                    fallbackLoads.Remove(name);
                    // Loading never rewrites data. Migration is committed by the next explicit normal Save.
                    return value;
                }
                throw new IOException("The stored document is busy.");
            }
            catch(Exception ex) when(IsDocumentFailure(ex))
            {
                fallbackLoads.Add(name);
                Protect(name,ex,DiagnosticStage.StoreLoad);
                PreserveUnreadableCopy(file,schema);
                return Fresh<T>();
            }
        }
    }

    private void SaveDocument<T>(string name,T value)
    {
        string file=DocumentPath(name),schema=Path.Combine(DirectoryPath,name+SchemaSidecarSuffix);
        lock(DocumentGate)
        {
            try
            {
                // Revalidating disk cannot make values returned by a failed Load trustworthy.
                // Keep blocking them even after a transient lock/access error has disappeared.
                if(fallbackLoads.Contains(name))
                    throw new StoreDocumentException(documentStates[name]==StoreDocumentState.FutureSchema?
                        DiagnosticCode.UnsupportedSchema:DiagnosticCode.InvalidDocument);
                // Serialize writers across Store instances AND processes using this profile.
                using var lease=AcquireWriteLease();
                var metadata=ReadSchema(schema);
                JsonNode? previous=null,known=null;byte[]? original=null;
                if(Exists(file))
                {
                    original=ReadBounded(file);
                    previous=MigratePayload<T>(ParsePayload(original),metadata.Version);
                    // Defaults can never overwrite an unreadable existing payload. The typed projection
                    // distinguishes unknown model fields from dictionary entries deliberately removed by the user.
                    known=JsonSerializer.SerializeToNode(DeserializePayload<T>(previous),Options);
                }
                var next=JsonSerializer.SerializeToNode(value,Options);
                if(next is not (JsonObject or JsonArray))throw new StoreDocumentException(DiagnosticCode.InvalidDocument);
                if(previous!=null && known!=null)next=PreserveUnknown(previous,next,known);
                byte[] bytes=Encode(next);
                if(original!=null && metadata.Version==0)PreserveMigrationOriginal(file,original);

                // Write-ahead version barrier. Future implementations MUST publish their target version
                // here BEFORE changing the payload; older readers then fail closed even after a crash.
                // With v0/pending1, readers apply the idempotent migration to either old or new payload.
                metadata.Metadata["PendingSchemaVersion"]=CurrentSchemaVersion;
                byte[] pendingMetadata=Encode(metadata.Metadata,64*1024);
                metadata.Metadata["SchemaVersion"]=CurrentSchemaVersion;
                metadata.Metadata.Remove("PendingSchemaVersion");
                byte[] committedMetadata=Encode(metadata.Metadata,64*1024);
                AtomicWrite(schema,pendingMetadata);
                AtomicWrite(file,bytes);
                // Payload publication is the caller's commit boundary (including staged credentials).
                // A pending=current barrier remains readable and can be finalized by a later Save.
                documentStates[name]=StoreDocumentState.Current;
                try{AtomicWrite(schema,committedMetadata);}
                catch(Exception ex) when(IsDocumentFailure(ex))
                {
                    Warning="The document was saved. Schema metadata finalization is pending and will be retried on a later save.";
                    PrivateDiagnostics.Record(DiagnosticStage.StoreSave,DiagnosticCode.SchemaFinalizationPending);
                }
            }
            catch(Exception ex) when(IsDocumentFailure(ex))
            {
                Protect(name,ex,DiagnosticStage.StoreSave);
                throw;
            }
        }
    }

    private static JsonNode PreserveUnknown(JsonNode previous,JsonNode next,JsonNode known)
    {
        if(previous is JsonObject oldObject && next is JsonObject newObject && known is JsonObject knownObject)
        {
            var merged=(JsonObject)newObject.DeepClone();
            foreach(var property in oldObject)
            {
                if(!knownObject.ContainsKey(property.Key) && !newObject.ContainsKey(property.Key))
                    merged[property.Key]=property.Value?.DeepClone();
                else if(property.Value is JsonNode oldValue && newObject[property.Key] is JsonNode newValue &&
                    knownObject[property.Key] is JsonNode knownValue)
                    merged[property.Key]=PreserveUnknown(oldValue,newValue,knownValue);
            }
            return merged;
        }
        // Arrays are replaced deliberately: index merging could attach private data to a different item after reorder.
        return next.DeepClone();
    }

    private FileStream AcquireWriteLease()
    {
        string file=Path.Combine(DirectoryPath,".store-write.lock");
        var elapsed=Stopwatch.StartNew();
        while(true)
        {
            try
            {
                _=Exists(file);
                return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            }
            catch(IOException ex) when(ex is not StoreDocumentException && elapsed.ElapsedMilliseconds<2000)
            { Thread.Sleep(25); }
        }
    }

    private static byte[] Encode(JsonNode value,int limit=MaxDocumentBytes)
    {
        var bytes=Encoding.UTF8.GetBytes(value.ToJsonString(Options));
        if(bytes.Length>limit)throw new StoreDocumentException(DiagnosticCode.LimitExceeded);
        return bytes;
    }

    private static void PreserveMigrationOriginal(string file,byte[] bytes)
    {
        string backup=file+".migration-v0.bak";
        if(Exists(backup))return; // Immutable; never replace a prior recovery source.
        AtomicWrite(backup,bytes);
    }

    private void PreserveUnreadableCopy(string file,string schema)
    {
        // Retain the previous Load fallback's local recovery-copy behavior, bounded and once only.
        // Failure to copy never authorizes rewriting the original; failed-load protection still blocks Save.
        try
        {
            string backup=file+".backup-original";
            if(Exists(backup))return;
            using var lease=AcquireWriteLease();
            _=ReadSchema(schema); // Never touch an unknown future or malformed metadata document.
            if(!Exists(backup) && Exists(file))AtomicWrite(backup,ReadBounded(file));
        }
        catch(Exception ex) when(IsDocumentFailure(ex)){/* The untouched original remains the recovery source. */}
    }

    private static void AtomicWrite(string file,byte[] bytes)
    {
        string temp=file+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough))
            {stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            if(Exists(file))
            {
                string backup=file+".last-good.bak";
                _=Exists(backup);
                // Atomic replacement also retains the last validated payload; never delete the live file first.
                File.Replace(temp,file,backup,true);
            }
            else File.Move(temp,file);
        }
        finally {try{if(File.Exists(temp))File.Delete(temp);}catch{/* A stale temp is harmless and is never loaded. */}}
    }

    private static bool IsDocumentFailure(Exception ex)=>ex is IOException or UnauthorizedAccessException or
        JsonException or InvalidOperationException or NotSupportedException or ArgumentException;

    private void Protect(string name,Exception ex,DiagnosticStage stage)
    {
        var code=PrivateDiagnostics.Classify(ex);
        documentStates[name]=code==DiagnosticCode.UnsupportedSchema?StoreDocumentState.FutureSchema:StoreDocumentState.RecoveryRequired;
        Warning=code==DiagnosticCode.UnsupportedSchema?
            "A newer settings schema was found. This document is read-only. Use a compatible NabyScape version or recover a preserved backup.":
            "A stored document could not be read or saved safely. Defaults may be in use; the original file is preserved. Review recovery guidance before changing files.";
        PrivateDiagnostics.Record(stage,code);
    }
}
