# Performance — the two caps, and what a machine can prove

> Last verified: 2026-08-11
> **Verified:** the caps hold and the arena allocates ~450 B/frame at 40 alive,
> asserted by 4 PlayMode tests. **Frame time on the actual RTX 3050 is NOT
> verified** and cannot be from a headless run — see below.

## Overview

Two numbers in [Difficulty.asset](../../Assets/_Project/Data/Game/Difficulty.asset)
carry the whole performance story, and CLAUDE.md calls both "not tuning knobs":

| | Value | Why |
| --- | --- | --- |
| `maxAliveDrones` | 40 | The 4 GB VRAM budget on the target laptop. |
| `maxSimultaneousAttackers` | 3 | Why twenty enemies read as fair rather than as a mugging. |

A rule nobody checks is a rule that quietly stops holding, so
[HordeLoadTests](../../Assets/_Project/Tests/PlayMode/HordeLoadTests.cs) checks
both under a full arena.

## What the tests assert

| Test | Asserts | Last measured |
| --- | --- | --- |
| `TheAliveCap_Holds_NoMatterHowHardYouPush` | 400 spawn attempts past a full arena never exceed the cap | 40 alive, held |
| `FortyAlive_StaysInsideTheAllocationBudget` | worst-case GC allocation per frame < 16 KB | **mean 450 B, worst 610 B** |
| `TheAttackTokenCap_IsReached_AndNeverExceeded` | tokens saturate the cap and never pass it | **peak 3 / 3 with 40 alive** |
| `AFullArena_ThrowsNothing` | no unexpected error/exception over an 8 s pressure window | clean |

The allocation figure is the important one. ~450 B/frame with forty drones
pathing, attacking, detonating and recycling means the game systems allocate
essentially nothing per frame — that is the object pool and the no-LINQ,
no-new-collections rule doing exactly the job they exist for. The residual is the
test framework's own coroutine machinery.

## What a machine cannot prove here

**Frame time on the RTX 3050.** These run under `-batchmode -nographics`, where
there is no GPU work at all. Any millisecond figure from that run is a
measurement of a machine that is not rendering the game. A green light on a
number the run cannot legitimately produce is worse than no number, so the tests
log what they measure and assert only what they can stand behind.

The remaining question — *does it stutter at wave 12 with forty drones on
screen* — is a human sitting in front of the laptop with the frame-time graph
open. That is item 9 on the tuning card.

## Non-obvious patterns

- **Pressure windows are measured in SECONDS, never frames.** A `-batchmode` run
  is uncapped and can push a thousand frames a second, so an early version's
  "900 frames" was under one second of game time — nowhere near long enough for a
  Rusher to cross the arena. That version passed while reporting a peak of 0
  tokens held, which is the shape of a test that checks nothing. `PressureSeconds`
  exists so it cannot happen again.
- **The token test makes the player invulnerable** for its duration. Forty
  Rushers detonate for 24 each against 100 HP, so without it the arena clears
  itself seconds after the first arrival and the test measures a corpse. It uses
  `Health.Invulnerable`, the same flag the sandbox console flips.
- **The allocation window settles first.** The frames right after a spawn burst
  legitimately allocate — agent path buffers, pool growth — and measuring them
  would report the setup rather than the steady state.
- **`Assert.Inconclusive` if the counter is unavailable.** `ProfilerRecorder` is
  not guaranteed in every player configuration; a gate that cannot measure has
  not measured, and must not report green.
- The budget and window constants are `const` in the test class rather than
  ScriptableObject fields. They are harness thresholds, not game tuning — no
  balance decision reads them, and a CI budget living beside drone health would
  make that asset harder to reason about.

## Related Systems

- [drones.md](drones.md) — the archetypes and the attack-token mechanism.
- [waves.md](waves.md) — `DifficultyConfig` and where the caps are applied.
- [pooling.md](pooling.md) — why the allocation number is as low as it is.

## Gotchas

- Raising `maxAliveDrones` is not a tuning decision, it is a VRAM decision. The
  cap test will keep passing at any value; the laptop will not.
- `TheAttackTokenCap_IsReached_AndNeverExceeded` asserts the peak is **greater
  than zero**. Without that clause the test passes trivially on an arena where
  nothing ever got close enough to attack.
