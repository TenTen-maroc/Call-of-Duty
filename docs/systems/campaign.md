# Campaign

> Last verified: 2026-08-13
> **Status: under construction.** This file is the map of the mission layer as
> it lands. Sections marked ⏳ are planned, not built. Do not cite a ⏳ row as
> if it were code — the whole point of this folder is that it never lies.

## Overview

The campaign is a mission layer **on top of** the existing wave engine, not a
second engine. `WaveRunner` keeps owning spawning, the attack-token cap, the
shop and the endless ramp; a `MissionDirector` drives it through seven additive
methods and consumes its four events. With no director in the scene, every one
of those additions is inert and endless mode is byte-identical to before.

**What exists today:** the objective layer, director, zones, HUD, mission menu and
two authored missions are wired into the generated scenes. Mission 1 now carries
the first humanization vertical slice described below. The slice is machine-tested
and screenshot-reviewed; it still needs a human combat and audio timing pass.

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

## Data Assets

- **`MissionConfig`** (`Data/Missions/Mission_NN_*.asset`) — an ordered step
  list, a wave list, an arena, starting money and briefing text. **Built** — no
  assets authored yet.
- **`MissionObjective`** family (`Data/Missions/Objectives/Objective_*.asset`) —
  stateless ScriptableObjects, eight concrete types. **Built** — no assets
  authored yet. See the three rules below.
- **`ArenaConfig`** (`Data/Arenas/Arena_*.asset`) ⏳ — the data-driven arena.

## Mission 1 humanization vertical slice

`Radio_Mission01_MaraVenn.asset` contains nine restrained subtitle lines from
operator **Mara Venn**. Each row has a stable ID, semantic trigger, occurrence,
priority, cooldown, interruption policy, display time and optional `AudioClip`.
The shipped clips are deliberately null: the scheduler still displays the line
without warning spam, and the docs do not pretend generated or placeholder speech
is final voice acting.

`RadioDialogueArbiter` is pure runtime logic. It performs occurrence selection,
priority interruption, bounded queuing, duplicate suppression and per-line
cooldowns. `RadioDialogueScheduler` adds unscaled-time playback and subtitle
events. `MissionDirector` owns only trigger timing: entry, first objective, first
contact, player badly hurt, wave clear, objective complete, completion and failure.
This keeps authored copy out of combat code and lets a future recorded clip replace
a null reference without changing the schedule.

Mission 1's visible objectives are contextual rather than mechanical: get the
relay online, break the drone push, then fall back to extraction. After the second
push, the step's `completionDelaySeconds = 4` suspends waves and hides the objective
HUD before extraction appears. The quiet beat is independent of radio duration,
so missing audio cannot stall the mission and a long localization cannot move the
gameplay gate. `humanizationVersion` applies this authored upgrade once without
continually overwriting later designer edits.

## The three objective rules

Transcribed from `EffectModule.cs:12-27`, because they hold for the same reasons,
and now enforced in [MissionObjective.cs](../../Assets/_Project/Scripts/Waves/MissionObjective.cs):

1. **Objectives are STATELESS.** The asset holds numbers and text only; one
   asset is shared by every mission that uses it. Per-instance values travel in
   `ref ObjectiveState`.
2. **Objectives never mutate the world.** They read a context and write state.
   Spawning, healing, phase changes and save writes belong to the director, so
   each has exactly one place to go wrong. (`AttackModule` bends this — a drone's
   attack *is* a world change. The mission layer has no equivalent need, so it
   holds the stricter line.)
3. **Objectives never subscribe to anything.** No `+=` in an objective, ever.
   Domain Reload is off, so a ScriptableObject that subscribes keeps the
   subscription into the next Play session — the mutable-static bug class in a
   form the guard cannot see. The director subscribes once into
   `MissionProgress`; objectives **poll** it. This is also what makes every
   objective EditMode-testable with no scene.

Rule 3 is asserted, not just written down:
`NoObjective_HoldsADelegate_BecauseNoObjectiveMaySubscribe` reflects over every
concrete objective and fails on any delegate field or event.

## The objective layer — code map

All of it lives in `Assets/_Project/Scripts/Waves/`, inside the existing
`CoD.Waves` assembly, and **no new assembly was created**. That is the single
most valuable property of the choice: the EditMode test asmdef already references
`CoD.Waves` and does not reference `CoD.UI` or `CoD.Player`, so objective logic
put here is testable with zero asmdef edits and can never accidentally reach for
a scene.

| File | What it is |
| --- | --- |
| [MissionObjective.cs](../../Assets/_Project/Scripts/Waves/MissionObjective.cs) | The abstract SO + `ObjectiveContext`. Modelled on `EffectModule`. |
| [ObjectiveState.cs](../../Assets/_Project/Scripts/Waves/ObjectiveState.cs) | `ObjectiveStatus` + the per-instance `ObjectiveState` struct. |
| [ObjectiveMath.cs](../../Assets/_Project/Scripts/Waves/ObjectiveMath.cs) | Pure helpers: floor-plane containment, `PickDifferent`, `Progress01`, allocation-free int/seconds append. |
| [MissionProgress.cs](../../Assets/_Project/Scripts/Waves/MissionProgress.cs) | The polled record. Plain C# class, director-owned. Counts interactions by Core's `InteractKind` — see below. |
| [MissionConfig.cs](../../Assets/_Project/Scripts/Waves/MissionConfig.cs) | The mission asset: steps, waves, arena, money, `OnValidate`. |
| [Objectives/](../../Assets/_Project/Scripts/Waves/Objectives/) | The eight concrete types, one file each. |

The `Objectives/` folder is **filing, not architecture** — everything in it stays
in the `CoD.Waves` namespace, so the director, the HUD and the tests need one
`using` and never learn which file a type lives in.

### The contract

```csharp
public abstract class MissionObjective : ScriptableObject
{
    string stableId, title, description
    virtual bool Critical             => true   // failing it fails the mission
    virtual bool CompletesWithMission => false  // NoAlarm: satisfied when the others finish
    virtual bool RequiresWaves        => false  // MissionConfig.OnValidate reads this

    void BeginStep(in ObjectiveContext, ref ObjectiveState, float now, float timeLimitSeconds)
    abstract void Begin(in ObjectiveContext, ref ObjectiveState)
    abstract void Tick (in ObjectiveContext, ref ObjectiveState, float now, float deltaTime)
    virtual  void End  (in ObjectiveContext, ref ObjectiveState)
    abstract void Describe(StringBuilder into, in ObjectiveState)   // caller-owned; NEVER returns a string
}
```

`BeginStep` is the entry point the director calls — never `Begin` directly. It is
the one place the STEP's time limit is stamped onto the state, so an objective
cannot forget to do it and two objectives cannot disagree about what a deadline
means. It *assigns* the state rather than adjusting it: the director reuses state
slots between steps, and a leftover accumulator would read as progress nobody
made.

`ObjectiveContext` is a readonly struct carrying the `MissionProgress`, a
**nullable** `WaveRunner` (`Phase`, `WaveNumber`, `EnemiesRemaining`, each
degrading quietly when there is none) and the player's position. The nullability
is not a convenience — it is what lets the whole family be driven with no runner
in existence, which is what the tests do.

### The eight types

| Type | Fields | Completes when |
| --- | --- | --- |
| `Obj_SurviveWaves` | `waves` | `WavesCleared` reaches baseline + N. `RequiresWaves`. |
| `Obj_KillQuota` | `quota`, `droneFilter?` | Kills (optionally of one `DroneConfig`) reach baseline + N. |
| `Obj_HoldZone` | `zoneId`, `holdSeconds`, `resetOnLeave`, `requireWavePhase` | Occupancy accumulates to `holdSeconds`. Out of phase the clock **pauses**; only stepping off can reset it. |
| `Obj_ReachZone` | `zoneId` | The player is inside the zone. |
| `Obj_DestroyTargets` | `count` | `TargetsDestroyed` reaches baseline + N. |
| `Obj_Extract` | `zoneId`, `dwellSeconds` | Dwell completes **while standing on the pad**. Always resets on leave. |
| `Obj_NoAlarm` | — | **Never on its own.** Fails when the alarm is raised; `CompletesWithMission`. |
| `Obj_Interact` | `kind`, `count` | Interactions of that `InteractKind` reach baseline + N. |

**`Obj_Escort` and `Obj_RepairBeacon` are deliberately absent.** Both need scene
actors that do not exist — an escortee with pathing and its own health, and a
beacon wired as a mission target. A stub objective that silently never completes
is strictly worse than a missing one: it authors, it validates, it ships, and the
mission it is in can never be finished.

### One interaction enum, and it lives in Core

There is exactly **one** enum describing what an interaction is:
`CoD.Core.InteractKind` in
[Interaction.cs](../../Assets/_Project/Scripts/Core/Interaction.cs), with six
members — `Generic`, `Terminal`, `Charge`, `Intel`, `Extract`, `Door`.

It lives in Core because three layers need the same word and none of them may
reference each other. The **player** raises the kind (`PlayerInteractor`), the
**arena** authors it (`InteractPoint`), and the **mission layer** counts it
(`MissionProgress`, `Obj_Interact`). `CoD.Waves` references `CoD.Core` and never
the reverse, so Core is the only assembly all three can see — the enum has
nowhere else it could go without one of them reaching across a boundary.

`CoD.Waves` briefly had a second, four-member `InteractionKind` of its own, with
`MissionDirector.RecordInteraction` translating between them in a hand-written
switch. Its own comment called that "a seam, not a feature", and it was: one
concept written down twice, correct only for as long as somebody kept both
copies in step. It also had a live bug — `Generic` and `Extract` had no case, so
they fell out of the switch and were counted **nowhere**, not even in the running
total that documents itself as "interactions of every kind". The mission layer
now takes Core's enum straight through, so nothing between the thing the player
used and the counter can disagree.

Two consequences worth knowing:

- **`InteractKind` is APPEND ONLY.** `Obj_Interact.kind` is a serialized field,
  so Unity writes the enum into every objective `.asset` as a raw int. Appending
  a member is safe; reordering silently re-points every authored objective at a
  different kind, with no error and no diff anyone would notice. Same trap as
  `RunOutcome` and `GameMode`.
- **The counter array is sized from the enum, not from a constant.**
  `MissionProgress` derives its slot count as *highest member value + 1*, once
  per domain in a `static readonly`. A hand-kept `Count` is two copies of one
  fact, and when it drifts the range guard in `RecordInteraction` quietly drops
  every interaction of the new kind — an objective that can never complete and
  nothing anywhere saying why. Highest-value-plus-one rather than
  `GetValues().Length` because the two agree only while the enum is contiguous
  from zero, and an appended `Sabotage = 10` would reintroduce exactly the same
  silent drop.
  `MissionProgress_HasASlotForEveryInteractKind_AndDropsValuesThatAreNotMembers`
  asserts both the sizing and the guard.

### Why "Timed" is not an objective type

The time limit lives on `MissionConfig.Step` as `timeLimitSeconds`, is stamped
into `state.Deadline` by `BeginStep`, and is checked uniformly by the director.
The alternative — an `Obj_Timed` wrapping another objective — makes objectives
compose objectives, and a ScriptableObject tree that references SOs of its own
type is where systems like this rot: it can nest, it can cycle, and the inspector
shows you none of it. This way **any** objective can be timed, timing is one
number, and the game has exactly one countdown implementation.

## Shared Helpers

- [ObjectiveMath.cs](../../Assets/_Project/Scripts/Waves/ObjectiveMath.cs) —
  `WithinFloorRadius` and `PickDifferent` are lifted **verbatim** (comments
  included) from [ArenaObjective.cs](../../Assets/_Project/Scripts/Waves/ArenaObjective.cs),
  which solved both first:
  - **Containment is floor-plane, not spherical** (`delta.y = 0f`). The player's
    origin is at their feet and a zone is a pad, so a sphere is just a smaller
    circle — and it shrinks further the moment anyone stands on a crate, which is
    exactly when they would swear they were on the marker.
  - **`PickDifferent` has no reroll loop.** It draws from a range one shorter and
    steps over the excluded index. Pick-compare-pick-again has an unbounded worst
    case, which on a list of one is not a worst case but a hang.
  - `AppendInt` / `AppendSeconds` write digits by hand.
    `StringBuilder.Append(int)` formats via `ToString()` on several runtimes and
    allocates a string per call; objective text is rebuilt every frame the HUD
    shows it.
  - **`ArenaObjective` still owns its own copies** and should later delegate to
    `ObjectiveMath` so there is one implementation again. It was out of scope
    here (a different agent's file in the same session).

## Key Behaviors & Non-Obvious Patterns

- **Baselining is the whole mechanism.** Every counting objective snapshots the
  running total in `Begin` and measures the difference. Without it, a mission
  whose third step asks for two more kills is already satisfied by the forty the
  player got in step one. `Counter` is clamped at zero so a checkpoint rewind —
  which resets `MissionProgress` while the state slot survives — cannot run a
  progress bar backwards.
- **Zones are ids, not references.** A ScriptableObject cannot hold a scene
  Transform, so the director registers each pad's position and radius with
  `MissionProgress.RegisterZone(id, center, radius)` and objectives only ever
  name the id. An unregistered id answers *false*, never *true*: an objective
  pointing at a zone this arena does not have stays visibly incomplete instead of
  completing on frame one.
- **Per-type kills do not allocate.** Two parallel arrays (`DroneConfig[]`,
  `int[]`, capacity 8) scanned linearly, in the `StatSheet` spirit — not a
  Dictionary, which grows, rehashes and produces garbage on the frame a whole
  wave dies at once. Overflow warns **once**, not per kill.
- **`requireWavePhase` on a hold PAUSES the clock; it never resets it.** It stops
  the player banking the hold during the shop break, when nothing is shooting at
  them — and that is *all* it does. The two conditions in `Obj_HoldZone.Tick` are
  deliberately separate branches (`inside && phaseAllows` accumulates,
  `!inside && resetOnLeave` zeroes) because they were once a single `&&`, and
  that one operator deleted holds the player had legitimately earned: standing
  perfectly still on the pad when a wave ended fell into the reset branch. With
  the shipped defaults — 45 s, `requireWavePhase`, `resetOnLeave` — the whole
  hold then had to fit inside one uninterrupted Wave phase, and a wave ends when
  the last drone dies. The default-authored objective was plausibly impossible.
  Pinned by `HoldZone_RequireWavePhase_PausesTheHold_AndNeverSpendsProgressThePlayerEarned`
  and its complement, which proves the pause did not turn `resetOnLeave` into a
  no-op.
- **`Obj_Extract` re-tests the zone after the dwell** so a `dwellSeconds` of 0
  cannot end the mission from across the arena via `0 >= 0`.
- **`MissionConfig.OnValidate`** errors (never throws) on: no `stableId`, no
  steps, a step with no objective, a wave-counting step in a mission with no
  waves, and a `CompletesWithMission` objective authored as a **non-parallel**
  step — the one that hangs a mission, because the director waits at a step whose
  only route to completion is the mission ending. It warns on a step 0 marked
  parallel. It clamps a negative time limit *and says so in the same breath*:
  `BeginStep` already reads any non-positive limit as untimed, so the clamp
  changes no behaviour — it stops the stored number disagreeing with the played
  one, and the error is what tells the author their deadline is never going to
  fire. **Nothing here mutates silently.** A mis-authored asset costs a log line,
  never the run — the same discipline as `ShopConfig` and `WaveConfig`.

## Tests

[MissionObjectiveTests.cs](../../Assets/_Project/Tests/EditMode/MissionObjectiveTests.cs)
— 44 EditMode tests, and the file exists to prove a claim as much as to use it:
a hand-built `MissionProgress` plus `ScriptableObject.CreateInstance` is a
**complete** environment for this layer. No arena, no navmesh, no runner, no
frame. `EveryObjective_RunsWithNoSceneAndNoRunner` reflects over the family and
drives each type through Begin → Tick → End → Describe, so a future objective
that quietly starts needing a scene fails there.

Three tests need a component, all of them the hold's phase gate. `MakeRunner`
builds a `WaveRunner` and forces `WaveRunner.Phase` through its private setter,
because a gate only ever tested in its blocking direction could be permanently
shut without anyone noticing — and because the gate's real behaviour is what
happens when the phase *changes underneath a stationary player*, which no
fixed-phase test can see. That is the case that was missing when the gate and the
leave-reset were one condition.

## Related Systems

- [waves.md](waves.md) — the runner the director drives.
- [save.md](save.md) — schema 4 carries the campaign block.
- [menus.md](menus.md) — the campaign row and mission select.
- [drones.md](drones.md) — the enemy layer both families share.

## Four ways a mission can be uncompletable, all of which happened

Every one of these compiled, passed all eight guards, validated clean in
`OnValidate`, and shipped in the catalog. None was caught by a test; all four
were caught by reading. They are written down because each is a *class* of
failure this layer invites, not a one-off.

**1. A zone nobody registered.** `MissionProgress.RegisterZone` had no caller
anywhere in the game, so `IsInsideZone` answered false forever — correctly, by
its own design. Every `ReachZone`, `HoldZone` and `Extract` objective was
therefore uncompletable, and mission 1 stalled on its *first* step with the
runner suspended and the arena empty. The tests passed because they registered a
zone under the player's feet themselves.
*Fixed:* `MissionDirector` carries serialized `MissionZone[]` markers and calls
`RegisterZone` on mission start **and after every checkpoint rewind**, because
`Progress.Reset()` clears them.

**2. An objective that needs enemies but does not say so.**
`MissionObjective.RequiresWaves` defaults to false and only `Obj_SurviveWaves`
overrode it. `MissionDirector` gates the wave loop on that flag, so mission 2 —
kill quota, then hold, then extract — left the runner suspended from `Awake`
and never spawned a single drone. Five authored waves, dead weight.
*Fixed:* `Obj_KillQuota` returns true; `Obj_HoldZone` returns `requireWavePhase`,
because a hold that does not need a live wave must not drag one in behind it.

**3. A rewind that never revived the player.** `OnPlayerDown` rebuilt the step
machine and never touched `Health`. `RunContext.BeginRun` ends in `ApplyStats`,
which uses `AdjustMax` and **not** `ConfigureMax` — deliberately, so buying a
passive at 8 HP does not heal you — so with an unchanged max the delta is zero
and current health stays at zero. A dead player takes no damage, can never raise
`Died` again, and `WeaponController` refuses to fire, so waves respawned around
an invincible corpse that could not shoot and the mission wedged forever.
*Fixed:* the rewind calls `ResetHealth()`. **The test that covered this path
asserted the phase, the death count and the save file, and never once asserted
the player was alive** — which is why it certified the bug green.

**4. A result nobody wrote.** `SettingsHub.RecordMissionResult` had no caller, so
no mission was ever marked complete and mission select never unlocked anything
past mission one.
*Fixed:* `FinishMission` writes it — and deliberately **not** through
`RecordRunEnded`, which writes `bestRound`. A campaign mission must never touch
the permadeath record.

### And one that only shows up in a stopwatch

`ApplyWaveGate` called `StartFrom` then `Resume`. `StartFrom` sets a fresh
countdown; `Resume` then *adds back* the whole interval the runner spent
suspended — which began at scene load. A player who took 25 s to reach the first
objective got a 29-second empty arena instead of a 4-second countdown. Both
comments were individually correct and wrong together. The order is now `Resume`
then `StartFrom`, so the stale value is overwritten rather than added to.

### The ownership flag, and why `Suspended` was not enough

`WaveRunner.Start` guarded on `Suspended`. A director that suspends in `Awake`
and *resumes* in its own `Start` can leave that false before `WaveRunner.Start`
runs — and the runner would then begin its own run underneath a mission that had
already started one. The guard is now a one-way `_directorOwned` flag set inside
`Suspend`, which outlives any particular suspend.

## Gotchas

- **`ArenaObjective` (the repair beacon) needs zero changes** to work as a
  mission objective: start the GameObject inactive and `OnEnable → Relocate()`
  already does the right thing. That is a free win from how it was written.
- **A bool cannot be `Check`ed by `GreyBoxVerify`** — it tests
  `objectReferenceValue`. So the campaign flag must never be a serialized scene
  field; the *absence of a director* is the endless configuration.
- **`Obj_NoAlarm` needs the director's help to ever finish.** It is the only type
  with `CompletesWithMission => true`. A director that ignores that flag will
  hang a stealth mission forever with every other step complete — nothing left in
  the arena can change its status. The *authoring* half of that hang is now
  caught: `MissionConfig.OnValidate` errors when a `CompletesWithMission`
  objective is authored as a non-parallel step, naming the step index. The
  runtime half stays the director's responsibility — a constraint sharing a step
  group with real objectives ends when they do, but one authored alone in its own
  group has nothing to end alongside, which is exactly the shape the new error
  refuses to ship.
- **Nothing calls `Begin` directly.** `BeginStep` stamps the deadline; a director
  that reaches past it produces steps whose time limits silently do nothing.
- **`ObjectiveState` members that do not mutate are marked `readonly`.** Drop the
  keyword and every read through an `in` parameter silently copies the whole
  struct first — the cheap thing quietly becomes the expensive thing in a
  per-frame path.
- **`InteractKind` order IS serialized — it is APPEND ONLY.** `Obj_Interact.kind`
  writes it into every objective `.asset` as a raw int, so adding a member is
  safe and reordering one silently re-points authored objectives at a different
  kind. The same trap `RunOutcome` and `GameMode` carry, except this one is
  already sprung: the assets exist the moment anyone authors a mission. There is
  exactly one such enum and it lives in `CoD.Core` — see
  [One interaction enum, and it lives in Core](#one-interaction-enum-and-it-lives-in-core).
- **`MissionProgress` capacities are buffer sizes, not tuning numbers**
  (`KILL_TYPE_CAPACITY`, `ZONE_CAPACITY`), which is why they are `const` in code
  rather than fields on an asset. Exceeding either warns once and degrades:
  further kill types count toward the total only, and a dropped zone makes every
  objective pointing at it impossible.
