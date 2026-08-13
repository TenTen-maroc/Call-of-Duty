# Save

> Last verified: 2026-08-13 — schema 6 adds violence preferences.
> Atomic write/backup recovery, migrations, mission records, accessibility, violence
> round-trip and refusal to overwrite a newer schema are covered by tests.

## Overview

One versioned JSON file holding what outlives a run: best round, lifetime kills
and runs, settings, and per-mission campaign results. Notice what is **not** in
it — the run itself. Permadeath means money, wave and passives are never
serialised, and that single design decision is why this system is one page
instead of a migration problem.

## Shape

```json
{
  "schemaVersion": 6,
  "bestRound": 0,
  "totalKills": 0,
  "totalRuns": 0,
  "sandboxUnlocked": true,
  "lastMode": 0,
  "settingsInitialised": true,
  "mouseSensitivity": 0.12,
  "fovVertical": 62.0,
  "masterVolume": 1.0,
  "invertLook": false,
  "graphicsInitialised": true,
  "postProcessing": true,
  "antiAliasing": 2,
  "accessibilityInitialised": true,
  "subtitlesEnabled": true,
  "subtitleSize": 1,
  "campaignSelected": false,
  "selectedMissionId": "",
  "missionRecords": [
    {
      "missionId": "mission_01_cold_open",
      "completed": true,
      "bestRating": 3,
      "bestTimeSeconds": 214.5,
      "deaths": 2
    }
  ]
}
```

`antiAliasing` is `0` Off, `1` FXAA, `2` SMAA. The enum's ORDER is a file format:
append new modes, never reorder the existing ones.

`subtitleSize` is `0` Small, `1` Medium, `2` Large. Its enum order is likewise
append-only. `accessibilityInitialised` distinguishes a player's deliberate OFF
choice from a pre-v5 save that needs current defaults seeded from `SettingsConfig`.

`lastMode` is `0` for Run and `1` for Sandbox. It is how the menu's mode choice
reaches the next scene without a mutable static — Domain Reload is off, so a
static would survive into the following Play session and start a Run in Sandbox.

Written to `Application.persistentDataPath`:
`cod_save.json`, with `cod_save.bak.json` alongside it.

## The second axis — read this before touching `GameMode`

**`lastMode` has exactly two values and always will. `campaignSelected` is a
separate bool.** Mode means *rules*; the bool means *content*.

| | Endless (`campaignSelected: false`) | Campaign (`campaignSelected: true`) |
| --- | --- | --- |
| **Run** (`lastMode: 0`) | permadeath, writes `bestRound` | missions, writes `missionRecords` only |
| **Sandbox** (`lastMode: 1`) | cheat console, writes nothing | missions with everything unlocked |

⚠️ **Never add `GameMode.Campaign = 2`.** `JsonUtility` writes the enum as a raw
int and **C# enums are not range-checked**, so an already-shipped build reading
`lastMode: 2` gets "not Sandbox" at all six sites that branch that way, treats a
campaign mission as a Run, and `RunContext.RecordRunEnded` writes the mission's
wave number into `bestRound`. The permadeath record would be polluted by a build
that can no longer be patched — which is exactly the harm `Save`'s future-version
refusal below exists to prevent, self-inflicted.

As a bool, that same old build reads `lastMode: Run`, ignores three keys it has
never heard of, and starts a normal endless run. Safe degradation on the side you
cannot fix.

`missionRecords` is keyed by the mission's `stableId`, never by index — a record
found by position dies the first time a mission is inserted, renamed or cut, and
the player's whole history silently shifts one mission to the left. Same reason
drones, passives and shop entries all carry one.

There is **no `campaignInitialised` flag**, and the absence is a decision. That
pattern exists for `settingsInitialised` and `graphicsInitialised` because their
real defaults are TUNING NUMBERS that have to come from a ScriptableObject — a
sensitivity of `0` is not "the player chose nothing", it is a dead mouse. Nothing
in the campaign block works that way: `false` means the menu has never been
pointed at the campaign, `""` means no mission chosen, `[]` means no mission
played. The zero value **is** the correct answer, so a flag would only be a
second thing to keep in sync.

## Schema history

| Version | Added | Migration |
| --- | --- | --- |
| 1 → 2 | the settings block (fov, invert, the initialised flag) and the remembered mode | Clears `settingsInitialised`. v1 wrote `mouseSensitivity` and `masterVolume` that nothing ever read, so there was no player choice to preserve. |
| 2 → 3 | the graphics block (`graphicsInitialised`, `postProcessing`, `antiAliasing`) | **Deliberately a no-op.** `graphicsInitialised` defaults to false, which is exactly what makes `SettingsHub` seed the block from `SettingsConfig` on the next resolve — the same path a brand-new save takes. Writing real values in `Migrate` would put a tuning number in a migration, and tuning numbers live in ScriptableObjects. |
| 3 → 4 | the campaign block (`campaignSelected`, `selectedMissionId`, `missionRecords[]`) | **Deliberately a no-op, for a different reason than 2 → 3.** That one is empty because a real default would be a tuning number; this one is empty because the block has no real default at all. An old save **is** an endless save and the zero values already say so, which is also why there is no `campaignInitialised` to clear. The one thing a v3 file can hand over is a **null** `missionRecords`, and that is fixed in `Normalise` on the load path rather than in this step — see below. |
| 4 → 5 | the accessibility block (`accessibilityInitialised`, `subtitlesEnabled`, `subtitleSize`) | **Deliberately a no-op.** The false initialised flag makes `SettingsHub` seed current values from `SettingsConfig`; migration code never owns player-facing tuning defaults. |

The version is bumped even when the migration does nothing, because the number is
what tells a **downgraded** build that this file holds fields it cannot represent.
`Save` refuses to overwrite a file from the future for exactly that reason.

## Runtime Types

- **[SaveData.cs](../../Assets/_Project/Scripts/Core/SaveData.cs)** — the shape,
  plus `MissionRecord`. `[Serializable]` with public fields, because
  `JsonUtility` ignores properties and anything private without
  `[SerializeField]`. `MissionRecord` is a nested `[Serializable]` **class** and
  not a `Dictionary<string, MissionRecord>` for that same reason: `JsonUtility`
  serialises nested classes and writes **nothing at all** for a Dictionary, so
  the obvious shape would have looked like it worked right up until the first
  reload.
- **[SaveSystem.cs](../../Assets/_Project/Scripts/Core/SaveSystem.cs)** — `Load`,
  `Save`, `Migrate`, and `Normalise`. A static class of methods only; deliberately
  holds no cached instance, because Domain Reload is off and a static cache would
  carry the previous Play session's record into this one.
- **[RunContext.cs](../../Assets/_Project/Scripts/Core/RunContext.cs)** — loads on
  `Awake`; `RecordRunEnded` writes the run record.
- **[SettingsHub.cs](../../Assets/_Project/Scripts/Core/SettingsHub.cs)** — the
  other two writers: `Persist` (the settings block, on closing the settings page)
  and `SetLastMode` (the menu's mode choice).

**Three writers, one file, one object.** RunContext takes its `SaveData` from the
SettingsHub when the scene has one, because two independently loaded instances
each write the WHOLE file and the last one silently reverts the other half. That
bug shipped once: change your sensitivity, die, and the settings came back
un-chosen. `RunAndSettings_ShareOneSaveObject` pins it.

## Key Behaviors & Non-Obvious Patterns

- **The write is atomic.** Content goes to `cod_save.tmp.json`, then
  `File.Replace` swaps it into place and demotes the old file to `.bak` in one
  operation. A direct write interrupted by a crash leaves an unparseable file and
  the record is gone — a failure that is silent, permanent, and only ever hits
  players who already had something worth losing.
- **`Load` never returns null.** Missing file → defaults. Corrupt file → the
  backup. Corrupt backup → defaults. The game always starts.
- **`Normalise` runs on every load, outside `Migrate`.** `#nullable enable` checks
  the code, not the deserialiser: `JsonUtility` assigns the keys the JSON actually
  names and leaves the rest alone, so a file from before schema 4 hands back a
  **null** `missionRecords` through a field declared non-null, and the first thing
  to iterate it throws on load. It sits outside `Migrate` on purpose — a save from
  the future returns from `Migrate` before any migration step runs and can be just
  as null, so "must not be null" belongs to the field, not to a version. It also
  repairs a null array *slot* and a null `missionId`, because a save file is the
  one input in this game a player can open in Notepad.
- **A save from the future is left alone — on read AND on write.** If
  `schemaVersion` is higher than this build's, `Load` returns the data untouched
  rather than guessing, and `Save` **refuses outright**. Writing would have been
  the worse half: `JsonUtility` serialises only the fields this build knows, so a
  downgraded build would drop every newer field and relabel the result as current,
  making the loss undetectable to the newer build that came back for it. The
  refusal logs at `Error`, not `Warn` — `GameLog.Warn` is compiled out of a
  shipping build, and a save path that silently does nothing forever is exactly
  what the guard exists to prevent.
- `RecordRunEnded` is called by the WaveRunner on player death, and by both pause
  menu exits, before the game-over panel reads the record.
- **`RunContext.SetANewRecord` is what the death screen reads**, not a comparison
  against `bestRound`. By the time the panel draws, `bestRound` has already been
  raised, so `RoundReached >= bestRound` was true for every run that merely tied —
  and true in Sandbox, where `RecordRunEnded` deliberately writes nothing at all
  and there was no record to have beaten.
- **Sandbox never writes.** Infinite money and a cheat console mean a record set
  there is not a record, and one accidental session would overwrite a real best
  round permanently.

## Related Systems

- [waves.md](waves.md) — permadeath is what triggers the only write.
- [ui.md](ui.md) — the game-over panel is the only place the record is displayed.
- [campaign.md](campaign.md) — owns the campaign block and writes mission results.
- [settings.md](settings.md) — owns the schema 5 accessibility values.

## Testing this system

PlayMode fixtures that load the arena run the REAL game against the REAL save
path, so every one of them composes
[SaveFileGuard](../../Assets/_Project/Tests/PlayMode/SaveFileGuard.cs) and calls
`CaptureAndReset()` — capture alone was not enough. `bestRound` is raise-only and
`RecordRunEnded` writes nothing in Sandbox, so a fixture that read whatever the
tester last played passed on one machine and failed on the next for reasons
nothing in the test mentioned. `CaptureAndReset` puts a known save in place and
`Restore` puts the player's own back, byte for byte.

## Gotchas

- Adding a field is free; **changing or removing one is a migration**. Bump
  `CurrentSchemaVersion` and add a step in `Migrate`, which reads the version
  first for exactly that reason.
- **A new `SaveData` field means a new line in
  [SaveFileGuard.CaptureAndReset](../../Assets/_Project/Tests/PlayMode/SaveFileGuard.cs).**
  That literal is hand-listed rather than `new SaveData()` so adding a field is a
  decision someone makes. Forget, and the field defaults to zero in every PlayMode
  fixture — a test then asserts on a value the code under test never had to write,
  and passes. Both bugs in that file's header were exactly that shape.
- **Never widen `GameMode`.** Anything that looks like a third mode is a second
  axis; see the table near the top. A `lastMode: 2` reaching a shipped build is
  unrecoverable, because the build that misreads it is the one you cannot patch.
- Settings live in the same file on purpose. A second file is a second thing to
  keep versioned, and they fail the same way.
- Nothing in a run is saved. If a "continue run" feature is ever wanted, that is
  a new file and a new decision — do not quietly widen this one.
