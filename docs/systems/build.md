# Build — producing a Windows player, proving it runs, and looking at it

> Last verified: 2026-08-12
> **Verified — the gate:** yes, in the strongest sense available. Both a release
> and a development `.exe` have been produced and executed outside the editor;
> each booted, reached the menu, loaded the arena and logged zero errors.
> **Verified — the screenshot route:** NO. It compiles clean across all nine
> assemblies and every guard passes, but no player containing it has been built
> or run. The `.exe` currently in `Build/Windows-Dev/` predates it and ignores
> the flag. The first person to run `node Tools/screenshot.mjs` is testing it.

## Overview

Until this system existed, nothing in the repo produced a `.exe`. Every gate ran
inside the editor, where editor-only code paths are live, `AssetDatabase` works,
and nothing is stripped. A build can pass all of that and still fail on its own.

Four pieces:

- [GameBuilder.cs](../../Assets/_Project/Scripts/Editor/GameBuilder.cs) — makes
  the executable. Menu items plus `-executeMethod` entry points.
- [BuildSmokeTest.cs](../../Assets/_Project/Scripts/Core/BuildSmokeTest.cs) — a
  component in `00_Boot` that is dormant unless the exe is launched with
  `-codSmokeTest` or `-codScreenshots`. The first route boots, waits for the
  menu, loads the arena, counts every error and exception, and quits with an
  exit code. The second walks the same itinerary with a window open and writes a
  PNG at each beat.
- [verify-build.mjs](../../Tools/verify-build.mjs) — the gate. Builds, runs the
  exe blind, and fails loudly.
- [screenshot.mjs](../../Tools/screenshot.mjs) — the eye. Builds, runs the exe
  **with graphics**, and leaves PNGs on disk.

## Commands

```bash
# The gate. Builds, runs the exe headlessly, greps the marker.
node Tools/verify-build.mjs           # release
node Tools/verify-build.mjs --dev     # development build

# The eye. Builds the DEVELOPMENT player, runs it in a 1600x900 window,
# writes four PNGs per pass to Logs/Screenshots/.
node Tools/screenshot.mjs                    # build, then the endless + campaign passes
node Tools/screenshot.mjs --reuse            # skip the build, use Build/Windows-Dev as it stands
node Tools/screenshot.mjs --no-campaign      # endless pass only
node Tools/screenshot.mjs --mission mission_02_hardcontact

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

The **screenshot route is the opposite**, and the asymmetry is the point. It is
wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, exactly like the cheat
console, down to the `Route.Screenshots` enum member itself. A shipped game has
no business carrying a capture harness, and the table above claims the release
binary contains none of it.

Timings are `const` in the component rather than ScriptableObject fields. They
are harness timeouts, not game tuning; no balance decision reads them, and a CI
timeout living beside drone health would make that asset harder to reason about.

## Looking at the game — the screenshot route

`node Tools/screenshot.mjs` is the only thing in this repo that can answer *what
does it look like* without a human opening Unity and pressing Play.

Launched as
`CallOfDuty.exe -codScreenshots -codShotDirectory <abs> -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile <path>`
— note what is **absent**: no `-batchmode`, no `-nographics`. A real window with
a real swap chain is the entire requirement.

Four frames per pass, and the script runs two passes:

| Frame | When | What it is for |
| --- | --- | --- |
| `01-main-menu.png` | menu active + 5 frames | the front door, the record line, the row list |
| `02-arena-loaded.png` | arena active + 5 frames | first drawn frame of the arena — lighting, palette, viewmodel |
| `03-arena-hud.png` | +2.5 s | the HUD with something to say. In the campaign pass this is the **objective list** |
| `04-arena-wave.png` | +7 s | drones in the air. The runner's countdown is ~4 s, so this lands mid-wave |

Pass `endless` runs the normal loop. Pass `campaign` runs mission 1, whose
`stableId` the script reads out of
[Missions.asset](../../Assets/_Project/Data/Missions/Missions.asset) rather than
hard-coding — a hard-coded id that stopped matching would make `MissionDirector`
fall back to the endless loop and the pass would photograph the wrong HUD while
claiming to be the campaign.

PNGs go to `Logs/Screenshots/{endless,campaign}/`. That folder was chosen because
`Logs/` is **already** gitignored and **already** covered by
`guard-no-build-artifacts`; a new top-level folder would need a new ignore rule,
and the first person to forget it commits a megabyte of screenshots through LFS.

### What it proves, and what it does not

It proves the game **renders**, and it shows exactly **what** it renders. A frame
that comes back black, or with the objective list off the bottom of the screen,
is a real defect caught by a machine rather than by someone noticing a week
later. It also counts errors and exceptions the way the smoke test does, so it
doubles as the first gate that has ever exercised the rendering path in a player
at all.

It proves **nothing about frame rate**. One machine's timing in a scripted run,
with the harness's own capture stalls in it, is not the target laptop's frame
time under a real wave. Item 9 on the tuning card is still a human with a 3050,
and no screenshot will ever retire it.

### How the campaign frame is reached

There is no synthetic keypress anywhere in this. `CoD.Core` references nothing —
not `CoD.UI`, so `MissionSelectPanel` is not a type `BuildSmokeTest` can name,
and not the Input System, so there is no way to press ENTER on the CAMPAIGN row
from inside the harness.

Instead it writes the two save axes the mission-select screen writes —
`campaignSelected`, `selectedMissionId`, plus `lastMode: Run` — before `00_Boot`
hands off to the menu. The save file is the only sanctioned channel between a
menu and the scene it loads (see [settings.md](settings.md) and
`SettingsHub.SetCampaign`), so this is a substitute `MissionDirector` genuinely
cannot tell apart. **The previous values are put back** when the route finishes:
a tool for looking at the game does not get to change it, and leaving
`campaignSelected` true would open the player's next real launch on a mission
they never chose.

### Why `CaptureScreenshotAsTexture`, not `CaptureScreenshot`

`ScreenCapture.CaptureScreenshot(path)` is fire-and-forget — it hands the encode
and the write to the end of the frame and returns immediately, so quitting on the
next line leaves a zero-byte file, or no file at all, and nothing anywhere reports
it. The route instead waits for `WaitForEndOfFrame`, takes the pixels, encodes and
writes them itself, and logs `COD_SHOT <bytes> <absolute path>`. The byte count is
proof a frame exists rather than a hope that one will, and the path is absolute
because both `ScreenCapture` and `File` resolve a relative one against the
process working directory — which for a launched player is wherever the caller
happened to be standing.

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

- [menus.md](menus.md) — the scenes both routes walk through.
- [settings.md](settings.md) — the save file the smoke test exposed a bug in.
- [campaign.md](campaign.md) — the mission layer, and the two save axes the
  screenshot route borrows and puts back.
- [rendering.md](rendering.md) — what the screenshot route is photographing.
  Until it existed, none of that had ever been seen by anything automated.

## Gotchas

- **Unity locks the project.** Close the editor before any headless build or the
  run fails with a lock error. `verify-build.mjs` cannot do this for you, and
  neither can `screenshot.mjs`.
- `verify-build.mjs` **deletes the output directory first**. A stale executable
  that failed to rebuild would otherwise be smoke-tested happily, and the gate
  would pass on last week's binary. `screenshot.mjs` does the same, and it
  matters more there: a stale *gate* fails loudly, a stale *screenshot* looks
  perfect.
- **Every screenshot carries Unity's "Development Build" watermark**, bottom
  right. There is no API to turn it off. It is the price of the harness being
  stripped from release, and it is the right trade — but do not file it as a
  rendering bug.
- **`--reuse` is the one way `screenshot.mjs` can show you a game that is not the
  game in your working tree.** It prints the binary's build time for exactly that
  reason. A player built before the capture harness existed does not recognise
  `-codScreenshots` at all: it just starts the game and plays it until the
  three-minute timeout kills it, with nothing in the log, so the script says so
  explicitly when no frame and no acknowledgement come back.
- **The screenshot route needs a usable display.** `WaitForEndOfFrame` never
  fires when nothing is drawn, so this cannot run on a headless box — which is
  the whole reason `verify-build.mjs` still uses `-nographics` and stays the
  gate. `screenshot.mjs` is a tool you run, not a gate that runs you.
- The route sets `Application.runInBackground = true` for itself.
  `PlayerSettings.runInBackground` is off deliberately, and a windowed player
  that loses focus stops ticking — alt-tab away mid-capture and the coroutine
  would freeze with two frames written and no explanation.
- The marker strings are duplicated between `BuildSmokeTest.cs` and
  `verify-build.mjs` — a Node script cannot read a C# `const`. A mismatch fails
  the gate rather than silently passing it, because a missing marker is a
  failure.
- `Build/Windows-Dev/` contains a `..._BurstDebugInformation_DoNotShip` folder.
  The name is the instruction; it is gitignored with the rest of `Build/`.
- **A no-op scene rebuild still shows a huge diff.** Unity assigns fresh local
  fileIDs every time `GreyBoxBuilder` regenerates a scene, so re-running it
  produces thousands of changed lines with *equal* insertions and deletions and
  nothing semantically different. Compare the counts before believing it, and
  drop pure churn with `git checkout -- Assets/_Project/Scenes/`.
  `GreyBoxVerify.VerifyHeadless` checks the committed scenes without
  regenerating them, which is the right tool when you only want proof.
