# Save

> Last verified: 2026-08-11 — compiles clean. **Not yet verified in play:** an
> actual write to disk, the `.bak` appearing on the second run, and recovery from
> a deliberately corrupted file.

## Overview

One versioned JSON file holding what outlives a run: best round, lifetime kills
and runs, and settings. Notice what is **not** in it — the run itself. Permadeath
means money, wave and passives are never serialised, and that single design
decision is why this system is one page instead of a migration problem.

## Shape

```json
{
  "schemaVersion": 1,
  "bestRound": 0,
  "totalKills": 0,
  "totalRuns": 0,
  "sandboxUnlocked": true,
  "mouseSensitivity": 0.12,
  "masterVolume": 1.0
}
```

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
  `Awake`, and `RecordRunEnded` is the only thing that writes.

## Key Behaviors & Non-Obvious Patterns

- **The write is atomic.** Content goes to `cod_save.tmp.json`, then
  `File.Replace` swaps it into place and demotes the old file to `.bak` in one
  operation. A direct write interrupted by a crash leaves an unparseable file and
  the record is gone — a failure that is silent, permanent, and only ever hits
  players who already had something worth losing.
- **`Load` never returns null.** Missing file → defaults. Corrupt file → the
  backup. Corrupt backup → defaults. The game always starts.
- **A save from the future is left alone.** If `schemaVersion` is higher than this
  build's, the loader warns and returns the data untouched rather than guessing:
  records may be incomplete, but they are never rewritten wrongly.
- `RecordRunEnded` is called by the WaveRunner on player death, before the
  game-over panel reads the record — so a new best shows the run that just set it.

## Related Systems

- [waves.md](waves.md) — permadeath is what triggers the only write.
- [ui.md](ui.md) — the game-over panel is the only place the record is displayed.

## Gotchas

- Adding a field is free; **changing or removing one is a migration**. Bump
  `CurrentSchemaVersion` and add a step in `Migrate`, which reads the version
  first for exactly that reason.
- Settings live in the same file on purpose. A second file is a second thing to
  keep versioned, and they fail the same way.
- Nothing in a run is saved. If a "continue run" feature is ever wanted, that is
  a new file and a new decision — do not quietly widen this one.
