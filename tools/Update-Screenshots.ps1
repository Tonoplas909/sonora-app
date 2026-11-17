<#
.SYNOPSIS
  Regenerates the images of the README (docs/images) from a fictional demo library.

.DESCRIPTION
  1. builds Sonora in Release,
  2. creates the demo profile (tools/New-DemoProfile.ps1),
  3. renders each view with Sonora's own diagnostics mode (--snapshot) against that profile only
     (SONORA_DATA_DIR), so nothing of your real library, account or settings can appear,
  4. downsizes them and assembles the theme gallery.

  Requires the .NET SDK and Windows PowerShell 5.1 or later. Takes a few minutes.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent $PSScriptRoot
$images = Join-Path $root 'docs\images'
$work   = Join-Path $env:TEMP 'sonora-shots'
$demo   = Join-Path $env:TEMP 'sonora-demo'
New-Item -ItemType Directory -Force -Path $images, $work | Out-Null

Write-Host 'Building (Release)...'
dotnet build (Join-Path $root 'Sonora\Sonora.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$exe = Get-ChildItem (Join-Path $root 'Sonora\bin\Release') -Filter Sonora.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName

& (Join-Path $PSScriptRoot 'New-DemoProfile.ps1') -Out $demo | Out-Null
$env:SONORA_DATA_DIR = Join-Path $demo 'data'

function Snap([string]$file, [string]$view, [string]$theme = '') {
  $args = @('--snapshot', "`"$file`"", $view)
  if ($theme) { $args += @('--theme', $theme) }
  Remove-Item $file -ErrorAction SilentlyContinue
  $p = Start-Process $exe -ArgumentList $args -PassThru
  if (-not $p.WaitForExit(120000)) { $p.Kill(); throw "Snapshot '$view' timed out." }
  if (-not (Test-Path $file)) { throw "Snapshot '$view' produced no image." }
}

# Scales an image down to a maximum width and saves it as PNG.
function Save-Scaled([string]$from, [string]$to, [int]$maxWidth) {
  $src = [System.Drawing.Image]::FromFile($from)
  try {
    $w = [Math]::Min($maxWidth, $src.Width); $h = [int]($src.Height * $w / $src.Width)
    $dst = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($src, 0, 0, $w, $h)
    $dst.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $dst.Dispose()
  } finally { $src.Dispose() }
}

Write-Host 'Main views...'
foreach ($shot in @(
  @{ Name = 'home';        View = 'home';       Width = 1320 },
  @{ Name = 'now-playing'; View = 'play';       Width = 1320 },
  @{ Name = 'fullscreen';  View = 'fullscreen'; Width = 1320 },
  @{ Name = 'appearance';  View = 'appearance'; Width = 1320 },
  @{ Name = 'mini-player'; View = 'miniplayer'; Width = 520 }
)) {
  $raw = Join-Path $work ($shot.Name + '.png')
  Snap $raw $shot.View
  Save-Scaled $raw (Join-Path $images ($shot.Name + '.png')) $shot.Width
  Write-Host "  $($shot.Name).png"
}

Write-Host 'Theme gallery...'
$themes = @(
  @{ Id = 'liquide';   Label = 'Verre liquide' }, @{ Id = 'studio';  Label = 'Studio' },    @{ Id = 'affiche'; Label = 'Affiche' },
  @{ Id = 'soul';      Label = 'Soul' },          @{ Id = 'fonction'; Label = 'Fonction' }, @{ Id = 'encre';   Label = 'Encre' },
  @{ Id = 'clair';     Label = 'Standard' },      @{ Id = 'synthwave'; Label = 'Synthwave' }, @{ Id = 'gta6';  Label = 'GTA 6' }
)
$tileW = 640; $cols = 3; $gap = 12
$tiles = foreach ($t in $themes) {
  $raw = Join-Path $work ("theme-$($t.Id).png")
  Snap $raw 'album' $t.Id
  $img = [System.Drawing.Image]::FromFile($raw)
  $h = [int]($img.Height * $tileW / $img.Width)
  $bmp = New-Object System.Drawing.Bitmap $tileW, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'ClearTypeGridFit'
  $g.DrawImage($img, 0, 0, $tileW, $h)
  # A caption: the theme's name on a translucent strip at the bottom-left.
  $font = New-Object System.Drawing.Font 'Segoe UI Semibold', 12
  $size = $g.MeasureString($t.Label, $font)
  $strip = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(185, 12, 12, 16))
  $g.FillRectangle($strip, 0, $h - $size.Height - 14, $size.Width + 26, $size.Height + 14)
  $g.DrawString($t.Label, $font, [System.Drawing.Brushes]::White, 13, $h - $size.Height - 7)
  $g.Dispose(); $img.Dispose()
  Write-Host "  $($t.Id)"
  $bmp
}
$tileH = $tiles[0].Height
$rows = [Math]::Ceiling($tiles.Count / $cols)
$sheet = New-Object System.Drawing.Bitmap ($cols * $tileW + ($cols - 1) * $gap), ($rows * $tileH + ($rows - 1) * $gap)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::Transparent)
for ($i = 0; $i -lt $tiles.Count; $i++) {
  $g.DrawImage($tiles[$i], ($i % $cols) * ($tileW + $gap), [Math]::Floor($i / $cols) * ($tileH + $gap))
}
$sheet.Save((Join-Path $images 'themes.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $sheet.Dispose(); $tiles | ForEach-Object { $_.Dispose() }

Remove-Item Env:SONORA_DATA_DIR
Write-Host "Done: $images"
Get-ChildItem $images -Filter *.png | ForEach-Object { '{0,-18} {1,6:N0} KB' -f $_.Name, ($_.Length / 1KB) }
