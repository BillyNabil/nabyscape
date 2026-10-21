param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$RunDiagnostics
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& (Join-Path $PSScriptRoot 'tools\Test-SettingsScenes.ps1')
$islandProject = Join-Path $PSScriptRoot 'src\NabyScape\NabyScape.csproj'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
# Leave assemblies file-backed at runtime. ZIP delivery still compresses the download;
# internal bundle compression would decompress the runtime assemblies into private RAM.
& $Dotnet publish $islandProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -o $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$executable = Join-Path $outputPath 'NabyScape.exe'
Copy-Item -LiteralPath $executable -Destination (Join-Path $outputPath 'NabyScape-Setup.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $outputPath 'LICENSE') -Force
foreach ($notice in @('LUCIDE-LICENSE.txt', 'NAudio-LICENSE.txt', 'DOTNET-LICENSE.txt', 'DOTNET-ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "licenses\$notice") -Destination (Join-Path $outputPath $notice) -Force
}
$testProcess = Start-Process -FilePath $executable -ArgumentList @('--self-test', '--report', ('"' + (Join-Path $outputPath 'unit-report.txt') + '"')) -WindowStyle Hidden -PassThru -Wait
if ($testProcess.ExitCode -ne 0) { throw 'Logic tests failed.' }
if ($RunDiagnostics) {
    $diagnosticPath = Join-Path $outputPath 'diagnostics'
    $diagnosticStarted = [DateTime]::UtcNow
    $diagnosticProcess = Start-Process -FilePath $executable -ArgumentList @('--diagnostics', ('"' + $diagnosticPath + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($diagnosticProcess.ExitCode -ne 0) { throw 'Integration checks failed. Read diagnostics\integration-report.txt.' }
    $diagnosticReport = Join-Path $diagnosticPath 'integration-report.txt'
    if (!(Test-Path -LiteralPath $diagnosticReport) -or (Get-Item -LiteralPath $diagnosticReport).LastWriteTimeUtc -lt $diagnosticStarted.AddSeconds(-1)) { throw 'Diagnostics did not produce a fresh report. Close other NabyScape instances and try again.' }
}
Write-Output "Completed: $outputPath"
