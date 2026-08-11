# Save

> Last verified: 2026-08-11 — code-verified after the full-codebase audit.
> The write, the `.bak` on the second run, recovery from a deliberately corrupted
> file, and the refusal to overwrite a newer schema are all covered by EditMode
> tests. **Still unverified in play:** nothing in this system.

## Overview

One versioned JSON file holding what outlives a run: best round, lifetime kills
and runs, and settings. Notice what is **not** in it — the run itself. Permadeath
means money, wave and passives are never serialised, and that single design
decision is why this system is one page instead of a migration problem.

## Shape

```json
{
  "schemaVersion": 2,
  "bestRound": 0,
  "totalKills": 0,
  "totalRuns": 0,
  "sandboxUnlocked": true,
  "lastMode": 0,
  "settingsInitialised": true,
  "mouseSensitivity": 0.12,
  "fovVertical": 62.0,
  "masterVolume": 1.0,
  "invertLook": false
}
```

`lastMode` is `0` for Run and `1` for Sandbox. It is how the menu's mode choice
reaches the next scene without a mutable static — Domain Reload is off, so a
static would survive into the following Play session and start a Run in Sandbox.

Written to `Application.persistentDataPath`:
`cod_save.json`, with `cod_save.bak.json` alongside it.

## Runtime Types

- **[SaveData.cs](../../Assets/_Project/Scripts/Core/SaveData.cs)** — the shape.
  `[Serializable]` with public fields, because `JsonUtility` ignores properties
  and anything private without `[SerializeField]`.
- **[SaveSystem.cs](../../Assets/_Project/Scripts/Core/SaveSystem.cs)** — `Load`,
  `Save`, and `Migrate`. A static class of methods only; deliberately holds no
  cached instance, because Domain Reload is off and a static cache would carry the
  previous Play session's record into this one.
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
- Settings live in the same file on purpose. A second file is a second thing to
  keep versioned, and they fail the same way.
- Nothing in a run is saved. If a "continue run" feature is ever wanted, that is
  a new file and a new decision — do not quietly widen this one.
