param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$Directory,[string]$Scenarios='hidden,closed-paused,closed-playing,panel-media,settings-static,settings-moving,mini-windows',[ValidateRange(1,10)][int]$Runs=2,[ValidateRange(1,600)][int]$Seconds=15,[switch]$Cycles)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OptimizationMemory {
 [StructLayout(LayoutKind.Sequential)] struct Counters {
  public uint Size,Faults;
  public UIntPtr Peak,Total,QuotaPeakPaged,QuotaPaged,QuotaPeakNonpaged,QuotaNonpaged,Pagefile,PeakPagefile,Commit,PrivateWorking;
  public ulong SharedCommit;
 }
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool K32GetProcessMemoryInfo(IntPtr process,ref Counters counters,uint size);
 public static double[] Read(IntPtr handle) {
  var c=new Counters{Size=(uint)Marshal.SizeOf<Counters>()};
  if(!K32GetProcessMemoryInfo(handle,ref c,c.Size))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  return new[]{c.PrivateWorking.ToUInt64()/1048576d,c.Total.ToUInt64()/1048576d,c.Commit.ToUInt64()/1048576d};
 }
}
'@
$report=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Directory))
if(Test-Path -LiteralPath $report){throw 'Use a new directory for every measurement; do not mix prior progress or samples.'}
New-Item -ItemType Directory -Path $report | Out-Null
$arguments=@('--assistant-only','--performance-benchmark','--benchmark-seconds',$Seconds,'--benchmark-runs',$Runs,'--benchmark-scenarios',$Scenarios,'--diagnostics',('"'+$report+'"'))
if(!$Cycles){$arguments+='--benchmark-no-cycles'}
$process=Start-Process -FilePath ([IO.Path]::GetFullPath($Executable)) -ArgumentList $arguments -WindowStyle Hidden -PassThru
$samples=[Collections.Generic.List[object]]::new()
try {
 while(!$process.HasExited) {
  $progress=Join-Path $report 'progress.txt'
  if(Test-Path -LiteralPath $progress) {
   $last=Get-Content -LiteralPath $progress -Tail 1
   if($last -match '^START (\d+)/\d+ ([\w-]+)$') {
    try {$memory=[OptimizationMemory]::Read($process.Handle);$samples.Add([pscustomobject]@{at=[DateTimeOffset]::UtcNow;run=[int]$Matches[1];scenario=$Matches[2];privateWorkingMiB=$memory[0];totalWorkingMiB=$memory[1];privateCommitMiB=$memory[2]})}catch{if(!$process.HasExited){throw}}
   }
  }
  Start-Sleep -Seconds 1
 }
 $process.WaitForExit()
 if($process.ExitCode -ne 0){throw "Benchmark failed with exit $($process.ExitCode)"}
 ConvertTo-Json -InputObject $samples -Depth 4 | Set-Content -LiteralPath (Join-Path $report 'private-working-set-samples.json') -Encoding utf8
 Write-Output "BENCHMARK_OK $report ($($samples.Count) memory samples)"
} finally {$process.Dispose()}
