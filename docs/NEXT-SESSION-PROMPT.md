# Next session prompt

Paste the block below into a fresh Claude Code session opened at the repo root.
It is deliberately self-contained: a new session has none of this context, and
the gotchas listed are ones that already cost time once.

Keep this file updated as milestones land — it is the handoff, not a snapshot.

---

## Read this first: the tuning card

Phases 4-7 are built, tested and **never played**. 42 automated tests prove the
loop *runs* — drones spawn, path, die, pay out, the shop opens, the pool
recycles — so anything you find now is a feel problem, not a broken build. The
bar in this repo is FUN, and that is the one thing the machine cannot check.

Open `Assets/_Project/Scenes/10_GreyBox.unity` and press Play. Backquote opens
the sandbox console (1-9). Work down this list; for each one, the asset field to
move is named.

| # | What to feel | If it is wrong |
| --- | --- | --- |
| 1 | **Three Rushers with the AR.** Console 6 spawns a burst. Does the chase read? Is 0.55 s of fuse enough warning? Does the blast make you respect them? | `Drone_Rusher.moveSpeed` (6.0, between walk 5.2 and sprint 8.0) · `ContactDetonate_Std.fuseSeconds` · `.damage` (24) |
| 2 | **Wave 5, ~15 alive.** Does the 3-attacker cap read as fair, or do the extras look passive and stupid? The console shows `attacking / cap` live. | `Difficulty.maxSimultaneousAttackers` — raise to 4 before you touch anything else |
| 3 | **The first shop break.** Is four offers plus a reroll a real decision on wave-3 money, or an obvious pick? | `Shop.offersPerBreak` · `Shop.rerollBaseCost` · the passives' costs |
| 4 | **A Shooter's opening shot.** It misses on purpose. Does that teach you where it is, or just annoy? | `RangedBurst_Std.firstShotMissDegrees` · `.reactionDelay` (0.4) |
| 5 | **A Tank at wave 7.** Is walking away the obvious answer, or does it feel like a wall you cannot fight? | `Drone_Tank.maxHealth` (600) · `HeavySlam_Std.windupSeconds` (0.9) |
| 6 | **Explosive + Chain on the rifle.** Console 9 for money, buy both. Absurd in the good way, or a frame-rate event? | `Effect_Chain.jumpsPerHit` · `Effect_Explosive.radius` · both `maxDepth` (1) |
| 7 | **The SMG vs the rifle.** Buy it at wave 3+ ($500). Is the trade — faster, snappier, useless past 30 m — a real choice or an obvious upgrade? | `SMG_Rapid.falloffRange` · `.bodyDamage` |
| 8 | **The arena lanes.** Does breaking line of sight actually change a fight, or do drones still arrive as one mass? | the block positions in `GreyBoxBuilder.BuildRoom` |
| 9 | **40 alive on the 3050.** Watch the frame time. The cap exists for the 4 GB budget. | `Difficulty.maxAliveDrones` — the one number not to raise |

Values edited in Play Mode **persist**, because they live on ScriptableObjects.
That is the point: tune while playing, stop, and the numbers are still there.

---

```text
/goal Continue Call of Duty — a fixed-arena horde-survival FPS in Unity 6 (URP),
offline single-player, Windows. Read @CLAUDE.md and @docs/systems/README.md
before doing anything, then the specific docs/systems file for whatever you touch.

WORK IN PLAN MODE FIRST. Read-only. Produce a plan and WAIT for approval.

ABOUT ME
- Solo dev, full-time. I know TypeScript well, C# barely. When you use a
  C#-specific idiom (properties, coroutines, attributes, structs by ref), add a
  one-line inline note. Do not explain general programming.
- Hardware: RTX 3050 Laptop, 4 GB VRAM. Texture budget and spawn counts matter
  far more than poly count.

WHERE THINGS STAND
The whole loop is code-complete: rifle, three drone archetypes, timed waves,
a between-wave shop selling passives and effect modules, permadeath with a
versioned save, and four stacking effect modules with depth-limited recursion.
Seven assemblies, all clean, six guards green, and GreyBoxVerify proves every
scene reference survives a save/reload round trip.

Phase 3 (the grey box) is verified in play. Phases 4-7 are verified by TEST but
never felt: 42 tests (37 EditMode, 5 PlayMode) prove the maths and prove the loop
actually runs, so a problem you hit now is a feel problem rather than a broken
build. The tuning card at the top of docs/NEXT-SESSION-PROMPT.md lists what to
judge and which asset field to move. docs/AUTOPILOT-PLAN.md is how autopilot
operates between those sessions and what it may decide alone.

TOOLS — USE THESE, THEY ARE FASTER THAN THE EDITOR
- node Tools/check.mjs      six guards; also runs on every commit
- node Tools/typecheck.mjs  compiles EVERY assembly using Unity's own Roslyn,
                            without opening Unity and without a licence. Run it
                            after every edit. Warnings count as failure.

SCENES AND PREFABS ARE GENERATED, NOT HAND-AUTHORED
Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs builds every prefab, both
scenes, the materials, the navmesh and the tuning assets. Menu: CoD → Build Grey
Box. Headless (Unity must NOT be open — it locks the project):
  "C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Unity.exe" \
    -batchmode -quit -projectPath . \
    -executeMethod CoD.EditorTools.GreyBoxBuilder.BuildHeadless -logFile -
Extend the builder rather than hand-editing a .unity file.

WHAT IS ACTUALLY LEFT
- Play-test and tune Phases 4-7 against the card. That is the real work, and it
  is the only milestone in docs/AUTOPILOT-PLAN.md that needs a human.
- Everything else in that plan's section 3 is done: automated verification (42
  tests), the SMG as a second weapon proving "weapons are data", and the
  three-lane arena with a solid centre.
- Do NOT start the M5 content list (damage numbers, a second arena, unlocks)
  until the tuning pass says the core is fun. Content on an unfun core is
  content that gets rebuilt.
- ContentRegistry (stableId lookup) is still not built. Nothing needs it while
  runs are never serialised; it lands with unlocks or loadout persistence.
- Cinemachine is still not installed, on purpose — see CLAUDE.md.

RUN THE TESTS, THEY ARE PART OF THE GATE
  Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode     -testResults Logs/tests-editmode.xml
  Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode     -testResults Logs/tests-playmode.xml

HARD-WON GOTCHAS — DO NOT REDISCOVER THESE
- Assigning an ASSET reference to a component in a scene that has never been
  saved silently does not persist. Scene-object references survive, asset ones do
  not, and nothing errors. GreyBoxVerify re-opens the saved scene, repairs, then
  re-opens again to prove it stuck. Do not "simplify" that away.
- A pooled NavMeshAgent enabled off-mesh throws on the first SetDestination, and
  a reused one walks the new drone to the dead one's destination. Prefabs ship
  with the agent DISABLED; Initialize does enable → Warp → ResetPath.
- NavMeshSurface.BuildNavMesh leaves the data in memory. It must be written to an
  asset and re-assigned, or the reference is dropped on scene save and drones
  spawn and never move.
- Configs are read-only at runtime. Domain Reload is disabled, so a runtime write
  to a ScriptableObject persists between Play sessions and rewrites your balance.
  This is why WaveScaling, StatSheet and the runtime module list all exist.
- Effect module recursion: follow-ups resolve at depth+1, and a module runs at
  depth 0 unless maxDepth says otherwise. Explosive → Chain → Explosive is the
  loop that rule prevents.
- Opening the project rewrites Packages/manifest.json. Check it before committing.
- Every new .cs needs a .meta sibling or the pre-commit hook blocks the commit.
- Viewmodel parts must have NO colliders, and drone shape details carry none
  either — hull and core are the only things a bullet can find.
- The aim ray comes from CameraPivot, not the camera — shake must never move the
  point of impact.
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
