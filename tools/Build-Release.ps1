param([Parameter(Mandatory)][string]$Tag,[string]$Dotnet='dotnet',[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Expected tag vMAJOR.MINOR.PATCH.' }
$repo=Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrEmpty($OutputDirectory)) {
    $OutputDirectory = Join-Path $repo 'release-output'
}
[xml]$project=Get-Content -LiteralPath (Join-Path $repo 'src\NabyScape\NabyScape.csproj')
if ($Tag.Substring(1) -ne $project.Project.PropertyGroup.Version) { throw 'Tag must match project version.' }
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release output must be a new empty path; never reuse prior assets.' }
New-Item -ItemType Directory -Path $output | Out-Null
$publish=Join-Path $output 'publish'
& $Dotnet clean (Join-Path $repo 'src\NabyScape\NabyScape.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
& (Join-Path $repo 'build.ps1') -Dotnet $Dotnet -OutputDirectory $publish
if ($LASTEXITCODE -ne 0) { throw 'Build checks failed.' }
Start-Sleep -Seconds 2
$setup=Join-Path $output 'NabyScape-win-x64-Setup.exe'
Copy-Item -LiteralPath (Join-Path $publish 'NabyScape-Setup.exe') -Destination $setup
$portable=Join-Path $output 'NabyScape-win-x64-Portable.zip'
$notices=@('LICENSE','LUCIDE-LICENSE.txt','NAudio-LICENSE.txt','DOTNET-LICENSE.txt','DOTNET-ThirdPartyNotices.txt')
$files=@((Join-Path $publish 'NabyScape.exe'))+@($notices | ForEach-Object { Join-Path $publish $_ })
Compress-Archive -LiteralPath $files -DestinationPath $portable
$noticeZip=Join-Path $output 'NabyScape-Notices.zip'
Compress-Archive -LiteralPath @($notices | ForEach-Object { Join-Path $publish $_ }) -DestinationPath $noticeZip
@($setup,$portable,$noticeZip) | ForEach-Object { ((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding ascii
& (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1') -Version $Tag.Substring(1) -Setup $setup -OutputDirectory (Join-Path $output 'winget')
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -OutputDirectory $output
Write-Output "Release checks passed: $output"
