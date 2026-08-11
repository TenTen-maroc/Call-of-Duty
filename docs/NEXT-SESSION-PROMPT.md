# Next session prompt

Paste the block below into a fresh Claude Code session opened at the repo root.
It is deliberately self-contained: a new session has none of this context, and
the gotchas listed are ones that already cost time once.

Keep this file updated as milestones land — it is the handoff, not a snapshot.

---

```text
/goal Continue Call of Duty — a fixed-arena horde-survival FPS in Unity 6 (URP),
offline single-player, Windows. Read @CLAUDE.md and @docs/systems/README.md
before doing anything, then the specific docs/systems file for whatever you touch.

WORK IN PLAN MODE FIRST. Read-only. Produce a plan and WAIT for approval.
Do one milestone per session and STOP at the end of it.

ABOUT ME
- Solo dev, full-time. I know TypeScript well, C# barely. When you use a
  C#-specific idiom (properties, coroutines, attributes, structs by ref), add a
  one-line inline note. Do not explain general programming.
- Hardware: RTX 3050 Laptop, 4 GB VRAM. Texture budget and spawn counts matter
  far more than poly count.

WHERE THINGS STAND
Phases 0-3 are done and the game RUNS. Unity 6000.0.81f1 + URP, Personal licence
active. The grey box has: first-person movement (walk/sprint/crouch/jump), mouse
look with deterministic recoil, a hitscan rifle with bloom/falloff/reload
cancelling, an 8-block viewmodel with sway and bob, object pooling for every
spawn, impact decals and sparks, a hitmarker with distinct kill feedback, a
bloom-tracking crosshair, ammo/health HUD, placeholder audio, and a dev-gated
cheat console (backquote, then 1-5).

AR_Standard is 700 RPM at 25 damage: 4 shots to kill 100 HP, ~257 ms TTK.
Movement speed, arena scale and spawn distance are all tuned around that number.
Change it deliberately or not at all.

Verified in play: movement, look, sprint, firing, ammo, HUD, audio, crosshair.
NOT verified: damage falloff at range, crouch headroom blocking, sprint-to-fire
delay, and whether the pool actually reuses rather than grows.

TOOLS — USE THESE, THEY ARE FASTER THAN THE EDITOR
- node Tools/check.mjs      six guards; also runs on every commit
- node Tools/typecheck.mjs  compiles EVERY assembly using Unity's own Roslyn,
                            without opening Unity and without a licence. Run it
                            after every edit. It has caught real bugs the editor
                            would only have shown minutes later.
Both run in the pre-commit hook, and warnings count as failure.

SCENES AND PREFABS ARE GENERATED, NOT HAND-AUTHORED
Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs builds every prefab, both
scenes, the materials and the tuning assets. Menu: CoD → Build Grey Box.
Headless (Unity must NOT be open — it locks the project):
  "C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Unity.exe" \
    -batchmode -quit -projectPath . \
    -executeMethod CoD.EditorTools.GreyBoxBuilder.BuildHeadless -logFile -
Extend the builder rather than hand-editing a .unity file. A scene assembled by
hand cannot be reviewed, re-created, or explained.

CLOSE THESE LOOSE ENDS FIRST
1. REBUILD THE GREY BOX before playing: run CoD -> Build Grey Box in the editor
   (or headless with Unity closed). The 2026-08-11 audit session changed the
   builder AFTER the committed scenes were generated: Target_Dummy gained a
   head Weakpoint collider and a TargetRespawn, casings moved to the Ignore
   Raycast layer, and the player's Health is wired to GameConfig. The committed
   scenes/prefabs predate those changes; the builder is the source of truth.
2. Play-test what the audit wired but nobody has run: burst fire (flip
   AR_Standard's fireMode in the Inspector), headshots on the dummy's head
   (1.5x, weapon-owned — HealthConfig.weakpointMultiplier was deleted, the
   weapon's headshotMultiplier is the ONE owner), target respawn after 2.5 s,
   casing ejection arcs, godmode under fire, and holding the trigger through
   an empty-mag auto-reload.

THE ROADMAP — ONE PER SESSION, IN THIS ORDER

PHASE 4 — the Rusher drone
  DroneConfig + AttackModule ScriptableObjects (shapes are in
  @docs/DATA-MODEL-SKETCH.md — follow them). One DroneController reads both;
  drone #4 must be data, never new code. NavMesh agent via the AI Navigation
  package (already installed), baked once on the arena. ContactDetonate attack.
  Pooled like everything else, registered in the pool in the SAME commit that
  creates the prefab. A DroneSpawner that takes a count.
  Create the CoD.Enemies asmdef only when it has scripts — an empty asmdef logs
  a console warning and the console is the quality gate.
  DONE = three Rushers chasing me is fun with the AR. Tune until it is.

PHASE 5 — waves and the shop
  WaveConfig assets (Wave_01..Wave_10 hand-authored, then a formula),
  DifficultyConfig with the hard caps, a WaveRunner that runs a timed wave then
  opens a shop break, permadeath, and a versioned JSON save holding best round.
  The two caps are not negotiable: maxAliveDrones 40 protects the 4 GB GPU, and
  maxSimultaneousAttackers 3 is why twenty enemies feels fair instead of
  instantly lethal. Implement the attack-token system properly.
  ShopConfig/ShopItemConfig/PassiveConfig, and the StatSheet rebuild pipeline:
  effective = (base + sum of flatAdds) x product of mults, recomputed from owned
  passives on every purchase. NEVER by writing to a config asset.

PHASE 6 — Shooter and Tank
  Same DroneConfig, different data plus RangedBurst and HeavySlam AttackModules.
  Shooter: reaction delay 0.4 s, first shot a deliberate near-miss, ~0.7
  accuracy. That near-miss converts "I died from nowhere" into "I got caught
  out" — same event, completely different feeling.

PHASE 7 — EffectModules
  Explosive / Pierce / Ricochet / Chain as stateless ScriptableObject rules.
  Read the depth and recursion rules in @docs/DATA-MODEL-SKETCH.md carefully:
  follow-ups resolve at depth+1 and modules only run at depth 0 unless maxDepth
  is set, or Explosive → Chain → Explosive is an infinite loop. Pierce changes
  the ray budget, it is not a follow-up. Then the between-wave shop UI.

HARD-WON GOTCHAS — DO NOT REDISCOVER THESE
- Assigning an ASSET reference to a component in a scene that has never been
  saved silently does not persist. Scene-object references survive, asset ones do
  not, and nothing errors. GreyBoxVerify re-opens the saved scene, repairs, then
  re-opens again to prove it stuck. Do not "simplify" that away.
- Opening the project rewrites Packages/manifest.json. It once re-added
  In App Purchasing (deprecated), Analytics, Timeline and more. Check the
  manifest after any editor run and before committing.
- Every new .cs needs a .meta sibling or the pre-commit hook blocks the commit.
  Unity generates them on focus; the guard is protecting the next clone.
- Viewmodel parts must have NO colliders. A collider on the player's own gun
  sits in front of the camera and every shot raycasts into it.
- The aim ray comes from CameraPivot, not the camera — the camera carries shake,
  and shake must never move the point of impact.
- Configs are read-only at runtime. Domain Reload is disabled, so a runtime write
  to a ScriptableObject persists between Play sessions and rewrites your balance.
- No mutable statics, and no Find/GetComponent/Camera.main/Instantiate/Destroy
  inside Update/FixedUpdate/LateUpdate. Both are enforced by guards.
- Unity's FOV field is VERTICAL. 62 ≈ 95 horizontal.

CONSTRAINTS (from @CLAUDE.md — enforce them on yourself)
- Every tunable number in a ScriptableObject. Zero magic numbers in scripts.
- Everything that spawns goes through the object pool.
- No per-frame allocation: no LINQ, no new collections, no string concatenation.
- #nullable enable atop every first-party file; zero warnings.
- Atomic commits, one subsystem each. Update the matching docs/systems/*.md in
  the SAME task, including its Last verified date and what is actually verified
  in play versus only compiled.

DEFINITION OF DONE FOR A SESSION
- node Tools/typecheck.mjs clean, node Tools/check.mjs all six green
- Unity console clean
- The milestone is FUN, not merely working. Those are different, and only the
  second one matters.

Now: read the files, then show me the plan. Do not write code yet.
```
