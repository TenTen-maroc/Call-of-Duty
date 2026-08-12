# Call of Duty — the plan to become a real one

## Context

**What was asked.** Make this look like a modern Call of Duty, and give it a story with
missions, more guns, and everything that implies. Decisions taken during planning:
**~$150–300 art budget**, **human soldier enemies** (not drones), **no time ceiling**.

**What exists today.** A code-complete, shippable-shaped horde-survival FPS. Unity
6000.0.81f1 + URP 17.0.4, 108 tests, six guards, a real `.exe` that boots headlessly.
And **zero imported art** — every mesh in the game is `GameObject.CreatePrimitive(Cube)`,
there is exactly one texture (procedurally generated), 15 synthesised placeholder `.wav`s,
and all 12 materials are stock URP/Lit. All three scenes, 12 prefabs and ~50 ScriptableObjects
are emitted by [GreyBoxBuilder.cs](Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs) — 2951
lines of editor code that owns the entire project's scene truth.

**The intended outcome.** A four-arena, twelve-mission campaign with human enemies, a real
arsenal, and a coherent stylised-military look — reached without breaking the six guards,
the 108 tests, the headless build gate, or the ScriptableObject tuning law that makes this
codebase maintainable.

---

## Read this before anything else

### 1. The reachable look is stylised-realistic, not photoreal. Commit to it.

$250, one dev, no artist, and a 4 GB laptop does not produce Modern Warfare, and **chasing
it actively makes things worse**: a 4K-textured photoreal crate next to a flat-shaded wall
looks worse than the honest grey box. The target is Synty-tier shapes + disciplined HDR
grading + strong VFX and audio + excellent game feel. That reads as a finished game. Mixed
visual languages read as a student project. **Buy inside one look and stay there.**

### 2. The play session is still step zero, and this plan is 100× the content the gate was written for.

[CLAUDE.md](CLAUDE.md) says content does not start before the tuning pass says the core is
fun. That gate was overridden once already (2026-08-11 — G1–G5, all still `⚠️ never played`).
Phases 4–7, R1–R2 and G1–G5 have **never been played by a human**. This plan proposes ten
times that volume of content on top. Do the nine-item card in
[docs/NEXT-SESSION-PROMPT.md](docs/NEXT-SESSION-PROMPT.md) — roughly two hours — before
Phase 1. It is the cheapest possible de-risking of everything below.

### 3. Commit #1 is a CLAUDE.md amendment, and it is not ceremony.

CLAUDE.md:87-89 states enemies are drones **deliberately**, because "humanoid animation is
the single largest art-cost sink in a solo FPS, and skipping it is what makes this project
finishable", and :105 locks "3 drone archetypes, no more, for v1". Human enemies and story
missions reverse two locked decisions. The file's own contract (line 5) is *"When code
conflicts with this doc, update the doc first, then the code."* The premise is genuinely
obsolete — **Mixamo is free, commercially licensed, ~2500 clips, and retargets onto any
Unity Humanoid avatar** — but that has to be written down, not assumed.

### 4. Humans are the cast; the drone code path stays alive, for free.

The user chose humans. Humans become the campaign's primary enemy. **But do not delete or
rename the drone layer** — the fiction below makes both work, and keeping it costs nothing:

- A soldier is a `DroneConfig` + an `AttackModule` + a rigged prefab. **Same data type.**
  The only new code is an optional `EnemyAnimator` component the controller null-checks.
- Renaming `DroneConfig`/`DroneController` is a ~40-site edit that breaks `GreyBoxVerify`'s
  six hardcoded asset paths, `WaveDesignTests`' asset assertions, and every `SetRef` call —
  and buys tidiness only. `LoadOrCreate` ([GreyBoxBuilder.cs:2561](Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs))
  runs `configure` **on create only**, so a renamed asset path *silently creates a fresh
  default asset, discards every tuned number, and reports success.*
- Humanoids cap at ~12–18 alive; drones cap at 40. Keeping drones is the only way the game
  still has a horde in it.

**Premise that makes both legitimate:** Vantage Dynamics' autonomous weapons have gone rogue;
Meridian PMC is paid to contain the *story*, not the machines. You fight the people covering
for the machines, and the machines. Every mission can mix families.

If zero drones is genuinely wanted, that is one authoring decision — don't put them in the
wave configs — not an architecture change.

---

## Ground rules every phase inherits

| Rule | Enforced by |
|---|---|
| Zero errors **and zero warnings**, 9 assemblies | `node Tools/typecheck.mjs` |
| Six guards: LFS coverage, meta integrity, no per-frame `Find`/`GetComponent`/`Instantiate`/`Destroy`/`Camera.main`, **no mutable statics** (Domain Reload is OFF; `static readonly int` IS allowed), no build artifacts, LFS hooks | `node Tools/check.mjs` |
| Every serialized field the verifier names is **immutable** — a rename reports "no such field" and fails | [GreyBoxVerify.cs](Assets/_Project/Scripts/Editor/GreyBoxVerify.cs) |
| Real Windows player built and RUN, zero errors boot→menu→arena. Unity must be CLOSED | `node Tools/verify-build.mjs` |
| Every tunable in a ScriptableObject under `Assets/_Project/Data/`; everything spawned goes through `ObjectPool`; no per-frame allocation; `#nullable enable` line 1 | CLAUDE.md + review |
| **Never write to a ScriptableObject at runtime** (URP asset, `sharedProfile`, AudioMixer) — Domain Reload is off, so the write survives into the next Play session and rewrites the shipped default | `CameraGraphics.cs:12-20` documents the trap |

**LFS.** Free tier is 1 GB storage / 1 GB bandwidth per month; current usage is 16 objects,
~1 MB. Strategy: raw packs live in `Assets/ThirdParty/` and are **gitignored** (the meta
guard already exempts that folder); only the curated subset actually referenced by a scene
gets copied into `Assets/_Project/Art/` and committed. Commit `ThirdParty/MANIFEST.md`
(pack, publisher, version, order ID, files copied) as the reproducibility story. **Get
resolution and import settings right before the first commit of any binary** — LFS storage
is cumulative and never reclaimed without a history rewrite. Budget $5/mo for a GitHub Data
Pack (50 GB) if the cap is ever threatened; it is inside the art budget and is the obvious
answer.

---

## Phase 0 — two spikes, no purchases, ~3 days

**0.1 The play session.** The nine-item tuning card. Report per item.

**0.2 The humanoid spike — this replaces an estimate with a measurement.** Import one free
Mixamo character + 3 clips into `ThirdParty/` (Rig → Humanoid), put 18 in `10_GreyBox` with
a trivial controller, and **profile on the actual 3050**. Target ≤ 8 ms CPU main thread,
≤ 10 ms GPU at 1080p. The number that comes out sets `maxAliveOverride` for every human
wave in the game.

> The real cost driver is **shadow-casting skinned meshes** (one skinned draw per cascade
> per soldier, ×4 cascades), not VRAM and not GC. An Animator evaluates in native code and
> does not allocate per frame — cache `Animator.StringToHash` in `static readonly int`
> (precedent: `DroneController.cs:45-46`) and the 16 KB/frame budget is untouched. One
> shared 1024 atlas across every soldier variant puts the **entire enemy cast under 24 MB
> of VRAM.** Say this out loud so the wrong thing does not get optimised.

**0.3** Confirm the target pack advertises **Unity Humanoid** rigs. If rigs differ you get
one Animator Controller per character and this plan collapses.

**0.4** Commit the CLAUDE.md amendment + `docs/systems/campaign.md` stub.

---

## Track G — Graphics

*Roughly 20–30 sessions, and the image is transformed **before a dollar is spent**. That
ordering is deliberate: G8 exists so G9's money lands on a prepared surface.*

**G0 — measure.** New `Tools/guards/guard-texture-budget.mjs` (fails `maxTextureSize > 1024`
outside an allowlist for `Art/Textures/Weapons|Hands` → 2048). New `Editor/ArtReport.cs`
(`CoD → Report Texture Budget`). A dev-only frame-time/draw-call HUD row. Baseline captured
into a new **Budget** section of `docs/systems/rendering.md`.

**G1 — the confirmed defects.** Zero purchases, highest value-per-hour in the plan.
1. `BuildSparksPrefab` (:994) **never assigns a particle material** — every bullet impact
   renders default magenta. Verified `m_Materials: - {fileID: 0}`.
2. `Fx_Hot.mat` is an **opaque Lit** material used by every particle system. Add
   `LoadOrCreateParticleMaterial` using `URP/Particles/Unlit`, Transparent, **Additive**,
   **soft particles on** (depth texture is already enabled, so it is free).
3. `PC_RPAsset.m_VolumeProfile` still points at Unity's template `SampleSceneProfile.asset`,
   which overrides `bloom.skipIterations` to 0 — a second unmanaged stack under the real one.
   Repoint at an empty `PostFx_Global.asset`, delete the template, and pin `skipIterations`
   explicitly in `LoadOrCreateVolumeProfile`.
4. **The tactical palette never reached disk.** `LoadOrCreateMaterial` returns existing
   materials untouched, so `GreyBox_Floor` is `0.32,0.33,0.35` on disk vs `0.17,0.18,0.20`
   in code — ~1.8× too bright. Fix the *class*: new `PaletteConfig` SO + `ApplyPalette`
   re-applied every build, exactly as `ApplySurface` already is.
5. **The gun is reflecting a blue sky inside a sealed bunker.** `RenderSettings.skybox` is
   Unity's default and is the only reflection source in the game, while `Weapon_Body.mat` is
   metallic 0.85. Set a dark custom reflection now; real probes land in G4.
6. SSAO radius 0.3 → 0.5, intensity 0.4 → 0.75, `Downsample` on. Via a new reproducible
   `Editor/RendererSetup.cs`, not a hand-edit.

**G2 — the viewmodel camera. This is the #1 fix.** One camera, weapon parented to it, no
viewmodel layer, no depth clear: the gun **clips through every wall** and **warps whenever
world FOV changes** ([PlayerLook.cs:150](Assets/_Project/Scripts/Player/PlayerLook.cs)).
Add layer 6 `Viewmodel` to TagManager; strip it from the base camera's culling mask; add an
**Overlay camera** (clear Depth, near 0.01, far 5, in the base's `cameraStack`, **not**
tagged MainCamera, **no** AudioListener); reparent `WeaponRig` under it; split world FOV from
viewmodel FOV. Muzzle flash moves to the Viewmodel layer; the shell casing **stays in the
world** (it has a Rigidbody and must bounce off the real floor). New `ViewmodelTests.cs`.
Guard the build with a `LayerMask.NameToLayer("Viewmodel") < 0` error — Unity silently
assigns layer −1 and fails at runtime, not build time.

**G3 — impact response.** Decal Renderer Feature (**Screen Space, not DBuffer** — DBuffer
costs a depth prepass + 2–3 fullscreen targets for nothing on flat grey boxes) → real
`DecalProjector` bullet holes from one 512 atlas. Per-surface impacts keyed on
**`hit.collider.gameObject.layer`** — an int field read, which is guard-clean, where
`GetComponent<SurfaceTag>()` from `Update` would not be. `ImpactConfig.impactSound` (declared
since forever, read by nothing) finally gets a consumer. **Tracers, which do not exist at
all** — pooled `TrailRenderer`, **1 in 3 rounds** (every round looks like a laser show and
kills the muzzle flash). Muzzle flash → 4-frame flipbook + stretch quad + the already-wired
`MuzzleLight`. Heat shimmer on explosions from `_CameraOpaqueTexture`, already enabled and
currently paid for by nothing — the project's first `.shadergraph`.

**G4 — light.** Four baked reflection probes at 128 (the biggest single material win — it is
what stops metallic 0.85 surfaces being featureless). Point lights → **spot lights with 256
cookies** (the cookie atlas is already 2048 and costs nothing). ⚠️ *This deliberately breaks
`RenderingTests.Arena_IsLit` (:89), which counts `LightType.Point`* — widen it to "enabled,
non-directional, not under a Camera" **in the same commit, with a comment.** All lights stay
`LightShadows.None`; **the sun remains the only shadow caster.** Optional G4b: Adaptive Probe
Volumes for real bounce light, last, measured.

**G5 — audio.** There is no mixer, no music, no ambience, no footsteps, no reverb.
`Master.mixer` is **the one asset the builder cannot generate** (`AudioMixerController` is
internal) — author it by hand, commit it, and document the exception loudly in a new
`docs/systems/audio.md` or the next session will rebuild around a missing asset. Buses
Master→SFX(Weapons/Impacts/Enemies/World)/UI/Music/Ambience. `SettingsHub.Apply` stops
writing `AudioListener.volume` and writes an exposed param. **Reverb via a mixer send, not
`AudioReverbZone`s** — that single change is what makes the rifle sound like it is *inside a
facility*. Footsteps (`Player/Footsteps.cs`, distance accumulator, one raycast per **step**),
ambience loops, menu + wave-tension music, a third mechanical weapon layer.

**G6 — viewmodel feel.** Move `WeaponSway`'s 9 pose/sway/bob fields into a `ViewmodelConfig`
SO — they are tuning numbers on a MonoBehaviour, the one live violation of CLAUDE.md's most
important rule. Weapon-lower on wall proximity (one cached `SphereCastNonAlloc`) — the camera
stack fixes the *rendering* half of clipping, this fixes the *gameplay* half. Inspect;
procedural reload/ADS via `AnimationCurve` fields. Screen-space damage feedback via a
**runtime-instanced** Volume (read `.profile`, never `sharedProfile`). **Refuse render-texture
scopes for v1** — reflex/holo sights need no RT; only snipers do.

**G7 — the grade. Effectively free.** HDR grading with a 32³ LUT is already on, so everything
folded into it costs zero milliseconds: `ShadowsMidtonesHighlights` (cool shadows / warm
highlights — the single biggest "modern shooter" move), `WhiteBalance`, `LiftGammaGain`,
`ColorCurves`, a hand-authored `ColorLookup` strip (~32 KB — **do not buy a LUT pack**), light
`ChromaticAberration`. **Keep Tonemapping = Neutral**; ACES hue-shifts the reds that carry
threat readability. Extend `RenderingTests.ArenaProfile_KeptItsOverrides_ThroughTheSave` —
the `AddObjectToAsset` trap fails silently as an empty profile.

**Refuse, with reasons:** Motion Blur (URP's is camera-only; it hides incoming enemies),
always-on DoF (1.5–3 ms and fights target readability), MSAA (multiplies every render target;
lives on the URP asset = the Domain-Reload wall; SMAA + a good grade is the right answer),
HDRP, **ray tracing (URP does not support it at all — not a preference, the feature does not
exist)**, GPU Resident Drawer (targets tens of thousands of instances; this scene has ~60),
4K textures, shadow-casting point/spot lights.

**G8 — the art seam. Build this BEFORE spending a cent.** `GreyBoxBuilder` keeps owning the
scene; **art becomes data**. New `ArenaKitConfig` / `WeaponKitConfig` / `EnemyKitConfig` SOs
holding optional prefab + material references. `BuildRoom`'s ~30 `AddBox` calls become
`AddBlock(..., kit.wallModule, kit.wallMaterial)` where `AddBlock`:
1. **always** creates the box collider from the same position/scale,
2. instantiates the art prefab as a child named `Art` with **every collider stripped**,
3. falls back to `CreatePrimitive(Cube)` when the kit field is null.

**The load-bearing rule: collision and navmesh come from the box; art comes from the prefab.
Art never changes gameplay geometry.** Consequences that matter — the navmesh bake is
byte-identical with or without art, every gameplay test is unaffected by an art swap, and
**it is reversible**: null the kit fields, rebuild, and you are back in a shippable grey box.
`GreyBoxVerify` gains a `VerifyKits()` enforcing **all-null or all-non-null per kit** — a
*mixed* kit is the real failure mode, because it looks half-built and verifies clean. Plus
committed `TextureImporter`/`ModelImporter` presets and an `ArtImportPostprocessor` that
stamps settings by folder — this is how the 1024 rule becomes automatic instead of
aspirational, and what stops one $60 pack silently importing 40 4K textures.

**G9 — art in.** Free first (ambientCG 1K materials, Poly Haven HDRI, Unity's Particle Pack,
Kenney, Sonniss GDC bundle for audio — kept **entirely outside the repo**, export trimmed
clips only). Then **one paid pack at a time, each its own commit with its own VRAM
measurement.** Never import two packs before measuring one.

---

## Track W — The arsenal

**W1 — foundations (no art dependency, all testable today).**
- `ViewmodelConfig` + `ViewmodelSockets`; `WeaponConfig` gains a viewmodel reference so
  guns stop all looking like the same 8 cubes.
- `LoadoutRuntime` — primary/secondary slots. `swapTime` and `PlayerLoadoutConfig.weaponSlots`
  are **dead fields with zero readers** today; make them live. New `SwapNext`/`Slot1`/`Slot2`
  actions in `CoD.inputactions`. ⚠️ *Verify the number row does not double-bind against the
  shop* — `PlayerInput.SetBlocked` should already cover it, but confirm on the first build.
- A shared action lock (`TryReserve`/`BusyUntil`) for swap/reload/melee/throw.
- **Fix the pellet-scoping bug before the shotgun exists.** `FireOneShot` (:371-373) loops
  pellets calling `CastOneRay`, and `CastOneRay` ends in `DrainFollowUps`, which clears
  `_followUps` **and `_alreadyHit`** (:594-595). So every buffer the comments call "per shot"
  is actually **per pellet**. At 12 pellets: effect modules run 12× per trigger pull,
  `MAX_FOLLOW_UPS_PER_SHOT = 96` becomes 1152 (the hang guard multiplied by the pellet
  count), 12 stacked hitmarker clicks, and one wall blast eats a quarter of the 48-decal
  pool. Hoist the clears into `FireOneShot`. Standalone correctness fix, EditMode-testable
  with a synthetic 12-pellet config.

**W2 — the TTK test, resolved honestly.** `WeaponDataTests.EveryWeapon_LandsInsideTheArcadeTtkWindow`
asserts every weapon lands in 200–400 ms. A one-shot weapon reports **TTK 0** and is
structurally incapable of passing; the metric also ignores `pelletsPerShot`, `burstPause` and
falloff. Fix the *model* first (`ShotsToKill` accounts for pellets, `TimeToKill` for burst
pause, add `TimeToKillAtRange`), then split the law by class:

| Class | Law |
|---|---|
| AR / SMG / LMG / Pistol / Marksman | TTK ∈ [200, 400] ms — unchanged, the game's identity |
| Shotgun | ≤ 1 pull at contact, ≥ 2 pulls at 10 m, via `TimeToKillAtRange` |
| Sniper / Launcher | **One shot, one kill, by design.** Assert **re-engagement cost** instead: `adsTime ≥ 0.35` **and** `SecondsPerShot ≥ 0.9` |

Do **not** author a 99-damage sniper to squeak past the old assertion — it breaks anyway the
moment `healthMultiplierByWave` (ramping to 3.5×) makes nothing one-shot. Make
`WeaponConfig.OnValidate`'s 150–500 ms warning class-aware too, or every sniper asset screams
in the Inspector forever. Drive the test's list off a new `WeaponRegistry` so weapon #7 is
not a test edit.

**W3 — the guns.** Pistol, Marksman, LMG are **pure data** — ship them first, they prove the
claim. Shotgun needs W1's fix plus `pelletSpreadDegrees` (fixed pattern geometry, distinct
from bloom). Sniper needs `OpticConfig` + hold-breath + a bolt-cycle time distinct from RPM.
LMG wants `RecoilPattern`'s two-point line to become an `AnimationCurve` (curves are already
an established pattern in `DifficultyConfig`/`ShopConfig`).

**W4 — projectiles, for the launcher.** A working swept-ray pooled projectile already exists:
[DroneProjectile.cs](Assets/_Project/Scripts/Enemies/DroneProjectile.cs) — no collider,
sweeping between frames because a fast trigger tunnels through walls at any sane physics step.
**Promote it to `CoD.Core/Projectile.cs`** (legal: Core references nothing; both Enemies and
Weapons reference Core). `WeaponConfig` gains `DeliveryMode { Hitscan, Projectile }`.
⚠️ **Carry the `WeaponConfig` on the projectile, not `Runtime.Config` at impact** — a rocket
in flight outlives a weapon swap, and reading the current runtime applies the *pistol's*
falloff to it. Write that into the class header.

**W5 — attachments.** A new `AttachmentConfig` SO composed into `WeaponConfig` — **not** the
`EffectModule` pattern. `EffectModule` is a behaviour hook where a new module is a new C#
class; attachments are 90% stat deltas, so routing them through it means a class per
attachment, which is exactly the combinatorial mess to avoid. Slots {Optic, Muzzle, Barrel,
Underbarrel, Magazine, Stock, Ammo}, `WeaponClass allowedClasses` (giving `weaponClass` its
first-ever reader), a `Modifier[]` folded at equip-time onto the runtime.
⚠️ **Do not extend `Stat`/`StatExtensions.Count`** — that resizes `StatSheet`'s arrays and
ripples into passives, money and health. Use a separate `WeaponStat` enum and sheet.
⚠️ Attachments modify the **runtime**, never `SecondsPerShot` on the config — that keeps
`WeaponCadenceRegressionTests` exercising the authored path.

**W6 — grenades and melee** (new systems, not EffectModules), **W7 — tracers + the impact
library** (shared with G3), **W8 — procedural reload/inspect/swap** on the viewmodel.

---

## Track E — Human soldiers

**What survives, confirmed by audit:**

| | Verdict |
|---|---|
| `AttackModule` + `ref DroneAttackState` | **The best-designed thing in the enemy layer.** Stateless SO of numbers, per-instance state in a ref struct. `RangedBurst` becomes the rifleman — *including* its deliberate opening miss, which matters **more** with humans since a man firing from cover is otherwise invisible. `ContactDetonate` → suicide bomber. `HeavySlam` → breacher. |
| `Weakpoint` + `GetComponentInParent<Health>()` | **The single luckiest thing in the codebase.** 8–9 capsule colliders on bones, each with a `Weakpoint`, all resolving to the root `Health` — **zero changes to `ResolveHit`.** |
| `DroneRegistry`, `AttackTokenPool`, `DroneSpawner`, `DifficultyConfig` | All survive. `maxSimultaneousAttackers = 3` is why 20 enemies read as fair — with soldiers the token *is* the classic combat-director "who may shoot the player right now". |
| `DroneController` | Survives. Add an optional `EnemyAnimator` component the controller null-checks (`SetSpeed01`/`PlayAttack`/`PlayDeath`) — null on drones, present on soldiers. **Root motion OFF**; the NavMeshAgent owns movement. |

**E1 — the animation layer.** Animator + Unity Humanoid avatar + one blend tree + Mixamo
clips + **one shared avatar across every soldier**. Twelve-plus clips of full-body humanoid
motion is not something you write. Alive cap comes from the Phase 0 measurement — expect
**12–18**, not 40. Start with `WaveConfig.maxAliveOverride` (already honoured by
`WaveRunner.StartWave` and `DroneSpawner.SetAliveCapOverride`) — **zero new code.** Only add
`maxAliveHumanoids` to `DifficultyConfig` once that demonstrably fails.

**E2 — the hitbox rig.** ⚠️ History matters: a previous `HealthConfig.weakpointMultiplier`
was **deleted** because it double-dipped with `WeaponConfig.headshotMultiplier`, making the
weapon the single owner of the headshot bonus. Do not reintroduce that. Add a `HitZone`
component + `HitZoneConfig` where the zone contributes a *zone* factor and the weapon keeps
owning the headshot bonus. One custom `Hitbox` layer.

**E3 — the telegraph, re-channelled. This is the real design problem.** `SetTelegraph` ramps
emission ~3.9× through every attack windup and is *the fairness contract of the entire enemy
design* — the difference between "I died from nowhere" and "I got caught out". Humans have no
glowing core. **Do not delete `SetTelegraph`; re-implement it**, keeping the method and its
0..1 contract (every `AttackModule` calls it, and `ForceReleaseAttackToken` depends on
`Cancel` resetting it). One channel becomes four, each better at its job:
1. **Pose** — a distinct, *large* windup pose. `reactionDelay = 0.4 s` is exactly the window,
   and a big 0.4 s pose is readable at 25 m. This is the strongest argument for Animator.
2. **Muzzle flash + slow tracer at range.** ⚠️ **Keep `projectileSpeed = 18`** — "hitscan
   enemies are unreadable and unavoidable at the same time". Do not raise it for realism.
3. **Spatialised voice barks — the channel emission never had.** The prefab already carries a
   3D AudioSource with linear rolloff to 35 m. "Contact!" / "Flanking left!" / "Reloading!"
   are the cheapest assets in this plan and they solve the **offscreen** case a glow never could.
4. **A diegetic chest/helmet IR strobe** — real kit, costs nothing in fiction, and drives off
   the same `MaterialPropertyBlock` machinery `SetTelegraph` already uses.

Archetype identity inverts: silhouette first (slim+rifle / bulky vest / belt-fed / no gun +
sprinting), accent band as backup. Preserves the arena's palette rule — cool is architecture,
warm means something is trying to kill you.

**E4 — AI, staged.** Stage 1 ships with the model swap and is days: **stop-to-shoot**
(`SetSpeedMultiplier(0)` **already exists** — highest value-per-line change in the whole plan),
**strafe** on a re-rolling phase, **look-at** (`agent.updateRotation = false`). Stage 2 is
cover, a week-plus: **do not write a cover-point generator** — the arena already has exactly
the right geometry (1.2 m shoot-over cover, 3 m dividers and bunker, four pillars); emit
`CoverPoint` components **inside `BuildRoom` alongside the boxes** so they can never drift
out of sync, and claim them via a `CoverRegistry` shaped like `DroneRegistry`, **evaluating
one soldier per frame round-robin**. Stage 3: suppression = `_suppressedUntil` set on
`Health.Damaged` (90% of the read for 5% of the work); flanking = prefer a lane the squad is
*not* in, which is flanking without a planner. **Do not adopt a behaviour-tree framework** —
`AttackModule` + a phase enum already scales to all of this, and it is what keeps "a new
enemy is DATA" true. Ship stages 1 and 5, play it, then do stage 2.

**E5 — death.** Mixamo death animations (4–6, selected by `DamageInfo.Direction`, which is
already carried) for the common case; **ragdoll only for explosive kills, hard-capped at 4**
in `DifficultyConfig`. The E2 hitbox rig **is** the ragdoll skeleton.
⚠️ **The corpse defect, stated in advance:** `ResolveHit` returns `Blocked` for a dead target
and `CastOneRay` **breaks the pierce loop on anything not `Damaged`** — so **a lingering
corpse stops bullets**, becoming free cover. Never surfaced with drones because they despawn
instantly. Fix: disable the `Hitbox`-layer colliders on death. Cap live corpses at ~8,
recycle oldest-first.
**Gore policy, written down: "restrained but real"** — directional blood spray + a surface
decal, no dismemberment, no gibs. It is a `SurfaceType.Flesh` entry in the W7 impact library,
which means gore level is a **data swap** and a reduced-blood accessibility setting is a
second asset, not a code branch.

**E6 — perf.** LODGroup 12/22/32 m (the arena is 40 m across). **Shadows are the biggest
lever** — distance 25 m, one cascade, `ShadowCastingMode.Off` on LOD1+. Animator culling
`CullUpdateTransforms` (**not** `CullCompletely`, which freezes a soldier walking behind you).
GPU skinning on. One shader variant so the SRP Batcher groups them; no `renderer.material`
anywhere. Move the 8 spawn points out of line-of-sight — a drone materialising on a ring is
acceptable, **a man materialising is not** — then re-verify `NavMesh_CoversTheArena_WithNoIslands`
with a human agent profile (radius/height differ from the drone's).

---

## Track C — The campaign

**C1 — the mode axis.** ⚠️ **Do not add `GameMode.Campaign = 2`.** `GameMode` is serialised
as a raw int; C# enums are not range-checked, so a shipped build reading `lastMode: 2` gets
"not Sandbox" = **treated as Run**, and `RecordRunEnded` writes a campaign mission's wave
number into `bestRound`. The permadeath record is polluted by a build you cannot patch —
exactly the harm `SaveSystem`'s future-version refusal exists to prevent, self-inflicted.
**Make it a second axis:** `GameMode` keeps two values and means *rules*; a new
`campaignSelected` bool means *content*. An old build reading a campaign save sees
`lastMode: Run`, ignores two unknown fields, and starts a normal endless run. Safe degradation.

**C2 — save schema 4.** A `campaign` block (`campaignSelected`, `selectedMissionId`,
`missionRecords[]`) + a `Migrate` branch that is **deliberately empty**, following the `< 3`
precedent — an `xInitialised` flag re-seeds from a ScriptableObject, because a tuning number
in a migration is banned. ⚠️ **Do not regress the two-SaveData clobber** — three sites
(`SettingsHub.Persist`, `SettingsHub.SetLastMode`, `RunContext.RecordRunEnded`) each serialise
the *whole* object, so campaign writes must go through the shared `SettingsHub.Save`.
`SaveFileGuard` must gain the new fields or every save test silently zeroes them.

**C3 — the `WaveRunner` seam. Seven additive methods, all inert without a director.**

| # | Addition | Why the runner cannot express it today |
|---|---|---|
| 1 | `internal void SetWaves(WaveConfig[])` | `_waves` is private-serialized, written only by editor code. Refuses mid-wave. |
| 2 | `Suspended` + `Suspend()`/`Resume()` | No way to hold the loop for a briefing. Non-destructive; the queue survives. |
| 3 | `internal void StartFrom(int wave)` | Checkpoint restore. The cheat console's skip-to-wave should have been this. |
| 4 | `internal void AbortWave()` | Destructive companion for checkpoint rewind. |
| 5 | `internal void SetDeathEndsRun(bool)` | The run ends **only** via `Health.Died` today. Campaign death is a rewind. |
| 6 | `public event Action? PlayerDown` | Raised instead of `FinishRun` when death does not end the run. |
| 7 | `FinishRun(RunOutcome)`, `RunEnded` → `Action<RunOutcome>` | **`RunEnded` has zero subscribers today** — the signature change costs nothing. |

**Why this is not a fork:** every one generalises a capability the runner *already has and
hides* — it already picks a starting wave, carries a wave list, has a do-not-tick state, ends
the run, clears the arena. A fork would have to duplicate and keep in sync the spawn queue's
struct-copy-writeback subtlety, both placement-failure hang guards (one of which needed a
*second* fix), and the "wave that planned nothing" recovery with its explicit refusal to pay a
clear bonus. That is the entire hard-won part of the file.

⚠️ **The ordering hazard, and the one guarantee this hangs on.** Unity does not guarantee
`Awake` order between components, but **does** guarantee every `Awake` completes before any
`Start`. So: `MissionDirector.Awake()` reads `campaignSelected` and — if true — calls
`Suspend()` + `SetDeathEndsRun(false)`; `WaveRunner.Start()` gains `if (Suspended) return;`
**before** `BeginRun`. The director then begins the run on its own schedule, frames later.
**No new serialized bool, no `GreyBoxVerify` gap** (a bool cannot be `Check`ed — it tests
`objectReferenceValue`). *The absence of a director is the endless configuration.*

**C4 — objectives.** `MissionObjective : ScriptableObject`, modelled on `EffectModule`:
`Begin` / `Tick(in ObjectiveContext, ref ObjectiveState, ...)` / `End` / `Describe(StringBuilder)`.
Three transcribed rules: **stateless** (per-instance data in `ref ObjectiveState`), **never
mutate the world** (the director owns spawning/healing/phase/save, so each has one place to go
wrong), and — critically — **never subscribe to anything**. A ScriptableObject that subscribes
keeps the subscription across Play sessions with Domain Reload off: the mutable-static bug
class in a form the guard cannot see. The director subscribes once into `MissionProgress`;
objectives **poll** it, which is what makes every objective EditMode-testable with no scene.

Types: `SurviveWaves`, `KillQuota`, `HoldZone`, `ReachZone`, `DestroyTargets`, `Escort`,
`Extract`, `NoAlarm`, `RepairBeacon`, `Interact`. **"Timed" is deliberately not a type** — it
is `Step.timeLimitSeconds` checked uniformly by the director, so any objective can be timed
and there is no wrapper-SO-composing-SO tree.

Three pieces of [ArenaObjective.cs](Assets/_Project/Scripts/Waves/ArenaObjective.cs) lift out
verbatim into `ObjectiveMath`: the **floor-plane** containment test (`delta.y = 0` — the
player's origin is at their feet and a zone is a pad), `PickDifferent` (uniform pick excluding
the previous index with no reroll loop), and `AddPad` — where **destroying the cylinder's
collider is the load-bearing line**, since a floor collider blocks movement or eats the aim
ray. `ArenaObjective` itself needs **zero code changes**: start it inactive, and `OnEnable →
Relocate()` already does the right thing on activation.

**C5 — zones and interaction.** No physics triggers (there are none in the project and none
are needed) — a polled zone registry plus distance-and-facing interaction reusing the same
`ObjectiveMath`. Needed for extract points, terminals, charges and intel.

**C6 — story delivery.** ⚠️ **TextMeshPro is already installed** (Unity 6 ships it inside
`com.unity.ugui` 2.0.0) — using it costs one asmdef reference, not a package. ⚠️ **Legacy
`Text` already supports rich text** (`supportRichText` defaults true), so green/amber/red
objective colouring is free *today*. Recommendation: **stay on legacy `Text`** and spend the
budget elsewhere. Comms subtitles + squelch, intel pickups, briefing screens. **No voice
acting** — the single most expensive line item, and subtitles cover it.

**C7 — mission select.** A `RowCampaign` on `MainMenuPanel` (a const + `RowCount` bump + an
`Activate` case + an `AppendRow`), a `MissionSelectPanel`, per-mission ratings. The choice
travels menu→arena through the **save file**, the only sanctioned channel.

**C8 — data-driven arenas. The largest single refactor; do it LAST.** `BuildRoom` is ~30
literal boxes and `BakeNavMesh` writes to a single const path, so a second arena would
overwrite the first's bake. New `ArenaConfig` SO; one navmesh asset per arena; `RegisterScenes`
and `GreyBoxVerify` generalised from one hardcoded scene to N; and a `TestScenes` constant
replacing the `"10_GreyBox"` string literal in all five PlayMode test files. **Everything in
C1–C7 proves out on `10_GreyBox` alone first** — you get a working campaign on one arena
before touching the builder's arena code.

**C9 — the arc.** Four arenas — `10_GreyBox` reskinned as **Vantage Test Floor**, `11_Depot`
(outdoor yard, long lanes, containers), `12_Substation` (tight, catwalks), `13_Uplink`
(rooftop array + extraction pad). Twelve missions, three per arena, every one composed from
the objective types above and nothing else — that is the test of whether the type list was
right:

| # | Name | Arena | Composition |
|---|---|---|---|
| 1 | SHAKEDOWN | A1 | ReachZone → SurviveWaves(2, drones) → Extract |
| 2 | HARD CONTACT | A1 | KillQuota(12 soldiers) → HoldZone(45 s) → Extract — **first humans** |
| 3 | BLACKOUT | A3 | Interact → DestroyTargets(3) → SurviveWaves(2) → Extract |
| 4 | QUIET PART | A2 | ‖NoAlarm + ReachZone + Interact → Extract — **first stealth** |
| 5 | RECLAMATION | A2 | Escort → HoldZone(60 s, mixed) → Extract — **first escort** |
| 6 | CROSSFIRE | A1 | SurviveWaves(5) + timed HoldZone — pure combat, uses the endless machinery and shop straight |
| 7 | THE MANIFEST | A3 | ‖NoAlarm + KillQuota(officer) → Intel → Extract under 90 s |
| 8 | DEAD AIR | A4 | DestroyTargets(4) → SurviveWaves(3, drone-heavy) → HoldZone(60 s) |
| 9 | SCORCHED | A2 | Three chained timed ReachZones → Extract — the only mission with no shop |
| 10 | THE COMPANY MAN | A1 | Escort ‖ NoAlarm, where the alarm activates a contingency step list instead of failing |
| 11 | HANDOVER | A3 | HoldZone(90 s) ‖ DestroyTargets(2) during the hold — largest wave in the game |
| 12 | VANTAGE | A4 | DestroyTargets(3) → KillQuota(Tank + escort) → Extract under 120 s |

**Ship missions 1 and 2, and play them, before authoring 3–12.**

---

## Execution order — one sequence across three tracks

```
0.  Play session (9-item card) + Mixamo profiling spike + CLAUDE.md amendment
1.  G0 measure · G1 defects · G2 viewmodel camera          ← the image transforms here
2.  W1 foundations + the pellet bug · W2 the TTK model
3.  C1 mode axis · C2 save v4 · C3 the seven WaveRunner additions
4.  G7 the grade (1 session, free) · G3 impacts + tracers
5.  C4 objectives · C5 zones+interaction · MissionDirector
6.  C6 mission UI · C7 campaign menu
7.  Mission 1 SHAKEDOWN → PLAY IT.  Mission 2 HARD CONTACT → PLAY IT.
8.  G8 the art seam  ← build the slot before the money lands
9.  Buy pack #1. G9a free art · G4 light · G5 audio
10. E1 animation · E2 hitboxes · E3 telegraph · E4 stage 1 · E5 death
11. W3 the guns · W4 projectiles · W5 attachments · G6 viewmodel feel
12. C8 data-driven arenas · arenas 2-4
13. Missions 3-12 · E4 stage 2 cover · E6 perf pass
```

Steps 1–7 deliver **a playable two-mission campaign with a transformed image, before a dollar
is spent and before the builder's arena code is touched.** That is the ordering that makes
this recoverable if any of it turns out not to be fun.

---

## The money — ~$150–300

| Item | Source | Cost |
|---|---|---|
| **Character animation** | **Mixamo** — free, no attribution, commercial, ~2500 clips + auto-rigger. **This is the line item that obsoletes CLAUDE.md's founding premise.** | **$0** |
| Soldiers + weapons + modular environment | **Synty POLYGON Military / War / Spec Ops** (frequent 50% sales). One atlas per pack = one draw-call family, near-ideal for 4 GB. **Verify it advertises Unity Humanoid rigs.** | $30–60 |
| Second environment pack (arenas 2–4) | Synty, same family | $30–50 |
| Viewmodel weapons | Stay inside the same look. CLAUDE.md permits 2048 for weapons/hands — spend slightly more here, it is on screen 100% of the time | $15–40 |
| URP VFX pack | **Verify URP compatibility explicitly** — many packs are Built-in RP only and render magenta | $25–45 |
| Radio / military SFX + barks | Sonniss GDC bundle is free and annual; buy only what is missing | $0–40 |
| GitHub LFS data pack, if needed | 50 GB storage + bandwidth | $5/mo |
| **Reserve** | You will discover one thing you need | **$50–80** |

**Do not buy:** photoreal AAA environment packs (4K blows VRAM *and* clashes with everything
else), HDRP-only anything, packs needing Amplify/Shader Forge, or any "Ultimate FPS Kit"
whole-game framework — it would fight `WeaponController`, the pool, every guard and
`GreyBoxVerify` simultaneously. **Wait for a sale; the same $200 buys roughly twice as much.**

---

## What breaks, and the resolution

| Gate | Breakage | Resolution |
|---|---|---|
| `WeaponDataTests.EveryWeapon_LandsInsideTheArcadeTtkWindow` | Sniper/launcher report TTK 0 → fail `≥200` | Fix the model, then per-class laws (W2). Never author a 99-damage sniper |
| `WeaponConfig.OnValidate` 150–500 ms warning | Screams on every sniper asset forever | Make it class-aware in the same commit |
| `RenderingTests.Arena_IsLit` (:89) | Counts `LightType.Point`; G4 converts to spots | Widen to "enabled, non-directional, not under a Camera" in the same commit, with a comment |
| `RenderingTests.AssertPostProcessingIsLive` (:46) | Asserts the **base** camera; camera stacking may move post | Only if needed: widen to "the base **or any camera in its stack**", in the same commit. **Do not silently loosen it** |
| `HordeLoadTests` | Doc strings say 40; humanoid cap is lower | `FillToCap` is cap-relative so it stays green. Update the doc strings + CLAUDE.md's S4 row, and **add** an Animator/SkinnedMeshRenderer count assertion so a shadow regression is machine-caught |
| `GreyBoxLoopTests.NavMesh_CoversTheArena_WithNoIslands` | Moved spawn points; humanoid agent radius/height differ | Re-bake with a human agent profile, `baseOffset = 0`, every spawn point on the mesh |
| `GreyBoxVerify` | **Any renamed serialized field is fatal** | Rename types via `git mv`; never rename a field the verifier names. Add `Check`s for every new reference |
| `LoadOrCreate` (:2561) | Runs `configure` **on create only** — a renamed asset path silently creates a fresh default and reports success | Do not rename data assets. When you eventually do, change every string path in one commit |
| `ShopServiceTests` | Weapon path moves from `EquipWeapon` to `AcquireWeapon` | Update in the same commit; add a "both slots full replaces active" case |
| `Stat` / `StatExtensions.Count` | Extending resizes `StatSheet` arrays, ripples into passives/money/health | **Do not extend it.** Separate `WeaponStat` enum + sheet |
| `Tools/check.mjs` | A Synty import trips LFS + meta guards en masse | Import as **one commit**, run the guards immediately |
| `Tools/typecheck.mjs` (zero warnings) | Third-party warnings would break the gate | Add **no new assemblies**; `ThirdParty/` sits outside `Assets/_Project/Scripts` so guards skip it. Mission code lives in `CoD.Waves` so EditMode reaches it with zero asmdef edits |

---

## Verification

**Per commit:**
```
node Tools/typecheck.mjs      # 9 assemblies, zero errors AND zero warnings
node Tools/check.mjs          # six guards + the two new ones (texture budget, LFS budget)
Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode -testResults Logs/tests-editmode.xml
Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode -testResults Logs/tests-playmode.xml
```

**Whenever scenes, Build Settings, save files or `#if UNITY_EDITOR` blocks are touched** —
which is nearly every phase here — **with Unity closed:**
```
node Tools/verify-build.mjs   # builds a real player and RUNS it; requires COD_SMOKE_OK
```
Run it **twice** across the save-schema commit: once before, once after, to prove migration
on a real file written by a real build. That gate is what caught the settings block being
wiped on every death.

**New automated coverage this plan adds:**
- `ViewmodelTests` — culling mask excludes the layer, exactly one overlay in the stack, no second AudioListener
- `MissionObjectiveTests` (EditMode, no scene — objectives poll a hand-built `MissionProgress`)
- `CampaignBootTests` / `CheckpointTests` / `PermadeathIntegrityTests` — **the last one proves a campaign run never writes `bestRound`**
- `MissionFlowTests` (PlayMode, real scene, `SaveFileGuard.CaptureAndReset()` mandatory)
- `ArenaDesignTests` — every arena's navmesh has no islands
- An Animator/SkinnedMeshRenderer count assertion inside `HordeLoadTests`

**What no machine can verify — put these on the tuning card:**
- **Frame time.** `-batchmode -nographics` does almost no GPU work. Measure manually at
  1080p: the current arena at 40 drones (baseline, G0), and 18 soldiers in the worst-case
  corner (E1). Target ≤ 8 ms CPU main thread, ≤ 10 ms GPU. Write the measured number into
  CLAUDE.md's S4 row.
- **VRAM.** Memory Profiler snapshot from a **Development build** (the editor inflates
  everything), plus `CoD → Report Texture Budget`. Hard cap 700 MB textures, target 450 MB.
  Commit the table into `docs/systems/rendering.md` and diff it every phase.
- **Is it fun.** Missions 1 and 2 get played before 3–12 exist.

**Docs to update in the same task as the code** (repo rule): `docs/systems/rendering.md`,
`weapons.md`, `drones.md`, `waves.md`, `save.md`, `performance.md`, `settings.md`, plus new
`campaign.md`, `audio.md`, `art-pipeline.md`.
