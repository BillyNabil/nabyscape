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
}
internal static class UpdateHandoff
{
    private static int preparing;
    internal static bool TryBegin()=>System.Threading.Interlocked.CompareExchange(ref preparing,1,0)==0;
    internal static void End()=>System.Threading.Interlocked.Exchange(ref preparing,0);
    internal static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NabyScape","updates");
    internal static async System.Threading.Tasks.Task Prepare(App app)
    {
        if(!TryBegin())return;
        try
        {
        if(app.Updates.Busy || app.Updates.VerifiedSetup==null || app.Updates.VerifiedHash==null)throw new IOException("Download and verify an update first.");
        string installed=Environment.ProcessPath!;
        if(!OfficialUpdates.IsInstalled(installed))throw new IOException("Portable copy: open the official release and replace your portable files manually after quitting.");
        string setup=Path.Combine(Folder,"NabyScape-Setup.exe");
        if(!string.Equals(setup,app.Updates.VerifiedSetup,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid update staging location.");
        await System.Threading.Tasks.Task.Run(()=>{OfficialUpdates.VerifyFile(setup,app.Updates.VerifiedHash);OfficialUpdates.InspectSetup(setup,app.Updates.Available!.Version);});
        // Every fallible save happens before the helper starts and before shutdown.
        Flush(()=>app.FlushPreferenceEditors(),()=>app.Store.Save("preference-backup",app.Settings),()=>app.Store.Save("settings",app.Settings),()=>app.Store.Save("session",app.Session));
        using var process=Process.GetCurrentProcess();
        var data=new UpdateHandoffData{ProcessId=process.Id,StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks,InstalledPath=installed,Hash=app.Updates.VerifiedHash,Version=app.Updates.Available!.Version.ToString()};
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
        verify();int result=install();if(result!=0)throw new IOException("Setup failed with exit code "+result+". Reopen your installed NabyScape; see install-error.txt.");
    }
    internal static System.Version Validate(UpdateHandoffData data)
    {
        if(!OfficialUpdates.IsInstalled(data.InstalledPath) || data.ProcessId<=0 || data.StartedUtcTicks<=0 || data.StartedUtcTicks>DateTime.UtcNow.AddMinutes(1).Ticks || !System.Text.RegularExpressions.Regex.IsMatch(data.Hash,"^[A-F0-9]{64}$") || !System.Text.RegularExpressions.Regex.IsMatch(data.Version,@"^\d+\.\d+\.\d+$") || !System.Version.TryParse(data.Version,out var version) || version<=OfficialUpdates.InstalledVersion)throw new IOException("Invalid update handoff.");
        return version;
    }
    internal static int Run()
    {
        try
        {
            if(!string.Equals(Environment.ProcessPath,Path.Combine(Folder,"NabyScape-UpdateHelper.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid updater helper location.");
            var data=JsonSerializer.Deserialize<UpdateHandoffData>(File.ReadAllText(Path.Combine(Folder,"handoff.json")))??throw new IOException("Missing update handoff.");
            var version=Validate(data);
            using(var parent=Process.GetProcessById(data.ProcessId))
            {
                if(!SameProcess(data,parent.Id,parent.StartTime.ToUniversalTime().Ticks,parent.MainModule?.FileName??""))throw new IOException("Update process identity changed.");
                using(var ready=System.Threading.EventWaitHandle.OpenExisting("Local\\NabyScape.UpdateReady."+data.ProcessId))ready.Set();
                string setup=Path.Combine(Folder,"NabyScape-Setup.exe");
                Complete(()=>parent.WaitForExit(120000),()=>{OfficialUpdates.VerifyFile(setup,data.Hash);OfficialUpdates.InspectSetup(setup,version);},()=>
                {
                    using var installer=Process.Start(new ProcessStartInfo(setup){UseShellExecute=true,ArgumentList={"--silent","--launch"}})??throw new IOException("Could not open setup.");
                    installer.WaitForExit();return installer.ExitCode;
                });
            }
            File.Delete(Path.Combine(Folder,"handoff.json"));return 0;
        }
        catch(Exception ex){Directory.CreateDirectory(Folder);File.AppendAllText(Path.Combine(Folder,"update-error.log"),DateTimeOffset.UtcNow+" "+ex+Environment.NewLine);return 1;}
    }
}
