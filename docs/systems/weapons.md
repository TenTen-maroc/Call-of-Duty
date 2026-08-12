# Weapons

> Last verified: 2026-08-12 (runs; firing, ammo, HUD and audio confirmed in play.
> The pellet-scoping fix, the corrected time-to-kill model and the per-class
> balance laws below are covered by tests and have never been *felt*. The
> one-shot premise and the registry/folder cross-check are gate fixes only: no
> authored value on `AR_Standard` or `SMG_Rapid` has ever moved.
>
> ⚠️ **The arsenal grew from two weapons to six on 2026-08-12** — pistol,
> marksman, LMG and shotgun, authored by `ArsenalBuilder`. None of the four has
> been fired. Their numbers satisfy their classes' balance laws by arithmetic and
> by test; whether any of them is *fun* is unanswered, and the shotgun has a known
> unfixed hole in the fire path — see "Does 'a weapon is data' hold?" below.
>
> ⚠️ **And to eight with the launcher and the sniper (W4-W5, 2026-08-12)** — the
> launcher is the first weapon whose shot does not resolve on the frame the
> trigger is pulled; the sniper is the first weapon that is not finished until
> something is bolted to it. Both are covered by tests that drive the real fire
> path, and **nobody has fired either**.
>
> Every asset named here is generated: run `CoD → Build Grey Box`, then
> `CoD → Build Arsenal`, then `CoD → Build VFX`, then `CoD → Build Grey Box`
> again. The last pass is not superstition — it is what puts `Fx_Rocket` in the
> pool's prewarm list and the weapon registry on the sandbox console.)

## Overview

One modular weapon system. A weapon is a `WeaponConfig` asset read at runtime and
never written to, plus a `WeaponRuntime` object holding everything that changes.
`WeaponController` is the MonoBehaviour that turns input into shots, damage and
feedback. New weapons are new assets, not new code.

**Two deliveries, one fire path.** `WeaponConfig.delivery` picks between them and
it is the ONLY branch in `FireOneShot`:

- **Hitscan** — a raycast per pellet, nearest hit wins, resolved on the frame the
  trigger is pulled. Six of the seven weapons.
- **Projectile** — a real pooled object in the air, resolved when it arrives. The
  launcher, and see "The launcher, and what a projectile costs" below.

Cadence, ammo, burst, bloom, the shotgun pattern, recoil, the muzzle, the casing
and the audio are shared by both. A launcher differs from a rifle in what leaves
the barrel and in nothing else.

## Data Assets

- **[WeaponConfig.cs](../../Assets/_Project/Scripts/Weapons/WeaponConfig.cs)** —
  every tunable number for a weapon: damage, falloff, RPM, fire mode, magazine,
  handling times, recoil, spread, ADS, and the feedback prefab/clip references.
  `stableId` is the save/registry key and is never renamed once shipped.
  `OnValidate` warns in the Inspector when a weapon leaves the window its CLASS
  answers to — see "The time-to-kill model" below; the 200–400 ms window is the
  law for the core automatics only.
- **[PlayerLoadoutConfig.cs](../../Assets/_Project/Scripts/Weapons/PlayerLoadoutConfig.cs)** —
  starting weapon and carried-slot count. Lives here, not on `GameConfig`, so
  `CoD.Core` keeps depending on nothing.
- **[ImpactConfig.cs](../../Assets/_Project/Scripts/Weapons/ImpactConfig.cs)** —
  decal/particle prefabs, lifetimes, and the surface offset that stops decals
  z-fighting with the wall they sit on.
- **[WeaponRegistry.cs](../../Assets/_Project/Scripts/Weapons/WeaponRegistry.cs)** —
  the whole arsenal in one asset: the list the balance tests walk and the list a
  save resolves a `stableId` against. Written by `ArsenalBuilder` — see below.

`WeaponController.EquipWeapon` swaps the carried weapon at runtime and **carries
installed effect modules across**: modules are ammunition tech, not part of the
gun, so buying Explosive Rounds and then a new rifle never silently throws the
purchase away.

`AR_Standard` is 700 RPM at 25 damage: 4 shots to kill 100 HP, 3 gaps of
0.0857 s ≈ **257 ms TTK**. Movement, arena scale and spawn distance are all tuned
around that number — change it deliberately. That window is the law for the core
automatics and **only** for them; the shotgun, sniper and launcher classes answer
to different ones, and the model that computes TTK was corrected on 2026-08-12 —
see the next section before authoring any weapon.

## The arsenal

Eight weapons, all one class (`WeaponConfig`) driving one controller. Two are the
grey box's; four were added on 2026-08-12 to test the claim that a weapon is data,
and the launcher and the sniper were added the same day to find out where the
claim stops.

| Asset | `stableId` | Class / law | Damage × pellets | RPM | Mag / reserve | Point-blank TTK | Falloff | ADS | The trade |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AR_Standard` | `wpn_ar_standard` | AssaultRifle · arcade | 25 × 1 | 700 auto | 30 / 180 | **257 ms** (4 pulls) | 25→60 @ 0.6 | 0.25 s | the baseline the whole game is tuned to |
| `SMG_Rapid` | `wpn_smg_rapid` | SMG · arcade | 20 × 1 | 900 auto | 40 / 240 | **267 ms** (5 pulls) | 14→34 @ 0.5 | 0.20 s | faster and snappier, dead past 30 m |
| `Pistol_Sidearm` | `wpn_pistol_sidearm` | Pistol · arcade | 34 × 1 | 400 single | 12 / 96 | **300 ms** (3 pulls) | 12→30 @ 0.45 | **0.15 s** | fastest thing to bring up; twelve rounds |
| `DMR_Marksman` | `wpn_dmr_marksman` | Marksman · arcade | 55 × 1 | 240 single | 10 / 60 | **250 ms** (2 pulls) | 40→90 @ **0.85** | 0.32 s | 2.0× headshot and real range; a miss costs 250 ms |
| `LMG_Support` | `wpn_lmg_support` | LMG · arcade | 20 × 1 | 750 auto | **100** / 300 | **320 ms** (5 pulls) | 20→55 @ 0.55 | 0.42 s | 8 s of continuous fire; 5.4 s empty reload, 70% recoil recovery |
| `SG_Breacher` | `wpn_sg_breacher` | Shotgun · **contact burst** | 10 × **12** | 70 single | 6 / 48 | **one pull** at contact, **two** at 10 m | 6→16 @ 0.3 | 0.28 s | owns the first six metres and nothing after |
| `RL_Launcher` | `wpn_rl_launcher` | Launcher · **re-engagement** | 100 × 1 **+ blast** | 55 single | **1** / 8 | **one pull**, after a ~1 s flight | 30→80 @ 0.85 | 0.45 s | one round in the tube, and the shot has to be led |
| `SR_Longshot` | `wpn_sr_longshot` | Sniper · **re-engagement** | 100 × 1 | 50 single | 5 / 40 | **one pull** | 60→140 @ **0.9** | 0.44 s (**0.55 scoped**) | 1.20 s bolt cycle, 5x optic, and hipfire is not an option |

Built by [ArsenalBuilder.cs](../../Assets/_Project/Scripts/Editor/ArsenalBuilder.cs)
(`CoD → Build Arsenal`, or `-executeMethod CoD.EditorTools.ArsenalBuilder.BuildArsenalHeadless`).
**Run the grey box first** — the rifle and the SMG are its assets and the arsenal
builder *loads* them rather than creating a second copy with a second `stableId`.
Like every builder here the configure callback runs **on create only**, so a
number a human moved in the Inspector survives a re-run; what is re-asserted every
run is references — the registry's entries, and any feedback slot still empty.

All five new weapons adopt the **same** `Fx_MuzzleFlash` and `Fx_ShellCasing`
prefabs the rifle uses, so the arsenal more than trebled on **one** new pool
entry — `Fx_Rocket`, which the launcher genuinely needs — and without a new gun's
first shot allocating.

`ArsenalBuilder` also re-checks every weapon against its class's law at build time
and throws (non-zero exit, headless) if one fails. It reads the same `const`s off
`WeaponConfig` that `WeaponDataTests` reads — one law, two readers, never two laws.

## The launcher, and what a projectile costs

The launcher answers to `BalanceLaw.ReEngagementCost`, and it *earns* the
exemption from the arcade window rather than being granted it by its enum:
100 damage × 1 pellet is exactly one pull on a 100 HP drone, so the one-shot
premise holds and is asserted. Then the price of the second shot — 0.45 s to aim
against the 0.35 s floor, and 55 RPM = **1.09 s** to cycle against the 0.9 s
floor. Re-engagement is ~1.54 s against the AR's 0.257 s time-to-kill: six rifle
kills per launcher kill, and that gap *is* the balance.

**The flight time is the fairness.** A rocket at 34 m/s crosses the arena's
longest lane in about a second, so a rusher closing at 6 m/s has to be led. That
is the skill cost the damage pays for, and it is also why delivery is a
projectile rather than a hitscan ray with a big radius: a 100-damage hitscan
weapon is unavoidable and unreadable at the same time, which is the exact
argument that keeps the Shooter drone's rounds slow.

**A magazine of one.** One chambered, eight in reserve, 3.0 s to reload, 1.1 s to
swap away from. Two rounds would make the miss free.

`headshotMultiplier` is 1.0 on purpose: `Explosive` already refuses weakpoint
children on the blast, and a weakpoint bonus on the direct hit would make a
100-damage round a 150-damage round for aim that a 4.5 m blast makes irrelevant.

### The blast is its own asset

`Effect_RocketBlast` — 4.5 m, 0.7 of the round that caused it, falling to 0.35 at
the edge, `maxDepth` **0**. Deliberately NOT the shop's `Effect_Explosive`, which
ships `maxDepth 1` so blast victims detonate in turn: that is the absurd thing
the shop *sells*, and on a 100-damage round in a wave-12 crowd it is a frame
event rather than a big hit. Sharing one asset would also mean tuning the
launcher silently retunes the shop item, because configs are shared references.

### The three ways a projectile can go wrong, none of which throws

Splitting the pull from the impact by a second opens three holes. All three are
pinned by [LauncherTests.cs](../../Assets/_Project/Tests/PlayMode/LauncherTests.cs).

1. **It can resolve with the wrong weapon.** A rocket in flight OUTLIVES A WEAPON
   SWAP. Read the controller's *current* runtime at impact and a rocket still in
   the air when the player takes the pistol lands for the pistol's damage, the
   pistol's falloff and the pistol's modules. So the `WeaponConfig` is **carried
   on the round** — `Projectile.Payload` — and the sink casts it back, refusing
   the impact rather than guessing if the cast fails.
2. **It can resolve outside the weapon's damage model.** Falloff, the stat sheet,
   the cheat multiplier, the weakpoint bonus, the impact VFX, the per-surface
   sound, the hitmarker rule and the ordered effect-module list all live in
   `WeaponController.ResolveHit`. A projectile that applied its own damage would
   need a second copy of every one of them, and a launcher whose blast never
   fires is a 100-damage rifle. So the projectile hands the impact **back**,
   through `IProjectileImpactSink`, and `ResolveHit` + `DrainFollowUps` run
   exactly as they do for a ray.
3. **It can detonate on the shooter.** `ProjectileShot.Owner` is passed through
   on the sweep whatever side it is on, and `Explosive` already refuses
   `OwnerHealth`.

⚠️ **`ResolveHit` takes an explicit `rangeMetres` rather than reading
`hit.distance`.** For a ray they are the same number. For a projectile the hit is
the result of a sweep across ONE FRAME, so its `distance` is a few centimetres
however far the rocket flew — reading it would have made every launcher round
point blank at every range in the arena, silently, because falloff has no other
symptom. The value passed is `Projectile.DistanceTravelled`.

⚠️ **A projectile weapon throws no tracer**, even though `VfxBuilder` stamps
`tracerPrefab` onto every weapon on disk. The round *is* the visible line;
`_tracerEnd` is only ever resolved by a hitscan pull, so a tracer here would fly
to the far end of the aim ray while the rocket that matters is a metre out of the
tube. One line in `SpawnTracer`, kept by a test.

### Does "a weapon is data" hold? — the honest answer

**Three out of four: yes, completely.** The pistol, the marksman and the LMG are
each one `Configure` method in `ArsenalBuilder` and **zero** lines of runtime code.
Between them they move the fire mode (`Single`), the headshot multiplier (2.0×),
the magazine (12 → 100), the reload class (1.4 s → 5.4 s), the recoil recovery
(0.95 → 0.70), the scope (`adsFovMultiplier` 0.45 with a matching sensitivity
drop) and the falloff shape — and the fire path, the HUD, the shop, the save and
the pool all needed nothing. That is the claim standing up under four times the
load it had ever carried.

**The shotgun is where it breaks, and the break is geometry.** `pelletsPerShot:
12` gets the *damage* right — `DamagePerShot` is `bodyDamage × pellets`, so the
ContactBurst law (one pull at contact, two at ten metres) is satisfied by
arithmetic over fields that already exist. What no field expresses is the
**pattern**:

- A pattern is a **fixed** cone: the same twelve-pellet spread on the first pull
  and the fiftieth, hip or aimed. It is a property of the choke, not of the state
  of the gun.
- Bloom is the opposite: `WeaponRuntime.CurrentSpread` starts at `baseSpread`,
  grows by `spreadPerShot`, decays back — and
  `WeaponController.CurrentSpreadDegrees` returns **exactly zero while aiming**,
  deliberately ("a random cone while aiming reads as the game cheating").
- `FireOneShot` casts every pellet through that one number. So an **aimed**
  shotgun today puts all twelve pellets on a single point: 120 damage in one ray
  at any range inside the falloff. That is a sniper wearing a shotgun's name, and
  the balance law does not see it, because `ShotsToKillAtRange` charges for
  *distance* and knows nothing about where the pellets landed.

**The fix is one field and one line**, and neither is authorable from a data file:
`pelletSpreadDegrees` on `WeaponConfig`, and `CastOneRay` taking a cone of
`max(pelletSpreadDegrees, bloom)` rather than bloom alone. Until then the shotgun
is honest at the hip (`baseSpread 4.0`) and wrong down the sights. Recorded in
`ConfigureShotgun`'s header and asserted as far as it can be by
`EveryMultiPelletWeapon_ThrowsAConeRatherThanAPoint`, which also carries the note
about what to strengthen when the field lands.

**Shop rows are still missing.** `BuildShopItems` lives in `GreyBoxBuilder` and
was out of scope for the arsenal work, so the four new weapons are in the registry
and in the balance gate but not on any shelf. Until they are, they are reachable
only by the Sandbox cheat console.

## The time-to-kill model, and the law it answers to

**The metric was wrong in four ways, and the law it fed was wrong in one more.**
Fixed 2026-08-12, before the arsenal grew past two guns. Covered by tests, never
played.

`ShotsToKill` divided health by `bodyDamage`; `TimeToKill` multiplied the gaps by
`SecondsPerShot`. That model:

- **reported TTK 0 for a one-shot weapon**, so a sniper and a launcher were
  structurally incapable of clearing a 200 ms floor — the only way to "pass" was
  to author a 99-damage sniper that does not one-shot;
- **ignored `pelletsPerShot`**, scoring a 12x11 shotgun as an 11-damage gun that
  needs nine pulls to kill a 100 HP drone, when it needs one;
- **ignored `burstPause`**, scoring a 3-round burst as if the gap between bursts
  were free — 257 ms for a gun that really takes 377 ms;
- **ignored falloff**, though `DamageAtDistance` was already there and correct.

| Member | What it answers |
| --- | --- |
| `SecondsPerShot` | cadence, `60 / RPM` |
| `DamagePerShot` | `bodyDamage x pelletsPerShot` — one trigger PULL, however many rays it casts |
| `ShotsToKill(health)` | pulls at point blank, floored at **1** |
| `ShotsToKillAtRange(health, m)` | the same, charged for falloff |
| `TimeForShots(n)` | `n-1` cadence gaps **plus one `burstPause` per burst boundary crossed** |
| `TimeToKill(health)` | `TimeForShots(ShotsToKill(health))` |
| `TimeToKillAtRange(health, m)` | the honest number at a distance |

`TimeForShots` mirrors `WeaponController.FireOneShot`, which adds `burstPause`
**on top of** the cadence after the last round of a burst. Four rounds at
`burstCount 3` cross exactly one boundary, not two — `(shots - 1) / burstCount`.

`AR_Standard` is unchanged by all of this: 700 RPM, 25 damage, 1 pellet, full
auto, so 4 shots and 3 gaps of 0.0857 s ≈ **257 ms**, exactly as before.

### The law is per class, not universal

The 200–400 ms window is the identity of the CORE AUTOMATICS. It is not a law of
physics, and enforcing it everywhere produces a strictly worse game than admitting
that. It also breaks on its own terms the moment
`DifficultyConfig.healthMultiplierByWave` (ramping to 3.5x) means nothing
one-shots regardless of what was authored.

| Class | `BalanceLaw` | The law |
| --- | --- | --- |
| AssaultRifle, SMG, LMG, Pistol, Marksman | `ArcadeTtkWindow` | TTK in [200, 400] ms against 100 HP |
| Shotgun | `ContactBurst` | `ShotsToKill() == 1` at contact **asserted**, ≥ 2 pulls at 10 m, via `TimeToKillAtRange` |
| Sniper, Launcher | `ReEngagementCost` | `ShotsToKill() == 1` **asserted first**, then `adsTime ≥ 0.35 s` **and** `SecondsPerShot ≥ 0.9 s` |

**A one-shot weapon is balanced on the cost of the NEXT shot.** ~1.25 s to
re-engage against the AR's 0.257 s is the trade actually being sold; TTK for a
sniper is zero by design and says nothing at all. **A shotgun is the gap between
its two numbers** — one pull at every range is a sniper without a scope, two pulls
at contact is just a bad rifle.

#### The one-shot premise is asserted, never assumed (fixed 2026-08-12)

`ReEngagementCost` exempts a weapon from the 200–400 ms window **on the premise
that it one-shots**, and for one commit nothing checked the premise. Neither the
test nor `OnValidate` asked `ShotsToKill() == 1`, so `weaponClass = Sniper` was a
blanket exemption from every TTK bound in the project. A weapon at `bodyDamage
25` / `roundsPerMinute 60` / `adsTime 0.40` needs **four pulls and 3.0 seconds**
to kill a 100 HP drone, clears both re-engagement floors, and passed the gate in
silence — the "99-damage sniper that does not one-shot" the split was written to
prevent, arriving through the door the split opened. The asymmetry gave it away:
the parallel `ContactBurst` branch had always asserted its own premise.

**The exemption is now earned by the ASSET, not granted by the enum.**
`ShotsToKill() == 1` is the first assertion in the `ReEngagementCost` branch of
`EveryWeapon_ObeysTheLawOfItsClass`, and `OnValidate` carries the matching
class-aware warning naming the pulls, the damage per pull and the real TTK.
Binary, so there is no wider warn band the way the arcade window has one — a
weapon either one-pulls or it belongs to a different class.

`TheOneShotExemption_IsEarnedByTheAsset_NotGrantedByTheEnum` watches that gate
**fail** on the impostor above and **pass** on an honest 100-damage sniper. The
per-weapon check was split out of the loop into
`AssertObeysTheLawOfItsClass(config)` for exactly that reason: a gate nobody has
seen bite is a gate nobody knows is connected.

The window, the shotgun range and the one-shot floors are `const` on
`WeaponConfig` (`ARCADE_TTK_MIN_MS`, `SHOTGUN_TWO_PULL_METRES`,
`ONE_SHOT_MIN_ADS_SECONDS`, `ONE_SHOT_MIN_CYCLE_SECONDS`), read by BOTH
`OnValidate` and `WeaponDataTests`. They are boundaries rather than dials — the
same kind of number as `MAX_FOLLOW_UPS_PER_PULL` — and a law with two copies is a
law that gets edited on one side to make a test go green.
`WeaponConfig.LawFor(WeaponClass)` is the single mapping, and an unlisted class
defaults to `ArcadeTtkWindow`: a new class cannot escape the only balance rule
this project has simply by being new.

`OnValidate` is class-aware for the same reason, and warns wider than the test
fails (150–500 ms): a correctly authored sniper that screams in the Inspector
forever is how a developer learns to ignore the console.

### WeaponRegistry

[WeaponRegistry.cs](../../Assets/_Project/Scripts/Weapons/WeaponRegistry.cs) — a
`WeaponConfig[]` in one asset, plus `ByStableId` (ordinal comparison; returns null
rather than throwing, so a save naming a retired weapon falls back to the loadout
instead of ending the run). It exists because the balance gate used to walk a
hardcoded array of two asset paths, which made weapon number three a TEST edit —
and a law you have to remember to opt a weapon into is a law the seventh weapon
escapes. `OnValidate` errors on a null entry (a deleted asset leaves a hole, and
the hole drops that weapon out of every gate while the list still looks the right
length) and on a duplicate `stableId` (which aliases two weapons into one for
every save that names it, with no runtime error anywhere).

**`Weapons.asset` is written by
[ArsenalBuilder](../../Assets/_Project/Scripts/Editor/ArsenalBuilder.cs)**, listing
all six weapons in build order — including the two it did not author, because a
weapon missing from the registry fails the folder cross-check, and that is the gate
working rather than a problem to route around. The write is append-only (order is
presentation, and re-sorting on every build would renumber a list a human curated),
re-points an entry whose asset was replaced, and **compacts null slots with a
warning** — a null is the residue of a deleted asset, it still counts toward
`Length`, and it is never authored intent.

`TheRegistry_IfItExists_HasNoHolesAndNoAliasedIds` self-ignores when the asset is
absent (a fresh clone, before anyone runs the builder), and
`TheArsenalGate_ActuallyFindsTheShippedWeapons` stops the enumeration from silently
finding nothing and staying green.

#### The scan and the registry must AGREE (fixed 2026-08-12)

`WeaponDataTests.AllWeapons()` used to return `registry.allWeapons` whenever the
registry existed and was non-empty, and scan the folder only as a fallback. That
re-opened the exact hole the registry exists to close: the moment the builder
writes `Weapons.asset`, weapon number three added to
`Assets/_Project/Data/Weapons` but **forgotten in the registry** drops silently
out of `EveryWeapon_ObeysTheLawOfItsClass`, and
`TheArsenalGate_ActuallyFindsTheShippedWeapons` cannot see it — `Length >= 2` and
both known `stableId`s stay true. That is "weapon seven quietly escapes the
balance gate", which is the failure `WeaponRegistry`'s own header cites as its
reason to exist.

**Neither source is now trusted over the other.**

- `ScanWeaponFolder()` runs **every time**. The scan is the *coverage*: an asset
  is inside every balance law the moment it exists on disk, with nothing to
  remember and nothing to opt into.
- When the registry exists, `AssertRegistryAndFolderDescribeTheSameArsenal`
  asserts the two `stableId` sets are equal, **in both directions**, naming the
  offending asset: on disk but unlisted (outside every balance law), listed but
  absent from the folder (a save key that resolves to nothing), an empty slot
  (still counts toward `Length`), and duplicate ids on disk (which would collapse
  two weapons into one set entry and make the comparison agree about an arsenal
  that does not exist).
- Only then is the registry used, for **ordering and lookup**. Anything the scan
  found that the registry does not list is appended rather than dropped, so
  coverage never depends on the assertion having been reached.
- The "no registry asset yet" path stays graceful: absent means the scan alone is
  the gate. Absent is never a *preference* for the registry.

`TheRegistryAndTheFolder_MustDescribeTheSameArsenal` builds synthetic registries
and watches the cross-check throw on each of those four disagreements, plus pass
on an agreeing pair — synthetic, so the check is proven connected whether or not
anyone has run the builder.

#### Four more gaps, found by growing the arsenal (2026-08-12)

Going from two weapons to six exposed four properties nothing checked, each true
by luck while the arsenal was two guns and each silent when it stops being true.

- **The aliased-id check only ran when a registry existed.** The blank-`stableId`
  and duplicate-`stableId` assertions were the opening of the registry
  cross-check, and the registry did not exist until `ArsenalBuilder` shipped — so
  two assets sharing one id were caught by nothing at all. An arsenal is authored
  by *copying the nearest weapon*, and the id is the field people forget.
  `AssertNoAliasedIdsOnDisk` is now called on **every** scan, registry or no
  registry, and `TheFolderScan_RejectsAnAliasedId_EvenWithNoRegistry` watches it
  bite on both a copied id and a blank one.
- **`displayName` had no gate.** It is the shop row, the HUD label and the loadout
  line; a blank one ships as a button the player cannot identify, with nothing
  anywhere reporting it. `EveryWeapon_IsNameableInAShopRowAndInASave`.
- **Two copy-a-weapon-and-narrow-one-number mistakes.** `maxSpread` below
  `baseSpread` makes the clamp fire on the very first shot, so the gun is
  permanently tighter than the cone its own asset claims; `reserveAmmo` below
  `magazineSize` is a gun that can never finish one reload, and `RefillReserve`
  hands back a *fraction* of that. `reloadEmptyTime` below `reloadTime` is worse
  than wasted — it makes emptying the magazine the optimal play.
  `EveryWeapon_BloomCanNeverShrinkBelowItsOwnBaseline` and
  `EveryWeapon_CanCompleteOneReloadFromItsOwnReserve`.
- **The starting weapon and every shop weapon are DIRECT references.**
  `PlayerLoadoutConfig.startingWeapon` and `ShopItemConfig.weapon` both work
  perfectly while pointing at a weapon no `stableId` resolves — the player carries
  it for the rest of the run and the next save writes a key that comes back null.
  Same failure as a registry entry with no asset behind it, arriving from the
  other end. `TheStartingWeapon_IsPartOfTheArsenal` and
  `EveryWeaponTheShopSells_IsPartOfTheArsenal`, both scanning rather than listing,
  so shop row number twelve is not a test edit.

## Runtime Types

- **[WeaponRuntime.cs](../../Assets/_Project/Scripts/Weapons/WeaponRuntime.cs)** —
  plain C# object, one per carried weapon. Ammo, reserve, bloom, shot cadence,
  reload timing, shots-in-burst. Owns reload begin/cancel/complete and spread
  decay. Never touches the config.
- **[RecoilPattern.cs](../../Assets/_Project/Scripts/Weapons/RecoilPattern.cs)** —
  static, stateless. Vertical climb lerps first-shot → shot-eight. Horizontal
  comes from an integer hash of `(recoilSeed, shotIndex)`, so the pattern is
  identical on every machine and every run.
- **[WeaponController.cs](../../Assets/_Project/Scripts/Weapons/WeaponController.cs)** —
  the MonoBehaviour. Exposes `Hit(bool killed)` and `Fired` events; the UI
  subscribes and the weapon never learns the UI exists. `Hit` fires **once per
  trigger pull** that landed damage, with a sticky kill flag — not once per
  pellet and not once per follow-up.

## Audio

Six placeholder clips in `Assets/_Project/Audio/`, synthesised by
`node Tools/make-placeholder-audio.mjs` and wired onto `AR_Standard` by the
builder, which also forces mono / PCM / decompress-on-load. The gunshot is two
layers on purpose — a close mechanical crack plus a distance tail. One-layer
gunshots are the top reason a shooter sounds cheap. See the folder README.

## Key Behaviors & Non-Obvious Patterns

- **Configs are read-only at runtime.** Domain Reload is disabled, so a runtime
  write to an asset persists between Play sessions and silently rewrites your
  authored balance. Passives will modify a StatSheet, never the asset.
- **ADS spread is exactly zero.** Aimed accuracy is governed by recoil alone; a
  random cone while aiming reads as the game cheating.
- **Recoil recovers to 85%, not 100%.** `CommitRecoilToAim` folds the
  unrecovered slice into the real aim point each frame, which is what forces
  burst-firing instead of holding the trigger.
- **The aim ray comes from the camera pivot, not the camera.** The camera carries
  the shake offset; shake must never move the point of impact.
- **Sprint-to-fire** is enforced from the moment sprint is *released*
  (`TrackSprintRelease`), not from when the trigger is pulled.
- **Burst mode is a started-burst-finishes-itself loop**: the trigger press arms
  `BurstShotsRemaining`, the cadence fires the rest, and `burstPause` lands after
  the final round. Reloading or starting a sprint abandons a queued burst.
- **Reload cancelling** past `reloadCommitPoint` keeps the ammo. Firing during a
  reload attempts the cancel first — but **never when the magazine is empty**:
  cancelling an empty-mag reload gains nothing, and holding the trigger would
  re-cancel the auto-reload every frame, leaving a gun that never reloads.
- **Auto-reload on empty** only when reserve remains; otherwise a dry-fire click
  with a 0.25 s re-trigger delay. All reloads enter through one
  `TryBeginReload`, which is also where `reloadClip` plays.
- **Headshots: the weapon owns the number.** A `Weakpoint` component on a child
  collider relays hits to its owner's `Health`; the controller multiplies by
  `WeaponConfig.headshotMultiplier` and flags `DamageInfo.IsWeakpoint`. There is
  deliberately no second multiplier on the target side — two owners of the same
  bonus double-dipped every headshot.
- **Casing ejection overwrites the rigidbody's velocity, never adds to it.** A
  pooled rigidbody keeps whatever velocity it despawned with; eject speed, up
  kick and spin are `WeaponConfig` numbers. Casings live on the Ignore Raycast
  layer so a tumbling casing never eats a bullet, and `_hitMask` defaults to
  `Physics.DefaultRaycastLayers` to match.
- **One trigger pull is one shot, however many pellets it throws.** Each pellet
  gets its own ray, its own `ResolveHit` and its own damage; the follow-up drain,
  the already-hit set, the follow-up hang guard and the hitmarker are all scoped
  to the *pull*. See "Pellet scoping" below — this was per-pellet until 2026-08-12.
- `Physics.RaycastNonAlloc` into a pre-sized `RaycastHit[32]`, sorted in place by
  an insertion sort — no allocation in the firing path, and the sort only matters
  once Pierce lets one ray resolve several targets in order.
- Damage goes through `IDamageable`, so the weapon has no enemy-specific code.
- `EffectiveSpreadDegrees` is public so the crosshair can visualise the exact
  cone the raycast will use — movement, crouch and airborne multipliers included.
  Bloom the player cannot see is bloom that only feels like bad luck.
- The controller pushes its ADS progress into `WeaponSway` rather than the sway
  polling it, so there is one owner of the blend.

## Effect modules — the "without limits" engine

A weapon's real behaviour is `WeaponConfig` **plus an ordered list of
`EffectModule` assets**. Stacking is the product: a rifle with Pierce and Chain
is that list with two entries, not a new class. The shop sells them, and they are
installed on the **runtime** list — never appended to the config asset, which
would edit authored data that survives into the next Play session.

| Module | What it does | Shape |
| --- | --- | --- |
| [Explosive.cs](../../Assets/_Project/Scripts/Weapons/Explosive.cs) | every hit detonates for a fraction of the shot | queues Damage follow-ups |
| [Pierce.cs](../../Assets/_Project/Scripts/Weapons/Pierce.cs) | passes through bodies, losing damage per target | changes the **ray budget** |
| [Ricochet.cs](../../Assets/_Project/Scripts/Weapons/Ricochet.cs) | bounces off the surface it hit | queues Ray follow-ups |
| [Chain.cs](../../Assets/_Project/Scripts/Weapons/Chain.cs) | jumps to nearby untouched targets | queues Damage follow-ups |

### The three rules

1. **Modules are stateless.** One asset is shared by every weapon carrying it, so
   all per-shot state lives on the weapon: the already-hit set, the overlap
   buffer, the follow-up queue.
2. **Modules never apply damage.** They enqueue follow-ups and the weapon applies
   them. Double-dip prevention, the already-hit set and the depth counter then
   live in exactly one place instead of four.
3. **A module runs at depth 0 only, unless it opts in with `maxDepth`.**
   Follow-ups resolve at `depth + 1`. Without this rule
   Explosive → Chain → Explosive never terminates. Explosive, Ricochet and Chain
   ship at `maxDepth 1` — they react to each other exactly once, deliberately.

**Pierce is the exception that proves the shape.** It has no after-effect at all:
`Resolve` is empty, and it works by contributing `ExtraRayBudget` and
`PierceDamageFalloff`, which the weapon reads *before* the cast. Ricochet and
Chain work through the aftermath; a piercing bullet has to keep going during the
same cast.

Two independent bounds stop a mis-authored module from freezing a frame: the
`FollowUpBuffer` has a fixed capacity (dropped work is a missing spark), and
`DrainFollowUps` caps iterations at `MAX_FOLLOW_UPS_PER_PULL` (96) **per trigger
pull** — not per pellet, which is what it used to be. The depth rules are the
real limit; those are the seatbelt.

## Targets

The grey-box dummy (`Target_Dummy` prefab) is the weapon's test bench: a body
with `Health` and `HitFlash`, a `Head` child whose collider carries a
[Weakpoint](../../Assets/_Project/Scripts/Core/Weakpoint.cs) relay, and a
[TargetRespawn](../../Assets/_Project/Scripts/Core/TargetRespawn.cs) that hides
a dead target and pops it back up after `HealthConfig.targetRespawnSeconds` —
so a tuning session never runs out of things to shoot. Drones will NOT respawn;
they despawn through the pool.

## Audit fixes (2026-08-11)

Six defects in the fire path, all silent — the gun kept firing through every one
of them.

- **A ricochet could kill the player who fired it.** `ApplyFollowUpRay` damaged
  whatever Health the bounced ray found first, with no owner check, while
  Explosive and Chain had both refused `OwnerHealth` from the start. Bounce off
  the wall you are standing against and the round came home.
- **One bullet hit one drone twice.** Every drone puts two colliders on the line —
  the hull, which carries the Health, and the `Core` child, whose Weakpoint relays
  to it — and the pierce loop resolved both, applying a headshot AND a body shot
  and spending two of the pierce budget on one body. `ResolveHit` now returns
  `HitOutcome.AlreadyPierced` for the second, which costs neither damage nor budget.
- **A held trigger destroyed every reload.** `Update` starts the reload and reaches
  `TryFire` in the same frame, so the cancel branch ran at elapsed 0 — far below
  the commit point — and killed it. Tapping R with the trigger down did nothing at
  all; the gun could only be reloaded by running it dry. Cancelling now takes a
  fresh press.
- **Fire rate was a function of frame rate.** `NextShotAllowedAt` was scheduled
  from the frame that noticed the shot rather than the shot that was due, rounding
  every round up to a whole frame: 700 RPM fired at 600 on a 60 Hz display. It now
  carries the remainder forward while firing on cadence, and restarts from now
  after a pause — no catch-up burst.
- **Decals were stamped on drones.** 20 s lifetime, ~12 rounds a second, against a
  pool prewarmed for 48; and a decal on a drone is spawned into the world, not
  parented to it, so the drone died and its bullet holes hung in mid-air. Bodies
  get the spark, walls get the hole.
- **Buffers sized for the typical case, not the authored one.** `RaycastNonAlloc`
  and `OverlapSphereNonAlloc` return an arbitrary subset when full and report no
  overflow, so a short buffer does not clip the far end of a line — it silently
  drops the wall. Ray buffer 16 to 32 (a full Pierce budget is 9 bodies x 2
  colliders plus the wall), effect overlap 24 to 64. Chain and Explosive now log
  when they fill it, as `Blast.Apply` already did.
- **Explosive never claimed its victims.** Chain marks a target the moment it
  queues one; Explosive did not, and the shipped asset has `maxDepth: 1` — so each
  blast victim detonated again and re-found the same neighbours, putting several
  full blasts on one drone from a single round.

`dryFireCooldown` and `muzzleFlashLifetime` moved from literals in
`WeaponController` onto `WeaponConfig`, where every other number on that path
already lived.

## Pellet scoping: the pull and the pellet (2026-08-12)

**A latent bug, fixed before the weapon that would have triggered it exists.**
Every buffer in the fire path whose comment said "per shot" was in fact scoped
**per pellet**: `CastOneRay` ended by calling `DrainFollowUps`, and
`DrainFollowUps` ended by clearing both the follow-up queue and the already-hit
set. At `pelletsPerShot: 1` — both shipped weapons — the two scopes are the same
object, which is why nothing ever surfaced. At 12 they diverge badly:

- **One pull paid twelve times for one effect module.** Explosive and Chain both
  work by asking the weapon "have you already hit this one?"; the set was wiped
  between pellets, so all twelve got a fresh "no". Twelve detonations, twelve
  chains, each free to re-hit what the pellet before had claimed.
- **The hang guard multiplied by the pellet count.** `guard` is a local inside
  `DrainFollowUps`, so a 96-follow-up ceiling became 1152 per pull — precisely
  the frame freeze the constant exists to prevent.
- **Twelve `Hit` events.** `Hitmarker` does a `PlayOneShot` per event: twelve
  clicks stacked in one frame under one punch animation. It already carried a
  workaround for a plain pellet overwriting a sibling pellet's kill confirmation.
- **A follow-up could cancel a later pellet's damage.** Follow-ups drained
  between pellets and marked their victims, and `ResolveHit` read the same set to
  skip a body's second collider — so a blast from pellet 1 made pellet 2's direct
  hit on that drone deal nothing at all.

The drain, both clears and the follow-up budget now live in `FireOneShot`, one
level up, so their scope is **one trigger pull**. The clears happen at the START
of the pull rather than the end, so an early return cannot leak marks into the
next one.

**Two sets, not one.** The hull/Core de-duplication (every drone puts two
colliders on the line, and only the hull carries `Health`) is a genuinely
per-*ray* concern and moved to its own `_piercedThisRay`, cleared at the top of
`CastOneRay`. Point `ResolveHit` at the per-pull set instead and a twelve-pellet
shotgun deals one pellet of damage, because pellet 2 reads pellet 1's mark and
passes straight through. `HitOutcome.AlreadyPierced` still means exactly what it
meant: *this ray* already went through this body.

**`Hit` is raised once per pull**, by an accumulator (`RegisterHit`) that every
damage path funnels through — primary, follow-up damage, follow-up ray. The kill
flag is sticky: if any pellet or any follow-up killed, the pull killed, which is
what the player actually did. `Hitmarker` and `Crosshair` needed no change;
`Crosshair` never subscribed to `Hit` at all.

Covered by
[PelletScopingTests.cs](../../Assets/_Project/Tests/PlayMode/PelletScopingTests.cs)
(PlayMode — the fire path needs a physics scene, a real aim ray and a live
`Health`, none of which EditMode can reach). It equips a synthetic 12-pellet
`WeaponConfig` with a zeroed cone, installs a stand-in module targeting a
collider-less bystander, and asserts one payment, one hitmarker, a sticky kill
flag, and twelve pellets' worth of damage on the primary target. It drives
`FireOneShot` by reflection because a headless run has no input device to press.

## Attachments — the second data pattern, and why it is not the first

An attachment is a `AttachmentConfig` asset composed into `WeaponConfig`, and it
is **deliberately not an `EffectModule`**. That distinction is the whole design:

| | `EffectModule` | `AttachmentConfig` |
| --- | --- | --- |
| What it is | a **behaviour hook** — code that runs on an impact | a **stat delta** — numbers |
| Adding one | a new C# class with a new `Resolve` | an asset |
| Stacks | yes, ordered, with depth rules | one per slot, replaced not stacked |
| Where it lands | the follow-up queue | `WeaponRuntime.Stats` |

Routing attachments through the module pattern would have meant a class per
attachment, and seven slots × a handful of options each is exactly the
combinatorial mess this project exists to avoid.

**Slots**: `Optic · Muzzle · Barrel · Underbarrel · Magazine · Stock · Ammo`.
One per slot; fitting a second optic replaces the first rather than folding in
underneath it.

**`allowedClasses` is `weaponClass`'s second real reader.** Before this, the
field decided which balance law a weapon answered to and nothing else. `TryFit`
**refuses** an attachment that does not suit the class rather than fitting it and
doing nothing — so a shop can decline the sale instead of charging for an optic
the player will never see work.

### The shipped set

| Asset | Slot | Fits | What it does | What it costs |
| --- | --- | --- | --- | --- |
| `Attach_Scope_Long` | Optic | **Sniper only** | ADS FOV ×0.42 (0.48 → **0.20**, ~5x), sensitivity ×0.45 | ADS time ×1.25 |
| `Attach_Grip_Angled` | Underbarrel | any | hip bloom ×0.82, horizontal recoil ×0.75 | ADS time ×1.08 |
| `Attach_Mag_Extended` | Magazine | any | magazine ×1.5 | reload speed ×0.85 |
| `Attach_Suppressor` | Muzzle | any | vertical recoil ×0.9 | range ×0.85 **and** damage ×0.95 |
| `Attach_Stock_Heavy` | Stock | any | vertical recoil ×0.7 | ADS time ×1.15, sprint-to-fire ×1.2 |

Not one of them is a straight upgrade. An attachment with no downside is not a
build decision, it is a patch note.

Only the scope ships fitted, on the sniper. The other four are fitted to nothing
on purpose — bolting them to guns nobody has fired would retune those guns before
anyone has judged them. They are reachable from the sandbox console instead
(see below).

### `WeaponStat` is not `Stat`, and that is the most important line

`CoD.Core.Stat` is the **passive** sheet: five values describing the player, whose
`StatExtensions.Count` sizes two arrays inside `StatSheet` that `RunContext`
rebuilds on every purchase and that `PlayerMotor` and `WeaponController` read
every frame. Adding eleven weapon values to it would resize those arrays, widen
the shop's modifier surface to numbers no passive should reach, and put the whole
passive pipeline in the blast radius of a scope's zoom level.

So: two enums, two sheets, same pipeline — `(base + flats) × mults`, rebuilt from
scratch whenever the fitted set changes. They meet in exactly two places on
purpose: **reload speed** and **damage** are multiplied by both, because "the
player reloads faster" and "this magazine reloads faster" are different claims
that genuinely compose.

⚠️ **`WeaponStat` has no fire-rate entry and must never grow one.** Cadence is
scheduled off `Config.SecondsPerShot` — the AUTHORED number — and
`WeaponCadenceRegressionTests` pins the overshoot arithmetic that keeps a 700 RPM
rifle firing at 700 RPM rather than at whatever the player's monitor rounds it
to. A `FireRate` attachment stat would move that schedule onto a runtime value
the regression test does not exercise, and rate of fire is one half of the
time-to-kill the whole game is tuned around. `AttachmentTests` asserts the enum
never grows one; a weapon that should fire faster is a new weapon.

### What reads the runtime instead of the config

Every effective value lives on `WeaponRuntime` and gameplay reads it there. The
config keeps the authored answer, which is what `OnValidate`, `WeaponDataTests`
and `ArsenalBuilder`'s gate are written against — a balance law measured against
a scope somebody fitted would not be a law.

`Damage` · `AdsTime` · `ReloadSpeedMultiplier` · `RecoilVerticalMultiplier` ·
`RecoilHorizontalMultiplier` · `HipSpreadMultiplier` · `MagazineSize` ·
`AdsFovMultiplier` · `AdsSensitivityMultiplier` · `SprintToFireTime` ·
`DamageAtDistance(m)`.

Three of those have a floor or a clamp at the read site rather than in the sheet,
because the unit is only known there: ADS time floors at 0.03 s (a weapon that is
permanently aimed is broken, not fast), magazine size rounds and floors at 1, and
the two ADS multipliers clamp to a usable band.

⚠️ **The recoil multipliers are applied to the KICK, never to the pattern.**
`RecoilPattern` is seeded and deterministic — the same seed always produces the
same climb, which is what makes recoil learnable — so a stock that reached inside
it would change the SHAPE of a pattern the player has memorised rather than its
size.

⚠️ **`CurrentAmmo` is seeded AFTER the authored attachments are fitted.** An
extended magazine changes what a full magazine is; seeding first and rebuilding
later would start every run with a 45-round magazine holding 30 rounds, which
reads as "the gun is not reloading properly".

### What W5 deliberately did not build

- **A scope OVERLAY image.** The sniper's 5x is a real FOV change and a real
  sensitivity change; the black-surround picture that would sell it is UI work
  and belongs with G6's `Sight_Glass`. A render-texture scope stays refused —
  it renders the world twice.
- **Hold-breath.** It needs a new input action, a stamina float and a sway
  multiplier, and the sway numbers are the nine serialized fields G6 is about to
  move into a `ViewmodelConfig`. Building it now means writing it twice.
- **A shop row.** Attachments are not for sale yet, for the same reason the five
  new weapons are not: shop odds are one of the things the tuning card asks
  about, and changing them would spoil that answer before it is given.

## Reaching the arsenal at all (2026-08-12)

Until W4, the game could put exactly **two** of its weapons in a player's hands:
the rifle the loadout starts with, and the SMG the shop sells. The pistol, the
marksman rifle, the LMG, the shotgun and the launcher were authored,
balance-gated, covered by tests and **unreachable by any human being** — and a
weapon nobody can hold cannot be judged, which is the one thing this project
still needs from a person.

The sandbox cheat console's **digit 0** now walks `WeaponRegistry.allWeapons`,
wrapping at the end and stepping past empty slots. It goes through
`WeaponController.EquipWeapon` — the same call the shop makes — so bought effect
modules carry across exactly as they do in a real run, and the cheat exercises
the shipping path rather than a private one. The registry reference is wired by
`GreyBoxBuilder` and re-asserted and checked by `GreyBoxVerify`; missing it is a
warning at build time, not a failure, because `ArsenalBuilder` runs after the
grey box and on a first-ever build the asset genuinely does not exist yet.

**MINUS** fits the next attachment that suits the weapon in hand, wrapping and
skipping anything the class rule refuses — so the long scope is silently passed
over on everything but the sniper. It goes through `WeaponRuntime.TryFit`, the
same call a shop would make.

**None of the six new weapons and none of the attachments is in the shop**,
deliberately. Shop odds are one of the things the tuning card asks about (item
3), and adding to the pool would change that answer before anyone has given it.

## Related Systems

- [pooling.md](pooling.md) — every muzzle flash, casing, decal and spark.
- [player.md](player.md) — supplies input, aim ray, motion state, and takes recoil.
- [shop.md](shop.md) — sells the effect modules and the damage/reload passives.
- [drones.md](drones.md) — what the modules are aimed at.

## Gotchas

- `CoD.Weapons` **must** reference `CoD.Player` in its asmdef. It did not, and it
  would have failed on first open; the type-check now catches this class of error.
- `pelletsPerShot > 1` fires N rays from one bloom value — and **bloom is zero
  while aiming**, so an aimed `SG_Breacher` puts all twelve pellets on one point
  for 120 damage in a single ray. There is no `pelletSpreadDegrees` field yet;
  see "Does 'a weapon is data' hold?" for the one-field, one-line fix. Each pellet
  re-rolls the cone, and nothing else in the fire path is per pellet: read "Pellet
  scoping" before adding anything to `CastOneRay`.
- **Every TTK number in this doc is point blank.** `TimeToKill` takes no range;
  `TimeToKillAtRange` is the one that charges for falloff, and for anything past
  `falloffRange.x` it is the number that matters.
- **`WeaponClass` is APPEND-ONLY.** Unity serialises an enum as its integer
  value, so inserting a member re-classes every asset authored after it — an AR
  that quietly becomes a Shotgun is held to a different balance law and leaves no
  import error behind.
- `ShotsToKill` is floored at **1**. It used to be able to return 0, and a TTK of
  0 is below every floor forever, which is what made a sniper unauthorable.
- **`weaponClass` is not a way out of the TTK window.** `Sniper` and `Launcher`
  are exempt only while `ShotsToKill() == 1`, and both `OnValidate` and
  `WeaponDataTests` check it *before* the re-engagement floors. Setting the class
  on a four-pull weapon does not exempt it; it makes it fail loudly.
- **A weapon is inside the balance gate because its asset EXISTS**, not because
  it was added to `Weapons.asset`. The folder scan always runs, and the registry
  is cross-checked against it in both directions rather than replacing it — the
  moment one is preferred over the other, a forgotten registry entry is a weapon
  with no balance law at all.
- Feedback prefabs on `WeaponConfig` must be registered in the pool prewarm list
  or the first shot allocates. The four new weapons deliberately reuse the
  rifle's `Fx_MuzzleFlash` and `Fx_ShellCasing`, which are already prewarmed;
  giving one its own flash prefab means a new pool entry in the same commit.
- **`ArsenalBuilder` never creates `AR_Standard` or `SMG_Rapid`.** It loads them
  and throws by name if they are missing. A second AR authored here would be a
  second file with a second `stableId`, and the loadout would point at whichever
  one was written last.
- **Renaming a path in a builder does not rename the asset.** `LoadOrCreate`
  configures on create only, so a renamed path creates a fresh default file,
  discards every tuned value in the old one, and reports success.
- **The already-hit set is cleared per trigger pull, not per pellet and not per
  frame.** Leaving it would make chains stop working after the first shot;
  clearing it too early lets one pull pay for the same target once per pellet.
  There are now TWO sets and they are not interchangeable: `_alreadyHit` is the
  per-pull one modules read through `HasHit`/`MarkHit`, and `_piercedThisRay` is
  the per-ray one that skips a body's second collider.
- A pierce budget is spent on **bodies only**. `ResolveHit` returns whether it
  damaged something, and the cast stops at the first thing that is not
  damageable — otherwise a piercing round shoots through the arena wall.
- `_hitBuffer` is **32** entries because a piercing round has to find several
  bodies *and* the wall behind them in one cast: a full Pierce budget is 9 bodies
  x 2 colliders each, plus the wall. `RaycastNonAlloc` returns an arbitrary
  subset when the buffer fills and reports no overflow, so a short buffer does
  not clip the far end of the line — it silently drops the wall.
- Verified in play: firing, ammo, HUD and audio, **on the rifle only**. NOT yet
  verified: all four new weapons, damage falloff at range, shotgun pellet spread,
  reload cancelling, burst mode, headshots on the dummy's head, casing ejection
  arcs, and target respawn — all awaiting a grey-box rebuild plus a play test.
  Four weapons that satisfy their laws on paper is not four weapons that feel
  different; the pistol's 6.7-clicks-a-second and the LMG's 70% recoil recovery
  are the two most likely to need a retune the moment anyone fires them.

## Sandbox module depth and resupply (2026-08-11)

**Not verified in play.**

`EffectModule.maxDepth` capped stacking everywhere, including the mode whose whole
purpose is play without consequences. `GameConfig.sandboxExtraEffectDepth` (1)
grants Sandbox one extra level; a Run always gets zero.

The bonus shifts the depth the module is **asked** about —
`module.RunsAtDepth(context.Depth - _extraEffectDepth)` — rather than the maxDepth
it declares. maxDepth lives on a shared config asset and Domain Reload is off, so
writing to it would rewrite the shipped balance for every Play session afterwards.

`MAX_FOLLOW_UPS_PER_SHOT` and the fixed-capacity `FollowUpBuffer` are untouched.
They are the hard backstop that makes deeper recursion a bigger effect rather than
a frozen frame, and they are the whole reason it is safe to let Sandbox off the
leash at all.

Resolved in `Start`, not `Awake`: `RunContext` reads the save in its own `Awake`
and `Mode` comes from that save, so a frame earlier would depend on undefined
script execution order. It reads `RunContext.Config` rather than carrying its own
serialized `GameConfig` — every extra asset reference in a scene is another one
that can come back `{fileID: 0}` after a save and fail silently.

**`WeaponController.RefillReserve(fraction)`** tops the held weapon's reserve up by
a fraction of its CONFIG reserve, so one consumable asset stays correct across both
weapons. It returns false when there is nothing to add, which is what lets the shop
refuse the sale instead of taking money for nothing — see [shop.md](shop.md).
