# Player

> Last verified: 2026-08-11 (runs; movement, look and firing confirmed in play)

## Overview

Movement, look, and input for the single first-person player. Four components on
one rig, each with a narrow job: `PlayerInput` reads intent, `PlayerMotor` moves
the capsule, `PlayerLook` aims the camera, `CameraShake` adds cosmetic violence.
Every tunable number comes from `GameConfig`.

## Data Assets

- **[GameConfig.cs](../../Assets/_Project/Scripts/Core/GameConfig.cs)** — player
  health, movement speeds, gravity, jump height, capsule heights, acceleration,
  sensitivity, pitch clamp, FOV, landing dip, slow-mo scale. One asset, in
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
  the body, pitch on the pivot. Owns recoil offset, ADS sensitivity, FOV, and the
  landing dip. Exposes `AimRay`.
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
Player            CharacterController, PlayerInput, PlayerMotor, PlayerLook, Health, WeaponController
└ CameraPivot     pitch, set by PlayerLook
  └ Main Camera   Camera, AudioListener, CameraShake, 2x AudioSource
    └ WeaponRig   WeaponSway  (pose, bob, sway)
      └ Viewmodel 8 collider-less blocks forming the rifle
        ├ Muzzle       flash + MuzzleLight, at the barrel tip
        └ CasingEject  at the ejection port, rotated outward
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

## Gotchas

- `PlayerInput` fails soft: a missing action logs once and reads as zero rather
  than throwing every frame. Check the console if a key does nothing.
- Sensitivity is a raw multiplier on mouse delta, not a normalised value — it
  belongs behind a settings slider before anyone else plays this.
- `Cursor.lockState` is set in `Awake` and never restored; a pause menu will need
  to own that.
- Verified in play: movement, look, sprint and firing. NOT yet verified:
  crouch headroom blocking, landing dip, and the sprint-to-fire delay.
