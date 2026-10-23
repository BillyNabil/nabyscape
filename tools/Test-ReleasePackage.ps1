param([Parameter(Mandatory)][string]$OutputDirectory,[switch]$ValidateWinGet)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($OutputDirectory)
$expected=@('NabyScape-win-x64-Setup.exe','NabyScape-win-x64-Portable.zip','NabyScape-win-arm64-Setup.exe','NabyScape-win-arm64-Portable.zip','NabyScape-Notices.zip')
$lines=@(Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS'))
if ($lines.Count -ne $expected.Count) { throw 'Unexpected checksum entries.' }
foreach($name in $expected) {
    $matching=@($lines | Where-Object { $_ -match ('^[a-f0-9]{64}  '+[regex]::Escape($name)+'$') })
    if ($matching.Count -ne 1) { throw "Invalid checksum entry for $name" }
    $actual=(Get-FileHash -LiteralPath (Join-Path $root $name) -Algorithm SHA256).Hash
    if ($actual -ne $matching[0].Substring(0,64)) { throw "Checksum mismatch for $name" }
}
Write-Output 'PASS all five release asset SHA-256 hashes independently recomputed'
function Read-PeMachine([IO.Stream]$Stream) {
    $reader=[IO.BinaryReader]::new($Stream,[Text.Encoding]::UTF8,$true)
    try {
        if ($Stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw 'Invalid executable header.' }
        $Stream.Position=0x3C
        $offset=$reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $Stream.Length-6) { throw 'Invalid PE header offset.' }
        $Stream.Position=$offset
        if ($reader.ReadUInt32() -ne 0x4550) { throw 'Missing PE signature.' }
        return $reader.ReadUInt16()
    } finally { $reader.Dispose() }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest=Get-Content -LiteralPath (Join-Path $root 'winget/BillyNabil.NabyScape.installer.yaml') -Raw
foreach($architecture in @('x64','arm64')) {
    $rid="win-$architecture"
    $machine=if($architecture -eq 'arm64'){0xAA64}else{0x8664}
    $setupPath=Join-Path $root "NabyScape-$rid-Setup.exe"
    $stream=[IO.File]::OpenRead($setupPath)
    try { if ((Read-PeMachine $stream) -ne $machine) { throw "Wrong setup architecture for $rid." } } finally { $stream.Dispose() }
    $setupHash=(Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
    $pattern='(?ms)^- Architecture: '+$architecture+'\r?\n  InstallerUrl: [^\r\n]+/NabyScape-'+$rid+'-Setup\.exe\r?\n  InstallerSha256: '+$setupHash+'\r?$'
    if ($manifest -notmatch $pattern) { throw "WinGet architecture, URL or hash differs for $rid." }
    $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root "NabyScape-$rid-Portable.zip"))
    try {
        $entry=$zip.GetEntry('NabyScape.exe')
        if (!$entry) { throw "Portable archive has no executable: $rid." }
        $memory=[IO.MemoryStream]::new()
        $content=$entry.Open()
        try {
            $content.CopyTo($memory);$memory.Position=0
            if ((Read-PeMachine $memory) -ne $machine) { throw "Wrong portable architecture for $rid." }
            $memory.Position=0;$sha=[Security.Cryptography.SHA256]::Create()
            try { $portableHash=[BitConverter]::ToString($sha.ComputeHash($memory)).Replace('-','') } finally { $sha.Dispose() }
            if ($portableHash -ne $setupHash) { throw "Setup and portable executable differ for $rid." }
        } finally { $content.Dispose();$memory.Dispose() }
    } finally { $zip.Dispose() }
    Write-Output "PASS $rid setup/portable PE architecture, executable identity and WinGet metadata"
}
foreach($name in @('NabyScape-win-x64-Portable.zip','NabyScape-win-arm64-Portable.zip','NabyScape-Notices.zip')) {
    $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $root $name))
    try {
        foreach($notice in @('LICENSE','LUCIDE-LICENSE.txt','NAudio-LICENSE.txt','System.Speech-LICENSE.txt','DOTNET-LICENSE.txt','DOTNET-ThirdPartyNotices.txt')) { if (!$zip.GetEntry($notice)) { throw "Missing $notice in $name" } }
        Write-Output "PASS $name contents and all six notices"
    } finally { $zip.Dispose() }
}
if ($ValidateWinGet) { winget validate --manifest (Join-Path $root 'winget') --disable-interactivity; if ($LASTEXITCODE -ne 0) { throw 'WinGet validation failed.' } }
