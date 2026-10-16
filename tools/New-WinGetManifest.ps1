param([Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a stable three-part version.' }
if (!(Test-Path -LiteralPath $Setup -PathType Leaf)) { throw 'A real setup file is required.' }
$hash=(Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$base="BillyNabil.NabyScape"
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.10.0.schema.json
PackageIdentifier: $base
PackageVersion: $Version
InstallerType: exe
Scope: user
InstallModes:
- silent
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
UpgradeBehavior: install
Installers:
- Architecture: x64
  InstallerUrl: https://github.com/BillyNabil/nabyscape/releases/download/v$Version/NabyScape-win-x64-Setup.exe
  InstallerSha256: $hash
ManifestType: installer
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath (Join-Path $OutputDirectory "$base.installer.yaml") -Encoding utf8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.10.0.schema.json
PackageIdentifier: $base
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath (Join-Path $OutputDirectory "$base.yaml") -Encoding utf8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.10.0.schema.json
PackageIdentifier: $base
PackageVersion: $Version
PackageLocale: en-US
Publisher: BillyNabil
PackageName: NabyScape
License: MIT
LicenseUrl: https://github.com/BillyNabil/nabyscape/blob/main/LICENSE
ShortDescription: A desktop island for music, focus, and everyday tools.
PackageUrl: https://github.com/BillyNabil/nabyscape
ManifestType: defaultLocale
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath (Join-Path $OutputDirectory "$base.locale.en-US.yaml") -Encoding utf8
# Generator invariant checks run without installing or reaching the network.
$installer=Get-Content -LiteralPath (Join-Path $OutputDirectory "$base.installer.yaml") -Raw
foreach($required in @("InstallerSha256: $hash",'Scope: user','Silent: --silent',"/v$Version/NabyScape-win-x64-Setup.exe",'Architecture: x64')) { if (!$installer.Contains($required)) { throw "Manifest invariant missing: $required" } }
