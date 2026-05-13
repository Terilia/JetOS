# build-sprites.ps1
# Walks Mod/testmod/Sources/*.svg, rasterizes each to 256x256 PNG, compresses to BC3 DDS,
# and rewrites Mod/testmod/Data/LCDTextures.sbc with one entry per sprite.
#
# Tool preference for SVG -> PNG: Inkscape > ImageMagick > Edge headless (always available).
# Tool for PNG -> DDS: Tools/texconv/texconv.exe (already in repo).

$repoRoot   = 'C:\Users\xerdi\source\repos\Terilia\JetOS'
$sourcesDir = Join-Path $repoRoot 'Mod\testmod\Sources'
$spritesDir = Join-Path $repoRoot 'Mod\testmod\Textures\Sprites'
$dataDir    = Join-Path $repoRoot 'Mod\testmod\Data'
$texconv    = Join-Path $repoRoot 'Tools\texconv\texconv.exe'

$disabledSpriteNames = @(
    'JetOS_BG_GridDot',
    'JetOS_BG_ScanLine',
    'JetOS_Glyph_Back',
    'JetOS_Glyph_Check',
    'JetOS_Icon_Ammo',
    'JetOS_Icon_Canard',
    'JetOS_Icon_Config',
    'JetOS_Icon_Fuel',
    'JetOS_Icon_Gun',
    'JetOS_Icon_HUD',
    'JetOS_Icon_Power',
    'JetOS_Icon_Radar',
    'JetOS_Icon_Terrain',
    'JetOS_Icon_Weapons',
    'JetOS_KeyHint_Box',
    'JetOS_Missile',
    'JetOS_Missile_Heat',
    'JetOS_Missile_Radar',
    'JetOS_NoSignal',
    'JetOS_PitchRung_Inverted',
    'JetOS_PitchRung_Zero',
    'JetOS_RadarSweep',
    'JetOS_StatusRing',
    'JetOS_TapeBug',
    'JetOS_Warning'
)
$disabledSprites = @{}
foreach ($name in $disabledSpriteNames) { $disabledSprites[$name] = $true }

if (-not (Test-Path $sourcesDir)) { throw "Sources dir not found: $sourcesDir" }
if (-not (Test-Path $texconv))    { throw "texconv not found: $texconv" }

# ---- Detect SVG -> PNG tool ----
# Preference: Inkscape > ImageMagick (librsvg) > Edge headless.
# Edge headless is LAST resort because both --headless and --headless=new modes
# clip ~49 rows off the bottom of every 256x256 capture (verified via PNG inspection).
# ImageMagick now ships with librsvg on this box and renders SVG correctly to the
# full canvas — that's what we want.
function Find-SvgRenderer {
    if ($cmd = Get-Command inkscape -ErrorAction SilentlyContinue) { return @{ Kind = 'inkscape'; Exe = $cmd.Source } }
    if ($cmd = Get-Command magick -ErrorAction SilentlyContinue) { return @{ Kind = 'magick'; Exe = $cmd.Source } }
    foreach ($p in @(
        "$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )) {
        if (Test-Path $p) { return @{ Kind = 'edge'; Exe = $p } }
    }
    return $null
}

$renderer = Find-SvgRenderer
if (-not $renderer) { throw "No SVG renderer found. Install Inkscape or ImageMagick." }
Write-Host "SVG renderer: $($renderer.Kind) - $($renderer.Exe)"

function Convert-SvgToPng {
    param([string]$Svg, [string]$Png, $Renderer)
    switch ($Renderer.Kind) {
        'inkscape' {
            & $Renderer.Exe --export-type=png --export-filename=$Png --export-width=256 --export-height=256 --export-background-opacity=0 $Svg | Out-Null
        }
        'magick' {
            # -size before input forces librsvg to render at the target dimensions.
            # 2>$null suppresses the harmless "UnableToOpenConfigureFile colors.xml" notices.
            & $Renderer.Exe -background none -size 256x256 $Svg $Png 2>$null | Out-Null
        }
        'edge' {
            $tmp = [IO.Path]::GetTempFileName() + '.html'
            $userDir = Join-Path $env:TEMP ("edge_headless_" + [guid]::NewGuid().ToString('N'))
            $svgContent = Get-Content $Svg -Raw
            $html = @"
<!DOCTYPE html><html><head><meta charset='utf-8'><style>html,body{margin:0;padding:0;background:transparent}svg{width:256px;height:256px;display:block}</style></head><body>$svgContent</body></html>
"@
            [System.IO.File]::WriteAllText($tmp, $html, (New-Object System.Text.UTF8Encoding $false))
            $tmpUri = ([Uri]$tmp).AbsoluteUri
            # Use legacy --headless (NOT --headless=new): the new headless includes
            # browser chrome metrics in the screenshot output, cutting ~49 rows off
            # the bottom of every 256x256 capture. Legacy mode gives a clean
            # viewport-sized screenshot.
            & $Renderer.Exe --headless --disable-gpu "--user-data-dir=$userDir" --default-background-color=00000000 --hide-scrollbars --window-size=256,256 "--screenshot=$Png" $tmpUri 2>$null | Out-Null
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            Remove-Item $userDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if (-not (Test-Path $Png)) { throw "PNG not produced for $Svg" }
}

function Convert-PngToDds {
    param([string]$Png, [string]$OutDir)
    & $texconv -f BC3_UNORM -m 1 -y -nologo -o $OutDir $Png | Out-Null
}

function Add-LcdTextureDefinition {
    param([System.Text.StringBuilder]$Builder, [string]$Name, [bool]$Commented)

    if ($Commented) {
        [void]$Builder.AppendLine("    <!-- Unused sprite: $Name")
    }

    [void]$Builder.AppendLine('    <LCDTextureDefinition>')
    [void]$Builder.AppendLine('      <Id>')
    [void]$Builder.AppendLine('        <TypeId>LCDTextureDefinition</TypeId>')
    [void]$Builder.AppendLine("        <SubtypeId>$Name</SubtypeId>")
    [void]$Builder.AppendLine('      </Id>')
    [void]$Builder.AppendLine("      <TexturePath>Textures\Sprites\$Name.dds</TexturePath>")
    [void]$Builder.AppendLine("      <SpritePath>Textures\Sprites\$Name.dds</SpritePath>")
    [void]$Builder.AppendLine('    </LCDTextureDefinition>')

    if ($Commented) {
        [void]$Builder.AppendLine('    -->')
    }

    [void]$Builder.AppendLine('')
}

# ---- Walk all SVGs ----
$svgs = Get-ChildItem $sourcesDir -Filter '*.svg' | Sort-Object Name
if (-not $svgs) { throw "No SVG sources found in $sourcesDir" }
Write-Host "Found $($svgs.Count) SVG sources."

if (-not (Test-Path $spritesDir)) { New-Item -ItemType Directory -Path $spritesDir | Out-Null }

$ok = @()
$fail = @()
foreach ($svg in $svgs) {
    $name = [IO.Path]::GetFileNameWithoutExtension($svg.Name)
    $png  = Join-Path $spritesDir "$name.png"
    $dds  = Join-Path $spritesDir "$name.dds"
    try {
        Convert-SvgToPng -Svg $svg.FullName -Png $png -Renderer $renderer
        Convert-PngToDds -Png $png -OutDir $spritesDir
        if (Test-Path $dds) {
            $ok += $name
            Write-Host ("  OK  {0,-32} -> {1,7} bytes" -f $name, (Get-Item $dds).Length)
        } else {
            throw "DDS not produced"
        }
    } catch {
        $fail += @{ Name = $name; Error = $_.Exception.Message }
        Write-Host ("  FAIL {0}: {1}" -f $name, $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Converted: $($ok.Count) ok, $($fail.Count) failed"

# ---- Regenerate LCDTextures.sbc ----
$sbcPath = Join-Path $dataDir 'LCDTextures.sbc'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0"?>')
[void]$sb.AppendLine('<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">')
[void]$sb.AppendLine('  <LCDTextures>')
[void]$sb.AppendLine('')
$activeCount = 0
$disabledCount = 0
foreach ($name in $ok) {
    $commented = $disabledSprites.ContainsKey($name)
    Add-LcdTextureDefinition -Builder $sb -Name $name -Commented $commented
    if ($commented) { $disabledCount++ } else { $activeCount++ }
}
[void]$sb.AppendLine('  </LCDTextures>')
[void]$sb.AppendLine('</Definitions>')

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($sbcPath, $sb.ToString(), $utf8NoBom)
Write-Host ""
Write-Host "Wrote $sbcPath with $activeCount active <LCDTextureDefinition> entries ($disabledCount unused commented)"

if ($fail.Count -gt 0) {
    Write-Host ""
    Write-Host "FAILURES:" -ForegroundColor Red
    $fail | ForEach-Object { Write-Host ("  $($_.Name): $($_.Error)") -ForegroundColor Red }
}
