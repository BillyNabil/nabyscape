param([string]$Repository=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Uninstall-NabyScape.ps1')
$workspace=[IO.Path]::GetFullPath($Repository)
$fixture=[IO.Path]::GetFullPath((Join-Path $workspace ('outputs/uninstall-plan-'+[Guid]::NewGuid().ToString('N'))))
$allowed=[IO.Path]::GetFullPath((Join-Path $workspace 'outputs'))+[IO.Path]::DirectorySeparatorChar
if(-not $fixture.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture location'}
Write-Output "Isolated cleanup fixture: $fixture"
$local=Join-Path $fixture 'Local';$temp=Join-Path $local 'Temp';$outside=Join-Path $fixture 'PersonalModels'
$link=$null
New-Item -ItemType Directory -Path $local,$temp,$outside -Force | Out-Null
try {
    $sentinel=Join-Path $outside 'imported-model.onnx';'personal model' | Set-Content -LiteralPath $sentinel
    $roots=Get-NabyDataRoots $local $temp
    foreach($folder in @('NabyScape/local-speech/models','LumaIsland/local-speech/voices','NabyScape-Updates/rollback')) {
        $path=Join-Path $local $folder;New-Item -ItemType Directory -Path $path -Force | Out-Null
        'downloaded by app' | Set-Content -LiteralPath (Join-Path $path 'owned.bin')
    }
    $plan=New-NabyRemovalPlan $roots
    if($plan.Files.Count -ne 3){throw 'Managed model/update roots were not included'}
    Remove-NabyPlannedData $plan
    if((Test-Path -LiteralPath (Join-Path $local 'NabyScape')) -or (Test-Path -LiteralPath (Join-Path $local 'LumaIsland')) -or (Test-Path -LiteralPath (Join-Path $local 'NabyScape-Updates'))){throw 'App-owned files remain'}
    if((Get-Content -LiteralPath $sentinel -Raw).Trim() -ne 'personal model'){throw 'Imported model was changed'}
    'PASS complete app-owned profiles/models/update cleanup preserves external imported files'

    $rejected=$false
    try{Assert-NabyOwnedPath $sentinel (Join-Path $local 'NabyScape') | Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw 'An outside path was accepted'}
    'PASS paths outside the dedicated root are rejected'

    $appRoot=Join-Path $local 'NabyScape';New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
    $preserved=Join-Path $appRoot 'settings.json';'keep until preflight passes' | Set-Content -LiteralPath $preserved
    $link=Join-Path $appRoot 'external-model-link'
    New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
    $rejected=$false
    try{New-NabyRemovalPlan (,$appRoot) | Out-Null}catch{$rejected=$true}
    if(-not $rejected -or -not (Test-Path -LiteralPath $sentinel) -or -not (Test-Path -LiteralPath $preserved)){throw 'Linked tree was not rejected before deletion'}
    'PASS junctions are rejected before any model or settings deletion'
    [IO.Directory]::Delete($link,$false);$link=$null
    Remove-NabyPlannedData (New-NabyRemovalPlan (,$appRoot))

    $migration=Join-Path $local ('NabyScape.migration-'+[Guid]::NewGuid().ToString('N'))
    $speech=Join-Path $temp ('NabyScape-speech-'+[Guid]::NewGuid().ToString('N'))
    $other=Join-Path $temp 'NabyScape-unrelated-folder'
    New-Item -ItemType Directory -Path $migration,$speech,$other -Force | Out-Null
    $roots=Get-NabyDataRoots $local $temp
    if($roots -notcontains $migration -or $roots -notcontains $speech -or $roots -contains $other){throw 'Temporary folder ownership rules are incorrect'}
    'PASS only exact migration/speech job names and app-specific runtime caches are selected'

    $tokens=$null;$parseErrors=$null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Uninstall-NabyScape.ps1'),[ref]$tokens,[ref]$parseErrors) | Out-Null
    if($parseErrors.Count){throw 'Uninstaller syntax is invalid'}
    'PASS complete uninstaller parses without running Registry or credential cleanup'
} finally {
    # Every cleanup target is under the checked absolute fixture directory.
    if($link -and (Test-Path -LiteralPath $link)){[IO.Directory]::Delete($link,$false)}
    if(Test-Path -LiteralPath $fixture){Remove-NabyPlannedData (New-NabyRemovalPlan (,$fixture))}
}
