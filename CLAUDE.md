# CLAUDE.md — engineering rules for Call of Duty

> New here? This file is the contract for every Claude Code session and human
> contributor in this repo. It inherits the TenTen **game** playbook (vendored at
> [docs/playbook/](docs/playbook/)). When code conflicts with this doc, update the
> doc first, then the code.
>
> This is a Unity project. The TenTen **app** playbook (Cloudflare, D1, Drizzle,
> Hono, zod) does **not** apply here. Do not propose any of it.

## Current state — read this first

**Phase 0 complete. The Unity project does not exist yet.**

| | |
| --- | --- |
| Repo foundation | ✅ `.gitignore`, `.gitattributes` + LFS, guards, docs |
| Unity installed on this machine | ❌ not yet — see [docs/UNITY-SETUP-CHECKLIST.md](docs/UNITY-SETUP-CHECKLIST.md) |
| `Assets/`, `Packages/`, `ProjectSettings/` | ❌ created by Unity, not by hand |
| Pre-commit hook | committed but **inactive** — activated after the Unity project exists |
| `node Tools/check.mjs` | 5 of 6 guards pass; `guard-meta-files` fails by design until `Assets/` exists |

Next milestone is **Phase 3, the grey box**: one gun in a grey room that feels
good. Nothing else. See *Build in feel order* below.

## The game

A fixed-arena horde-survival FPS. Offline single-player, PC.

You are a lone operator in a compromised facility. Waves of malfunctioning
military drones escalate every round. Between waves, a shop sells weapons,
effect modules, and passive upgrades. Permadeath; the meta-goal is the highest
round reached. A separate sandbox mode unlocks everything for pure play.

The feel target is grounded-military (heavy weapons, tight ADS, punchy audio,
grey/red tactical palette) — the *mood* of a modern military shooter, on
non-human enemies so a solo dev never touches humanoid animation.

> The name is a working title. Enemies are drones, deliberately: humanoid
> animation is the single largest art-cost sink in a solo FPS, and skipping it
> is what makes this project finishable.

## Project facts

- **Type**: fixed-arena horde-survival FPS, wave-based, permadeath
- **Engine**: Unity 6 LTS, **URP** (never HDRP)
- **Platform**: Windows PC, standalone, **offline only**
- **Networking**: none — never. Do not add netcode, servers, or online features.
- **Hardware target**: RTX 3050 Laptop, **4 GB VRAM**. VRAM is the binding
  constraint on every art and spawn-count decision.
- **Namespace / asmdef prefix**: `CoD.*` — `CoD.Core`, `CoD.Player`,
  `CoD.Weapons`, `CoD.Enemies`, `CoD.Waves`, `CoD.UI`.
- **Owner**: solo maintenance; optimise for "understandable cold in 6 months".

## Locked design decisions

- **Enemies**: 3 drone archetypes, no more, for v1.
  - *Rusher* — fast, low HP, closes to melee, explodes on contact.
  - *Shooter* — hovers at range, fires in bursts, holds distance.
  - *Tank* — slow, high HP, heavy hits, forces the player to reposition.
- **Arena**: one fixed space with cover to break line-of-sight. Enemies path to
  the player; no large open level, no free-roam, for v1. NavMesh baked once.
- **Weapons**: ONE modular system. A "weapon" is a `WeaponConfig` asset plus an
  **ordered list** of `EffectModule` assets (explosive / pierce / ricochet /
  chain) — stacking is the point. Drones mirror the pattern with `AttackModule`
  assets. New weapons and drones are DATA, never new code. Configs are
  read-only at runtime; state lives in RunState/StatSheet/WeaponRuntime (see
  [docs/DATA-MODEL-SKETCH.md](docs/DATA-MODEL-SKETCH.md)). This is the
  "without limits" engine.
- **Loop**: timed waves (~45s target) → shop break → next wave. Escalating
  count and mix. Permadeath ends the run; a persistent record stores best round.
- **Modes**: Run (earned power, default) and Sandbox (everything unlocked +
  cheat console). Same core scene, different starting inventory and rules.

## Locked tech decisions

- Unity 6 LTS + URP. New **Input System** only — never `Input.GetKey`.
- **Cinemachine** for all camera work (shake, recoil impulse, follow).
- **AI Navigation** (modern NavMesh) for drone pathing.
- `#nullable enable` is the first line of every first-party `.cs` file (asmdefs
  have no nullable switch; a global `csc.rsp` would break ThirdParty). First-
  party code stays at zero console warnings — Unity has no per-asmdef
  warnings-as-errors, so the console is the gate.
- Domain Reload and Scene Reload are **disabled** for fast Play Mode entry —
  therefore **no mutable static state** (enforced by a guard). Reset any
  unavoidable static in `[RuntimeInitializeOnLoadMethod]`.
- Bought asset packs live in `Assets/ThirdParty/` and are **never edited in
  place**. Subclass, or copy the file into `_Project/` and edit the copy.

## Conventions (non-negotiable)

- **Every tunable number lives in a ScriptableObject asset in
  `Assets/_Project/Data/`.** Never a literal in a script; never a public field
  on a MonoBehaviour. MonoBehaviours hold *state* (current ammo, current HP),
  never *settings*. This is the most important rule in the file.
- Global constants (player HP, gravity, base FOV) live in one `GameConfig`
  asset. Wave definitions, enemy stats, weapon stats, shop prices: all assets.
- No `GameObject.Find`, `FindObjectOfType`, `GetComponent`, `Camera.main`,
  `Instantiate`, or `Destroy` inside `Update`/`FixedUpdate`/`LateUpdate`. Cache
  in `Awake`, serialize the reference, or use the pool. Enforced by a guard.
- **Everything that spawns goes through the object pool** — bullets, casings,
  impact VFX, damage numbers, drones — registered in the pool in the same commit
  that creates the prefab. In a horde game with 40+ enemies and hundreds of
  projectiles, `Instantiate`/`Destroy` per frame is the GC-hitch factory.
- No per-frame allocation: no LINQ, no `new` collections, no string
  concatenation. Non-allocating physics overloads with a pre-sized buffer.
- Physics in `FixedUpdate`. Input in `Update`. Camera in `LateUpdate` — always.
- All logging through the `GameLog` wrapper (stripped outside editor/dev builds).
- Saves (best round, unlocks, settings) are versioned JSON with `schemaVersion`,
  written to a temp file then moved into place, with one `.bak` kept.
- FOV: Unity's field is VERTICAL. For a 95° horizontal feel, enter ~62.
- Texture Max Size 1024 project-wide (2048 only for weapons/hands). 4 GB VRAM is
  spent on textures, not geometry.
- Naming: classes/files `PascalCase` (filename == type), private fields
  `_camelCase`, constants `SCREAMING_SNAKE_CASE`, SO assets `Category_Variant`
  (`AR_Standard`, `Drone_Rusher`, `Wave_05`), scenes `NN_Name`.

## Repo hygiene

- `.gitignore` and `.gitattributes` (Git LFS) are set up — done before the first
  commit, which is the only time it is cheap. All binaries through LFS. Mind the
  LFS quota (GitHub free is 1 GB storage / 1 GB bandwidth per month; one asset
  pack exceeds it — see [docs/playbook/unity-setup.md](docs/playbook/unity-setup.md)).
- Editor Settings: **Force Text** serialization, **Visible Meta Files**.
- Every asset has a committed `.meta` sibling.
- Commit `Assets/`, `Packages/`, `ProjectSettings/`. Nothing else.
- Push to the remote every session. One laptop is zero backups.

## Guards

`node Tools/check.mjs` runs every guard in `Tools/guards/`. Each header
documents the disaster it prevents. **Do not delete a guard without reading its
header.** Current guards: build artifacts in git, meta-file integrity (disk +
git), LFS coverage, LFS hooks surviving the `core.hooksPath` redirect, per-frame
lookups, and mutable statics. See
[Tools/guards/README.md](Tools/guards/README.md) — including why one guard
fails on purpose until the Unity project exists.

## Build in feel order, not feature order

1. One gun in a grey box room, tuned until firing it is satisfying alone
2. Impact feedback — hitmarker, decals, surface sounds
3. One drone (the Rusher) that pathfinds and reaches the player
4. The arena, three-lane-ish layout with cover
5. The wave loop + shop
6. Only then: the other two drones, effect modules, more weapons

Target TTK on a standard drone with the starter rifle is **~250 ms**. Movement,
arena scale, and spawn distance are tuned around it. Change it deliberately.

## Quality gates (every task)

- Unity console: zero errors, zero warnings in first-party code
- `node Tools/check.mjs` — all guards pass
- The smoke test scene boots, spawns the player, and fires one shot with working
  impact feedback
- No new tuning number introduced outside a ScriptableObject

## Docs

Once past ~3 subsystems, [docs/systems/](docs/systems/) holds one code-verified
markdown map per subsystem (weapons, drones, waves, shop, save), updated **in
the same task** that changes the subsystem.

## Debug / cheat console

The in-game console (godmode, infinite ammo, spawn weapon, spawn N drones,
slow-mo, skip to wave N, damage multiplier) is a **feature** of Sandbox mode,
not scaffolding. Keep it working — it is also the fastest way to test everything
else. Gate it behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` for Run mode.
