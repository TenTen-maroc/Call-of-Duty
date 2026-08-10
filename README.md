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

## Status — Phase 0

The repo foundation is in place. **The Unity project itself does not exist yet**
and Unity is not installed on this machine.

Start at **[docs/UNITY-SETUP-CHECKLIST.md](docs/UNITY-SETUP-CHECKLIST.md)** —
installer, the dual-GPU fix, project creation, Project Settings, packages, and
hook activation, in the order that matters.

Phase 0 landed before Unity deliberately: the first time Unity opens a project it
generates a `Library/` folder of 5–20 GB, and a `Library/` committed once is in
history forever. `.gitignore`, LFS, and the guards had to exist first.

Next: **Phase 2** (folders + asmdefs) → **Phase 3, the grey box** — one gun in a
grey room, tuned until firing it is satisfying on its own. No drones, no waves,
no shop until that is true.

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

## Guards

```bash
node Tools/check.mjs
```

Six guards, plain Node, no dependencies. They cover: build artifacts tracked in
git, `.meta` file integrity (on disk *and* in git), binaries outside LFS, LFS
hooks surviving the `core.hooksPath` redirect, per-frame lookups, and mutable
statics. Each script's header documents the disaster it prevents — read it
before deleting one.

**One guard fails on purpose right now**: `guard-meta-files` exits 1 while there
is no `Assets/` folder, which is how it detects being run from the wrong
directory. The pre-commit hook at `Tools/hooks/pre-commit` is committed but
inactive until the Unity project exists. See
[Tools/guards/README.md](Tools/guards/README.md).

## Layout

```text
Assets/            ← created by Unity (step 3 of the checklist)
  _Project/        ← all first-party work
  ThirdParty/      ← bought packs, never edited in place
Tools/
  check.mjs        ← runs every guard
  guards/          ← the six guards + their README
  hooks/           ← committed hooks, enabled via core.hooksPath
                     pre-commit (guards) + LFS's four (pre-push, post-*)
docs/
  UNITY-SETUP-CHECKLIST.md
  DATA-MODEL-SKETCH.md    ← the ScriptableObjects that ARE the game
  playbook/               ← vendored TenTen game playbook
  systems/                ← one code-verified map per subsystem, from Phase 3 on
```
