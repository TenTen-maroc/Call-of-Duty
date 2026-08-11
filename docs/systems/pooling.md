# Object pooling

> Last verified: 2026-08-11 (code compiles clean; has not been run)

## Overview

Everything that spawns goes through one pool: bullets' impact effects, casings,
muzzle flashes, damage numbers later, and drones. In a horde game with 40+
enemies and hundreds of projectiles, `Instantiate`/`Destroy` per frame is the
GC-hitch factory — the collector runs mid-firefight, exactly when the player is
under pressure.

## Runtime Types

- **[ObjectPool.cs](../../Assets/_Project/Scripts/Core/ObjectPool.cs)** — scene
  MonoBehaviour. `Spawn`, `SpawnForSeconds`, `Despawn`. Prewarms from a
  serialized `(prefab, count)` list in `Awake`.
- **[PooledObject.cs](../../Assets/_Project/Scripts/Core/PooledObject.cs)** —
  added to every instance the pool creates. Holds the source prefab (so despawn
  knows which stack to return to), a cached `Transform` and lazily-cached
  `Rigidbody`, plus `IsSpawned` / `SpawnGeneration` which only the pool writes.

## Key Behaviors & Non-Obvious Patterns

- **It is a scene object, not a singleton.** Domain Reload is off, so a `static`
  instance would survive between Play sessions pointing at a destroyed object —
  the bug that only appears on the second play. Consumers serialize a reference.
- `SpawnForSeconds` records a despawn deadline in a parallel list; the pool's own
  `Update` sweeps it **backwards** so removals do not skip entries. One `Update`
  for all timed objects rather than a timer component per instance.
- Missing prefabs grow the pool on demand rather than failing, and a
  `_leakWarningThreshold` (default 512) logs once when a prefab's live count
  crosses it — that is a leak, something spawning and never despawning.
- Despawn re-parents to the pool root and deactivates; it never destroys.
- **Spawn generations.** Every instance carries a counter bumped on each spawn,
  and a timed despawn records the generation it was issued for. Without it a
  stale timer fires on an object that was manually despawned and re-spawned in
  the meantime, killing an unrelated later use — the kind of bug that shows up as
  a decal vanishing early once every few minutes and is near-impossible to trace.
- **Double despawn is rejected** (`IsSpawned` guard). Pushing the same instance
  onto the stack twice would let the pool hand one object to two callers at once.
- `Spawn` pops past instances something external destroyed (scene change, stray
  `Destroy`) instead of returning a dead reference.
- `Despawn` on an object that never came from the pool logs an error rather than
  silently corrupting a stack.

## Registration

**A prefab is added to the prewarm list in the same commit that creates it.**
Wiring lives in [GreyBoxBuilder.cs](../../Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs)
(`SetPrewarm`), currently: impact decal 48, sparks 24, muzzle flash 4, casing 24,
dummy target 8.

## Related Systems

- [weapons.md](weapons.md) — the main consumer today.
- Drones will be pooled the same way; that is what the 40-alive cap assumes.

## Gotchas

- A pooled prefab must reset its own state in `OnEnable`, not `Awake` — `Awake`
  runs once per instance, not once per spawn. `Health` already does this.
- Particle prefabs need `playOnAwake` so they replay on reuse.
- Verified in play indirectly (impacts and casings appear and stop appearing).
  NOT yet verified: that the pool actually reuses rather than grows — watch the
  leak warning at 512 instances.
