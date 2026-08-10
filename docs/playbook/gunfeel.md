# Gun feel — concrete starting numbers

Feel is not a polish phase. It is the product. A shooter with mediocre art and
excellent gunplay is fun; the reverse is not. These numbers are arcade-shooter
starting points — tune from here, do not treat them as correct.

All of them belong in a `WeaponConfig` ScriptableObject, never in code.

## The core loop numbers

Assume **player and enemy have 100 HP**. Everything else is derived from that.

| Class | Fire rate (RPM) | Seconds/shot | Body damage | Shots to kill | TTK |
| --- | --- | --- | --- | --- | --- |
| Assault rifle | 700 | 0.086 | 25 | 4 | ~257 ms |
| SMG | 900 | 0.067 | 20 | 5 | ~267 ms |
| Shotgun | 70 | 0.857 | 8 × 12 pellets | 1–2 | instant close |
| Marksman/DMR | 250 | 0.240 | 50 | 2 | ~240 ms |
| Sniper | 45 | 1.333 | 100 | 1 | instant |

**Target TTK: 200–400 ms.** This is the defining choice of an arcade shooter.
Below 200 ms, fights are decided before the player reacts and it feels cheap.
Above 500 ms, it starts feeling like a hero shooter. Everything else — movement
speed, map size, spawn distance — is tuned *around* the TTK, so pick it first
and change it rarely.

Headshot multiplier: **1.5×** for automatics, **2.0×** for marksman rifles.
A 1.5× AR then kills in 3 headshots instead of 4, which is a meaningful reward
without making body shots pointless.

## Handling times

These are what make a weapon feel heavy or snappy, and players feel them more
than they feel damage numbers.

| Property | AR | SMG | Sniper |
| --- | --- | --- | --- |
| ADS time (hip → aimed) | 250 ms | 200 ms | 400 ms |
| Sprint-to-fire delay | 200 ms | 150 ms | 350 ms |
| Reload (partial) | 2.0 s | 1.8 s | 3.0 s |
| Reload (empty) | 2.6 s | 2.3 s | 3.8 s |
| Weapon swap | 600 ms | 500 ms | 800 ms |

**Sprint-to-fire delay is the most underrated number in the list.** It is the
gap between releasing sprint and being able to shoot. Too short and the game
becomes a sprint-around-corners festival. 150–250 ms is the arcade sweet spot.

**Reload cancelling**: allow it after the "ammo inserted" moment but before the
animation ends. Players who discover this feel skilled. It costs nothing to
implement and adds a real skill ceiling.

## Recoil

Recoil is a *camera rotation*, applied per shot, with a spring returning toward
(but not exactly to) the original aim point.

```
Vertical kick per shot      0.6°  → rising to 1.2° by shot 8
Horizontal kick per shot    ±0.35° (seeded random, not pure random)
Recovery start delay        90 ms after the last shot
Recovery duration           250 ms
Recovery completeness       85%   ← never return 100%
```

Two rules that matter more than the values:

- **Make the pattern deterministic.** Use a per-weapon seeded sequence so the
  first eight shots always kick the same way. Learnable recoil is the skill
  ceiling of a shooter; pure random recoil is just noise the player cannot
  improve against.
- **Recover to 85%, not 100%.** Full recovery makes sustained fire free and the
  gun feels weightless. The residual drift is what forces burst-firing.

ADS reduces recoil to **60%** of hipfire values. Crouching, another **80%**.

## Spread (hipfire)

Spread is a cone, in degrees, from the muzzle. ADS spread is **zero** — accuracy
while aimed should be controlled by recoil alone, not by a random cone. A random
cone while aiming feels like the game is cheating.

```
Base standing hipfire       2.5°
Per-shot bloom             +0.35°, capped at 6°
Bloom decay                 4°/second after 100 ms of not firing
Moving                      ×1.4
Sprinting                   ×2.5  (or block firing entirely — see sprint-to-fire)
Crouched                    ×0.7
Airborne                    ×2.0
```

## Camera and view

```
Base FOV                    95 (vertical ~62) — arcade shooters run wide
ADS FOV                     base × 0.75, eased over the ADS time
Sniper scope FOV            25
Mouse sensitivity (ADS)     × 0.75 of hipfire
FOV kick on fire            +1.5° for 60 ms, eased back
Landing camera dip          2° over 120 ms
Sprint FOV                  +8, eased over 200 ms
```

**Unity's `Camera.fieldOfView` (and Cinemachine's Lens FOV) is VERTICAL.**
For the 95° horizontal above, enter **~62** in Unity. Typing 95 into the Unity
field gives roughly 120° horizontal — instant fisheye, and the most common
"why does my game look wrong" mistake in a first FPS.

Weapon sway and bob: keep small. Bob amplitude around 0.02 units at walk speed.
Overdone bob is the most common first-project mistake and it makes players
nauseous. Sway should lag the camera by roughly 60–80 ms — the weapon following
the look direction slightly late is what sells physical weight.

## The juice checklist

Fire feedback the player consciously notices:

- Muzzle flash + a **real point light for 0.03 s** (the light is what sells it)
- Camera shake — Cinemachine Impulse, small: 0.4–0.8°
- Weapon kick animation with a spring return
- Shell casing ejected, pooled, physics-enabled, despawning after ~3 s
- Two-layer audio: a close mechanical crack + a distance/reverb tail. One-layer
  gunshots are the number-one reason a shooter sounds cheap.

Impact feedback — half of "does this gun feel good" is what the *world* does:

- **Hitmarker**: a small centre-screen X plus a short click. A distinctly
  different, lower-pitched, longer sound for a kill. This single element does
  more for feel than any amount of weapon polish.
- Surface-specific decals, particles, and sounds — concrete, metal, wood, flesh
  are the minimum four
- Damage numbers popping off the target (optional but adds a lot in a wave mode)
- Enemy hit-reaction animation or at least a flinch/flash
- Ragdoll on death, with the killing blow's force applied

Absence feedback — what happens when the player is *not* shooting well:

- Distinct dry-fire click on an empty magazine
- Low-ammo audio cue at ~25%
- Directional damage indicator
- Screen edge vignette scaled to missing health, plus a heartbeat under 30%

## The grey-box test

Before any art, any menu, any enemy: a grey room, one gun, some blocks to shoot.
If firing the weapon at a wall is *satisfying on its own* for two minutes
straight, the project has a foundation. If it is not, no amount of content will
fix it — and adding content first only makes the eventual re-tune more expensive.

Budget more time here than feels reasonable. This is the whole game.

## Enemy AI feel (for the single-player sandbox)

Enemies exist to make the guns feel good. Tune them as feedback devices, not as
opponents trying to win.

```
Reaction delay before first shot     300–500 ms after seeing the player
Accuracy                             60–75% at mid range, degrading with player movement
Burst pattern                        3–5 rounds, then a 0.8–1.5 s pause
Telegraph                            audible callout or muzzle-up tell before firing
```

Two design rules worth more than any of those numbers:

- **The three-attacker rule**: no matter how many enemies are alive, only ~3 may
  actively attack at once. The rest reposition, flank, or wait. Without this,
  twelve enemies means instant death and the fight has no shape. This is the
  standard trick in every shooter that feels good against crowds.
- **Miss the first shot on purpose.** The enemy's opening shot should be a
  deliberate near-miss that tells the player where the threat is. It converts
  "I died from nowhere" into "I got caught out" — the same event, a completely
  different feeling.

Enemy variety beats enemy count: a rusher that closes distance, a shooter that
holds cover, and a heavy that absorbs damage produce more interesting fights
than thirty of any one type.
