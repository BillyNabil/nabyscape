param([string]$Repository=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$settings=Join-Path $Repository 'src\NabyScape\UI\Settings'
$scene=Get-Content -LiteralPath (Join-Path $settings 'SettingsScene.cs') -Raw
$registry=[regex]::Match($scene,'Sections=\{([^}]+)\}').Groups[1].Value
$registered=@([regex]::Matches($registry,'"([^"]+)"') | ForEach-Object {$_.Groups[1].Value})
$cases=@([regex]::Matches($scene,'case "([^"]+)":') | ForEach-Object {$_.Groups[1].Value})
$titles=@(Get-ChildItem -LiteralPath $settings -Filter '*.cs' | ForEach-Object {[regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw),'Section\("([^"]+)"') | ForEach-Object {$_.Groups[1].Value}} | Sort-Object -Unique)
foreach($title in $titles) { if ($registered -notcontains $title -or $cases -notcontains $title) { throw "Settings section has no registered scene branch: $title" } }
if (@($registered | Sort-Object -Unique).Count -ne $registered.Count) { throw 'Duplicate Settings scene registration.' }
Write-Output "PASS all $($titles.Count) actual Settings section titles have registered render branches"
