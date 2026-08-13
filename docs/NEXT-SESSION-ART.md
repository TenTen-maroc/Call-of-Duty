# The art session — a self-contained prompt

> Status 2026-08-13: G8 is complete, green, committed, and was visually neutral.
> G9a sources 1–3 are integrated: ten 1K ambientCG CC0 surfaces plus Poly Haven
> Autoshop 01 as a 128 px specular reflection cubemap. The complete arena kit
> adds 19 collider-free `Art` children while keeping the original 19
> BoxColliders. Measured texture-memory deltas are 19.9 MB and 0.2 MB; Kenney adds
> 18 CC0 clips and 0.8 MB measured audio memory at zero VRAM. Weapon and enemy
> kits remain null. Sonniss is the next free source.

Paste the block at the bottom into a fresh Claude Code session opened at the repo
root. Everything above it is the reasoning behind the block; the block itself is
written to be executed without any of this context.

**Why this is its own handoff.** Art is the one track where a wrong move is
expensive and hard to reverse: a bought pack cannot be un-bought, an imported
4 K texture set eats a quarter of the target card, and forty new meshes dropped
into a project whose scenes are *generated* would fight the builder that owns
them. Every other track in this project can be reverted with `git revert`. This
one can cost money and VRAM, so it gets a gate — `G8`, the art seam — and the
gate lands **before** anything is downloaded or bought.

Read [PLAN-GRAPHICS-AND-GUNS.md](PLAN-GRAPHICS-AND-GUNS.md) §G8 and §G9 for the
full argument. The short version is in the prompt.

---

```text
You are continuing a solo-built Unity 6 + URP horde-survival FPS with a story
campaign, at the repo root. Autopilot is on: commit and push without asking.

THIS SESSION IS THE ART TRACK. It has one gate in it and the gate comes first.

FIRST, IN THIS ORDER:
1. CLAUDE.md — the contract. Locked decisions, conventions, current state table.
2. docs/PLAN-GRAPHICS-AND-GUNS.md — read §1 (what "like Call of Duty" means),
   §G8 (the art seam) and §G9 (buying). §G8 is this session's work.
3. docs/systems/README.md, then rendering.md, arena.md and performance.md.

WHERE IT STANDS
Eight weapons, a two-mission campaign, a wave loop with a shop, three drone
archetypes, a full HDR grade, a separate viewmodel camera, tracers, per-surface
impacts, and a hand-authored AudioMixer with footsteps and ambience routed
through it. 242 tests (182 EditMode + 60 PlayMode), 8 guards, 9 clean assemblies,
a release .exe that boots. Unity 6000.0.81f1.

THE IMAGE TODAY: 30 materials, ONE texture (procedurally generated), ZERO
imported meshes. Every wall, crate, drone and gun is a primitive created by
Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs. Render targets at 1080p total
~110-130 MB, so the 4 GB VRAM budget is NOT binding today — and binds hard the
moment textures arrive.

=== G8 — THE ART SEAM. BUILD THIS BEFORE DOWNLOADING OR BUYING ANYTHING. ===

A 3000-line editor script generates every scene from primitives. "How does
bought art coexist with that" has exactly one good answer:

  GreyBoxBuilder KEEPS OWNING THE SCENE. ART BECOMES DATA.

Add ArenaKitConfig / WeaponKitConfig / EnemyKitConfig ScriptableObjects holding
OPTIONAL prefab and material references. GreyBoxBuilder.BuildRoom's ~30 AddBox
calls become AddBlock(..., kit.wallModule, kit.wallMaterial), where AddBlock:

  1. ALWAYS creates the box collider from the same position and scale;
  2. instantiates the art prefab as a child named "Art", EVERY COLLIDER STRIPPED;
  3. falls back to CreatePrimitive(Cube) when the kit field is null.

THE LOAD-BEARING RULE: COLLISION AND NAVMESH COME FROM THE BOX, ART COMES FROM
THE PREFAB. ART NEVER CHANGES GAMEPLAY GEOMETRY. Consequences that are the whole
reason this is worth two sessions:

  - the navmesh bake is byte-identical with or without art, so pathing cannot
    break;
  - every gameplay test — hitscan, pooling, HordeLoadTests, the launcher, the
    campaign — is unaffected by an art swap;
  - IT IS REVERSIBLE. Null the kit fields, rebuild, and you are back to a
    shippable grey box. If the art never arrives, you still have a game.

GreyBoxVerify gains VerifyKits() enforcing ALL-NULL-OR-ALL-NON-NULL PER KIT. A
MIXED kit is the real failure mode, because it produces a scene that looks
half-built and verifies clean.

ACCEPTANCE CRITERION, AND IT IS NOT NEGOTIABLE: the entire suite passes with
every kit field STILL NULL, and `node Tools/screenshot.mjs` renders frames
identical to today's. G8 changes nothing you can see. That is how you know the
seam is a seam and not a rewrite.

ALSO IN G8, and it is what makes the texture rule automatic rather than
aspirational: committed TextureImporter / ModelImporter PRESETS plus an
ArtImportPostprocessor that stamps settings by folder. Without it, one $60 pack
silently imports forty 4 K textures and guard-texture-budget fails AFTER the
LFS objects are already committed. Stamp on import, not after.

=== G9a — FREE ART FIRST, AND IT IS NOT A CONSOLATION PRIZE ===

Only after G8 is committed and green. Ten CC0 materials is the whole arena.

  ambientCG            CC0            PBR materials. Download 1K, NEVER 4K.
  Poly Haven           CC0            HDRIs. One -> a 128 cubemap fixes the
                                      reflection fallback for ~1 MB.
  Kenney               CC0            impact / sci-fi / interface sounds.
  Sonniss GDC bundle   royalty-free   gun tails, impacts, room tone. KEEP IT
                                      ENTIRELY OUTSIDE THE REPO; export trimmed
                                      clips only.
  Unity Particle Pack  free           URP-compatible VFX.

ONE SOURCE PER COMMIT, each with its own VRAM and LFS measurement in the commit
message. Never import two before measuring one — that is how you end up unable
to attribute a 400 MB jump.

=== G9b — PAID, AND ONLY WITH EXPLICIT PERMISSION ===

DO NOT SPEND MONEY. Art packs are unbought by the user's standing decision. If
the work reaches the point where a purchase is the next step, STOP, say exactly
what you would buy and why, and let the user decide. Everything in G8 and G9a is
free and is worth more than the paid step that follows it.

For reference only, in priority order: one Synty POLYGON environment pack
($30-60, whole pack shares one atlas so VRAM is near zero and everything
batches), a first-person weapon set ($20-40, on screen 100% of the time, 2048 is
permitted here), a URP VFX pack ($25-45, VERIFY URP COMPATIBILITY BEFORE BUYING
— a large fraction are Built-in RP only and render magenta).

=== REFUSE THESE, AND SAY WHY RATHER THAN QUIETLY NOT DOING THEM ===

  photoreal "AAA environment" packs   4K blows VRAM *and* clashes with the
                                      stylised-realistic language this project
                                      committed to. Mismatched fidelity looks
                                      WORSE than an honest grey box.
  anything HDRP-only                  CLAUDE.md forbids HDRP. Not a toggle.
  anything needing Amplify/ShaderForge
  any "Ultimate FPS Kit"              would fight WeaponController, the pool,
                                      every guard and GreyBoxVerify at once.
  4K textures                         20 pairs = ~900 MB, a quarter of the card,
                                      for detail invisible at 1080p on a 3 m
                                      wall. guard-texture-budget blocks it.

=== HARD CONSTRAINTS ===

  RTX 3050 Laptop, 4 GB VRAM. The binding constraint on every art decision.
  Texture Max Size 1024 project-wide; 2048 only for weapons and hands.
  LFS: measure the staged total with guard-lfs-budget before every source commit;
    after Poly Haven it is 29.7 MB of a 400 MB project budget. GitHub free
    is 1 GB storage and 1 GB bandwidth PER MONTH, and one asset pack exceeds it.
    Audio is the sneaky killer at ~10 MB a minute.
  All binaries through LFS. Every asset needs a committed .meta sibling.

=== THE GATES, EVERY COMMIT. UNITY MUST BE CLOSED FOR ALL OF THEM. ===

  node Tools/typecheck.mjs      # 9 assemblies, zero errors AND zero warnings
  node Tools/check.mjs          # 8 guards, incl. texture and LFS budgets
  Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode  -testResults Logs/tests-editmode.xml
  Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode  -testResults Logs/tests-playmode.xml
  node Tools/verify-build.mjs   # builds a real player and RUNS it
  node Tools/screenshot.mjs     # renders 8 frames from the built player -- LOOK AT THEM

ART IS THE ONE TRACK WHERE THE SCREENSHOTS ARE THE POINT. To prove a change is
neutral (which G8 must be), compare against a baseline: `git stash -u`, run
screenshot.mjs, look, `git stash pop`, run it again. That is how "no visual
regression" becomes a claim rather than a hope.

=== THE BUILDERS, AND THE ORDER THEY RUN IN ===

Scenes and prefabs are GENERATED and committed. Editing a builder changes
NOTHING until it is re-run.

  1. CoD -> Build Grey Box     GreyBoxBuilder.BuildHeadless
  2. CoD -> Build Arsenal      ArsenalBuilder.BuildArsenalHeadless
  3. CoD -> Build VFX          VfxBuilder.BuildVfxHeadless
  4. CoD -> Build Grey Box     again — this is what puts newly-created prefabs
                               into the pool prewarm list
  5. CoD -> Wire Scene Extras  SceneWiring.WireSceneExtrasHeadless
  6. CoD -> Verify Grey Box    GreyBoxVerify.VerifyHeadless

MissionBuilder and AudioBuilder author ASSETS ONLY and have no scene ordering.

=== HARD-WON GOTCHAS. EVERY ONE OF THESE COST A SESSION ONCE. ===

- THE BUILDER IS THE GAME. Never call SaveScene(GreyBoxScenePath) mid-build; one
  attempt did, and would have overwritten the working arena with a scene
  containing no player, no HUD and no object pool.
- STEP 4 ABOVE IS NOT SUPERSTITION, AND STEP 5 IS THE ONE PEOPLE FORGET.
  GreyBoxBuilder does not EDIT 10_GreyBox, it writes a brand-new scene over the
  top — so every component SceneWiring added is gone after a rebuild, silently,
  with no error. The scene comes back whole and quiet, five objects short. After
  every rebuild, diff the OBJECT COUNT and the set of `m_Name:` values against
  HEAD; fileID churn hides it completely in a normal diff.
- RE-RUNNING THE BUILDER ALWAYS PRODUCES A SCENE DIFF even when nothing changed:
  Unity assigns fresh local fileIDs on every regeneration, so a no-op rebuild
  shows thousands of lines with roughly equal insertions and deletions. Check
  the counts before assuming you broke something.
- SetRef(component, "_field", value) WIRES BY STRING and GreyBoxVerify re-checks
  by string. A typo is a silent null no compiler catches.
- LoadOrCreate<T>(path, configure) RUNS configure ON CREATE ONLY. A renamed
  asset path silently creates a fresh default, discards all tuning, and reports
  success.
- ABSENCE IS NOT NEUTRALITY. A reflection source set to "nothing" is a
  reflection of BLACK — it turned every metal surface, including the weapon,
  matte black, and no gate could see it.
- ASSIGNING AN ASSET REFERENCE INTO A SCENE THAT HAS NEVER BEEN SAVED silently
  does not persist. Scene-object references survive, asset ones do not, and
  nothing errors. GreyBoxVerify re-opens, repairs, then re-opens to prove it
  stuck.
- A UnityEngine.Object HANDLE DOES NOT SURVIVE A SCENE SWITCH. Load assets AFTER
  opening the scene that needs them.
- VIEWMODEL PARTS AND DRONE SHAPE DETAILS CARRY NO COLLIDERS. Art children carry
  none either — that is the whole seam.
- Opening the project rewrites Packages/manifest.json. Check it before
  committing.

=== NON-NEGOTIABLE RULES ===

Every tunable in a ScriptableObject; everything spawned goes through the pool;
no per-frame allocation; no mutable statics (Domain Reload is OFF); nothing
writes to a ScriptableObject at runtime; #nullable enable line 1; zero warnings.
maxAliveDrones 40 and maxSimultaneousAttackers 3 are not knobs.

NEVER weaken a test or a gate to go green. If a test must change it must get
STRONGER, with the reason in the diff. Reverting a broken round is correct and
has precedent — two tracks were reverted rather than shipped.

Update docs/systems/rendering.md and arena.md in the SAME commit that changes
them, and record the VRAM measurement in rendering.md under Budget so it can be
diffed next phase.

=== WHAT NO MACHINE HERE CAN ANSWER ===

Whether it looks right, and what it costs in frame time on an RTX 3050 Laptop
with 4 GB. Headless runs do almost no GPU work. Measure at wave 12 with 40
alive, before and after every phase, from a DEVELOPMENT build — never the
editor, which inflates everything.

AND THE ONE BEFORE THAT: nobody has played this game yet. If a play report says
the grey box is not the problem, believe the report over this plan.
```
