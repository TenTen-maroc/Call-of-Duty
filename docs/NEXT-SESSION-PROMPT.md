# Next session prompt

Paste the block at the bottom into a fresh Claude Code session opened at the repo
root. It is deliberately self-contained: a new session has none of this context,
and the gotchas listed are ones that already cost time once.

Keep this file updated as milestones land — it is the handoff, not a snapshot.

---

## THE PLAY SESSION — the tuning card

Everything a machine can check is checked: 108 tests, six guards, nine clean
assemblies, and a Windows `.exe` that has been built and run outside the editor.
What is left is the one input automation does not have — **is it fun** — and it
needs you in front of the game.

Work down this card. For each item, answer in plain words ("the rushers feel
slow", "the shop pick was obvious"). Every item names the exact asset field to
move, so a report turns into a value change in seconds.

**How to run it:** open `Assets/_Project/Scenes/00_Boot.unity` and press Play to
get the real flow from the menu. Or open `10_GreyBox.unity` directly to skip
straight into a run — the cheat console is always available in the editor.

Values edited in Play Mode **persist**, because they live on ScriptableObjects.
That is the point: tune while playing, stop, and the numbers are still there.

## Part A — the shell, five minutes

New since the last handoff and never touched by a human. If any of these is
wrong, say so before touching the feel items.

| # | Do this | Should happen |
| --- | --- | --- |
| A1 | Play from `00_Boot` | Title, your best round, four rows. W/S moves, ENTER selects. |
| A2 | Menu → SETTINGS, drag every slider | FOV changes the view live; volume changes loudness live; the FOV row shows vertical AND horizontal. |
| A3 | ESC out of settings, START RUN, then ESC | Game freezes, mouse comes back, RESUME / SETTINGS / QUIT TO MENU / QUIT TO DESKTOP. |
| A4 | Change sensitivity mid-run from the pause menu, resume | The new sensitivity is live immediately. |
| A5 | Die, then start a new run | Your settings are still there and your best round updated. |
| A6 | Menu → SANDBOX | You start rich; backquote opens the console; your record is NOT written when the run ends. |

## Part B — the feel, the actual work

| # | What to feel | If it is wrong, move this |
| --- | --- | --- |
| 1 | **Three Rushers with the AR.** Console 6 spawns a burst. Does the chase read? Is 0.55 s of fuse enough warning? Does the blast make you respect them? | `Drone_Rusher.moveSpeed` (6.0, between walk 5.2 and sprint 8.0) · `ContactDetonate_Std.fuseSeconds` · `.damage` (24) |
| 2 | **Wave 5, ~15 alive.** Does the 3-attacker cap read as fair, or do the extras look passive and stupid? The console shows `attacking / cap` live. | `Difficulty.maxSimultaneousAttackers` — raise to 4 before you touch anything else |
| 3 | **The first shop break.** Is four offers plus a reroll a real decision on wave-3 money, or an obvious pick? | `Shop.offersPerBreak` · `Shop.rerollBaseCost` · the passives' costs |
| 4 | **A Shooter's opening shot.** It misses on purpose. Does that teach you where it is, or just annoy? | `RangedBurst_Std.firstShotMissDegrees` · `.reactionDelay` (0.4) |
| 5 | **A Tank at wave 7.** Is walking away the obvious answer, or does it feel like a wall you cannot fight? | `Drone_Tank.maxHealth` (600) · `HeavySlam_Std.windupSeconds` (0.9) |
| 6 | **Explosive + Chain on the rifle.** Console 9 for money, buy both. Absurd in the good way, or a frame-rate event? | `Effect_Chain.jumpsPerHit` · `Effect_Explosive.radius` · both `maxDepth` (1) |
| 7 | **The SMG vs the rifle.** Buy it at wave 3+ ($500). Is the trade — faster, snappier, useless past 30 m — a real choice or an obvious upgrade? | `SMG_Rapid.falloffRange` · `.bodyDamage` |
| 8 | **The arena lanes.** Does breaking line of sight actually change a fight, or do drones still arrive as one mass? | the block positions in `GreyBoxBuilder.BuildRoom` |
| 9 | **40 alive on the 3050.** Watch the frame time. This is the ONE thing no test can answer — headless runs have no GPU work at all. | `Difficulty.maxAliveDrones` — the one number not to raise |

## Part C — the new stuff, none of it ever played

Added 2026-08-11. The render pass (C1-C4) went in **before** this session on
purpose, so you judge the renderer that will actually ship. The gameplay items
(C5-C9) were shipped ahead of the play session **by explicit instruction**,
overriding the content gate in CLAUDE.md — so they are the least proven things
in the project and the most likely to need moving.

| # | What to feel | If it is wrong, move this |
| --- | --- | --- |
| C1 | **Does anything glow now.** The drone cores are emissive and the attack telegraph ramps them ~3.9x. Until this pass none of it resolved. Is the Rusher fuse readable across the arena? Is bloom too much? | `PostFx_Arena.asset` → Bloom `intensity` (0.35) · `threshold` (1.05) |
| C2 | **The tonemapper.** Neutral, deliberately not ACES, so the red/amber/crimson cores stay distinguishable. If the image looks flat or washed, try ACES and tell me which reads better. | `PostFx_Arena.asset` → Tonemapping `mode` |
| C3 | **The lane lights.** Warm, dim, one per lane, plus a cool key on the bunker. Do they say "they come from there", or is the arena just darker? | `GreyBoxBuilder.BuildArenaLights` colours and intensities |
| C4 | **Frame time with post ON, 40 alive.** THE question. Headless runs do no GPU work, so nothing automated can answer it. If it drops, turn POST-PROCESSING off in the settings menu and measure again — that is what the toggle is for. | `Difficulty.maxAliveDrones` is still not the knob. Bloom intensity is. |
| C5 | **Wave identity.** SWARM (14 rushers, nothing ranged) then SIEGE (4 rushers, 7 shooters). Do they feel like different fights, or just different counts? | the `plan` table in `GreyBoxBuilder.BuildWaves` — and bump `WaveDesignVersion` or the change will not land |
| C6 | **Field Repair / Resupply.** Always on the shelf at $120 / $90. Do they make a bad roll survivable, or are they just always the correct buy? | `Shop_Repair.cost` · `Shop_Resupply.cost` · `Consumable_Repair.healFraction` (0.5) |
| C7 | **Skipping the break (TAB).** Next clear pays x1.75. Is that enough to ever tempt you, or so much it is always right? | `Shop.skipBonusMultiplier` |
| C8 | **The repair beacon.** Moves lane every wave, 6 HP/s up to 35 for the wave. Does it pull you out of a good corner, or is walking to it just a way to die? | `Objective_Beacon.radius` (2.5) · `.healPerSecond` (6) · `.healBudgetPerWave` (35) |
| C9 | **Sandbox module depth.** Explosive + Chain in Sandbox now resolves one level deeper than in a Run. Absurd in the good way, or a frame-rate event? | `GameConfig.sandboxExtraEffectDepth` (1) |

**Nine, A1-A6, and C1-C9 — not "a vibe check".** Report per number. Anything you
cannot reach (a wave you never survived to, a module you never afforded) says so —
"couldn't get there" is itself a finding about pacing.

---

## What is already true, so you can skip re-checking it

- The loop runs: drones spawn, path, take damage, die, pay out, the shop opens,
  the pool recycles. Asserted by PlayMode tests against the real scene.
- The caps hold under a full arena: 40 alive, attack tokens peak at 3/3, and the
  game allocates ~450 bytes per frame with forty drones active.
- Settings load, clamp, save, survive a death and reach the camera.
- The built `.exe` boots, reaches the menu and loads the arena with zero errors,
  in both release and development configurations.
- The release binary contains no cheat-console code at all.
- Post-processing is ON in both scenes and the profile keeps its overrides through
  a save; the settings reach the camera; the arena trim carries no colliders and
  the navmesh still has no islands. All asserted by RenderingTests.
- The beacon relocates between waves and heals only within its per-wave budget.
- Skipping a break arms the bonus and spends it exactly once.

**One tuning interaction to watch:** the post `Vignette` (0.28) sits underneath
`PlayerDamageFeedback._lowHealthTint`, which is a separate full-screen image. Not
a conflict, but tune them together or the low-health cue reads as too strong.

---

```text
/goal Continue Call of Duty — a fixed-arena horde-survival FPS in Unity 6 (URP),
offline single-player, Windows. Read @CLAUDE.md, @docs/AUTOPILOT-PLAN.md and
@docs/systems/README.md before doing anything, then the specific docs/systems
file for whatever you touch.

WORK IN PLAN MODE FIRST unless I say otherwise. Audit my claims against the
actual code before planning — past sessions have been wrong about the state
until they checked.

ABOUT ME
- Solo dev, full-time. Strong TypeScript, weak C#. When you use a C#-specific
  idiom (properties, coroutines, attributes, ref structs, events), add a one-line
  inline note. Do not explain general programming.
- RTX 3050 Laptop, 4 GB VRAM. Texture budget and spawn counts matter far more
  than poly count.

WHERE THINGS STAND
Code-complete and shippable-shaped: main menu, Run and Sandbox modes, pause,
working settings, a Windows .exe proven to run outside the editor, and the whole
loop — two weapons, three drone archetypes, timed waves, a between-wave shop
selling passives and four stacking effect modules, permadeath with a versioned
save, and a three-lane arena. 108 tests, six guards, nine clean assemblies.

THE ONLY OPEN QUESTION IS WHETHER IT IS FUN. Phases 4-7 have never been played.
The card at the top of docs/NEXT-SESSION-PROMPT.md is the list, each item naming
the asset field to move. Run it as a LOOP with me: give me a short numbered
checklist, I play, I report in plain words, you translate that into asset-value
changes and hand me the next checklist. NEVER mark a feel item verified because
a test passed — say "awaiting play feedback" and mean it.

Do NOT start the content list (damage numbers, run summary, second arena,
unlocks) until the tuning pass says the core is fun. Content on an unfun core is
content that gets rebuilt.

TOOLS — USE THESE, THEY ARE FASTER THAN THE EDITOR
- node Tools/check.mjs        six guards; also runs on every commit
- node Tools/typecheck.mjs    compiles EVERY assembly with Unity's own Roslyn,
                              without opening Unity and without a licence.
                              Warnings count as failure.
- node Tools/verify-build.mjs builds a real Windows .exe and RUNS it headlessly.
                              The only gate that leaves the editor.

SCENES AND PREFABS ARE GENERATED, NEVER HAND-AUTHORED
Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs builds every prefab, all three
scenes, the materials, the navmesh and the tuning assets. Menu: CoD → Build Grey
Box. Headless (Unity must NOT be open — it locks the project):
  "C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Unity.exe" \
    -batchmode -quit -projectPath . \
    -executeMethod CoD.EditorTools.GreyBoxBuilder.BuildHeadless -logFile -
Extend the builder rather than hand-editing a .unity file. New scenes get built
by it too and registered by RegisterScenes.

RUN THE TESTS, THEY ARE PART OF THE GATE
  Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode -testResults Logs/tests-editmode.xml
  Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode -testResults Logs/tests-playmode.xml

HARD-WON GOTCHAS — DO NOT REDISCOVER THESE
- Assigning an ASSET reference into a scene that has never been saved silently
  does not persist. Scene-object references survive, asset ones do not, and
  nothing errors. GreyBoxVerify re-opens, repairs, then re-opens to prove it
  stuck. Every new scene needs its own repair pass, not just a check.
- A UnityEngine.Object handle does NOT survive a scene switch. Closing a scene
  unloads every asset it was the last thing holding, and a handle to an unloaded
  object compares equal to null. Load assets AFTER opening the scene that needs
  them.
- The record and the settings share one FILE, so they must share one OBJECT.
  Two SaveData instances each write the whole file and the second one wins —
  that bug wiped every setting on every death, and only a BUILT player showed it.
- A pooled NavMeshAgent enabled off-mesh throws on the first SetDestination, and
  a reused one walks the new drone to the dead one's destination. Prefabs ship
  with the agent DISABLED; Initialize does enable → Warp → ResetPath.
- NavMeshSurface.BuildNavMesh leaves the data in memory. It must be written to an
  asset and re-assigned, or the reference is dropped on scene save.
- Configs are read-only at runtime. Domain Reload is disabled, so a runtime write
  to a ScriptableObject persists between Play sessions and rewrites your balance.
  This is why WaveScaling, StatSheet, GameSettings and the runtime module list
  all exist.
- Effect module recursion: follow-ups resolve at depth+1, and a module runs at
  depth 0 unless maxDepth says otherwise. Explosive → Chain → Explosive is the
  loop that rule prevents.
- uGUI draws siblings in hierarchy order. A panel built earlier is painted UNDER
  one built later — that is why BuildPauseUi runs after BuildRunUi.
- Only ONE component may poll a shared key per screen. SPACE is "next wave" in
  the shop and "confirm" in a menu; two Updates racing on it is a coin flip,
  because MonoBehaviour execution order is undefined.
- Test windows that wait for DRONES are measured in seconds, never frames. A
  -batchmode run is uncapped; 900 frames was under a second of game time.
- Cursor.lockState is never assertable in -batchmode. There is no window, so it
  reads None whatever the code did.
- The arena origin (0,0,0) is INSIDE the centre bunker. Never use it as "the
  middle".
- Opening the project rewrites Packages/manifest.json. Check it before committing.
- Re-running the builder ALWAYS produces a scene diff even when nothing changed:
  Unity assigns fresh local fileIDs on every regeneration, so a no-op rebuild
  shows thousands of lines with equal insertions and deletions. Check the counts
  before assuming you broke something, and `git checkout -- Assets/_Project/Scenes/`
  to drop pure churn. `GreyBoxVerify.VerifyHeadless` proves the COMMITTED scenes
  are wired without regenerating them.
- Every new .cs needs a .meta sibling or the pre-commit hook blocks the commit.
- Viewmodel parts and drone shape details carry NO colliders.
- The aim ray comes from CameraPivot, not the camera — shake must never move the
  point of impact.
- Unity's FOV field is VERTICAL. 62 ≈ 95 horizontal.

NON-NEGOTIABLE RULES
- Every tunable number in a ScriptableObject; zero magic numbers in scripts.
- Everything that spawns goes through the object pool.
- No per-frame allocation: no LINQ, no new collections, no string concatenation.
- No mutable statics; no Find/GetComponent/Camera.main/Instantiate/Destroy in
  Update/FixedUpdate/LateUpdate. Both guarded.
- #nullable enable atop every first-party file, zero warnings.
- maxAliveDrones 40 and maxSimultaneousAttackers 3 are not tuning knobs.
- Atomic commits, one subsystem each, updating the matching docs/systems/*.md and
  docs/AUTOPILOT-PLAN.md milestone status in the SAME commit.

STOP AND ASK, don't improvise around: a gate that will not go green after 5
attempts on the same failure, anything that would weaken a guard or delete a test
to pass, or anything contradicting a locked decision in CLAUDE.md (netcode, HDRP,
a fourth archetype, Input.GetKey).
```
