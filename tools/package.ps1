# Builds the store release zip: RavenIron-RagnaroksWrath-<version>.zip in dist.
# Goes to Hexium (hexium.gg) - the only store we publish to (owner, 2026-09-03). The LAYOUT
# is Thunderstore's package format, because that is the format Hexium consumes.
#
# Guards the one mistake a manual zip invites: the THREE places the version lives
# (Plugin const, csproj, manifest.json) drifting apart. The script refuses to package
# unless all three agree, so a release can never claim a version its own log denies.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# --- the three versions must agree -------------------------------------------------
$pluginVer   = (Select-String -Path "$root\RagnaroksWrath\RagnaroksWrath.cs" -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
$csprojVer   = (Select-String -Path "$root\RagnaroksWrath\RagnaroksWrath.csproj" -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
$manifestVer = (Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json).version_number

if (($pluginVer -ne $csprojVer) -or ($pluginVer -ne $manifestVer)) {
    Write-Host "VERSION MISMATCH - refusing to package:" -ForegroundColor Red
    Write-Host "  Plugin const : $pluginVer"
    Write-Host "  csproj       : $csprojVer"
    Write-Host "  manifest.json: $manifestVer"
    exit 1
}

# --- clean Release build -----------------------------------------------------------
dotnet build "$root\RagnaroksWrath\RagnaroksWrath.csproj" -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

$dll = "$root\RagnaroksWrath\bin\Release\RagnaroksWrath.dll"
if (-not (Test-Path $dll)) { Write-Host "No Release DLL at $dll" -ForegroundColor Red; exit 1 }

# --- assemble the zip both stores expect -------------------------------------------
# Layout and writer both corrected 2026-08-27, learned on FireFront's upload day:
# store files at the root, the DLL under plugins/ (the BepInEx layout mod managers
# map onto BepInEx/plugins — Hexium refuses a root-level DLL), and entries written
# by hand because PS 5.1's Compress-Archive builds zips Hexium's parser rejects
# ("No manifest.json found") while .NET Framework's CreateFromDirectory names
# nested entries with spec-invalid BACKSLASHES.
$dist = "$root\dist"
$stage = "$dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item "$root\manifest.json", "$root\README.md", "$root\CHANGELOG.md", "$root\icon.png" -Destination $stage
New-Item -ItemType Directory -Force -Path "$stage\plugins" | Out-Null
Copy-Item $dll -Destination "$stage\plugins"

$zip = "$dist\RavenIron-RagnaroksWrath-$pluginVer.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $_.FullName, $rel,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force

Write-Host "Packaged: $zip" -ForegroundColor Green
Get-Item $zip | Select-Object Name, Length
