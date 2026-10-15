# 앱 아이콘(res\app.ico)을 생성한다. 사용법: powershell -ExecutionPolicy Bypass -File tools\make_icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot "..\res\app.ico"))

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconFrame([int]$s) {
    $u = $s / 256.0
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # 접힌 모서리가 있는 문서
    $fold = 60 * $u
    $L = 40 * $u; $T = 14 * $u; $R = 216 * $u; $B = 242 * $u
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddLine($L, $T, $R - $fold, $T)
    $page.AddLine($R - $fold, $T, $R, $T + $fold)
    $page.AddLine($R, $T + $fold, $R, $B)
    $page.AddLine($R, $B, $L, $B)
    $page.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $page)
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0x80, 0x86, 0x8B)), ([Math]::Max(1.0, 8 * $u))
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($outline, $page)

    $foldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $foldPath.AddLine($R - $fold, $T, $R - $fold, $T + $fold)
    $foldPath.AddLine($R - $fold, $T + $fold, $R, $T + $fold)
    $foldPath.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xDA, 0xDC, 0xE0))), $foldPath)
    $g.DrawPath($outline, $foldPath)

    # 본문 줄
    if ($s -ge 24) {
        $linePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0xB0, 0xB4, 0xB8)), ([Math]::Max(1.0, 10 * $u))
        foreach ($ly in 70, 98) { $g.DrawLine($linePen, 72 * $u, $ly * $u, 150 * $u, $ly * $u) }
    }

    # 빨간 PDF 띠
    $band = New-RoundedPath (18 * $u) (124 * $u) (220 * $u) (92 * $u) (14 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xD9, 0x30, 0x25))), $band)

    if ($s -ge 32) {
        $font = New-Object System.Drawing.Font "Segoe UI", ([single](64 * $u)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = [System.Drawing.StringAlignment]::Center
        $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF (18 * $u), (126 * $u), (220 * $u), (92 * $u)
        $g.DrawString("PDF", $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # 256 미만은 비트맵(DIB) 형식으로 넣는다. .NET Framework의 Icon 클래스가 작은 PNG 프레임을 못 읽는다.
        $bw = New-Object System.IO.BinaryWriter $ms
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($s * $s * 4 + $maskStride * $s))
        $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) { $frames += ,(New-IconFrame $s) }

$out = [System.IO.Path]::GetFullPath($Out)
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Close()
Write-Host "Icon written: $out"
