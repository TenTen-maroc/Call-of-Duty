# Audio

> Last verified: 2026-08-12
> **Verified in play: no. Verified at all: barely.** Everything below compiles
> (typecheck 9/9, zero warnings) and passes the eight guards. Nothing here has
> been executed: Unity was never launched for this work, no test covers it yet,
> the two config assets do not exist on disk yet, and neither component is wired
> into a scene yet. Read [What is not done](#what-is-not-done) before you assume
> the game makes any noise.

## READ THIS FIRST: there is no AudioMixer, and no builder can make one

**The mixer is a human step. It must be authored by hand, once, in the Unity
editor, and committed.** Nothing in `Tools/`, nothing in `AudioBuilder`, and
nothing any future session writes can produce it.

The reason is not laziness or scope. `UnityEditor.Audio.AudioMixerController` —
the only type that can construct a `.mixer` asset — is **internal to
`UnityEditor.dll`**. The runtime `UnityEngine.Audio` namespace exposes
`AudioMixer`, `AudioMixerGroup` and `AudioMixerSnapshot`, and every one of them is
a read-only handle to an asset that already exists. There is no public creation
API, no `ScriptableObject.CreateInstance` path, and no `-executeMethod` path. A
builder that tried would either fail to compile or produce a corrupt asset.

This matters because this project's whole discipline is "nothing in a scene or an
asset is hand-authored — a builder makes it". Audio is the one place that rule
breaks, and a session that does not know it will spend its time writing a builder
that cannot work, or worse, will design a settings screen around a mixer asset
that is not there.

**When someone does author it**, the migration is already prepared and is two
drags, not a rewrite:

| Field | Lives in | What to drop in |
| --- | --- | --- |
| `FootstepConfig.outputGroup` | `Footsteps_Player.asset` | the SFX group |
| `AmbienceConfig.outputGroup` | `Ambience_Arena.asset` | the Ambience group |

Both components already assign `AudioSource.outputAudioMixerGroup` from those
fields; both ship null, and null means "straight to the listener", which is
exactly today's behaviour. Nothing else changes.

Until then, **`AudioListener.volume` is the entire mix**.
[SettingsHub.Apply](../../Assets/_Project/Scripts/Core/SettingsHub.cs) sets it from
the player's saved master volume, and it is a single global multiplier over every
`AudioSource` in the scene. That is genuinely sufficient while the game has one
sound category. The first thing it cannot do is balance music against SFX — which
is the moment the mixer stops being optional.

## Overview

Three things, none of which existed before: the player's **footsteps**, the
arena's **room tone**, and the two config assets that hold every number for both.
There is still no music, no weapon reverb, no occlusion and no mixer.

There are also **no audio clips**. Every clip field in both configs ships empty,
and that is deliberate on two counts:

- **LFS.** Uncompressed WAV runs roughly 10 MB per minute. GitHub's free LFS quota
  is 1 GB of storage and 1 GB of bandwidth per month; the repo is currently using
  1.3 MB of a 400 MB budget (`guard-lfs-budget`). A careless drop of a folder of
  ambience beds would eat that in one commit.
- **Silence must be a valid state.** Both components treat an empty clip array or
  a null clip as "not authored yet" — no log, no warning, no error. A missing WAV
  is not a bug, and a config that shouted about one would produce an error *per
  step*, forever.

## Data Assets

Both live in `Assets/_Project/Data/Game/` and are created by
`CoD → Build Audio Config`
([AudioBuilder.cs](../../Assets/_Project/Scripts/Editor/AudioBuilder.cs)).

### `Footsteps_Player.asset` — [FootstepConfig](../../Assets/_Project/Scripts/Core/FootstepConfig.cs)

| Group | Fields | Shipped default |
| --- | --- | --- |
| Cadence | `strideLength`, `minSpeed`, `firstStepFraction` | 0.85 m, 0.55 m/s, 0.55 |
| Ground probe | `groundMask`, `probeStartHeight`, `probeDistance` | everything but Viewmodel and Ignore Raycast, 0.6 m, 1.6 m |
| Walk | `walkVolume`, `walkPitch` | 0.55, 1.00 |
| Sprint | `sprintVolume`, `sprintPitch` | 0.85, 0.95 |
| Crouch | `crouchVolume`, `crouchPitch` | 0.22, 1.05 |
| Jitter | `pitchJitter`, `volumeJitter` | 0.07, 0.06 |
| Landing | `landMinImpact`, `landVolume`, `landPitch` | 0.12, 0.80, 0.90 |
| Surfaces | `surfaces[]`, `defaultSurface` | `Concrete` (Default layer), `Metal grating` (unreachable), index 0 |
| Mixer | `outputGroup` | null — see above |

Each `SurfaceSet` carries `label`, `layers`, `physicsMaterial`, `stepClips[]`,
`landClips[]`, `volumeScale`, `pitchScale`.

### `Ambience_Arena.asset` — [AmbienceConfig](../../Assets/_Project/Scripts/Core/AmbienceConfig.cs)

| Group | Fields | Shipped default |
| --- | --- | --- |
| Room tone | `roomTone`, `roomToneVolume`, `roomTonePitch` | null, 0.30, 1.00 |
| Fade | `fadeInSeconds` | 1.5 s |
| Placed loops | `emitters[]`, `randomiseStartTime` | four emitters, true |
| Mixer | `outputGroup` | null — see above |

Each `Emitter` carries `label`, `clip`, `localPosition`, `volume`, `pitch`,
`minDistance`, `maxDistance`, `spatialBlend`, `spreadDegrees`, `rolloff`.

The four shipped emitters sit on the arena's own landmarks, taken from
[arena.md](arena.md) — the three lane lights and the centre bunker:

| Label | Local position | Why there |
| --- | --- | --- |
| `Vent_WestLane` | (−14.5, 4.2, 4) | west lane light |
| `Vent_EastLane` | (14.5, 4.2, 4) | east lane light |
| `Vent_NorthLane` | (0, 4.2, 14) | north lane light |
| `PowerHum_CoreBunker` | (0, 3.4, 2) | just above the 3 m bunker roof |

Sound sits where the light already is, so the two agree about where the
facility's machinery lives with no second list of coordinates to keep aligned —
and each lane gets an audible identity, which is the job the lane lights were
added to do for the eye. **None of them is the origin**: (0, 0, 0) is *inside*
`Core_Bunker`, the arena's oldest trap.

## Runtime Types

### [Footsteps](../../Assets/_Project/Scripts/Player/Footsteps.cs) (`CoD.Player`)

Serialized: `_config` (FootstepConfig), `_motor` (PlayerMotor), `_audio` (AudioSource).

Reads [PlayerMotor](../../Assets/_Project/Scripts/Player/PlayerMotor.cs)'s existing
surface: `HorizontalSpeed`, `IsGrounded`, `IsSprinting`, `IsCrouched`,
`LandingImpact`. **PlayerMotor was not modified** — it already exposed everything
this needed.

### [ArenaAmbience](../../Assets/_Project/Scripts/Core/ArenaAmbience.cs) (`CoD.Core`)

Serialized: `_config` (AmbienceConfig). Builds its own `AudioSource` children in
`Awake`, fades them up, then sets `enabled = false`.

## Key Behaviors & Non-Obvious Patterns

- **A distance accumulator, never a timer.** This is the whole design of
  `Footsteps`. A timer fires every N seconds, so walking and sprinting produce the
  same number of steps per second and the player hears legs moving at one rate
  while the world slides past at three — the "running on the spot" defect.
  Accumulating *distance* makes step spacing a property of the ground: the same
  legs, taking the same stride, simply arrive more often when the body moves
  faster. It also means the three gaits need no separate cadence numbers, so
  there is no second table to keep in agreement. **If a future session adds a
  `stepsPerSecond` field, it has undone this.**

- **The accumulator takes `min(intended, actual)`**, and that fixes two opposite
  bugs at once. `PlayerMotor.HorizontalSpeed` is the velocity the motor *wants*:
  it is integrated from input and never learns that `CharacterController.Move` was
  blocked, so a player holding W against a wall reports full walking speed forever
  and a naive `speed * deltaTime` plays footsteps for as long as they lean on it.
  Measuring the transform instead fixes that and introduces the mirror failure — a
  respawn or teleport moves the transform tens of metres in one frame and would
  machine-gun a dozen steps out of it. The minimum keeps each one's answer where it
  is right: the wall drives ACTUAL to zero, the teleport is clipped by INTENT, and
  ordinary walking has the two within a rounding error.

- **One raycast per step, never per frame.** The surface probe fires at the instant
  a step or a landing plays — roughly twice a second — and nowhere else. Probing
  per frame would be sixty casts a second to answer a question asked twice, against
  a budget of 16 KB/frame with 40 drones alive
  ([performance.md](performance.md)) whose current headroom is ~450 B.

- **Pitch jitter is not a garnish.** A footstep is the most-repeated sound in the
  game — roughly twice a second for a whole run, thousands of plays. Digital audio
  repeats a sample *exactly*, and the ear is very good at spotting an exact repeat:
  two identical steps in a row stop reading as legs and start reading as a machine
  playing a file. More clips do not fix it on their own — a random pick from four
  clips lands on the same clip a quarter of the time. `Footsteps.PickClip` therefore
  also rejects the previous index outright, and ±7% pitch turns four clips into
  hundreds of distinct steps.

- **Gait changes volume and pitch and nothing else.** Sprint is *louder and
  slightly lower* than walk, because a sprinting step is a heavier impact, not a
  faster one — the extra rate comes free from the accumulator. Crouch volume (0.22)
  is the entire stealth mechanic the game has.

- **The landing latch.** `PlayerMotor.LandingImpact` is a per-frame value: zeroed at
  the top of the motor's `Update`, set non-zero only on the frame the controller
  regains the ground. That makes it order-dependent, and Unity does not promise
  which of two components on the same object runs first. `Footsteps._landingLatched`
  makes the answer identical either way — fire on the first frame the value is
  non-zero, re-arm only once it is back to zero — which is why this needed no change
  to `PlayerMotor` and no script execution order entry.

- **Surface resolution is physics-material-first, layer-second.** The arena is
  generated and every piece of it lands on the `Default` layer; the project has
  exactly two usable layers and the other one is `Viewmodel` (the gun). Layers are
  therefore too coarse to tell a catwalk from a floor slab, while a `PhysicsMaterial`
  is a per-collider asset a human can drop onto one object without touching the
  builder that made it. `FootstepConfig.ResolveSurface` reads only
  `Collider.sharedMaterial` and `gameObject.layer` — two plain property reads, no
  `GetComponent`, no allocation.

- **`ArenaAmbience` builds its own sources instead of taking serialized ones.** The
  arena scene is generated by `GreyBoxBuilder` and re-generated whenever the
  geometry moves; an emitter hand-placed in that scene is one rebuild away from
  being gone, silently, with the config still listing it. Building from the config
  makes the asset the single source of truth — adding a hum near the shop is a row
  in an asset, not a scene edit — and the only thing wired into the scene is the
  component and its config reference.

- **This is not a pooling violation.** The rule that everything which spawns goes
  through `ObjectPool` exists to stop per-frame `Instantiate`/`Destroy` churn during
  a wave. Ambience emitters are created once in `Awake`, live for the whole scene,
  and are never destroyed or recycled — they are scene furniture, closer to the
  arena's lights than to a bullet.

- **`ArenaAmbience` disables itself when the fade finishes.** The only per-frame
  work it does is the fade, and the fade ends. Keeping a per-frame callback alive
  for the remaining forty minutes of a run to lerp a value that already reached its
  target is a cost with no upside.

- **The fade uses `Time.unscaledDeltaTime`.** The pause menu sets `timeScale` to 0,
  and a scaled fade would freeze there — leaving the arena half-loud for as long as
  the menu is open, then jumping when it closes.

- **Emitters use Linear rolloff and zero Doppler by default.** Logarithmic rolloff
  never actually reaches zero, so every emitter stays faintly audible from
  everywhere and four of them sum into mush in a 40 m room. Doppler on a static loop
  with a walking listener turns strafing into a pitch warble. Rolloff is authored
  per emitter; Doppler is hard-off in `ArenaAmbience.CreateSource` with a comment
  saying why.

## Scene wiring (NOT DONE — this is the handover)

Neither component is in any scene. `10_GreyBox` needs:

1. On the existing **`Player`** GameObject (the one carrying `CharacterController`,
   `PlayerInput`, `PlayerMotor`, `Health`, `PlayerLook`):
   - an `AudioSource` (2D; `Footsteps.Awake` forces `playOnAwake=false`,
     `loop=false`, `spatialBlend=0`, `dopplerLevel=0` itself)
   - a `Footsteps` component with `_config` → `Footsteps_Player.asset`,
     `_motor` → the `PlayerMotor` on the same object, `_audio` → that `AudioSource`
     (`_audio` also falls back to `GetComponent<AudioSource>()` in `Awake`)
2. A new empty GameObject **`Ambience` at world (0, 0, 0)** — emitter positions are
   local to it — carrying `ArenaAmbience` with `_config` → `Ambience_Arena.asset`.

`Footsteps` belongs on the `Player` root and **not** on the camera: the probe
origin is the component's own transform, and the camera's transform carries the
landing dip and the shake.

## Related Systems

- [player.md](player.md) — the motor whose speed, gait and landing signal drive
  every step.
- [settings.md](settings.md) — master volume, and why it is `AudioListener.volume`
  rather than a mixer.
- [arena.md](arena.md) — where the emitter coordinates come from.
- [performance.md](performance.md) — the 16 KB/frame allocation budget both
  components are written against.
- [rendering.md](rendering.md) — the other half of perceived production value.

## Gotchas

- **Do not write a builder for the mixer.** See the top of this file. It cannot
  work.
- **`AudioSource.pitch` retunes voices already playing on that source**, including
  earlier `PlayOneShot` clips. `Footsteps` uses one source, so a step that begins
  while the previous one is still ringing will pull the older one to the new pitch.
  At ~2 steps/second against clips of ~0.3 s this is rare and inaudible; if a
  future session authors long, tailed footstep samples it becomes real, and the fix
  is two alternating sources (left foot / right foot), not a shorter clip.
- **`defaultSurface` is a hand-typed index into a hand-edited array.** It is clamped
  in `DefaultSurfaceIndex` and in `OnValidate` rather than trusted — an out-of-range
  read would throw once per step.
- **A `SurfaceSet` with `layers` set to `Nothing` must never match**, and
  `ResolveSurface` checks `layers.value != 0` explicitly for that. Layer 0 is
  `Default`, which is *every object in the arena*, so a naive mask test would give
  the whole arena to whichever entry was left blank.
- **Surface index 1, `Metal grating`, matches nothing today** and that is
  intentional, not a bug: it makes the per-surface mechanism visible and one drag
  from real. Giving it a layer mask would steal every step from `Concrete`.
- **`AudioBuilder` is idempotent the same way `MissionBuilder` is** — the configure
  callback runs **on create only**, so tuned values and assigned clips survive a
  re-run. The trap that comes with that is identical: renaming a path in the builder
  does not rename the asset, it creates a fresh default one, orphans every tuned
  value in the old file, and reports success.
- **`ArenaAmbience.StartLoop` is deliberately not called `Start`.** A method named
  `Start` with parameters on a MonoBehaviour is a Unity magic-method signature
  mismatch and produces a console warning at runtime — invisible to typecheck.
- **There is a second, unrelated surface concept in flight.**
  [SurfaceType.cs](../../Assets/_Project/Scripts/Weapons/SurfaceType.cs) in
  `CoD.Weapons` is an enum keyed on `Collider.gameObject.layer`, authored in
  `ImpactConfig`, for what a *bullet* does on impact. Footsteps cannot use it —
  `CoD.Player` references `CoD.Core` and not `CoD.Weapons` — so the two mechanisms
  are currently independent. **The convergence, when someone wants it, is to move
  `SurfaceType` into `CoD.Core` and key both tables off it**, so a catwalk sparks
  and rings from the same authored fact. Do not duplicate a third surface table.

## What is not done

- **Unity was never launched for this work.** No editor, no `-runTests`, no
  `verify-build.mjs`. The gates that actually ran are `node Tools/typecheck.mjs`
  (9/9, zero errors and zero warnings) and `node Tools/check.mjs` (8 guards).
- **The two `.asset` files do not exist yet.** Somebody must run
  `CoD → Build Audio Config` (menu, or `-executeMethod
  CoD.EditorTools.AudioBuilder.BuildAudioHeadless`).
- **Nothing is wired into a scene** — see [Scene wiring](#scene-wiring-not-done--this-is-the-handover).
- **There are no clips**, so even fully wired the game is still silent. Authoring
  or sourcing footstep and ambience audio is the next real step, and it is the one
  that costs LFS budget — check `guard-lfs-budget` before committing any.
- **No test covers any of this.** An EditMode test over `FootstepConfig.ResolveSurface`
  and `DefaultSurfaceIndex` is cheap and worth having; a PlayMode test that drives
  the motor across the arena and counts steps against distance would be the one that
  actually proves the accumulator.
- **No mixer, no music, no weapon reverb, no occlusion.** All out of scope here.
