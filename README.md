# Call of Duty

A fixed-arena horde-survival FPS. Offline single-player, Windows PC.

Waves of malfunctioning military drones escalate every round; between waves a
shop sells weapons, effect modules, and passive upgrades. Permadeath — the
meta-goal is the highest round reached.

| | |
| --- | --- |
| Engine | Unity 6 LTS, **URP** (never HDRP) |
| Platform | Windows standalone, **offline only** — no netcode, ever |
| Hardware target | RTX 3050 Laptop, **4 GB VRAM** — the binding constraint on art and spawn counts |
| Namespace | `CoD.*` (`CoD.Core`, `CoD.Player`, `CoD.Weapons`, `CoD.Enemies`, `CoD.Waves`, `CoD.UI`) |
| Target TTK | ~250 ms on a standard drone with the starter rifle |

## Status — Phases 0–3 authored, one step from playable

Unity 6000.0.81f1 + IL2CPP is installed, the URP project is at the repo root, and
all of the grey-box code compiles clean. The repo foundation landed *before*
Unity deliberately: Unity's first import generates a 5–20 GB `Library/`, and a
`Library/` committed once is in history forever.

**What is left is one thing only: sign in to Unity Hub with a Unity ID.** Unity
will not start without an activated licence, and that is not something a script
can do. Then:

```
CoD → Build Grey Box
```

which generates every prefab, both scenes (`00_Boot`, `10_GreyBox`) and the
tuning assets, and adds the scenes to Build Settings. Open `10_GreyBox`, press
Play, and shoot the red blocks.

The scripts compile but have never *run*. Expect to fix runtime wiring, then
spend real time in the grey room tuning recoil, ADS and the hitmarker — working
and feeling good are different milestones, and only the second one matters.
After that: the Rusher drone, then waves, then the shop.

## Working in this repo

Read **[CLAUDE.md](CLAUDE.md)** first — it is the engineering contract, not a
suggestion. The headline rules:

- Every tunable number lives in a ScriptableObject asset. Never a literal in a
  script, never a public field on a MonoBehaviour.
- No `Find`/`GetComponent`/`Camera.main`/`Instantiate` inside `Update`,
  `FixedUpdate`, or `LateUpdate`.
- No mutable static state — Domain Reload is off, so statics survive between
  Play Mode sessions.
- `#nullable enable` atop every first-party `.cs` file; zero console warnings.
- Everything that spawns goes through the object pool.

## Guards and type-checking

```bash
node Tools/check.mjs        # six guards
node Tools/typecheck.mjs    # compiles every assembly, no editor, no licence
```

`typecheck.mjs` drives Unity's bundled Roslyn against the editor's own reference
assemblies, so it answers "does this compile?" without launching Unity — which
matters because Unity refuses to start without an activated licence. It builds
ugui from the editor's source and fetches the pinned Input System from the Unity
registry, caching both under `Library/`. Warnings count as failure: first-party
code stays at zero. Both run automatically on every commit.

Six guards, plain Node, no dependencies. They cover: build artifacts tracked in
git, `.meta` file integrity (on disk *and* in git), binaries outside LFS, LFS
hooks surviving the `core.hooksPath` redirect, per-frame lookups, and mutable
statics. Each script's header documents the disaster it prevents — read it
before deleting one.

The hook is active (`core.hooksPath Tools/hooks`) and has been verified to block
a deliberate `Library/` commit. See [Tools/guards/README.md](Tools/guards/README.md).

## Layout

```text
Assets/            ← URP project, merged at the repo root
  _Project/        ← all first-party work
  ThirdParty/      ← bought packs, never edited in place
Tools/
  check.mjs        ← runs every guard
  typecheck.mjs    ← compiles all assemblies without the editor
  guards/          ← the six guards + their README
  hooks/           ← committed hooks, enabled via core.hooksPath
                     pre-commit (guards) + LFS's four (pre-push, post-*)
docs/
  UNITY-SETUP-CHECKLIST.md
  DATA-MODEL-SKETCH.md    ← the ScriptableObjects that ARE the game
  playbook/               ← vendored TenTen game playbook
  systems/                ← one code-verified map per subsystem, from Phase 3 on
```
