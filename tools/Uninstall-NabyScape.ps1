param([switch]$Quiet,[switch]$KeepData,[switch]$RemoveData)
$ErrorActionPreference = 'Stop'

function Get-NabyDataRoots([string]$LocalRoot, [string]$TempRoot) {
    $local = [IO.Path]::GetFullPath($LocalRoot)
    $temp = [IO.Path]::GetFullPath($TempRoot)
    $roots = @('NabyScape','LumaIsland','NabyScape-Updates') | ForEach-Object { Join-Path $local $_ }
    # Interrupted profile migrations and speech jobs belong to this app only.
    foreach ($parent in @($local,$temp)) {
        if (Test-Path -LiteralPath $parent -PathType Container) {
            $roots += @(Get-ChildItem -LiteralPath $parent -Directory -Force | Where-Object {
                ($parent -eq $local -and $_.Name -match '^NabyScape\.migration-[a-f0-9]{32}$') -or
                ($parent -eq $temp -and $_.Name -match '^NabyScape-speech-[a-f0-9]{32}$')
            } | ForEach-Object FullName)
        }
    }
    foreach ($name in @('NabyScape','LumaIsland','BillyNabilaIsland','NabyScape-Setup','NabyScape-win-x64-Setup','NabyScape-win-arm64-Setup','NabyScape-UpdateHelper','NabyScape-Previous')) {
        $roots += Join-Path (Join-Path $temp '.net') $name
    }
    return @($roots | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique)
}

function Assert-NabyOwnedPath([string]$Path,[string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    if ($base -eq [IO.Path]::GetPathRoot($base).TrimEnd('\','/') -or
        ($full -ne $base -and -not $full.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))) {
        throw 'Cleanup path is outside its dedicated application folder.'
    }
    # Validate ancestors too: no junction may redirect a validated root elsewhere.
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Cleanup stopped because a linked folder or file was found: $cursor"
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

function New-NabyRemovalPlan([string[]]$Roots) {
    $files = New-Object 'Collections.Generic.List[object]'
    $directories = New-Object 'Collections.Generic.List[object]'
    foreach ($root in $Roots) {
        $full = Assert-NabyOwnedPath $root $root
        if (-not (Test-Path -LiteralPath $full)) { continue }
        if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Expected an application folder: $full" }
        $pending = New-Object 'Collections.Generic.Stack[string]'
        $pending.Push($full)
        while ($pending.Count -gt 0) {
            $folder = $pending.Pop()
            $null = Assert-NabyOwnedPath $folder $full
            $directories.Add([pscustomobject]@{Path=$folder;Root=$full})
            foreach ($entry in Get-ChildItem -LiteralPath $folder -Force) {
                $path = Assert-NabyOwnedPath $entry.FullName $full
                if ($entry.PSIsContainer) { $pending.Push($path) }
                else { $files.Add([pscustomobject]@{Path=$path;Root=$full}) }
            }
        }
    }
    return [pscustomobject]@{Files=$files.ToArray();Directories=@($directories.ToArray() | Sort-Object { $_.Path.Length } -Descending)}
}

function Remove-NabyPlannedData($Plan) {
    # Preflight the entire plan before the first mutation, then check each item again.
    foreach ($entry in @($Plan.Files)+@($Plan.Directories)) { $null = Assert-NabyOwnedPath $entry.Path $entry.Root }
    foreach ($entry in $Plan.Files) {
        $path = Assert-NabyOwnedPath $entry.Path $entry.Root
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    foreach ($entry in $Plan.Directories) {
        $path = Assert-NabyOwnedPath $entry.Path $entry.Root
        # A concurrent writer is an error; never recursively delete an unplanned item.
        if (Test-Path -LiteralPath $path -PathType Container) { [IO.Directory]::Delete($path,$false) }
    }
}

function Remove-NabySavedKeys {
    if (-not ('NabyScapeUninstallCredentials' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
public static class NabyScapeUninstallCredentials {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Credential {
        public uint Flags,Type; public string TargetName; public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME Written; public uint Size;
        public IntPtr Blob; public uint Persist,Count; public IntPtr Attributes; public string Alias,UserName;
    }
    [DllImport("advapi32",EntryPoint="CredEnumerateW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Enumerate(string filter,uint flags,out uint count,out IntPtr list);
    [DllImport("advapi32",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Delete(string target,uint type,uint flags);
    [DllImport("advapi32")] private static extern void CredFree(IntPtr pointer);
    public static void Remove() {
        uint count; IntPtr list;
        if(!Enumerate("NabyScape/Assistant/*",0,out count,out list)) {
            int code=Marshal.GetLastWin32Error();if(code==1168)return;throw new Win32Exception(code);
        }
        try {
            for(int i=0;i<count;i++) {
                var entry=(Credential)Marshal.PtrToStructure(Marshal.ReadIntPtr(list,i*IntPtr.Size),typeof(Credential));
                if(entry.Type!=1 || !Regex.IsMatch(entry.TargetName,@"^NabyScape/Assistant/[a-fA-F0-9]{32}$"))continue;
                if(!Delete(entry.TargetName,1,0) && Marshal.GetLastWin32Error()!=1168)throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        } finally { CredFree(list); }
    }
}
'@
    }
    [NabyScapeUninstallCredentials]::Remove()
}

function New-NabyUninstallDialog([bool]$RemoveAll) {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $form = New-Object Windows.Forms.Form
    $form.Text='Uninstall NabyScape';$form.ClientSize=New-Object Drawing.Size(560,255)
    $form.FormBorderStyle='FixedDialog';$form.StartPosition='CenterScreen';$form.MaximizeBox=$false;$form.MinimizeBox=$false
    $heading=New-Object Windows.Forms.Label;$heading.Text='Remove NabyScape from this PC?';$heading.Location=New-Object Drawing.Point(24,22);$heading.Size=New-Object Drawing.Size(515,28)
    $heading.Font=New-Object Drawing.Font('Segoe UI',14);$form.Controls.Add($heading)
    $choice=New-Object Windows.Forms.CheckBox;$choice.Text='Also remove my data and downloaded ASR / TTS models';$choice.Checked=$removeAll;$choice.Location=New-Object Drawing.Point(24,68);$choice.Size=New-Object Drawing.Size(515,32);$form.Controls.Add($choice)
    $detail=New-Object Windows.Forms.Label;$detail.Text='Includes settings, history, caches, downloaded engines and saved API keys. Imported files outside NabyScape and shared Windows components are kept.';$detail.Location=New-Object Drawing.Point(24,108);$detail.Size=New-Object Drawing.Size(510,58);$form.Controls.Add($detail)
    $confirm=New-Object Windows.Forms.Button;$confirm.Text='Uninstall';$confirm.DialogResult='OK';$confirm.Location=New-Object Drawing.Point(320,192);$confirm.Size=New-Object Drawing.Size(100,34);$form.Controls.Add($confirm)
    $cancel=New-Object Windows.Forms.Button;$cancel.Text='Cancel';$cancel.DialogResult='Cancel';$cancel.Location=New-Object Drawing.Point(432,192);$cancel.Size=New-Object Drawing.Size(100,34);$form.Controls.Add($cancel)
    $form.AcceptButton=$confirm;$form.CancelButton=$cancel
    $form.Tag=$choice
    return $form
}

function Invoke-NabyUninstall {
    if ($KeepData -and $RemoveData) { throw 'Choose either KeepData or RemoveData.' }
    $removeAll = -not $KeepData
    $islandTarget = [IO.Path]::GetFullPath($PSScriptRoot)
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NabyScape'
    $registered = (Get-ItemProperty -LiteralPath $uninstallKey -Name InstallLocation -ErrorAction Stop).InstallLocation
    if ($islandTarget -ne [IO.Path]::GetFullPath($registered)) { throw 'Invalid uninstaller location.' }
    $null = Assert-NabyOwnedPath $islandTarget $islandTarget
    if (-not $Quiet) {
        $form=New-NabyUninstallDialog $removeAll
        try { if ($form.ShowDialog() -ne 'OK') { return };$removeAll=$form.Tag.Checked } finally { $form.Dispose() }
    }
    $local=[Environment]::GetFolderPath('LocalApplicationData')
    $roots=Get-NabyDataRoots $local ([IO.Path]::GetTempPath())
    # Validate roots before stopping anything. Imported engines outside these roots are not owned.
    foreach ($root in $roots) { $null=Assert-NabyOwnedPath $root $root }
    $appExe=Join-Path $islandTarget 'NabyScape.exe'
    if (Test-Path -LiteralPath $appExe) { Start-Process -FilePath $appExe -ArgumentList '--exit' -WindowStyle Hidden -Wait }
    Start-Sleep -Milliseconds 800
    foreach ($process in Get-Process) {
        try { $processPath=$process.Path } catch { continue }
        if (-not $processPath -or $process.Id -eq $PID) { continue }
        $owned=$processPath.StartsWith($islandTarget.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)
        foreach ($root in $roots) { if ($processPath.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { $owned=$true } }
        if ($owned) {
            try { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction Stop };if(-not $process.WaitForExit(10000)){throw 'An application process did not stop. Retry after closing NabyScape.'} }
            catch { if (-not $process.HasExited) { throw } }
        }
    }
    if ($removeAll) {
        $plan=New-NabyRemovalPlan $roots
        Remove-NabySavedKeys
        Remove-NabyPlannedData $plan
    }
    $startupKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    foreach ($name in @('NabyScape','LumaIsland','BillyNabilaIsland')) {
        $value=(Get-ItemProperty -LiteralPath $startupKey -Name $name -ErrorAction SilentlyContinue).$name
        if ($value -and $value.Contains($islandTarget)) { Remove-ItemProperty -LiteralPath $startupKey -Name $name }
    }
    $programs=[Environment]::GetFolderPath('Programs')
    $shell=New-Object -ComObject WScript.Shell
    try {
        foreach ($name in @('NabyScape.lnk','Uninstall NabyScape.lnk','Luma Island.lnk','BillyNabila Island.lnk')) {
            $shortcut=Join-Path $programs $name
            if (-not (Test-Path -LiteralPath $shortcut)) { continue }
            $link=$shell.CreateShortcut($shortcut)
            try {
                $ownedExecutables=@($appExe,(Join-Path $islandTarget 'LumaIsland.exe'),(Join-Path $islandTarget 'BillyNabilaIsland.exe'))
                $ownedUninstall=$name -eq 'Uninstall NabyScape.lnk' -and $link.Arguments.Contains(('"'+(Join-Path $islandTarget 'Uninstall.ps1')+'"'))
                if ($ownedExecutables -contains $link.TargetPath -or $ownedUninstall) { Remove-Item -LiteralPath $shortcut -Force }
            }
            finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) | Out-Null }
        }
    } finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
    foreach ($name in @('NabyScape.exe','LumaIsland.exe','BillyNabilaIsland.exe','Uninstall.ps1','LICENSE','LUCIDE-LICENSE.txt','NAudio-LICENSE.txt','System.Speech-LICENSE.txt','DOTNET-LICENSE.txt','DOTNET-ThirdPartyNotices.txt')) {
        $path=Assert-NabyOwnedPath (Join-Path $islandTarget $name) $islandTarget
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    if ((Test-Path -LiteralPath $islandTarget -PathType Container) -and -not (Get-ChildItem -LiteralPath $islandTarget -Force | Select-Object -First 1)) { [IO.Directory]::Delete($islandTarget,$false) }
    Remove-Item -LiteralPath $uninstallKey -Force
    if (-not $Quiet) { [Windows.Forms.MessageBox]::Show('NabyScape has been uninstalled.','NabyScape') | Out-Null }
}

# Dot-source only exposes helpers for isolated fixture tests; no Registry or credential changes.
if ($MyInvocation.InvocationName -eq '.') { return }
try { Invoke-NabyUninstall }
catch {
    if (-not $Quiet) { Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show(('Uninstall could not finish. '+$_.Exception.Message),'NabyScape') | Out-Null }
    else { [Console]::Error.WriteLine($_.Exception.Message) }
    exit 1
}
