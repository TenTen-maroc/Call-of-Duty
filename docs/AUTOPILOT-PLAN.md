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

### M4 — Human tuning pass (blocked on a person)

The card at the top of [NEXT-SESSION-PROMPT.md](NEXT-SESSION-PROMPT.md). Seven
things to feel, each naming the asset field to move. Autopilot's job here is to
have already made every one of those numbers a single Inspector field.

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
