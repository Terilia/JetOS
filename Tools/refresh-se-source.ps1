<#
.SYNOPSIS
  Decompile the Space Engineers Bin64 assemblies into a clean, greppable source tree.

.DESCRIPTION
  Runs ilspycmd in project mode (one .cs file per type, foldered by namespace) for every
  MANAGED assembly in the SE Bin64 directory. Output is partitioned:

    se-source/
      game/        VRage* / Sandbox* / SpaceEngineers*   (Keen code)
      thirdparty/  everything else managed               (Roslyn, NLog, ImageSharp, ...)
      _meta/manifest.csv                                 (name, version, tier, files, status)

  Skipped by default and logged in the manifest:
    - native DLLs (no CLI header -- ilspycmd can't read them)
    - *.XmlSerializers.dll (auto-generated noise)

  Re-runnable: each assembly's destination folder is wiped before it is re-decompiled.

.PARAMETER Bin
  SE Bin64 directory. Default: G:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64

.PARAMETER Out
  Output root. Default: <repo>\se-source

.PARAMETER Only
  Decompile only assemblies whose base name matches this wildcard (e.g. 'Sandbox.Game').

.PARAMETER GameOnly
  Skip third-party assemblies.

.PARAMETER IncludeSerializers
  Also decompile *.XmlSerializers.dll (off by default -- huge and useless).

.EXAMPLE
  pwsh -File Tools\refresh-se-source.ps1
  pwsh -File Tools\refresh-se-source.ps1 -Only Sandbox.Game
  pwsh -File Tools\refresh-se-source.ps1 -GameOnly
#>
[CmdletBinding()]
param(
  [string]$Bin = "G:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64",
  [string]$Out,
  [string]$Only,
  [switch]$GameOnly,
  [switch]$IncludeSerializers
)

$ErrorActionPreference = "Stop"
if (-not $Out) { $Out = Join-Path (Split-Path $PSScriptRoot -Parent) "se-source" }

$ilspy = (Get-Command ilspycmd -ErrorAction SilentlyContinue).Source
if (-not $ilspy) { throw "ilspycmd not found on PATH. Install with: dotnet tool install -g ilspycmd" }
if (-not (Test-Path $Bin)) { throw "Bin64 not found: $Bin" }

Write-Host "ilspycmd : $ilspy"
Write-Host "source   : $Bin"
Write-Host "output   : $Out"
Write-Host ""

$files = Get-ChildItem $Bin -Recurse -Include *.dll,*.exe -File | Sort-Object Name
$rows  = New-Object System.Collections.Generic.List[object]
$i = 0

foreach ($f in $files) {
  $i++
  $base = [IO.Path]::GetFileNameWithoutExtension($f.Name)

  # Classify managed vs native via the CLI header (native DLLs throw).
  $ver = ""
  try { $ver = [System.Reflection.AssemblyName]::GetAssemblyName($f.FullName).Version.ToString() }
  catch {
    $rows.Add([pscustomobject]@{ Name=$f.Name; Tier="native"; Version=""; Files=0; Status="skipped-native" })
    continue
  }

  if (-not $IncludeSerializers -and $base -like "*XmlSerializers") {
    $rows.Add([pscustomobject]@{ Name=$f.Name; Tier="serializer"; Version=$ver; Files=0; Status="skipped-serializer" })
    continue
  }
  if ($Only -and $base -notlike $Only) { continue }

  $isGame = ($base -like "VRage*" -or $base -like "Sandbox*" -or $base -like "SpaceEngineers*")
  $tier   = if ($isGame) { "game" } else { "thirdparty" }
  if ($GameOnly -and -not $isGame) {
    $rows.Add([pscustomobject]@{ Name=$f.Name; Tier="thirdparty"; Version=$ver; Files=0; Status="skipped-gameonly" })
    continue
  }

  $dest = Join-Path (Join-Path $Out $tier) $base
  if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $dest | Out-Null

  Write-Host ("[{0,3}/{1}] {2,-12} {3}" -f $i, $files.Count, $tier, $f.Name)
  & $ilspy -p -o $dest $f.FullName *> $null
  $ok    = $LASTEXITCODE -eq 0
  $count = (Get-ChildItem $dest -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue | Measure-Object).Count
  $rows.Add([pscustomobject]@{
    Name=$f.Name; Tier=$tier; Version=$ver; Files=$count
    Status = $ok ? "ok" : "FAILED($LASTEXITCODE)"
  })
}

$meta = Join-Path $Out "_meta"
New-Item -ItemType Directory -Force -Path $meta | Out-Null
$rows | Sort-Object Tier, Name | Export-Csv (Join-Path $meta "manifest.csv") -NoTypeInformation -Encoding utf8

Write-Host ""
Write-Host "=== summary ==="
$rows | Group-Object Status | Sort-Object Name | ForEach-Object { "{0,-22} {1}" -f $_.Name, $_.Count } | Write-Host
$totalCs = ($rows | Measure-Object Files -Sum).Sum
Write-Host ("total .cs files       {0:N0}" -f $totalCs)
Write-Host "manifest written to   $(Join-Path $meta 'manifest.csv')"
$failed = $rows | Where-Object Status -like "FAILED*"
if ($failed) { Write-Host ""; Write-Host "FAILED assemblies:"; $failed.Name | ForEach-Object { Write-Host "  $_" } }
