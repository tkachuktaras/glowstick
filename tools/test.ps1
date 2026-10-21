# Builds and runs the automated tests (tools\Tests.cs) — no controller or drivers needed.
# Run: powershell -ExecutionPolicy Bypass -File tools\test.ps1                 — tests
#      powershell -ExecutionPolicy Bypass -File tools\test.ps1 -NewLang xx     — template lang\xx.json with all keys
param([string]$NewLang)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$buildInfo = New-BuildInfo ''
$out = Join-Path $root 'obj\Tests.exe'
# Program.cs has its own Main — the entry point is chosen explicitly
& $csc /nologo /target:exe /platform:anycpu /codepage:65001 /main:Glowstick.Tests `
    "/out:$out" "/resource:$vigem,Nefarius.ViGEm.Client.dll" $langResources $xamlResources $dataResources $references `
    $sources $buildInfo (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc exited with code $LASTEXITCODE" }

if ($NewLang) {
    $file = Join-Path $root "lang\$NewLang.json"
    if (Test-Path $file) { throw "$file already exists" }
    & $out $root --keys $file
} else { & $out $root }
exit $LASTEXITCODE
