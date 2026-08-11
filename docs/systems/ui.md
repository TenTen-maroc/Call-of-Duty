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
- **[PlayerDamageFeedback.cs](../../Assets/_Project/Scripts/UI/PlayerDamageFeedback.cs)** —
  what being hurt looks and sounds like: a red flash, one of four screen-edge
  wedges pointing at whatever hit you, a pulsing tint under 35% health, and a hurt
  sound. Listens to the player's `Health.Damaged`.
- **[WaveHud.cs](../../Assets/_Project/Scripts/UI/WaveHud.cs)** — wave number,
  enemies remaining, money, and the centre banner that counts the next wave in.
  Rebuilds a label only when its number changes.
- **[ShopPanel.cs](../../Assets/_Project/Scripts/UI/ShopPanel.cs)** — the
  between-wave shop. 1-4 buy, R rerolls, Space continues.
- **[GameOverPanel.cs](../../Assets/_Project/Scripts/UI/GameOverPanel.cs)** —
  round reached against the best on record, R to run it again.
- **[CheatConsole.cs](../../Assets/_Project/Scripts/UI/CheatConsole.cs)** —
  backquote toggles; 1-9 for godmode, infinite ammo, slow-mo, spawn dummy, damage
  multiplier, spawn a drone burst, clear drones, skip the wave, and +1000 money.
  It also shows the live alive-count and attacker-token counters. Entirely inside
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, so a shipping build cannot be cheated
  by someone who found the key.

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
  quiet framerate leaks in Unity UI. `IsReloading` is part of the cache key: an
  auto-reload on empty starts without an ammo change, and without it the
  `-- / reserve` reload readout never appeared at all.
- **The hitmarker never downgrades a kill.** A shotgun resolves several pellets
  in one frame; a plain hit pellet arriving after the kill pellet keeps the kill
  colour and duration instead of overwriting them.
- **Godmode actually blocks damage** via `Health.Invulnerable` — the console
  flips real state on the player's Health, not a flag nothing reads.
- **Slow-mo restores the project's own `fixedDeltaTime`**, captured in `Awake` —
  it never assumes Unity's 0.02 default.
- The hitmarker's kill sound matters more than it looks: per the gunfeel
  reference it does more for feel than any amount of weapon polish.
- **The damage direction indicator is the incoming-fire equivalent of the
  hitmarker.** `DamageInfo.Direction` is the direction the damage was *travelling*,
  so the source is the other way; that vector is projected onto the camera's own
  axes and the dominant one lights. It turns "I died from nowhere" into "I got
  caught out" — the same principle as the Shooter's deliberate opening miss.
- **Transparent overlays are disabled, not just faded to zero.** Four idle
  full-screen quads still cost fill rate on a laptop GPU, so `PlayerDamageFeedback`
  toggles `Image.enabled` as alpha crosses zero.

## Related Systems

- [weapons.md](weapons.md) — the event source for the hitmarker and crosshair.
- [drones.md](drones.md) — what the damage feedback is reacting to, and what the
  console's drone cheats drive.
- [waves.md](waves.md) — the phase machine every panel listens to.
- [shop.md](shop.md) — what the shop panel is drawing.

## Gotchas

- `Hitmarker` and `Crosshair` both hold a `WeaponController` reference; if the
  player rig is rebuilt they must be re-wired. `GreyBoxVerify` checks exactly that.
- The console uses IMGUI (`OnGUI`), which is fine because it only exists in dev
  builds — do not copy that pattern into shipping UI.
- Labels use the built-in `LegacyRuntime.ttf`. If text ever renders blank, that
  font lookup is the first thing to check.
