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

- FireFront 0.17.2 gained `FireManager.CollectActiveFirePositions(List<Vector3>)` — a public
  read API over the simulation's own cached positions, documented there as a load-bearing
  cross-mod contract.
- RW's `FireSystem` detects FireFront via Chainloader, resolves that API by cached reflection
  (soft dependency — neither mod needs the other to load), and raises `Scorch` on every zone
  containing a fire: flat `FireScorchPerMinute` (default 0.02, ~50 min to fully char), binary
  per zone because severity already shows up as more zones burning. Live tick time only — no
  zone clock, per `docs/zone-clock-ownership.md`. Dormant without FireFront, warning-not-silent
  when FireFront is present but older than 0.17.2 or its surface moved.
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

## 7. Remaining systems — 3 of 7 DONE 2026-08-25

**Done (v0.5.0):**

- `WorldStateSystem` — derived condition (Flourishing/Stable/Ailing/Stricken) from
  `BiomeMetrics` (sums-not-means over the sparse store, never persisted) x weather, with
  15% hysteresis so announced transitions cannot flap. Verified headless: initial condition
  `Stable (burden 2.08, 16 tracked, 5 infected)` against a hand-predicted 2.1, set silently on
  boot per design.
- `EcologySystem` — Corruption's first writer: plague or scorch >= 0.3 corrupts the land under
  it, ramping from a quarter-rate trickle at the line. Closes the first feedback loop
  (corruption feeds plague growth through the boost); severable via config. Verified headless:
  +0.00022 corruption in patient zero in one 60s tick with nobody online, matching the
  predicted 0.0132/h.
- `FarmingSystem` — Fertility-depletion's first writer, and the ZDO sweep deferred since task
  2. VERIFIED HEADLESS at v0.6.0: five planted turnips counted from the world save with
  nobody online (`'sapling_turnip': 5 standing`), and depletion reached the store one
  rotation later (`zone (-1,0): depletion=0.00208`, matching 5 crops x 0.002/crop-hour).
  The 0.5.1 lesson is in the sweep comment: the iterative walk yields every ~400 populated
  sectors, and it must be DRAINED per tick — resuming one chunk per tick stretched a rotation
  across an hour. HONESTLY SCOPED: depletion is only WRITTEN today — growth/yield effects
  need the client plugin's state sync, because a plant's lifecycle runs on its ZDO's owner (a
  client), and clients have no zone store.

**Remaining:** all four designed 2026-08-26 — `HealthSystem` (task 11), `ConsequenceSystem`
(task 12), `RivalrySystem` (task 13), `RelicSystem` (task 14, capstone — build last).

---

## 8. `TitleSystem` + nameplate patch — DONE 2026-08-26 (render awaits a second player)

Titles from world-sim events, never tracked stats. v1 set, one per running system so each is
provable: Stormrider (inside a storm's area), Plaguewalker (plague at the spread threshold
underfoot), Winterborn (configurable time online through Winter). Latest earned wins — a
title is where you have been lately, not a trophy case.

Verified live at v0.6.0: `Nomad (775624) earned Plaguewalker` on walking into the outbreak,
with `ragnarokswrath_titles_<uid>.dat` appearing beside the zone store (same fail-safe /
quarantine / no-BOM contract, keyed by `s_playerID` per the identity sheet; store behaviour
harness-pinned).

Plumbing decisions that will outlive this task:

- Titles travel by GUID-prefixed routed RPC (`TitleSync`), NOT a character-ZDO key: character
  ZDOs are client-owned and only the owner's writes replicate — a foreign key is stomped on
  the owner's next sync. Registration is keyed on the ZRoutedRpc INSTANCE (per-world-session),
  and a joining player gets the full table replayed.
- `Patch_Nameplate` is the first postfix under rule 1 AS AMENDED 2026-08-25: append-only on
  `Player.GetHoverName` at default priority, decorating whatever survives other mods. It also
  arms the client-side RPC handler, since pure clients tick no WorldTick systems.
- The Winterborn clock is in-memory and resets on restart: under-awarding is a shrug,
  double-announcing is spam.

Open inch: the nameplate RENDER is unverified — you cannot see your own plate, so it needs a
second player looking at a titled one.

---

## 9. Client plugin — FIRST PASS DONE 2026-08-26 (zone sync + plague fog, verified by eye)

**Ships as ONE role-aware DLL** (locked decision amended 2026-08-26): headless runs
simulation, clients add visuals, hosts do both. `RagnaroksWrath.Client/` is retired — the
main DLL already had to be client-installed for nameplates and TitleSync, so a second DLL
was two files and a version skew for nothing.

What landed, verified live (player saw the miasma in the outbreak):

- `Net/ZoneSync.cs` + `ZoneSyncSystem` — the keystone sync three systems were queued behind.
  Per-peer pushes of the zone ring around each player (5x5 default, ~700B), ABSOLUTE
  snapshots with defaults included (the stats sheet's delta lesson), clamp-on-read at the
  wire, per-world-session RPC registration, listen hosts bypassing the wire entirely.
- `Visuals/PlagueFog.cs` + `Core/FogMath.cs` — procedural miasma (texture, material,
  particles all code; no assets, no bundles, no prefab trap), world-space so it hangs behind
  a walking player, InShelter-suppressed, rule-3 wrapped with a throw-once latch. Emission
  floor at plague 0.15: fog is the DISCOVERY mechanic, so the frontier's fresh seeds must not
  telegraph themselves.
- Gated on `GraphicsDeviceType.Null` — the headless tell that survives client reference DLLs.

The lesson this pass paid for: **Valheim strips Unity's standard particle shaders.**
"Particles/Standard Unlit" and "Legacy Shaders/Particles/Alpha Blended" are both absent;
`Sprites/Default` is the first of FireFront's candidate chain that actually ships. The chosen
shader is logged at build, because two clients disagreeing about fog is otherwise
undiagnosable.

Still to come on this substrate, in whatever order earns it: storm gusts (same sync, new
emitter), farming's growth/yield consumer (client reads depletion). Scorch ash landed
0.14.0 and was VERIFIED BY EYE at 0.14.1 ("ash is falling") — third emitter, `AshMath` +
`Visuals\ScorchAsh`, owner-requested after the arson test left invisible scars. The
0.14.1 lesson earns its own line: 0.14.0's emitter BUILT and RAN and rendered nothing,
because nobody budgeted visibility — rate x size x alpha / dispersal area came to one
faint mote per 84 square meters at real-scar scorch. Run the per-square-meter arithmetic
BEFORE shipping a particle effect, not after a player stands in it and sees nothing. Delivered since:
HealthSystem (task 11, 0.8.x) and **frost breath (0.9.0, VERIFIED LIVE 2026-08-26)** —
built to this template after the chill's first live test came back "no vfx", shader chain
extracted to `ParticleKit` so the stripped-shader lesson cannot fork between emitters.
Verified by eye on the staged frost-0.75 zone: puffs on arrival ('Sprites/Default',
emitter built 61s after 0.9.0 load), a roof kills the breath, a campfire does NOT — while
the campfire DOES kill the chill. The two effects obeying different gates (cold air vs
cold body) is the design, observed working in one spot.

## 10. Packaging — DONE 2026-08-26

Root-level `manifest.json`, player-facing `README.md`, `CHANGELOG.md`, generated 256x256
`icon.png`, and `tools\package.ps1` producing the flat Thunderstore zip in `dist\`
(gitignored). Modeled on SkaldSaga's release SHAPE only — that project is dead and stays
reference-only; nothing is linked or copied from it.

---

## 11. `HealthSystem` — BUILT 2026-08-26 at 0.8.0, corrected in 0.8.1

**VERIFIED HEADLESS at 0.8.0 on the live dedicated server.** Exposure accrued on a real
connected player at the predicted rate: 0.0084 at 09:23:26 -> **0.2752** at 09:31:26 against
**0.27503** predicted (plague 0.9998 underfoot, 30min-to-max), agreeing to the four decimals
the ledger stores. Tier 1 crossed at 09:30:41 against 09:30:40 predicted. Ledger written
beside the zone store; zero warnings or errors across the run.

**The 0.8.0 defect the live test caught, and the lesson.** The player reported the icon
appearing while stamina "feels normal" — and it was: `Ramp` started at 1.0 ON the tier
threshold, so crossing tier 1 gave x0.98. The MOD ANNOUNCED SOMETHING IT HAD NOT DONE. The
spec's agreed table (0.25 -> x0.85) was written as tier VALUES and implemented as a ramp
that only reaches x0.85 at exposure 0.4375 — a spec-to-code translation error that every
off-game test passed straight over, because the tests asserted the ramp's shape rather than
the promise the tier made. 0.8.1 steps to the tier value on crossing, then ramps; the
regression is pinned by tests that were RUN AGAINST THE OLD CODE FIRST and observed to fail
(x1.00 at the threshold). **Generalised: assert what the player was promised, not what the
function computes.**

Also in 0.8.1: `SE_Plaguesick` overrides `GetIconText` (virtual; `Hud` calls it
unconditionally and shows any non-empty string — both decompile-verified, since "no ttl
means no text" was the plausible assumption) to carry severity while worsening and a real
`GetTimeString` countdown while recovering.

**VERIFIED IN-GAME 2026-08-26 (0.8.1, dedicated server, live player):**

- **Accrual to prediction** (four decimals, see above) and tiers 1 and 2 crossing on
  schedule with their messages and the icon; the 0.8.1 step made tier 1 FELT ("stamina
  bites now" — the player's words, and the acceptance that matters).
- **Relog is not a cure:** exposure froze at 0.574 for the 137s of a quit/deploy/rejoin,
  then resumed. **Survived a full server restart** at 0.6824 unchanged — the ledger, not
  the session, owns the condition.
- **Decay on leaving plagued ground** at the predicted rate; the icon's countdown branch
  ran live.
- **Frost chill, both directions:** staged frost 0.75 in zone (1,0) by store edit (fresh
  contact stamp — a stale one would have credited a backlog and drained the frost before
  it was felt), chill landed on entry AND a campfire cancelled it — the gate proven, not
  assumed. Contact recovery visibly draining the staged frost (0.750 -> 0.7479) is
  BiomeDrift agreeing the player was there.

Unobserved, accepted: the tier-3 centre-screen line (same code path as tiers 1-2, higher
threshold — the player peaked at 0.6963) and the live rate-change from poison-resist mead /
rested (the remedy REPORT wire ran throughout; the rate maths is harness-pinned).
0.8.2 (flush-on-shutdown) awaits its verification at the next server stop: the ledger
mtime must land beside Dedicated.db's instead of up to a minute earlier.

Off-game: 139/139 including 22 new Exposure/HealthStore tests, every rate matching its
hand prediction. Build clean; every runtime member access decompile-verified first.
**The flagged cold-flicker hazard was resolved by not fighting at all:** the chill is OUR
OWN code-built SE (Cold's icon, config regen penalties), applied by the client's 1s loop
with vanilla's own gates re-checked (campfire SE, shelter, frost damage-modifier,
warm-cozy area, and never while vanilla Cold/Freezing runs) — **no Harmony patch anywhere
in task 11**, so there is no remove/re-add fight to spam messages from. Frost-resist mead
still cancels it because we test the same damage-modifier aggregation vanilla does.

The world's state reaching the player's body. Spec settled in a design conversation with the
owner (their calls, recorded here so nobody re-litigates them by accident):

- **Plague is ACCUMULATING EXPOSURE**, not an instant zonal debuff and not a carried
  infection. Standing on plagued ground builds a per-player 0..1 meter; leaving drains it.
- **Weakens, never kills** — and non-lethal BY CONSTRUCTION, not by tuning: the plague effect
  is multipliers only (no `m_healthOverTime` damage), and frost escalates at most to vanilla
  **Cold**, never Freezing (Freezing is the only one of the pair that ticks damage).
- **Counterplay is vanilla remedies.** Poison-resist mead halves exposure accumulation,
  rested doubles decay, frost-resist blunts the cold — and that last one costs us NOTHING
  (see the decompile note below).
- **Effect palette: stamina first, then regen.** Sickness in the body before the wound.
- **Visibility: a real status-effect icon in vanilla's own bar + MessageFeed tier lines.**
  Vanilla's status bar is the game's UI, not a HUD of ours; the no-HUD rule stands.
- **Timescale: tens of minutes.** Max exposure after ~30 min standing in plague 0.95;
  sickness is the consequence of settling in blight, not of visiting it.
- **Frost's role: amplify vanilla Cold.** High zone frost makes Cold bite where it normally
  wouldn't. Frost is instant-environmental (like vanilla); only plague accumulates.

**Architecture** (server decides pressure, client applies effect — the TitleSync split):

- **Server:** `HealthSystem : IWorldSystem` on WorldTick. Sweeps connected players' character
  ZDO positions (the BiomeDrift contact pattern; `GetAllCharacterZDOS`, never Player lists —
  headless). Exposure accrues at `plague underfoot x rate` when plague >= the **shared 0.15
  floor** (same constant as PlagueFog's emission floor — fog is the discovery mechanic, and
  the sickness must not telegraph what the fog hides; name the constant once, use it twice).
  Decays otherwise. Ledger keyed by `s_playerID` (never `ZDOID.UserID` — session, not
  player), persisted as `ragnarokswrath_health_<uid>.dat` under the same fail-safe /
  quarantine / no-BOM / InvariantCulture contract as titles — **relogging is not a cure**.
- **Sync down:** exposure pushed to the owning peer on quantized change (0.01 steps),
  GUID-prefixed routed RPC per TitleSync (per-world-session registration, replay on join,
  listen host bypasses the wire).
- **Remedy report up:** the server cannot see a player's status effects (stats/skills/SEs are
  local-only), so the owning client reports a 2-bit remedy state (poison-resist active,
  rested active) via routed RPC, rate-limited ~5s. Trusting a client about its own relief is
  accepted — this is co-op drift, not anti-cheat.
- **Client effect:** a code-built `SE_Stats` instance — `ScriptableObject.CreateInstance`,
  fields set in code, vanilla sprite reused for the icon, no asset, no bundle, and **no
  ObjectDB registration**: the decompiled `SEMan.AddStatusEffect(StatusEffect, ...)` instance
  overload clones what it is handed and bypasses ObjectDB entirely. Tier changes mutate the
  LIVE clone (fetched by `GetStatusEffect(hash)`) — the multipliers are read fresh on every
  `ModifyStaminaRegen`/`ModifyHealthRegen` call, so no remove/re-add churn. `m_ttl` 0; we own
  add/remove. Rule-3 wrapped, throw-once latch like PlagueFog.
- **Frost delivery:** client-side patch around `Player.UpdateEnvStatusEffects` — **not**
  `EnvMan` (rule 4 untouched: we drive the player's RESPONSE, not environment selection, and
  the sky stays vanilla's). When synced zone frost >= threshold and vanilla's own gates say
  exposed (not near fire, not sheltered, not frost-resistant, not WarmCozy — the same checks
  at the top of the decompiled method), ensure vanilla's Cold SE. Because vanilla's gate
  already cancels on the frost damage-modifier and WarmCozyArea, **frost-resist mead and
  campfires counter our amplified cold with zero code of ours**.

**Tiers** (config defaults, exposure 0..1): 0.25 *Touched* — stamina regen x0.85;
0.5 *Sickened* — stamina x0.65, health regen x0.8; 0.8 *Ravaged* — stamina x0.45, health
regen x0.55. MessageFeed line on every tier crossing, both directions. Rates as config:
`ExposureMinutesToMax` 30 (at plague 1.0 underfoot, scaled by actual plague),
`RecoveryMinutes` 20, rested decay x2, poison-resist accumulation x0.5, frost Cold threshold
0.5. `EnableHealth` toggle already bound.

**Known hazard, resolved at build time:** vanilla's else-branch REMOVES Cold every
`UpdateEnvStatusEffects` pass when its own conditions say no — a naive postfix re-adding it
each call would fire the add/remove messages every tick. The anti-spam behaviour is an
acceptance criterion (below), and if a global `EnvMan.IsCold` postfix is considered instead,
its full client-side caller list must be enumerated first (other systems consult it).

**Acceptance:**

- Harness: exposure math (accrue/decay/remedy modifiers/tier mapping/quantization), ledger
  round-trip through the SHIPPING writer, corruption quarantine, no BOM.
- In-game, plague: walk into the outbreak — icon appears with live multipliers in its
  tooltip, MessageFeed fires at each tier, stamina visibly drags; walk out — it drains;
  rested drains faster; **relog while sick — still sick**.
- In-game, frost: a high-frost zone chills a player vanilla wouldn't chill; frost-resist mead
  cancels it; **no message spam** from the add/remove interplay; the sky logs vanilla's own
  environment at every transition (rule 4 held by evidence, as WeatherSystem did).
- Headless: exposure accrual logged for a connected player; the health ledger appears beside
  the zone store.

---

## 12. `ConsequenceSystem` — BUILT 2026-08-26 at 0.10.0 (in-game verification pending)

Off-game: 151/151 (8 new ConsequenceMath tests). All four flavors landed exactly on the
spec's surfaces, decompile gates honoured first:

- **Barren:** `Pickable.Interact` Priority.Low prefix honouring `__runOriginal`, refusal
  shaped like vanilla's own tar case; hover postfix explains. Both surfaces public.
- **Empowered:** `SpawnSystem.Spawn` exposes `levelUpMultiplier` as an ARGUMENT — the
  prefix scales vanilla's own roll and every vanilla cap/path stays in charge.
  `CreatureSpawner.Spawn` returns the spawned ZNetView; postfix rolls one star at
  vanilla's base chance x our multiplier. Passive list excluded from both.
- **Sickening:** `ConsequenceEffects` doses owned passive wildlife with a TTL'd SE_Stats
  slow (`m_speedModifier` decompile-verified) — expiry IS the cure, no removal
  bookkeeping. One `AddStatusEffect(template, resetTime: true)` covers add AND refresh.
  The spec's lethal edge ("a starving deer may die") is NOT built: SE health-over-time
  only heals (negative values never arm the ticker) — deferred until it earns a pass.
- **Withering:** `Plant.UpdateHealth` postfix sets unhealthy status -> vanilla's withered
  visual and `m_destroyIfCantGrow` death for free. `Plant.m_status` is PRIVATE at runtime
  (rule 5 catch — publicized compile hides it): reached via cached FieldRefAccess, lazily
  resolved, error names the field if Valheim's API moves. Hover postfix blames the soil,
  not the biome.
- **Reach rule enforced by construction:** all four surfaces only exist as live instances
  on machines near a player; a dedicated server holds ZDOs, an AFH keeper holds no client.
  The server half is only the announcer: one line per zone per session, worded by the
  worst flag (`Empowered > Barren > Sickening > Withering`).

**VERIFIED IN-GAME 2026-08-26 (0.10.0, dedicated server, live player).** Corruption staged
to 0.7 in the outbreak by store edit (fresh stamp), so zone (0,-1) carried all four flags.
Owner's report, all five checks: the festering announcement fired ONCE, pickables withered
and refused, wildlife visibly slowed, starred spawns appeared at the expected rarity, and
both negative controls held — the turnip farm at (-1,0) and the frost scar at (1,0) showed
no consequences. Zero warnings or errors on either side across the session (the error watch
ran throughout). A deploy near-miss worth keeping: the first 0.10.0 deploy was a DLL built
BEFORE the version bump — task-12 code, 0.9.0 label — caught by the strings audit, not by
the copy. Identify builds by content, always.

Unobserved, accepted: crop withering (needs a plant in blighted soil and a grow cycle —
the status flip and hover line are the quick half if anyone plants a turnip in the
outbreak; vanilla's cant-grow death is the slow half). Same standing as task 11's tier-3
line: same code path as verified surfaces, lower stakes, recorded rather than assumed.

The drift store growing hands. Spec settled in a design conversation with the owner; the
four framing calls and three flavor calls below are theirs:

- **Domain: wild vegetation, crops, creatures. NEVER player structures** — chosen
  deliberately, and it deletes the entire AwayFromHome minefield from this system's map.
  Do not add a "just a little structure decay" option later without a new conversation.
- **Severity: degrade with repair.** Pressure, not verdicts — everything it does can be
  pushed back by curing the land (the existing drift cures), replanting, or leaving.
- **Reach: player-present only.** Consequences fire only in zones with a real player
  standing in them. This is a design choice AND the mechanism: a present player's client has
  live instances and can own every physical act — the FireFront delegation lesson, promoted
  to architecture. No headless-instance problem exists here at all.
- **Voice: mixed by weight.** Per-creature and per-pickable effects are silent (the fog
  precedent — the world speaks through its body); a zone's first crossing into a
  consequence-active tier while contacted earns ONE MessageFeed line.
- **Creatures: both directions.** Blight empowers hostiles and sickens passive wildlife.
  The blight has a side, and it is not yours.
- **Wild vegetation: barren only.** Pickables (berries, mushrooms, thistle) stop yielding
  in bad zones — withered hover text, no theatrical tree damage. The land goes quiet.
- **Pacing: noticeable per visit.** Effects are visible during a stay — a starred spawn, a
  staggering deer, a bush that won't pick — without waiting for a return trip.

**Flavor mapping** (proposed defaults, all thresholds config):

- **Plague** sickens: pickables fail (>= 0.4), passive wildlife gets a code-built sickness
  SE — slowed (`ModifySpeed` is on the SEMan path) plus a mild drain, visibly staggering
  (>= 0.4). Sickness on wildlife may kill a starving deer eventually; that is the one
  lethal edge, and it is aimed at deer, not players.
- **Corruption** empowers: hostile spawns in corrupt zones (>= 0.5) come up starred via
  vanilla `SetLevel` at spawn time — vanilla's own language for "this one is worse". The
  `SetLevel`-resets-max-health trap is irrelevant at spawn: it IS the vanilla leveling path.
- **Scorch** starves: pickables fail on burned ground too (>= 0.5). Ash bears nothing.

**Architecture:**

- Clients already hold the zone ring — **ZoneSync needs no new payload**. The client-side
  `ConsequenceSystem` reads the synced cache and acts only on instances whose ZDOs the local
  client OWNS (the ownership check is what prevents two present players applying the same
  effect twice).
- Candidate patch surfaces, each behind the decompile gate before build: `Pickable`'s
  pick/hover path (a `Priority.Low` prefix that declines the pick with withered hover text —
  "no opinion means return true"), the spawn path (`SpawnSystem` / `CreatureSpawner`)
  postfix for stars, and `SEMan` instance-add for wildlife sickness (same no-asset SE
  pattern task 11 verified).
- "Passive wildlife" is an explicit configurable prefab-name list (Deer, Boar, Hare, ...),
  not a faction guess — factions lump deer with greydwarfs.
- Server side is thin: a WorldTick watcher that sends the landmark MessageFeed line on a
  contacted zone's first tier crossing (persisted flag in the zone store? No — a announced
  set in memory per session is enough; re-announcing after a restart is acceptable, spam
  within a session is not).
- The AFH keeper must not count as "player present" — verify at build that the keeper is
  not a real character ZDO (expected: it is not), and pin the check to the same
  contact-detection path BiomeDrift uses.
- **Boundary with FarmingSystem:** FarmingSystem owns growth/yield RATES (its client
  consumer); ConsequenceSystem owns physical ACTS. For crops that means: growth slowdown is
  farming's; withering-to-death in soil past a high line (>= 0.6 proposed) is consequence's.
  Neither writes the other's ledger.

**Acceptance:**

- Harness: threshold/tier math, the flavor mapping table, passive-list matching,
  announce-once logic.
- In-game, plague zone: a berry bush shows withered hover and declines the pick; a deer
  visibly slows with a sickness icon over it; walking out and curing the zone restores both.
- In-game, corrupt zone: a fresh spawn comes up starred; the same prefab in a clean zone
  does not.
- In-game, voice: exactly one MessageFeed line the first time a contacted zone crosses into
  a consequence tier; nothing per-bush, per-deer, per-spawn.
- Ownership: with two players in the zone, no double-applied effects (one SE per creature,
  one suppression per pickable).
- AFH: a keeper-held zone with no real player fires nothing.

---

## 13. `RivalrySystem` — PHASE A BUILT 2026-08-26 at 0.11.0 (in-game verification pending);
## phases B–E not started. PHASED.

Phase A landed: `RivalryMath` + `RivalryLedger` (167/167 off-game, 16 new — decay
half-life exact, watermark monotonic, negatives floored on read, prune-to-sparse,
quarantine, no BOM) + `RivalrySystem` phase-A writers. Decisions the build made:

- **Tending verified at the source:** `Piece.SetCreator` writes `GetPlayerID()` into
  `ZDOVars.s_creator` (Player.cs decompile) — the creator IS the ledger's identity long.
  The sweep copies FarmingSystem's one-whole-prefab-per-tick walk; the persisted plantTime
  WATERMARK makes each plant book exactly once across restarts (rebooting must not be a
  farming strategy). Known accepted gap: a plant sown mid-rotation after its prefab's turn
  can slip under the advancing watermark — rare, under-credit, Winterborn shrug.
- **Healing presence is observation, not coupling:** rivalry watches zone damage decrease
  between its own looks and splits care among the ring-present (BiomeDrift's own reach);
  baselines are forgotten for uncovered zones so a returning player cannot inherit the gap.
  No hooks into BiomeStateSystem — the store itself is the signal.
- **ARSON ARMED at RW 0.11.1 + FireFront 0.17.3** (same day; in-game verification
  pending). FireFront's new contract: `FireManager.CurrentFireIgniterPlayerId`, the
  persistent player id behind the current fire event, captured once beside `_fireOrigin`
  (spread inherits its arsonist, capture-once means later fire-throwers do not steal the
  event) and reset when the fires die. The id comes from `HitData.m_attacker` at the
  ignition patches — NOT the RPC sender, who is the object's OWNER and not the arsonist
  in someone else's loaded area. The ignite RPC was renamed (…IgniteRequest2) so a
  mixed-version pair no-ops cleanly; FireFront deploys both sides together as ever.
  RW's FireSystem books `scorchDelta x ArsonHarmPerScorchPoint` to the igniter in every
  zone it scorches, resolves the property optionally (absence = one info line, dormant,
  scorch unaffected — an older FireFront is legitimate). Natural fire books nobody.

**ARSON VERIFIED LIVE 2026-08-26 (RW 0.11.1 + FF 0.17.3, dedicated server).** The whole
chain observed link by link: `fireignite` on a beech -> the v2 RPC logged
`peer -482070028 ... igniter=775624` (SESSION id and IDENTITY id visibly different in one
line — the design's whole point) -> event captured -> reflection read -> first harm row
0.0067 = exactly two 10s ticks of one zone. The fire then crossed THREE zone borders
(into the outbreak, the frost scar, and (1,-2)) and every zone billed the same arsonist —
0.2664 total to 775624 — proving spread inherits its culprit across zones via FireFront's
capture-once event. The owner fought their own fire with the extinguish key while their
own ledger billed them, and the burn zone banked 0.209 scorch. Zero errors either side.

**0.8.2 FLUSH VERIFIED at the same stop:** the rivalry ledger (same write-behind contract
as HealthStore, same OnDestroy site) rewrote at shutdown 12:23:29.484 — 2ms after the
zone store's flush and 56s after its last cadence save, which is exactly the minute the
pre-0.8.2 code lost. Observed, not argued.

**Phase A is COMPLETE and fully live-verified: all three writers** (tending, healing
presence, arson) plus decay, watermark, sparse pruning, and the shutdown flush.

**PHASE B BUILT 2026-08-26 at 0.12.0 (in-game verification pending) — three of four
teeth, scoped by the acceptance list:** drift-harsher, pickable-refusal and the grudge
title are B's acceptance criteria and are built; the wildlife-flee bump and
hostiles-seek-you have NO acceptance criteria in the spec and sit on the riskiest patch
surface in the mod (BaseAI targeting), so they are deferred to their own pass with their
own decompile gates rather than riding along untestable. Decisions:

- **Grudge = clamp01((harm - care) x GrudgeScale)** per zone per player. Care offsets
  harm point for point — tending genuinely mollifies, and a net carer holds no grudge
  (negative grudge is phase C's mercies, not a sign flip here).
- **Drift tooth:** BiomeStateSystem's contact collection now carries WHO contacts; each
  zone drifts under the WORST present grudge — recovery x(1-g/2) (the land sulks, never
  refuses), frost/plague pressure x(1+g). Scaled at the rate arguments so BiomeDrift's
  pure math stays untouched and harness-covered.
- **Refusal tooth:** the task 12 pickable surface gained a personal gate — Barren refuses
  everyone, Shunned refuses YOU ("the land remembers what you did here"). Distinct
  messages, distinct hover lines, checked in that order.
- **Title tooth:** Ashbringer (harm is all fire today, so the name is true) at worst-zone
  grudge >= 0.5, EDGE-TRIGGERED — a persistent grudge would otherwise alternate with
  Plaguewalker every tick, announcing each swap. (The same latent flap exists between
  Stormrider and Plaguewalker when both hold; pre-existing, unobserved, noted.)
- **Wire:** ZoneSync's per-peer ring gained the receiving player's grudge per zone and
  was renamed (...zone_state2) so a version-skewed pair no-ops cleanly — the FireFront
  0.17.3 lesson applied to our own wire. GrudgeAt mirrors StateAt's authority rule.

**PHASE B VERIFIED LIVE 2026-08-26 (0.12.0, staged harm 0.6):** Ashbringer landed on
schedule, once, no flap (owner-reported); pickables refused the grudged hand with the
personal line (owner-reported); and the drift tooth measured **0.01753/h interim, then
0.01750/h EXACT over the full 13-minute formal window, against a 0.01750/h prediction** —
the x0.7 grudge signature to every digit the ledger stores, on save-to-save endpoints. THE MEASUREMENT STORY IS THE LESSON: the first window read 0.0578/h — triple
the prediction — because the player's returning footsteps paid a ~20min credit-on-contact
backlog INSIDE the window. The runbook's own most-documented trap caught the person who
documented it; the instrument was honest, the window was not. Re-baselined after the
backlog cleared, save-to-save. Open two-player inches: a clean player picking the refused
bush, and the grudge title rendering on a nameplate.

**PHASE C BUILT 2026-08-26 at 0.13.0 (in-game verification pending) — all three faces:**

- **Dominance** (`RivalryContest`, pure, 9 harness checks): per zone per column, floor-
  gated (nobody wins ground they barely touched), hysteresis-held (a challenger needs
  incumbent x1.15 — WorldState's anti-flap band applied to people). Vacancies fill
  SILENTLY and faded incumbents are replaced silently — a FLIP, the only announceable
  event, requires both rivals above the floor: the voice speaks only of genuinely
  contested ground, and a one-player world never hears it.
- **Mercies:** the dominant carer's presence quickens zone recovery x1.25 (stacking
  multiplicatively with any present grudge — the land weighs everyone), and standing on
  ground whose memory you hold sheds plague exposure x1.5 faster (decay only: the land's
  favour heals, it does not shield). The dominant-harmer face costs nothing — phase B
  already IS it.
- **Standing titles:** Warden / Despoiler at >= 3 zones held per column, edge-triggered
  via the shared EdgeTitle helper. Holder maps are static on RivalrySystem (the
  SeasonSystem.Current pattern) for BiomeState/Health/Title to read.

**PHASE D BUILT 2026-08-26 at 0.15.0 (in-game verification pending) — the spawn war:**

- **Contested** = blight (worse of plague/corruption) >= 0.5 AND total zone care >= 0.3 —
  sick, untended ground is just sick; tended, healthy ground is just loved; the war needs
  both. Intensity 1, x2 under a Devastating Storm — rule 4's "contest escalation"
  breadcrumb finally cashed. War state re-derives from store+ledger every pass (a restart
  mid-war re-derives the same war; a resolution during downtime is a Winterborn shrug).
- **Blight side:** the task 12 star surface gains x(1 + ContestStarBonus x intensity) —
  contested ground doubles star odds, storm war triples.
- **Wild side, THE FIND:** vanilla's own pheromone machinery (`SE_Stats.m_pheromoneTarget`
  and friends — the Bog Witch meads' fields, decompile-verified public, read by
  `UpdateSpawnList` on exactly the machine the reach rule lives on). ConsequenceEffects
  carries invisible TTL'd "war horn" SEs targeting the wildlife list while its player
  stands on contested ground — more deer answering, through the game's own rules, NO
  spawn patch at all. Horns silence by expiry when the war ends or the player leaves.
- **Resolution:** at the contested->uncontested edge, the wild won if the blight itself
  broke (clean ground is the wild's victory even if the tenders also faded); otherwise
  the blight won. One Centre-screen line to players near. Wire: ring push carries war
  intensity per zone (RPC now ...zone_state3).
- Acceptance protocol (solo-viable): stage care 0.5 in the outbreak (blight 1.0 there) ->
  contested; verify war horns log + starred odds; then hand-cure or fade -> exactly one
  resolution line, wild or blight by which side actually broke.
- The ledger is the third write-behind store, so it appears in `WorldTick.OnDestroy` per
  the 0.8.2 rule.

**PHASE A VERIFIED LIVE 2026-08-26 (0.11.0, dedicated server):** ledger born 181s after
boot; tending booked 0.05/plant to 775624 for the two crop saplings actually standing (the
predicted five was stale field knowledge — three had grown since 0.6.0; per-plant rate is
harness-pinned, the count is the world's); watermark advanced to real in-game plantTime
only after the full rotation and persisted. HEALING PRESENCE proved its trigger exactly:
care rows in the player's ring appeared in the same tick Nomad joined, booked from the
credit-on-contact backlog heal their arrival enabled — care requires presence, observed.
A design fact this surfaced, accepted: the player whose contact pays a zone's healing
backlog books that healing — their arrival IS what enabled it, consistent with the
credit-on-contact philosophy, but a long-absent zone's first visitor books its whole
recovery. Zero errors. Remaining phase-A inch: a NEWLY planted crop booking +0.05 exactly
once across sweeps (the watermark's live proof).

The world keeps score. The owner chose the maximal reading — all four visions at once —
which makes this the largest system in the mod, so the spec's whole job is phasing it into
independently shippable, config-severable slices sharing ONE spine. Build order A -> E; each
phase lands with its own tests and in-game run before the next begins.

**The spine — Phase A, the influence ledger.** Per-zone, per-player attributed acts, two
columns: harm and care, both decaying over real time (grudges fade; the file stays sparse —
prune at write). Persisted `ragnarokswrath_rivalry_<uid>.dat`, same fail-safe / quarantine /
no-BOM / InvariantCulture contract, keyed by `s_playerID`. Attribution uses hooks that
ALREADY exist:

- **Arson** — FireSystem's client->server ignition forward knows its sender; scorch that
  ignition produces books harm to the igniter.
- **Tending** — planted crops carry vanilla's creator id (verify the exact ZDO var at build
  via decompile); planting books care to the planter.
- **Healing presence** — drift only cures contacted zones, and contact is already
  per-player: recovery ticks split care credit among the players whose contact enabled them.
- Plague carries NO player attribution — task 11 deliberately chose exposure over carried
  infection, so nobody "brings" plague anywhere.

**Phase B — the grudge (world vs you).** Per zone, grudge = normalized(your harm - your
care). All four teeth, owner's call:

1. **Harsher drift underfoot** — while YOU are the contact, decay credit x(1+g), recovery
   x(1-g/2). Server-side, plugs into the existing BiomeDrift credit path. Invisible but
   measurable — the verification pattern already proves rates to four decimals.
2. **The wild shuns you** — in grudged zones, pickables fail for you specifically (task 12's
   pickable surface, per-player gate) and wildlife flees you sooner (alert-range bump for
   the grudged player; BaseAI surface behind the decompile gate).
3. **Hostiles seek you** — spawns prefer targeting the grudged player, decorating target
   selection at `Priority.Low` (decompile gate; AI-mod-collision caution noted — cede fights
   per rule 1).
4. **Grudge titles** — "Foe of the Forest", "Ashbringer" at grudge thresholds, riding
   TitleSystem/TitleSync unchanged. Latest-earned-wins applies; a grudge title competes with
   Plaguewalker like any other.

Clients learn their own grudges through ZoneSync's per-peer ring push — it is ALREADY
per-peer, so each player's ring gains their own grudge per zone. Small payload bump; the
512 KiB ceiling is not remotely in play.

**Phase C — the contest (player vs player).** Compare ledger columns. All three faces:

- **Titles and standing** — regional dominant-shaper titles (Warden of / Despoiler of),
  computed from column dominance over a floor (nobody wins a zone neither really touched).
- **The land takes sides** — the dominant carer gets small mercies in that zone (recovery
  gentler while they contact; task 11's remedies bite slightly better); the dominant harmer
  simply IS the grudge case Phase B built. No new machinery, only wiring.
- **Announcements** — MessageFeed narrates dominance flips, rate-limited and floor-gated so
  it speaks only about ground both rivals genuinely shaped.

**Phase D — the spawn war (world vs world).** A zone where opposing pressures are BOTH
strong (corruption/plague vs recovery/care) enters CONTESTED state; storms escalate it —
this is the "contest escalation" breadcrumb in rule 4's consumer list, finally honored.
While contested and a player is present (task 12's reach rule, reused): both alignments
intensify — blight-side spawns starred via task 12's surface, wild-side spawn rates up.
Resolution when one side's drift wins the ground; one MessageFeed landmark line per
resolution. Dangerous ground that resolves itself.

**Phase E — the nemesis. BUILT AND VERIFIED LIVE 2026-08-26 at 0.16.0.** First real
death: `Nemesis: Boar(Clone) marked — slayer of Nomad (kills 1, level 2)` — m_lastHit
resolved, ownership held, keys written, level stepped. Plate confirmed by eye (star +
slayer line). THE STRONG CLAIM CONFIRMED: server bounced (graceful, world saved), ZDOIDs
regenerated at load, and the boar STILL wore its star and slayer line on rejoin — the
ZDO-key design proven end to end. Accepted unobserved (same code path, lower stakes):
the x2 count on a second death to the same creature, and the owned-elsewhere skip line.
TASK 13 COMPLETE: all five phases built and verified.
`Core/NemesisMark.cs` (pure: level step with never-demote cap, TMP suffix — 12 harness
checks) + `Patches/Patch_Nemesis.cs` (Player.OnDeath observing postfix on the victim's
client: m_lastHit via cached FieldRef (rule 5 — it is protected), owner-gated ZDO write
of victim/kills/name keys, SetLevel step; Character.GetHoverName decorating postfix for
the plate — Player's own override keeps player plates untouched by construction).
Config: EnableNemesis (default true, under EnableRivalry), NemesisMaxLevel (default 3).
No server-side code at all — the world save is the ledger. ACCEPTANCE, in-game: die to a
creature; its plate gains "slayer of <you>" and a star; a second death to it increments
the count; the mark survives a server restart (the ZDO-key fact doing its job); a
non-owned killer logs "owned elsewhere" instead of writing graffiti.
**GATE DECIDED 2026-08-26: GO for the reduced form**, with one
design correction the decompile forced. Facts in
`docs/reference/CREATURE-PERSISTENCE-AND-NEMESIS-FACTS.md` (read on this machine against
the live install):

- **ZDOID keying is DEAD across sessions** — `ZDO.Load` reassigns every id
  (`m_uid.SetID(++ZDOID.m_loadID)`). The spec's "mark keyed by creature ZDOID + prefab"
  survives only within one server session.
- **The mark rides the creature instead:** ZDO extra data round-trips the world save, so
  a custom key (`rw_nemesis` = victim playerID) written onto the creature's ZDO IS the
  cross-session identity — the world save is the ledger. Owner-side writes only (the
  task 12 delegation pattern); at kill time the victim's client usually owns the
  attacker's ZDO, and when it doesn't, the write defers to the next encounter.
- **Kill attribution is vanilla's own:** `Character.m_lastHit.GetAttacker()` at
  `OnDeath`, processed on the victim's client — the mark's birthplace.
- **Star-up is safe on living creatures:** `SetMaxHealth` clamps only downward, so
  raising the level lifts the ceiling without healing. Never lower a living level.
- **GetHoverName is virtual** — the decorating-postfix nameplate pattern applies
  ("the troll that slew Nomad").
- **Despawns stay legitimate escapes:** night spawns (`s_despawnInDay`) and event mobs
  (`s_eventCreature`) evaporate by vanilla's rules; a despawned nemesis got away. The
  frozen-ZDO trap is tolerated, not touched: a frozen nemesis is simply not encountered.

Full cross-session tracking via our own creature store remains NOT planned — the ZDO-key
approach is strictly better and needs no new persistence.

**Config:** `EnableRivalry` master (already bound) plus one toggle per phase; every rate,
threshold, floor, and decay half-life in config.

**Acceptance (per phase, cumulative):**

- A: harness round-trips the ledger through the shipping writer; attribution books arson,
  planting, and healing presence to the right ids in-game; decay measured against config.
- B: a grudged player's zone measurably drifts harsher than a clean player's control zone
  (the four-decimal verification pattern); pickables refuse the grudged player while a clean
  player picks the same bush; a grudge title appears at threshold and replicates.
- C: dominance computes from the ledger, mercies measurable, one flip announcement per
  actual flip, silence below the floor.
- D: a hand-constructed contested zone (store edit — the supported path) escalates in a
  storm, war visible with a player present, exactly one resolution line.
- E: decided by its gate; if built, mark survives what it claims to survive, and no patch
  ever touches the frozen-ZDO trap.

---

## 14. `RelicSystem` — DESIGN AGREED 2026-08-26, not built. THE CAPSTONE — build last.

Consecrated places: sites where the world's story peaked become lasting landmarks with real
properties, marked by a physical stone. The world writes its own monuments. Owner's calls:

- **A relic is a PLACE, not an item.** Item-relics would reopen the no-prefabs locked
  decision; not chosen. The stone is an EXISTING vanilla prefab spawned by us (legal ground —
  both sides ship this mod, the hash resolves, no `CreateObjectsSorted` landmine).
- **All four story peaks consecrate:**
  1. *A great fire, survived* — scorch peaked past a threshold, then fully healed. Blessed.
  2. *A plague, cured* — plague past the spread threshold, then driven through zero (the
     permanence-through-zero mechanic is the trigger's backbone). Blessed.
  3. *A contest, resolved* — task 13's spawn war ends: wild won, blessed; blight won,
     cursed. This trigger ARRIVES with task 13 phase D; dormant until then.
  4. *A world recovery* — Stricken back to Stable/Flourishing consecrates ONE site, the
     zone whose healed delta defined the era. The rarest stone.
- **Real auras, modest** (all config): blessed ground — zone recovery drift x1.25, task 11
  exposure drains x1.5 while standing there, a warmth-like calm; cursed ground — recovery
  x0.8, exposure accrues x1.25, spawns slightly meaner at a low rate (task 12's star
  surface, gentler dial). Places worth pilgrimage, and places to walk around.
- **Destructible by players.** Breaking a stone lifts its blessing or curse — the world's
  memory CAN be vandalized — and the act books harm to the breaker in the task 13 rivalry
  ledger (attacker identity from the destruction hit; decompile gate). Until task 13 phase A
  exists, the booking is a log line. A desecrated site can consecrate again on a fresh peak
  cycle; while a stone stands, new peaks in that zone change nothing — the land's story is
  already told. One relic per zone.

**Honesty correction baked into the design:** vanilla runestone lore text is prefab-FIXED,
not per-instance — so the story does not ride vanilla text plumbing at all. The relic ledger
owns the story (event type, world day, blessed/cursed, attributed names where the rivalry
ledger knows them), and a client-side hover/interact decoration (the GetHoverName pattern)
reads it. The stone is the anchor; the ledger is the memoir. Exact prefab chosen at build
behind the decompile gate — needs: visible, dignified, destructible (or made destructible by
a client-added component — also gated).

**Placement and the headless trap:** consecration events are contact-driven by construction
(cures and contests need players), so someone is present when a stone rises — placement is
delegated to a present client (task 12's delegation rule), which has real terrain for ground
height. The server never calls `GetGroundHeight` for this (the returns-its-input trap).
Acceptance requires the stone standing ON the ground on a dedicated server, not floating or
buried.

**Peak tracking:** the zone store stays lean — RelicSystem tracks candidate peaks in its own
persisted ledger, `ragnarokswrath_relics_<uid>.dat` (same fail-safe / quarantine / no-BOM /
InvariantCulture contract): per-zone peak watermarks plus the standing relics. Consecration
fires on the through-zero / resolution / era transition, not on the peak itself.

**Sync:** relics are few — full-table replay on join via a GUID-prefixed routed RPC (the
TitleSync shape), plus a delta push on consecration/desecration. Clients need it for hover
text and aura application; auras execute exactly like task 12 acts (present client, owned
instances, ZoneSync-style cached state).

**Cross-system wiring (why this builds LAST):** exposure auras touch task 11; star surface,
presence rule, and delegation touch task 12; the contest trigger and vandalism booking touch
task 13. Build order 11 -> 12 -> 13 -> 14. `EnableRelic` already bound; each aura and
trigger gets its own config line.

**Acceptance:**

- Harness: peak-watermark state machine (peak, through-zero, consecrate-once, desecrate,
  re-arm), ledger round-trip through the shipping writer, aura math.
- In-game: a hand-driven cure cycle (store edit to peak, then real cure) raises a stone
  where predicted, hover tells the story; blessed-zone recovery and exposure drain measured
  against the aura multipliers (the four-decimal pattern); breaking the stone lifts the
  aura and logs/books the vandal; the relic survives a server restart.
- Dedicated: the stone stands on the ground, and consecration with no player present does
  not fire (it queues for next contact instead — nothing spawns blind on headless).
- Era monument: exactly one, on a real Stricken -> recovered transition.

- BepInEx pinned `denikson-BepInExPack_Valheim-5.4.2333` — confirmed current: it is the exact
  pack the live install runs.
- FireFront is deliberately NOT a manifest dependency (soft by design); the README sells the
  synergy instead.
- The package script refuses to build unless the THREE version homes agree (Plugin const,
  csproj, manifest) — the one manual-zip mistake worth automating away.
- First artifact: `RavenIron-RagnaroksWrath-0.7.1.zip` (46 KB, five files, flat).

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
