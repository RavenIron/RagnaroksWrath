# Zone clock ownership — decide before FireSystem

**The constraint.** `ZoneClock.CreditOnContact(zone)` *consumes* the backlog: it overwrites
`_lastContactUtcTicks[zone]` and returns the elapsed seconds exactly once. Whoever calls it
second gets ~0. `BiomeStateSystem` already calls it for every contacted zone, every tick.

Fire, Plague, Ecology and Farming all want "how long since anything happened here" too. Left
undecided, the failure mode is quiet and ugly: two systems calling `CreditOnContact` on the same
zone split the elapsed time by tick-ordering accident, both run at a fraction of their intended
rate, and every individual log line looks healthy. This is the same genus as the epsilon bug —
a simulation that runs slow instead of failing.

---

## Option A — one owner publishes deltas

`BiomeStateSystem` stays the only caller. After crediting a zone it publishes the delta
(callback, event, or a `LastCredited` side-channel on `ZoneClock`), and other systems consume
the same number.

- ✅ One ledger, one cap, one persistence format (the store already saves one `contactTicks`).
- ❌ Consumers tick at different intervals, so each needs its own per-zone accumulator of
  published deltas it hasn't processed yet — the bookkeeping we were trying to avoid, now
  duplicated per consumer.
- ❌ Couples every consumer's correctness to BiomeState being enabled. An admin who toggles
  `EnableBiomeState` off silently freezes plague growth too.

## Option B — per-consumer ledgers

Key the clock by (system, zone): each system gets its own independent `CreditOnContact`.

- ✅ No coupling, no ordering sensitivity; each system reasons about its own elapsed time.
- ❌ Persistence saves one `contactTicks` per zone. Either the format grows a column per
  system (a format bump for every new system — exactly what the tab format was chosen to
  avoid), or only Biome's ledger persists and every other system loses its backlog on restart.
- ❌ Memory and save size scale with zones × systems.
- ❌ `MaxCreditSeconds` caps each ledger separately, so "a month away credits one day" becomes
  "one day *per system*" — a subtle multiplication of the very thing the cap bounds.

## Option C — time evolution is centralised; other systems write state, not time (recommended)

Keep the invariant **"exactly one clock consumer" by construction** rather than by discipline:

- `BiomeStateSystem` (with `BiomeDrift`) remains the *only* place elapsed time is turned into
  state change. `BiomeDrift.Apply` grows the per-field evolution the other systems need —
  plague growth from `PlagueGrowthMultiplier × Corruption`, scorch decay, fertility recovery —
  all applied in the same single pass that already credits the zone.
- Fire, Plague, Farming become **event systems**: they *write* state (`Set` a scorch spike, a
  plague seed, a fertility hit) and handle their own live, tick-driven behaviour (fire spread
  while a player is present; plague hopping to an adjacent zone), using their own
  `Tick(deltaSeconds)` — real per-tick time, no zone clock needed.
- FireSystem in particular **must not use the zone clock at all**: the AwayFromHome constraint
  ("never burn unattended bases") means fire only acts while a player is present, which is
  precisely what live tick time gives and offline credit does not.

- ✅ No new persistence format, no accumulators, no coupling: the "one consumer" rule is
  structural, not a comment somebody has to remember.
- ✅ Matches the backlog's own framing — BiomeState is "the substrate every later system
  reads". Time-based evolution *is* the substrate.
- ❌ `BiomeDrift.Apply` accretes parameters as systems land (plague growth multiplier, etc.).
  Acceptable: it is pure and fully covered by the harness, which is exactly where accreting
  logic should live.
- ❌ A system wanting time-evolution that cannot be expressed as per-field drift (none known
  yet) would force revisiting this. Cross that bridge when a concrete case exists.

---

## Decision

**Option C.** Adopted 2026-08-25 unless overturned: the zone clock has exactly one consumer
(`BiomeStateSystem`), time-based evolution lives in `BiomeDrift.Apply`, and every other system
either writes state events or uses its own live tick time. FireSystem uses no zone clock, which
the AwayFromHome constraint independently requires anyway.
