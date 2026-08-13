# System docs

One code-verified markdown map per subsystem. Read the relevant file **before**
answering questions about or modifying a subsystem; update it **in the same task**
that changes it, including the `Last verified:` date.

Accuracy over completeness — document only what is actually in the code. Skip the
obvious, emphasise the non-obvious.

## Index

| Doc | Covers | Status |
| --- | --- | --- |
| [weapons.md](weapons.md) | `WeaponConfig`, `WeaponRuntime`, firing, recoil, spread, damage + falloff, effect modules | ✅ |
| [player.md](player.md) | Input, movement, look, camera shake, the rig layout | ✅ |
| [pooling.md](pooling.md) | The object pool every spawn goes through | ✅ |
| [ui.md](ui.md) | Crosshair, hitmarker, HUD, damage feedback, wave/shop/game-over panels, cheat console | ✅ |
| [drones.md](drones.md) | `DroneConfig`, `AttackModule`, NavMesh pathing, attack tokens | ✅ all three archetypes |
| [waves.md](waves.md) | `WaveConfig`, `WaveRunner`, `DifficultyConfig` caps and the endless ramp | ✅ |
| [shop.md](shop.md) | `ShopConfig`, `ShopItemConfig`, `PassiveConfig`, `StatSheet` rebuild | ✅ passives + modules |
| [save.md](save.md) | Versioned JSON, `schemaVersion` migration, atomic write, `.bak` | ✅ |
| [arena.md](arena.md) | The three-lane arena, cover heights, navmesh bake | ✅ |
| [settings.md](settings.md) | Sensitivity, FOV, invert, volume, post-processing, anti-aliasing — bounds, the runtime layer, schema 3 | ✅ |
| [rendering.md](rendering.md) | Post-processing stack, arena lighting, surface response, the generated detail normal | ✅ |
| [menus.md](menus.md) | Main menu, pause, the shared settings page, Run vs Sandbox | ✅ |
| [build.md](build.md) | Producing the Windows .exe, and the smoke test that proves it runs | ✅ |
| [campaign.md](campaign.md) | Missions, objectives, the director seam, and why campaign is a save AXIS rather than a third `GameMode` | ⚠️ under construction |
| [performance.md](performance.md) | The two caps under load, the allocation budget, what headless cannot prove | ✅ |
| [audio.md](audio.md) | The hand-authored mixer, its buses and its gate, footsteps and ambience — and why there is still no sound | ⚠️ no clips |

## Automated verification

Beyond `typecheck.mjs` and the guards, the project has 237 tests (177 EditMode,
60 PlayMode):

```
Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode -testResults Logs/tests-editmode.xml
Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode -testResults Logs/tests-playmode.xml
node Tools/verify-build.mjs     # builds a real .exe and proves it boots
```

EditMode covers the maths that fails silently — stat folding, save round-trip and
corruption recovery, the v1→v2 migration, settings clamping, follow-up bounds,
damage falloff, wave scaling, shop rules, and both weapons' TTK. PlayMode loads
the real grey box AND the real menu scene: drones spawn, path, close distance,
die, pay out, and hand the run to the shop with the pool recycling, and pause
stops the clock, blocks input and gives the clock back exactly as it found it. See
[AUTOPILOT-PLAN.md](../AUTOPILOT-PLAN.md) for what these can and cannot prove.

Every doc here describes code that now **runs**. Each states at the top what
has actually been verified in play and what has only been compiled — those are
different claims and the docs keep them apart.

## Template

Each file follows: Overview → Data Assets → Runtime Types → Scenes & Prefabs →
Key Behaviors & Non-Obvious Patterns → Related Systems → Gotchas. Use markdown
links for code references, cite line numbers where useful, keep each file under
~500 lines.

Design intent for the systems not yet built lives in
[../DATA-MODEL-SKETCH.md](../DATA-MODEL-SKETCH.md) and [../../CLAUDE.md](../../CLAUDE.md).
