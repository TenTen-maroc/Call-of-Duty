# Settings

> Last verified: 2026-08-11
> **Verified in play:** no. Compiled, gated, and covered by 7 EditMode tests.
> The values are proven to load, clamp, persist and reach the camera; whether
> the default sensitivity *feels* right is a tuning-card question.

## Overview

Mouse sensitivity, vertical FOV, invert-look, master volume, post-processing and
anti-aliasing: the things a player changes before they will judge anything else
about a shooter. All four
live on disk in the same versioned save as the run record, are bounded by a
ScriptableObject, and are held at runtime in a plain C# object that is never a
ScriptableObject.

**What this replaced.** `SaveData` already carried `mouseSensitivity` and
`masterVolume` before this system existed. Nothing read either one. They
serialised perfectly, survived a round trip, and changed nothing — `PlayerLook`
read `GameConfig.mouseSensitivity` instead, and no code anywhere touched volume.
Dead data that round-trips correctly is the failure mode a serialisation test
cannot catch, which is why [the tests](../../Assets/_Project/Tests/EditMode/SettingsTests.cs)
assert the *migration* and the *clamping*, not just the write.

## Data Assets

- **Settings.asset** (`Assets/_Project/Data/Game/Settings.asset`, type
  [SettingsConfig](../../Assets/_Project/Scripts/Core/SettingsConfig.cs)) — the
  **bounds and step sizes**, never the defaults. Built by
  `GreyBoxBuilder.ConfigureSettings`.
  - sensitivity `0.02 – 0.60`, step `0.01`. The ceiling is 5× the default, not
    the Inspector's 1.0: at 1.0 one mouse sweep is about nine full turns.
  - FOV `50 – 85` **vertical**, step `1`. Roughly 80–115 horizontal at 16:9.
    Below 50 the viewmodel eats the screen; above 85 the fisheye makes drone
    distance unreadable, and distance is how you survive a Rusher.
  - volume `0 – 1`, step `0.05`.
- **GameConfig.asset** supplies the **defaults** — `mouseSensitivity` and
  `baseFovVertical`. It is read on first launch and never written.

Why two assets: `GameConfig` holds what the designer picked, `SettingsConfig`
holds what the player is allowed to pick. Different lifetimes. Keeping the range
next to the default would invite a runtime write to a ScriptableObject, which
with Domain Reload off is permanent.

## Runtime Types

- [GameSettings](../../Assets/_Project/Scripts/Core/GameSettings.cs) — the live
  values. A **plain C# class**, deliberately not a ScriptableObject: a runtime
  write to an SO survives into the next Play session and rewrites the shipped
  defaults. Same reason `WaveScaling` and `StatSheet` exist. Owns all clamping,
  so a value has exactly one place it can go out of range. Exposes
  `SensitivityFraction` / `FovFraction` / `VolumeFraction` (0–1) so a UI can draw
  a bar without knowing the bounds.
- [SettingsHub](../../Assets/_Project/Scripts/Core/SettingsHub.cs) — the scene
  component. Loads the save, seeds defaults on first launch, applies
  `AudioListener.volume`, raises `Changed`, and writes back on `Persist()`.
  - Named `SettingsHub`, not `SettingsService`: `UnityEditor.SettingsService`
    exists, and the ambiguity breaks every editor script that touches it.
  - `Current` resolves lazily via `??=`, so **script execution order does not
    matter** — a consumer may read it before or after this component's `Awake`.
- [SaveData](../../Assets/_Project/Scripts/Core/SaveData.cs) schema **2** —
  adds `settingsInitialised`, `fovVertical`, `invertLook`, `lastMode`.

### The `settingsInitialised` flag

Every settings field defaults to **zero**, not to a playable value. A real
default is a tuning number, and tuning numbers live in a ScriptableObject. The
flag is how `SettingsHub` tells "the player chose silence" from "nobody has
chosen anything yet" — the second case seeds from `GameConfig` / `SettingsConfig`.

## Scenes & Prefabs

`GreyBoxBuilder` creates one **`Settings`** GameObject carrying `SettingsHub`,
wired to `Settings.asset` and `GameConfig.asset`. It is built **before the
player**, because `PlayerLook` serialises a reference to it and a serialized
reference cannot point at an object that does not exist yet.

`GreyBoxVerify` repairs and then re-checks `SettingsHub._bounds`,
`SettingsHub._defaults` and `PlayerLook._settings` across a save/reload round
trip. Without that last link the saved values are read, clamped, written — and
never reach the camera, which is precisely the old bug in a new place.

## Key Behaviors & Non-Obvious Patterns

- **One hub per scene, no singleton.** `DontDestroyOnLoad` would be a mutable
  static, which this project bans outright. Re-reading a 2 KB JSON file per scene
  load is the cheaper mistake, and it means a scene opened directly in the editor
  is fully configured.
- **`PlayerLook` caches, never polls.** It seeds `_sensitivity` / `_fovVertical`
  from `GameConfig` in `Awake`, then subscribes to `SettingsHub.Changed` *and
  pulls once* — the event only fires on a change, and the component may have
  woken up after the settings resolved. It unsubscribes in `OnDestroy`; a C#
  event keeps the publisher holding a reference to the subscriber.
- **Invert flips pitch only.** Inverting yaw as well is not a setting, it is a
  bug report.
- **`PlayerLook.SetCursorLocked` is the single owner of the cursor.** Pause and
  the menus call it rather than touching `Cursor` themselves.
- **`PlayerLook.BaseFov` now returns the live FOV**, so the weapon's ADS offset
  is computed against what the player actually set.

## The graphics rows (schema 3)

Two rows added when the image pipeline was turned on:
[rendering.md](rendering.md).

| Row | Values | Applied by |
| --- | --- | --- |
| POST-PROCESSING | ON / OFF | `CameraGraphics` → `UniversalAdditionalCameraData.renderPostProcessing` |
| ANTI-ALIASING | OFF / FXAA / SMAA | `CameraGraphics` → `UniversalAdditionalCameraData.antialiasing` |

**Why the camera and not MSAA.** MSAA lives on the
`UniversalRenderPipelineAsset`, which is a ScriptableObject. Domain Reload is off
here, so a runtime write to one survives into the next Play session and
permanently rewrites the shipped default — the same trap that produced
`WaveScaling`, `StatSheet` and this class itself. Camera state is scene state and
dies with the scene, so post-processing and post AA are the two knobs that can be
player-facing without paying that cost. MSAA stays off in the pipeline asset.

**Why `CoD.Player` hosts the applier.** `CameraGraphics` needs the render
pipeline assembly and `CoD.Core` must not have one — everything depends on Core.
`PlayerLook` and `CameraShake` already own camera concerns, so it sits beside
them. The main menu hosts one too; that scene has a camera and a `SettingsHub`,
which is all it needs.

**Defaults still live in the config.** `SettingsConfig.postProcessingDefault` and
`.antiAliasingDefault`, seeded into the save by `SettingsHub.Resolve` when
`graphicsInitialised` is false. The 2 → 3 migration writes nothing at all — see
[save.md](save.md).

**`AntiAliasingMode` is ours, not URP's.** Three values in `CoD.Core`, mapped to
`UnityEngine.Rendering.Universal.AntialiasingMode` inside `CameraGraphics`. It is
serialised as a number, so the order is a file format.

## Why no AudioMixer

Master volume drives `AudioListener.volume` — one global multiplier over every
`AudioSource`, which is the entire requirement while the game has one sound
category and no music.

An `AudioMixer` was considered and rejected: a `.mixer` is opaque asset YAML that
`GreyBoxBuilder` cannot generate (`AudioMixerController` is internal to
`UnityEditor.Audio`), and this project's rule is that nothing in a scene or its
assets is hand-authored. **Revisit the moment there is a second bus to balance —
music against SFX.** That is the first thing one line of global volume genuinely
cannot do.

## Related Systems

- [save.md](save.md) — the file these live in, and the v1 → v2 migration.
- [player.md](player.md) — `PlayerLook` is the only gameplay consumer.
- [ui.md](ui.md) — the pause and main-menu screens that edit these values.

## Gotchas

- A v1 save's settings block is **discarded, not migrated**. Nothing ever
  applied it, so there is no player choice to preserve — `settingsInitialised`
  is cleared and the block is re-seeded. The *record* (`bestRound`, `totalKills`,
  `totalRuns`) is preserved, and a test asserts exactly that.
- `SettingsConfig.OnValidate` pins a max below its min back up to the min. A
  reversed range silently pins every slider to one value and reads in the
  Inspector exactly like a working one.
- Setting a value does **not** apply it. Call `Apply()` (pushes + audio) or
  `ApplyAndPersist()`. The split exists so a menu can preview a value live and
  only pay the disk write when it closes.
