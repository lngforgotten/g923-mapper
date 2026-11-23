<#
.SYNOPSIS
  Draws the app icon (a steering wheel on a blue tile) at every standard size and writes
  src\G923Mapper.App\Assets\app.ico. Run it again after changing the design; the build embeds the result.

.PARAMETER Preview
  Optional path: also saves a 512 px PNG of the icon there so it can be looked at.
#>
param([string]$Preview = "")

Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$outDir = Join-Path $root 'src\G923Mapper.App\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Tile: rounded square with a diagonal blue gradient.
    $r = [Math]::Max(2.0, $s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $m = $s * 0.03
    $w = $s - 2 * $m
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 0x4F, 0x9C, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x1D, 0x4E, 0xD8)), 45.0
    $g.FillPath($grad, $path)

    # Wheel: white ring, hub and three spokes (left, right, bottom), like a real wheel's T shape.
    $c = $s / 2.0
    $ringR = $s * 0.31
    $ring = [Math]::Max(1.6, $s * 0.085)
    $spoke = [Math]::Max(1.4, $s * 0.07)
    $white = [System.Drawing.Color]::White
    $penRing = New-Object System.Drawing.Pen $white, $ring
    $penSpoke = New-Object System.Drawing.Pen $white, $spoke
    $penSpoke.StartCap = 'Round'; $penSpoke.EndCap = 'Round'
    $g.DrawEllipse($penRing, $c - $ringR, $c - $ringR, 2 * $ringR, 2 * $ringR)
    $inner = $ringR - $ring / 2
    $g.DrawLine($penSpoke, $c - $inner, $c, $c + $inner, $c)          # left-right bar
    $g.DrawLine($penSpoke, $c, $c, $c, $c + $inner)                  # bottom spoke
    $hubR = [Math]::Max(1.8, $s * 0.085)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $white), $c - $hubR, $c - $hubR, 2 * $hubR, 2 * $hubR)

    # The centre marker at the top of the ring, which is what makes a wheel look like a racing wheel. Skipped when tiny.
    if ($s -ge 32) {
        $amber = [System.Drawing.Color]::FromArgb(255, 0xFB, 0xBF, 0x24)
        $penMark = New-Object System.Drawing.Pen $amber, ($ring * 1.05)
        $g.DrawArc($penMark, $c - $ringR, $c - $ringR, 2 * $ringR, 2 * $ringR, 258, 24)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
    $bmp.Dispose()
}

# ICO container with PNG-compressed images (supported by Windows Vista and later).
$icoPath = Join-Path $outDir 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$p.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Dispose(); $fs.Dispose()
"{0}: {1} sizes, {2:N1} KB" -f $icoPath, $pngs.Count, ((Get-Item $icoPath).Length / 1KB)

if ($Preview) {
    $big = New-IconBitmap 512
    $big.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
    # A strip of the small sizes at 1:1, to judge how it reads at 16/24/32/48 px.
    $strip = New-Object System.Drawing.Bitmap 260, 70
    $sg = [System.Drawing.Graphics]::FromImage($strip); $sg.Clear([System.Drawing.Color]::FromArgb(255, 0x20, 0x22, 0x28))
    $x = 8; foreach ($s in 16, 24, 32, 48) { $b = New-IconBitmap $s; $sg.DrawImageUnscaled($b, $x, 10); $x += $s + 16; $b.Dispose() }
    $strip.Save(($Preview -replace '\.png$', '-small.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    "preview: $Preview"
}
