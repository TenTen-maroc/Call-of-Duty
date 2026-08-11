# Waves

> Last verified: 2026-08-11 — compiles clean, builds headlessly, references
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
  Current plan: 3 → 5 → 7 → 9 → 12 → 14 → 16 → 18 → 22 → 26 Rushers, bonuses
  80 → 220. Counts climb faster than the drip window, so later waves overlap.
- **[Difficulty.asset](../../Assets/_Project/Data/Game/Difficulty.asset)** — the
  two hard caps plus the endless curves (`countMultiplierByWave`,
  `healthMultiplierByWave`, `speedMultiplierByWave`, `endlessMix`).

## Runtime Types

- **[WaveRunner.cs](../../Assets/_Project/Scripts/Waves/WaveRunner.cs)** — phases
  `Countdown / Wave / Cleared / Shop / GameOver`, the spawn queue, money payouts,
  permadeath, `SkipWave` and `RestartRun` for the sandbox.
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

## Related Systems

- [drones.md](drones.md) — what a wave is made of; the token interface lives there.
- [shop.md](shop.md) — the break between waves and the passives it sells.
- [save.md](save.md) — what survives a death.
- [ui.md](ui.md) — the wave banner, shop panel and game-over screen.

## Gotchas

- `SpawnTask` is a struct in a `List`, so the loop **writes the modified copy
  back** (`_queue[i] = task`). Forgetting that is a wave that spawns forever.
- The runner calls `RunContext.BeginRun` in `Start`, after `RunContext.Awake` has
  loaded the save. Moving either changes what the first wave sees.
- `RestartRun` reloads the scene. That is the cheapest correct reset, but it means
  anything that must survive a restart has to be in the save file, not in a
  component.
- The two caps in Difficulty.asset are not tuning knobs. 40 protects a 4 GB GPU;
  3 attackers is why a crowd is fair. Change them deliberately, one at a time.
