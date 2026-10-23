using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NabyScape;

internal static class UpdateSignature
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct FileInfo {internal uint Size;internal string Path;internal IntPtr File,Subject;}
    [StructLayout(LayoutKind.Sequential)] private struct TrustData {internal uint Size;internal IntPtr Policy,Sip;internal uint Ui,Revocation,Choice;internal IntPtr File;internal uint State;internal IntPtr StateData,Url;internal uint Flags,Context;}
    [DllImport("wintrust.dll",ExactSpelling=true)] private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
    internal static void VerifyIfPresent(string path)
    {
        var file=new FileInfo{Size=(uint)Marshal.SizeOf<FileInfo>(),Path=path};IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(file,pointer,false);var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),Ui=2,Revocation=1,Choice=1,File=pointer};var action=new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");int result=WinVerifyTrust(new IntPtr(-1),ref action,ref data);
            // Existing official releases are unsigned. Once present, Authenticode must pass Windows trust/revocation policy.
            if(result!=0 && result!=unchecked((int)0x800B0100))throw new IOException("Windows could not validate the setup signature (0x"+result.ToString("X8")+").");
        }
        finally{Marshal.DestroyStructure<FileInfo>(pointer);Marshal.FreeHGlobal(pointer);}
    }
}
internal sealed class UpdateHandoffData
{
    public int ProcessId {get;set;}
    public long StartedUtcTicks {get;set;}
    public string InstalledPath {get;set;}="";
    public string Hash {get;set;}="";
    public string Version {get;set;}="";
    public string ProfileDirectory {get;set;}="";
    public string RestoreId {get;set;}="";
}
internal static class UpdateHandoff
{
    internal sealed class RecoveryBlockedException(string message):IOException(message);
    private static int preparing;
    internal static bool TryBegin()=>System.Threading.Interlocked.CompareExchange(ref preparing,1,0)==0;
    internal static void End()=>System.Threading.Interlocked.Exchange(ref preparing,0);
    internal static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NabyScape-Updates");
    internal static async System.Threading.Tasks.Task Prepare(App app,bool restore=false)
    {
        if(!TryBegin())return;
        try
        {
            string installed=Environment.ProcessPath!;
            OfficialUpdates.RequireAutomaticInstallPath(installed);
            string profile=UpdateRecovery.ProfileDirectory(app.Store.DirectoryPath);
            string? restoreId=restore?UpdateRecovery.PreviousId():null;
            if(restore)
            {
                if(restoreId==null)throw new IOException("No previous version is available.");
                await System.Threading.Tasks.Task.Run(()=>UpdateRecovery.Read(restoreId));
            }
            else
            {
                if(app.Updates.Busy || app.Updates.VerifiedSetup==null || app.Updates.VerifiedHash==null)throw new IOException("Download and verify an update first.");
                string setup=Path.Combine(Folder,"NabyScape-Setup.exe");
                if(!string.Equals(setup,app.Updates.VerifiedSetup,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid update staging location.");
                await System.Threading.Tasks.Task.Run(()=>{OfficialUpdates.VerifyFile(setup,app.Updates.VerifiedHash);OfficialUpdates.InspectSetup(setup,app.Updates.Available!.Version);});
            }
            Flush(()=>app.FlushPreferenceEditors(),()=>app.Store.Save("preference-backup",app.Settings),()=>app.Store.Save("settings",app.Settings),()=>app.Store.Save("session",app.Session));
            Directory.CreateDirectory(Folder);
            using var process=Process.GetCurrentProcess();
            var data=new UpdateHandoffData{ProcessId=process.Id,StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks,InstalledPath=installed,ProfileDirectory=profile,RestoreId=restoreId??"",Hash=restore?"":app.Updates.VerifiedHash!,Version=restore?"":app.Updates.Available!.Version.ToString()};
            File.WriteAllText(Path.Combine(Folder,"handoff.json"),JsonSerializer.Serialize(data));
            string helper=Path.Combine(Folder,"NabyScape-UpdateHelper.exe");File.Copy(installed,helper,true);
            using var ready=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset,"Local\\NabyScape.UpdateReady."+process.Id);
            _=Process.Start(new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--update-handoff"}})??throw new IOException("Could not start updater helper.");
            if(!await System.Threading.Tasks.Task.Run(()=>ready.WaitOne(15000)))throw new IOException("Updater did not confirm process identity. Your app remains open.");
            app.Quit();
        }
        finally{End();}
    }
    internal static void Flush(params Action[] saves){foreach(var save in saves)save();}
    internal static bool SameProcess(UpdateHandoffData data,int id,long started,string path)=>data.ProcessId==id && data.StartedUtcTicks==started && string.Equals(data.InstalledPath,path,StringComparison.OrdinalIgnoreCase) && OfficialUpdates.IsInstalled(path);
    internal static void Complete(Func<bool> wait,Action verify,Func<int> install)
    {
        if(!wait())throw new IOException("NabyScape did not quit. Installation cancelled.");
        verify();int result=install();if(result!=0)throw new IOException("Setup failed with exit code "+result+".");
    }
    internal static System.Version Validate(UpdateHandoffData data)
    {
        OfficialUpdates.RequireAutomaticInstallPath(data.InstalledPath);
        if(!OfficialUpdates.IsInstalled(data.InstalledPath) || data.ProcessId<=0 || data.StartedUtcTicks<=0 || data.StartedUtcTicks>DateTime.UtcNow.AddMinutes(1).Ticks || !System.Text.RegularExpressions.Regex.IsMatch(data.Hash,"^[A-F0-9]{64}$") || !System.Text.RegularExpressions.Regex.IsMatch(data.Version,@"^\d+\.\d+\.\d+$") || !System.Version.TryParse(data.Version,out var version) || version<=OfficialUpdates.InstalledVersion)throw new IOException("Invalid update handoff.");
        return version;
    }
    internal static void RunInstaller(string path,bool recovery=false)
    {
        var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true};
        if(recovery)start.ArgumentList.Add("--install");
        start.ArgumentList.Add("--silent");
        using var installer=Process.Start(start)??throw new IOException("Could not start setup.");
        try
        {
            if(!installer.WaitForExit(120000))throw new IOException("Setup timed out. Recovery files are preserved.");
            if(installer.ExitCode!=0)throw new IOException("Setup failed with exit code "+installer.ExitCode+".");
        }
        catch
        {
            // Disposing Process does not stop setup. Establish exit before any fallback mutation or launch.
            bool exited=false;
            try
            {
                if(installer.HasExited)exited=true;
                else
                {
                    try{installer.Kill();}catch(Exception){ /* Still wait: setup may have exited concurrently. */ }
                    exited=installer.WaitForExit(10000);
                }
            }
            catch(Exception){ /* An unobservable process must block recovery. */ }
            if(!exited)throw new RecoveryBlockedException("Setup could not be confirmed stopped. Automatic recovery is blocked; recovery files are preserved. Close setup before using the official installer manually.");
            throw;
        }
    }
    internal static void SignalStarted(string[] args)
    {
        int index=Array.IndexOf(args,"--update-started");
        if(index<0 || index+1>=args.Length || !UpdateRecovery.ValidId(args[index+1]) || !OfficialUpdates.IsInstalled(Environment.ProcessPath!))return;
        try{using var ready=System.Threading.EventWaitHandle.OpenExisting("Local\\NabyScape.UpdateStarted."+args[index+1]);ready.Set();}
        catch(System.Threading.WaitHandleCannotBeOpenedException){}
    }
    private static void Launch(string installed,bool requireReady)
    {
        string token=Guid.NewGuid().ToString("N");
        using var ready=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset,"Local\\NabyScape.UpdateStarted."+token);
        var start=new ProcessStartInfo(installed){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--background","--update-started",token}};
        using var child=Process.Start(start)??throw new IOException("Could not reopen NabyScape.");
        if(!requireReady)return; // Older versions do not implement the startup acknowledgement.
        if(ready.WaitOne(45000))return;
        if(!child.HasExited)
        {
            if(!string.Equals(child.MainModule?.FileName,installed,StringComparison.OrdinalIgnoreCase))throw new RecoveryBlockedException("The updated process identity changed; automatic recovery stopped.");
            try{child.Kill();if(!child.WaitForExit(10000))throw new RecoveryBlockedException("The updated app could not stop; automatic recovery stopped.");}
            catch(System.ComponentModel.Win32Exception){throw new RecoveryBlockedException("The updated app could not stop; recovery files are preserved.");}
        }
        throw new IOException("The updated app did not complete startup. Restoring the saved version.");
    }
    internal static int Run()
    {
        string? snapshot=null;bool installationStarted=false;UpdateHandoffData? data=null;
        try
        {
            if(!string.Equals(Environment.ProcessPath,Path.Combine(Folder,"NabyScape-UpdateHelper.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid updater helper location.");
            string handoff=Path.Combine(Folder,"handoff.json");if(new FileInfo(handoff).Length>8192)throw new IOException("Invalid update handoff.");
            data=JsonSerializer.Deserialize<UpdateHandoffData>(File.ReadAllText(handoff))??throw new IOException("Missing update handoff.");
            OfficialUpdates.RequireAutomaticInstallPath(data.InstalledPath);
            bool restore=data.RestoreId.Length>0;
            Version? version=restore?null:Validate(data);
            UpdateRecovery.ProfileDirectory(data.ProfileDirectory);
            if(restore)
            {
                if(!OfficialUpdates.IsInstalled(data.InstalledPath))throw new IOException("Invalid recovery destination.");
                var saved=UpdateRecovery.Read(data.RestoreId);
                if(!string.Equals(saved.ProfileDirectory,data.ProfileDirectory,StringComparison.OrdinalIgnoreCase))throw new IOException("Recovery profile does not match the running app.");
            }
            using(var parent=Process.GetProcessById(data.ProcessId))
            {
                if(!SameProcess(data,parent.Id,parent.StartTime.ToUniversalTime().Ticks,parent.MainModule?.FileName??""))throw new IOException("Update process identity changed.");
                using(var ready=System.Threading.EventWaitHandle.OpenExisting("Local\\NabyScape.UpdateReady."+data.ProcessId))ready.Set();
                if(!parent.WaitForExit(120000))throw new IOException("NabyScape did not quit. Installation cancelled.");
                if(restore){snapshot=UpdateRecovery.Capture(data.InstalledPath,data.ProfileDirectory);installationStarted=true;UpdateRecovery.Restore(data.RestoreId,data.InstalledPath);Launch(data.InstalledPath,false);UpdateRecovery.Prune(snapshot);}
                else
                {
                    string setup=Path.Combine(Folder,"NabyScape-Setup.exe");OfficialUpdates.VerifyFile(setup,data.Hash);OfficialUpdates.InspectSetup(setup,version!);
                    snapshot=UpdateRecovery.Capture(data.InstalledPath,data.ProfileDirectory);
                    installationStarted=true;
                    RunInstaller(setup);
                    OfficialUpdates.VerifyFile(data.InstalledPath,data.Hash);Launch(data.InstalledPath,true);UpdateRecovery.Prune(snapshot);
                }
            }
            File.Delete(handoff);return 0;
        }
        catch(Exception ex)
        {
            Directory.CreateDirectory(Folder);File.AppendAllText(Path.Combine(Folder,"update-error.log"),DateTimeOffset.UtcNow+" "+ex+Environment.NewLine);
            try
            {
                if(ex is RecoveryBlockedException)return 1;
                if(data!=null && snapshot!=null && installationStarted){UpdateRecovery.Restore(snapshot,data.InstalledPath);Launch(data.InstalledPath,false);}
                else if(data!=null && OfficialUpdates.IsAutomaticInstallPath(data.InstalledPath))
                {
                    // Launch only after the original, verified parent has actually exited.
                    bool exited;try{using var parent=Process.GetProcessById(data.ProcessId);exited=parent.StartTime.ToUniversalTime().Ticks!=data.StartedUtcTicks || parent.HasExited;}catch(ArgumentException){exited=true;}
                    if(exited)Launch(data.InstalledPath,false);
                }
            }
            catch(Exception recovery){File.AppendAllText(Path.Combine(Folder,"update-error.log"),DateTimeOffset.UtcNow+" Recovery: "+recovery+Environment.NewLine);}
            return 1;
        }
    }
}
