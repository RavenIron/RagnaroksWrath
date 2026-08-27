# Changelog

## 0.24.0

- **Seasons (shudnal) is now a recognised season source.** Until now only Seasonality
  (RustyMods) was detected; anyone running shudnal's Seasons got a second, disagreeing
  season clock from us driving fire risk, plague growth and farming yield. We now defer
  to whichever of the two is installed (they declare themselves mutually incompatible,
  so it is never both) and run our own clock only when neither is present. shudnal's
  season is read by reflection from its own state — not its global keys, which are off
  by default and renameable — and, as always, consumed as gameplay state only: their
  mod owns everything you see.

## 0.23.1

- Config guidance from the first live lightning session: the storm-look setting's
  default environment (`ThunderStorm`) is rainy, and rain rightly suppresses lightning
  fires — so look and bolts were mutually exclusive. Both descriptions now point at
  vanilla's dry storm (`Eikthyr`) for owners who want the sky *and* the fire. No
  behavior changed.

## 0.23.0

- **Storms strike.** A Devastating Storm over your head can now land a bolt of
  lightning nearby — and the fire it starts is real, handled entirely by FireFront's
  own simulation. Bolts are rare (about one storm in three at defaults), only ever
  land near an online player, never fall in rain, and never within 30m of anything
  player-built: the storm menaces the wild, not the homestead. Lightning fires are
  natural — nobody is booked for the sky's work. Everything is config: the rate, the
  landing ring, the standoff, or off entirely.
- **FireFront is now a listed dependency.** Mod managers install the pair together,
  so the fire half of the world can no longer be silently missing. Installing by
  hand still works both ways — without FireFront the fire systems just sleep — and
  the log now tattles at boot if your FireFront is too old for what this version
  expects (0.17.2 for fire memory, 0.17.3 for arson attribution).

## 0.22.1 – 0.22.3

- **Zone announcements actually arrive now.** Two lifelong delivery bugs stacked: a
  dedicated server could never reach remote players with zone-local lines, and the
  distance check lost anyone standing on high ground. Every zone-local message —
  war resolutions, consecrations, contest flips — now routes to each player properly
  and measures distance flat. If you never saw a centre-screen line before, this is
  why.

## 0.22.0

- **Plagues begin on their own.** Until now every outbreak needed an admin's hand —
  which meant a fresh public world could run forever without the blight arc ever
  starting. Genesis fixes that: every so often (about twice a day of played time by
  default, config-tunable) sickness quietly takes root in ground players actually
  touch — likelier on corrupted or burnt land, carried by storms. The seed is
  invisible until it grows; outbreaks are discovered, never announced. All the old
  containment rules still hold, and the old admin instruments still work.
- **Version mismatches are loud now.** A client running a different mod version than
  the server gets one warning — log and corner message — instead of features silently
  not showing. Mismatched pairs still fail safe; now they also fail audibly.
- README: keep the config identical on server and clients — several client-side gates
  read local values.

## 0.21.0

- **`wrath` from the comfort of F5.** Mutations typed in-game now forward to the server
  through vanilla's own remote-command pipe: the server checks you against
  adminlist.txt, refuses everyone else with "You are not admin", logs the admin and the
  exact line, and runs the command where the stores live. Reads still answer locally;
  confirm a forwarded edit with `wrath zone <x> <y>` a sync later.

## 0.20.1

- The farming boot line stopped promising what 0.20.0 already delivered.

## 0.20.0

- **Tired fields grow slow.** Fertility depletion — written by the farming sweep since
  0.6.0 — is finally felt: crops in depleted soil take longer to grow, up to double on
  fully exhausted ground (linear, configurable, off at 1). Resting a field now
  genuinely pays, closing the loop the depletion writer opened thirteen versions ago.
  Wild trees and bushes owe farmland's memory nothing — only the crop list pays.

## 0.19.0

- **The `wrath` console.** `wrath status`, `wrath zone [x y]`, `wrath zone set`,
  `wrath care/harm set`, `wrath relics`, `wrath save`. Reads answer everywhere (a pure
  client sees the synced view); mutations run only where the stores live — the server's
  own console or a listen host. Zone edits stamp fresh contact automatically, so the
  credit-on-contact backlog can never again eat a staged value before it is measured.
  Retires the stop-edit-copy-restart dance that one verification day performed five times.

## 0.18.0

- **Runes on the stones.** Standing relics now wear their nordic design: fehu, algiz,
  gebo and thurisaz rise slowly around every consecrated stone — gold where the ground
  is blessed, a dull red where it is cursed. Drawn in code, stroke by stroke, like
  everything else this mod renders: no assets, no textures touched, the fourth emitter
  on the same template as the fog, the frost and the ash.

## 0.17.2

- Arm the relic wire where a SOLO client actually runs (ConsequenceEffects' loop, the
  ZoneSync pattern) — the nameplate path only fires rendering someone else's plate.

## 0.17.1

- The stone that never rose: 0.17.0's first consecration was recorded perfectly and its
  placement request vanished into a client that had never armed its handler — pure
  clients tick no world systems, the nameplate-patch lesson, relearned. Clients now arm
  the relic wire on the render path, and placement is fire-and-forget no longer: the
  server retries until a client confirms the stone stands, the confirmation is
  persisted, and asking twice never builds twice. A 0.17.0 ledger row loads unconfirmed,
  so the lost first monument raises itself on the next visit.

## 0.17.0

- **Consecrated places.** The world now writes its own monuments: where a story
  completes, a stone rises. A great fire fully healed, a plague driven through zero —
  blessed. A spawn war resolved — blessed if the wild took the ground back, cursed if
  the blight claimed it. And rarest of all, the land recovering from Stricken raises
  one stone on the ground whose healing defined the era.
- Blessed ground heals quicker and sheds sickness faster while you stand on it; cursed
  ground sulks, sickens you faster, and breeds slightly meaner things. The stone speaks
  once to whoever arrives — and it can be broken, which lifts the aura, but the land
  books the vandal into its ledger. A desecrated site can earn a new stone the next
  time its story peaks and completes.

## 0.16.0

- **The nemesis.** The creature that kills you is marked in that moment: it climbs a
  level (up to two stars) and its nameplate remembers — *slayer of Nomad*, counted on
  repeat offenses. The mark lives in the creature's own body and travels with the world
  save; a nemesis that despawns got away. Kill it for the only cure.

## 0.15.3

- **The wild side's design is settled: refill pressure.** On contested ground the war
  keeps the wildlife topped up and keen — spawn chance 100% and a widened attempt gate
  (now the defaults) — but it deliberately never crowds a population past vanilla's own
  caps. The engine's pheromone override widens the gate, not the budget; we ship what
  the engine honestly supports rather than bolting on a second spawner.

## 0.15.2

- War census (verbose only): every minute on contested ground, log how many of each
  horn target are loaded and how many stand within 200m — the exact numbers vanilla's
  spawn budget sees. Added because "no animals came" turned out to mean "six deer and
  eight boar were already here, hidden in the fog".

## 0.15.1

- War-state edge logs on both sides: the server logs every change in the contested-zone
  count, the client logs once when it first sees war underfoot. Instrument before
  guessing — the difference between "the server never computed a war" and "the wire
  lost it" should never cost a round-trip again.

## 0.15.0

- **The spawn war.** A blighted zone that people genuinely fight for — sick past the line
  AND tended past its own — becomes CONTESTED ground, and both sides answer: the blight's
  spawns come up starred at doubled odds, while the wild surges to its defenders through
  the game's own spawn-attraction machinery (more deer, boar and hare answering the war
  horn near anyone standing their ground). A Devastating Storm overhead escalates the
  whole thing.
- Wars end when one side's drift wins: the land healing past the line is the wild's
  victory, the tending fading while blight stands is the blight's — announced once, to
  whoever is there to hear it. Dangerous ground that resolves itself.

## 0.14.1

- Ash made properly visible (bigger, darker, denser, tighter) — 0.14.0's motes were
  arithmetic-invisible at real scar scorch.

## 0.14.0

- **Ash over the burn scars.** Zones the fire marked now show it: gray ash motes drift
  down over ground whose scorch runs high, thinning as the land heals — the memory of
  fire, visible, on the same clock as the healing itself. FireFront's living flames and
  its permanent dirt-paint are separate and untouched.

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
