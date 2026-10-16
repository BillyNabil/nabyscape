param([Parameter(Mandatory)][string]$OutputDirectory,[switch]$ValidateWinGet)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($OutputDirectory)
$expected=@('NabyScape-win-x64-Setup.exe','NabyScape-win-x64-Portable.zip','NabyScape-Notices.zip')
$lines=@(Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS'))
if ($lines.Count -ne $expected.Count) { throw 'Unexpected checksum entries.' }
foreach($name in $expected) {
    $matching=@($lines | Where-Object { $_ -match ('^[a-f0-9]{64}  '+[regex]::Escape($name)+'$') })
    if ($matching.Count -ne 1) { throw "Invalid checksum entry for $name" }
    $actual=(Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash
    if ($actual -ne $matching[0].Substring(0,64)) { throw "Checksum mismatch for $name" }
}
Write-Output 'PASS all three release asset SHA-256 hashes independently recomputed'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($name in @('NabyScape-win-x64-Portable.zip','NabyScape-Notices.zip')) {
    $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root $name))
    try {
        foreach($notice in @('LICENSE','LUCIDE-LICENSE.txt','NAudio-LICENSE.txt','DOTNET-LICENSE.txt','DOTNET-ThirdPartyNotices.txt')) { if (!$zip.GetEntry($notice)) { throw "Missing $notice in $name" } }
        if ($name.Contains('Portable') -and !$zip.GetEntry('NabyScape.exe')) { throw 'Portable archive has no executable.' }
        Write-Output "PASS $name contents and all five notices"
    } finally {$zip.Dispose()}
}
$manifest=Get-Content -LiteralPath (Join-Path $root 'winget/BillyNabil.NabyScape.installer.yaml') -Raw
$setupHash=(Get-FileHash -LiteralPath (Join-Path $root $expected[0]) -Algorithm SHA256).Hash
if (!$manifest.Contains("InstallerSha256: $setupHash")) { throw 'WinGet hash differs from final setup.' }
Write-Output 'PASS WinGet fixture uses the real setup hash'
if ($ValidateWinGet) { winget validate --manifest (Join-Path $root 'winget') --disable-interactivity; if ($LASTEXITCODE -ne 0) { throw 'WinGet validation failed.' } }
