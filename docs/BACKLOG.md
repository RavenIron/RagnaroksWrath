# Ragnarok's Wrath — backlog

Ordered. Each task lists its acceptance criteria. Read `CLAUDE.md` first — the house style
rules and locked decisions there constrain every task below. Engine-level facts (headless
behaviour, ZDO limits, identity, vanilla-piece interop) live in `docs/reference/`; read its
README before citing any of them.

**Definition of done for every task:** `.\tools\run-tests.ps1` green, project builds, and
anything touching game internals has been run in-game once with its log line observed.

---

## 0. Verify persistence in-game — DONE 2026-08-25

Verified at v0.1.6 on **both** paths: a singleplayer host world (a Steam Cloud save) and a real
dedicated server (a `worlds_local` save). The server run is the one that counts:

```
[Info   :   BepInEx] Loading [Ragnarok's Wrath 0.1.6]
Persistence: resolved store — uid 4690126, dir C:/Users/donfr/AppData/LocalLow/IronGate/Valheim/worlds_local.
Persistence: DEBUG wrote test zone (9999,9999) and forced a save
WorldTick online — 1 system(s), budget 2ms/frame, dedicated=True
```

`dedicated=True` is the fact no client run can establish — `ZNet.IsDedicated()` is a compile-time
constant, false in the client assembly. The store was written, appeared on disk, and read back
with values intact on the next load; `.bak` rotated, no `.tmp` orphaned. The two worlds produced
two independent stores in one directory, so world-scoping is demonstrated, not just unit-tested.

Five real bugs came out of this, every one compile-clean and invisible off-game:

1. `AccessTools.FieldRefAccess<World, ulong>` threw — `World.m_uid` is declared `long`, and
   FieldRefAccess is type-exact rather than converting. Gave uid 0 on every load.
2. `World.GetWorldSavePath()` defaults to `FileSource.Auto`, which returns `""` under Steam Cloud
   and yielded a bare `/worlds`. Must pass `FileSource.Local` — see CLAUDE.md's known trap.
3. Switching to `world.GetDBPath()` did not help: it delegates to `GetDBPath(m_fileSource)`, and
   that world's source was Auto too. Two routes to the same empty base.
4. The harness's `World` stub declared `m_uid` as `ulong`, disagreeing with the real assembly and
   so proving nothing about the field it stood in for.
5. The shipped writer emitted a UTF-8 BOM. Not a correctness bug — `File.ReadAllLines` consumes
   it — but a format one, in a file whose whole point is being readable and diffable.

The temporary scaffolding this needed (`ModConfig.DebugWriteTestZone`,
`WorldTick.MaybeWriteDebugTestZone`, the `9 - Diagnostics` config section) was **removed in
0.1.9** once task 2 began writing real drift, which is what it existed to stand in for.

## 1. Fix silent corruption detection — DONE 2026-08-25

`Persistence.Load()` now treats "the file had content, zero lines parsed, at least one line
failed" as corruption: an error-level log naming the file, then `TryQuarantine` to `.corrupt`.
`_zones` stays empty and `_loaded` stays true, so the world stays playable and never throws.
The version header is recognised by **content** rather than by position — skipping line 1
unconditionally swallowed the only line a short binary file has, which is what hid the bug.

**Acceptance, all met:** a wholly-corrupt file is quarantined as `.corrupt` and logged at error
level; a header-only file is not; a partially-corrupt file stays on the per-line-isolation path.
Three tests in `PersistenceTests()` cover them. (The harness is 42/42 as of the BOM
fix that followed in 0.1.6.)

The quarantine path has still only ever been exercised against the test seams; task 0's in-game
runs never produced a corrupt file to quarantine. The spec this task pointed at (`docs/prompt-persistence-corruption-fix.md`) was
never in the repo; it arrived out of band.

---

## 2. `BiomeStateSystem` — FIRST PASS DONE 2026-08-25

Built as two pieces: `Core/BiomeDrift.cs` holds the arithmetic (pure, no clock/config/season, so
the harness compiles and tests the shipping source) and `Systems/World/BiomeStateSystem.cs`
drives it. Verified in-game on the dedicated server at v0.1.8:

```
[BiomeStateSystem] drifted 9 of 9 contacted zone(s); 9 zone(s) tracked.
```

Nine zones for a contact radius of 1, landing in the store as a 3x3 block around the player,
frost climbing 0.0024 to 0.0117 across two saves — 0.280/h measured against 0.28/h predicted.
Only `frost` non-zero on all nine lines, so sparseness holds in practice.

**Acceptance, met:** drift accrues on zones players visit; it survives save/load (the store was
read back with values intact); a zone returning to default is removed from the store (unit
test — the in-game runs never drove a zone all the way back). Drift math has unit tests against
fixed elapsed-seconds inputs.

**One bug worth remembering.** The epsilon that lets a healed zone leave the store was 5e-4 and
also applied to accumulated value: a tick of frost adds ~3e-5, so every pass zeroed what the last
one gained and frost could never accumulate, in any season, while the logs looked healthy. Fixed
by making epsilon a float-dust guard (1e-6) — linear decay reaches zero by crossing it, not by
rounding. Two regression tests run at the real default rates.

**Still to do on this system:**

- Iterate `ZDOMan` for live zone state. v1 reads character ZDOs only, because no drift input
  currently comes from world contents. This arrives with FireSystem/PlagueSystem, and is the
  point at which the sweep must be staggered against AwayFromHome's 60s full-index rescan.
- `WorldGenerator.instance.GetHeight()` for terrain-aware drift — nothing in v1 needs terrain.
- Only `Frost` accumulates. Fertility, Corruption, Scorch and Plague recover but nothing raises
  them yet; Scorch's one seasonal tie is that it recovers more slowly at higher fire risk.

## 3. `WeatherSystem` — DONE 2026-08-25

Verified at v0.2.2 on the dedicated server, with a player watching. All four acceptance
criteria, from one log:

```
[WeatherSystem] storm started at (-27, -17).
[WeatherSystem] storm began - sky is 'Clear' (forceWeather=False); at the centre:
    fireRisk x1.80, plagueSpread x1.50, wind x2.00 (vanilla 0.10 -> gameplay 0.20).
[WeatherSystem] storm ended - sky is 'Clear' (forceWeather=False); at the centre:
    fireRisk x1.00, plagueSpread x1.00, wind x1.00.
```

Player-confirmed: announcement and banner both seen; sky visually unchanged. Storms are REAL
vanilla `RandomEvent`s (banner, timer, pause-when-nobody-near, replication all inherited) and
POSITIONAL — every multiplier is `...At(pos)`, using the same containment maths as vanilla's
banner (`StormArea`, copied from `IsInsideRandomEventArea`: XZ-only, strict `<`, y>3000 bail),
so the two cannot disagree about where a storm is.

Notes for whoever touches this next:

- The Awake patch is a PREFIX, not the postfix this backlog originally suggested — house rule 1
  applies, and the decompiled `Awake` is only `m_instance = this;` with `m_events` already
  deserialized, so a prefix is safe. Storm liveness reads vanilla's private `m_randomEvent`
  via cached `FieldRefAccess` (rule 5).
- Vanilla SAVES the active event in the world file and resumes it on load. A resumed storm has
  no `storm started` line — only `storm began` — and this is correct. Do not "fix" it.
- The storm clock only accrues while a player is online. It originally accrued on an empty
  server, blew past its maximum, and fired the instant someone's character ZDO appeared —
  during their loading screen, with no HUD to show the announcement to.
- Event banner strings are PLAIN TEXT, not `$tokens`: we register no localisation, and vanilla
  renders an unknown token as visible garbage.
- `StormsForceWeather` (default false) remains the only line that can ever set
  `m_forceEnvironment`.

---

## 4. `WindSystem` — DONE 2026-08-25

Same session. Reads `EnvMan.GetWindIntensity()`/`GetWindDir()` at tick rate, never writes;
`WindState.Combine` produces the gameplay number (`vanilla 0.10 -> gameplay 0.20` under a
storm's x2.00). `IntensityAt(pos)` is positional so a gale across the map cannot drive fire
spread here. NaN-guarded before publishing, because everything downstream multiplies by it.

---

## 5. `FireSystem` — DONE 2026-08-25 (bridge to FireFront, verified live)

**Scope changed by decision, not drift.** FireFront (com.raveniron.firefront — same studio,
`C:\Users\donfr\source\repos\FireFront`) already ships verified fire spread: ignition from
vanilla fire damage, neighbor + ground-cell propagation with a distance leash, live wind bias,
rain suppression, client sync. Building spread here would have put two Raven Iron mods igniting
and destroying the same pieces. Decided 2026-08-25 (locked-decisions table): **FireFront owns
fire; RW owns the land's memory of it.**

What was built:

- FireFront 0.18.0 gained `FireManager.CollectActiveFirePositions(List<Vector3>)` — a public
  read API over the simulation's own cached positions, documented there as a load-bearing
  cross-mod contract.
- RW's `FireSystem` detects FireFront via Chainloader, resolves that API by cached reflection
  (soft dependency — neither mod needs the other to load), and raises `Scorch` on every zone
  containing a fire: flat `FireScorchPerMinute` (default 0.02, ~50 min to fully char), binary
  per zone because severity already shows up as more zones burning. Live tick time only — no
  zone clock, per `docs/zone-clock-ownership.md`. Dormant without FireFront, warning-not-silent
  when FireFront is present but older than 0.18.0 or its surface moved.
- `Core/FireScorch.cs` holds the pure zone-mapping and rate math, harness-tested (78/78).

The original acceptance criteria split by owner: fire spread itself is FireFront's behaviour;
RW's three criteria were all **verified live on the dedicated server** with a player-lit fire:

- Scorch appeared exactly where fire burned — one 10s bridge tick measured 0.0033351 against a
  predicted 0.003333, and a spreading ground fire scorched four zones (peak 0.149).
- It survived a full server restart on disk (fires are FireFront runtime state and died with
  the process; scorch is our persistent state and loaded back as 16 zones).
- With no fires left, BiomeDrift recovery turned it downward: 0.1490 -> 0.1479 over one
  autosave, every scorched zone healing.

Two operational facts learned on the way, both now load-bearing knowledge:

- **FireFront must be installed on clients too.** RPC_Damage runs on the piece's OWNER — a
  nearby client, not the server — and FireFront's client patch is what forwards the ignition.
  A client without it lights fires the server never learns about (FireFront documents this in
  its own patch comments).
- **A leashed ground fire can be self-sustaining** in dry weather: fuel regrows in 90s, so the
  front cycles its own footprint indefinitely inside the 40m leash. Observed live at 46 cells
  and climbing. One for FireFront's backlog (as is remote extinguish: its console commands run
  where typed, so a client cannot put out the server's fires).

The unattended-bases ⚠️ moves with the fire: it now constrains FireFront's damage zones, not
this repo. Worth carrying into FireFront's own backlog if it isn't already handled there.

---

## 6. `PlagueSystem` — DONE 2026-08-25 (verified live)

Built to `docs/zone-clock-ownership.md`: growth and cure live in `BiomeDrift.Apply` on the
zone clock (linear growth gated on the POST-decay value — a cure that reaches zero is
permanent, and proportional growth was rejected because it can never outrun linear decay at
seed levels); spread is an event system on live tick time (`Core/PlagueSpread.cs` pure and
harness-tested, `PlagueSystem` rolling the dice and writing seeds, storm-scaled positionally).

Containment is structural: only zones at the spread threshold (0.5) infect; seeds start at
0.05 and climb only through player contact; infected zones are never re-seeded. The front
advances one ring past wherever players actually go.

Verified live on the dedicated server, patient zero hand-edited into the store (the format's
hand-repairability doing real work — no debug scaffolding this time):

- **Spread:** four orthogonal neighbours seeded at exactly 0.05 across two passes; diagonals
  untouched; a zone bordered by multiple hotspots seeded once.
- **Growth:** patient zero 0.6 -> 0.6217, matching 0.042/h x (1 + corruption 0.5) minus
  recovery to four decimals once the credit-on-contact backlog (29 min of downtime) was
  accounted. Seeds inside the contact ring grew; the seed outside it did not move at all.
- **Cure:** with growth zeroed, every contacted zone drained at the predicted 0.02/h while the
  uncontacted one stayed frozen — drift acts only where people are, in both directions.
  Through-zero permanence (the epsilon snap kills a cured zone and warm weather cannot
  resurrect it) is pinned by unit tests.
- **Persists:** the outbreak survived two server restarts and a mid-test hand edit.

How a plague STARTS is deliberately out of scope: nothing invents outbreaks yet. Patient zero
comes from a future event system, a `wrath` console command, or an admin's editor.

---

## 7. Remaining systems

In roughly this order, each following the same `IWorldSystem` shape:

`WorldStateSystem` (derived aggregate — computed bottom-up from `BiomeMetrics × WeatherSystem`,
never stored top-down) · `EcologySystem` · `FarmingSystem` · `HealthSystem` ·
`ConsequenceSystem` · `RivalrySystem` · `RelicSystem`

---

## 8. `TitleSystem` + nameplate patch

Earned title rendered under player nameplates. Sourced from world-sim events (surviving a
harsh season, holding a contested zone), **not** from tracked stats — this mod does no data
mining. Requires a Harmony patch on nameplate rendering.

---

## 9. Client plugin

`RagnaroksWrath.Client` — visual-only, no HUD. Renders fire spread, storm intensity, and
plague fog, which pure server-side logic cannot push.

Read the presence-layer notes before starting: sky, weather, particles and grass are **one
global kit** welded to the local player's camera via a scene-authored `FollowPlayer`, which
early-outs when `Player.m_localPlayer == null` — the dedicated-server condition.

- **Particles cannot be swapped per-camera** — they carry simulated history. Anything
  simulated needs its own instantiated copy.
- **`m_psystems` are gated on `InShelter()`** — vanilla suppresses weather particles under a
  roof. Decide deliberately whether storms respect that.
- **`m_rainCloudAlpha` lives on the cloud dome's own material.** Drive it with a
  `MaterialPropertyBlock`, not `.material` (which instantiates a material you must then own
  and destroy).
- **Trap: resolve-on-attach latches OFF.** Resolving an `EnvSetup` at zone load returns null
  because the far heightmap does not exist yet, and if the retry sits behind an
  `if (_env == null) return;` early-out it can never reach itself — silent, for the whole
  session. Resolve on the settle tick.

---

## 10. Packaging

`manifest.json`, README, icon, changelog. Match the shape of The Raven's Call's release.
Version-pin BepInEx in `dependencies` (AwayFromHome and The Raven's Call both pin
`denikson-BepInExPack_Valheim-5.4.2333` — confirm current).

---

## Open questions

- **Verify the Seasonality GUID.** `RustyMods.Seasonality` is inferred, not confirmed. If
  wrong, detection silently fails and we run a competing season clock — the exact conflict
  rule 4 exists to prevent.
- **Console commands.** Prefix `wrath` is decided; the command set is not designed yet.
  Likely `wrath status`, `wrath zone`, `wrath reload`.
- **Shelter and storms.** Do Devastating Storm effects respect vanilla's `InShelter()`
  suppression?
- **Per-system config surface.** Master toggles exist; per-system tuning knobs are not
  designed beyond `SeasonLengthDays`.
