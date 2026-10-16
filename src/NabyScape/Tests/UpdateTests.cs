using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal static class UpdateTests
{
    private sealed class Fake(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
    private static string Release(string version="2.4.3",string host="https://github.com/BillyNabil/nabyscape",string name="NabyScape-win-x64-Setup.exe")=>JsonSerializer.Serialize(new{tag_name="v"+version,html_url=host+"/releases/tag/v"+version,draft=false,prerelease=false,assets=new[]{new{name, browser_download_url=host+"/releases/download/v"+version+"/"+name,size=4},new{name="SHA256SUMS",browser_download_url=host+"/releases/download/v"+version+"/SHA256SUMS",size=100}}});
    internal static void Run(Action<string,Action> test)
    {
        static void Assert(bool pass){if(!pass)throw new Exception("Update fixture failed");}
        static void Reject(Action action){try{action();}catch(Exception ex) when(ex is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or UriFormatException or System.Collections.Generic.KeyNotFoundException){return;}throw new Exception("Invalid input accepted");}
        test("Updates accept newer stable and ignore equal/older releases",()=>{Assert(OfficialUpdates.Parse(Release(),new(2,4,2))?.Version==new Version(2,4,3));Assert(OfficialUpdates.Parse(Release(),new(2,4,3))==null && OfficialUpdates.Parse(Release(),new(3,0,0))==null);});
        test("Updates reject malformed host schema prerelease and wrong architecture assets",()=>
        {Reject(()=>OfficialUpdates.Parse("{}",new(2,4,2)));Reject(()=>OfficialUpdates.Parse(Release(host:"http://github.com/BillyNabil/nabyscape"),new(2,4,2)));Reject(()=>OfficialUpdates.Parse(Release(host:"https://github.com.evil/BillyNabil/nabyscape"),new(2,4,2)));Reject(()=>OfficialUpdates.Parse(Release(name:"NabyScape-win-arm64-Setup.exe"),new(2,4,2)));Reject(()=>OfficialUpdates.Parse(Release().Replace("\"prerelease\":false","\"prerelease\":true"),new(2,4,2)));Reject(()=>OfficialUpdates.Parse(Release("2.4.3-beta"),new(2,4,2)));});
        test("Pre-update preference backups restore with default update preference",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-old-backup-"+Guid.NewGuid().ToString("N"));try{var store=new Store(root);string json=JsonSerializer.Serialize(new Settings{Width=333});var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;node.AsObject().Remove("AutoCheckUpdates");File.WriteAllText(Path.Combine(root,"preference-backup.json"),node.ToJsonString());var settings=new Settings{AutoCheckUpdates=false};PreferenceReset.Restore(store,settings);Assert(settings.Width==333 && settings.AutoCheckUpdates);}finally{Directory.Delete(root,true);}
        });
        test("Updates strict checksum parsing rejects duplicate missing and malformed hashes",()=>{string line=new string('A',64)+"  NabyScape-win-x64-Setup.exe";Assert(OfficialUpdates.Checksum(line)==new string('A',64));Reject(()=>OfficialUpdates.Checksum(line+"\n"+line));Reject(()=>OfficialUpdates.Checksum("bad"));});
        void Fixture(string name,Action<Store> action)=>test(name,()=>{string path=Path.Combine(Path.GetTempPath(),"NabyScape-update-test-"+Guid.NewGuid().ToString("N"));try{action(new Store(path));}finally{if(Directory.Exists(path))Directory.Delete(path,true);}});
        static HttpResponseMessage Ok(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text)};
        Fixture("Updates offline timeout rate-limit and redirects leave app intact",store=>
        {
            foreach(string mode in new[]{"offline","timeout","rate","redirect"})
            {
                int calls=0;using var updates=new OfficialUpdates(store,new Fake((request,token)=>{calls++;if(mode=="offline")throw new HttpRequestException("offline");if(mode=="timeout")throw new TaskCanceledException();if(mode=="redirect")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found){Headers={Location=new Uri("https://evil.test/setup.exe")}});return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));}));
                updates.Check(true).GetAwaiter().GetResult();Assert(!updates.Busy && updates.Available==null && updates.VerifiedSetup==null && calls<=2);
            }
        });
        Fixture("Updates duplicate checks and 24 hour state do not poll",store=>
        {
            var pending=new TaskCompletionSource<HttpResponseMessage>();int calls=0;using var updates=new OfficialUpdates(store,new Fake((r,t)=>{calls++;return pending.Task;}));var first=updates.Check(true);updates.Check(true).GetAwaiter().GetResult();Assert(calls==1);pending.SetResult(Ok(Release()));first.GetAwaiter().GetResult();updates.Check(false).GetAwaiter().GetResult();Assert(calls==1 && File.Exists(Path.Combine(store.DirectoryPath,"update-state.json")));
            using var next=new OfficialUpdates(store,new Fake((r,t)=>throw new Exception("Should not poll")));next.Check(false).GetAwaiter().GetResult();Assert(next.Status=="Ready to check for updates.");Assert(!CustomizationProfile.Export(new Settings()).Contains("AutoCheckUpdates"));
        });
        Fixture("Updates bad checksum and truncated downloads never become executable",store=>
        {
            foreach(bool truncated in new[]{false,true})
            {
                using var updates=new OfficialUpdates(store,new Fake((r,t)=>Task.FromResult(Ok(r.RequestUri==OfficialUpdates.Endpoint?Release():r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS")?new string('A',64)+"  NabyScape-win-x64-Setup.exe":truncated?"abc":"abcd"))));updates.Check(true).GetAwaiter().GetResult();updates.Download().GetAwaiter().GetResult();Assert(updates.VerifiedSetup==null && updates.Status.Contains("failed") && !File.Exists(Path.Combine(store.DirectoryPath,"updates","NabyScape-Setup.exe.partial")));
            }
        });
        Fixture("Updates successful fake download checksum verification and duplicate download exclusion",store=>
        {
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("abcd")));var pending=new TaskCompletionSource<HttpResponseMessage>();int setupCalls=0,inspections=0;
            using var updates=new OfficialUpdates(store,new Fake((r,t)=>
            {
                if(r.RequestUri==OfficialUpdates.Endpoint)return Task.FromResult(Ok(Release()));
                if(r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS"))return Task.FromResult(Ok(hash+"  NabyScape-win-x64-Setup.exe"));
                setupCalls++;return pending.Task;
            }),(path,version)=>{inspections++;Assert(version==new Version(2,4,3));});
            updates.Check(true).GetAwaiter().GetResult();var first=updates.Download();updates.Download().GetAwaiter().GetResult();Assert(setupCalls==1);pending.SetResult(Ok("abcd"));first.GetAwaiter().GetResult();Assert(inspections==1 && updates.VerifiedHash==hash && File.ReadAllText(updates.VerifiedSetup!)=="abcd");
        });
        test("Update helper fake installer waits verifies and surfaces failure without executing on timeout",()=>
        {
            int step=0;UpdateHandoff.Complete(()=>{Assert(step++==0);return true;},()=>Assert(step++==1),()=>{Assert(step++==2);return 0;});Assert(step==3);
            try{UpdateHandoff.Complete(()=>false,()=>throw new Exception("Must not verify"),()=>throw new Exception("Must not install"));throw new Exception("Timeout accepted");}catch(IOException){}
            try{UpdateHandoff.Complete(()=>true,()=>{},()=>7);throw new Exception("Failure accepted");}catch(IOException ex){Assert(ex.Message.Contains("7"));}
        });
        test("Updates inspect actual published x64 file version and reject mismatch without executing it",()=>
        {OfficialUpdates.InspectSetup(Environment.ProcessPath!,OfficialUpdates.InstalledVersion);Reject(()=>OfficialUpdates.InspectSetup(Environment.ProcessPath!,new Version(99,0,0)));});
        test("Update duplicate install handoff is excluded until completion",()=>
        {try{Assert(UpdateHandoff.TryBegin() && !UpdateHandoff.TryBegin());}finally{UpdateHandoff.End();}try{Assert(UpdateHandoff.TryBegin());}finally{UpdateHandoff.End();}});
        test("Update helper rejects downgrade malformed version and arbitrary executable handoffs",()=>
        {
            var data=new UpdateHandoffData{ProcessId=42,StartedUtcTicks=DateTime.UtcNow.Ticks,InstalledPath=Path.Combine(Installer.InstallDirectory,"NabyScape.exe"),Hash=new string('A',64),Version="99.0.0"};Assert(UpdateHandoff.Validate(data)==new Version(99,0,0));
            foreach(string version in new[]{"2.4.2","1.0.0","99.0.0-beta",""}){data.Version=version;try{UpdateHandoff.Validate(data);throw new Exception("Invalid handoff accepted");}catch(IOException){}}
            data.Version="99.0.0";data.InstalledPath=Path.Combine(Path.GetTempPath(),"other.exe");try{UpdateHandoff.Validate(data);throw new Exception("Arbitrary handoff accepted");}catch(IOException){}
        });
        test("Update handoff exact PID start time path portable guard and backup failure",()=>
        {
            string path=Path.Combine(Installer.InstallDirectory,"NabyScape.exe");var data=new UpdateHandoffData{ProcessId=42,StartedUtcTicks=123,InstalledPath=path};Assert(UpdateHandoff.SameProcess(data,42,123,path));Assert(!UpdateHandoff.SameProcess(data,43,123,path) && !UpdateHandoff.SameProcess(data,42,124,path) && !OfficialUpdates.IsInstalled(Path.Combine(Path.GetTempPath(),"NabyScape.exe")));int saves=0;try{UpdateHandoff.Flush(()=>saves++,()=>throw new IOException("backup failure"),()=>saves++);}catch(IOException){}Assert(saves==1);
        });
    }
}
