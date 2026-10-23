using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NabyScape;

// Only application-owned enums cross this boundary. Never pass a message, path, endpoint or exception text.
internal enum DiagnosticStage { Application, StoreLoad, StoreSave, Updater, Recovery, DiagnosticsCopy, DiagnosticsExport }
internal enum DiagnosticCode { UnknownFailure, InvalidJson, InvalidDocument, UnsupportedSchema, AccessDenied,
    IoFailure, LimitExceeded, Cancelled, VerificationFailed, MigrationFailed, Completed, SchemaFinalizationPending }
internal enum DiagnosticService { Application, Media, Audio, Assistant, Updates, Recovery }
internal enum DiagnosticServiceState { Unknown, Disabled, Starting, Ready, Busy, Degraded, Failed }

internal static class PrivateDiagnostics
{
    private const int Capacity=64;
    private static readonly object Gate=new();
    private readonly record struct Entry(DiagnosticStage Stage,DiagnosticCode Code,long? DurationMilliseconds);
    private static readonly Entry[] Ring=new Entry[Capacity];
    private static readonly Dictionary<DiagnosticService,DiagnosticServiceState> Services=new();
    private static int next,count;

    internal static void Record(DiagnosticStage stage,DiagnosticCode code,TimeSpan? duration=null)
    {
        if(!Enum.IsDefined(stage) || !Enum.IsDefined(code))return;
        long? milliseconds=duration.HasValue?(long)Math.Clamp(duration.Value.TotalMilliseconds,0,86_400_000):null;
        lock(Gate)
        {
            Ring[next]=new(stage,code,milliseconds);
            next=(next+1)%Capacity;count=Math.Min(count+1,Capacity);
        }
    }

    internal static void SetServiceState(DiagnosticService service,DiagnosticServiceState state)
    {
        if(!Enum.IsDefined(service) || !Enum.IsDefined(state))return;
        lock(Gate)Services[service]=state;
    }

    // Classification uses types only. In particular, do not inspect Message, StackTrace, Data or InnerException.
    internal static DiagnosticCode Classify(Exception ex)=>ex switch
    {
        StoreDocumentException document=>document.Code,
        JsonException or DecoderFallbackException=>DiagnosticCode.InvalidJson,
        UnauthorizedAccessException=>DiagnosticCode.AccessDenied,
        OperationCanceledException=>DiagnosticCode.Cancelled,
        InvalidDataException=>DiagnosticCode.InvalidDocument,
        IOException=>DiagnosticCode.IoFailure,
        _=>DiagnosticCode.UnknownFailure
    };

    internal static string CreateReport(Store store)
    {
        // Read Store health before taking Gate, matching the Store -> diagnostics lock order.
        var health=store.GetHealth();
        Entry[] entries;KeyValuePair<DiagnosticService,DiagnosticServiceState>[] services;
        lock(Gate)
        {
            entries=Enumerable.Range(0,count).Select(i=>Ring[(next-count+i+Capacity)%Capacity]).ToArray();
            services=Enum.GetValues<DiagnosticService>().Select(service=>new KeyValuePair<DiagnosticService,DiagnosticServiceState>(
                service,Services.TryGetValue(service,out var state)?state:DiagnosticServiceState.Unknown)).ToArray();
        }
        // Construct an explicit allowlist projection; never serialize App, Settings, exceptions or service objects.
        return JsonSerializer.Serialize(new
        {
            ReportSchemaVersion=1,
            Application=new
            {
                Name="NabyScape",
                Version=typeof(App).Assembly.GetName().Version?.ToString()??"unknown",
#if DEBUG
                Build="Debug",
#else
                Build="Release",
#endif
                ProcessArchitecture=ArchitectureName(RuntimeInformation.ProcessArchitecture),
                OperatingSystemArchitecture=ArchitectureName(RuntimeInformation.OSArchitecture),
                RuntimeVersion=Environment.Version.ToString(),
                OperatingSystemVersion=Environment.OSVersion.Version.ToString()
            },
            Configuration=new
            {
                SupportedSchemaVersion=Store.CurrentSchemaVersion,
                Coverage="Documents observed in this process only",
                health.ObservedDocuments,health.LegacyDocuments,health.CurrentDocuments,
                health.RecoveryRequiredDocuments,health.FutureSchemaDocuments,health.HasStorageWarning
            },
            Services=services.Select(pair=>new {Service=pair.Key.ToString(),State=pair.Value.ToString()}).ToArray(),
            RecentEvents=entries.Select(entry=>new {Stage=entry.Stage.ToString(),Code=entry.Code.ToString(),entry.DurationMilliseconds}).ToArray(),
            EventCapacity=Capacity
        },new JsonSerializerOptions{WriteIndented=true});
    }

    private static string ArchitectureName(Architecture architecture)=>architecture switch
    {Architecture.X64=>"x64",Architecture.Arm64=>"arm64",Architecture.X86=>"x86",Architecture.Arm=>"arm",_=>"other"};
}
