# Player

> Last verified: 2026-08-11 (code compiles clean; has not been run)

## Overview

Movement, look, and input for the single first-person player. Four components on
one rig, each with a narrow job: `PlayerInput` reads intent, `PlayerMotor` moves
the capsule, `PlayerLook` aims the camera, `CameraShake` adds cosmetic violence.
Every tunable number comes from `GameConfig`.

## Data Assets

- **[GameConfig.cs](../../Assets/_Project/Scripts/Core/GameConfig.cs)** — player
  health, movement speeds, gravity, jump height, capsule heights, acceleration,
  sensitivity, pitch clamp, FOV, landing dip, slow-mo scale. One asset, in
  `Assets/_Project/Data/Game/`.
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
  the body, pitch on the pivot. Owns recoil offset, ADS sensitivity, FOV, and the
  landing dip. Exposes `AimRay`.
- **[CameraShake.cs](../../Assets/_Project/Scripts/Player/CameraShake.cs)** —
  trauma-based, decaying, applied as a local offset on the camera only.

## Rig Layout

Built by `CoD → Build Grey Box`:

```
Player            CharacterController, PlayerInput, PlayerMotor, PlayerLook, Health, WeaponController
└ CameraPivot     pitch, set by PlayerLook
  └ Main Camera   Camera, AudioListener, CameraShake, 2x AudioSource
    ├ Muzzle      + MuzzleLight (point, starts disabled)
    └ CasingEject
```

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
- `LandingImpact` scales with impact velocity, so a hop and a drop read
  differently.
- **FOV is VERTICAL in Unity.** 62 ≈ 95 horizontal at 16:9. Typing 95 gives a
  ~120° fisheye; `GameConfig.OnValidate` warns above 80.
- Shake lives on the camera, aim on the pivot — see [weapons.md](weapons.md).

## Related Systems

- [weapons.md](weapons.md) — consumes input, aim ray and motion state; pushes recoil.

## Gotchas

- `PlayerInput` fails soft: a missing action logs once and reads as zero rather
  than throwing every frame. Check the console if a key does nothing.
- Sensitivity is a raw multiplier on mouse delta, not a normalised value — it
  belongs behind a settings slider before anyone else plays this.
- `Cursor.lockState` is set in `Awake` and never restored; a pause menu will need
  to own that.
- Nothing here has ever executed.
