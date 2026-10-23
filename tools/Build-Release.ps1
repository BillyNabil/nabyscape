param([Parameter(Mandatory)][string]$Tag,[string]$Dotnet='dotnet',[string]$OutputDirectory,[Alias('CompileOnly')][switch]$SkipTests)
$ErrorActionPreference='Stop'
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Expected tag vMAJOR.MINOR.PATCH.' }
$repo=Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrEmpty($OutputDirectory)) { $OutputDirectory = Join-Path $repo 'release-output' }
[xml]$project=Get-Content -LiteralPath (Join-Path $repo 'src\NabyScape\NabyScape.csproj')
if ($Tag.Substring(1) -ne $project.Project.PropertyGroup.Version) { throw 'Tag must match project version.' }
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release output must be a new empty path; never reuse prior assets.' }
New-Item -ItemType Directory -Path $output | Out-Null
& $Dotnet clean (Join-Path $repo 'src\NabyScape\NabyScape.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
$notices=@('LICENSE','LUCIDE-LICENSE.txt','NAudio-LICENSE.txt','System.Speech-LICENSE.txt','DOTNET-LICENSE.txt','DOTNET-ThirdPartyNotices.txt')
$assets=@()
foreach($rid in @('win-x64','win-arm64')) {
    $publish=Join-Path $output "publish/$rid"
    & (Join-Path $repo 'build.ps1') -Dotnet $Dotnet -OutputDirectory $publish -Runtime $rid -SkipTests:$SkipTests
    if ($LASTEXITCODE -ne 0) { throw "Build checks failed for $rid." }
    $setup=Join-Path $output "NabyScape-$rid-Setup.exe"
    Copy-Item -LiteralPath (Join-Path $publish "NabyScape-$rid-Setup.exe") -Destination $setup
    $portable=Join-Path $output "NabyScape-$rid-Portable.zip"
    $files=@((Join-Path $publish 'NabyScape.exe'))+@($notices | ForEach-Object { Join-Path $publish $_ })
    Compress-Archive -LiteralPath $files -DestinationPath $portable
    $assets+=@($setup,$portable)
}
$noticeZip=Join-Path $output 'NabyScape-Notices.zip'
Compress-Archive -LiteralPath @($notices | ForEach-Object { Join-Path $publish $_ }) -DestinationPath $noticeZip
$assets+=$noticeZip
$assets | ForEach-Object { ((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding ascii
& (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1') -Version $Tag.Substring(1) -Setup (Join-Path $output 'NabyScape-win-x64-Setup.exe') -Arm64Setup (Join-Path $output 'NabyScape-win-arm64-Setup.exe') -OutputDirectory (Join-Path $output 'winget')
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -OutputDirectory $output
Write-Output "Release package validation passed: $output (SkipTests=$SkipTests; cross-architecture binaries are never executed)."
