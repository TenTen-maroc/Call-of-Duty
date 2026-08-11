# Shop and passives

> Last verified: 2026-08-11 — compiles clean, builds headlessly, references
> proven by GreyBoxVerify. **Not yet verified in play:** whether four offers is
> the right number, and whether wave-3 money makes the first break a real
> decision.

## Overview

Between waves the shop offers four items drawn from a weighted pool. Buying a
passive appends it to the run's owned list and **rebuilds the StatSheet from
scratch** — the pipeline is `(base + Σ flatAdd) × Π mult`, and nothing anywhere
writes a modified number back into a config asset.

## Data Assets

- **[Shop.asset](../../Assets/_Project/Data/Game/Shop.asset)** — economy and pool
  in one asset, because neither can be balanced without the other. Starting money
  300, four offers per break, reroll 50 growing ×1.5 within a break, prices scaled
  by wave.
- **[Data/Shop](../../Assets/_Project/Data/Shop)** — five `ShopItemConfig` assets,
  one per passive.
- **[Data/Passives](../../Assets/_Project/Data/Passives)** — Reinforced Plating
  (+25 max HP, ×4), Servo Legs (+10% move, ×3), Quick Hands (+25% reload, ×3),
  Hollow Points (+15% damage, ×5), Scrap Magnet (+25% money, ×3).

## Runtime Types

- **[ShopService.cs](../../Assets/_Project/Scripts/Waves/ShopService.cs)** — the
  weighted draw, reroll, and purchase. Plain C#, owned by the WaveRunner.
- **[ShopItemConfig.cs](../../Assets/_Project/Scripts/Waves/ShopItemConfig.cs)** —
  `kind` plus exactly one payload reference; `OnValidate` errors on a mismatch.
- **[PassiveConfig.cs](../../Assets/_Project/Scripts/Core/PassiveConfig.cs)** — a
  list of `(stat, kind, value)` rows. That is the entire upgrade system.
- **[StatSheet.cs](../../Assets/_Project/Scripts/Core/StatSheet.cs)** — the fold.
- **[Stat.cs](../../Assets/_Project/Scripts/Core/Stat.cs)** — MaxHealth, MoveSpeed,
  ReloadSpeed, DamageMult, MoneyGainMult. Deliberately short: every entry is read
  by real gameplay code.

## Who reads which stat

| Stat | Applied by | How |
| --- | --- | --- |
| MaxHealth | `RunContext.ApplyStats` | `Health.ConfigureMax`, which also heals to full |
| MoveSpeed | `PlayerMotor` | caches a multiplier on `StatsChanged` |
| ReloadSpeed | `WeaponRuntime.BeginReload` | duration divided by the multiplier |
| DamageMult | `WeaponController.ResolveHit` | one of three multipliers, see below |
| MoneyGainMult | `RunState.AddMoney` | applied to kills and clear bonuses |

## Key Behaviors & Non-Obvious Patterns

- **The sheet is rebuilt, never incremented.** Incremental application works right
  up until one path forgets to undo itself, and then the player has a permanent
  ghost bonus nobody can find.
- **Three damage multipliers, three owners:** falloff (the weapon), the stat sheet
  (what the player bought), and `DamageMultiplier` (the cheat). They are separate
  on purpose — a single combined field would make the cheat indistinguishable
  from a purchase in a bug report.
- **No duplicates within one break.** Four identical "Max HP" offers is a break
  with no decision in it.
- **Buying removes the offer from the list** rather than greying it out, keeping
  the list short enough to read at a glance.
- **An unimplemented item kind refunds and refuses.** Weapons and effect modules
  are in the pool's future; until they have handlers, a purchase returns the money
  rather than taking it for nothing.
- **Reload speed is captured when the reload starts** (`WeaponRuntime.ReloadDuration`),
  so cancelling measures against the same number the reload began with.
- Raising max health tops the player up. Deliberate: the shop only opens between
  waves, and an upgrade that leaves you at 12/125 reads as broken.

## Related Systems

- [waves.md](waves.md) — owns the ShopService and decides when a break happens.
- [ui.md](ui.md) — the keyboard-driven shop panel.
- [weapons.md](weapons.md) — reads DamageMult and ReloadSpeed.

## Gotchas

- The shop pool's **item references are re-linked on every build**, but weights,
  `minWave` and `maxOwned` are only written when the item count changes — so
  Inspector tuning survives a rebuild while a broken payload link cannot.
- `ContentRegistry` from the data-model sketch is **not built yet**. Nothing needs
  stableId lookup while runs are never serialised; it lands with unlocks or
  loadout persistence, whichever comes first.
- Shop input is raw `Keyboard.current`, not uGUI buttons. Adding a clickable shop
  means an EventSystem, an input module, and cursor lock/unlock around every
  break — three new failure modes for a four-line list.
