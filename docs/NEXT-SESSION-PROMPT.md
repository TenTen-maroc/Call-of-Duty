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

**Nine, A1-A6, C1-C9, D1-D9 and E1-E9 — not "a vibe check".** Report per number. Anything you
cannot reach (a wave you never survived to, a module you never afforded) says so —
"couldn't get there" is itself a finding about pacing.

---

## Part D — the 2026-08-12 pass, also never played

The image work (D1-D4) is presentation on a core still awaiting Part B. The
feel items (D5-D6) are the ones that matter most here, because both changed
behaviour that is reachable **on the rifle that ships today** — not only on the
shotgun that does not exist yet.

| # | What to feel | If it is wrong, move this |
| --- | --- | --- |
| D1 | **The arena got darker and the metal got duller.** The floor was shipping ~1.9x brighter than intended for the life of the project, and the gun was reflecting a blue sky inside a sealed bunker. Is the box now readable, or is it too dark to fight in? | `Palette_GreyBox.asset` → `floor` (0.17) · `wall` (0.28) · `indoorReflection` |
| D2 | **The grade.** Cool shadows, warm highlights, −6 white balance, light chromatic aberration. Does it read as a military shooter, or as a filter? Turn post-processing off in Settings and back on — that comparison is the whole question. | `PostFx_Arena.asset` → `ShadowsMidtonesHighlights` · `WhiteBalance` · `ChromaticAberration.intensity` (0.06) |
| D3 | **Impacts.** Sparks rendered as Unity magenta error particles until now, and the bullet hole was a bright orange dot. Both are new. Do impacts read at 25 m? | `Fx_Spark.mat` / `Fx_ImpactMark.mat` · `Impact_Default.asset` lifetimes |
| D4 | **Frame time with the additive/soft particles at 40 alive.** Soft particles sample the depth texture, which SSAO already pays for — but this is still the one question no headless run answers. | `Difficulty.maxAliveDrones` is still not the knob |
| D5 | **The hitmarker, with Pierce or Chain bought.** THE feel item of this pass. One trigger pull now raises **one click per target**, and a kill **always** confirms. Kill two drones with one chained shot: do you hear two kill confirms? Put twelve pellets into one drone (Sandbox, spawn a shotgun config): is it one click, not twelve? | `WeaponController.RegisterHit` — and say which of the two cases feels wrong before anything is changed |
| D6 | **Explosive rounds.** Explosive is now `OncePerPull`: one blast per trigger pull however many rays it casts. On the single-pellet rifle nothing should have changed at all — confirm that first. Then confirm a multi-pellet weapon does not stack twelve booms. | `EffectModule.OncePerPull` · `Effect_Explosive.asset` radius / damageFraction |

| D7 | **The gun stops clipping through walls.** It renders on its own overlay camera now, so walk into every wall you can find and put the muzzle inside one. Also sprint, and ADS: the world FOV moves and the viewmodel's should NOT, which is the bug that made the gun stretch on every sprint. | `GameConfig.viewmodelFovVertical` · `.viewmodelAdsFovDelta` |
| D8 | **The muzzle flash lights the gun AND the room.** There are two lights now, on one clock, because a camera culls lights by layer and neither could reach both. Fire the SMG in a long burst in a dark corner: the gun should strobe, not sit under a continuous glow. Then check the flash is not blowing the viewmodel out. | `AR_Standard.viewmodelMuzzleLightIntensity` (2.2) · `.muzzleLightDuration` (0.03) — the ROOM intensity is 12 and must stay far higher |
| D9 | **Post-processing off still turns post-processing off.** URP resolves stack post at the last camera in the stack, so the overlay had to be told too. Settings → post-processing OFF, then fire: bloom must be gone from the muzzle flash as well as from the room. | nothing — if this is wrong it is a bug, not a tuning value |

**D5 is the one to be suspicious of.** Before this pass a shot that killed two
drones through a chain raised two kill confirms; the first version of this fix
collapsed that to one, which was a regression on the shipped rifle discovered by
review rather than by play. The current rule — one click per target, a kill
always confirms — is the second attempt, and it has never been heard by a human.

---

## Part E — the campaign, and it has never been played by anyone

Two missions exist. They are the least proven thing in the project: authored,
wired, covered by a suite that drives them without fighting them, and **never
experienced by a human being**. The review that preceded them found FOUR
separate ways a mission could be uncompletable — see
[docs/systems/campaign.md](systems/campaign.md) — so treat "it worked" as the
finding, not the assumption.

**How to reach it:** main menu → CAMPAIGN → mission 1.

| # | What to feel | If it is wrong, move this |
| --- | --- | --- |
| E1 | **Does the objective line tell you what to do?** Top-left, updated as steps complete. If you ever stand still not knowing where to go, that is the finding — say where you were. | `Objective_*.asset` → `title` / `description` |
| E2 | **SHAKEDOWN, mission 1.** Walk to the control point → survive 2 waves → walk back out to extract. Is the walk-out a fighting retreat or a boring stroll through an empty room? | `Mission_01_Shakedown.asset` → its wave list |
| E3 | **The zones.** Both pads are 3 m. Standing "on it" should be obvious without looking down. Do you ever think you are on it and not be? | `MissionZones` in the scene → the `_zones` radii on `MissionDirector` |
| E4 | **Dying mid-mission.** THE one to try deliberately. You should restart at the checkpoint, alive, with the wave loop resuming — not at the menu, and not stuck. Do it twice in a row. | `MissionDirector.OnPlayerDown` — and if you end up alive-but-invincible, stop and tell me: that was a real bug and this is the test of its fix |
| E5 | **HARD CONTACT, mission 2.** Kill 12 → hold the control point 45 s → extract. Does the hold feel like a siege, or like standing in a circle? | `Objective_Hold_ControlPoint.asset` → `holdSeconds` (45) · the mission's wave list |
| E6 | **The ending screen.** Finishing should say MISSION COMPLETE, not YOU DIED. If it says the wrong thing, that is a bug I thought I fixed. | `GameOverPanel.Redraw` |
| E7 | **Does the campaign leave the endless game alone?** Play a normal Run after a mission. Your best round must be untouched, and no mission should start. | if a mission starts, `MainMenuPanel.StartGame` lost its `SetCampaign(false, ...)` |

| E8 | **The objective line is readable.** Found clipped in the first real play session — it printed "EADY / THE CONTROL POINT" instead of "REACH THE CONTROL POINT", cut off the left edge of the screen, on the first screen of the first mission. Confirm it reads in full, including a longer line like a hold timer. | `GreyBoxBuilder.BuildObjectiveHud` — anchor, pivot and `sizeDelta` |
| E9 | **No wave countdown while you are walking.** Also found in that session: the banner read `WAVE 1 IN 1` permanently during the walk-to-the-control-point step. A held runner never left `Countdown` and `_phaseEndsAt` was still zero, and `Mathf.Max(1, seconds)` floored the display at one — so the player was promised a wave in one second that never came, which reads as a frozen game. Confirm the banner is now silent until enemies are actually coming. | `WaveHud.UpdateBanner` |

---

## Part F — the launcher, and the five guns nobody could hold

Added 2026-08-12 (W4). Until this landed the game could put **two** of its
weapons in a player's hands; there are now seven and all of them are reachable.

**How to reach them:** Sandbox, backquote for the console, **digit 0** cycles the
weapon. Digit 2 for infinite ammo and digit 6 to spawn drones.

| # | What to feel | If it is wrong, move this |
| --- | --- | --- |
| F1 | **The launcher.** One round in the tube, a rocket that takes about a second to cross a lane, a 4.5 m blast. THE question: does having to LEAD a rusher feel skilful or feel like the gun is broken? | `RL_Launcher.projectileSpeed` (34) — raise it to 45 before touching anything else |
| F2 | **Is the rocket readable in flight?** It is a dark cube with a bright trail on the world camera. Can you see it leave the tube and track it to the wall, or does it vanish? | `Fx_Rocket` in `VfxBuilder.BuildRocketPrefab` — the trail `time` (0.5) and `widthMultiplier` (0.13) |
| F3 | **The blast.** 70 damage at the centre, 24.5 at 4.5 m, and it must NEVER hurt you. Fire one at a wall two metres away: you should take nothing. If you take damage, stop and say so — that is a bug, not a number. | `Effect_RocketBlast.radius` · `.damageFraction` (0.7) |
| F4 | **One round, three seconds to reload.** Is that "make it count" or is it "the launcher is unusable in a wave"? | `RL_Launcher.magazineSize` (1) · `.reloadEmptyTime` (3.4) |
| F5 | **Cycling weapons mid-wave.** Digit 0 through all seven. Does each one feel like a different gun, or like the same gun with different numbers? This is the first time anybody has been able to answer that. | the per-weapon `Configure` methods in `ArsenalBuilder` |
| F6 | **The shotgun, finally holdable.** `SG_Breacher` has never been fired. Twelve pellets, one pull at contact, two at ten metres. Also the D5 hitmarker case: twelve pellets into one drone must be ONE click. | `SG_Breacher.pelletSpreadDegrees` is **still 0** — an aimed shotgun puts every pellet on one point. Say whether that reads as broken before it is changed |

| F7 | **The sniper, scoped.** 5x, one pull, a 1.2 s bolt cycle, five rounds. Does the zoom feel usable in a 40 m room, or is it too much magnification for the space? | `Attach_Scope_Long` → `AdsFov` (×0.42) — the config's own 0.48 is the UNSCOPED number |
| F8 | **The sniper, unscoped.** Press MINUS to fit, and note that the gun is a legal weapon without it. Is the difference obvious? That difference is the whole test of "an attachment is a stat delta". | `SR_Longshot.adsFovMultiplier` (0.48) |
| F9 | **The other four attachments.** MINUS cycles: angled grip, extended magazine, suppressor, heavy stock. Is any one of them an obvious auto-take, or does each cost something you notice? | the `Configure*` methods in `ArsenalBuilder` |
| F10 | **The reverb, once you add it.** Four clicks in `Master.mixer` — see docs/systems/audio.md. It is the single change that makes a rifle sound like it is INSIDE a facility, and it needs both ears and clips. Nothing about it can be judged from a screenshot. | `SFX Reverb` → Dry Level, Decay Time · the Send level (−12 dB) |

**F3, F6 and F9 are the ones that matter.** F3 because self-damage would be the
one launcher bug that ends runs, F6 because the shotgun's missing pattern is a
known unfixed hole that has now become reachable, and F9 because an attachment
with no felt downside is a patch note rather than a decision.

---

**E8 and E9 came from ONE screenshot of someone playing for the first time.**
That is the entire argument for this card: 200 automated checks, a clean build
and a booting release binary said nothing about either of them, because neither
is a crash, a null or a failed assertion — they are just *wrong on screen*.

**E4 and E7 are the two that matter.** E4 because the rewind is the mechanic the
whole campaign rests on, and E7 because the campaign must never be able to
damage the record the endless game is played for.

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

**One image question nobody has answered.** Every frame the harness renders shows
a large flat pale-blue band across the lower third of the arena, with a hard
horizontal edge, and a warmer floor beneath it. It is NOT a regression — a
stash-and-rebuild baseline at the previous commit renders it identically — but
whether it reads as a lit floor or as a lighting seam is exactly the "atmospheric
or blotchy" question in the list below, and it is the most conspicuous thing in
the image.

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

---

# THE BUILD PROMPT — paste this into a fresh session

Use this when the job is **continuing construction**. The tuning card above is
for the other job — a human playing and reporting feel.

```
You are continuing a solo-built Unity 6 + URP horde-survival FPS with a story
campaign, at the repo root. Autopilot is on: commit and push without asking.

FIRST, IN THIS ORDER:
1. CLAUDE.md — the contract. Locked decisions, conventions, current state table.
2. docs/PLAN-GRAPHICS-AND-GUNS.md — the image and the arsenal, ordered, executable.
3. docs/PLAN-CAMPAIGN.md — missions and human enemies.
4. docs/systems/README.md — then the specific docs/systems/*.md for whatever you touch.

WHERE IT STANDS
Six weapons, a two-mission campaign with checkpoints, a wave loop with a shop,
three drone archetypes, a full HDR grade, a separate viewmodel camera, tracers,
per-surface impacts, footsteps and ambience components (no audio FILES yet).
~222 tests. Everything is a grey box: zero imported art, one generated texture.

W4 IS DONE (2026-08-12). Projectiles landed: Enemies/DroneProjectile.cs was
PROMOTED to Core/Projectile.cs keeping its .meta (so every prefab reference
survived), both sides fire it, a round passes through its own declared faction and
its owner, and RL_Launcher fires a rocket that carries its own WeaponConfig.
Sandbox console digit 0 now cycles the whole registry — before that, five of the
seven weapons could not be held by anybody. Nobody has fired any of it: see Part F
of the tuning card above.

W5 IS DONE (2026-08-12). AttachmentConfig is composed into WeaponConfig, it is
NOT the EffectModule pattern, Stat/StatExtensions.Count is untouched and WeaponStat
is a separate enum and sheet. Five attachments ship; SR_Longshot is a legal weapon
whose 5x optic is an attachment rather than a field. TWO PIECES OF W5 WERE
DELIBERATELY NOT BUILT and both belong with G6: the scope OVERLAY image (the FOV
change is real, the black-surround picture that sells it is UI and belongs with
G6's Sight_Glass), and HOLD-BREATH (it needs an input action, a stamina float and
a sway multiplier — and the sway numbers are the nine serialized fields G6 is
about to move into a ViewmodelConfig, so building it now means writing it twice).

G5a IS DONE (2026-08-12). Assets/_Project/Audio/Master.mixer exists: ten buses,
four exposed parameters (MasterVolume / SfxVolume / MusicVolume / AmbienceVolume),
a -12 dB send from SFX into a Receive + SFX Reverb on a sibling Reverb bus, and
AudioBuilder now routes footsteps -> World and ambience -> Ambience on every run.

It was created in the editor and FINISHED AS TEXT with the editor closed — a
.mixer is ordinary Unity YAML, so the "no builder can make one" rule turns out to
be narrower than it read: nothing can make the FIRST one, but an existing one can
be edited as text and handed back to Unity to validate. Because it is the only
asset with no builder, it now has a gate instead:
  Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.AudioBuilder.VerifyMixerHeadless

WHAT IS STILL MISSING IS THE SOUND ITSELF. There are no clips, and that is the
remaining human step: Kenney (CC0) and the Sonniss GDC bundle (royalty-free) are
the free sources, both are a download decision, and audio is the sneaky LFS
killer at ~10 MB a minute — the repo is on 1.3 MB of a 400 MB budget. Mono for
anything spatialised; stereo only for music and ambience.

THE HARD-WON FINDING: GROUPS CAN BE HAND-WRITTEN AS TEXT, EFFECTS CANNOT. A
Send, a Receive and an SFX Reverb were written into the YAML. The file parsed,
the asset imported, and VerifyMixer passed — because loading a mixer does not
build its DSP graph. Then the PlayMode suite dropped from 60 to 57, all three
failing on `Assertion failed on expression: 'res == FMOD_OK'` the moment a routed
AudioSource instantiated the mixer: a built-in effect needs a m_Parameters list of
parameter GUIDs that only the editor mints. The effects were removed and all 60
passed again. THE PLAYMODE SUITE IS THE GATE FOR THIS, and only because footsteps
and ambience are now routed — that routing is what makes the arena build the DSP
graph on every test run.

So the Reverb bus exists and is EMPTY. Finishing it is four editor actions, and
docs/systems/audio.md lists them. Do not hand-write them into the YAML again.

ONE MORE THING DELIBERATELY LEFT ALONE: the master volume slider still writes
AudioListener.volume rather than the exposed MasterVolume. Moving it today would
be a REGRESSION — only footsteps and ambience are routed, so the slider would
stop working for the weapons, impacts, hitmarker and UI. Switch it when a second
bus needs balancing, which is the same moment every source gets an output group.

DO NEXT, in this order — each is free and worth more than the paid art that follows:
  G5b clips for footsteps, impacts and weapon layers. The mixer they route
      through already exists (G5a above); this is the download-and-trim step.
  G4  reflection probes and shaped lights. RETRY — a previous attempt was reverted
      for cause; read "the four ways it went wrong" below.
  G6  viewmodel feel: move WeaponSway's nine serialized numbers into a
      ViewmodelConfig, wall-lower, inspect. RETRY — also reverted; see below.
      ALSO PICKS UP W5's two deferred pieces: the sniper's scope overlay and
      hold-breath, both of which need the sway config this milestone creates.
  G8  the art seam. THE GATE: nothing is bought until this lands, because it is
      what makes an art swap reversible.
  E2-E5 human soldiers, C8 data-driven arenas, missions 3-12.

THE GATES, every commit:
  node Tools/typecheck.mjs      # 9 assemblies, zero errors AND zero warnings
  node Tools/check.mjs          # 8 guards
  Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode  -testResults Logs/tests-editmode.xml
  Unity.exe -batchmode -runTests -projectPath . -testPlatform PlayMode  -testResults Logs/tests-playmode.xml
  node Tools/verify-build.mjs   # builds a real player and RUNS it
  node Tools/screenshot.mjs     # renders 8 frames from the built player -- LOOK AT THEM

⚠️ THE SCREENSHOT HARNESS FIRES NOTHING. BuildSmokeTest lives in CoD.Core, which
references nothing and therefore cannot reach WeaponController — so no frame it
renders has ever contained a shot, a tracer, an impact or a rocket. It proves the
arena and the HUD render; it cannot photograph the weapon work. To compare a
render change against a baseline today, `git stash -u`, run it, look, and pop.
Closing that gap needs a dev-only trigger on the CoD.UI side (where the cheat
console already lives) plus a Core-side seam for the harness to pull it, and it is
worth doing before the next Track G phase.

UNITY MUST BE CLOSED for every one of those. "Another Unity instance is running"
is the error, and it is the user having the editor open.

THE FIVE THINGS THAT COST TIME TODAY. Every one compiled, passed all guards, and
was invisible until something rendered a frame or a reviewer read the code:
1. THE BUILDER IS THE GAME. Scenes and prefabs are generated by
   Assets/_Project/Scripts/Editor/GreyBoxBuilder.cs and committed. Editing it
   changes NOTHING until it is re-run. Never call SaveScene(GreyBoxScenePath)
   mid-build -- one attempt did, and would have overwritten the working arena with
   a scene containing no player, no HUD and no object pool.
2. SetRef(component, "_field", value) wires by STRING and GreyBoxVerify re-checks
   by string. A typo is a silent null no compiler catches.
3. LoadOrCreate<T>(path, configure) runs configure ON CREATE ONLY. A renamed asset
   path silently creates a fresh default, discards all tuning, and reports success.
4. ABSENCE IS NOT NEUTRALITY. A reflection source set to "nothing" is a reflection
   of BLACK -- it turned every metal surface, including the weapon, matte black.
5. #if UNITY_EDITOR || DEVELOPMENT_BUILD -- a call placed outside the directive its
   callee lives in compiles in the editor and breaks ONLY the release build.

WHY G4 AND G6 WERE REVERTED, so the retry does not repeat it:
  G4: saved over the real scene mid-build; enabled a 2048 cookie atlas for a 16.8 MB
      VRAM cost on a 4 GB target; pinned probe EXRs to a size that shrank each cube
      face to ~16-42 px; aimed every fixture straight down, which removes light from
      every wall. Bake from a SCRATCH scene, measure the real byte sizes, and tilt
      the fixtures so they wash a vertical surface.
  G6: the rig settled at (0,0,0) -- the config was wired and ignored -- and the
      wall probe self-hit the player's own capsule, so the gun was permanently at
      port arms. Its tests were good and are worth rewriting from.

NON-NEGOTIABLE: every tunable in a ScriptableObject; everything spawned goes
through the pool; no per-frame allocation; no mutable statics (Domain Reload is
OFF); nothing writes to a ScriptableObject at runtime; #nullable enable line 1;
zero warnings. maxAliveDrones 40 and maxSimultaneousAttackers 3 are not knobs.

NEVER weaken a test or a gate to go green. If a test must change it must get
STRONGER, with the reason in the diff. Reverting a broken round is correct and
has precedent -- two tracks were reverted rather than shipped.

DO NOT SPEND MONEY. Art packs are unbought by the user's standing decision.
Everything above is free.

STILL UNANSWERED, and no machine here can answer them: is it fun, does it hold
frame time on an RTX 3050 Laptop with 4 GB, and does the arena lighting read as
atmospheric or as blotchy. Those need the tuning card and a human.
```
