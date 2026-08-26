# Ragnarok's Wrath — file placement
#
# Workflow: download everything into the solution root, then run this.
#   C:\Users\donfr\source\repos\RagnaroksWrath> .\place-files.ps1
#
# Creates any missing folders, moves each known file where it belongs, renames
# gitignore.txt -> .gitignore, and reports anything it does not recognise.
#
# Safe to re-run. Options:
#   -WhatIf           preview without moving anything
#   -FromDownloads    also look in %USERPROFILE%\Downloads
#
# The map below is the single thing to update when new files are added.

[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$FromDownloads
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ---------------------------------------------------------------------------
# file name -> destination folder (relative to solution root)
# Future files are listed now so this script does not need reissuing every time.
# ---------------------------------------------------------------------------
$map = [ordered]@{
    # --- server plugin ---
    'RagnaroksWrath.cs'            = 'RagnaroksWrath'
    'RagnaroksWrath.csproj'        = 'RagnaroksWrath'
    'ModConfig.cs'                 = 'RagnaroksWrath\Config'
    'IWorldSystem.cs'              = 'RagnaroksWrath\Core'
    'WorldTick.cs'                 = 'RagnaroksWrath\Core'
    'ZoneClock.cs'                 = 'RagnaroksWrath\Core'
    'ZoneKey.cs'                   = 'RagnaroksWrath\Core'
    'ZoneState.cs'                 = 'RagnaroksWrath\Core'
    'Persistence.cs'               = 'RagnaroksWrath\Core'
    'MessageFeed.cs'               = 'RagnaroksWrath\Feedback'
    'SeasonSystem.cs'              = 'RagnaroksWrath\Systems\World'
    'WeatherSystem.cs'             = 'RagnaroksWrath\Systems\World'
    'WindSystem.cs'                = 'RagnaroksWrath\Systems\World'
    'BiomeStateSystem.cs'          = 'RagnaroksWrath\Systems\World'
    'WorldStateSystem.cs'          = 'RagnaroksWrath\Systems\World'
    'FireSystem.cs'                = 'RagnaroksWrath\Systems\World'
    'PlagueSystem.cs'              = 'RagnaroksWrath\Systems\World'
    'EcologySystem.cs'             = 'RagnaroksWrath\Systems\World'
    'FarmingSystem.cs'             = 'RagnaroksWrath\Systems\World'
    'HealthSystem.cs'              = 'RagnaroksWrath\Systems\World'
    'ConsequenceSystem.cs'         = 'RagnaroksWrath\Systems\World'
    'RivalrySystem.cs'             = 'RagnaroksWrath\Systems\World'
    'RelicSystem.cs'               = 'RagnaroksWrath\Systems\World'
    'TitleSystem.cs'               = 'RagnaroksWrath\Systems'
    'Patch_EnvMan.cs'              = 'RagnaroksWrath\Patches'
    'Patch_ZNetScene.cs'           = 'RagnaroksWrath\Patches'
    'Patch_Nameplate.cs'           = 'RagnaroksWrath\Patches'

    # --- client plugin ---
    'RagnaroksWrath.Client.csproj' = 'RagnaroksWrath.Client'
    'RagnaroksWrathClient.cs'      = 'RagnaroksWrath.Client'
    'WeatherVisuals.cs'            = 'RagnaroksWrath.Client\Visuals'
    'FireVisuals.cs'               = 'RagnaroksWrath.Client\Visuals'
    'PlagueVisuals.cs'             = 'RagnaroksWrath.Client\Visuals'
    'ClientReceiver.cs'            = 'RagnaroksWrath.Client\Net'

    # --- test harness ---
    'CoreTests.csproj'             = 'tests\CoreTests'
    'Program.cs'                   = 'tests\CoreTests'
    'Stubs.cs'                     = 'tests\CoreTests'

    # --- tooling ---
    'fetch-libs.ps1'               = 'tools'
    'run-tests.ps1'                = 'tools'
    'dnread.py'                    = 'tools'
    'setup-ragnarokswrath.ps1'     = 'tools'

    # --- docs ---
    'Ragnaroks_Wrath_Roadmap.md'   = 'docs'
    'BACKLOG.md'                   = 'docs'
    'prompt-persistence-corruption-fix.md' = 'docs'
}

# Files renamed on arrival
$renames = [ordered]@{
    'gitignore.txt' = '.gitignore'
}

# ---------------------------------------------------------------------------

# Browsers append " (1)" on re-download; match those and take the newest.
function Resolve-Source([string]$name, [string]$dir) {
    if (-not (Test-Path $dir)) { return $null }

    $exact = Join-Path $dir $name
    if (Test-Path $exact) { return $exact }

    $base = [System.IO.Path]::GetFileNameWithoutExtension($name)
    $ext  = [System.IO.Path]::GetExtension($name)
    $pattern = "^$([regex]::Escape($base))(\s*\(\d+\))?$"

    $alt = Get-ChildItem -Path $dir -Filter "$base*$ext" -File -ErrorAction SilentlyContinue |
           Where-Object { $_.BaseName -match $pattern } |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1

    if ($alt) { return $alt.FullName }
    return $null
}

$searchDirs = @($root)
if ($FromDownloads) { $searchDirs += (Join-Path $env:USERPROFILE 'Downloads') }

$moved = 0
$inPlace = 0
$notFound = @()

foreach ($name in $map.Keys) {
    $src = $null
    foreach ($dir in $searchDirs) {
        $src = Resolve-Source $name $dir
        if ($src) { break }
    }

    if (-not $src) { $notFound += $name; continue }

    $destDir = Join-Path $root $map[$name]
    if (-not (Test-Path $destDir)) {
        if ($PSCmdlet.ShouldProcess($destDir, 'Create folder')) {
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        }
    }

    $dest = Join-Path $destDir $name

    if ((Resolve-Path $src).Path -eq $dest) {
        Write-Host "  =  $name" -ForegroundColor DarkGray
        $inPlace++
        continue
    }

    if ($PSCmdlet.ShouldProcess($dest, "Move $name")) {
        Move-Item -Path $src -Destination $dest -Force
        Write-Host "  -> $($map[$name])\$name" -ForegroundColor Green
        $moved++
    }
}

foreach ($name in $renames.Keys) {
    $src = $null
    foreach ($dir in $searchDirs) {
        $src = Resolve-Source $name $dir
        if ($src) { break }
    }
    if (-not $src) { continue }

    $dest = Join-Path $root $renames[$name]
    if ($PSCmdlet.ShouldProcess($dest, "Move $name -> $($renames[$name])")) {
        Move-Item -Path $src -Destination $dest -Force
        Write-Host "  -> $($renames[$name])" -ForegroundColor Green
        $moved++
    }
}

Write-Host ""
Write-Host "Moved $moved, already in place $inPlace." -ForegroundColor Cyan

# Anything left loose in root that is not recognised
$keep = @('place-files.ps1', '.gitignore', 'README.md', 'CLAUDE.md', 'RagnaroksWrath.sln')
$strays = Get-ChildItem -Path $root -File |
          Where-Object { $_.Name -notin $keep -and
                         $_.Extension -in @('.cs', '.csproj', '.ps1', '.py', '.md', '.txt') }

if ($strays) {
    Write-Host ""
    Write-Host "Unrecognised files still in root - tell Claude, or add to the map above:" -ForegroundColor Yellow
    $strays | ForEach-Object { Write-Host "  ? $($_.Name)" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  .\tools\fetch-libs.ps1    # once per machine"
Write-Host "  .\tools\run-tests.ps1     # off-game logic tests"
Write-Host "  then Build in Visual Studio"
