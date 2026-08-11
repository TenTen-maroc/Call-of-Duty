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
| [settings.md](settings.md) | Sensitivity, FOV, invert, volume — bounds, the runtime layer, schema 2 | ✅ |

## Automated verification

Beyond `typecheck.mjs` and the guards, the project has 49 tests:

```
Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode -testResults Logs/tests-editmode.xml
Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode -testResults Logs/tests-playmode.xml
```

EditMode covers the maths that fails silently — stat folding, save round-trip and
corruption recovery, follow-up bounds, damage falloff, wave scaling, shop rules,
and both weapons' TTK. PlayMode loads the real grey box and asserts the loop
runs: drones spawn, path, close distance, die, pay out, and hand the run to the
shop, with the pool recycling instances. See
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
