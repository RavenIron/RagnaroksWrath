# Session handoff — 2026-08-26

For the next session picking this up cold. Read `CLAUDE.md` first (house rules, locked
decisions, verified-state ledger), then this for the operational state the docs don't carry.
`docs/BACKLOG.md` has per-task detail; `docs/reference/README.md` gates the engine fact
sheets; `docs/zone-clock-ownership.md` is the architecture decision everything drift-shaped
obeys.

## Where things stand

- **Backlog tasks 0–10: done.** RW at **0.7.1** (117/117 off-game tests), FireFront at
  **0.17.2** (its 0.18.0 never shipped — the `CollectActiveFirePositions` cross-mod contract
  landed in 0.17.2). Both repos on GitHub under **RavenIron**, clean and pushed.
- **First release artifact exists:** `tools\package.ps1` → `dist\RavenIron-RagnaroksWrath-<v>.zip`.
  Not yet uploaded to Thunderstore — that step is the user's, in a browser.
- **Remaining, unnumbered:** design conversations for HealthSystem / Consequence / Rivalry /
  Relic (no spec anywhere — do not invent them without asking); storm-gust and frost-breath
  emitters on the existing ZoneSync; farming's growth/yield consumer (client-side, reads
  synced depletion); the nameplate RENDER check (needs a second player looking at a titled
  one — everything up to the pixels is verified).

## The live world (Dedicated, uid 4690126)

Genuine state, not test residue — do not wipe:

- An outbreak centred on zone (0,-1) at plague ~0.95, corruption-fed, four seeded neighbours.
  It regrows fast (corruption boost); winter or a config cure drains it.
- Titles ledger: `ragnarokswrath_titles_4690126.dat` — Nomad (775624) = Plaguewalker.
- Five turnips in zone (-1,0) tiring the soil; scorch from the fire test healing slowly.
- Stores live in `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local\`, plain TSV,
  hand-editable (that's a supported write path — the plague was seeded that way).

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
