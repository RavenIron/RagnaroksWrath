# Jotunn integration + headless-safe automation facts

**Applies to every project in `WubarrkCODING`.** Written 2026-08-24 while building `Let It Grow`'s
farming-automation expansion (first time this workspace added Jotunn to a project that uses the
plain `..\libs-Tools\*.dll` HintPath reference style instead of the `BepInEx.Core`-NuGet style
`DvergrAllies`/`MistsofAvalor`/`BlightedHeart` use). Everything below was verified against a real
`dotnet build` on this machine, or against `DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`
(line numbers cited where used).

---

## 1. `dotnet` is installed but NOT on PATH on this machine

`/home/rohan/.dotnet/dotnet` (SDK 8.0.424) exists and builds real BepInEx mod csproj files fine —
`which dotnet`/`dotnet --version` fail because it's simply not exported to `PATH` in this shell
environment. Every build command in this workspace's docs assumes PowerShell on Windows; on this
Linux box, invoke it by full path: `/home/rohan/.dotnet/dotnet build Foo.csproj -c Debug`.

## 2. Adding the `JotunnLib` NuGet package to a HintPath-style project breaks it

`Njord`/`Fatty`/`Let It Grow` (before this) reference `BepInEx.dll`/`0Harmony.dll`/etc. directly via
`<Reference><HintPath>..\libs-Tools\X.dll</HintPath></Reference>`, never via NuGet. Adding
`<PackageReference Include="JotunnLib" Version="2.*" />` on top of that reference style produces a
build that reports **0 errors but is actually broken**:

- `MSB3243`: "No way to resolve conflict between BepInEx, Version=5.4.23.3... and BepInEx" (same for
  0Harmony) — the NuGet package pulls in its own copies of BepInEx/0Harmony that collide with the
  HintPath ones, and MSBuild picks one arbitrarily rather than failing loudly.
- `MSB3245`: `Could not locate the assembly "assembly_valheim_publicized"` (and `assembly_utils_publicized`,
  `assembly_guiutils_publicized`, `gui_framework_publicized`, `SoftReferenceableAssets_publicized`,
  `HarmonyXInterop`, `Mono.Cecil`, `MonoMod.Utils`, `UnityEngine.ProfilerModule`, `BepInEx.Preloader`,
  and several more) — the NuGet package assumes the `BepInEx.Core` + `BepInEx.AssemblyPublicizer.MSBuild`
  + `UnityEngine.Modules` NuGet-driven project shape (which auto-generates `_publicized` copies with
  specific names) that `DvergrAllies.csproj` uses. It does **not** know about this project's inline
  `<Publicize>true</Publicize>` HintPath convention, and none of those assemblies exist anywhere on
  disk in this shape.

**Fix, proven working**: drop the `JotunnLib` package entirely and reference the DLL directly instead,
exactly like `MistsofAvalor.csproj` already does:
```xml
<Reference Include="Jotunn">
  <HintPath>..\libs-Tools\Jotunn.dll</HintPath>
  <Private>false</Private>
</Reference>
```
No separate `HarmonyXInterop`/`Mono.Cecil`/`MonoMod.Utils` references are needed alongside it —
whatever `Jotunn.dll` needs from those is merged/embedded inside it already (same category of thing
as `JetBrains.Annotations`, below). `[BepInDependency(Jotunn.Main.ModGuid)]` and
`[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]` (the latter
needs `using Jotunn.Utils;`, **not** `using BepInEx;` — it is a Jotunn type despite living right next
to BepInEx's own `[BepInPlugin]`) both resolve fine off the plain `Jotunn.dll` reference.

## 3. Compiling `libs-Tools\ServerSync.cs` needs more than BepInEx+0Harmony+assembly_valheim

Every project that does `<Compile Include="..\libs-Tools\ServerSync.cs" />` (Njord, Fatty, Let It
Grow) needs this **full** reference set for it to compile clean, discovered by adding references one
compiler-error at a time rather than copying Njord's list blind:

- `assembly_utils.dll` — `SyncedList` (the type of `ZNet.m_adminList`) and the
  `string.GetStableHashCode()` extension both live here, not in `assembly_valheim.dll`. (This matches
  `VALHEIM-API-REFERENCE/README.md`'s own "two things live in assembly_utils.dll, unverified" note —
  now verified: they compile clean once referenced, no reimplementation needed.)
- `Unity.TextMeshPro.dll` — `TMP_Text`, used by ServerSync's admin-UI code paths.
- `UnityEngine.UI.dll` — `MaskableGraphic`, same reason.
- The obvious ones: `BepInEx.dll`, `0Harmony.dll`, `assembly_valheim.dll` (Publicized), `UnityEngine.dll`,
  `UnityEngine.CoreModule.dll`.

`JetBrains.Annotations`' `[PublicAPI]` attribute (used throughout `ServerSync.cs`) needs **no explicit
reference at all** — it resolves transitively through one of the above (almost certainly merged into
`BepInEx.dll` or `0Harmony.dll` at build time by whoever produced those DLLs), confirmed by a clean
`--no-incremental` build with no separate `JetBrains.Annotations.dll` anywhere in `libs-Tools`.

## 4. Common UnityEngine modules a mod hits the moment it does anything beyond Harmony patches

Beyond the always-needed `UnityEngine.dll`/`UnityEngine.CoreModule.dll`:
- **`GUI`/`GUILayout`/`GUI.Window`** (any custom in-game IMGUI panel) → `UnityEngine.IMGUIModule.dll`.
- **`Physics.OverlapSphere`/`Physics.CheckSphere`/`Collider`** (any nearby-object scan) →
  `UnityEngine.PhysicsModule.dll`.
- **`Input.GetKeyDown`/`Input.GetMouseButton`** → `UnityEngine.InputLegacyModule.dll` (already noted
  in `Fatty`'s own project instructions from the Feast Ledger drag fix; re-confirmed here for a second,
  unrelated feature — this is clearly a recurring first-hit-costs-a-build-cycle gap, not a one-off).
- **`AssetBundle`/`AssetUtils.LoadAssetBundleFromResources`** → `UnityEngine.AssetBundleModule.dll`.

None of these are optional extras — a project that adds a GUI panel, a radius scan, or a hotkey and
doesn't yet have the matching module reference gets a same-shaped `CS0246`/error wall each time
(`GUILayout`/`Physics`/`Input` "does not exist in the current context"), not a subtle runtime failure.
Cheap to add all four up front to any new mod that will plausibly grow a UI or automation feature.

## 5. `Pickable.RPC_Pick` can be called directly — no `Player`/`Humanoid` needed at all

`Pickable.Interact(Humanoid character, bool repeat, bool alt)` (`assembly_valheim.decompiled.cs:59699`)
is what the "E" key calls, but it only touches `character` for two things: a tar-stuck message and a
Farming-skill XP roll (`character is Player player`). The actual harvest — spawning the item drop(s)
and marking the object picked — is entirely inside **`RPC_Pick(long sender, int bonus)`** (`:59734`),
which never reads its `sender` for anything beyond the owner check (`m_nview.IsOwner()`) already
required to call it at all, and treats `bonus` as a plain skill-bonus multiplier (0 = no bonus).

**Once `assembly_valheim` is Publicized, `RPC_Pick` is an ordinary public instance method** — call it
directly (`pickable.RPC_Pick(0L, 0)`) to auto-harvest something with **zero** `Player`/`Humanoid`
reference anywhere in the call, as long as you already own (or have claimed) the `Pickable`'s ZDO.
This matters because vanilla's own auto-harvest precedent, `Piece.OnPlaced()`'s harvest-radius sweep
(`:117597`), calls `Interact(Player.m_localPlayer, false, false)` — which is fine for that code (it
only ever runs client-side, right when a player places something) but is the **wrong template to copy
for a persistent, server-tickable automation object**, per the next fact.

## 6. `Player.m_localPlayer` is null on a true dedicated server — even with players connected

`Player.m_localPlayer` is a *client-local* concept: the character the running client instance itself
controls. A dedicated (non-listen) server process never has one of these, ever, regardless of how
many remote clients are connected — this matches the already-documented fact in `Fatty`'s own project
notes ("`Player` objects do not exist headless at all... `Player.GetAllPlayers()` walks the local
instance list") — but that phrasing undersells it: **it's not that there are zero `Player` instances
on a dedicated server with people connected; it's that `m_localPlayer` specifically stays null while
`Player.GetAllPlayers()` correctly returns the connected characters' server-side networked replicas.**

Any tick-driven automation component (a persistent `ZNetView`'d object doing periodic work via
`InvokeRepeating`, e.g. a farm scarecrow, a feeding trough, anything modeled on `AwayFromHome`'s
`KeeperFeeder`/`KeeperSupply` pattern) that needs to call an instance method genuinely scoped to
`Player` (like `PlacePiece`, which stamps a creator id) should resolve `Player.GetAllPlayers()
.FirstOrDefault()` instead of `Player.m_localPlayer`. This works identically on a listen server, a
client's own single-player game, and a true dedicated server with anyone connected; it only comes up
empty when the world is *completely* unpopulated, which is the one case where "pause this tick and
retry the next one" is the correct, honest behavior anyway (there is no `Humanoid` anywhere to act
through, full stop — this is not a workaround-able gap, it's what "the world has nobody in it" means
for any player-instance-scoped vanilla API).

## 7. [SUPERSEDED 2026-08-24] The bundled `libs-Tools/Editor/Unity` copy is a dead end — but a fresh native install is NOT

The original finding here (no exec bit, no license, no Windows player module on the *bulk-copied*
`libs-Tools/Editor/Unity` binary) is still accurate **for that specific copied binary** — don't try to
resurrect it, `chmod +x` alone won't fix the missing license. But the broader conclusion this section
originally drew — "any Unity Editor step has to happen on a licensed Windows machine" — turned out to
be **wrong** once the user set up a genuinely fresh Arch Linux machine from scratch. On a fresh box,
Unity's own official CLI tool gets you a fully working, headless, license-activated Editor entirely
natively on Linux, no Windows machine involved at all:

1. Install the CLI (`~/.local/bin/unity`, distinct from the Unity Hub GUI) and sign in:
   `unity auth login`, then `unity license activate --personal` (or whatever license the user holds).
2. `unity install <version> -y --accept-eula` — installs the actual Editor.
3. `unity install-modules -e <version> -m windows-mono -y --accept-eula` — needed *only* if a build
   script targets `BuildTarget.StandaloneWindows64` (true for both `AvalorBundleBuilder.cs` and
   `LetItGrowBundleBuilder.cs`, since both match a Windows Valheim client). List available modules
   first with `unity install-modules -e <version> -l`.
4. **The one real Arch-specific blocker**: the Editor binary fails with `error while loading shared
   libraries: libxml2.so.2: cannot open shared object file` — confirmed via
   `ldd /path/to/Editor/Unity`. Root cause: Arch's current `libxml2` package ships SONAME `.so.16`; the
   Unity Linux Editor binary was built against the older `.so.2` ABI. Fix: `sudo pacman -S
   libxml2-legacy` (this is in Arch's official `extra` repo, not the AUR — no AUR helper needed). This
   is a sudo-gated system package install a Claude Code session cannot run non-interactively (no TTY
   for the password prompt) — hand the exact command back to the user to run themselves.
5. `unity projects create <name> --path <dir> --editor-version <version> --template
   com.unity.template.3d` now works and creates a real project with `Library/`, `Packages/`,
   `ProjectSettings/` etc.
6. Add any needed packages by editing `Packages/manifest.json` directly (e.g.
   `"com.unity.cloud.gltfast": "6.19.0"` — version copied from the known-working `AwayFromHome/Unity`
   reference project's own manifest; no scoped registry needed, it's a plain Unity registry package).
7. Copy `.glb`/script assets into `Assets/`, then run headlessly with **`unity run <project> --
   -executeMethod <Class>.<Method> -logFile <path>`** — do **not** also pass `-batchmode`/`-nographics`/
   `-quit` yourself, the `run` subcommand already manages those and errors out
   ("conflicts with a reserved Unity flag managed by this command") if you do.

**First launch is slow** (package resolution + importing every asset, e.g. two ~25-30MB `.glb` files)
but exit code 0 with real log output is the normal, expected outcome — not a hang.

See §8 for a real compile bug this surfaced, and its fix — worth checking any time `BuildAssetBundles`
reports success but produces a suspiciously small/empty bundle file.

## 8. `BuildAssetBundles` silently "succeeds" with a 0-byte bundle if Player scripts fail to compile — and `com.unity.collections@2.6.6` has exactly this bug for a Release `StandaloneWindows64` target

`UnityEditor.BuildPipeline.BuildAssetBundles(...)` has to compile **Player** scripts for the target
platform before it can pack anything — even a bundle containing zero MonoBehaviours, like a bundle of
just a `Mesh` + a `Texture2D`. If that Player-script compile fails, `BuildAssetBundles` returns without
writing a real bundle file, but does **not** throw — a bundle-builder script that doesn't check the
return value (or the resulting file's size) will happily report false success. Symptom actually seen:
`EditorUtility.DisplayDialog`/`Debug.Log` from `LetItGrowBundleBuilder.BuildKit()` printed "Packed 2
meshes + 2 albedo textures... DONE" while the on-disk `letitgrow_kit` file was 0 bytes.

The actual compile failure, reproduced on Unity 6000.0.61f1 with `com.unity.collections` version
`2.6.6` (installed transitively — `com.unity.cloud.gltfast` depends on it for `Unity.Mathematics`/job
system usage) targeting a Release (non-development) `BuildTarget.StandaloneWindows64` build:

```
Library/PackageCache/com.unity.collections@.../Unity.Collections/NativeList.cs(850,24): error CS7036:
There is no argument given that corresponds to the required formal parameter 'safety' of
'NativeArray<T>.ReadOnly.ReadOnly(void*, int, ref AtomicSafetyHandle)'
```

Root cause: `NativeList.AsReadOnly()`/`AsParallelReader()` in that package version branch on
`#if ENABLE_UNITY_COLLECTIONS_CHECKS` and call a 2-arg `NativeArray<T>.ReadOnly` constructor in the
`#else` (checks-off) branch — the branch Release Standalone player builds take by default. For this
exact Editor + module combination, only the 3-arg (checks-on) constructor overload exists in the
reference assemblies the `windows-mono` support module ships, so the 2-arg call fails to resolve. This
is a genuine upstream package/Editor-module skew, not anything specific to this workspace's code — the
same failure would hit *any* project on this Editor version building a `StandaloneWindows64`
AssetBundle/Player with this collections version, on any OS.

**Fix, proven working**: force `ENABLE_UNITY_COLLECTIONS_CHECKS` onto the `Standalone` scripting-define
group for the duration of the build, via the modern (Unity 2021.2+) API, then restore whatever was
there before:
```csharp
using UnityEditor.Build;
NamedBuildTarget nbt = NamedBuildTarget.Standalone;
string prevDefines = PlayerSettings.GetScriptingDefineSymbols(nbt);
bool hadDefine = prevDefines.Split(';').Contains("ENABLE_UNITY_COLLECTIONS_CHECKS");
if (!hadDefine)
    PlayerSettings.SetScriptingDefineSymbols(nbt, string.IsNullOrEmpty(prevDefines)
        ? "ENABLE_UNITY_COLLECTIONS_CHECKS" : prevDefines + ";ENABLE_UNITY_COLLECTIONS_CHECKS");
try { manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64); }
finally { if (!hadDefine) PlayerSettings.SetScriptingDefineSymbols(nbt, prevDefines); }
```
This forces the branch with the working 3-arg overload. Combine with actually checking the return
value (`manifest == null`) and the output file's size before reporting success — both
`AvalorBundleBuilder.cs` and (now-fixed) `LetItGrowBundleBuilder.cs` should get this treatment if
either is copied forward into a new bundle-builder script.

**Unrelated but adjacent gotcha, same session**: creating a Unity project *inside* a `dotnet`
SDK-style mod project's own directory (e.g. `<mod>/Unity/`) breaks `dotnet build` for the mod —
`Unity/Library/PackageCache/**/*.cs` (Unity's own package sources, thousands of files) get swept into
the mod DLL's compile by the SDK's default recursive glob, producing a wall of CS0246/CS1069 errors
from code that has nothing to do with the mod. Fix: add `<Compile Remove="Unity\**" />` (plus matching
`EmbeddedResource Remove`/`None Remove`) to the mod's `.csproj`. Cheaper to just create the Unity
project as a sibling directory instead, if starting fresh.
