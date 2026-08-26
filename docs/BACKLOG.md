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

**Remaining:** `HealthSystem` (designed — task 11) · `ConsequenceSystem` (designed — task
12) · `RivalrySystem` · `RelicSystem` (no spec yet — design before building).

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

Still to come on this substrate, in whatever order earns it: storm gusts and frost breath
(same sync, new emitters), farming's growth/yield consumer (client reads depletion),
HealthSystem delivery.

## 10. Packaging — DONE 2026-08-26

Root-level `manifest.json`, player-facing `README.md`, `CHANGELOG.md`, generated 256x256
`icon.png`, and `tools\package.ps1` producing the flat Thunderstore zip in `dist\`
(gitignored). Modeled on SkaldSaga's release SHAPE only — that project is dead and stays
reference-only; nothing is linked or copied from it.

---

## 11. `HealthSystem` — DESIGN AGREED 2026-08-26, not built

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

## 12. `ConsequenceSystem` — DESIGN AGREED 2026-08-26, not built

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
