$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
# Original pixel controller: shared silhouette stays legible at taskbar sizes.
$pixels = @(
'................',
'...GGGGGGGGGG...',
'..GKKKKKKKKKKG..',
'.GKKKKKKKKKKKKG.',
'.GKKKKKKKKKKKKG.',
'.GKGGGKKKKKPKKG.',
'.GKKGKKKKKPKPKG.',
'.GKGGGKKKKKPKKG.',
'.GKKGKKKCCKKKKG.',
'.GKKKKKKKKKKKKG.',
'.GKKKKGGGGKKKKG.',
'.GKKKG....GKKKG.',
'..GGG......GGG..',
'................',
'..CCCCCCCCCCCC..',
'................')
$palette = @{
G = [Drawing.Color]::FromArgb(85,255,160)
K = [Drawing.Color]::FromArgb(8,9,14)
P = [Drawing.Color]::FromArgb(255,91,183)
C = [Drawing.Color]::FromArgb(66,232,255)
}
$sizes = @(16,32,48,64,128,256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $unit = $size / 16
    for ($y=0; $y -lt 16; $y++) {
        for ($x=0; $x -lt 16; $x++) {
            $key = $pixels[$y].Substring($x,1)
            if ($palette.ContainsKey($key)) {
                $brush = New-Object Drawing.SolidBrush($palette[$key])
                $graphics.FillRectangle($brush, [int]($x*$unit), [int]($y*$unit), [int]$unit, [int]$unit)
                $brush.Dispose()
            }
        }
    }
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($memory.ToArray())
    if ($size -eq 128) { $bitmap.Save((Join-Path $assetRoot 'TaskbarGames.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $assetRoot 'TaskbarGames.ico'))
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
