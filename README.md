# Ragnarok's Wrath

> *The land remembers who walked it.*

A Valheim world-simulation mod by **Raven Iron**. The world reacts and remembers: seasons turn, storms rage, plague takes root and creeps, fire leaves scars, farmed soil tires — and every zone quietly keeps score of what has happened there, across restarts, forever. One DLL — install on server and clients.

**Not** a tracker, dashboard, or HUD mod. There is no UI. The world tells you itself: a sickly fog in a blighted valley, a storm announcement as the sky stays honest, a title under a player's name.

---

## What it does today

### 🍂 Seasons
Spring, Summer, Fall, Winter on a configurable clock — driving **gameplay**, never visuals: fire risk, plague growth, frost, farming yield all swing with the season. Runs its own clock, or defers automatically to **Seasonality** (RustyMods) when installed — no second clock, no conflict.

### ⛈ Devastating Storms
Real vanilla events — banner, music, timer — fired on our schedule, positional and local: fire risk, wind and plague spread all rise **inside the storm's area** and nowhere else. The sky is never touched (Seasonality-safe); a config toggle exists for owners who run no weather mod and want the thunderstorm look.

### 🦠 Plague
A spreading, curable sickness that lives in the land. It grows where players linger, creeps zone to zone along its frontier, feeds on corrupted ground, rides storms — and dies back through winter or neglect. Above a threshold, clients render a **grey-green miasma** drifting through the sick zone: no marker, no map icon, just the land looking wrong.

### 🔥 Fire scars (with FireFront)
Install **FireFront** (Raven Iron) and fires char the land itself: burning zones accrue Scorch, which suppresses recovery and outlives the flames. Without FireFront the system sleeps — Ragnarok's Wrath ships no second fire simulation.

### 🌱 Land that keeps score
Fertility tires under dense crops and rests back to health. Blighted and burnt ground corrupts, and corruption feeds the plague. The world derives its own overall condition — Flourishing, Stable, Ailing, Stricken — and announces the turning points to everyone.

### 🏅 Titles
Earned from what happened to you, never from tracked stats: **Stormrider** (caught inside a devastating storm), **Plaguewalker** (walked the blight), **Winterborn** (endured the winter). Latest earned shows under your nameplate. Admin-editable ledger on the server.

---

## Install

Drop `RagnaroksWrath.dll` into `BepInEx/plugins/` on the **server and every client**. The same DLL does both jobs: the server runs the simulation, clients render what it pushes.

Config appears at `BepInEx/config/com.raveniron.ragnarokswrath.cfg` — every system has its own on/off switch, every rate is tunable, and the world's save data is plain tab-separated text an admin can read and hand-edit.

## Plays well with

- **Seasonality** — detected and deferred to automatically
- **FireFront** — optional; unlocks fire scarring
- **AwayFromHome** — deliberately compatible: drift never ticks on zone load state, and sweeps are staggered off its rescan

## Early build

Systems are live and server-verified, but this is an early, evolving mod. Report anything strange — the logs are chatty in all the right places.
