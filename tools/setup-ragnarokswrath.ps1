# Ragnarok's Wrath - project scaffolder
# Raven Iron | com.raveniron.ragnarokswrath
# Run from your project root:  .\setup-ragnarokswrath.ps1

$root = "RagnaroksWrath"

# folder : file : namespace
$files = @{
    "$root/RagnaroksWrath.cs"                     = "RavenIron.RagnaroksWrath"
    "$root/Config/ModConfig.cs"                   = "RavenIron.RagnaroksWrath.Config"

    "$root/Core/WorldTick.cs"                     = "RavenIron.RagnaroksWrath.Core"
    "$root/Core/ZoneClock.cs"                     = "RavenIron.RagnaroksWrath.Core"
    "$root/Core/ZoneKey.cs"                       = "RavenIron.RagnaroksWrath.Core"
    "$root/Core/Persistence.cs"                   = "RavenIron.RagnaroksWrath.Core"

    "$root/Net/RpcSync.cs"                        = "RavenIron.RagnaroksWrath.Net"
    "$root/Net/Messages.cs"                       = "RavenIron.RagnaroksWrath.Net"

    "$root/Systems/World/SeasonSystem.cs"         = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/WeatherSystem.cs"        = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/WindSystem.cs"           = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/BiomeStateSystem.cs"     = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/WorldStateSystem.cs"     = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/FireSystem.cs"           = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/PlagueSystem.cs"         = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/EcologySystem.cs"        = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/FarmingSystem.cs"        = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/HealthSystem.cs"         = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/ConsequenceSystem.cs"    = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/RivalrySystem.cs"        = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/World/RelicSystem.cs"          = "RavenIron.RagnaroksWrath.Systems.World"
    "$root/Systems/TitleSystem.cs"                = "RavenIron.RagnaroksWrath.Systems"

    "$root/Patches/Patch_EnvMan.cs"               = "RavenIron.RagnaroksWrath.Patches"
    "$root/Patches/Patch_ZNetScene.cs"            = "RavenIron.RagnaroksWrath.Patches"
    "$root/Patches/Patch_Nameplate.cs"            = "RavenIron.RagnaroksWrath.Patches"

    "$root/Feedback/MessageFeed.cs"               = "RavenIron.RagnaroksWrath.Feedback"

    # minimal visual-only client plugin (no HUD, no dashboard)
    "$root.Client/RagnaroksWrathClient.cs"        = "RavenIron.RagnaroksWrath.Client"
    "$root.Client/Visuals/WeatherVisuals.cs"      = "RavenIron.RagnaroksWrath.Client.Visuals"
    "$root.Client/Visuals/FireVisuals.cs"         = "RavenIron.RagnaroksWrath.Client.Visuals"
    "$root.Client/Visuals/PlagueVisuals.cs"       = "RavenIron.RagnaroksWrath.Client.Visuals"
    "$root.Client/Net/ClientReceiver.cs"          = "RavenIron.RagnaroksWrath.Client.Net"
}

foreach ($file in $files.Keys) {
    $dir = Split-Path $file -Parent
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Write-Host "Created folder: $dir"
    }

    if (Test-Path $file) {
        Write-Host "Skipped (exists): $file"
        continue
    }

    $ns = $files[$file]
    $class = [System.IO.Path]::GetFileNameWithoutExtension($file)

    $content = @"
namespace $ns
{
    public class $class
    {
        // TODO: implement $class
    }
}
"@
    Set-Content -Path $file -Value $content
    Write-Host "Created file:   $file"
}

Write-Host ""
Write-Host "Ragnarok's Wrath scaffold complete."
Write-Host "  $root/        -> server-side world simulation"
Write-Host "  $root.Client/ -> minimal visual-only client plugin"
