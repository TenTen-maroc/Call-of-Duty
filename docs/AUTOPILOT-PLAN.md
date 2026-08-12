# Autopilot plan — how this repo finishes itself

> Last updated: 2026-08-11

This is the standing plan for autopilot sessions. It answers two questions a
fresh session cannot answer from the code alone: **what may I do without
asking**, and **what is left to do**.

It is not a handoff to a human. A handoff says "play this and tell me how it
feels"; this says "here is the next milestone, here is how you prove it, ship
it". The only work reserved for a human is the judgement automation genuinely
cannot make — and this file names exactly which judgements those are.

---

## 1. The autopilot contract

**Standing authority.** `~/.autopilot` is present on this machine, so commits,
pushes and headless Unity runs need no per-step approval. Direct-push-to-main is
the branch model. Close Unity when a headless build needs it; the editor's
unsaved state is expendable, the repo is not.

**The loop, every milestone, in order:**

1. Write the C#. Extend `GreyBoxBuilder` for anything that appears in a scene or
   a prefab — never hand-edit a `.unity` file.
2. `node Tools/typecheck.mjs` — zero errors **and** zero warnings, all assemblies.
3. `node Tools/check.mjs` — six guards.
4. Headless build:
   `Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.GreyBoxBuilder.BuildHeadless -logFile Logs/build.log`
   then grep the log for `STILL NULL` and for `survived a save/reload round trip`.
5. Headless tests:
   `Unity.exe -batchmode -runTests -projectPath . -testPlatform EditMode -testResults Logs/tests-editmode.xml`
   then the same with `-testPlatform PlayMode`. Both must be green.
6. `node Tools/verify-build.mjs` — builds a real Windows player and runs it.
   The only gate that leaves the editor. Required for anything touching scenes,
   Build Settings, save files, or a `#if UNITY_EDITOR` block.
7. `git diff --exit-code Packages/manifest.json` — opening the project re-adds
   deprecated packages; revert if it moved.
8. Update the matching `docs/systems/*.md` **in the same commit**.
9. Commit (Conventional Commits, `[autopilot]` trailer) and push.

**Stop and report — do not improvise around:**

- A gate that will not go green after 5 attempts on the same failure.
- Anything that would weaken a guard, delete a test, or lower a cap to pass.
- A design decision that contradicts a locked decision in `CLAUDE.md` (netcode,
  HDRP, humanoid enemies, a fourth archetype, `Input.GetKey`).
- The two hard caps: `maxAliveDrones 40`, `maxSimultaneousAttackers 3`.

**Never:** write to a ScriptableObject at runtime, add a mutable static, put a
tuning number in a script, or spawn anything outside the pool. Those four are
what the guards and the iron rules exist for, and every one of them has already
cost this project time once.

---

## 2. What autopilot can and cannot verify

This is the honest boundary, and the reason earlier sessions stopped short.

| Claim | Provable by machine? | How |
| --- | --- | --- |
| It compiles, warning-free | ✅ | `typecheck.mjs`, seven assemblies |
| No banned pattern shipped | ✅ | six guards |
| Every scene reference is wired | ✅ | `GreyBoxVerify` save/reload round trip |
| The maths is right (stats, falloff, save round-trip, recursion bounds) | ✅ | EditMode tests |
| The loop actually runs — waves spawn, drones path, damage lands, the shop opens | ✅ | PlayMode tests |
| The two caps hold under a full arena | ✅ | `HordeLoadTests` — 40 alive, tokens peak 3/3 |
| Per-frame allocation at 40 alive | ✅ | `ProfilerRecorder`, 450 B/frame mean against a 16 KB budget |
| Frame time with 40 alive on a 3050 | ❌ | headless has no GPU work; only a human sees a stutter |
| The BUILT game runs at all | ✅ | `verify-build.mjs` — builds a `.exe` and executes it |
| **Is it fun** | ❌ | the tuning card, played by a human |

Everything except the last two rows is autopilot's job, and a milestone is not
done until they are green. "Compiles but nobody ran it" is not a finished
milestone — it is an unverified one, and the tests exist so that state stops
being acceptable.

The fun judgement stays human, and that is not a limitation to engineer around:
it is the one input the machine does not have. Autopilot's obligation is to
deliver something worth judging, with every number it depends on already isolated
in an asset so the judgement can be acted on in seconds.

---

## 3. The roadmap, and what is left

Phases 0-7 are **built, gated and tested**. What remains, in the order autopilot
should take it:

### M1 — Automated verification (done, 2026-08-11)

EditMode tests for the pure logic (stat folding, save round-trip and recovery,
follow-up bounds, damage falloff, wave scaling, shop draw and purchase) and a
PlayMode smoke test that loads the grey box, runs the loop, and asserts drones
spawn, path, take damage and die, and that the wave advances to the shop.

**Why first:** every later milestone is verified by it, and it converts the
"code-complete but unplayed" state into "machine-verified, human-untuned".

### M2 — The second weapon, as data (done, 2026-08-11)

`SMG_Rapid` plus a `ShopItemKind.Weapon` handler that swaps the carried weapon at
runtime. **Acceptance:** a new weapon requires one asset and zero new classes; if
it needs a code change, the modular claim is false and that is the finding to
report.

### M3 — The arena (done, 2026-08-11)

The grey box is one open room; a horde game needs geometry that breaks
line-of-sight, so retreating is a skill rather than a straight line. Three lanes,
a raised centre, cover that a Shooter can be forced out of.
**Acceptance:** navmesh bakes with no isolated islands (asserted in a test), and
the spawn ring still resolves from every point.

### S1 — The runtime settings layer (done, 2026-08-11)

`SaveData` carried `mouseSensitivity` and `masterVolume` that **nothing read**.
Now: `SettingsConfig` bounds, a `GameSettings` runtime object that never writes
to a ScriptableObject, a `SettingsHub` per scene, schema 2 with a migration, and
`PlayerLook` driven by the saved values. Master volume drives
`AudioListener.volume`; see [systems/settings.md](systems/settings.md) for why
not an AudioMixer. **Acceptance:** a v1 save keeps its record and re-seeds its
settings, asserted by test.

### S2 — Menus: main menu, pause, modes (done, 2026-08-11)

There was no way in and no way out. Now `20_MainMenu` (title, record, Run vs
Sandbox, settings, quit), pause on Escape with correct timeScale capture/restore
and full input blocking, and a settings page shared by both. Run and Sandbox are
carried through `SaveData.lastMode`, never a static. See
[systems/menus.md](systems/menus.md). **Acceptance:** a PlayMode test loads the
menu scene, and pause is proven to stop the clock, block the action map, and
restore whatever timeScale it found rather than a hard 1.

### S3 — The Windows build, and proof it runs (done, 2026-08-11)

Nothing produced a `.exe`. Now `GameBuilder` does, headlessly, and
`node Tools/verify-build.mjs` builds one and RUNS it: `BuildSmokeTest` boots the
player, reaches the menu, loads the arena, counts every error, and quits with an
exit code. Release (93 MB) has no cheat console in the binary at all;
development (132 MB) has it and correctly refuses outside Sandbox — that gate had
never been exercised in a real player. See [systems/build.md](systems/build.md).

**It immediately earned itself:** `RunContext` and `SettingsHub` each loaded
their own `SaveData`, so ending a run rewrote the whole file and reverted every
setting. Invisible to every editor gate; obvious in the save a built player left
behind. Fixed, verified, and covered by two PlayMode tests.

### S4 — The caps under load (done, 2026-08-11)

`maxAliveDrones 40` and `maxSimultaneousAttackers 3` are called "not tuning
knobs" and nothing checked them. Four PlayMode tests now push a full arena:
400 spawn attempts past the cap never exceed it, attack tokens saturate at
**3 / 3 with 40 alive**, an 8-second pressure window throws nothing, and GC
allocation holds at **450 B/frame mean, 610 B worst** — the pool doing its job.
See [systems/performance.md](systems/performance.md).

Frame time on the 3050 is still NOT verified and cannot be from a headless run.
That stays item 9 on the tuning card.

### M4 — Human tuning pass (BLOCKED ON A PERSON — this is the only thing left)

The card at the top of [NEXT-SESSION-PROMPT.md](NEXT-SESSION-PROMPT.md): six
shell checks and nine feel judgements, each naming the asset field to move.
Autopilot's job was to make every one of those numbers a single Inspector field
and to remove every reason the session could fail for a non-fun reason. That job
is done — S1 to S4 closed the settings, the menus, the build and the caps.

Run it as a LOOP with the human: hand them a short numbered checklist, take back
plain-language answers, translate those into asset values, hand back the next
checklist. **Never mark a feel item verified because a test passed.** Say
"awaiting play feedback" and mean it.

### M5 — Then, only if M4 says the core is fun

- Damage numbers and a kill counter (feedback, cheap, high return)
- A second arena, unlocked by best round
- `ContentRegistry` — the moment anything needs `stableId` lookup, which is
  unlocks or loadout persistence, whichever comes first
- Cinemachine, as one file plus the package, when recoil impulse is worth it

**Do not start M5 before M4.** Content built on an unfun core is content that
gets rebuilt.

---

## 4. Session template

```text
Read CLAUDE.md, docs/systems/README.md, and this file.
Take the first unfinished milestone from section 3.
Run the loop in section 1 for it. Stop only for the reasons listed there.
Update this file's milestone status in the same commit.
```

A session that finishes a milestone updates section 3 and stops. A session that
finishes them all reports that M4 is the blocker and says so plainly, rather than
inventing work to look busy.

**As of 2026-08-11 they are all finished.** M4 is the blocker. There is no
machine-checkable work left that would not be guessing at what a person has to
judge — and the M5 content list must not start until they have judged it.

## 2026-08-11 — the image pipeline and the content gate override

Two things landed in one session.

**R1-R2, the render pass.** The project had been rendering with post-processing
switched off for its whole life and no gate noticed: the camera had no
`UniversalAdditionalCameraData`, so the emissive drone cores and the attack
telegraph that ramps them clipped flat instead of glowing. Turning it on, plus
arena lighting, surface response and a generated detail normal, is
[docs/systems/rendering.md](systems/rendering.md). Anti-aliasing and
post-processing became player-facing settings (save schema 3).

**G1-G5, the content gate override.** CLAUDE.md gates the content list behind a
play session that has not happened. The user was shown that gate and chose the
full scope anyway, so wave identity, shop consumables, the skip-the-break gamble,
the repair beacon and sandbox module depth all shipped ahead of it.

For autopilot, the standing consequence is this: **G1-G5 are the least proven
work in the repo.** They compile, they hold under 108 tests, and nobody has felt
any of them. Treat a play report that contradicts one of them as authoritative
over the design intent recorded in the commit message, and expect to move their
numbers rather than defend them.

## 2026-08-12 — W4, projectiles and the launcher

**What landed.** `Enemies/DroneProjectile.cs` was PROMOTED to
`Core/Projectile.cs` rather than copied, so both the Shooter drone and the
player's launcher fly the same object; `RL_Launcher` is the first weapon in the
game whose shot does not resolve on the frame the trigger is pulled. Full detail
in [docs/systems/weapons.md](systems/weapons.md).

**Three things a future session should not have to rediscover.**

1. **Moving a script FILE while keeping its `.meta` keeps every prefab
   reference.** `Fx_DroneProjectile.prefab` binds its component by the script's
   guid, so `git mv` of the `.meta` alone made a cross-assembly rename cost zero
   repair passes and zero scene churn. Deleting the `.meta` and letting Unity mint
   a new one would have left a missing script on a committed prefab.
2. **A rebuild of the grey box silently drops what other builders added.**
   `GreyBoxBuilder` writes a brand-new scene over the top, so `SceneWiring`'s
   footsteps and ambience were gone and nothing errored. The scene came back
   whole and quiet, five objects short. Diff the OBJECT COUNT and the set of
   `m_Name:` values against HEAD after every rebuild — the fileID churn hides it
   completely in a normal diff.
3. **The full build order is four passes, not three:** Grey Box → Arsenal → VFX →
   Grey Box → SceneWiring → GreyBoxVerify. Arsenal creates the launcher, VFX
   creates the round and assigns it, and the SECOND grey box pass is what puts
   `Fx_Rocket` in the pool prewarm and the weapon registry on the cheat console.

**One design decision was reversed mid-session by a failing test**, and it is
worth recording because the reasoning that produced the wrong answer was good.
An undeclared body defaulted to `Faction.Player` on the argument that the failure
fell safely — a hostile that forgot the interface would only BLOCK friendly fire.
True, and half the picture: it also made every prop and every training dummy
transparent to the player's own rockets. The launcher's first test fired point
blank into the sandbox dummy and watched the round sail through it. The fix was a
third value, `Unaligned`, and making BOTH sides declare. A default that reads
safely in one direction is not safe; it is untested in the other.

**Also worth knowing for the next Track G phase:** `Tools/screenshot.mjs` cannot
photograph any weapon work at all. `BuildSmokeTest` lives in `CoD.Core`, which
references nothing, so it can never reach `WeaponController` — no frame it has
ever rendered contains a shot, a tracer, an impact or a rocket. The way to
compare a render change against a baseline today is `git stash -u`, rebuild,
look, pop. That was done for this commit: the frames are byte-comparable in
content to HEAD's, which is what makes "no visual regression" a claim rather than
a hope.

## 2026-08-12 — W5, attachments and the sniper

**What landed.** `AttachmentConfig` composed into `WeaponConfig`, a `WeaponStat`
enum and sheet kept strictly separate from the passive `Stat` one, five
attachments, and `SR_Longshot` — a bolt gun whose 5x optic is an ATTACHMENT
rather than a config field, so the weapon is still a legal weapon with the scope
off. Detail in [docs/systems/weapons.md](systems/weapons.md).

**Two pieces of W5 were deliberately not built**, and the reason is worth keeping
because both look like omissions from the plan: the scope OVERLAY image and
HOLD-BREATH. Both need the nine sway numbers that G6 is about to move out of
`WeaponSway` into a `ViewmodelConfig`. Building either now means writing it
twice, and G6 has already been reverted once. They are recorded in the DO NEXT
list against G6 rather than left to be rediscovered.

**One flake was found and fixed rather than shrugged at.**
`TheBeacon_Relocates_AndHealsWithinItsBudget` failed once and passed on a
re-run — the kind of result that gets waved through as "PlayMode is flaky". It
was not: the test set the player's position ONCE and then waited up to twenty
seconds, which is a race against both a CharacterController resolving a capsule
dropped into the floor and the beacon RELOCATING if a wave boundary crossed the
wait. Either leaves the player off the pad and reports "timed out", which says
nothing. It now re-seats the player every frame and asserts the wave has not
ended, so the two real causes fail with their own names. The assertions it exists
for are unchanged — the test got stronger, which is the only direction a test is
allowed to move here.

## 2026-08-12 — G5a, the mixer, and where hand-editing stops

**The one asset no builder can produce now exists**, and getting it there
narrowed the rule it was documented under. `Assets/_Project/Audio/Master.mixer`
was created in the editor by a human and then FINISHED AS TEXT with the editor
closed: ten buses, four exposed parameters, and `AudioBuilder` now routing
footsteps to `World` and ambience to `Ambience` on every run.

**Groups are hand-writable. Effects are not.** A Send, a Receive and an SFX
Reverb were written into the YAML alongside them. The file parsed, the asset
imported, and the new `VerifyMixer` gate passed — because loading a mixer does
not build its DSP graph. The PlayMode suite then went 60 → 57, all three failures
reading `Assertion failed on expression: 'res == FMOD_OK'`, thrown the instant a
routed AudioSource instantiated the mixer. A built-in effect needs a
`m_Parameters` list of parameter GUIDs that only the editor mints; written empty,
the effect exists on paper and its DSP cannot be constructed. Removing the three
effects put all 60 back. The Reverb bus ships empty and finishing it is four
clicks, listed in docs/systems/audio.md.

**Two things this pass added that outlive it.**

1. `AudioBuilder.VerifyMixer` / `VerifyMixerHeadless` — the mixer was the only
   asset in the project with no builder, and therefore the only one with nothing
   watching it. It checks NAMES rather than structure on purpose: where Reverb
   sits is a mixing decision a human may change, but whether a group called
   `World` exists is a contract with `AudioBuilder` and `SettingsHub`.
2. Routing the two configs made the PlayMode suite a gate for the mixer's DSP
   graph, which is what caught the effects. Before it, a malformed effect would
   have been invisible until somebody pressed Play.

**What was deliberately NOT done**, so a future session does not read it as an
omission: the master volume slider still writes `AudioListener.volume`. Moving it
onto the exposed `MasterVolume` today would be a regression, not progress — only
footsteps and ambience are routed, so the slider would silently stop working for
the weapons, impacts, hitmarker and every UI cue. The switch belongs to the
moment a second bus needs balancing, which is the same moment every source gets
an output group.
