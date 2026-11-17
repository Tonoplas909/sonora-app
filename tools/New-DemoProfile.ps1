<#
.SYNOPSIS
  Builds a small, entirely fictional music library plus a Sonora data folder that points at it.

.DESCRIPTION
  Used for the screenshots in the README and handy for trying Sonora without touching your own profile.
  Everything is made up: invented artists and albums, generated cover art, silent audio files.
  Nothing is copied from any real recording or artwork.

  Run Sonora against the result with:
      $env:SONORA_DATA_DIR = '<Out>\data'
      .\Sonora\bin\Release\net10.0-windows10.0.19041.0\win-x64\Sonora.exe

.PARAMETER Out
  Folder to create (default: %TEMP%\sonora-demo). It is emptied first.
#>
param([string]$Out = (Join-Path $env:TEMP 'sonora-demo'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
$music = Join-Path $Out 'music'
$data  = Join-Path $Out 'data'
New-Item -ItemType Directory -Path $music, $data | Out-Null

# Artist, album, year, genre, accent colours of the cover (two hex colours), tracks.
$albums = @(
  @{ Artist = 'Nocturne Club';    Album = 'Neon Hours';        Year = 2024; Genre = 'Electro'; Colors = '#FF4F9A', '#2B0F54'; Tracks = 'Midnight Drive', 'Afterglow', 'Static Bloom', 'Velvet Rain', 'Last Train Home' },
  @{ Artist = 'Lune Rouge';       Album = 'Paper Planets';     Year = 2023; Genre = 'Indie';   Colors = '#FF8A3D', '#8A2468'; Tracks = 'Orbit Lines', 'Cardboard Moon', 'Small Satellites', 'Paper Planets' },
  @{ Artist = 'Aster & Co.';      Album = 'Golden Hour Tapes'; Year = 2022; Genre = 'Pop';     Colors = '#FFD166', '#EF476F'; Tracks = 'Sunday Light', 'Honey Static', 'Come Back Slow', 'Kodak Summer' },
  @{ Artist = 'Mira Valence';     Album = 'Slow Orbit';        Year = 2025; Genre = 'Ambient'; Colors = '#5CE1E6', '#0D0221'; Tracks = 'Drift', 'Tidal Hum', 'Glass Garden', 'Deep Field', 'Low Tide' },
  @{ Artist = 'The Hollow Pines'; Album = 'Winter Cabin';      Year = 2021; Genre = 'Folk';    Colors = '#B8C4BB', '#2F3E46'; Tracks = 'Wood Smoke', 'Frozen Lake', 'Letters in Snow', 'Pine Hours' },
  @{ Artist = 'Kodama';           Album = 'Green Machine';     Year = 2024; Genre = 'Electro'; Colors = '#C5F277', '#0B3D2E'; Tracks = 'Moss Protocol', 'Fern Circuit', 'Understory', 'Canopy Loop' },
  @{ Artist = 'Aster & Co.';      Album = 'Blue Hour Sessions';Year = 2025; Genre = 'Pop';     Colors = '#7B9DFF', '#1B1B3A'; Tracks = 'Indigo', 'Night Bus', 'Window Seat', 'Soft Reset' },
  @{ Artist = 'Lune Rouge';       Album = 'Salt & Static';     Year = 2020; Genre = 'Indie';   Colors = '#F28482', '#3D405B'; Tracks = 'Salt Air', 'Static Hearts', 'Lighthouse', 'Undertow' }
)

function ToColor([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# A square abstract cover: a diagonal gradient, a big soft disc and a few thin rings. No text, no real artwork.
function New-Cover([string]$path, [string]$c1, [string]$c2, [int]$seed) {
  $size = 600
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (ToColor $c1), (ToColor $c2), 55
  $g.FillRectangle($grad, $rect)
  $rnd = New-Object System.Random $seed
  $cx = 150 + $rnd.Next(0, 300); $cy = 150 + $rnd.Next(0, 300); $r = 130 + $rnd.Next(0, 90)
  $disc = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))
  $g.FillEllipse($disc, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
  for ($i = 1; $i -le 4; $i++) {
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60 - 10 * $i, 255, 255, 255)), 3
    $rr = $r + 38 * $i
    $g.DrawEllipse($pen, $cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr)
  }
  $shade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
  $g.FillRectangle($shade, 0, $size - 120, $size, 120)
  $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
  $params = New-Object System.Drawing.Imaging.EncoderParameters 1
  $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 88L
  $bmp.Save($path, $codec, $params)
  $g.Dispose(); $bmp.Dispose()
}

# A silent mono 8 kHz 8-bit WAV of the given length: small, valid and playable.
function New-SilentWav([string]$path, [int]$seconds) {
  $rate = 8000; $count = $rate * $seconds
  $fs = [System.IO.File]::Create($path)
  $w = New-Object System.IO.BinaryWriter $fs
  $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $count)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
  $w.Write([Text.Encoding]::ASCII.GetBytes('fmt ')); $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1)
  $w.Write([int]$rate); $w.Write([int]$rate); $w.Write([int16]1); $w.Write([int16]8)
  $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]$count)
  $w.Write([byte[]](,[byte]128 * $count))
  $w.Dispose(); $fs.Dispose()
}

$tracks = New-Object System.Collections.Generic.List[object]
$seed = 7
foreach ($a in $albums) {
  $dir = Join-Path $music (Join-Path $a.Artist $a.Album)
  New-Item -ItemType Directory -Path $dir | Out-Null
  New-Cover (Join-Path $dir 'cover.jpg') $a.Colors[0] $a.Colors[1] $seed
  $seed += 11
  $n = 0
  foreach ($title in $a.Tracks) {
    $n++
    $seconds = 150 + (($n * 37 + $seed) % 120)   # a believable 2.5 to 4.5 minutes in the library
    $file = Join-Path $dir ('{0:00} {1}.wav' -f $n, $title)
    New-SilentWav $file 90                        # the file itself is a short silence
    $fi = Get-Item $file
    # Tags are cached with the file's size and time, so the scan trusts them instead of reading the (tagless) WAV.
    $tracks.Add([ordered]@{
      Path = $fi.FullName; Title = $title; Artist = $a.Artist; Album = $a.Album; AlbumArtist = $a.Artist; Genre = $a.Genre
      TrackNumber = $n; Year = $a.Year; DurationSec = $seconds; FileTicks = $fi.LastWriteTimeUtc.Ticks; FileSize = $fi.Length
    })
  }
}

$tracks | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $data 'library.json') -Encoding UTF8
@{ Folders = @($music); ThemeId = 'liquide'; Volume = 70; Language = 'en' } | ConvertTo-Json | Set-Content (Join-Path $data 'state.json') -Encoding UTF8

Write-Host "Demo profile ready in $Out"
Write-Host "  data folder : $data   (set SONORA_DATA_DIR to it)"
Write-Host "  music folder: $music  ($($tracks.Count) tracks, $($albums.Count) albums)"
