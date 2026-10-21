# Draws the icon (a red-and-blue circle, as in the tray) and saves an .ico with 16..256 px frames (PNG inside).
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\app.ico'))
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'
    $m = [Math]::Max(1, [int]($s / 16)); $d = $s - 2 * $m
    $g.FillPie((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 30))), $m, $m, $d, $d, 90, 180)
    $g.FillPie((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 80, 255))), $m, $m, $d, $d, 270, 180)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
    $s = $sizes[$k]; $len = $frames[$k].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f) }
$w.Close()
Write-Host "Icon: $Out"
