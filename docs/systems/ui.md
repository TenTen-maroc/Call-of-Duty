# UI

> Last verified: 2026-08-12 (runs; crosshair, HUD and hitmarker confirmed in play.
> The layout section below is measured, not played — the scene has to be
> regenerated before any of it is on screen.)

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

## Layout

Every label on the arena canvas is created by `BuildLabel` in
[GreyBoxBuilder.cs](../../Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs), which
sets `anchorMin`, `anchorMax` **and** `pivot` to the same vector — so a label
anchored `(0, 1)` grows down-and-right from the top-left corner, and one anchored
`(0.5, 0.5)` grows symmetrically about the centre. The canvas is
`ScreenSpaceOverlay` + `ScaleWithScreenSize`, reference 1920×1080, **match =
width**, so the canvas is always exactly 1920 reference units wide and gets
taller or shorter as the aspect ratio changes. Horizontal positions are therefore
fixed; vertical ones are not.

- **The title-safe inset.** `HUD_SAFE_X` (120) and `HUD_SAFE_Y` (72) are the
  margin every corner-anchored label keeps — ammo, health, wave, enemies, money.
  They are 6.25% and 6.7% of their axes, deliberately clear of the 5% band that
  displays and capture paths are free to crop.
- **The top-left column** — wave line, enemies count, objective list — is one
  block. `HUD_COLUMN_WIDTH` (720) is shared, and each row's Y is *derived* from
  the row above (`HUD_COLUMN_WAVE_Y` → `..._ENEMIES_Y` → `..._OBJECTIVE_Y`)
  rather than typed, so the rows cannot drift into each other again.
- **The mission banner is anchored to the top edge**, not to the centre plus an
  offset. A centre-anchored banner's distance from the top grows with half the
  canvas height, and the canvas gets *shorter* as the screen gets wider.
- **The interact prompt is centre-anchored on purpose** and is the one element
  that should stay that way: its job is to sit a fixed distance under the
  crosshair. 120 units below centre cannot outrun half the canvas at any aspect.
- **`BuildLabel`'s 320×48 is a placeholder, not a default.** Any label carrying a
  string longer than roughly fifteen characters has to replace it.

`CampaignTests.TheObjectiveLine_StaysInsideTheCanvas_AndHoldsItsOwnStrings` is
the gate. It compares each label's `GetWorldCorners` against the canvas's own,
so it fails on *clipping* rather than on a coordinate, and it measures width with
Unity's text generator (`preferredWidth`/`preferredHeight`) rather than a
character count, so it never pins a label to one font size.

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

## Mission HUD (campaign only)

- [ObjectiveHud.cs](../../Assets/_Project/Scripts/UI/ObjectiveHud.cs) — the
  objective list and the MISSION COMPLETE / FAILED banner. Reads
  `MissionDirector`; never drives it. In endless mode the director disables
  itself, so this simply has nothing to show.

  Redrawn on the director's `ObjectivesChanged` **plus** a 0.1 s tick. Both are
  needed and neither is enough: without the event a completed objective would
  linger for up to a tenth of a second, and without the tick a hold timer would
  never appear to count down.

  It compares the rebuilt text against the last one with a **hand-written loop**,
  not `StringBuilder.Equals(string)`. That call is a trap — depending on which
  overload the compiler picks it is either a span comparison or `object.Equals`,
  and `object.Equals` compares references, so it would be false every time and
  quietly rebuild the string on every tick, which is the exact cost the
  comparison exists to avoid, with nothing on screen to show it is happening.

- [InteractPrompt.cs](../../Assets/_Project/Scripts/UI/InteractPrompt.cs) — the
  "HOLD F" line and its fill bar, from `PlayerInteractor`. The prompt string is
  built by the interactable and stored once, so the label is assigned only when
  the TARGET changes — the bar is what moves while the hold advances.

  The bar is a filled `Image` rather than a number: a number counting up reads
  as data and a bar filling reads as a thing you are doing. It is also the only
  feedback that a released hold is **draining** rather than cancelled, which is
  the one behaviour of the interaction system a player has to learn by seeing.

- [MissionSelectPanel.cs](../../Assets/_Project/Scripts/UI/MissionSelectPanel.cs)
  — see [menus.md](menus.md). Mirrors `SettingsPanel`'s host-driven contract
  exactly, for the same reason: two components polling one key in one frame is
  a race, because MonoBehaviour execution order is undefined.

## Related Systems

- [weapons.md](weapons.md) — the event source for the hitmarker and crosshair.
- [drones.md](drones.md) — what the damage feedback is reacting to, and what the
  console's drone cheats drive.
- [waves.md](waves.md) — the phase machine every panel listens to.
- [shop.md](shop.md) — what the shop panel is drawing.

## Gotchas

- **A wired label and a visible label are different claims.** `GreyBoxVerify`
  proves the first; only the PlayMode layout test proves the second. The
  objective line shipped 90 units from the left edge — inside the 5% a display
  may crop — and a real play session photographed the campaign's opening
  instruction reading "EADY / THE CONTROL POINT". Every gate passed: it compiled,
  every reference was non-null, the mission ran end to end and the headless build
  booted. None of them asked where anything was *drawn*.
- **The wave line was 320 units wide and nothing said so.** Once waves got
  identities, "WAVE 9 — CROSSFIRE" measured wider than that at 34 pt, wrapped,
  and a 48-tall Truncate box discarded the wrapped line. The wave name was simply
  never drawn, with no error anywhere. Unity gives no signal when a `Text`
  overflows a Truncate rect — assert `preferredWidth` against `rect.width`.
- **Moving a label in the builder changes nothing until the scene is
  regenerated.** `CoD → Build Grey Box`, then `CoD → Verify and Repair Grey Box`.
  The layout test fails against a stale scene, which is the intended behaviour.
- `Hitmarker` and `Crosshair` both hold a `WeaponController` reference; if the
  player rig is rebuilt they must be re-wired. `GreyBoxVerify` checks exactly that.
- The console uses IMGUI (`OnGUI`), which is fine because it only exists in dev
  builds — do not copy that pattern into shipping UI.
- Labels use the built-in `LegacyRuntime.ttf`. If text ever renders blank, that
  font lookup is the first thing to check.
