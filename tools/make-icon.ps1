param([string]$OutIco, [string]$PreviewDir)
# 정확한 시계 아이콘: 검은 둥근 사각형 + 흰 시계 테두리/바늘(10:10) + 주황 초침
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $small = $s -le 24

    # 배경: 둥근 사각형
    [float]$inset = [Math]::Max(0.5, $s * 0.03); [float]$w = $s - 2 * $inset; [float]$rad = $s * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    [float]$d = 2 * $rad
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($inset + $w - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($inset + $w - $d, $inset + $w - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $inset + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s), (C '#232323'), (C '#060606'))
    $g.FillPath($bg, $path)
    if (-not $small) {
        $edge = New-Object System.Drawing.Pen ((C '#3A3A3A')), ([Math]::Max(1, $s / 128))
        $g.DrawPath($edge, $path)
    }

    $cx = $s / 2; $cy = $s / 2
    $r = $s * ($(if ($small) { 0.34 } else { 0.32 }))
    $white = C '#F2F2F2'; $accent = C '#FFA31A'

    # 시계 테두리
    $ringW = $(if ($small) { [Math]::Max(1.3, $s * 0.08) } else { $s * 0.045 })
    $ring = New-Object System.Drawing.Pen $white, $ringW
    $g.DrawEllipse($ring, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))

    # 눈금 (큰 크기만): 12·3·6·9시는 길게, 나머지는 짧게
    if ($s -ge 48) {
        for ($i = 0; $i -lt 12; $i++) {
            $a = $i * 30 * [Math]::PI / 180
            $major = ($i % 3) -eq 0
            $len = $(if ($major) { $r * 0.16 } else { $r * 0.08 })
            $tw = $(if ($major) { $s * 0.022 } else { $s * 0.012 })
            $pen = New-Object System.Drawing.Pen ((C $(if ($major) { '#D0D0D0' } else { '#8A8A8A' }))), $tw
            $r1 = $r * 0.86; $r0 = $r1 - $len
            $g.DrawLine($pen, [float]($cx + $r0 * [Math]::Sin($a)), [float]($cy - $r0 * [Math]::Cos($a)), [float]($cx + $r1 * [Math]::Sin($a)), [float]($cy - $r1 * [Math]::Cos($a)))
        }
    }

    function Hand($deg, $len, $width, $color, $tail) {
        $a = $deg * [Math]::PI / 180
        $pen = New-Object System.Drawing.Pen $color, $width
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawLine($pen, [float]($cx - $tail * [Math]::Sin($a)), [float]($cy + $tail * [Math]::Cos($a)), [float]($cx + $len * [Math]::Sin($a)), [float]($cy - $len * [Math]::Cos($a)))
    }

    # 10:10 — 시침·분침 흰색, 초침 주황
    $handW = $(if ($small) { [Math]::Max(1.3, $s * 0.085) } else { $s * 0.05 })
    Hand 305 ($r * 0.48) $handW $white 0
    Hand 60 ($r * 0.70) ($handW * 0.8) $white 0
    if ($small) {
        Hand 165 ($r * 0.72) ([Math]::Max(1, $s * 0.06)) $accent 0
    } else {
        Hand 165 ($r * 0.82) ($s * 0.022) $accent ($r * 0.18)
        $dot = $s * 0.045
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $accent), [float]($cx - $dot), [float]($cy - $dot), [float](2 * $dot), [float](2 * $dot))
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($PreviewDir) { $bmp.Save((Join-Path $PreviewDir "icon-$s.png")) }
    $bmp.Dispose()
    $images += , @($s, $ms.ToArray())
}

# ICO 파일: 헤더 6바이트 + 항목 16바이트씩 + PNG 데이터
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $data = $img[1]
    $dim = $(if ($s -ge 256) { 0 } else { $s })
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes, sizes: $($sizes -join ', '))"
