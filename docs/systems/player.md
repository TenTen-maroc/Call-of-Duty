# Player

> Last verified: 2026-08-12 (runs; movement, look and firing confirmed in play).
>
> **The two-camera viewmodel split is SOURCE ONLY.** What has actually been
> executed against it is two commands: `node Tools/typecheck.mjs` (nine
> assemblies, zero errors and zero warnings) and `node Tools/check.mjs` (eight
> guards). That is all. The scenes and prefabs have **not** been regenerated, so
> the arrangement described under "The viewmodel camera" below does not yet exist
> in `10_GreyBox.unity` or in `Fx_MuzzleFlash.prefab`, and
> [ViewmodelTests.cs](../../Assets/_Project/Tests/PlayMode/ViewmodelTests.cs)
> would **fail** if it ran right now — on purpose: every assertion in it is
> written to fail against the pre-split scene, which is the only way a test is a
> gate rather than decoration. Nothing below is locked in, proven, or covered
> until `CoD → Build Grey Box` has regenerated the scenes, `GreyBoxVerify` has
> passed, and both suites have run green. Nobody has seen any of it on screen.

## Overview

Movement, look, and input for the single first-person player. Four components on
one rig, each with a narrow job: `PlayerInput` reads intent, `PlayerMotor` moves
the capsule, `PlayerLook` aims the camera, `CameraShake` adds cosmetic violence.
Every tunable number comes from `GameConfig`.

The rig carries **two cameras**: a base camera that draws the world and a URP
overlay camera that draws the gun and nothing else. They share a transform and
never an FOV.

## Data Assets

- **[GameConfig.cs](../../Assets/_Project/Scripts/Core/GameConfig.cs)** — player
  health, movement speeds, gravity, jump height, capsule heights, acceleration,
  sensitivity, pitch clamp, FOV, **viewmodel FOV and its ADS delta**, landing
  dip, slow-mo scale. One asset, in
  `Assets/_Project/Data/Game/`. The player's `Health` reads its max from here
  (via its `_playerConfig` field, wired by the builder) — props and targets use
  a `HealthConfig` instead.
- **[CoD.inputactions](../../Assets/_Project/Settings/CoD.inputactions)** — the
  `Player` map: Move, Look, Fire, Aim, Reload, Jump, Sprint, Crouch. Keyboard and
  mouse, with arrow keys alongside WASD.

## Runtime Types

- **[PlayerInput.cs](../../Assets/_Project/Scripts/Player/PlayerInput.cs)** —
  looks actions up by name once in `Awake` and exposes them as plain values. All
  gameplay code asks this, so rebinding or adding a gamepad never reaches into
  the motor or the weapon. New Input System only; `Input.GetKey` is never used.
- **[PlayerMotor.cs](../../Assets/_Project/Scripts/Player/PlayerMotor.cs)** —
  `CharacterController` movement. Publishes `IsSprinting`, `IsCrouched`,
  `IsGrounded`, `HorizontalSpeed`, `LandingImpact` for the weapon and camera.
- **[PlayerLook.cs](../../Assets/_Project/Scripts/Player/PlayerLook.cs)** — yaw on
  the body, pitch on the pivot. Owns recoil offset, ADS sensitivity, **both
  cameras' FOV**, and the landing dip. Exposes `AimRay`. Serialized `_camera` is
  the world camera; `_viewmodelCamera` is the overlay.
- **[CameraShake.cs](../../Assets/_Project/Scripts/Player/CameraShake.cs)** —
  trauma-based, decaying, applied as a local offset on the camera only.
- **[WeaponSway.cs](../../Assets/_Project/Scripts/Player/WeaponSway.cs)** — moves
  the viewmodel the camera does not: sway lagging the look input, bob driven by
  real ground speed, and a tucked sprint pose. `SetAdsProgress` is pushed in by
  `WeaponController` each frame so the gun rises into the sight on the same blend
  as the FOV change.

## Rig Layout

Built by `CoD → Build Grey Box`:

```text
Player                CharacterController, PlayerInput, PlayerMotor, PlayerLook, Health, WeaponController
└ CameraPivot         pitch, set by PlayerLook
  └ Main Camera       Camera (Base), AudioListener, CameraShake, CameraGraphics, 2x AudioSource
    │                 tag MainCamera · near 0.05 · cullingMask EXCLUDES Viewmodel
    │                 CameraGraphics holds BOTH cameras (see "The post-processing setting")
    └ ViewmodelCamera Camera (Overlay), in the base camera's cameraStack
      │               untagged · NO AudioListener · near 0.01 / far 5 · cullingMask = Viewmodel only
      ├ ViewmodelKey  directional light on the Viewmodel layer — the gun's steady key light
      └ WeaponRig     WeaponSway  (pose, bob, sway)          [layer: Viewmodel]
        └ Viewmodel   8 collider-less blocks forming the rifle
          ├ Muzzle       flash spawn point, at the barrel tip
          │ └ MuzzleLight  layer Default ON PURPOSE — it lights the room, not the gun
          └ CasingEject  at the ejection port, rotated outward
```

Not in the rig, but part of it: **`Fx_MuzzleFlash`** is pooled, spawned at the
muzzle on every shot, and carries a `FlashLight` point light on the Viewmodel
layer. That light is the gun's half of the muzzle flash — see "Lighting" below.

Layer `Viewmodel` is user slot **8** in
[TagManager.asset](../../ProjectSettings/TagManager.asset). It was the first
custom layer in the project.

## Key Behaviors & Non-Obvious Patterns

- **Camera work is in `LateUpdate`, always.** In `Update` it could run before the
  motor moved the body that frame, producing jitter that is painful to diagnose.
- **`CharacterController.Move` runs in `Update`, not `FixedUpdate`** — it is not
  rigidbody physics, and it reads best tied to the rendered frame.
- **Crouch will not stand up into geometry.** A `SphereCast` checks headroom
  first; without it the player clips a ceiling and falls out of the arena.
- **Sprint is forward-only and cancelled by crouch**, otherwise it is free speed
  in every direction and the arena effectively shrinks.
- A `-2` downward bias while grounded keeps `isGrounded` true on slopes and steps.
- **Hitting a ceiling zeroes upward velocity** (`CollisionFlags.Above`), or the
  controller stays glued to the ceiling for the rest of the jump arc.
- `LandingImpact` scales with impact velocity, so a hop and a drop read
  differently.
- **FOV is VERTICAL in Unity.** 62 ≈ 95 horizontal at 16:9. Typing 95 gives a
  ~120° fisheye; `GameConfig.OnValidate` warns above 80.
- Shake lives on the camera, aim on the pivot — see [weapons.md](weapons.md).
- **The viewmodel has no colliders and casts no shadows.** A collider on the
  player's own gun sits in front of the camera, so every shot would raycast into
  the weapon; a viewmodel casting scene shadows looks like the floating prop it is.
- Sway is computed in `LateUpdate`, after `PlayerLook` has aimed, so it reacts to
  the final rotation rather than last frame's.

## The viewmodel camera (2026-08-12)

Until this change there was **one** camera and `WeaponRig` hung off it on the
Default layer, inside the world culling mask. Two defects came free with that,
and no gate in the repo could see either:

1. **The gun clipped through every wall.** The barrel tip is 0.53 m in front of
   the lens and the world near plane is 0.05 m.
2. **The gun warped whenever the world FOV moved** — the sprint bonus and the
   ADS/fire-kick offset both lerp `_camera.fieldOfView`, and a child of that
   camera is re-projected with it.

**The arrangement.** The base camera keeps the `MainCamera` tag, the
`AudioListener`, `CameraShake` and `CameraGraphics`, and simply clears the
Viewmodel bit out of its culling mask. The overlay camera is a child of it:
`renderType = Overlay`, culling mask = Viewmodel only, near 0.01 / far 5,
appended to the base camera's `cameraStack`. `renderType` is set **before** the
stack append — URP rejects a Base camera found inside a stack and logs an error,
and this builder runs headlessly where an error is the verdict.

**The FOV split.** `PlayerLook.UpdateFov` now drives two cameras from two
formulas:

| | world camera (`_camera`) | viewmodel camera (`_viewmodelCamera`) |
| --- | --- | --- |
| base | saved `fovVertical` (player setting) | `GameConfig.viewmodelFovVertical` |
| sprint | `+ sprintFovBonus` | never |
| ADS / fire kick | `+ _fovOffset` from the weapon | `+ viewmodelAdsFovDelta * _adsProgress` |
| easing | exponential, `sprintFovEaseTime` | none — `_adsProgress` is already a ramp |

`viewmodelFovVertical` ships equal to the default `baseFovVertical` (62) so the
pose the viewmodel was authored against does not move. The player's FOV slider
now changes the world and leaves the gun alone.

**Lighting.** A camera's culling mask culls **lights** as well as renderers — by
the light's own GameObject layer — so the sun and all four arena lights are
invisible to the overlay camera. Without its own light the gun would render on
ambient alone: near black, on a metallic 0.85 material in a bunker whose only
reflection source is a flat dark custom cubemap. `ViewmodelKey`, a shadowless
directional light parented to the overlay camera, is that steady light; it is on
the Viewmodel layer so the world camera culls it in turn.

**The muzzle flash needs TWO lights, and this is the trap.** The two masks are
disjoint, so no single light can reach both the room and the gun. `MuzzleLight`
is pulled back out of the recursive layer set and left on Default, because its
whole job is to light the room and the drones for two frames — and that is
exactly what makes it invisible to the only camera that draws the weapon. Left
at one light, the split silently cost the gun its flash: the most repeated
visual event in the game stopped touching the object it comes out of.

The gun's half is a second point light, `FlashLight`, on the pooled
`Fx_MuzzleFlash` prefab, on the Viewmodel layer. It could not simply hang under
`Muzzle` beside the first: `WeaponController` drives one serialized `Light` by
toggling `Behaviour.enabled`, and `enabled` is per **component** — it does not
cascade to children, so a second light parented under `MuzzleLight` would burn
permanently. Living on the flash prefab instead, its lifetime is the pool's
(`muzzleFlashLifetime`), so the light and the flash sprite are one object and
cannot desync. The cost is that its range and intensity are builder numbers
rather than per-weapon `WeaponConfig` ones; if a weapon ever wants its own flash
brightness on the viewmodel, add a second serialized `Light` field to
`WeaponController` and drive it from `UpdateMuzzleLight` beside the first.

**What is on which layer.**

- Viewmodel: `WeaponRig` and everything under it, `ViewmodelKey`, and the
  `Fx_MuzzleFlash` prefab including its `FlashLight` (set on the prefab in
  `BuildMuzzleFlashPrefab`, not at spawn time, so the pool stays a dumb spawner).
- Not Viewmodel: `MuzzleLight` (Default), and `Fx_ShellCasing` — the casing has
  a `Rigidbody` and must bounce off the real floor, so it stays on Ignore
  Raycast in the world.

**The post-processing setting.** `CameraGraphics` holds **both** cameras and
mirrors `renderPostProcessing` onto the overlay. It has to: URP resolves a
stack's post-processing at the **last** camera in the stack that has the flag
enabled, which is the viewmodel camera. Wired to the base camera alone, a player
choosing "Post-processing: Off" cleared the base, left the overlay true, and the
frame stayed graded — a player-facing R2 row that does nothing, with the base
camera reporting success to anything that asked it. Anti-aliasing is **not**
mirrored: URP takes a stack's post AA from the base camera, so writing it to the
overlay is either ignored or a second full-screen pass on a 4 GB laptop GPU.

**Never built, never run, never played.** Everything above compiles across all
nine assemblies with zero warnings and passes the guards. Nothing else has
happened to it.

[ViewmodelTests.cs](../../Assets/_Project/Tests/PlayMode/ViewmodelTests.cs)
*describes* the arrangement — culling masks, stack shape, the listener and which
camera carries it, layers, near planes, both halves of the muzzle flash, and the
FOV split itself — and every one of those assertions is written to **fail**
against the pre-split scene. That is what makes it a gate, and it is also why
the suite is currently red: the scene on disk is still the pre-split one. It
proves nothing until `CoD → Build Grey Box` regenerates the scenes and the
prefabs and the suite is run.

Order of work from here: regenerate, run `GreyBoxVerify`, run both suites, then
play. The first play session should check, in order: that the gun is lit (not
black), that firing lights the gun *and* the wall behind it, that the grading
covers the viewmodel, that 62/62 still frames it where it used to sit, and that
walking into a wall no longer eats the barrel.

## Audit fixes (2026-08-11)

- **Sustained fire could flip the camera past vertical.** `_pitch` was clamped to
  `GameConfig.pitchClamp`, but the value written to the camera pivot was the
  unclamped sum `_pitch + _recoilPitch` — and recoil has no bound and, at any real
  fire rate, never recovers mid-magazine: recovery is gated on `recoveryDelay`
  (0.09 s) since the last shot while 700 RPM puts shots 0.086 s apart. A held
  trigger piled up a magazine's worth of kick, tens of degrees, and drove the view
  upside down. The COMPOSED pitch is clamped now.
- **A max-health upgrade no longer refills the bar.** `Health.AdjustMax` moves
  current health by the same amount the maximum moved, so +25 max reads as
  125/125 rather than the broken-looking 100/125 — but buying anything else no
  longer heals. `ConfigureMax` keeps the refill, because a pooled drone needs it.
- **Negative damage healed.** `Mathf.Min(amount, current)` passed a negative
  straight through; it is clamped at both ends now.

### Known, deliberately not changed

Two real findings that would alter gun feel, held for the tuning session rather
than decided here:

- **Weapon sway saturates on any mouse movement.** `WeaponSway` clamps the raw
  `<Mouse>/delta` — accumulated pixels, so at least 1 for essentially any motion —
  to plus or minus 1 before scaling, so the sway is binary rather than
  proportional. Fixing it means choosing a reference delta, which is a feel call.
- **Bob is scaled by a literal 6 m/s**, which sits between the shipped `walkSpeed`
  5.2 and `sprintSpeed` 8.0, so walking already reads at 0.87 of full bob.

## Related Systems

- [weapons.md](weapons.md) — consumes input, aim ray and motion state; pushes recoil.
- [rendering.md](rendering.md) — the post-processing stack the overlay camera
  joins.
- [settings.md](settings.md) — `CameraGraphics` applies the player's post/AA
  choices: post to both cameras in the stack, AA to the base camera only.

## Gotchas

- `PlayerInput` fails soft: a missing action logs once and reads as zero rather
  than throwing every frame. Check the console if a key does nothing.
- Sensitivity is a raw multiplier on mouse delta, not a normalised value — it
  belongs behind a settings slider before anyone else plays this.
- `Cursor.lockState` is set in `Awake` and never restored; a pause menu will need
  to own that.
- Verified in play: movement, look, sprint and firing. NOT yet verified:
  crouch headroom blocking, landing dip, and the sprint-to-fire delay.
- **`LayerMask.NameToLayer` returns -1 for a missing layer and Unity assigns -1
  without complaining.** `GreyBoxBuilder.RequireViewmodelLayer` logs and throws
  instead, so deleting the `Viewmodel` row from `TagManager.asset` fails the
  build rather than producing a gun no camera draws.
- **`viewmodelAdsFovDelta` is wired but not yet driven.** `PlayerLook` exposes
  `SetAdsProgress`, and nothing calls it: `WeaponController` lives in
  `CoD.Weapons`, which references `CoD.Player` and not the reverse, so the value
  has to be pushed the way `SetFovOffset` already is. One line in
  `WeaponController.UpdateAds` — `_look.SetAdsProgress(_adsProgress);` beside the
  existing `_sway.SetAdsProgress(_adsProgress)` — turns it on. Until then the
  viewmodel FOV is simply constant, which is correct, just less alive.
- **`CameraGraphics` takes two cameras in the arena and one in the menu.**
  `_viewmodelCamera` is deliberately optional — `20_MainMenu` has no viewmodel —
  so a null there is legal in one scene and a broken setting in the other. That
  asymmetry is why `GreyBoxVerify` checks the field on the grey box pass and
  **not** in `VerifyMenuScene`. Mirror `renderPostProcessing` onto the overlay
  and nothing else; anti-aliasing belongs to the base camera for the whole stack.
- **A camera's culling mask culls lights, by the LIGHT's GameObject layer.** This
  is the single most expensive fact in this file: it is why the gun needs its own
  key light, why the muzzle flash needs two lights, and why moving a light
  "tidily" back into the recursive layer set breaks something that logs nothing.
  `Light.cullingMask` is not the answer — URP ignores per-light culling masks and
  uses rendering layers instead, so the camera mask is the only lever here.
