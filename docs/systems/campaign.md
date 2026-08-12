# Campaign

> Last verified: 2026-08-12
> **Status: under construction.** This file is the map of the mission layer as
> it lands. Sections marked ⏳ are planned, not built. Do not cite a ⏳ row as
> if it were code — the whole point of this folder is that it never lies.

## Overview

The campaign is a mission layer **on top of** the existing wave engine, not a
second engine. `WaveRunner` keeps owning spawning, the attack-token cap, the
shop and the endless ramp; a `MissionDirector` drives it through seven additive
methods and consumes its four events. With no director in the scene, every one
of those additions is inert and endless mode is byte-identical to before.

See [docs/PLAN-CAMPAIGN.md](../PLAN-CAMPAIGN.md) for the full plan and the
reasoning behind each decision.

## The mode axis — read this before touching the save

`GameMode` has exactly two values (`Run`, `Sandbox`) and means **rules**. It is
serialised by `JsonUtility` as a raw int, and **C# enums are not range-checked**
— so adding `Campaign = 2` would mean an already-shipped build reading that save
gets "not Sandbox", treats it as a Run, and writes a campaign mission's wave
number into `bestRound`. The permadeath record would be polluted by a build that
can no longer be patched.

**Campaign is therefore a second axis: a saved bool meaning *content*.** An old
build reading a campaign save sees `lastMode: Run`, ignores two fields it does
not know, and starts a normal endless run. That is safe degradation on the side
you cannot fix.

| | Run | Sandbox |
| --- | --- | --- |
| **Endless** | permadeath, writes `bestRound` | cheat console, writes nothing |
| **Campaign** | missions, checkpoints, writes `missionRecords` only | missions with infinite money — the dev/test mode |

## Database Tables

None. This game has no database; persistence is versioned JSON — see
[save.md](save.md).

## Data Assets ⏳

- **`MissionConfig`** (`Data/Missions/Mission_NN_*.asset`) — an ordered step
  list, a wave list, an arena, starting money, briefing and comms.
- **`MissionObjective`** family (`Data/Missions/Objectives/Objective_*.asset`) —
  stateless ScriptableObjects. See the three rules below.
- **`ArenaConfig`** (`Data/Arenas/Arena_*.asset`) — the data-driven arena.

## The three objective rules ⏳

Transcribed from `EffectModule.cs:12-27`, because they hold for the same reasons:

1. **Objectives are STATELESS.** The asset holds numbers and text only; one
   asset is shared by every mission that uses it. Per-instance values travel in
   `ref ObjectiveState`.
2. **Objectives never mutate the world.** They read a context and write state.
   Spawning, healing, phase changes and save writes belong to the director, so
   each has exactly one place to go wrong.
3. **Objectives never subscribe to anything.** Domain Reload is off, so a
   ScriptableObject that subscribes keeps the subscription into the next Play
   session — the mutable-static bug class in a form the guard cannot see. The
   director subscribes once into `MissionProgress`; objectives **poll** it.
   This is also what makes every objective EditMode-testable with no scene.

## Related Systems

- [waves.md](waves.md) — the runner the director drives.
- [save.md](save.md) — schema 4 carries the campaign block.
- [menus.md](menus.md) — the campaign row and mission select.
- [drones.md](drones.md) — the enemy layer both families share.

## Gotchas

- **`ArenaObjective` (the repair beacon) needs zero changes** to work as a
  mission objective: start the GameObject inactive and `OnEnable → Relocate()`
  already does the right thing. That is a free win from how it was written.
- **A bool cannot be `Check`ed by `GreyBoxVerify`** — it tests
  `objectReferenceValue`. So the campaign flag must never be a serialized scene
  field; the *absence of a director* is the endless configuration.
