# UI

> Last verified: 2026-08-11 (runs; crosshair, HUD and hitmarker confirmed in play)

## Overview

Four components on one screen-space canvas, all of which *listen* — none of them
are polled by gameplay, and no gameplay code knows the UI exists. The weapon
raises events; the UI decides what to draw.

## Components

- **[Crosshair.cs](../../Assets/_Project/Scripts/UI/Crosshair.cs)** — four arms
  and a centre dot. The gap tracks `WeaponController.EffectiveSpreadDegrees`, so
  the reticle opens as the weapon blooms. Fades out under ADS, where spread is
  always zero and the sight is the aiming device.
- **[Hitmarker.cs](../../Assets/_Project/Scripts/UI/Hitmarker.cs)** — subscribes
  to `WeaponController.Hit(bool killed)`. Four bars forming an X, punched out and
  eased back. The kill variant is a different colour, longer, and a lower sound.
- **[Hud.cs](../../Assets/_Project/Scripts/UI/Hud.cs)** — ammo and health, plus
  the low-ammo bar at 25% magazine.
- **[CheatConsole.cs](../../Assets/_Project/Scripts/UI/CheatConsole.cs)** —
  backquote toggles; 1-5 for godmode, infinite ammo, slow-mo, spawn dummy, damage
  multiplier. Entirely inside `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, so a
  shipping build cannot be cheated by someone who found the key.

## Key Behaviors & Non-Obvious Patterns

- **The crosshair showing bloom is the point of it.** Bloom the player cannot see
  is bloom that just feels like bad luck. Watching the reticle open while holding
  the trigger teaches burst-firing with no tutorial.
- **Everything is outlined.** Each white element has a dark plate one pixel proud
  on each side. The first version was plain white and vanished against the bright
  floor — a reticle that is invisible half the time is worse than none.
- **Crosshair alpha goes through a `CanvasGroup`, not per-`Graphic` colour.** With
  per-graphic alpha the dark outlines stayed visible while the white bars faded.
- **`Hud` only rebuilds text when the number changes.** Assigning `Text.text`
  every frame allocates a string every frame and dirties the canvas — one of the
  quiet framerate leaks in Unity UI.
- The hitmarker's kill sound matters more than it looks: per the gunfeel
  reference it does more for feel than any amount of weapon polish.

## Related Systems

- [weapons.md](weapons.md) — the event source for the hitmarker and crosshair.

## Gotchas

- `Hitmarker` and `Crosshair` both hold a `WeaponController` reference; if the
  player rig is rebuilt they must be re-wired. `GreyBoxVerify` checks exactly that.
- The console uses IMGUI (`OnGUI`), which is fine because it only exists in dev
  builds — do not copy that pattern into shipping UI.
- Labels use the built-in `LegacyRuntime.ttf`. If text ever renders blank, that
  font lookup is the first thing to check.
