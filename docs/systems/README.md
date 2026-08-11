# System docs

One code-verified markdown map per subsystem. Read the relevant file **before**
answering questions about or modifying a subsystem; update it **in the same task**
that changes it, including the `Last verified:` date.

Accuracy over completeness — document only what is actually in the code. Skip the
obvious, emphasise the non-obvious.

## Index

| Doc | Covers | Status |
| --- | --- | --- |
| [weapons.md](weapons.md) | `WeaponConfig`, `WeaponRuntime`, firing, recoil, spread, damage + falloff | ✅ Phase 3 |
| [player.md](player.md) | Input, movement, look, camera shake, the rig layout | ✅ Phase 3 |
| [pooling.md](pooling.md) | The object pool every spawn goes through | ✅ Phase 3 |
| [ui.md](ui.md) | Crosshair, hitmarker, HUD, cheat console | ✅ Phase 3 |
| [drones.md](drones.md) | `DroneConfig`, `AttackModule`, NavMesh pathing, attack tokens | ✅ Rusher |
| [waves.md](waves.md) | `WaveConfig`, `WaveRunner`, `DifficultyConfig` caps and the endless ramp | Waves |
| [shop.md](shop.md) | `ShopConfig`, `ShopItemConfig`, `PassiveConfig`, `StatSheet` rebuild | Shop |
| [save.md](save.md) | Versioned JSON, `schemaVersion` migration, atomic write, `.bak` | Save |

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
