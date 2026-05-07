# Inspects a PNG and reports where opaque pixels are found across rows.
# Tells us if the bottom of the canvas was cut off during render.

param(
    [string]$Png = 'C:\Users\xerdi\source\repos\Terilia\JetOS\Mod\testmod\Textures\Sprites\JetOS_Glyph_Cross.png'
)

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($Png)
$bmp = New-Object System.Drawing.Bitmap($img)
Write-Host "File: $Png"
Write-Host "Size: $($img.Width) x $($img.Height)"

$rect = New-Object System.Drawing.Rectangle 0,0,$bmp.Width,$bmp.Height
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $data.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
$bmp.UnlockBits($data)

$first = -1; $last = -1
$rowMaxAlpha = New-Object int[] $bmp.Height
for ($y = 0; $y -lt $bmp.Height; $y++) {
    $maxA = 0
    $rowStart = $y * $stride
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        # BGRA
        $a = $buf[$rowStart + $x*4 + 3]
        if ($a -gt $maxA) { $maxA = $a }
    }
    $rowMaxAlpha[$y] = $maxA
    if ($maxA -gt 8) {
        if ($first -lt 0) { $first = $y }
        $last = $y
    }
}

Write-Host ""
Write-Host "First non-transparent row (top of visible content): $first"
Write-Host "Last  non-transparent row (bottom of visible)     : $last"
Write-Host "Visible vertical span: $($last - $first + 1) of $($bmp.Height) rows"
Write-Host ""

Write-Host "Last 20 rows max-alpha (bottom of canvas):"
for ($y = [Math]::Max(0, $bmp.Height - 20); $y -lt $bmp.Height; $y++) {
    $bar = '#' * [int]($rowMaxAlpha[$y] / 8)
    Write-Host ("  row {0,3}: alpha {1,3} {2}" -f $y, $rowMaxAlpha[$y], $bar)
}

Write-Host ""
Write-Host "First 8 rows max-alpha (top of canvas):"
for ($y = 0; $y -lt 8; $y++) {
    $bar = '#' * [int]($rowMaxAlpha[$y] / 8)
    Write-Host ("  row {0,3}: alpha {1,3} {2}" -f $y, $rowMaxAlpha[$y], $bar)
}

$img.Dispose()
$bmp.Dispose()
