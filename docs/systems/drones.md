# Drones

> Last verified: 2026-08-11 — every assembly compiles clean, the grey box builds
> headlessly and GreyBoxVerify proves the references survive a save/reload round
> trip. **Not yet verified in play:** the chase, the fuse window, blast damage on
> the player, and pool reuse across many waves.

## Overview

Drones are the only enemy in the game. One `DroneController` reads a
`DroneConfig` for its numbers and ticks an `AttackModule` for its behaviour, so a
new archetype is **two assets and no new code** — the same modular contract the
weapons use.

All three v1 archetypes exist. The Rusher closes to contact and detonates, the
Shooter holds a ring and fires bursts, the Tank walks in and slams. Adding the
second and third cost the controller **zero new fields**: kiting is one number
(`preferredRange`), and everything else lives in the attack module.

They are pooled like everything else, pathfind with Unity's AI Navigation
package, and ask for an *attack token* before committing to an attack, which is
how a crowd stays fair.

## Data Assets

- **[Drone_Rusher.asset](../../Assets/_Project/Data/Drones/Drone_Rusher.asset)** —
  100 HP (four AR body shots, the same ~257 ms TTK the gun was tuned around),
  moveSpeed 6.0, hoverHeight 0.9, `preferredRange` 0 (closes to contact),
  repathInterval 0.15, 10 score / 12 money.
- **[Drone_Shooter.asset](../../Assets/_Project/Data/Drones/Drone_Shooter.asset)** —
  75 HP (three AR body shots), moveSpeed 4.2, hoverHeight 1.25 so it shoots over
  the rushers' heads, **`preferredRange` 14** — the whole archetype in one number.
  20 score / 20 money.
- **[Drone_Tank.asset](../../Assets/_Project/Data/Drones/Drone_Tank.asset)** —
  600 HP (24 AR body shots, most of a magazine), moveSpeed 2.6, stopDistance 1.6,
  60 score / 65 money.
- **[ContactDetonate_Std.asset](../../Assets/_Project/Data/Attacks/ContactDetonate_Std.asset)** —
  triggerRadius 2.2, fuse 0.55 s, lunge ×1.35, 24 damage at the centre falling to
  ×0.33 at blastRadius 3.5.
- **[RangedBurst_Std.asset](../../Assets/_Project/Data/Attacks/RangedBurst_Std.asset)** —
  range 16, reactionDelay 0.4, accuracy 0.7, burst 3 at 0.18 s, cooldown 1.6,
  12 damage, projectile speed 18, **firstShotDeliberateMiss on**.
- **[HeavySlam_Std.asset](../../Assets/_Project/Data/Attacks/HeavySlam_Std.asset)** —
  triggerRadius 3.2, windup 0.9 s at 15% speed, slamRadius 4.5, 34 damage,
  cooldown 2.5.
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
- **[RangedBurst.cs](../../Assets/_Project/Scripts/Enemies/RangedBurst.cs)** — the
  Shooter's. Reaction delay, deliberate opening miss, accuracy cone, burst, cooldown.
- **[HeavySlam.cs](../../Assets/_Project/Scripts/Enemies/HeavySlam.cs)** — the
  Tank's. Long telegraph at near-zero speed, then a wide radial hit.
- **[DroneProjectile.cs](../../Assets/_Project/Scripts/Enemies/DroneProjectile.cs)** —
  the Shooter's round. Pooled, ray-swept between frames.
- **[Blast.cs](../../Assets/_Project/Scripts/Enemies/Blast.cs)** — radial damage,
  shared by the detonation and the slam so they can never drift apart.
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

- **The Shooter's first shot misses on purpose.** It is thrown wide on a fixed
  angle — fixed, not random, because a warning shot has to miss *reliably* or it
  eventually kills the player with the round that was supposed to teach them.
  That single decision is what turns "I died from nowhere" into "I got caught
  out": same damage event, completely different feeling. `firstShotDeliberateMiss`
  exists as a toggle so the reason stays visible instead of becoming folklore —
  turn it off and the Shooter immediately feels unfair.
- **The Tank is not a trade.** Too much health to burn down at arm's length, too
  much slam damage to eat, and a windup long enough to leave during. The correct
  answer is to move, keep firing and come back — which only reads because the
  drone nearly stops while charging.
- **Ranged fire is a projectile, not hitscan**, at 18 m/s: fast enough to punish
  standing still, slow enough to sidestep once seen. A hitscan enemy weapon is
  unavoidable and unreadable at the same time.
- **Both ranged archetypes release their token the moment the attack resolves**,
  not when the cooldown ends. A cooldown is that drone's problem, not the pack's.
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

## Audit fixes (2026-08-11)

- **A Shooter's round could hang in the air forever.** `DroneProjectile` treated a
  drone as a hit, and `Resolve` returned early for its own kind WITHOUT despawning
  and without advancing — so the sweep restarted from the same point every frame.
  The round never moved, never expired, and never released its pooled instance; a
  wave of shooters leaked the pool for the rest of the run. Drones are now skipped
  inside the sweep, so the round keeps going.
- **Its weakpoint `Core` was still solid.** `DroneController` lives on the hull;
  the `Core` child carries only a `Weakpoint`. Testing the collider alone
  recognised one and not the other, so a round clipping another drone's core hit
  something with no Health and vanished — the shooter's own kind acting as cover.
  `BelongsToADrone` resolves the collider to the Health behind it first.
- **Every archetype repainted itself Rusher-red on spawn.** `Initialize` calls
  `SetTelegraph(0f)`, and the two ends of the ramp were literals in
  `DroneController` — so the Shooter's amber core and the Tank's crimson one, both
  authored by the builder, were overwritten on the first frame. The colours are
  now per-archetype fields on `DroneConfig`, authored to match each prefab's
  material. Hue says which drone; brightness says how close the attack is.
- **A timed-out attack token left the windup running.** `ForceReleaseAttackToken`
  reset two state fields but never called `attack.Cancel`, so a drone that lost
  its token kept its lunge speed multiplier and its bright telegraph tint — for
  the rest of the wave, looking and moving as though it were about to detonate.
- **The alive cap failed open.** `CanSpawn` returned true with no
  `DifficultyConfig`, removing the cap that exists to protect a 4 GB GPU. It now
  falls back to a hard 40 and logs once.
- **Blast damage could miss the player entirely.** `Blast.Apply` ran a
  16-collider overlap with mask Everything; each drone takes two slots, so in a
  dense pack the player could be outside the truncated result and the detonation
  did nothing at all. Buffer 16 to 64, and a full buffer is now reported.


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
- The agent's radius (0.4, and 0.5 for the Tank) must stay **at or under** the
  radius the surface bakes for (the default humanoid 0.5), or drones will not fit
  through gaps the mesh says exist. The Tank's *hull* is deliberately wider than
  its agent: a fatter agent would refuse paths the mesh allows and the Tank would
  stand still looking broken. The visual clips walls slightly; that is the trade.
- **Shape details carry no colliders.** Fins, barrels and plates are decoration —
  hull and core are the only two things a bullet can find, so where you have to
  aim never depends on cosmetics.
- `DroneRegistry.Alive` is exposed as the concrete `List<T>` on purpose —
  iterating an `IReadOnlyList` boxes the struct enumerator. Index into it; never
  add or remove from outside.
