#requires -Version 7.0
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '../../mods/nico-item-manager/preview.png'),
    [Parameter(Mandatory)][string]$AssetRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$canvas = [Drawing.Bitmap]::new(960, 540)
$graphics = [Drawing.Graphics]::FromImage($canvas)
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit

function Fill-Rect([string]$Color, [int]$X, [int]$Y, [int]$W, [int]$H) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Color))
    $graphics.FillRectangle($brush, $X, $Y, $W, $H)
    $brush.Dispose()
}
function Text([string]$Value, [string]$Family, [float]$Size, [string]$Color, [float]$X, [float]$Y) {
    $font = [Drawing.Font]::new($Family, $Size, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Color))
    $graphics.DrawString($Value, $font, $brush, $X, $Y, [Drawing.StringFormat]::GenericTypographic)
    $brush.Dispose()
    $font.Dispose()
}

try {
    # The background and all six item sprites are exported from the full game.
    $background = [Drawing.Image]::FromFile((Join-Path $assetRoot 'textures/sharedassets0.assets/bazaar.png'))
    $graphics.DrawImage($background, [Drawing.Rectangle]::new(0, 0, 960, 540))
    $background.Dispose()
    $shade = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Rectangle]::new(0, 0, 960, 540),
        [Drawing.Color]::FromArgb(244, 9, 19, 26),
        [Drawing.Color]::FromArgb(170, 9, 19, 26), 0.0)
    $graphics.FillRectangle($shade, 0, 0, 960, 540)
    $shade.Dispose()

    # A ledger-like title block balances a small inventory of original sprites.
    Fill-Rect '#E4AB58' 48 68 45 5
    Text "NICO' ITEM MANAGER" 'Bahnschrift' 17 '#A8B9B5' 109 60
    Text 'ITEM' 'Impact' 100 '#050E14' 51 115
    Text 'ITEM' 'Impact' 100 '#FFF0CC' 47 109
    Text 'MANAGER' 'Impact' 90 '#050E14' 51 224
    Text 'MANAGER' 'Impact' 90 '#FFF0CC' 47 218
    Text '物品管理器' 'Microsoft YaHei' 39 '#E4AB58' 49 341
    Fill-Rect '#3C7A7B' 51 412 349 2
    Text 'CATALOG  /  SEARCH  /  EDIT' 'Bahnschrift' 15 '#A8B9B5' 51 435

    # Stepped corners and integer scaling keep the game's pixel-art silhouettes intact.
    $sprites = @('metal_ingot', 'electronic3', 'wire', 'chemical2', 'electronic2', 'book4')
    for ($i = 0; $i -lt $sprites.Count; $i++) {
        $x = 532 + ($i % 2) * 179
        $y = 73 + [Math]::Floor($i / 2) * 132
        $edge = if ($i -eq 1) { '#E4AB58' } else { '#365E64' }
        Fill-Rect '#071219' ($x + 5) ($y + 6) 160 118
        Fill-Rect $edge ($x + 4) $y 152 118
        Fill-Rect $edge $x ($y + 4) 160 110
        Fill-Rect '#14262C' ($x + 3) ($y + 7) 154 104
        Fill-Rect '#1C343A' ($x + 3) ($y + 7) 154 20
        $source = [Drawing.Image]::FromFile((Join-Path $assetRoot ('sprites/resources.assets/' + $sprites[$i] + '.png')))
        try {
            $scale = [Math]::Floor([Math]::Min(120 / $source.Width, 84 / $source.Height))
            $w = [int]($source.Width * $scale)
            $h = [int]($source.Height * $scale)
            $destination = [Drawing.Rectangle]::new([int]($x + (160 - $w) / 2), [int]($y + 28 + (84 - $h) / 2), $w, $h)
            $graphics.DrawImage($source, $destination, 0, 0, $source.Width, $source.Height, [Drawing.GraphicsUnit]::Pixel)
        } finally { $source.Dispose() }
        Text ('0' + ($i + 1)) 'Consolas' 12 '#92ABA9' ($x + 12) ($y + 9)
    }
    Fill-Rect '#E4AB58' 532 490 41 3
    Fill-Rect '#365E64' 581 490 292 3
    $canvas.Save([IO.Path]::GetFullPath($OutputPath), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose()
    $canvas.Dispose()
}
Get-Item -LiteralPath $OutputPath | Select-Object FullName, Length
