param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$RunDiagnostics,
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [Alias('CompileOnly')][switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($SkipTests -and $RunDiagnostics) { throw 'CompileOnly/SkipTests cannot be combined with RunDiagnostics.' }
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$canExecute = $Runtime -eq "win-$hostArchitecture"
if ($RunDiagnostics -and !$canExecute) { throw 'Diagnostics require a host matching the target architecture.' }
if (!$SkipTests) { & (Join-Path $PSScriptRoot 'tools\Test-SettingsScenes.ps1') }
if (!$SkipTests -and $canExecute) { & (Join-Path $PSScriptRoot 'tools\Test-UninstallPlan.ps1') }
$islandProject = Join-Path $PSScriptRoot 'src\NabyScape\NabyScape.csproj'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
# Leave assemblies file-backed at runtime. ZIP delivery still compresses the download;
# internal bundle compression would decompress the runtime assemblies into private RAM.
& $Dotnet publish $islandProject -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -o $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$executable = Join-Path $outputPath 'NabyScape.exe'
$peStream=[IO.File]::OpenRead($executable)
$peReader=[IO.BinaryReader]::new($peStream)
try {
    if ($peStream.Length -lt 64 -or $peReader.ReadUInt16() -ne 0x5A4D) { throw 'Invalid published executable.' }
    $peStream.Position=0x3C;$peOffset=$peReader.ReadInt32()
    if ($peOffset -lt 64 -or $peOffset -gt $peStream.Length-6) { throw 'Invalid published PE header.' }
    $peStream.Position=$peOffset
    $expectedMachine=if($Runtime -eq 'win-arm64'){0xAA64}else{0x8664}
    if ($peReader.ReadUInt32() -ne 0x4550 -or $peReader.ReadUInt16() -ne $expectedMachine) { throw "Published executable does not match $Runtime." }
} finally { $peReader.Dispose();$peStream.Dispose() }
Copy-Item -LiteralPath $executable -Destination (Join-Path $outputPath "NabyScape-$Runtime-Setup.exe") -Force
# Retain the local build alias; official release assets always use the RID-qualified name.
Copy-Item -LiteralPath $executable -Destination (Join-Path $outputPath 'NabyScape-Setup.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $outputPath 'LICENSE') -Force
foreach ($notice in @('LUCIDE-LICENSE.txt', 'NAudio-LICENSE.txt', 'System.Speech-LICENSE.txt', 'DOTNET-LICENSE.txt', 'DOTNET-ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "licenses\$notice") -Destination (Join-Path $outputPath $notice) -Force
}
if (!$SkipTests -and $canExecute) {
    $testProcess = Start-Process -FilePath $executable -ArgumentList @('--self-test', '--report', ('"' + (Join-Path $outputPath 'unit-report.txt') + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($testProcess.ExitCode -ne 0) { throw 'Logic tests failed.' }
} else { Write-Output "Runtime checks not executed for $Runtime (SkipTests=$SkipTests; native host=$hostArchitecture)." }
if ($RunDiagnostics) {
    $diagnosticPath = Join-Path $outputPath 'diagnostics'
    $diagnosticStarted = [DateTime]::UtcNow
    $diagnosticProcess = Start-Process -FilePath $executable -ArgumentList @('--diagnostics', ('"' + $diagnosticPath + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($diagnosticProcess.ExitCode -ne 0) { throw 'Integration checks failed. Read diagnostics\integration-report.txt.' }
    $diagnosticReport = Join-Path $diagnosticPath 'integration-report.txt'
    if (!(Test-Path -LiteralPath $diagnosticReport) -or (Get-Item -LiteralPath $diagnosticReport).LastWriteTimeUtc -lt $diagnosticStarted.AddSeconds(-1)) { throw 'Diagnostics did not produce a fresh report. Close other NabyScape instances and try again.' }
}
Write-Output "Completed: $outputPath"
