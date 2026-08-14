# Waves

> Last verified: 2026-08-12 — compiles clean, builds headlessly, references
> proven by GreyBoxVerify. **Not yet verified in play:** the pacing of a full
> wave, whether the alive cap is ever actually reached, and the feel of the
> 3-attacker limit with a crowd.

## Overview

The loop: countdown → wave → cleared → shop → countdown. Permadeath ends it, the
round reached is saved, and R restarts. [WaveRunner](../../Assets/_Project/Scripts/Waves/WaveRunner.cs)
owns the phase machine, the spawn queue, the attack-token pool and the shop; it
publishes events and properties and never touches a UI element.

Waves 1-10 are hand-authored assets because the opening is the part every run
replays and most runs never get past. Past wave 10 the endless ramp takes over.

## Data Assets

- **Wave_01 … Wave_10** ([Data/Waves](../../Assets/_Project/Data/Waves)) — entries
  of `(drone, count, spawnOverSeconds, startDelay)` plus a clear bonus. The count
  drips in evenly across the window rather than arriving as a lump.
  Current plan, and the teaching order is the point:

  | Wave | Rushers | Shooters | Tanks | Clear bonus |
  | --- | --- | --- | --- | --- |
  | 1-3 | 3 / 5 / 7 | — | — | 80 / 90 / 100 |
  | 4-6 | 7 / 9 / 10 | 2 / 3 / 4 | — | 120 / 140 / 155 |
  | 7-8 | 10 / 12 | 4 / 5 | 1 / 1 | 185 / 205 |
  | 9-10 | 14 / 16 | 6 / 7 | 2 / 3 | 240 / 300 |

  Three waves of pure Rushers to learn the fuse, the first Shooters at 4, one
  Tank alone at 7. Shooters and Tanks enter on a `startDelay` **after** the
  rushers have engaged — a new threat arriving with everything else is noise, not
  a lesson.
- **[Difficulty.asset](../../Assets/_Project/Data/Game/Difficulty.asset)** — the
  two hard caps plus the endless curves (`countMultiplierByWave`,
  `healthMultiplierByWave`, `speedMultiplierByWave`, `endlessMix`).

## Runtime Types

- **[WaveRunner.cs](../../Assets/_Project/Scripts/Waves/WaveRunner.cs)** — phases
  `Countdown / Wave / Cleared / Shop / GameOver`, the spawn queue, money payouts,
  permadeath, `SkipWave` and `RestartRun` for the sandbox. Also carries the
  mission-director seam described below, which is inert without a director.
- **[RunOutcome.cs](../../Assets/_Project/Scripts/Waves/RunOutcome.cs)** — how a
  run stopped. `Died` is value 0 and the default, because it is the only ending
  endless mode has.
- **[WaveConfig.cs](../../Assets/_Project/Scripts/Waves/WaveConfig.cs)** — one
  authored wave. `OnValidate` warns when a wave is far larger than the alive cap.
- **[AttackTokenPool.cs](../../Assets/_Project/Scripts/Waves/AttackTokenPool.cs)** —
  the three-attacker rule. Implements `IAttackTokenSource`, replacing the
  always-grant stub the drones were built against.
- **[RunState.cs](../../Assets/_Project/Scripts/Core/RunState.cs)** /
  **[RunContext.cs](../../Assets/_Project/Scripts/Core/RunContext.cs)** — money,
  wave, kills, owned passives, and the event everything else listens to.

## Key Behaviors & Non-Obvious Patterns

- **A wave ends when the queue is empty AND nothing is alive**, not on a timer.
  `durationTarget` is a design note, not a rule.
- **The alive cap throttles the queue rather than dropping spawns.** At 40 alive
  the queue simply waits for deaths — the wave gets longer, never smaller.
- **Difficulty scaling rides on `WaveScaling`, applied per spawn.** Health and
  speed multipliers are handed to `DroneController.Initialize` and never written
  into the DroneConfig, which would corrupt the authored balance permanently
  (Domain Reload is off — see [drones.md](drones.md)).
- **Kills are paid through the registry, not per drone.** `DroneRegistry.Killed`
  fires for any drone the player kills, including ones the sandbox console
  spawned, so the economy never depends on who created the drone.
- **A self-detonating Rusher pays nothing.** Only `Died` (killed by damage) pays,
  or suiciding into a wall would be an income stream.
- **Token timeouts are mandatory, not defensive.** A drone that dies mid-windup or
  gets stuck behind cover would otherwise hold a third of the pack's attacking
  capacity for the rest of the wave, and the horde slowly turns into a staring
  contest that looks like broken AI.
- **Endless waves** take the last authored wave's size × `countMultiplierByWave`,
  split by the mix weights, capped at 3× the alive cap. With no mix authored it
  falls back to the spawner's default drone rather than spawning an empty wave.

## Audit fixes (2026-08-11)

**Three ways the run could hang in `RunPhase.Wave` forever** — no timeout, no
death, no way out but quitting. All three came from the same clear condition:
`_queue.Count == 0 && AliveCount == 0 && _spawnedThisWave > 0`.

1. **A wave that planned nothing** (every `WaveConfig` entry lost its drone, or an
   endless wave with no mix and no fallback) starts with an empty queue and can
   never satisfy `_spawnedThisWave > 0`. `StartWave` now detects it, logs an error
   and ends the wave — **without paying the clear bonus**, because a mis-authored
   wave is mis-authored every time it comes round, and paying for it turns one bad
   asset into an unbounded money press that also inflates the permanent record.
2. **Spawns that can never be placed** (no spawn point reaching the navmesh, a
   missing prefab) left `task.Remaining` undecremented forever, and the retry
   re-sampled every spawn point against the navmesh every frame. The runner now
   backs off to the entry's own interval, warns at 30 consecutive failures, and
   gives up at 120 — ending the wave outright if not one drone was ever placed.
3. The round is banked only once the wave is known to be real: `SetWave` used to
   run before `BuildQueue` had said whether there was anything to fight.

**The endless economy is data now.** The clear bonus past the last authored wave
was `100 + wave * 10` in the script — untunable, and a pay CUT at wave 11 (210
against Wave_10's 220) on the wave where count, health and prices all step up.
It reads `DifficultyConfig.endlessClearBonusBase/PerWave`, authored at 120/12.
The endless base count and spawn window moved to the same asset.

**`WaveConfig.maxAliveOverride` finally does something.** It was serialized in all
ten wave assets and read by nothing; `StartWave` now passes it to the spawner.
0 still means "use `DifficultyConfig.maxAliveDrones`".


## Wave identity, the beacon, and skipping (2026-08-11)

**Not verified in play.** Every number below is a tuning-card question.

### Identity, not a ramp

The ten authored waves used to add roughly two drones each with the same mix
throughout. That is a difficulty curve but not a memory: no wave was recognisable,
so nothing taught anything specific and nothing was worth dreading.

| # | Name | Shape |
| --- | --- | --- |
| 1 | CONTACT | 3 rushers. Learn the rifle and the fuse. |
| 2 | PROBE | 5 rushers. |
| 3 | OVERWATCH | 5 rushers, 3 shooters — room for the deliberate first miss to land. |
| 4 | SWARM | 14 rushers over 8 s, **no ranged threat at all**. |
| 5 | SIEGE | 4 rushers, 7 shooters. The wave that makes the lane dividers worth using. |
| 6 | BREACH | 10 / 4 / 1 — the first tank. |
| 7 | ANVIL | 6 / 3 / **3 tanks**. Walking away is meant to be the right answer. |
| 8 | SWARM II | 20 rushers over 10 s, 2 shooters. |
| 9 | CROSSFIRE | 8 / 9 / 1. |
| 10 | OVERRUN | 16 / 7 / 3. |

`WaveConfig.displayName` shows in the HUD next to the number, because identity the
player cannot see is not identity.

**`WaveConfig.designVersion` is what makes a redesign land.** `WriteWave`'s old
rebuild test was array length alone, so changing 7 rushers to 14 while keeping one
entry looked applied and did nothing. `LoadOrCreate` has the same trap from the
other side — its configure callback runs on CREATE only — which is why the payout
and the name are written in `WriteWave` rather than there. Drone references are
still re-linked unconditionally; a broken one is a wave that spawns nothing.

**The endless seam moved with it.** Raising wave 10 to 320 put the ramp's opening
wave below it, a pay cut on exactly the wave where count, health and shop prices
all step up together. `endlessClearBonusPerWave` is 20, in the asset and in the
builder default. `CoreLogicTests` guards the seam.

### The repair beacon

[ArenaObjective](../../Assets/_Project/Scripts/Waves/ArenaObjective.cs) plus
`Objective_Beacon.asset`. The arena has three lanes and nothing gave the player a
reason to be in one rather than another, so the correct play was to find a corner
with good sightlines and never leave it.

On every `WaveStarted` the beacon moves to a different lane anchor and its heal
budget resets. Standing inside `radius` (2.5 m, measured on the floor plane) heals
`healPerSecond` (6) up to `healBudgetPerWave` (35).

- Only what was **actually** restored comes off the budget, so standing on it at
  full health cannot quietly burn the allowance.
- Only heals during `RunPhase.Wave`. Through the break the budget would be free.
- Never the same lane twice running — chosen from a range one shorter with a step
  over the previous index, so it is uniform and never loops to reroll.

### Skipping the shop

`WaveRunner.SkipShopForBonus()` on `TAB`. See [shop.md](shop.md).

## The mission-director seam (2026-08-12)

> **Additive and inert.** There is no `MissionDirector` in the repo yet. Every
> member below is unreachable in Run and in Sandbox, and endless mode behaves
> exactly as it did before the seam existed. That inertness is the acceptance
> criterion, not a nicety — see [campaign.md](campaign.md).

Before this, `WaveRunner` could not be told which waves to run, could not be
held for a briefing, and could end a run exactly one way: `_playerHealth.Died`.
A campaign needs all three. A **second** runner would have to duplicate — and
keep in sync forever — the spawn queue's struct-copy writeback, both
placement-failure hang guards (one of which needed a second fix after the first
only half-worked), and the wave-that-planned-nothing recovery with its explicit
refusal to pay a clear bonus. That is the entire hard-won part of the file, so
the loop was **opened**, not forked.

### The API

| Member | Access | What it does | Refuses / guards |
| --- | --- | --- | --- |
| `SetWaves(WaveConfig[])` | internal | Swaps the authored wave list, because a mission ships its own. `_waves` is private-serialized and otherwise written only by `GreyBoxBuilder`. | **`Phase == Wave`** — logs an error, keeps the old list |
| `Suspended` | public get | The loop is held. | — |
| `Suspend()` / `Resume()` | internal | Hold and release. Non-destructive: queue, live drones and attack tokens all survive. | Both are idempotent |
| `StartFrom(int wave)` | internal | Points the loop at a wave and enters countdown — a checkpoint restore. Sets `_wave = wave - 1`, because a countdown starts `_wave + 1`. | Clamps at 0, so no negative wave |
| `NextWaveNumber` | public get | The wave that will be fought next: `WaveNumber` during `Wave`, `WaveNumber + 1` in every other phase. The number `StartFrom` takes. | — |
| `AbortWave()` | internal | The destructive companion: `ClearTheArena()`. | Caller **must** pair it with `Suspend()` or `StartFrom()` |
| `SetDeathEndsRun(bool)` | internal | False makes a death a checkpoint rewind instead of a game over. | — |
| `PlayerDown` | public event | Raised **instead of** ending the run when `SetDeathEndsRun(false)` is in force. | Unreachable in endless |
| `FinishRun(RunOutcome)` | internal | Ends the run without a corpse: clears the arena, records the outcome, phase → `GameOver`, raises `RunEnded`. | No-ops once `Phase == GameOver` |
| `Outcome` | public get | How the run ended. Meaningful at `GameOver`. | Defaults to `Died` |
| `RunEnded` | public event | **Signature changed** from `Action` to `Action<RunOutcome>`. It had zero subscribers in the whole project at the time, so nothing broke. | — |

- **[RunOutcome.cs](../../Assets/_Project/Scripts/Waves/RunOutcome.cs)** —
  `Died / MissionComplete / MissionFailed / Abandoned`. `Died` is **first on
  purpose**: it is therefore the value of an uninitialised field and the only
  ending endless mode has, so with no director the default is already right and
  nothing has to be set for endless to stay endless. Reordering it is the bug.

### The ordering guarantee this hangs on

Unity does **not** guarantee `Awake` order between components, but it **does**
guarantee that every `Awake` completes before any `Start`, for objects present
when the scene loads. So:

1. `MissionDirector.Awake()` calls `Suspend()` (and `SetDeathEndsRun(false)`).
2. `WaveRunner.Start()` opens with `if (Suspended) return;` — **before**
   `RunContext.BeginRun`. In campaign the runner therefore begins no run, opens
   no countdown and spawns nothing.
3. The director starts everything later, on its own schedule, after the briefing.

No execution-order attribute to keep in sync, no race, and **no new serialized
field**: a bool cannot be `Check`ed by `GreyBoxVerify` (it tests
`objectReferenceValue`). *The absence of a director is the endless configuration.*

### Behaviours worth knowing before touching this

- **`Update()` early-returns while suspended**, before `_tokens.Tick`. A hold
  therefore cannot time out the attack tokens of drones that are standing
  perfectly still through a briefing.
- **`Resume()` gives the clock back.** Phase deadlines are absolute `Time.time`
  stamps, so without the shift a ten-second briefing would leave `_phaseEndsAt`
  ten seconds in the past and the countdown would end on the first frame back —
  a wave arriving with no warning at all. `_suspendedAt` exists only for this.
- **`AbortWave()` does not pick a phase.** Left alone in `RunPhase.Wave` with an
  empty arena, the very next `Update` finds the clear condition satisfied and
  pays the bonus for a fight that was cancelled. That is why the contract is
  "always with `Suspend()` or `StartFrom()`".
- **`FinishRun` deliberately does not call `RunContext.RecordRunEnded`.** That
  writes the permadeath record, and a mission's wave number must never land in
  `bestRound`. The caller decides what an ending is worth recording — the same
  split `PausePanel` already uses when it quits a run.
- **`ClearTheArena()` was extracted from `OnPlayerDied`.** The reasoning in its
  comment ("a game-over screen with drones still chewing on the corpse behind it
  reads as a crash") applies identically to a mission-complete screen and to a
  checkpoint fade. Three callers: `OnPlayerDied`, `FinishRun`, `AbortWave`.
- **`OnPlayerDied` does not route through `FinishRun`.** It keeps its own
  `RecordRunEnded` and its own `SetPhase`, so the endless death path is
  byte-identical to what shipped.

### What is proven, and what is not

[WaveRunnerSeamTests.cs](../../Assets/_Project/Tests/EditMode/WaveRunnerSeamTests.cs)
(EditMode) covers the parts that are pure: the `RunOutcome` defaults and member
count, a fresh runner reporting the endless configuration, every seam member
still existing with the signature a director will compile against, `Suspend` /
`Resume` idempotence, `Update` doing nothing while suspended (with an
un-suspended control that *does* move), `SetWaves` landing outside a wave and
being refused inside one with the old list still standing, `StartFrom`'s
off-by-one and its clamp, and `FinishRun` recording the outcome, ending the
phase and raising `RunEnded` exactly once.

The members are `internal` and the tests live in another assembly, so they are
reached by reflection rather than by widening the API or adding an
`InternalsVisibleTo` that shipping code would carry forever. A side benefit: the
lookups fail loudly if anyone renames a seam member.

**PlayMode still owes:** death routing (`PlayerDown` against `RunEnded`) needs a
`Health` and a fired `Awake`; `AbortWave`'s `DespawnAll` and token release need
a registry and a pool; and a hold carrying a *live* queue across real frames
cannot be shown without a scene. None of it is reachable until a director
exists.


## Related Systems

- [drones.md](drones.md) — what a wave is made of; the token interface lives there.
- [shop.md](shop.md) — the break between waves and the passives it sells.
- [save.md](save.md) — what survives a death.
- [ui.md](ui.md) — the wave banner, shop panel and game-over screen.

## Gotchas

- `SpawnTask` is a struct in a `List`, so the loop **writes the modified copy
  back** (`_queue[i] = task`). Forgetting that is a wave that spawns forever.
- The runner calls `RunContext.BeginRun` in `Start`, after `RunContext.Awake` has
  loaded the save. Moving either changes what the first wave sees — and `Start`
  now returns before `BeginRun` when `Suspended` is set, which is the whole
  campaign hand-off. Do not move that check below `BeginRun`.
- `RestartRun` reloads the scene. That is the cheapest correct reset, but it means
  anything that must survive a restart has to be in the save file, not in a
  component.
- The two caps in Difficulty.asset are not tuning knobs. 40 protects a 4 GB GPU;
  3 attackers is why a crowd is fair. Change them deliberately, one at a time.
