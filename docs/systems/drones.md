# Drones

> Last verified: 2026-08-11 — every assembly compiles clean, the grey box builds
> headlessly and GreyBoxVerify proves the references survive a save/reload round
> trip. **Not yet verified in play:** the chase, the fuse window, blast damage on
> the player, and pool reuse across many waves.

## Overview

Drones are the only enemy in the game. One `DroneController` reads a
`DroneConfig` for its numbers and ticks an `AttackModule` for its behaviour, so a
new archetype is **two assets and no new code** — the same modular contract the
weapons use. The first one is the Rusher: closes to contact, lights a fuse, and
detonates.

They are pooled like everything else, pathfind with Unity's AI Navigation
package, and ask for an *attack token* before committing to an attack, which is
how a crowd stays fair.

## Data Assets

- **[Drone_Rusher.asset](../../Assets/_Project/Data/Drones/Drone_Rusher.asset)** —
  100 HP (four AR body shots, the same ~257 ms TTK the gun was tuned around),
  moveSpeed 6.0, hoverHeight 0.9, `preferredRange` 0 (closes to contact),
  repathInterval 0.15, 10 score / 12 money.
- **[ContactDetonate_Std.asset](../../Assets/_Project/Data/Attacks/ContactDetonate_Std.asset)** —
  triggerRadius 2.2, fuse 0.55 s, lunge ×1.35, 24 damage at the centre falling to
  ×0.33 at blastRadius 3.5.
- **[Difficulty.asset](../../Assets/_Project/Data/Game/Difficulty.asset)** —
  `maxAliveDrones` 40, `maxSimultaneousAttackers` 3,
  `minSpawnDistanceFromPlayer` 12, `spawnSampleRadius` 4, `attackTokenTimeout` 6.

**Speed is a relationship, not a number.** 6.0 sits between the player's walk
(5.2) and sprint (8.0): backpedalling loses the race, sprinting wins it. Change
the player's speed and this one has to move with it.

## Runtime Types

- **[DroneConfig.cs](../../Assets/_Project/Scripts/Enemies/DroneConfig.cs)** —
  the archetype. No behaviour enum, and deliberately **no weakpoint multiplier**:
  `WeaponConfig.headshotMultiplier` is the single owner of that number
  project-wide.
- **[AttackModule.cs](../../Assets/_Project/Scripts/Enemies/AttackModule.cs)** —
  abstract, stateless ScriptableObject. `TriggerRange` plus
  `Tick(drone, ref state, now, dt)` and an optional `Cancel`.
- **[DroneAttackState.cs](../../Assets/_Project/Scripts/Enemies/DroneAttackState.cs)** —
  the mutable struct the drone owns and passes by `ref`. Phase, phase deadline,
  cooldown, burst counter, token flag, first-attack flag.
- **[ContactDetonate.cs](../../Assets/_Project/Scripts/Enemies/ContactDetonate.cs)** —
  the Rusher's attack.
- **[DroneController.cs](../../Assets/_Project/Scripts/Enemies/DroneController.cs)** —
  agent + health + pooling + telegraph. `Initialize` at spawn, `Retire` on exit.
- **[DroneRegistry.cs](../../Assets/_Project/Scripts/Enemies/DroneRegistry.cs)** —
  who is alive. A scene component, not a static list.
- **[DroneSpawner.cs](../../Assets/_Project/Scripts/Enemies/DroneSpawner.cs)** —
  `Spawn` / `SpawnBurst`, spawn-point selection, navmesh snapping, alive cap.
- **[IAttackTokenSource.cs](../../Assets/_Project/Scripts/Enemies/IAttackTokenSource.cs)** —
  the three-attacker rule as an interface. `UnlimitedAttackTokens` is the stand-in
  until the wave system supplies a real pool.
- **[DifficultyConfig.cs](../../Assets/_Project/Scripts/Enemies/DifficultyConfig.cs)** —
  caps and spawn rules. Lives in `CoD.Enemies` rather than with the wave code so
  the dependency runs one way only: waves reference enemies, never the reverse.

## Scenes & Prefabs

- **Drone_Rusher.prefab** — dark hull cube, two fins, and a glowing `Core` child
  that is simultaneously the weakpoint (a `Weakpoint` relay to the body's
  `Health`) and the fuse telegraph. `NavMeshAgent` radius 0.4, height 1.2,
  **disabled on the prefab**. Also `Health`, `HitFlash`, `PooledObject`, a
  spatial `AudioSource`, and `DroneController`.
- **Fx_Explosion.prefab / Fx_DroneDeath.prefab** — pooled particles, each with
  its own `AudioSource`. Deliberately different in size and sound: "I killed it"
  and "it got me" must never read the same.
- **10_GreyBox** — a `NavMeshSurface` on `Room` (children only) baked to
  [NavMesh_GreyBox.asset](../../Assets/_Project/Scenes/NavMesh_GreyBox.asset), a
  `Drones` root holding the registry and spawner, and eight spawn points on a
  16 m ring.
- Pool prewarm: drone 24, explosion 8, death VFX 8 — sized for a wave, not a demo.

## Key Behaviors & Non-Obvious Patterns

- **The fuse is the design.** An enemy that removes a quarter of your health the
  instant it touches you is a coin flip. The same enemy with 0.55 s of audible and
  visible warning is a decision: shoot it, or move. Kill it mid-fuse and the blast
  **does not go off** (`ContactDetonate.Cancel`) — killing a lit rusher has to be
  a reward or there is no reason to shoot one.
- **Attack tokens gate the attack phase, not the chase.** A drone denied a token
  keeps closing; it just cannot commit. That is what stops twenty enemies from
  being twenty simultaneous detonations.
- **One exit path.** Death, self-detonation, wave cleanup and stray deactivation
  all funnel through `DroneController.Retire`, so the token, the registry entry
  and the pool slot are released exactly once each. `Died` fires only for a kill —
  a self-detonation pays no score or money, or suiciding into a wall becomes an
  income stream.
- **Drones do not damage each other.** The blast skips any `Health` whose object
  also has a `DroneController`; chain-detonations would steal the player's kills
  and the wave's money with them.
- **Blast damage hits root colliders only.** Weakpoint children are skipped, both
  because explosions should not score headshots and because matching two colliders
  on one target would apply the damage twice.
- **Max HP comes from the config, not a HealthConfig asset.** `Health.ConfigureMax`
  is called at spawn, which also re-fills a pooled instance.
- **Repathing is throttled** to `repathInterval` (0.15 s). Forty agents calling
  `SetDestination` every frame is the single largest CPU cost in a horde game and
  the difference is invisible to the player.
- **The telegraph uses a MaterialPropertyBlock**, never `renderer.material` —
  touching `.material` clones it per drone, which is forty extra materials and
  forty broken batches in a full wave.

## Related Systems

- [pooling.md](pooling.md) — every drone, explosion and death VFX comes from the pool.
- [weapons.md](weapons.md) — the AR is what a drone's 100 HP is measured in, and
  the weapon owns the headshot multiplier the drone's core rewards.
- [ui.md](ui.md) — `PlayerDamageFeedback` is what makes a detonation legible.
- `waves.md` — the wave runner will drive the spawner and supply the real token pool.

## Gotchas

- **A pooled `NavMeshAgent` is the trap of this milestone.** An agent enabled
  while its object sits off the navmesh throws on the first `SetDestination`, and
  a reused agent that kept its old path walks the new drone to the dead one's
  destination. The prefab therefore ships with the agent disabled and
  `Initialize` does: enable → `Warp` → `ResetPath`. `Retire` disables it again.
- **The navmesh is baked from `Room`'s children only.** An "all objects" bake
  would carve the player capsule and the dummy targets into the mesh as permanent
  obstacles. If you add arena geometry, parent it under `Room` or it will not be
  walkable.
- **`NavMeshSurface.BuildNavMesh` leaves the data in memory.** The builder writes
  it to `NavMesh_GreyBox.asset` and re-assigns it; without that the reference is
  dropped on scene save and drones spawn and never move. GreyBoxVerify repairs
  and re-checks it.
- The agent's radius (0.4) must stay **under** the radius the surface bakes for
  (the default humanoid 0.5), or drones will not fit through gaps the mesh says
  exist.
- `DroneRegistry.Alive` is exposed as the concrete `List<T>` on purpose —
  iterating an `IReadOnlyList` boxes the struct enumerator. Index into it; never
  add or remove from outside.
