# Graphics like Call of Duty, and a real arsenal — the executable plan

> Companion to [PLAN-CAMPAIGN.md](PLAN-CAMPAIGN.md), which covers missions and
> human enemies. This one covers **the image** and **the guns**, and it is
> written to be executed rather than admired: every phase names the files, the
> numbers and the gate.
>
> Written 2026-08-12, after the pass that fixed six invisible render defects and
> the viewmodel camera. It supersedes the thinner Track G / Track W sections in
> the campaign plan.

## 1. What "like Call of Duty" actually means, and what it does not

The instinct is that CoD looks good because of polygon counts and 4K textures.
That is the one thing this project **cannot** copy — a 4 GB laptop and no
artist — and also the least important. Strip a modern military shooter down and
the impression is made by five things, in this order:

| | What creates the impression | Where this project stands |
| --- | --- | --- |
| 1 | **The gun on screen** — it is 30% of every frame, forever | ✅ own camera, no wall clipping, no FOV warp. Still 8 grey cubes. |
| 2 | **Impact feedback** — what happens when you shoot a thing | ⚠️ sparks + a flat quad. No tracers, no decal projection, one surface type. |
| 3 | **Light and grade** — contrast, colour separation, a coherent look | ✅ HDR grade, cool/warm split, SSAO, fog. ❌ no reflections, no cookies. |
| 4 | **Audio** — arguably half the perceived production value | ⚠️ mixer + 18 retained CC0 clips now cover footsteps, ambience, impacts, enemies and UI; firearm recordings, reverb effects and music remain. |
| 5 | **Geometry and texture** — the thing everyone thinks is #1 | ❌ 100% primitives. One generated texture. |

**Items 1–4 are almost entirely code and discipline. Item 5 is the only one that
needs money, and it is last for a reason.** A grey box with great impacts,
great light and great audio reads as stylised. A detailed box with flat light
and placeholder audio reads as broken.

So: **the whole of Track G below is done before a single dollar is spent.**

## 2. The honest ceiling

**Stylised-realistic. Not photoreal.** Synty-tier shapes, a disciplined grade,
strong VFX and audio, everything readable at 1080p. That will read as a
competent indie military shooter.

Chasing photoreal on this hardware, with this budget, produces *mismatched*
fidelity — a 4K crate beside a flat-shaded wall — which looks **worse** than an
honest grey box. This is the single most common way a solo project's art dies.
Commit to one visual language and buy only inside it.

## 3. Where the image stands today, measured

- **30 materials, 1 texture** (procedurally generated), 0 imported meshes.
- Forward+, HDR, SSAO (intensity 0.7, radius 0.5, half-res), SMAA.
- Post: Neutral tonemap, bloom, vignette, film grain, **plus** cool-shadow /
  warm-highlight split, white balance −6, lift, chromatic aberration — all of
  which fold into the 32³ grading LUT and therefore cost **zero extra ms**.
- Viewmodel renders on its own overlay camera with its own FOV and near clip.
- Two muzzle lights on one clock — one for the room, one for the gun.
- `guard-texture-budget` and `guard-lfs-budget` now make the 1024 rule and the
  LFS quota **enforceable** rather than aspirational.

Render targets at 1080p total ~110–130 MB. **The 4 GB budget is not binding
until textures arrive**, and then it binds hard: twenty 4K albedo/normal pairs
is ~900 MB — a quarter of the card — for detail invisible on a 3 m wall.
The same twenty at 1024 is ~43 MB.

---

# TRACK G — the image

Ordered by **impression per hour**, not by category. Each phase is independently
shippable and independently revertible.

## G3 — Impact response *(the biggest remaining free win, ~3–4 sessions)*

Shooting a wall currently produces a spark burst and an orange-ish quad. In a
shooter, impact is the feedback loop — it is what makes the gun feel connected
to the world.

**G3a — Tracers.** There are none at all today.
- New pooled `Fx_Tracer` prefab: a `TrailRenderer` (allocation-free), spawned in
  `WeaponController.SpawnMuzzleEffects`, flying muzzle→hitpoint at ~250 m/s.
- New `WeaponConfig` fields: `tracerEveryNRounds`, `tracerSpeed`, `tracerWidth`.
- **1 in 3 rounds, never every round.** Every-round tracers read as a laser show
  and destroy the muzzle flash's punch.
- Call `TrailRenderer.Clear()` on spawn — a pooled trail keeps its old points.

**G3b — Real decals.** Add the **Decal Renderer Feature** to `PC_Renderer.asset`.
- **Screen Space, not DBuffer.** DBuffer costs a depth prepass plus 2–3
  fullscreen targets for normal-blended decals nobody will notice on flat walls.
- Replace `Fx_ImpactDecal`'s lit quad with a `DecalProjector` + URP's stock
  `Shader Graphs/Decal` material. One 512 atlas, 4 bullet-hole variants, ~0.35 MB.
- Max draw distance ~30 m. Stays pooled exactly as now.

**G3c — Per-surface response.** `ImpactConfig` is one surface and says so in its
own comment.
- New `SurfaceType { Concrete, Metal, Grate, Glass, Flesh }`.
- `ImpactConfig` becomes a table: decal + particles + **sounds** per surface.
  `impactSound` has existed since the file was written and is read by nothing.
- **Key on `hit.collider.gameObject.layer`** — an int field read, allocation-free
  and guard-clean. `GetComponent<SurfaceTag>()` from a hit resolved in `Update`
  would trip `guard-no-find-in-update`. Costs 2–3 layers in `TagManager`.
- `Flesh` is where the blood impact lands when humans arrive, which makes gore
  level a **data swap** and a reduced-blood accessibility option a second asset
  rather than a code branch.

**G3d — Muzzle flash quality.** Currently one untextured quad.
- 4-frame flipbook on the additive material, random roll (already there), plus a
  stretched "star" quad and a small smoke puff on the last round of a burst.

**G3e — Heat shimmer, nearly free.** `_CameraOpaqueTexture` is already enabled
and currently paid for by nothing. One `.shadergraph` sampling it with a UV
offset gives real distortion on explosions. This will be the project's first
shader; `.shadergraph` is JSON and commits fine — add `*.shadergraph text eol=lf`
to `.gitattributes` at the same time.

**Gate:** `HordeLoadTests`' 16 KB/frame allocation budget is the real risk here
(current headroom ~450 B). Tracers are the allocation suspect — measure.

## G4 — Light and reflections *(~2–3 sessions)*

**G4a — Reflection probes. The single biggest material win available.**
Four baked box probes (three lanes + the bunker) at resolution 128, baked from
the builder via `Lightmapping.BakeReflectionProbe` so the builder stays the
single source of scene truth. ~1 MB each. This is what stops `Weapon_Body`
(metallic 0.85) and `Drone_Hull` (metallic 0.75) being featureless — the session
that fixed the sky reflection only stopped the *wrong* answer; this supplies the
right one.

**G4b — Point lights become spot lights with cookies.** Four point lights at
4.2 m read as floating orbs. Spots aimed down with a 256 gobo each read as
ceiling fixtures. The cookie atlas is already sized 2048 and costs nothing extra.
⚠️ **This deliberately breaks `RenderingTests.Arena_IsLit`**, which counts
`LightType.Point`. Update it in the *same commit* to count "enabled,
non-directional, not parented under a Camera", with a comment. Do not work
around it silently.

**G4c — Emissive light strips** above each lane, extending the existing `AddTrim`
pattern, keeping the palette rule (cool = architecture, warm = threat).

**Refuse:** a custom height-fog fullscreen pass. URP has no built-in height fog
and the payoff does not justify a hand-maintained fullscreen shader.
**Optional, last, measured:** Adaptive Probe Volumes for real bounce light —
genuinely the biggest lighting upgrade available, at the cost of a bake step and
10–40 MB of VRAM.

## G5 — Audio *(~3–4 sessions, and worth more than it looks)*

The implementation now has a mixer, footsteps, ambience and an optional 18-clip
CC0 kit. Music, recorded firearm layers, reverb effects and audible tuning remain;
this is still plausibly the largest gap between this and a commercial shooter.

- **`Master.mixer` is the one asset the builder cannot generate** —
  `AudioMixerController` is internal and has no public creation API. Author it by
  hand once, commit it, and **document that exception loudly**, or the next
  session will assume the builder owns it and rebuild around a missing asset.
- Buses: `Master → SFX (Weapons / Impacts / Enemies / World)`, `UI`, `Music`,
  `Ambience`. `SettingsHub.Apply` stops writing `AudioListener.volume` and writes
  an exposed param instead.
- **Reverb via a mixer send, not `AudioReverbZone`s.** One SFX→Reverb send at
  ~−12 dB with a small-hall preset. This single change is what makes the rifle
  sound like it is *inside a facility* — cheaper and far more controllable than
  per-lane zones.
- **Footsteps**: a distance accumulator driven from `PlayerMotor.HorizontalSpeed`,
  one raycast **per step**, never per frame. Clips and cadence in a
  `FootstepConfig` asset.
- **Ambience**: one 2D room tone + 2–3 spatialised loops. The cheapest "this is a
  real place" signal that exists.
- **Weapon layering**: `fireCloseLayer` / `fireTailLayer` already exist; add a
  mechanical layer and ±3% random pitch.
- ⚠️ **Audio is the sneaky LFS killer** — 44.1 kHz stereo WAV is ~10 MB/minute.
  Mono for everything spatialised; stereo only for music and ambience.

## G6 — Viewmodel feel *(~3–4 sessions)*

The camera split fixed the *rendering*. This fixes the *motion*.

- **Move `WeaponSway`'s nine pose/sway/bob fields into a `ViewmodelConfig`
  asset.** They are tuning numbers living on a MonoBehaviour — the one place the
  project's most important rule does not currently hold.
- **Weapon lower on wall proximity.** One `SphereCastNonAlloc` forward from the
  muzzle per frame with a cached buffer; within ~0.6 m blend to a port-arms pose.
  The camera stack fixed the rendering half of clipping; this is the gameplay
  half, and shooters do both.
- **Inspect** on an idle timer. **Reload and ADS stay procedural** — curve-driven
  poses, no Animator, no imported animation. `AnimationCurve` fields on the SO
  are legal tunables.
- **A reflex sight** — a `Sight_Glass` quad with an additive emissive reticle.
  ⚠️ **Refuse a render-texture scope for v1**: a 512 RT scope renders the world
  twice. Reflex and holo sights need no RT; only a sniper scope does.
- **Screen-space damage feedback via a runtime-instanced `Volume`.** Read
  `volume.profile` (which clones once) — **never** `sharedProfile`, because
  Domain Reload is off and the write would permanently rewrite the shipped asset.

## G8 — The art seam ✅ *(built 2026-08-13; **BUILT BEFORE DOWNLOADING OR SPENDING ANYTHING.**)*

A 2951-line editor script generates every scene from primitives. The question
"how does bought art coexist with that" has exactly one good answer:

**`GreyBoxBuilder` keeps owning the scene. Art becomes data.**

New `ArenaKitConfig` / `WeaponKitConfig` / `EnemyKitConfig` assets holding
*optional* prefab and material references. `BuildRoom`'s ~30 `AddBox` calls
become `AddBlock(..., kit.wallModule, kit.wallMaterial)`, where `AddBlock`:

1. **always** creates the box collider from the same position and scale;
2. instantiates the art prefab as a child named `Art`, **every collider stripped**;
3. falls back to `CreatePrimitive(Cube)` when the kit field is null.

**The load-bearing rule: collision and navmesh come from the box; art comes from
the prefab. Art never changes gameplay geometry.** Consequences that make this
worth the two sessions:

- The navmesh bake is byte-identical with or without art, so pathing cannot break.
- Every gameplay test — hitscan, pooling, `HordeLoadTests` — is unaffected by an
  art swap.
- **It is reversible.** Null the kit fields, rebuild, and you are back to a
  shippable grey box. If the art never arrives, you still have a game.

`GreyBoxVerify` gains a `VerifyKits()` enforcing **all-null or all-non-null per
kit** — a *mixed* kit is the real failure mode, because it produces a scene that
looks half-built and verifies clean.

Also: committed `TextureImporter` / `ModelImporter` presets and an
`ArtImportPostprocessor` that stamps settings by folder. **This is how the 1024
rule becomes automatic**, and it is what stops one $60 pack silently importing
forty 4K textures.

**Acceptance criterion: the entire suite passes with every kit field still null.**

## G9 — Buying, finally

**Free first, and it is not a consolation prize.**

| Source | Licence | What it gives |
| --- | --- | --- |
| **ambientCG** | CC0 | ✅ 2026-08-13 — ten 1K industrial surfaces integrated; Color + NormalGL only, 19.9 MB measured texture-memory delta. |
| **Poly Haven** | CC0 | ✅ 2026-08-13 — Autoshop 01 imported as a 128 px linear specular cubemap; procedural sky remains visible, 0.2 MB measured texture-memory delta. |
| **Kenney** | CC0 | ✅ 2026-08-13 — 18 retained clips for footsteps, impacts, ambience, enemies/explosions and interface feedback; 0 MB VRAM, 0.8 MB measured audio memory. |
| **Sonniss GDC bundle** | royalty-free | Free annually, hundreds of GB of gun tails, impacts, room tone. **Keep it entirely outside the repo**; export trimmed clips only. |
| **Unity Particle Pack** | free | URP-compatible VFX. |

**Then paid, one pack at a time, each its own commit with its own VRAM
measurement. Never import two packs before measuring one** — that is how you end
up unable to attribute a 400 MB jump.

| Priority | Item | Cost |
| --- | --- | --- |
| 1 | **One Synty POLYGON environment pack** (Sci-Fi / Military). The whole pack shares one atlas, so VRAM is near zero and everything batches. This one decision is what makes the 4 GB constraint a non-issue. | $30–60 |
| 2 | **A first-person weapon set.** On screen 100% of the time; CLAUDE.md already permits 2048 here. **Stay inside the same visual language** — a photoreal AR against flat-shaded walls looks broken in a way neither asset looks broken alone. | $20–40 |
| 3 | **A URP VFX pack.** ⚠️ Verify URP compatibility *before* buying — a large fraction are Built-in RP only and render magenta. | $25–45 |
| 4 | Audio, only if something is still missing after G5. | $0–40 |
| — | **Hold $60–100 in reserve** for the thing you discover you need. | |

**Do not buy:** photoreal "AAA environment" packs (4K blows VRAM *and* clashes),
anything HDRP-only, anything needing Amplify/Shader Forge, or any "Ultimate FPS
Kit" — it would fight `WeaponController`, the pool, every guard and
`GreyBoxVerify` simultaneously. **Wait for a sale; the same $200 buys twice as
much.**

---

# TRACK W — the arsenal

The weapon system is in unusually good shape for this: the TTK model was
rebuilt to account for pellets, burst pauses and range; the balance law is now
**per class** with the one-shot exemption *asserted* rather than granted by an
enum; `WeaponRegistry` cross-checks against a folder scan so weapon seven cannot
escape the gate; and the pellet-scoping bug that would have made a shotgun
detonate twelve times per pull is fixed.

**That means the next three guns are pure data.** Which is the claim the whole
architecture was built to make, so ship them first and prove it.

## W3 — The guns

| Order | Weapon | What it needs beyond a `WeaponConfig` asset |
| --- | --- | --- |
| 1 | **Pistol** | **Nothing.** Pure data. Ship it first — it is the proof. |
| 2 | **Marksman** | **Nothing.** `fireMode Single`, high damage, `headshotMultiplier 2.0`. |
| 3 | **LMG** | Nothing hard. `magazineSize 100`, long `reloadEmptyTime`. Worth upgrading `RecoilPattern`'s two-point line to an `AnimationCurve` — thin for a 100-round burst, and curves are already an established pattern in `DifficultyConfig` and `ShopConfig`. |
| 4 | **Shotgun** | `pelletSpreadDegrees` — a *fixed pattern* cone, distinct from bloom (`CurrentSpread` is bloom, pattern is geometry). Plus a much steeper falloff, e.g. `falloffRange (6, 16)`. The per-pull scoping it needs already landed. |
| 5 | **Sniper** | A scope overlay (W5), hold-breath (an input action + a stamina float + a sway multiplier), and a bolt-cycle time distinct from `roundsPerMinute`. Note `adsFovMultiplier` bottoms at 0.2 → 62° × 0.2 ≈ 12° vertical ≈ 5× zoom, plenty for a 40 m arena. |
| 6 | **Launcher** | **Projectiles.** See W4. |

⚠️ Each new weapon must be added to `WeaponRegistry`, or the cross-check fails
loudly — which is the point. And the sniper/launcher must genuinely one-shot,
because that premise is now asserted.

## W4 — Projectiles, for the launcher

**Do not write one.** A working swept-ray pooled projectile already exists:
`Enemies/DroneProjectile.cs` — no collider, sweeping between frames because a
small fast trigger tunnels through walls at any sane physics step, and covered by
`DeathAndProjectileTests`.

**Promote it to `Core/Projectile.cs`** (legal: `CoD.Core` references nothing, and
both `Enemies` and `Weapons` reference Core). Add an `owner` so a rocket cannot
kill the shooter. `WeaponConfig` gains `DeliveryMode { Hitscan, Projectile }` and
`FireOneShot` branches once.

⚠️ **The one genuinely invasive part**: a launcher must still run effect modules,
so the projectile's impact has to reach `ResolveHit` — hand it an
`IProjectileImpactSink` (an interface reference, no allocation). And **carry the
`WeaponConfig` on the projectile**, never read `Runtime.Config` at impact: a
rocket in flight outlives a weapon swap, and reading the current runtime would
apply the *pistol's* falloff to it. Write that into the class header.

## W5 — Attachments and optics

**A new `AttachmentConfig` SO composed into `WeaponConfig` — deliberately NOT the
`EffectModule` pattern.** `EffectModule` is a *behaviour hook* where a new module
is a new C# class; attachments are 90% stat deltas, so routing them through it
means a class per attachment, which is exactly the combinatorial mess to avoid.

- Slots: `{ Optic, Muzzle, Barrel, Underbarrel, Magazine, Stock, Ammo }`.
- `WeaponClass allowedClasses` — giving `weaponClass` a second real reader.
- A `Modifier[]` folded onto the **runtime** at equip time.

⚠️ **Do not extend `Stat` / `StatExtensions.Count`** — that resizes `StatSheet`'s
arrays and ripples into passives, money and health. Use a separate `WeaponStat`
enum and sheet.
⚠️ Attachments modify the **runtime**, never `SecondsPerShot` on the config, so
`WeaponCadenceRegressionTests` keeps exercising the authored path.

## W6 — Multi-weapon carry, grenades, melee

- `swapTime` and `PlayerLoadoutConfig.weaponSlots` are **dead fields with zero
  readers**. Make them live: a `LoadoutRuntime` with primary/secondary, new
  input actions, a shared action lock (`TryReserve`/`BusyUntil`) covering swap,
  reload, melee and throw.
- ⚠️ **Verify the number row does not double-bind** — the shop is bought with
  digits. `PlayerInput.SetBlocked` disables the whole map during a break, which
  *should* cover it, but confirm on the first build rather than assuming.
- Grenades and melee are **new systems, not EffectModules** — they are not
  triggered by a bullet impact.
- `SaveData` gains `unlockedWeaponIds`, which finally gives `stableId` the
  runtime reader it was designed for.

## W7 — Viewmodel animation, per weapon

Today every weapon looks like the same 8 cubes: `EquipWeapon` changes zero
visuals. `WeaponConfig` gains a `ViewmodelConfig` reference (prefab + named
socket paths for muzzle, casing eject, sight), and the rig swaps on equip.

**Stay procedural.** No Animator on the viewmodel: curve-driven poses cost
nothing, are tunable from a ScriptableObject, and avoid an import pipeline for
the one object that is on screen constantly. (Humans are the opposite case — see
the campaign plan — because a *pose* is the telegraph there.)

---

# The order, across both tracks

Interleaved so that each block ships something felt, and so the money lands last.

```
0.  THE PLAY SESSION            <- everything below is re-ordered by what it says
1.  W3.1-3   pistol, marksman, LMG        pure data; proves the architecture
2.  G3       tracers, decals, per-surface impacts, muzzle flash
3.  G6       viewmodel config, wall-lower, reflex sight
4.  W3.4     shotgun            (needs nothing new; the scoping fix landed)
5.  G5       audio: mixer, reverb, footsteps, ambience
6.  G4       reflection probes, cookied spots
7.  W4       projectiles -> launcher
8.  W5       attachments + optics -> sniper
9.  G8       THE ART SEAM       <- the gate. Nothing is bought before this.
10. G9a      free art (ambientCG, Poly Haven, Kenney, Sonniss)
11. G9b      pack #1, measured. Then pack #2, measured.
12. W6, W7   multi-weapon carry, grenades, per-weapon viewmodels
```

**Steps 1–8 are free and are worth more than steps 10–11.** That ordering is the
single most important claim in this document. If it is followed, the money lands
on a game that already reads well; if it is inverted, the money lands on a game
whose impacts, audio and light cannot show it off.

# Effort, honestly

| Block | Sessions | Money |
| --- | --- | --- |
| W3 (pistol/marksman/LMG/shotgun) | 2–3 | $0 |
| G3 impacts | 3–4 | $0 |
| G6 viewmodel feel | 3–4 | $0 |
| G5 audio | 3–4 | $0 |
| G4 light | 2–3 | $0 |
| W4 + W5 projectiles, attachments, sniper | 4–6 | $0 |
| G8 art seam | 2–3 | $0 |
| G9 art in | ongoing | $75–185 |
| W6 + W7 carry, grenades, viewmodels | 4–5 | $0 |

**~25–35 focused sessions, of which the last block is the only one that costs
money.**

# What to refuse, and why

| | Why |
| --- | --- |
| **HDRP** | Forbidden by CLAUDE.md, correctly. +300–600 MB of render targets and a rewrite, not a toggle. |
| **Ray tracing** | **URP does not support it at all.** Not a perf call — the feature does not exist in this pipeline. The 3050's RT cores are irrelevant here. |
| **4K textures** | 20 pairs = ~900 MB, a quarter of the card, for detail invisible at 1080p on a 3 m wall. `guard-texture-budget` now blocks it. |
| **MSAA** | Multiplies every render target; does nothing for the specular and emissive aliasing that dominates this image; and lives on the URP asset, which is the Domain-Reload wall. SMAA + a good grade is the right answer. |
| **Motion blur** | URP's is camera-only. On a fast turn it smears the whole screen and *hides the drone about to reach you*. Already refused by assertion. |
| **Always-on depth of field** | 1.5–3 ms and it fights target readability. ADS-only is defensible later. |
| **Shadow-casting point/spot lights** | Six cube faces per light sharing one 2048 atlas — visible acne *and* real frame time. The sun stays the only caster; use a blob-shadow decal once G3 lands the Decal feature. |
| **A render-texture scope** | Renders the world twice. Reflex and holo sights need no RT. |
| **GPU Resident Drawer** | Targets tens of thousands of instances. This scene has ~60 static blocks. |
| **An "Ultimate FPS Kit"** | Would fight `WeaponController`, the pool, every guard and `GreyBoxVerify` at once. |

# Verification, every phase

```
node Tools/typecheck.mjs      # 9 assemblies, zero errors AND zero warnings
node Tools/check.mjs          # 8 guards, incl. texture and LFS budgets
Unity.exe -batchmode -runTests ... -testPlatform EditMode
Unity.exe -batchmode -runTests ... -testPlatform PlayMode
node Tools/verify-build.mjs   # a real player, built and RUN. Unity must be CLOSED.
```

Plus, for anything in Track G, the two questions **no gate in this repo can
answer** — because `-batchmode -nographics` does almost no GPU work:

1. **Does it look right.** Only a human at 1080p.
2. **What does it cost in frame time.** Measure at wave 12 with 40 alive, before
   and after every phase. Memory Profiler snapshot from a **Development build**,
   never the editor, which inflates everything.

Record both in `docs/systems/rendering.md` under Budget and diff them each phase.
