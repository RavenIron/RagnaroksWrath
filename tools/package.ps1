# Builds the Thunderstore release zip: RavenIron-RagnaroksWrath-<version>.zip in dist\.
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

# --- assemble the flat zip Thunderstore expects ------------------------------------
$dist = "$root\dist"
$stage = "$dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $dll, "$root\manifest.json", "$root\README.md", "$root\CHANGELOG.md", "$root\icon.png" -Destination $stage

$zip = "$dist\RavenIron-RagnaroksWrath-$pluginVer.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force

Write-Host "Packaged: $zip" -ForegroundColor Green
Get-Item $zip | Select-Object Name, Length
