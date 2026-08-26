# Reference sheets — provenance and caveats

Six decompile-verified Valheim engine fact sheets, copied in 2026-08-25 from a different
workspace (they were written for `c:\WubarrkCODING`, mostly out of AwayFromHome and
TortalPortal). They are kept verbatim — do not edit them to fit this project, add notes here
instead.

| Sheet | Answers |
|---|---|
| `ZDO-WIRE-LIMITS-FACTS.md` | how large a ZDO payload may get before the network stack breaks |
| `VANILLA-PIECE-INTEROP-FACTS.md` | driving vanilla pieces by *calling* them instead of patching them |
| `VALHEIM-DEDICATED-SERVER-FACTS.md` | what a headless server can see and do |
| `PLAYER-IDENTITY-FACTS.md` | which long is actually a player, and which three only look like one |
| `JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md` | Jotunn/csproj integration, Unity module references, bundle baking |
| `HEADLESS-AND-EMPTY-SERVER-FACTS.md` | four traps that fail silently on a build agent or an empty server |

## Read these three caveats before citing any of them

**1. Their cross-references do not resolve here.** Every line number points into
`libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (and its
`_SERVER` counterpart), which is not in this repo and not on this machine. Nor are
`IMPLEMENTATIONS\AwayFromHome.md`, `TEXT-ENCODING-FACTS.md`, `libs-Tools\ServerSync.cs`
or `Jotunn.dll`. Cite these sheets for their reasoning and their shapes, never for a line
number a reader could go and check.

**2. Two of them contradict each other on `Player.GetAllPlayers()` headless.**
`VALHEIM-DEDICATED-SERVER-FACTS.md` says it iterates the local instance list and is empty on
a dedicated server; `JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md` §6 says it correctly returns
connected characters' server-side replicas, and recommends it as the headless-safe pattern.
Both agree `Player.m_localPlayer` is always null on a true dedicated server.

The dedicated-server sheet looks stronger — it cites line numbers and a runtime-measured
`ZoneSystem.m_activeArea = 2`, and its reasoning chains from a server's reference position
sitting at ~world origin, so a player 3 km out has no `Player` GameObject on the server at
all. But that is inference, not proof, and **it cannot be settled from this repo**: `libs\`
holds only the *client* publicized assembly, and `ZNet.IsDedicated()` is a compile-time
constant returning `false` there, so `tools\dnread.py` cannot answer a server-branch
question. It needs the SERVER decompile or a headless run.

Until then, use `ZNet.GetAllCharacterZDOS()` for "where is every player" — both sheets agree
it works, and it sidesteps the dispute.

**3. Some of it is environment-specific and not true here.** The Jotunn sheet's §1 says
`dotnet` is not on `PATH`; that is about a Linux box (`/home/rohan/.dotnet`). On this Windows
machine `dotnet build` works directly.

## Where these have already been absorbed

Much of this is condensed into `CLAUDE.md` — the Smelter check-before-remove ordering, the
RPC register arities, `GetGroundHeight` returning its input on a raycast miss, the
empty-server clock freeze, unreleased persistent ZDOs, moving `rb.position` rather than the
ZDO, publicized-assemblies-are-compile-time-only, and the "validate the instrument"
debugging discipline. `HEADLESS-AND-EMPTY-SERVER-FACTS.md` is almost entirely already there.
These sheets are the long-form source with the reasoning behind those one-liners.

## One deliberate divergence

`PLAYER-IDENTITY-FACTS.md`'s persistence checklist says *"refuse writes after a bad read"*.
`Persistence` does not do that, on purpose: it quarantines the damaged file to `.corrupt` so
nothing can overwrite the evidence, then carries on writing. A write-refusal latch would keep
the world running while silently persisting nothing that happened afterwards, which is the
failure mode house property 3 exists to prevent. Same guarantee, without that cost.
