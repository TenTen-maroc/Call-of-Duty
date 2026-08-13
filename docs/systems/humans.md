# Human enemies

> Last verified: 2026-08-13. The Meridian Rifleman is compiled, builder-verified,
> covered by EditMode and PlayMode tests, and visually reviewed in the development
> player. Combat feel, animation foot contact, audio balance, and RTX 3050 frame
> time still require an interactive human pass.

## Overview

Humans do not create a second enemy engine. `Meridian_Rifleman` is a
`DroneConfig + RangedBurst + prefab` and reuses `DroneController`,
`DroneSpawner`, `DroneRegistry`, attack tokens, navigation, health, projectiles,
and pooling. The optional `HumanEnemyPresentation` adds bounded cover choices,
aim/firing posture, regional reactions, deferred corpse presentation, ragdoll,
and pooled-state restoration.

`MeridianHumanBuilder` owns the shared Humanoid avatar/controller, prefab,
materials, regional hit rig, tactical equipment, two property-block variants,
LOD group, ragdoll bodies, configs, and all gore prefabs. Rebuild generated
content through that builder; do not hand-edit the prefab.

## Regional damage

`HitRegion` is append-only: Torso retains value zero, followed by Head, both
arms, both legs, and Armor. A `HitZone` forwards to root `Health` after reading
the factor from `HitZoneConfig`: head/torso 1.0, arms 0.75, legs 0.70, armor
0.45. Head zones also use the existing `Weakpoint`; the weapon remains the sole
owner of `headshotMultiplier`, so it is applied once.

`DamageInfo` carries region and `DamageKind` while preserving its original
constructor. Projectiles, hitscan, pierce, explosions, and effect follow-ups
therefore share one readonly allocation-free payload. Armor never enters the
flesh path. Arm damage briefly disrupts aim; leg damage briefly slows/stumbles;
all reaction state resets through the pool.

## Cover and telegraph

The outdoor builder creates 14 `CoverPoint`s beside the same generated cover
geometry and registers them with one `CoverRegistry`. Claims are exclusive and
released on death, despawn, or reuse. Each decision checks a fixed authored
budget instead of globally scanning every point per frame. Lane affinity gives
the rifleman lightweight flanking without a behavior-tree framework.

`RangedBurst` retains the deliberate opening miss and visible pooled projectile.
The agent strongly slows while firing, faces the target, strafes between bursts,
plays preparation/fire/reload states, and uses the existing three-token fairness
cap. Null bark audio remains valid; no final voice was fabricated.

## Violence presentation

`GoreLevel` is Off, Reduced, or Extreme and defaults to Extreme from
`SettingsConfig`. `GoreManager` owns fixed oldest-first rings: 96 decals, 24
wounds/stumps, 12 pools, 8 corpses, 4 ragdolls, and 24 severed parts. Effects use
the project pool; there is no runtime mesh cutting or per-hit Instantiate/Destroy.

- Off: neutral impact/death presentation, no blood objects.
- Reduced: short spray and limited surface decals, no parts, stumps, or pools.
- Extreme: directional spray, attached wounds, projected decals, delayed pools,
  lethal region dismemberment, and immediate explosive ragdoll.

Dead hitboxes disable immediately. Hidden regions, ragdoll bodies, colliders,
animator state, reactions, and attached effects are restored before reuse.

## Verification

`HumanCombatDataTests` pins region factors, gore policy/caps, and the Humanoid
prefab contract. `OutdoorHumanPlayModeTests` loads `11_AtlasOutpost`, verifies
collider-free presentation art, every baked spawn path, the 12-human cap,
explosive death, dead hitbox behavior, and reset. The Mission 2 screenshot route
renders nine 1600x900 frames covering selection, approach, first contact,
regional hits, Extreme aftermath, Reduced, and Off.
