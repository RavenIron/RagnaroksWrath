# Creature persistence and nemesis-marking facts

Decompile-verified 2026-08-26 on THIS machine (`ilspycmd -r <Managed> assembly_valheim.dll
-t ZDO|ZDOMan|Character|BaseAI|MonsterAI`, live Steam install) for the task 13 phase E
feasibility gate. Unlike the six imported sheets, these were read against the current game
version on the box that runs it.

## 1. ZDOIDs are NOT stable across a server restart

`ZDO.Load(ZPackage, int)` opens with:

```csharp
m_uid.SetID(++ZDOID.m_loadID);
```

Every ZDO gets a **fresh sequential id at world load**. The saved world does not preserve
ZDOIDs. Consequences:

- Any cross-session identity keyed by ZDOID (in our own store, in a config, anywhere)
  silently dangles after every server restart.
- WITHIN a session, ZDOIDs are stable from creation/load to shutdown — session-scoped
  keying is fine.

## 2. ZDO custom keys persist through the world save — the mark rides the creature

`ZDO.Save`/`ZDO.Load` round-trip the full extra-data tables (floats, ints, longs, strings,
byte arrays) for `Persistent` ZDOs. A custom hash key written onto a creature's ZDO
(e.g. `rw_nemesis` = victim playerID) **survives restarts with the creature**, no stable
ZDOID needed — the world save is the ledger.

Caveat that travels with every ZDO write: **only the owner's writes replicate.** A
non-owner `Set` is local graffiti. Mark writes must run on the machine that owns the
creature's ZDO at that moment (the task 12 delegation pattern). At the moment a creature
kills a player, the victim's client very likely owns the attacker's ZDO (nearest player);
when it does not, defer the write to the next encounter by whoever owns it then — and if
the session ends first, the nemesis got away.

## 3. Kill attribution: `m_lastHit` on the victim, resolved at death

`Character` caches `m_lastHit` (a `HitData`); `Character.OnDeath()` attributes via
`m_lastHit.GetAttacker()` — this is vanilla's own kill-stats path (line ~2439:
`m_lastHit.GetAttacker() == Player.m_localPlayer`). A player's death processes on the
owning client, so the victim's machine holds the attacker reference at exactly the moment
the mark must be born.

## 4. Starring up a LIVING creature is safe — SetMaxHealth clamps only downward

`Character.SetLevel(int)` writes `s_level` to the ZDO and calls `SetupMaxHealth()` →
`SetMaxHealth(base × level)`. The body of `SetMaxHealth`:

```csharp
m_nview.GetZDO().Set(ZDOVars.s_maxHealth, health);
if (GetHealth() > health) SetHealth(health);
```

Raising the level RAISES the ceiling and leaves current health untouched — no free heal.
(Lowering a level clamps current health down; don't lower levels on living creatures.)
The task-12 note "SetLevel resets max health" is true and benign in the upward direction.
`SetLevel` writes the ZDO, so owner-side only (see §2).

## 5. Nameplate decoration surface

`Character.GetHoverName()` is `virtual`, instance-level, and delegates to
`Tameable.GetHoverName()` when present. A result-decorating postfix (rule 1 as amended)
appends to whatever survives — same pattern as the player nameplate title. EnemyHud
consumes it.

## 6. Legitimate despawns (the "got away" set)

`MonsterAI` evaporates two classes, both ZDO-flagged and re-read every ~4s:

- `s_despawnInDay` — night spawns walk away and despawn at daybreak
  (`MoveAwayAndDespawn`).
- `s_eventCreature` — event mobs despawn when their event ends.

Everything else with a persistent ZNetView saves with the world (the kited-troll fact).
A nemesis in either despawn class can legitimately vanish; a design that treats despawn
as escape needs no patch and fights nothing.

## 7. The frozen-ZDO trap is untouched by all of the above

A creature whose owner disconnected stays frozen for everyone (known trap, CLAUDE.md).
A ZDO-key mark tolerates this: the mark sits in the frozen ZDO, unreadable-in-practice
until the zone is genuinely live again, and no part of the design needs to patch
ownership.
