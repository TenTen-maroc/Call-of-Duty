# Build — producing a Windows player, and proving it runs

> Last verified: 2026-08-11
> **Verified:** yes, in the strongest sense available. Both a release and a
> development `.exe` have been produced and executed outside the editor; each
> booted, reached the menu, loaded the arena and logged zero errors.

## Overview

Until this system existed, nothing in the repo produced a `.exe`. Every gate ran
inside the editor, where editor-only code paths are live, `AssetDatabase` works,
and nothing is stripped. A build can pass all of that and still fail on its own.

Two pieces:

- [GameBuilder.cs](../../Assets/_Project/Scripts/Editor/GameBuilder.cs) — makes
  the executable. Menu items plus `-executeMethod` entry points.
- [BuildSmokeTest.cs](../../Assets/_Project/Scripts/Core/BuildSmokeTest.cs) — a
  component in `00_Boot` that is dormant unless the exe is launched with
  `-codSmokeTest`. It boots, waits for the menu, loads the arena, counts every
  error and exception, and quits with an exit code.
- [verify-build.mjs](../../Tools/verify-build.mjs) — the one command that does
  both and fails loudly.

## Commands

```bash
# The gate. Builds, runs the exe headlessly, greps the marker.
node Tools/verify-build.mjs           # release
node Tools/verify-build.mjs --dev     # development build

# The pieces, if you want them separately (Unity must be CLOSED — it locks the project):
Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.GameBuilder.BuildWindowsHeadless            -logFile Logs/player-build.log
Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.GameBuilder.BuildWindowsDevelopmentHeadless -logFile Logs/player-build.log
```

In the editor: **CoD → Build Windows Player** / **(Development)**.

Output goes to `Build/Windows/` and `Build/Windows-Dev/`. Both are gitignored and
covered by `guard-no-build-artifacts`.

| Build | Size | Cheat console |
| --- | --- | --- |
| Release | 93 MB | **not in the binary at all** — zero occurrences in the player log |
| Development | 132 MB | compiled in, disabled at runtime unless the mode is Sandbox |

## The scene list has exactly one owner

`GameBuilder` reads `EditorBuildSettings.scenes`, which
`GreyBoxBuilder.RegisterScenes` writes. There is no second list to keep in sync.

The build **refuses** if scene 0 is not `00_Boot.unity`. Index 0 is what a player
loads on launch; getting it wrong ships a game that opens mid-run with no menu,
and nothing in the editor would ever show it.

## The smoke test

Launched as `CallOfDuty.exe -codSmokeTest -batchmode -nographics -logFile <path>`.

1. `Awake` checks the command line. Without the flag it calls `Destroy(this)` —
   a real player carries a component that removes itself on the first frame.
2. With the flag it marks itself `DontDestroyOnLoad` (one persistent object, not
   a static — the no-mutable-statics rule is untouched) and subscribes to
   `Application.logMessageReceived`.
3. Waits for `20_MainMenu`, loads `10_GreyBox`, plays 6 real-time seconds.
4. Logs `COD_SMOKE_OK` and quits 0, or `COD_SMOKE_FAIL` and quits 1 if any
   `Error`, `Exception` or `Assert` was logged along the way.

Warnings do not count — a player build legitimately warns about things the
editor does not.

**It is not compiled out of release builds**, deliberately. Gating it behind
`DEVELOPMENT_BUILD` would mean the release binary — the one that actually ships —
could never be verified, which defeats the purpose. It is inert and tiny.

Timings are `const` in the component rather than ScriptableObject fields. They
are harness timeouts, not game tuning; no balance decision reads them, and a CI
timeout living beside drone health would make that asset harder to reason about.

## What this caught

The first run found a bug no editor gate could see. `RunContext` and
`SettingsHub` each loaded their **own** `SaveData`. Both write the whole file, so
ending a run reverted the entire settings block to un-chosen — change your
sensitivity, die, and it is gone. It was visible only by reading the save file a
built player produced. `RunContext.Save` now delegates to the `SettingsHub` when
one is wired, `GreyBoxVerify` checks that link, and two PlayMode tests cover it.

## Player settings applied by the build

Set in code, not clicked once in an Inspector, so a fresh clone builds the same
executable:

- **1920×1080** default resolution. Unity's default is 1024×768, which is 4:3 —
  and every FOV number in this project is tuned for 16:9.
- **Borderless fullscreen** (`FullScreenWindow`). Alt-tabs cleanly, which
  exclusive fullscreen does not, and this game now has a pause menu people will
  alt-tab out of.
- Application identifier `ma.tenten.callofduty`.

**Scripting backend is deliberately left at Mono.** IL2CPP is the better shipping
answer — faster, harder to decompile — but it needs the Windows IL2CPP module
installed and turns a 40-second build into several minutes. One line to change
when there is something to ship rather than something to test.

## Related Systems

- [menus.md](menus.md) — the scenes the smoke test walks through.
- [settings.md](settings.md) — the save file the smoke test exposed a bug in.

## Gotchas

- **Unity locks the project.** Close the editor before any headless build or the
  run fails with a lock error. `verify-build.mjs` cannot do this for you.
- `verify-build.mjs` **deletes the output directory first**. A stale executable
  that failed to rebuild would otherwise be smoke-tested happily, and the gate
  would pass on last week's binary.
- The marker strings are duplicated between `BuildSmokeTest.cs` and
  `verify-build.mjs` — a Node script cannot read a C# `const`. A mismatch fails
  the gate rather than silently passing it, because a missing marker is a
  failure.
- `Build/Windows-Dev/` contains a `..._BurstDebugInformation_DoNotShip` folder.
  The name is the instruction; it is gitignored with the rest of `Build/`.
