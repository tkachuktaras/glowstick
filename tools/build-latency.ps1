# Builds dist\LatencyTest.exe — Glowstick's latency test (see tools\LatencyTest.cs).
# Run: powershell -ExecutionPolicy Bypass -File tools\build-latency.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4) not found.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$out = Join-Path $dist 'LatencyTest.exe'

& $csc /nologo /target:exe /optimize+ /platform:x64 /codepage:65001 `
    "/out:$out" "/win32manifest:$(Join-Path $PSScriptRoot 'latency.manifest')" `
    /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'LatencyTest.cs') (Join-Path $root 'src\Native.cs')
if ($LASTEXITCODE -ne 0) { throw "csc exited with code $LASTEXITCODE" }
Write-Host "Done: $out"
