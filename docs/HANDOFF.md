# Session handoff — 2026-08-26

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
- **Tasks 13–14 specced, not built** (owner's calls recorded per task — do not re-litigate;
  13 then 14, Relic capstone). Honour each spec's decompile gates.
- **Remaining, unnumbered:** build tasks 13–14; the storm-gust emitter (`Visuals\ParticleKit`
  is the shared substrate); farming's growth/yield consumer; the nameplate RENDER check
  (needs a second player); crop-wither quick half; the 0.8.2 flush verification (stop the
  server WHILE a player stands in plague — a logged-out player's ledger may already be
  cadence-clean); package + Thunderstore upload of 0.10.0.

## The live world (Dedicated, uid 4690126)

Genuine state, not test residue — do not wipe:

- An outbreak centred on zone (0,-1) at plague **~1.0** with corruption **0.7** (raised
  from ~0.30 by store edit for the task 12 empowerment test, kept as genuine state) —
  the zone now carries ALL FOUR consequence flags, and the corruption boost makes its
  plague effectively incurable by neglect. Winter or a config cure drains the plague;
  nothing but time off drains corruption. Seeded neighbours sit below the 0.15 floor.
- **A cold scar at zone (1,0), frost ~0.74** — staged for the chill/breath test and KEPT
  deliberately (owner's call): breath fogs there, the chill bites, and it only drains while
  someone stands in it. A `.prefrost` backup of the zone store sits beside it if it ever
  needs restoring.
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
