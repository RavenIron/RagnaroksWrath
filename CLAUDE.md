# Ragnarok's Wrath

A Valheim world-simulation mod by **Raven Iron**. The world reacts and remembers: fire
spreads, plague takes root, seasons and storms drive real gameplay consequences, and each
zone quietly drifts based on what has happened there.

**Not** a tracker, dashboard, HUD, or companion app. Those were deliberately cut. If a task
seems to call for player-facing UI, that is a signal to re-read the scope section, not to
build UI.

---

## Commands

```powershell
.\tools\fetch-libs.ps1     # once per machine: copies game/BepInEx DLLs into libs\
.\tools\run-tests.ps1      # off-game logic tests (net10) — run before every commit
.\place-files.ps1          # sorts loose files in root into their project folders
python tools\dnread.py libs\assembly_valheim_publicized.dll ZNet EnvMan
                           # BROKEN on Skadi's box: `python` there is the Microsoft Store
                           # stub, not an interpreter. Use ilspycmd instead (below).
```

To inspect a game member — signature, accessibility, default parameter values, or the actual
method body — decompile it. `dotnet tool install -g ilspycmd`, then:

```powershell
$m = "<Valheim>\valheim_Data\Managed"
ilspycmd -r $m $m\assembly_valheim.dll -t World
```

Read the body, do not infer it from the shape of the output. Two of the three Persistence bugs
found on 2026-08-25 were "a plausible-looking value from a method I had not read".

Build in Visual Studio, or `dotnet build RagnaroksWrath\RagnaroksWrath.csproj`.

To test in-game: copy `RagnaroksWrath\bin\Debug\RagnaroksWrath.dll` into
`<Valheim>\BepInEx\plugins\`, load a world, read `<Valheim>\BepInEx\LogOutput.log`.

**Skadi's client runs through Gale**, not the Steam folder — the live plugin path is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, and the log sits
beside it. Dropping a DLL into the Steam install loads nothing and looks exactly like a broken
mod. Valheim locks the DLL while running, so close the game before copying.

**Testing on a dedicated server** needs Steam app `896660` (a separate download from the client)
and its own BepInEx — the server install ships none. Copy `winhttp.dll`, `doorstop_config.ini`,
`doorstop_libs\`, `.doorstop_version` and `BepInEx\core\` into the server root; doorstop's config
uses a relative path, so a copy from any BepInEx install works. Keep that install minimal —
only this plugin — so a failure there is unambiguously ours. Confirm it took by checking
Valheim's own `isModded:` line flips to `True`.

---

## Layout

```
RagnaroksWrath/            server plugin (net472) — all simulation
  Config/ModConfig.cs      config surface; every system has an on/off toggle
  Core/                    WorldTick, ZoneClock, ZoneKey, ZoneState, Persistence, IWorldSystem
  Feedback/MessageFeed.cs  the ONLY player-facing output channel
  Systems/World/           the simulation systems
  Patches/                 Harmony patches
RagnaroksWrath.Client/     minimal visual-only plugin (no HUD) — not yet started
tests/CoreTests/           net10 harness; compiles the REAL source against stubs
tools/                     scripts
libs/                      gitignored; populated by fetch-libs.ps1
docs/                      roadmap; docs/reference/ = engine fact sheets (read its README)
```

Reference assemblies are **publicized** (`assembly_valheim_publicized.dll`) and resolved
through a relative `libs\` path, never a hardcoded Steam path — Skadi clones this repo too.

---

## House style — non-negotiable

Each rule came from a measured production failure. Do not relitigate them in code review.

1. **Harmony: prefixes for behaviour (`Priority.Low`, honour `__runOriginal`, "no opinion"
   means `return true`) — and, amended 2026-08-25, RESULT-DECORATING postfixes at default
   priority where appending to a return value is the whole point (first use: the nameplate
   title). A decorating postfix never replaces logic and cedes every fight: whoever rewrites
   the value outright wins, and we decorate what survives.**
   The max-priority-prefix-replace + min-priority-postfix-reassert pattern is
   formally retracted: it cost ~50% of a sibling mod's entire patch-layer CPU, and
   `int.MaxValue` defeats every other mod's ordering including explicit `HarmonyBefore`. Cede
   the final say. If one specific third-party mod ever forces the issue, put it behind a
   default-off config toggle, never in the default path.

2. **No long-lived coroutines. Use a time-budgeted cursor driven from a single `Update`.**
   Every long-lived coroutine in this codebase's lineage independently grew the same bug: a
   `while (true)` whose body can `continue` past its only `yield`, hard-locking the game. It
   reached production once. `WorldTick` is the cursor; systems implement `IWorldSystem` and
   own no timers of their own.

3. **Keep cosmetics off the gameplay path.** A VFX call that throws inside a shared prefix
   aborts everything downstream. Visual work goes in its own try/catch with a `finally` that
   advances whatever state it owns.

4. **Never patch `EnvMan` environment selection. Never touch materials or textures.**
   Seasonality (RustyMods, 558K downloads, 608 dependent mods) owns that ground. Two mods
   forcing environment selection on the same client is a straight conflict where whoever
   patches last silently wins. We consume season/weather as **read-only gameplay state** —
   fire risk, plague growth, farming yield, contest escalation — and never drive visuals with
   it. When Seasonality is installed, read its global keys (`season_winter`, `season_summer`,
   `season_spring`, `season_fall`) rather than running a competing clock.

5. **Publicized assemblies are COMPILE-TIME ONLY.** At runtime the game loads the real
   assembly with original accessibility, and Mono refuses private access:
   `Method 'EnvMan.GetCurrentDay()' is inaccessible from method '...'`. **The build is clean
   and the failure appears only in-game.** Reach private members through a cached
   `AccessTools.MethodDelegate` / `AccessTools.FieldRefAccess`, resolved once and stored in a
   static. Keep retrying resolution until it succeeds rather than latching a failure; if the
   member is genuinely absent, log an error naming it, because that means Valheim's API moved.
   See `SeasonSystem.TryGetCurrentDay` for the reference implementation.

**Debugging discipline.** A silent success and a silent no-op are indistinguishable from
outside the game. When something "doesn't work", spend the round-trip on **one log line
proving the code ran at all** before spending it on another guess. When a symptom survives
several confident fixes, stop fixing and audit the instrument — a confident, well-formed,
wrong measurement is the most common cause of a long debugging session here.

---

## Locked decisions — do not revisit without asking

| Decision | Answer |
|---|---|
| HUD / companion app / dashboard | **None.** Cut deliberately. |
| Data mining (kills, deaths, playtime, gear) | **None.** That is The Raven's Call's job, a separate mod. No data connection between them. |
| Old Steve / SkaldSaga source | Folded into The Raven's Call. **Not referenced here.** Clean build. |
| `EnvMan` / textures / materials | **Never touched.** See rule 4. |
| Devastating Storms | `RandEventSystem` event with `m_forceEnvironment` **left empty** — full vanilla event (name, duration, music, spawns, banner) without overriding weather. A `StormsForceWeather` toggle, **default false**, exists for owners not running Seasonality. |
| Persistence | **World-scoped sparse file**, keyed by world uid. ZDO custom keys rejected: they attach to an *object*, drift attaches to a *coordinate*, and an anchor prefab per zone would trigger the `ZNetScene.CreateObjectsSorted` → `DestroyZDO` landmine. |
| FireSystem | **A bridge to FireFront, never a second fire sim.** FireFront (com.raveniron.firefront, same studio) owns ignition, spread, burning, VFX. RW reads its fires by reflection (`FireManager.CollectActiveFirePositions`, public since FireFront 0.17.2) and raises zone `Scorch`. Without FireFront, FireSystem is dormant. Decided 2026-08-25. |
| Client plugin | Required, but **visual-only** — renders fire/storm/plague effects the server cannot push. No HUD. |
| Timeline | Open-ended. Done when it's done. |
| Console prefix | `wrath` (e.g. `wrath status`) |
| GUID / namespace | `com.raveniron.ragnarokswrath` / `RavenIron.RagnaroksWrath` |

---

## Compatibility constraints

**Seasonality (RustyMods)** — see rule 4. Detect via
`Chainloader.PluginInfos.ContainsKey("RustyMods.Seasonality")`.
⚠️ **That GUID string is inferred, not verified.** If it is wrong, detection silently fails
and we run a second season clock alongside theirs — the exact conflict rule 4 exists to
prevent. Verify against their DLL's `BepInPlugin` attribute when convenient.

**AwayFromHome (Wubarrk)** — runs farms where no player stands, by rotating a "keeper" that
loads a site for ~180s then unloads it.
- 🚫 **Never patch `GetPlayersInZone` or `FindBaseSpawnPoint`.** AFH's "peaceful pen"
  guarantee is an *omission*, not code. Patching those makes hostiles spawn at their sites and
  their users will report it against them.
- **Never tick drift on zone load state.** The keeper rotation would make zones drift based on
  whether someone built a stone nearby. `ZoneClock`'s credit-on-contact design already handles
  this; keep it that way.
- **Stagger our ZDO sweep.** AFH rescans the full object index every 60s by default.
- ⚠️ **`FireSystem` must not burn unattended bases.** AFH's whole selling point is production
  continuing where nobody stands. Fire spreading to structures in an unloaded or keeper-held
  zone reads as griefing-by-mod. Require a real player in the zone for fire to propagate to
  player-built pieces, behind a config toggle.

**FireFront (Raven Iron)** — our own structure-fire mod; the fire simulation this mod's
FireSystem bridges to instead of competing with. The read API
(`FireManager.CollectActiveFirePositions(List<Vector3>)`, FireFront 0.17.2+) is documented in
FireFront's source as a load-bearing cross-mod contract: renaming it there silently disarms
Scorch here, and FireSystem warns every tick when the surface cannot be resolved rather than
going quietly dormant.

**SkyNet Redux** patches `EnvMan`
 and `ZoneSystem` for performance throttling — a third
overlap if it is ever installed alongside.

---

## Known traps

- **Setting a ZDO's position does not move an object.** It is a suggestion the owning machine
  overwrites next frame — and with a `Rigidbody`, continuously, all the way out of the world.
  `ZSyncTransform` makes the fall *persistent*. `ZoneSystem.GetGroundHeight` returns its own
  input on a raycast miss, so "is it underground?" is permanently false on a headless server.
  Applies the moment `FireSystem` or `ConsequenceSystem` destroys or relocates anything: move
  the live instance and `rb.position`, not just the ZDO, and re-assert for ~5s.
- **A mod adding a prefab MUST ship server-side** or `ZNetScene.CreateObjectsSorted` calls
  `DestroyZDO` on any hash it cannot resolve — silent data loss. We currently add no prefabs;
  keep it that way unless deliberately decided otherwise.
- **Valheim never releases ownership of a persistent ZDO when its owner disconnects.** An
  object a player touched then logged off from is frozen for everyone else.
- **`ZNet.UpdateNetTime` returns early at zero players**, so the world clock stops on an empty
  server. Never patch the global clock — AwayFromHome credits offline production the same way
  and unfreezing it would double-credit every furnace. `ZoneClock` keeps its own real-UTC
  ledger instead.
- **`Tameable` counts down on real frame time**, so taming keeps working while production sits
  dead on an empty server. Know whether a system is timestamp-clocked or tick-clocked before
  debugging it.
- **A cloud-saved world has NO filesystem path, and the API says so by returning `""`.**
  `Utils.GetSaveDataPath` returns an empty string for `FileSource.Auto` and `.Cloud` whenever
  Steam Cloud is enabled, because cloud saves are addressed by a *relative* path through Steam's
  cloud API. `World.GetWorldSavePath()` defaults to `Auto`, so it yields a bare `"/worlds"` —
  which reads as a broken absolute path and is actually a correct relative one. Any mod writing a
  sidecar file with plain `File.*` must pass `FileSource.Local` explicitly. Consequence, accepted
  deliberately in `Persistence`: on a cloud world the drift store stays on that machine and does
  not travel with the save.
- **Always use `InvariantCulture` for anything written to or parsed from disk.** A
  comma-decimal locale otherwise produces save files that work locally and corrupt on a
  European server owner's machine.
- **Driving a vanilla piece: copy the caller's ORDER, not just its arguments.** `Smelter`
  checks `IsItemAllowed` *before* `RemoveItem` because the RPC re-validates on arrival and
  silently drops what it rejects. Put every irreversible step after every check.
- `ZRoutedRpc.Register` tops out at **6** type parameters, `ZNetView.Register` at **4**.

---

## Current state

**Built and verified in-game:** plugin loads, config binds, `WorldTick` drives systems,
`SeasonSystem` resolves a real season from the world day (confirmed: logged `Summer` on an
established world, not the `Spring` enum default — a brand-new world legitimately logs `Spring`
at day 0, so read that line together with `resolved EnvMan.GetCurrentDay accessor` above it).
`Persistence` is covered by the paragraph below.

**Built and unit-tested (111/111):** `ZoneKey`, `ZoneClock` (credit-on-contact drift timing),
`ZoneState`, `Persistence` (world-scoped, atomic, fail-safe), `ModConfig`, `MessageFeed`.

**Verified in-game, both paths:** `Persistence`, verified 2026-08-25 at v0.1.6
on **both** paths — singleplayer (a Steam Cloud world) and a real dedicated server (a
`worlds_local` world) — with `dedicated=True` logged on the server binary, which is the one fact
no client run can establish. Store written, file on disk, read back with values intact, `.bak`
rotated, no `.tmp` orphaned. The two worlds produced two separate stores in the same directory,
so world-scoping is now demonstrated rather than only unit-tested.

**Known open bugs:** none. The wholly-corrupt-file case was fixed 2026-08-25. `File.ReadAllLines`
does not throw on binary garbage — it returns junk strings that each fail per-line parsing — so
`Load()` now also treats "the file had content, nothing parsed, and at least one line failed" as
corruption: error-level log naming the file, then quarantine to `.corrupt` so the next autosave
cannot overwrite the evidence. A header-only file stays on the quiet path (nothing failed, so
nothing is wrong) and a partially-corrupt one stays on per-line isolation. One non-obvious part of
the fix: the version header is now recognised by **content**, not by being line 1 — skipping line 1
unconditionally swallowed the only line a short binary file has, which is what hid the bug.

**Built and verified in-game (2026-08-26, v0.6.0, dedicated server):** `TitleSystem` +
`TitleStore` + `TitleSync` + the first amended-rule-1 postfix — Plaguewalker earned live by
walking into the outbreak, titles file created beside the zone store. `FarmingSystem` fully
verified headless: planted crops counted from the world save with nobody online, depletion
matching the configured rate. Nameplate RENDER still needs a second player's eyes.

**Built and verified headless (2026-08-25, v0.5.0, dedicated server):**
 `WorldStateSystem`
(initial condition Stable, burden 2.08 vs 2.1 predicted, no announcement on boot by design)
and `EcologySystem` (+0.00022 corruption in one tick with nobody online, matching the
predicted rate). `FarmingSystem` is built and registered; its sweep needs crops planted to
verify, and its growth/yield CONSUMER side waits for the client plugin's state sync.

**Built and verified in-game (2026-08-25, v0.4.0, dedicated server):**
 `PlagueSystem` +
plague growth in `BiomeDrift`. Spread, growth, cure and persistence all observed live from a
hand-seeded patient zero; every measured rate matched prediction once credit-on-contact
backlogs were accounted. Uncontacted zones neither grow nor heal — drift acts only where
players are, in both directions.

**Built and verified in-game (2026-08-25, v0.3.0 + FireFront 0.17.2, dedicated server):**

`FireSystem`, end to end with a player-lit fire: client patch -> RPC forward -> server sim ->
reflection bridge -> scorch in four zones (rate matched prediction to four decimals) ->
survived a server restart -> recovered via BiomeDrift once the fires were gone. FireFront must
be installed client-side too: ignition damage processes on the piece's owner.

**Built and verified in-game (2026-08-25, v0.2.2, dedicated server):**
 `WeatherSystem` +
`WindSystem` (+ `StormArea`, `WindState`). A scheduler-fired Devastating Storm was seen by a
player: announcement and banner on screen, `fireRisk x1.80, plagueSpread x1.50, wind x2.00` at
the storm centre returning to x1.00 when it lifted, and the sky logged `'Clear'` at every
transition across three storms — rule 4 held by evidence, not assertion. One unplanned case
passed too: vanilla saves the active event into the world file, and a storm resumed across a
server restart was picked up correctly because storm liveness reads vanilla's own event state
rather than a parallel timer.

**Built and verified in-game (2026-08-25, v0.1.8, dedicated server):** `BiomeStateSystem` +

`BiomeDrift`. Contact detection reads live character ZDOs — `drifted 9 of 9 contacted zone(s)`
for a contact radius of 1, the 9 zones landing in the store as a 3x3 block around the player,
frost climbing 0.0024 to 0.0117 across two saves. Measured 0.280/h against a predicted 0.28/h.

**Not started:** every other system, all Harmony patches, the entire client plugin.


See `docs/BACKLOG.md` for what to build next and in what order.

---

## Working agreement

- **Run `.\tools\run-tests.ps1` before every commit.** It is two seconds and it covers exactly
  the logic that fails silently.
- **Add tests for anything with serialization, parsing, or drift math.** The harness compiles
  the *shipping* source, not a copy — a harness that duplicates logic proves nothing and
  drifts.
- **At least one serialization test must round-trip through the SHIPPING writer.** Every
  `Persistence` fixture was hand-built with `File.WriteAllLines`, which emits no BOM, while the
  writer used `Encoding.UTF8`, which does. The tests agreed with each other and disagreed with
  the file on disk for as long as they existed. Assert on bytes the mod actually wrote.
- **A rounding floor near a per-tick delta does not slow a system, it stops it.** `BiomeDrift`
  snapped any decayed value under 5e-4 to zero, which also ate the ~3e-5 a tick of frost adds:
  each pass zeroed what the last one gained, so frost could never accumulate in any season while
  every log line looked healthy. Epsilon is a float-dust guard now (1e-6), and the regression
  tests run at the real default rates and interval so a tuning change cannot quietly restore it.
- **Prove a new test fails without its fix.**
 The BOM was first written up here as a
  quarantine bug; reverting the fix showed `File.ReadAllLines` consumes a BOM and `Load()` was
  never affected — a format defect, not a correctness one. One revert-and-rerun turned a
  confident guess into a fact, and corrected the claim before it reached the docs.
- **A clean build proves nothing about member access.** Anything reaching into game internals
  needs one in-game run before it is done.
- **Verify game APIs with `tools\dnread.py` rather than assuming.** It reports whether a method
  exists and whether it is static, without launching Valheim.
- **Ask before changing anything in the locked-decisions table.** Those were deliberate calls,
  several of them reversals of an earlier plan.
