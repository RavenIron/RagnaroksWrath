# Session handoff — 2026-08-26 (updated through phase D, ~13:50)

For the next session picking this up cold. Read `CLAUDE.md` first (house rules, locked
decisions, verified-state ledger), then this for the operational state the docs don't carry.
`docs/BACKLOG.md` has per-task detail; `docs/reference/README.md` gates the engine fact
sheets; `docs/zone-clock-ownership.md` is the architecture decision everything drift-shaped
obeys.

## Where things stand

- **Backlog tasks 0–12: done.** RW at **0.10.0** (151/151 off-game tests), deployed AND
  verified by strings on both sides (Gale client profile + dedicated server — Program Files
  IS copyable from PowerShell when the server is stopped; the old "cannot write there"
  memory was circumstantial). FireFront at **0.17.2**. Both repos pushed under **RavenIron**.
- **Task 12 (ConsequenceSystem) VERIFIED LIVE same day** — all five checks by the owner:
  one-line announcement (once), withered/refusing pickables, slowed wildlife, starred
  spawns at expected rarity, both negative controls held. Crop withering recorded
  unobserved (plant a turnip in the outbreak for the quick half). Deploy near-miss to
  remember: a DLL built BEFORE the version bump shipped with task-12 code and a 0.9.0
  label — the strings audit caught it; identify builds by content, always.
- **Task 11 (HealthSystem) VERIFIED LIVE end to end** — accrual to four decimals, tiers
  felt (after the 0.8.1 step fix — read that backlog entry for the lesson: assert what the
  player was PROMISED, not what the function computes), relog/restart persistence, decay to
  through-zero row removal, the chill with its campfire gate, frost breath with its roof
  gate. Unobserved, accepted: tier-3 line, live mead/rested rate change.
- **0.8.2 flush-fix verification PENDING:** needs a player to get exposed, then a server
  stop — the health ledger's mtime must land beside Dedicated.db's instead of up to 60s
  earlier. Today's stops had an empty ledger, so it has never been observed doing its job.
- **PHASE D (spawn war) BUILT at 0.15.0 and STAGED, verification IN FLIGHT at handoff:**
  contested = blight >= 0.5 AND total zone care >= 0.3; storms x2 the intensity (rule 4's
  breadcrumb cashed); blight side rides the task-12 star surface x(1+bonus x intensity);
  wild side is vanilla's OWN pheromone machinery (`SE_Stats.m_pheromoneTarget` — the Bog
  Witch mead fields, public, read by UpdateSpawnList) via invisible TTL'd "war horn" SEs
  on players standing contested ground. Resolution at the contested->uncontested edge
  (wild wins if blight broke, blight wins if care faded), ONE Centre line. Wire is now
  ...zone_state3 (war intensity per zone in the ring). THE OUTBREAK (0,-1) IS STAGED AS
  WAR GROUND: care hand-set to 0.5 (backup `.prephased`). **HORN DISCREPANCY RESOLVED
  2026-08-26 14:34, client chain VERIFIED LIVE at 0.15.1 both ends:** the silence had two
  boring causes stacked — the owner was standing in (1,-1), one zone EAST of the war (the
  contact-tick block in the zone store proved it), and the server run of the moment was a
  13:36 build of 0.15.0 that PREDATED the war-state edge log, so its silence proved
  nothing (audit the instrument). On the 0.15.1 restart the server logged `war state: 1
  contested zone(s)` first tick, and once the owner walked into (0,-1) proper the client
  logged `war intensity 1.0 underfoot.` then `3 war horn(s) ready for contested ground.`
  — server war state -> ring push -> client cache -> horn build, every link observed. The
  "horns sounding" audio was never ours; the horns are silent SEs.
  **ENGINE FACT, decompile-read 2026-08-26 (SpawnSystem.UpdateSpawnList body):** vanilla's
  `m_pheromoneMaxInstanceOverride` widens the instance-cap GATE but NOT the group-size
  BUDGET — the spawn-count line computes `m_maxSpawned - currentCount` from the spawner's
  RAW `m_maxSpawned`, ignoring the override. So pheromones can never push a population
  above vanilla's stock cap; they only refill toward it faster (and `GetNrOfInstances`
  at range 0 counts the WHOLE loaded area, not the zone). Observed live: `Spawned Deer
  x 0` — a line only reachable when a pheromone raised the gate past ambient while the
  raw-cap arithmetic zeroed the group. That line is also PROOF the horn's prefab-
  reference equality holds and the override applies: without a pheromone the pass breaks
  before logging. Also: Hare's spawner is Mistlands-tagged, biome-gated out before
  pheromones are consulted — in Meadows only Deer/Boar can ever answer the horn. Configs
  raised to ContestWildSpawnChance=100 / ContestWildMaxSpawned=15 both ends (the max
  override is gate-only given the quirk). The war therefore reads as REFILL PRESSURE:
  visible only when local wildlife is below vanilla's cap — hunt the ambient deer down,
  then horns refill at 100% chance from 40-80m out. Whether refill pressure is enough
  wild-side teeth, or phase D needs its own modest spawn budget, is a DESIGN DECISION
  for Raven Iron — the "no spawn patch at all" intent has now met vanilla's ceiling.
  **WILD SIDE VERIFIED LIVE 2026-08-26 15:18 (0.15.2):** the 0.15.2 war census (verbose-
  gated, 60s, logs per-target instances loaded + within 200m — the numbers vanilla's
  budget actually sees) showed the truth in one line: 6 deer and 8 boar ALREADY within
  200m, invisible in the fog — the population was above vanilla's caps the whole time,
  "no animals came" was "the animals were already here". Owner culled deer 6 -> 2;
  next attempt logged `Spawned Deer x 2`. Boar stayed `x 0` at 5 loaded >= its raw cap —
  the negative control proving the gate-not-budget engine fact in the same breath (and
  tamed boar COUNT: a pen near war ground permanently mutes the boar horn, same vanilla
  behavior that stops wild boar near pens). Full loop: war computed -> synced -> horns
  -> pheromone -> vanilla spawner -> budget open -> creatures spawned. Census stays in
  the shipping code behind VerboseLogging.
  **RESOLUTION EDGE VERIFIED server-side 2026-08-26 15:59:** `war in (0,-1) resolved:
  Blight.` + `war state: 0 contested zone(s)`, exactly once. THE HALF-LIFE ROUTE ABOVE
  IS WRONG — `RivalryHalfLifeHours` has an AcceptableValueRange floor of 1h (deliberate:
  "0 would disable decay"), BepInEx silently clamps AND persists the clamped value back
  into the cfg. Working route, used live: stop server, edit the war zone's ledger care
  to just ABOVE threshold (0.31 vs 0.30), restart — war re-derives, decay crosses in
  minutes, edge fires mid-session. TWO LESSONS BAKED IN: (1) the owner reported seeing
  the Centre line ~6 min BEFORE the edge fired; the ledger disproved it (care 0.3033,
  still above threshold, decay monotonic) — an expected announcement will be "seen"
  early; trust the store over the eyewitness. A title (Ashbringer) landed at the false
  sighting's timestamp. (2) decay ran ~3x slower than pure math predicts and it is NOT
  a bug: the owner standing in the fog kept the zone contacted, drift healed plague/
  scorch on contact, healing booked care to them — the defender holds ground by standing
  on it. Expect slow care fades wherever a player camps damaged ground. Ledger NOT
  restored from `.prephased` (that predates the afternoon's real history — Ashbringer's
  harm, the tending care); post-test ledger kept, half-life 48h rebound, fresh boot
  correctly derives NO war at care 0.297.
  **DESIGN DECIDED 2026-08-26 (0.15.3): REFILL PRESSURE.** Raven Iron chose vanilla's
  machinery as-is over a mod-owned spawn budget; ContestWildSpawnChance=100 and
  ContestWildMaxSpawned=15 are the shipped defaults. Locked-decisions row added to
  CLAUDE.md. Phase D is CLOSED. 0.15.3 built but NOT yet deployed to the live server
  (running 0.15.1) or client (0.15.2) — the live cfgs already carry 100/15 explicitly,
  so only fresh installs are affected; deploy at the next natural restart.
  Player-side sighting of the true resolution Centre line:
  owner could NOT confirm (the only confident sighting was the disproven early one).
  ACCEPTED UNOBSERVED on component evidence — the Centre pipe is live-verified since
  v0.2.2 (storm announcements seen on screen, same MessageFeed.ToPlayersNear path) and
  the call site's execution is logged with a player in the area. To observe it properly
  someday: HalfLife=1 (valid, no clamp), care=0.31, restart, watch deliberately —
  expect ~5-10 min, healing-presence income stretches pure-decay math ~3x. A dead-ledger edit CANNOT test resolution: a
  restart re-derives war from the store, so care edited below 0.3 just means no war and
  no edge (the Winterborn shrug — UpdateWar's own comment). Live route instead: set
  `RivalryHalfLifeHours = 0.05` in the server cfg, restart, stand within 64m of (0,-64);
  war re-derives (care 0.51), decays past 0.3 in ~2.3 min, edge fires mid-session ->
  expect exactly ONE Centre line "The blight has claimed this ground." + server log `war
  in (0,-1) resolved: Blight.` + `war state: 0 contested zone(s)`. Then restore half-life
  48 and the ledger from `.prephased`. Do not cure the outbreak to force a wild win — it
  is guarded world state.
- **Task 13 PHASES A, B COMPLETE and live-verified; PHASE C BUILT (RW 0.14.1, FireFront
  0.17.3):** the ledger with all three writers proven (A); the grudge with three teeth
  verified — Ashbringer, the personal pick refusal, and the drift tooth measured at
  **0.01750/h observed vs 0.01750/h predicted, EXACT**, after the first window was
  contaminated by the credit-on-contact backlog (the runbook's own trap; re-baseline
  after the backlog clears, save-to-save). Phase C (dominance, mercies x1.25 zone /
  x1.5 sickness, Warden/Despoiler, flip voice) is harness-pinned (180+) and deployed;
  its in-game bits await play: mercy rate measurement, a 3-zone title, and the flip
  announcement which STRUCTURALLY requires two players. Phase D CLOSED 2026-08-26 (see
  above). PHASE E VERIFIED LIVE 2026-08-26 at 0.16.0: a boar killed Nomad, took its star
  and slayer line (owner's eyes), and KEPT BOTH through a full server bounce — the
  ZDO-key mark surviving ZDOID regeneration is the design's strong claim, observed.
  TASK 13 COMPLETE, all five phases. Both ends deployed at 0.16.0. Task 14 (RelicSystem,
  the capstone) is the last system in the mod.
  Scorch ash (0.14.x) verified by eye — burn scars visibly dust now, fading with healing.
  Deferred with cause: wildlife-flee and hostiles-seek-you (no acceptance criteria,
  BaseAI is the riskiest surface — own pass, own gates).
- **Version note:** client runs 0.14.1; server runs 0.14.0 until its next stop (the
  0.14.1 delta is client-only ash tuning). FireFront 0.17.3 both sides.
- **Task 14 specced, not built** (owner's calls recorded; Relic is the capstone, after 13).
- **Remaining, unnumbered:** task 13 phases B–E, task 14; the storm-gust emitter
  (`Visuals\ParticleKit` is the substrate); farming's growth/yield consumer; the nameplate
  RENDER check (needs a second player); the crop-wither slow half (a turnip stands in the
  outbreak at blight 1.0 — it should be visibly unhealthy now and die at grow time);
  package + Thunderstore upload (now 0.11.1 + FireFront 0.17.3 as a pair).

## The live world (Dedicated, uid 4690126)

Genuine state, not test residue — do not wipe:

- An outbreak centred on zone (0,-1) at plague **~1.0** with corruption **0.7** (raised
  from ~0.30 by store edit for the task 12 empowerment test, kept as genuine state) —
  the zone now carries ALL FOUR consequence flags, and the corruption boost makes its
  plague effectively incurable by neglect. Winter or a config cure drains the plague;
  nothing but time off drains corruption. Seeded neighbours sit below the 0.15 floor.
- **A cold scar at zone (1,0), frost ~0.74** — staged for the chill/breath test and KEPT
  deliberately (owner's call): breath fogs there, the chill bites, and it only drains while
  someone stands in it. `.prefrost`/`.pretask12` zone-store backups sit beside it.
- **A burn scar across zones (1,-1)/(1,0)/(0,-1)/(1,-2)** — the owner's own arson test
  (2026-08-26): a beech lit south of the scar spread four zones before the extinguish key
  and a server cycle killed it. Scorch ~0.21 in (1,-1), and the rivalry ledger bills
  775624 exactly 0.2664 harm for it. Genuine history now — do not clean it up.
- **The rivalry ledger** `ragnarokswrath_rivalry_4690126.dat`: 775624 carries care across
  a dozen zones (tending + healing presence) and the arson harm above. Both columns decay
  at a 48h half-life.
- Titles ledger: `ragnarokswrath_titles_4690126.dat` — Nomad (775624) = Plaguewalker.
- Health ledger `ragnarokswrath_health_4690126.dat`: header-only right now (Nomad recovered
  fully; the row through-zero-deleted itself, which is correct).
- Five turnips in zone (-1,0) tiring the soil; scorch from the fire test healing slowly.
- Stores live in `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local\`, plain TSV,
  hand-editable (a supported write path — plague AND frost were both seeded that way; stamp
  the contact column with fresh `DateTime.UtcNow.Ticks` when editing, or the backlog credits
  the elapsed gap and drains your edit on first contact).

## Runbook (the part that cost round-trips to learn)

- **Deploy targets.** Client: `%APPDATA%\com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins\RagnaroksWrath\`
  (the user launches through GALE — the Steam folder loads nothing). Server:
  `C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\plugins\RagnaroksWrath\`.
  FireFront and ServerDevcommands are installed both sides too. **A running game locks its
  DLL** — ask the user to quit before copying; verify a deploy by `strings` on the copied
  file, never by trusting the cp.
- **Bump the version on every deploy** (Plugin const + csproj together; `package.ps1` enforces
  manifest agreement) — it's the only way to know which build a log came from.
- **Server start** (background, output to a scratch log):
  `cd <server dir> && SteamAppId=892970 ./valheim_server.exe -nographics -batchmode -name "My server" -port 2456 -world "Dedicated" -password "secret" -crossplay`
  Every restart mints a NEW join code — grep the console log for `registered with join code`
  and hand it to the user each time. Stop via `taskkill` (graceful first), then confirm
  `Dedicated.db` mtime moved — check the save, not the process.
- **Logs.** Server: `<server dir>\BepInEx\LogOutput.log` (recreated each boot — watches on it
  die across restarts; re-arm). Client: Gale profile `BepInEx\LogOutput.log`.
- **Verification pattern that works:** background `until`-loop watches on the log and on the
  store file; the zone store is a 120s-autosave SNAPSHOT, so absence in the file means "not
  saved yet", not "not happening". Predict the number before reading it — every verified rate
  so far matched prediction once credit-on-contact backlogs were accounted (contact stamps
  keep accruing across server downtime; first contact pays the backlog).
- **Config edits** need a server restart to take effect; BepInEx rewrites the cfg on exit, so
  don't edit a client's cfg while its game runs. `VerboseLogging` gates the per-tick lines —
  currently OFF both sides.
- **Tooling quirks of this machine:** `python` is the Store stub (`dnread.py` dead — use
  `ilspycmd`, installed globally; ALWAYS decompile before designing against a game API, and
  read bodies, not signatures). Bash here has perl; PowerShell doesn't. PowerShell 5.1:
  no `&&`, and `2>&1` on native exes fakes failures. gh CLI is installed and authed as
  RavenIron; repo-local git email is ravenirongames@gmail.com in both repos.

## Cross-repo contract (do not break silently)

`FireManager.CollectActiveFirePositions(List<Vector3>)` in FireFront is resolved by RW via
reflection. Renaming or re-signing it in FireFront disarms RW's Scorch with only a per-tick
warning on the RW side. It is comment-documented at both ends.
