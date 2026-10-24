# Builds dist\Glowstick.exe with the C# compiler from .NET Framework 4 (present in every Windows 10/11).
# Run: powershell -ExecutionPolicy Bypass -File build.ps1
#      -Screenshots — then render the README screenshots (assets\readme\*.png) from the built exe on demo data (Showcase.cs)
# The Pro build (repo glowstick_pro) calls this script with its sources and resources added:
#   -ExtraSources, -ExtraResources (csc /resource: arguments), -Out (the exe path), -NoUpdates (no GitHub update check)
param([string[]]$ExtraSources = @(), [string[]]$ExtraResources = @(), [string]$Out = '', [switch]$NoUpdates, [switch]$Screenshots)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\common.ps1')

$ico = Join-Path $root 'assets\app.ico'

# Official driver installers — embedded in the exe (not stored in git; downloaded here and checked by SHA-256)
$redist = Join-Path $root 'redist'
New-Item -ItemType Directory -Force $redist | Out-Null
$drivers = @(
    @{ File = 'ViGEmBus_1.22.0_x64_x86_arm64.exe'; Resource = 'ViGEmBus_setup.exe'
       Url  = 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe'
       Sha  = '89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a' },
    @{ File = 'HidHide_1.5.230_x64.exe'; Resource = 'HidHide_setup.exe'
       Url  = 'https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe'
       Sha  = 'f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6' }
)
$driverResources = foreach ($d in $drivers) {
    $path = Join-Path $redist $d.File
    if (-not (Test-Path $path)) {
        Write-Host "Downloading $($d.File)…"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        (New-Object Net.WebClient).DownloadFile($d.Url, $path)
    }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $d.Sha) { Remove-Item $path; throw "SHA-256 mismatch for $($d.File): $hash" }
    "/resource:$path,$($d.Resource)"
}

$repo = if ($NoUpdates) { '' } else { Get-Repo }
$buildInfo = New-BuildInfo $repo
$out = if ($Out) { $Out } else { Join-Path (Join-Path $root 'dist') 'Glowstick.exe' }
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
# csc renames an output that is in use (a running Glowstick) and then fails — the running app would lose its exe
if (Test-Path $out) {
    try { [IO.File]::Open($out, 'Open', 'ReadWrite', 'None').Close() }
    catch { throw "$out is in use (Glowstick is running from it) — close it or build with -Out <another path>" }
}

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    "/out:$out" "/win32icon:$ico" "/win32manifest:$(Join-Path $root 'src\app.manifest')" `
    "/resource:$vigem,Nefarius.ViGEm.Client.dll" $driverResources $langResources $xamlResources $dataResources $ExtraResources $references `
    $sources $ExtraSources $buildInfo
if ($LASTEXITCODE -ne 0) { throw "csc exited with code $LASTEXITCODE" }
Write-Host "Done: $out (version $(Get-AppVersion), update repo: $(if ($repo) { $repo } else { 'none' }))"

if ($Screenshots) {
    # The hidden showcase mode: four made-up controllers, Pro unlocked (in the Pro build), nothing real touched; renders off
    # screen and exits with the number of failed shots. The README shows Pro — build it from glowstick_pro with -Screenshots.
    $shots = Join-Path $root 'assets\readme'
    $p = Start-Process $out -ArgumentList '--showcase-shots', "`"$shots`"" -PassThru -Wait
    $log = Join-Path $env:TEMP 'Glowstick-showcase\log.txt'
    if ($p.ExitCode -ne 0) { if (Test-Path $log) { Get-Content $log -Tail 30 }; throw "$($p.ExitCode) screenshot(s) failed (log: $log)" }
    Write-Host "Screenshots: $shots"
}
