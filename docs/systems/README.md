# System docs

One code-verified markdown map per subsystem. Read the relevant file **before**
answering questions about or modifying a subsystem; update it **in the same task**
that changes it, including the `Last verified:` date.

Accuracy over completeness — document only what is actually in the code. Skip the
obvious, emphasise the non-obvious.

## Index

Nothing yet — the codebase is at Phase 0 and no subsystem exists. These land as
they are built:

| Doc | Covers | Arrives with |
| --- | --- | --- |
| `weapons.md` | `WeaponConfig`, `WeaponRuntime`, firing, recoil, spread, damage + falloff, `EffectModule` stacking | Phase 3 (grey box), extended at Wild Arsenal |
| `drones.md` | `DroneConfig`, `AttackModule`, NavMesh pathing, attack tokens, pooling | Rusher milestone |
| `waves.md` | `WaveConfig`, `WaveRunner`, `DifficultyConfig` caps and the endless ramp | Waves milestone |
| `shop.md` | `ShopConfig`, `ShopItemConfig`, `PassiveConfig`, `StatSheet` rebuild pipeline | Shop milestone |
| `save.md` | Versioned JSON, `schemaVersion` migration, atomic temp-file write, `.bak` | Waves milestone (best-round record) |
| `pooling.md` | The object pool every spawn goes through | Phase 3 (grey box) |

## Template

Each file follows: Overview → Data Assets → Runtime Types → Scenes & Prefabs →
Key Behaviors & Non-Obvious Patterns → Related Systems → Gotchas. Use markdown
links for code references, cite line numbers where useful, keep each file under
~500 lines.

Until these exist, the design intent lives in
[../DATA-MODEL-SKETCH.md](../DATA-MODEL-SKETCH.md) and [../../CLAUDE.md](../../CLAUDE.md).
