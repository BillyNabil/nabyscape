using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed record OfficialRelease(Version Version,string Tag,Uri Notes,Uri Setup,long Size,Uri Checksums);
internal sealed class UpdateState { public DateTimeOffset LastAttempt {get;set;} }
internal sealed class OfficialUpdates : IDisposable
{
    internal const string Repository="https://github.com/BillyNabil/nabyscape";
    internal static readonly Uri Endpoint=new("https://api.github.com/repos/BillyNabil/nabyscape/releases/latest");
    internal static Version InstalledVersion=>new(typeof(App).Assembly.GetName().Version!.ToString(3));
    private readonly HttpClient http;
    private readonly Store store;
    private readonly string stagingDirectory;
    private readonly Action<string,Version> inspect;
    private readonly UpdateState state;
    private int active;
    private CancellationTokenSource? cancellation;
    internal OfficialRelease? Available {get;private set;}
    internal string? VerifiedSetup {get;private set;}
    internal string? VerifiedHash {get;private set;}
    internal string Status {get;private set;}="Ready to check for updates.";
    internal bool Busy=>active!=0;
    internal event Action? Changed;
    internal event Action<OfficialRelease>? UpdateAvailable;
    internal event Action? DownloadReady;
    internal OfficialUpdates(Store store,HttpMessageHandler? transport=null,Action<string,Version>? inspect=null,string? stagingDirectory=null)
    {
        this.store=store;this.stagingDirectory=Path.GetFullPath(stagingDirectory??UpdateHandoff.Folder);this.inspect=inspect??InspectSetup;state=store.Load<UpdateState>("update-state");
        http=new(transport??new HttpClientHandler{AllowAutoRedirect=false});http.Timeout=TimeSpan.FromSeconds(45);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NabyScape/"+InstalledVersion);http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    internal void Cancel()=>cancellation?.Cancel();
    private void Report(string text){Status=text;Changed?.Invoke();}
    internal async Task Check(bool manual,bool enabled=true,bool autoDownload=false)
    {
        if(!manual && (!enabled || DateTimeOffset.UtcNow-state.LastAttempt<TimeSpan.FromHours(24)))return;
        bool checkedRelease=false;
        await Operation(async token=>
        {
            state.LastAttempt=DateTimeOffset.UtcNow;store.Save("update-state",state);Report("Checking official releases…");
            using var response=await Get(Endpoint,token);string json=await ReadText(response,1024*1024,token);
            Available=Parse(json,InstalledVersion);VerifiedSetup=null;VerifiedHash=null;
            checkedRelease=true;
            Report(Available==null?"You have the latest stable version.":Localization.Format("Version {0} is available.",Available.Version));
            if(Available!=null)UpdateAvailable?.Invoke(Available);
        });
        if(checkedRelease && !manual && autoDownload && Available!=null && !Busy)await Download();
    }
    internal async Task Download()
    {
        if(Available==null)return;
        await Operation(async token=>
        {
            var release=Available;VerifiedSetup=null;VerifiedHash=null;
            string folder=stagingDirectory;Directory.CreateDirectory(folder);
            string partial=Path.Combine(folder,"NabyScape-Setup.exe.partial"),target=Path.Combine(folder,"NabyScape-Setup.exe");
            try
            {
                Report("Downloading checksums…");using var sums=await Get(release.Checksums,token);
                string hash=Checksum(await ReadText(sums,65536,token));
                if(File.Exists(target) && new FileInfo(target).Length==release.Size)
                {
                    try
                    {
                        await Task.Run(()=>{VerifyFile(target,hash);inspect(target,release.Version);},token);
                        token.ThrowIfCancellationRequested();VerifiedHash=hash;VerifiedSetup=target;Report("Download verified. Choose Install and restart when ready.");DownloadReady?.Invoke();return;
                    }
                    catch(InvalidDataException){ /* A stale or incomplete cached setup is replaced below. */ }
                }
                using var response=await Get(release.Setup,token);
                if(response.Content.Headers.ContentLength is long length && length!=release.Size)throw new InvalidDataException("Setup length does not match release metadata.");
                using(var input=await response.Content.ReadAsStreamAsync(token))
                using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,81920,true))
                {
                    byte[] buffer=new byte[81920];long total=0;int read;long lastPercent=-1;
                    while((read=await input.ReadAsync(buffer,token))>0)
                    {total+=read;if(total>release.Size)throw new InvalidDataException("Setup is larger than expected.");await output.WriteAsync(buffer.AsMemory(0,read),token);long percent=total*100/release.Size;if(percent!=lastPercent){lastPercent=percent;Report(Localization.Format("Downloading update… {0}%",percent));}}
                    if(total!=release.Size)throw new InvalidDataException("Setup download was interrupted.");
                }
                token.ThrowIfCancellationRequested();await Task.Run(()=>{VerifyFile(partial,hash);inspect(partial,release.Version);},token);token.ThrowIfCancellationRequested();
                if(OperatingSystem.IsWindows())File.WriteAllText(partial+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\nHostUrl="+release.Setup.AbsoluteUri+"\r\n");
                File.Move(partial,target,true);VerifiedHash=hash;VerifiedSetup=target;Report("Download verified. Choose Install and restart when ready.");DownloadReady?.Invoke();
            }
            finally{if(File.Exists(partial))File.Delete(partial);}
        });
    }
    private async Task Operation(Func<CancellationToken,Task> action)
    {
        if(Interlocked.CompareExchange(ref active,1,0)!=0)return;
        using var source=new CancellationTokenSource(TimeSpan.FromMinutes(10));cancellation=source;
        var clock=Stopwatch.StartNew();PrivateDiagnostics.SetServiceState(DiagnosticService.Updates,DiagnosticServiceState.Busy);
        try{await action(source.Token);PrivateDiagnostics.Record(DiagnosticStage.Updater,DiagnosticCode.Completed,clock.Elapsed);PrivateDiagnostics.SetServiceState(DiagnosticService.Updates,DiagnosticServiceState.Ready);}
        catch(OperationCanceledException){PrivateDiagnostics.Record(DiagnosticStage.Updater,DiagnosticCode.Cancelled,clock.Elapsed);PrivateDiagnostics.SetServiceState(DiagnosticService.Updates,DiagnosticServiceState.Degraded);Report("Update cancelled or timed out. Your current app is unchanged.");}
        catch(Exception ex){PrivateDiagnostics.Record(DiagnosticStage.Updater,ex is InvalidDataException?DiagnosticCode.VerificationFailed:DiagnosticCode.UnknownFailure,clock.Elapsed);PrivateDiagnostics.SetServiceState(DiagnosticService.Updates,DiagnosticServiceState.Failed);store.Log(ex);Report(Localization.Format("Update failed: {0}",ex.Message));}
        finally{cancellation=null;Interlocked.Exchange(ref active,0);Changed?.Invoke();}
    }
    private async Task<HttpResponseMessage> Get(Uri uri,CancellationToken token)
    {
        for(int attempt=0;;attempt++)
        {
            var result=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            // GitHub release downloads use a signed redirect. Only its dedicated asset CDN is accepted.
            if(result.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect)
            {
                var next=result.Headers.Location;result.Dispose();
                if(uri.Host!="github.com" || next==null || next.Scheme!="https" || next.Host!="release-assets.githubusercontent.com" || next.UserInfo!="" || !next.IsDefaultPort)throw new InvalidDataException("Untrusted update redirect.");
                result=await http.GetAsync(next,HttpCompletionOption.ResponseHeadersRead,token);
            }
            if((result.StatusCode==HttpStatusCode.TooManyRequests || (int)result.StatusCode>=500) && attempt<1)
            {result.Dispose();await Task.Delay(TimeSpan.FromSeconds(2),token);continue;}
            try{result.EnsureSuccessStatusCode();return result;}catch{result.Dispose();throw;}
        }
    }
    private static async Task<string> ReadText(HttpResponseMessage response,int limit,CancellationToken token)
    {
        using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();byte[] bytes=new byte[8192];int count;
        while((count=await input.ReadAsync(bytes,token))>0){if(output.Length+count>limit)throw new InvalidDataException("Update metadata is too large.");output.Write(bytes,0,count);}
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
    internal static OfficialRelease? Parse(string json,Version current)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        string tag=root.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..],out var version) || root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Expected a stable official release.");
        var notes=new Uri(root.GetProperty("html_url").GetString()!);
        if(notes.AbsoluteUri!=Repository+"/releases/tag/"+tag)throw new InvalidDataException("Invalid release identity.");
        if(version<=current)return null;
        Uri Asset(string name,out long size)
        {
            var assets=root.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==name).ToArray();
            if(assets.Length!=1)throw new InvalidDataException("Missing or duplicate official asset: "+name);
            var a=assets[0];size=a.GetProperty("size").GetInt64();
            var uri=new Uri(a.GetProperty("browser_download_url").GetString()!);
            if(uri.AbsoluteUri!=Repository+"/releases/download/"+tag+"/"+name || size<1 || size>512L*1024*1024)throw new InvalidDataException("Invalid official asset.");
            return uri;
        }
        var setup=Asset(RuntimePlatform.SetupAsset,out long size);var sums=Asset("SHA256SUMS",out _);
        return new(version,tag,notes,setup,size,sums);
    }
    internal static string Checksum(string text)
    {
        var matches=text.Split('\n').Select(line=>Regex.Match(line.TrimEnd('\r'),@"^([a-fA-F0-9]{64})  "+Regex.Escape(RuntimePlatform.SetupAsset)+"$")).Where(m=>m.Success).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Invalid or missing setup checksum.");return matches[0].Groups[1].Value.ToUpperInvariant();
    }
    internal static void VerifyFile(string path,string hash)
    {using var file=File.OpenRead(path);if(!string.Equals(Convert.ToHexString(SHA256.HashData(file)),hash,StringComparison.Ordinal))throw new InvalidDataException("Setup checksum mismatch.");}
    internal static void InspectSetup(string path,Version expected)
    {
        using var file=File.OpenRead(path);using var reader=new BinaryReader(file);
        if(file.Length<64 || reader.ReadUInt16()!=0x5A4D)throw new InvalidDataException("Setup is not a Windows executable.");
        file.Position=0x3C;int offset=reader.ReadInt32();if(offset<64 || offset>file.Length-6)throw new InvalidDataException("Invalid setup header.");
        file.Position=offset;if(reader.ReadUInt32()!=0x4550 || reader.ReadUInt16()!=RuntimePlatform.PeMachine)throw new InvalidDataException("Setup architecture does not match "+RuntimePlatform.Rid+".");
        UpdateSignature.VerifyIfPresent(path);
        var version=FileVersionInfo.GetVersionInfo(path);if(new Version(version.FileMajorPart,version.FileMinorPart,version.FileBuildPart)!=expected)throw new InvalidDataException("Setup version does not match the release.");
    }
    internal static bool IsInstalled(string path)=>InstallPaths.OwnsExecutable(path);
    internal static bool IsAutomaticInstallPath(string path)=>IsInstalled(path) && string.Equals(Path.GetFileName(path),"NabyScape.exe",StringComparison.OrdinalIgnoreCase);
    internal static void RequireAutomaticInstallPath(string path)
    {
        if(!IsInstalled(path))throw new IOException("Portable copy: quit and replace the portable files manually.");
        if(!IsAutomaticInstallPath(path))throw new IOException("This legacy executable cannot install or restore updates automatically. Quit the app, download the official installer from "+Repository+"/releases/latest, run it, then open NabyScape.exe before using automatic recovery.");
    }
    public void Dispose(){Cancel();http.Dispose();}
}
