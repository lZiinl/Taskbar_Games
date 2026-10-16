# 직접 정의한 16×16 픽셀 게임패드를 PNG 프레임과 다중 크기 ICO로 생성한다.
# GDI 비트맵을 정수 배율로 확대하므로 외부 이미지 도구나 원격 이미지가 필요하지 않다.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
# Original pixel controller: shared silhouette stays legible at taskbar sizes.
# 점은 투명, G/K/P/C는 아래 팔레트의 색상 키이다. 각 행 길이는 16으로 유지해야 한다.
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
# Windows 탐색기·작업표시줄·고해상도 배율에서 선택할 아이콘 크기들이다.
$sizes = @(16,32,48,64,128,256)
# 각 PNG를 먼저 메모리에 만들고 실제 바이트 길이를 알아낸 뒤 ICO 디렉터리 오프셋을 계산한다.
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    # 모든 출력 크기는 16의 배수이므로 각 원본 픽셀이 정수 크기 블록으로 확대된다.
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
    # PNG 인코딩 결과를 바이트 배열로 복사한 후 스트림과 GDI 객체를 반환한다.
    # 128px 프레임은 README에서 사용할 독립 PNG로도 저장한다.
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
    # ICO 헤더는 6바이트, 프레임별 디렉터리 엔트리는 16바이트다.
    # 첫 이미지 데이터는 모든 디렉터리 엔트리 뒤에서 시작한다.
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        # ICO의 1바이트 너비/높이 필드에서 0은 256픽셀을 의미한다.
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
    # 디렉터리에 적은 순서대로 PNG 데이터를 붙인다. BinaryWriter는 숫자를 리틀엔디언으로 기록한다.
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
