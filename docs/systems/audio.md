# Audio

> Last verified: 2026-08-12
> **Verified in play: no. Verified by machine: only by the compiler.** Everything
> below compiles (typecheck 9/9, zero warnings) and passes the guards. Both config
> assets are now on disk. Neither component has ever *run*: Unity has not been
> launched for any of this work, no test covers it, and the scene wiring described
> in [Scene wiring](#scene-wiring) was written but **never executed** — the menu
> item exists, nobody has pressed it. Read
> [What is not done](#what-is-not-done) before you assume the game makes any noise.

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
Those three names are the contract [SceneWiring](../../Assets/_Project/Scripts/Editor/SceneWiring.cs)
writes through `SerializedObject`, and renaming one without updating that file is a
silent null no compiler catches — so `SetRef` there reports a missing field as a
hard failure with an exit code, rather than shrugging.

Reads [PlayerMotor](../../Assets/_Project/Scripts/Player/PlayerMotor.cs)'s existing
surface: `HorizontalSpeed`, `IsGrounded`, `IsSprinting`, `IsCrouched`,
`LandingImpact`. **PlayerMotor was not modified** — it already exposed everything
this needed.

### [ArenaAmbience](../../Assets/_Project/Scripts/Core/ArenaAmbience.cs) (`CoD.Core`)

Serialized: `_config` (AmbienceConfig) — **and nothing else**. Builds its own
`AudioSource` children in `Awake` from the rows in the asset, fades them up, then
sets `enabled = false`. There is no emitter list to wire in the scene, by design:
see [Scene wiring](#scene-wiring).

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

## Scene wiring

`CoD → Wire Scene Extras`
([SceneWiring.cs](../../Assets/_Project/Scripts/Editor/SceneWiring.cs)), or headless:

```bash
Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.SceneWiring.WireSceneExtrasHeadless
```

**This is a separate step from every other builder, and it has never been run.**
Both components, both config assets and this whole page shipped in one commit and
the game stayed silent, because nothing put either component into a scene. Every
gate was green the whole time: the code compiled, the guards passed over it, the
assets were on disk with correct defaults, and the feature did not exist at
runtime. Nothing in this project's toolchain can tell "installed" from "compiled",
which is why the pass ends by re-opening the saved scene and proving the
components are in it.

What it puts into `10_GreyBox`:

1. On whatever GameObject carries **`PlayerMotor`** — found by component, never by
   the name `Player`, so renaming it in the builder cannot turn this into a no-op:
   - an `AudioSource` on that same root (2D; `Footsteps.Awake` forces
     `playOnAwake=false`, `loop=false`, `spatialBlend=0`, `dopplerLevel=0` itself,
     and the pass writes them at author time too so the Inspector tells the truth)
   - a `Footsteps` with `_config` → `Footsteps_Player.asset`, `_motor` → the
     `PlayerMotor` on the same object, `_audio` → that `AudioSource`
2. A root GameObject **`Ambience` at world (0, 0, 0)** carrying `ArenaAmbience`
   with `_config` → `Ambience_Arena.asset`. **That is its only serialized field** —
   there are no emitter transforms to wire, because the component builds its own
   `AudioSource` children in `Awake` from the rows in the asset.

`Footsteps` belongs on the `Player` root and **not** on the camera: the probe
origin is the component's own transform, and the camera's transform carries the
landing dip and the shake. The pass warns about any `Footsteps` it finds without a
`PlayerMotor` beside it.

The `AudioSource` goes on the **player root** rather than a child of its own, for
robustness rather than tidiness: `Footsteps.Awake` falls back to
`GetComponent<AudioSource>()` on its own GameObject, so a `_audio` reference that
failed to persist still resolves at runtime. It is safe because exactly one script
in the project calls `GetComponent<AudioSource>()` — that fallback — and nothing
else on the `Player` owns a source (the weapon's two live on the camera, the HUD's
on the canvas).

### Run order, and the way this silently un-installs itself

**`CoD → Build Grey Box` first, then this.** `GreyBoxBuilder` does not edit
`10_GreyBox`; it calls `EditorSceneManager.NewScene(EmptyScene)` and writes a brand
new scene over the top. Every component this pass adds is therefore **gone** after
a grey box rebuild — silently, with no error and no missing reference, because the
scene is simply whole and quiet again. Re-run `Wire Scene Extras` after every grey
box build, exactly as `CoD → Build Missions` has to be re-run after one.

That is also why the pass is idempotent: "run it again" is the fix, so running it
again has to be free. It tests for the *component*, not for an object name — a
second `Footsteps` on the player would be a flam rather than a louder step, and a
second `ArenaAmbience` would build a full second set of emitters from the same
asset. References are re-asserted on every run (a null one is a component that
warns once at `Awake` and then does nothing all run), but `SetRef` writes only when
the value actually differs, so a second run leaves the scene byte-identical and
never re-saves it.

### What to read in the log

One line, always:

```text
SceneWiring: added N component(s), rewired M reference(s), unresolved K  [Assets/_Project/Scenes/10_GreyBox.unity]
```

- **First run on a freshly built grey box**: expect `added 3` (a `Footsteps`, an
  `AudioSource`, an `ArenaAmbience`), `rewired 4` (`_config`, `_motor`, `_audio`,
  and the ambience `_config`), `unresolved 0`.
- **Second run, immediately after**: `added 0, rewired 0, unresolved 0`. Anything
  else means the pass is not idempotent, or the grey box was rebuilt in between.
- **`unresolved` must be 0.** Non-zero prints
  `SceneWiring: UNRESOLVED after save+reload:` with one line per failure and the
  headless entry point exits 1. `(no such serialized field)` in that list is the
  serious one — it means a field was renamed or a name was guessed.
- The success line is
  `SceneWiring: footsteps and ambience are in the scene, and every reference
  survived a save/reload round trip.`
- Warnings that are worth reading but do not fail the run: a `Footsteps` with no
  `PlayerMotor` beside it, more than one `ArenaAmbience`, and an `Ambience` root
  that is not at the world origin with identity rotation and unit scale (its
  emitter coordinates are local to it, so a drift silently moves the whole room
  tone; the pass reports it and deliberately does not correct it, because an offset
  might be a second arena).

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
- **Empty clip arrays are silent on every path — audited line by line, not
  assumed.** `PlayStep` and `HandleLanding` both take `PickClip`, which returns
  `null` for a null or zero-length array, and both `return` on it without logging.
  `Surface(index)` returns `null` when nothing is authored at all
  (`DefaultSurfaceIndex` is `-1` for an empty `surfaces` array) and both callers
  return on that too. `ArenaAmbience.Build` counts only non-null clips, and a count
  of zero sets `enabled = false` in `Awake` with no log. The *only* audio log in
  either component is `Footsteps.Awake`'s one-shot warning for a missing `_config`,
  `_motor` or `_audio` — a wiring fault, fired once, never per step. **Nothing here
  can spam the console**, which is what makes it safe to wire the components in
  before any WAV exists.
- **A wired, clipless `Footsteps` is not free.** It stays `enabled`, so `Update`
  runs every frame and fires one `Physics.Raycast` per stride — roughly twice a
  second — to resolve a surface whose clips it will then find empty and discard.
  It allocates nothing (the single-hit `out RaycastHit` overload, cached
  references, float maths), so it does not touch the 16 KB/frame budget; the cost is
  CPU only and negligible. Worth knowing before anyone profiles a silent build and
  wonders what is casting rays. `ArenaAmbience` does not have this problem — it
  disables itself in `Awake` when nothing is authored.
- **`Wire Scene Extras` is not part of any build gate.** `GreyBoxVerify` does not
  know these two components exist, so a grey box rebuild that drops them exits 0
  with every reference green. The wiring pass carries its own round-trip
  verification instead. If a future session folds these into `GreyBoxBuilder`, fold
  the checks into `GreyBoxVerify` in the same commit or the gate goes back to being
  blind to exactly this failure.

## What is not done

- **Unity has still never been launched for any of this.** No editor, no
  `-runTests`, no `verify-build.mjs` — not for the components, not for the configs,
  and not for the wiring pass, which was written and never executed. The gates that
  actually ran are `node Tools/typecheck.mjs` (9/9, zero errors and zero warnings)
  and `node Tools/check.mjs`.
- **The two `.asset` files now exist** — `Footsteps_Player.asset` and
  `Ambience_Arena.asset`, both under `Assets/_Project/Data/Game/`, both carrying the
  right `m_Script` guid. `CoD → Build Audio Config` is still the way to recreate
  them if they are ever lost, and it leaves an existing one untouched.
- **`CoD → Wire Scene Extras` has never been run**, so `10_GreyBox` on disk still
  contains neither component. Until somebody runs it, everything on this page is
  code that cannot execute. See [Scene wiring](#scene-wiring) for the exact log
  lines that prove it worked.
- **There are no clips**, so even fully wired the game is still silent. Authoring
  or sourcing footstep and ambience audio is the next real step, and it is the one
  that costs LFS budget — check `guard-lfs-budget` before committing any.
- **No test covers any of this.** An EditMode test over `FootstepConfig.ResolveSurface`
  and `DefaultSurfaceIndex` is cheap and worth having; a PlayMode test that drives
  the motor across the arena and counts steps against distance would be the one that
  actually proves the accumulator.
- **No mixer, no music, no weapon reverb, no occlusion.** All out of scope here.
