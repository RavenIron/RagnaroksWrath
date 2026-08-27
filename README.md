# Ragnarok's Wrath

> *The land remembers who walked it.*

A Valheim world-simulation mod by **Raven Iron**. The world reacts and remembers: seasons turn, storms rage, plague takes root and creeps, fire leaves scars, farmed soil tires, the wild wages war on the blight, the land holds grudges against the people who hurt it — and raises stone monuments where its stories end. Every zone quietly keeps score of what has happened there, across restarts, forever. One DLL — install on server and clients.

**Not** a tracker, dashboard, or HUD mod. There is no UI. The world tells you itself: a sickly fog in a blighted valley, ash drifting over an old burn scar, a deer that staggers, a bush that refuses your hand specifically, runes glowing on a stone that remembers a fire.

---

## What it does today

### 🍂 Seasons
Spring, Summer, Fall, Winter on a configurable clock — driving **gameplay**, never visuals: fire risk, plague growth, frost, farming yield all swing with the season. Runs its own clock, or defers automatically to **Seasonality** (RustyMods) when installed — no second clock, no conflict.

### ⛈ Devastating Storms
Real vanilla events — banner, music, timer — fired on our schedule, positional and local: fire risk, wind and plague spread all rise **inside the storm's area** and nowhere else. A storm over contested ground escalates the war beneath it. And with FireFront installed, a storm can **strike**: a rare bolt of lightning lands near someone under it and starts a real fire — never in rain, never near anything player-built. The sky is never touched (Seasonality-safe); a config toggle exists for owners who run no weather mod and want the thunderstorm look.

### 🦠 Plague — in the land, and in you
A spreading, curable sickness that lives in the ground — and it starts on its own: every so often (rarer than daily by default, likelier on corrupted or burnt land, carried by storms) an outbreak quietly takes root where people live and walk. It grows where players linger, creeps along its frontier, feeds on corruption, rides storms — and dies back through winter or neglect. Clients see a **grey-green miasma** where it runs deep: no marker, no map icon, just the land looking wrong.

Stand in it and it gets into **you**: exposure builds over tens of minutes into a real sickness — stamina first, then healing — shown as a vanilla status icon, never a HUD. Leaving drains it; rest drains it faster; poison-resist mead slows the intake. Relogging is not a cure. High-frost ground chills you where vanilla wouldn't — a campfire or frost-resist mead answers it, and your breath fogs in the cold air.

### 🔥 Fire scars (with FireFront)
**FireFront** (Raven Iron) ships as a dependency — mod managers install the pair together — and fires char the land itself: burning zones accrue Scorch, which suppresses recovery and outlives the flames — and gray **ash drifts** over old burn scars, thinning as the land heals. Installing by hand? You need both mods (FireFront 0.17.2+, 0.17.3+ for arson attribution — the log tells you at boot if the pairing is stale). Without FireFront the system sleeps; Ragnarok's Wrath ships no second fire simulation.

### 🌱 Land that keeps score
Fertility tires under dense crops — and **tired fields grow slow**: crops in depleted soil take up to double the time, so resting a field genuinely pays. Blighted and burnt ground corrupts, corruption feeds the plague, and the world derives its own condition — Flourishing, Stable, Ailing, Stricken — announcing the turning points to everyone.

### 💀 Consequences
Sick land acts like it: berry bushes and mushrooms stop yielding on plagued or burnt ground, wildlife staggers visibly sick, crops in deeply blighted soil wither and die, and creatures born on corrupted ground come up **starred**. Player structures are never touched.

### ⚔️ The land takes sides
Every act is remembered, per zone, per player: planting and healing presence book **care**; arson books **harm** to the arsonist by name (FireFront identifies who lit it — even across the fire's whole spread). Grudged ground drifts harsher under your feet, refuses you berries it gives freely to others, and marks you — **Ashbringer** — while tended ground heals faster for its keeper and sheds their sickness sooner. Regional standing earns **Warden** or **Despoiler**. And where blight and genuine care collide, the zone goes to **war**: the blight's spawns star harder, the wild pours in to answer, storms escalate it, and the war ends one way or the other with a single line to whoever stands there.

### 🐗 The nemesis
The creature that kills you is marked in that moment: it gains a star, and its nameplate remembers — *slayer of Nomad*, counted on repeat offenses. The mark lives in the creature's own body and survives server restarts. If it despawns, it got away. Kill it for the only cure.

### 🗿 Relics — the world writes its own monuments
Where a story completes, a stone rises: a great fire fully healed, a plague driven out — blessed. A war lost to the blight — cursed. The land recovering from Stricken raises one rare stone on the ground that healed the most. Standing stones carry real auras (blessed ground heals quicker and sheds sickness faster; cursed ground sickens you and breeds meaner things) and wear **glowing runes** — gold or blood-red. The stones can be broken, but the land books the vandal, and it does not forget.

### 🏅 Titles
Earned from what happened to you, never from tracked stats: **Stormrider**, **Plaguewalker**, **Winterborn**, **Ashbringer**, **Warden**, **Despoiler**. Latest earned shows under your nameplate.

### 🛠 The `wrath` console
`wrath status`, `wrath zone`, `wrath relics` answer anywhere; `wrath zone set`, `wrath care set`, `wrath harm set`, `wrath save` run on the server — and typed in-game by an **admin**, they forward through vanilla's own admin gate, execute where the world lives, and reply to your screen. Server owners never need to hand-edit a save file again (though they can: every store is plain tab-separated text).

---

## Install

Drop `RagnaroksWrath.dll` into `BepInEx/plugins/` on the **server and every client**. The same DLL does both jobs: the server runs the simulation, clients render what it pushes and act where they stand.

Config appears at `BepInEx/config/com.raveniron.ragnarokswrath.cfg` — every system has its own on/off switch, every rate, threshold, and aura is tunable.

**Keep the config identical on the server and every client.** Several client-side gates (consequence thresholds, wildlife lists, relic dials) read local values; a client with a diverging config behaves differently on the same world. If the server and a client disagree about the mod *version*, the client warns once on screen — mismatched pairs fail safe, but features silently stop showing until they match.

## Plays well with

- **Seasonality** — detected and deferred to automatically (GUID verified against their source)
- **FireFront** — a listed dependency (installed automatically by mod managers); unlocks fire scarring, arson attribution, and storm lightning. Removing it is safe: those systems simply sleep.
- **AwayFromHome** — deliberately compatible: drift never ticks on zone load state, unattended bases never burn, and sweeps are staggered off its rescan

## Known limitations

- On a **Steam Cloud** singleplayer world, the mod's world-state files live beside the local save and do not travel with the cloud — move machines and the land starts a fresh memory. Dedicated servers and local worlds are unaffected.
- The mod adds **no prefabs, no assets, no bundles** — every visual is generated in code. This is deliberate and load-bearing; it will not change.

## Early build

Every system is live and dedicated-server-verified — most rates to four decimal places against prediction — but this is an early, evolving mod. Report anything strange: the logs are chatty in all the right places.
