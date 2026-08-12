# Rendering

> Last verified: 2026-08-12
> **Verified in play: no.** Compiled, built, and covered by 5 PlayMode tests that
> assert both scenes render with post-processing on and that the profile kept its
> overrides through the save. Whether the bloom intensity *looks* right is a
> tuning-card question, and item 9 — frame time on the 3050 — is the one thing no
> headless run can answer, because a `-batchmode` run does almost no GPU work.

## Overview

The image pipeline: the post-processing stack, the arena's light rig, surface
response on the materials, and the one generated texture. URP 6 (17.0.4),
**Forward+**, [PC_RPAsset](../../Assets/Settings/PC_RPAsset.asset) driving
[PC_Renderer](../../Assets/Settings/PC_Renderer.asset).

**What this replaced: nothing at all.** The project ran its whole life with
post-processing switched off and no gate noticed. The `Main Camera` in every scene
had no `UniversalAdditionalCameraData` component, so URP left `renderPostProcessing`
false. `m_SupportsHDR` was on while `m_ColorGradingMode` was still LDR. There was no
`Volume` anywhere.

The cost of that was already sunk. Three archetype-coloured **emissive** drone core
materials existed, and `DroneController.SetTelegraph` already ramps `_EmissionColor`
to roughly 3.9× through every attack windup — the thing that makes a contact
detonation fair instead of a coin flip. None of it glowed; the values clipped flat.
Everything compiled, every guard passed, all 84 tests were green, and the game
rendered with no tonemapping, no bloom and no anti-aliasing.

A missing component is invisible to every other gate in this repo. That is what
[RenderingTests](../../Assets/_Project/Tests/PlayMode/RenderingTests.cs) is for.

## The renderer

[PC_Renderer](../../Assets/Settings/PC_Renderer.asset) is not the stock forward
renderer, and two of its settings are load-bearing:

- **`m_RenderingMode: 2` — Forward+, not Forward.** The per-object light limit
  (`m_AdditionalLightsPerObjectLimit: 4`) stops applying the way it does in
  Forward: Forward+ culls lights per screen tile rather than per object, so the
  four static arena lights plus the muzzle flash plus an explosion light can all
  reach the same surface without one of them being dropped. That is why the
  limit of 4 is survivable. It costs a depth prepass, which the depth texture
  below was already paying for.
- **One Renderer Feature: `ScreenSpaceAmbientOcclusion`,** active, and nothing in
  this doc previously mentioned it. Settings as committed: intensity `0.4`,
  radius `0.3`, samples `1`, direct-lighting strength `0.25`, `Downsample: 0`
  (full resolution), `AfterOpaque: 0`, `Source: 1` (**DepthNormals**). The
  DepthNormals source is the expensive half of the choice — it makes URP produce
  a full-resolution `_CameraNormalsTexture` in the prepass. `Samples: 1` and a
  0.5 m radius keeps it to contact shadows in corners rather than a general
  dimming; on a grey box with almost no albedo variation it is doing a large
  share of the work that makes a wall meet a floor.

**Fixed — the second, unmanaged volume stack.** `PC_RPAsset.m_VolumeProfile` used
to point at Unity's template `SampleSceneProfile.asset`. The pipeline asset's
profile is the *default* volume: it sits underneath every scene's `Volume` at the
lowest priority, so everything it overrode was in force everywhere unless the
scene volume happened to override the same parameter. That template carried an
active `Bloom` (with its own iteration-count override), an active `Vignette`, an
active `Tonemapping` and a **dormant `MotionBlur`** — one Inspector click from
live, in a file nobody thought of as game content.

Both pipeline assets now point at
[PostFx_QualityBase](../../Assets/Settings/PostFx_QualityBase.asset), which is
deliberately empty: the quality tier contributes nothing, and
[PostFx_Arena](../../Assets/_Project/Data/Game/PostFx_Arena.asset) is the only
place post-processing is authored. `SampleSceneProfile.asset` is deleted.

Bloom's `maxIterations` is now pinned explicitly rather than inherited, so no
future base profile can move it without somebody noticing. (`skipIterations` was
the URP 16 spelling and is obsolete in 17 — the compiler catches it.)

## Data Assets

- **Palette_GreyBox.asset** (`Assets/_Project/Data/Game/`, a `PaletteConfig`) —
  every colour the arena is built from, and the fix for a whole bug CLASS.
  `LoadOrCreateMaterial` returns an existing `.mat` untouched — right for a value
  a human tuned, wrong for a shipped default — so the colour literals that used
  to live in the builder were read exactly once, on the day each material was
  created. The "tactical palette" commit later changed them and **nothing
  happened**: `GreyBox_Floor` shipped at 0.32 grey against an intended 0.17,
  ~1.9× too bright, for the life of the project, with every gate green.
  `ApplyPalette`/`ApplyEmission` now re-assert from this asset on every build,
  the same way `ApplySurface` already re-asserted smoothness and metallic.
  **Tune this asset, not the `.mat`** — the next build overwrites the material,
  which is the right way round.

- **PostFx_QualityBase.asset** (`Assets/Settings/`) — deliberately empty. See
  the fixed second-stack note below.

- **PostFx_Arena.asset** (`Assets/_Project/Data/Game/`, a `VolumeProfile`) — the
  whole stack, shared by the arena **and** the menu. One place to tune, and a menu
  that looks like the game it launches. Built by
  `GreyBoxBuilder.LoadOrCreateVolumeProfile`.

  | Override | Value | Why |
  | --- | --- | --- |
  | `Tonemapping` | **Neutral** | **Not ACES.** ACES desaturates and hue-shifts reds, and every threat here is read by the colour of its core through fog — Rusher red, Shooter amber, Tank crimson. Filmic rolloff is worth having; losing the palette that carries the readability is not. |
  | `Bloom` | threshold 1.05, intensity 0.35, scatter 0.62, HQ filtering **off** | The change that makes the existing emissive cores and the telegraph resolve. HQ filtering stays off for a 4 GB 3050. |
  | `Vignette` | 0.28, smoothness 0.35 | Pulls the eye to the crosshair. |
  | `ColorAdjustments` | contrast +8, saturation −6 | The grey/red tactical palette. |
  | `FilmGrain` | Thin1, 0.15 | Nearly free, and it breaks up surfaces carrying little texture. |
  | `ShadowsMidtonesHighlights` | shadows cool, highlights warm | The single biggest "modern shooter" move, and it reinforces the palette rule — cool is architecture, warm is a threat — in every pixel rather than only the emissive ones. |
  | `WhiteBalance` | temperature −6 | Cools the image toward the tactical palette. |
  | `LiftGammaGain` | small lift | Stops the corners crushing to nothing under the vignette **and** `PlayerDamageFeedback`'s low-health tint, which stack in the same place. Crushed corners hide drones. |
  | `ChromaticAberration` | 0.06 | Reads as a lens. Identity at screen centre, so it never smears the crosshair or the point of impact. |

  Everything from `ShadowsMidtonesHighlights` down folds into the 32³ HDR grading
  LUT the pipeline already builds every frame, so the whole grade costs **no
  additional milliseconds**. That is also why a missing one is invisible, and why
  `RenderingTests` asserts each survived the save.

  **Refused deliberately, and asserted absent:** `MotionBlur` (URP's is
  camera-only — a fast mouse turn smears the whole screen and hides the drone
  about to reach you), `DepthOfField` (1.5–3 ms and it fights target
  readability; ADS-only is defensible later, always-on never is), and
  `PaniniProjection` (62° vertical is not wide enough to need a fullscreen
  distortion pass). `ColorLookup` is *wanted* but absent: it needs a strip graded
  from a real screenshot, which means grading it from the game rather than from
  imagination.

- **Objective_Beacon.mat / Trim_Emissive.mat** — see the colour rule below.
- **Surface_Detail_N.png** (`Assets/_Project/Art/Textures/`) — one shared 1024
  detail normal, **generated once and never rewritten**.

## The colour rule

This is a design constraint, not decoration, and it is why the trim is blue:

- **Warm and bright — red, amber, crimson — is a threat.** Drone cores only.
- **Cool blue is architecture.** Edge trim on the dividers, bunker, pillars and
  the shoot-over cover.
- **Green is help.** The repair beacon, and nothing else in the game.

Lane lights are deliberately dim and desaturated for the same reason: if a wall
can be warm and bright, the player learns to check walls for danger.

## Where it is built

Everything below is generated by
[GreyBoxBuilder.cs](../../Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs); no
scene is hand-authored.

- `LoadOrCreateVolumeProfile` — creates the profile and its overrides.
- `AddOverride<T>` — adds a `VolumeComponent` **and persists it as a sub-asset**.
- `Override<T>` — sets a parameter **and** flips `overrideState`.
- `BuildPostFx` — the global `Volume` object, in both scenes.
- `EnablePostProcessing` — `UniversalAdditionalCameraData` on a camera.
- `BuildArenaLights` / `AddLight` — the four point lights.
- `AddTrim` — an emissive strip with **no collider**.
- `ApplySurface` — smoothness, metallic, and the detail normal.
- `EnsureDetailNormal` / `ValueNoise` / `Hash01` — the generated texture.

## Runtime

- [CameraGraphics.cs](../../Assets/_Project/Scripts/Player/CameraGraphics.cs) —
  applies the player's post-processing and anti-aliasing choices to the camera.
  Subscribes to `SettingsHub.Changed`; **no `Update`**. Lives in `CoD.Player`
  beside `PlayerLook` and `CameraShake` so that `CoD.Core` — which everything
  depends on — never references the render pipeline. See
  [settings.md](settings.md).

## Budget

The binding constraint on this project is **4 GB of VRAM on an RTX 3050 Laptop**,
and it is spent on textures, not geometry. Two things compete for it: the render
targets, which are fixed by the settings below and do not move as content is
added, and the textures, which move every time somebody imports a pack.

### Render targets at 1920×1080

Computed from resolution × format for the CURRENT contents of
[PC_RPAsset](../../Assets/Settings/PC_RPAsset.asset) and
[PC_Renderer](../../Assets/Settings/PC_Renderer.asset). **These are arithmetic,
not measurement** — the setting each row is derived from is named so the row can
be re-checked when a setting changes. 1920×1080 is 2,073,600 pixels, so 1 B/px is
2.07 MB.

| Target | From | Format | Size |
| --- | --- | --- | --- |
| Camera colour ×2 (post ping-pong) | `m_SupportsHDR: 1`, `m_HDRColorBufferPrecision: 0` → 32-bit | R11G11B10, 4 B/px | 16.6 MB |
| Camera depth attachment | always | D32_SFloat_S8_UInt, 8 B/px allocated | 16.6 MB |
| `_CameraDepthTexture` | `m_RequireDepthTexture: 1` | R32_SFloat, 4 B/px | 8.3 MB |
| `_CameraNormalsTexture` | SSAO `Source: 1` (DepthNormals) | 4 B/px, full res | 8.3 MB |
| `_CameraOpaqueTexture` | `m_RequireOpaqueTexture: 1`, `m_OpaqueDownsampling: 1` (½ res) | 960×540, 4 B/px | 2.1 MB |
| Main light shadow atlas | `m_MainLightShadowmapResolution: 2048`, `m_ShadowCascadeCount: 4` | 2048² depth, four 1024² tiles | 16.8 MB |
| Additional light shadow atlas | `m_AdditionalLightsShadowmapResolution: 2048` | 2048² depth | **0 MB today** |
| SSAO target + blur | Renderer Feature, `Downsample: 0` | R8 full res, ping-pong | ~5 MB |
| SMAA edge + blend | camera post AA, the shipped default | R8G8 + RGBA8, full res | ~12 MB |
| Bloom mip chain ×2 | `PostFx_Arena` | from ½ res down | ~5.5 MB |
| Colour grading LUT | `m_ColorGradingMode: 1` (HDR), size 32 | 1024×32 strip | 0.3 MB |
| | | | **≈90 MB** |

Two lines in that table are worth arguing about before any content is added:

- **The additional-light atlas costs nothing today because nothing uses it.** All
  four arena lights are `LightShadows.None`. Give any one of them a shadow and
  16.8 MB is allocated for the atlas — plus the per-frame cost of another shadow
  pass, which is the part that shows up on the 3050, not the memory.
- **`m_RequireOpaqueTexture` is on and nothing currently reads it.** Nothing in
  the project samples `_CameraOpaqueTexture` (no refraction, no distortion, no
  glass). It is 2.1 MB and a full-screen copy every frame for a feature that is
  not in use. Turning it off is a free win the moment somebody confirms that in
  the Frame Debugger; leave it until then, because a shader that quietly needs it
  fails as an invisible object, not as an error.

Depth-format padding and driver allocation granularity make the total soft by
roughly ±20 MB. The real figure comes from a snapshot, not from this table.

### Texture budget

| | Budget | What it means |
| --- | --- | --- |
| Target | **450 MB** | Where the project should sit. Leaves room for meshes, shader variants, the ~90 MB above, and the driver's own reserve on a laptop that is also driving a desktop. |
| Hard cap | **700 MB** | Past this, the 4 GB card is at real risk in a built player: the failure is a hitch when a new drone type first appears, or an allocation failure minutes into a run. |

What 450 MB actually buys, in BC7/BC5 with mip maps (mips add a third):

| Max Size | Per texture | Fits in 450 MB |
| --- | --- | --- |
| 4096 | 22.4 MB | 20 |
| 2048 | 5.6 MB | 80 |
| 1024 | 1.4 MB | **321** |
| 512 | 0.35 MB | 1285 |

This is why CLAUDE.md says 1024 project-wide and why
[guard-texture-budget.mjs](../../Tools/guards/guard-texture-budget.mjs) enforces
it. Texture memory scales with **area**: 4096 → 1024 is 16× less, every time.
Twenty 4K albedo/normal pairs — forty textures, one modest environment pack —
is 896 MB, *past the hard cap on its own*, for detail that is invisible at 1080p
on a 3 m crate. The same forty at 1024 is 56 MB, or 42 MB if the albedos go to
BC1. Weapons and hands may sit at 2048 because the viewmodel fills a third of the
screen for the entire run; nothing else earns it.

Where the project stands right now: **one texture**, the generated 1024 detail
normal. There is enormous headroom. This is a discipline problem, not a capacity
problem, and the guards exist so it stays one — including
[guard-lfs-budget.mjs](../../Tools/guards/guard-lfs-budget.mjs), because the
other price of a 4K import is an LFS object that is billed forever.

`CoD → Report Texture Budget`
([ArtReport.cs](../../Assets/_Project/Scripts/Editor/ArtReport.cs)) prints the
running total by folder against these two numbers. Its figures are editor-side
and **overstate** a shipping player.

### Nothing here can measure frame time

**No automated gate in this repo can answer "does it hold 60 fps on the 3050",
and none ever will.** Both test suites and `Tools/verify-build.mjs` run with
`-batchmode -nographics`, which does almost no GPU work — no shadow passes, no
post-processing, no fill. A green run proves the code executes, not that the
frame fits. That is tuning-card item 9, and it is a human with a laptop.

The tools that can answer it, and what each one is for:

| Tool | Answers | The catch |
| --- | --- | --- |
| **Memory Profiler** snapshot | Actual VRAM by object, the real total | Take it against a **Development build**, never the editor. The editor holds uncompressed copies, editor-only assets and the whole asset database, and inflates everything. |
| **Frame Debugger** | The real pass list and target formats — the only way to confirm the table above | Attach to a Development build; the editor's pass list is not the player's. |
| **RenderDoc** | Per-draw GPU timing and real allocation sizes | Capture from the built `.exe`. This is the one that says *which pass* costs the frame. |
| **`nvidia-smi`** | Total process VRAM from outside Unity, including driver overhead | `nvidia-smi --query-gpu=memory.used --format=csv -l 1`. The only number that includes everything, and the only one that matters when the card runs out. |
| Unity Profiler, GPU module | Frame time split by pass, live | Development build, and connect over the network rather than running it on the same GPU. |

## Key Behaviours & Non-Obvious Patterns

- **Volume overrides must be sub-assets.** `VolumeProfile.Add` only fills an
  in-memory list; the URP inspector is what normally calls
  `AssetDatabase.AddObjectToAsset`. Skip it from a script and the profile saves
  referencing objects that were never written — an empty profile in the Inspector,
  and no post-processing at all at runtime. Same silent-null class as the scene
  asset references `GreyBoxVerify` exists to repair.
- **`overrideState` or it does nothing.** A `VolumeParameter` whose override flag
  is false is ignored no matter what value it holds. Assigning without it looks
  completely correct.
- **`sharedProfile`, never `profile`.** Reading `.profile` clones the asset, and
  the scene ends up owning a private copy that silently stops tracking the one
  everything else tunes.
- **MSAA is off, and stays off.** MSAA lives on the `UniversalRenderPipelineAsset`,
  so changing it at runtime is a write to a ScriptableObject — and Domain Reload is
  off, so that write survives into the next Play session and rewrites the shipped
  default. Anti-aliasing is the **camera's** post AA (SMAA by default), which is
  scene state and dies with the scene.
- **The sun is the only shadow caster.** All four arena lights are
  `LightShadows.None`. Four extra shadow maps is real frame time on the exact
  hardware tuning-card item 9 asks about.
- **`m_AdditionalLightsPerObjectLimit` stays at 4.** The explosion light and the
  muzzle light can already reach a surface alongside the static ones. URP degrades
  by picking the strongest rather than failing.
- **Trim carries no collider.** `BakeNavMesh` collects from `PhysicsColliders`, so
  a trim strip built with the collider `CreatePrimitive` hands out would carve a
  floating obstacle into the drone navmesh — drones pathing around thin air, which
  reads as broken AI rather than a build mistake.
- **`ApplySurface` always applies.** `LoadOrCreateMaterial` returns an existing
  material untouched, which is right for values a human tuned. These are shipped
  defaults being *introduced*, so they are re-asserted the way `SetRef` re-links a
  reference — otherwise the materials already on disk keep the old flat look
  forever and the change appears to do nothing.
- **`_NORMALMAP` or the bump map is ignored** — the same trap as `_EMISSION` on
  the drone cores.
- **The detail normal is written once.** A `.png` goes through Git LFS, the free
  quota is 1 GB storage and 1 GB bandwidth a month, and regenerating on every build
  would push a fresh ~635 KB object every time the menu item is clicked.
  `EnsureDetailNormal` returns early when the file exists. The noise is seeded and
  hash-based, never `UnityEngine.Random`, so regenerating after a delete produces
  the same bytes.
- **Tiling cannot be per-object.** `AddBox` scales cubes and a scaled cube keeps
  0..1 UVs per face, so every wall shares one material and therefore one tiling
  (floor 24, walls 10). Close enough for a detail map; the alternative is a
  material per block.
- **Editing `PC_RPAsset` is enough for the built player too.**
  `QualitySettings.asset` has zero `renderPipeline` overrides, and the Mobile level
  excludes Standalone.

## Gotchas

- The camera's `m_AllowMSAA: 1` is a no-op while the pipeline asset has MSAA off.
- The post `Vignette` sits underneath `PlayerDamageFeedback._lowHealthTint`, which
  is a full-screen uGUI image. Not a conflict — different layers — but tune them
  together or the low-health cue reads as too strong.
- Screen Space Overlay canvases draw **after** post-processing, so menu and HUD
  text is never grained or vignetted. That is why the menu can share the arena
  profile safely.
- A `-batchmode` test run does almost no GPU work, so **no automated gate can
  measure what any of this costs in frame time.** The manual tools that can are
  named under [Budget](#nothing-here-can-measure-frame-time).
- The pipeline asset carries its own **default** volume profile that blends
  UNDERNEATH every scene volume. It is deliberately empty here; if post-processing
  ever behaves in a way `PostFx_Arena` does not explain, that asset is the first
  place to look. See [The renderer](#the-renderer).

## Related Systems

- [settings.md](settings.md) — the player-facing post/AA rows and schema 3.
- [arena.md](arena.md) — the geometry the lights and trim sit on.
- [performance.md](performance.md) — what is and is not measurable headlessly.
- [drones.md](drones.md) — the emissive cores and the attack telegraph this
  finally resolves.
