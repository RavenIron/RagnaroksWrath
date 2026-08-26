# Changelog

## 0.13.0

- **The land takes sides.** Every shaped zone now remembers who shaped it MOST: its
  dominant carer and its dominant despoiler, floor-gated so nobody wins ground they barely
  touched, hysteresis-held so the crown doesn't flap.
  - The dominant carer's presence heals the zone 25% faster, and sickness leaves them 50%
    faster on their own ground — the world's favour, earned in the ledger.
  - Holding three zones' memory earns **Warden** (care) or **Despoiler** (harm) under your
    nameplate.
  - When a zone genuinely changes hands between two rivals who both shaped it, the ground
    says so to whoever stands there — one line per actual taking; walkovers and fades pass
    in silence, and a solo world never hears this voice at all.

## 0.12.0

- **The land holds grudges.** Zones remember who hurt them (net of who tended them), and
  react to that person specifically:
  - Ground you wronged **drifts harsher under your feet** — healing up to halved, rot up
    to doubled, scaling with the grudge. Your friends walk the same ground untroubled.
  - Past a threshold, **its pickables refuse your hand** — "the land remembers what you
    did here" — while anyone else picks freely.
  - The worst offenders wear it: the **Ashbringer** title lands when any zone's grudge
    crosses the line, announced like every title.
- Grudges fade (48-hour half-life) and tending genuinely mollifies — care offsets harm
  point for point. The atonement loop is real: heal what you burned and the land forgets.
- Wire note: the zone sync now carries your personal grudge per zone (renamed RPC; server
  and clients update together, as ever).

## 0.11.1

- **Arson has a name now.** With FireFront 0.17.3+, the scorch a fire burns into each zone
  is booked as harm to whoever lit it — spread fires inherit their arsonist, natural and
  creature fires book nobody. With an older FireFront, attribution stays quietly dormant
  and fire scars accrue exactly as before. The ledger's harm column has its first writer;
  grudges come next.

## 0.11.0

- **The world keeps score (first ledger).** Every zone now remembers who tended it: planting
  crops books care to the planter (once per plant, ever), and standing by while damaged land
  heals books care to those present. Both fade over days — the world forgives on a long
  enough timeline. Nothing reads the ledger yet; grudges, contests and the spawn war build
  on it in coming releases. Stored beside the other world files, plain text, admin-editable.

## 0.10.0

- **The land pushes back.** Four consequences of drift, all reversible by curing the land,
  none of them ever touching player structures:
  - Plagued or scorched ground goes **barren** — berries, mushrooms and thistle refuse the
    hand, with a withered hover line saying why.
  - Corrupted ground breeds **starred enemies** — better odds on vanilla's own level-up
    roll, so the danger map follows the drift map. Wildlife is never starred.
  - Plagued zones **sicken wildlife** — deer and boar visibly slow, and recover on their
    own once the plague (or the animal) is gone.
  - Badly blighted soil **kills crops** — plants turn unhealthy and die at grow time,
    through the game's own machinery. Cure the land, replant, farm again.
- Each affected zone announces itself once — one line, the first time you stand in it —
  never per bush, never per deer.

## 0.9.0

- **Frost breath.** On land whose frost has drifted high, your breath fogs — a soft puff
  every few seconds, denser the colder the ground. It starts BELOW the chill threshold, so
  the land shows its cold before it bites, the way plague fogs before it sickens. Purely
  visual, procedural, local-only; a roof keeps it off you.

## 0.8.2

- **Shutdown no longer loses the last minute of sickness.** The exposure ledger saved on a
  60-second cadence but was missing from the shutdown flush, so a clean server stop or world
  exit could quietly drop up to a minute of exposure drift. Found live (it cost 0.02); the
  ledger now flushes alongside the zone store.

## 0.8.1

- **The sickness can now actually be felt.** In 0.8.0 the penalties ramped up from nothing at
  the moment a tier was announced, so "a sickness takes root in you" came with a 2% stamina
  penalty — invisible in the hands. Crossing a tier now bites at once (stamina ×0.85 the
  instant it takes hold, deepening as exposure climbs) and keeps ramping from there.
- **The sickness icon carries a readout.** While it is getting worse, the icon shows how far
  gone you are; once you are off plagued ground it becomes a real countdown to clean, in the
  game's own timer format — and it shortens when you are rested.

## 0.8.0

- **Plague sickness.** Standing on plagued ground now builds exposure on YOU — slowly, over
  tens of minutes. Stamina fails first, then healing; the sickness weakens but can never
  kill. It shows as a status icon (vanilla's own bar) and fades away from blighted land —
  faster rested, slower to take hold with poison resistance. Leaving and rejoining is not a
  cure: exposure is persisted per player, world-scoped, in the same admin-editable format as
  everything else.
- **The chill.** Land deep in frost now bites players the weather alone would not — unless
  they carry frost resistance, stand by a fire, or shelter. Never lethal, never Freezing.

## 0.7.1

First packaged build. Everything below verified live on a dedicated server.

- **Zone sync + plague miasma.** The server now pushes each player the zone state around
  them (absolute snapshots, self-healing on packet loss), and clients render a procedural
  grey-green fog in zones past the plague threshold. Fresh seeds stay invisible — the fog is
  how you *discover* a zone has turned, not a minimap.
- **Titles.** Stormrider, Plaguewalker, Winterborn — earned from world events, shown under
  nameplates, persisted world-scoped in an admin-editable ledger. Announced once, never spam.
- **World condition.** The land judges itself from total burden (Flourishing / Stable /
  Ailing / Stricken) and announces only real turning points — transitions are
  hysteresis-guarded so they cannot flap.
- **Ecology.** Sustained plague or scorch corrupts the ground under it, and corruption feeds
  plague growth — the first feedback loop. Severable by config.
- **Farming.** Dense crops tire a zone's soil over time; rest heals it. (Growth/yield effects
  read from this in a future build.)
- **Plague.** Grows where players linger, spreads zone-to-zone along its frontier, rides
  storms, dies back through winter or neglect; a cure that reaches zero is permanent.
- **Fire scars.** With FireFront installed, burning zones accrue Scorch that outlives the
  flames and slows the land's recovery. Without it, the system sleeps.
- **Devastating Storms.** Real vanilla events on our schedule; fire risk, wind and plague
  spread rise inside the storm's area only. The sky is never forced (default).
- **Seasons.** Gameplay-only clock with Seasonality auto-deferral.
- **Persistence.** World-scoped, atomic, fail-safe stores: corrupt files quarantine loudly
  instead of vanishing silently, values clamp on read as well as write, and everything is
  plain tab-separated text.
