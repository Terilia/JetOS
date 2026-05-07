# write-svgs.ps1 — emits the 36 new sprite SVGs into Mod/testmod/Sources/.
$dst = 'C:\Users\xerdi\source\repos\Terilia\JetOS\Mod\testmod\Sources'
if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst | Out-Null }

$svgs = [ordered]@{}

$svgs['JetOS_PitchRung_Zero'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="square">
  <line x1="32" y1="128" x2="224" y2="128"/>
</svg>
'@

$svgs['JetOS_PitchRung_Inverted'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="12" stroke-linecap="square" stroke-linejoin="miter">
  <polyline points="40,108 100,128 40,148"/>
  <polyline points="216,108 156,128 216,148"/>
</svg>
'@

$svgs['JetOS_RollPointer'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="128,160 96,96 160,96"/>
</svg>
'@

$svgs['JetOS_BankArc'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="6">
  <path d="M 28,128 A 100 100 0 0 1 228,128"/>
  <line x1="128" y1="20" x2="128" y2="44"/>
  <line x1="78" y1="36" x2="86" y2="56"/>
  <line x1="178" y1="36" x2="170" y2="56"/>
  <line x1="42" y1="78" x2="58" y2="92"/>
  <line x1="214" y1="78" x2="198" y2="92"/>
</svg>
'@

$svgs['JetOS_AoABracket'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="12" stroke-linecap="square">
  <line x1="48" y1="64" x2="48" y2="192"/>
  <line x1="48" y1="64" x2="160" y2="64"/>
  <line x1="48" y1="128" x2="120" y2="128"/>
  <line x1="48" y1="192" x2="160" y2="192"/>
</svg>
'@

$svgs['JetOS_TapeBug'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="48,80 128,80 168,128 128,176 48,176"/>
</svg>
'@

$svgs['JetOS_TapeIndex'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="64,128 144,80 192,80 192,176 144,176"/>
</svg>
'@

$svgs['JetOS_GMeterFace'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="8">
  <circle cx="128" cy="128" r="92"/>
  <line x1="128" y1="36" x2="128" y2="56"/>
  <line x1="128" y1="200" x2="128" y2="220"/>
  <line x1="36" y1="128" x2="56" y2="128"/>
  <line x1="200" y1="128" x2="220" y2="128"/>
  <g stroke-width="5">
    <line x1="65" y1="65" x2="78" y2="78"/>
    <line x1="178" y1="78" x2="191" y2="65"/>
    <line x1="65" y1="191" x2="78" y2="178"/>
    <line x1="178" y1="178" x2="191" y2="191"/>
  </g>
</svg>
'@

$svgs['JetOS_GaugeNeedle'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="128,28 138,128 128,148 118,128"/>
  <circle cx="128" cy="128" r="12"/>
</svg>
'@

$svgs['JetOS_RangeRing'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="4">
  <circle cx="128" cy="128" r="120"/>
</svg>
'@

$svgs['JetOS_OwnShip'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="128,40 144,200 112,200"/>
  <polygon points="40,140 220,140 200,160 56,160"/>
  <polygon points="100,190 156,190 148,210 108,210"/>
</svg>
'@

$svgs['JetOS_LockCone'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none">
  <path d="M 128,128 L 80,28 A 110 110 0 0 1 176,28 Z" fill="#FFFFFF" fill-opacity="0.18" stroke="#FFFFFF" stroke-width="6"/>
</svg>
'@

$svgs['JetOS_RadarSweep'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-linecap="round">
  <line x1="128" y1="128" x2="128" y2="20" stroke-width="6"/>
  <path d="M 128,20 A 108 108 0 0 0 70,40" stroke-width="4" opacity="0.45"/>
  <path d="M 70,40 A 108 108 0 0 0 30,82" stroke-width="3" opacity="0.20"/>
</svg>
'@

$svgs['JetOS_Missile'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <rect x="120" y="64" width="16" height="128"/>
  <polygon points="120,64 128,32 136,64"/>
  <polygon points="100,192 120,170 120,192"/>
  <polygon points="156,192 136,170 136,192"/>
</svg>
'@

$svgs['JetOS_Bay_Empty'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="10">
  <rect x="48" y="64" width="160" height="128" rx="12"/>
</svg>
'@

$svgs['JetOS_Bay_Loaded'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF">
  <rect x="48" y="64" width="160" height="128" rx="12" stroke-width="10"/>
  <g fill="#FFFFFF" stroke="none">
    <rect x="120" y="100" width="16" height="80"/>
    <polygon points="120,100 128,84 136,100"/>
    <polygon points="106,180 120,168 120,180"/>
    <polygon points="150,180 136,168 136,180"/>
  </g>
</svg>
'@

$svgs['JetOS_FuelTank'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="10">
  <rect x="60" y="56" width="136" height="160" rx="20"/>
  <rect x="116" y="32" width="24" height="24" fill="#FFFFFF"/>
  <line x1="60" y1="120" x2="196" y2="120" stroke-width="6"/>
</svg>
'@

$svgs['JetOS_Battery'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="10">
  <rect x="48" y="80" width="160" height="96" rx="6"/>
  <rect x="208" y="108" width="20" height="40" fill="#FFFFFF"/>
  <line x1="98" y1="100" x2="98" y2="156"/>
  <line x1="148" y1="100" x2="148" y2="156"/>
</svg>
'@

$svgs['JetOS_StatusDot'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <circle cx="128" cy="128" r="60"/>
</svg>
'@

$svgs['JetOS_StatusRing'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="14">
  <circle cx="128" cy="128" r="60"/>
</svg>
'@

$svgs['JetOS_Icon_HUD'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linecap="square">
  <line x1="64" y1="128" x2="192" y2="128"/>
  <polyline points="64,96 64,64 96,64"/>
  <polyline points="160,64 192,64 192,96"/>
  <polyline points="192,160 192,192 160,192"/>
  <polyline points="96,192 64,192 64,160"/>
</svg>
'@

$svgs['JetOS_Icon_Radar'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="12" stroke-linecap="round">
  <path d="M 64,180 Q 128,80 192,180"/>
  <line x1="128" y1="180" x2="128" y2="216"/>
  <line x1="100" y1="216" x2="156" y2="216"/>
  <path d="M 90,80 Q 128,60 166,80" opacity="0.7"/>
  <path d="M 70,52 Q 128,28 186,52" opacity="0.4"/>
</svg>
'@

$svgs['JetOS_Icon_Weapons'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="14">
  <circle cx="128" cy="128" r="80"/>
  <line x1="128" y1="32" x2="128" y2="80"/>
  <line x1="128" y1="176" x2="128" y2="224"/>
  <line x1="32" y1="128" x2="80" y2="128"/>
  <line x1="176" y1="128" x2="224" y2="128"/>
</svg>
'@

$svgs['JetOS_Icon_Terrain'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="12" stroke-linecap="round">
  <path d="M 32,180 Q 80,120 128,160 Q 176,200 224,140"/>
  <path d="M 32,140 Q 80,80 128,120 Q 176,160 224,100" opacity="0.7"/>
  <path d="M 32,100 Q 80,40 128,80 Q 176,120 224,60" opacity="0.4"/>
</svg>
'@

$svgs['JetOS_Icon_Config'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF" fill-rule="evenodd">
  <path d="M 128,32 L 156,40 L 180,28 L 200,52 L 224,72 L 216,100 L 232,128 L 216,156 L 224,184 L 200,204 L 180,228 L 156,216 L 128,224 L 100,216 L 76,228 L 56,204 L 32,184 L 40,156 L 24,128 L 40,100 L 32,72 L 56,52 L 76,28 L 100,40 Z M 128,90 a 38 38 0 1 0 0.001 0 Z"/>
</svg>
'@

$svgs['JetOS_Icon_Canard'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="40,124 116,76 200,90 224,140 104,168 40,150"/>
</svg>
'@

$svgs['JetOS_Icon_Gun'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <rect x="40" y="160" width="176" height="50" rx="8"/>
  <rect x="80" y="100" width="96" height="60" rx="8"/>
  <rect x="116" y="32" width="24" height="80"/>
</svg>
'@

$svgs['JetOS_Icon_Fuel'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <path d="M 128,40 Q 80,120 80,168 Q 80,216 128,216 Q 176,216 176,168 Q 176,120 128,40 Z"/>
</svg>
'@

$svgs['JetOS_Icon_Power'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <polygon points="140,32 64,140 116,140 96,224 192,108 132,108 156,32"/>
</svg>
'@

$svgs['JetOS_Icon_Ammo'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <path d="M 96,40 Q 128,16 160,40 L 160,200 Q 160,216 144,216 L 112,216 Q 96,216 96,200 L 96,40 Z"/>
</svg>
'@

$svgs['JetOS_BG_ScanLine'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <line x1="0" y1="128" x2="256" y2="128" stroke="#FFFFFF" stroke-width="2"/>
</svg>
'@

$svgs['JetOS_BG_GridDot'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="#FFFFFF">
  <circle cx="128" cy="128" r="3"/>
</svg>
'@

$svgs['JetOS_KeyHint_Box'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="14">
  <rect x="32" y="32" width="192" height="192" rx="24"/>
</svg>
'@

$svgs['JetOS_Glyph_Check'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="22" stroke-linecap="round" stroke-linejoin="miter">
  <polyline points="48,128 104,184 208,72"/>
</svg>
'@

$svgs['JetOS_Glyph_Cross'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="22" stroke-linecap="round">
  <line x1="56" y1="56" x2="200" y2="200"/>
  <line x1="200" y1="56" x2="56" y2="200"/>
</svg>
'@

$svgs['JetOS_Glyph_Back'] = @'
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="none" stroke="#FFFFFF" stroke-width="20" stroke-linecap="round" stroke-linejoin="miter">
  <line x1="48" y1="128" x2="208" y2="128"/>
  <polyline points="112,64 48,128 112,192"/>
</svg>
'@

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
foreach ($name in $svgs.Keys) {
    $path = Join-Path $dst "$name.svg"
    [System.IO.File]::WriteAllText($path, $svgs[$name], $utf8NoBom)
}
Write-Host "Wrote $($svgs.Count) SVG files to $dst"
