---
name: tenten-game-playbook
description: Playbook for building games in Unity — FPS, shooter, arcade, single-player or local. Use this skill whenever the user wants to start a game project, scaffold a Unity repo, asks "how should I build this game", asks about gunplay feel, weapon tuning, recoil, time-to-kill/TTK, enemy AI, wave spawning, or game folder/asset conventions — even if they never say "playbook" or "Unity". Covers Unity 6 + URP setup tuned for a 4 GB-VRAM laptop, ScriptableObject tuning discipline, Unity .gitignore and Git LFS, guard scripts against known Unity repo disasters, concrete gun-feel numbers, and a starter CLAUDE.md for the game repo. This is the game-side sibling of tenten-app-playbook — do NOT use that one for game projects (Cloudflare/D1/Hono do not apply here).
---

# TenTen Game Playbook

The sibling of `tenten-app-playbook`, for a completely different domain. The web
playbook's stack half — Workers, D1, Drizzle, Hono, zod, RTL, centimes — **does
not apply to a game and must not be carried over.** What carries over is the
philosophy that made digitronics.ma survivable by one person:

- every convention written down once, in a `CLAUDE.md` the repo owns
- every value that gets tuned lives in **one place**, never scattered
- every disaster becomes an **automated guard**, not a memory
- every subsystem gets a code-verified doc, updated in the same commit

The user (Doctor) is a solo developer on a **MSI Katana GF76** — i7-12650H,
32 GB RAM, **RTX 3050 Laptop with only 4 GB VRAM**, ~170 GB free. Every
recommendation below is shaped by that machine. VRAM and thermal throttling are
the real constraints, not the CPU.

## Step 0 — Understand the game (one exchange, max)

Establish four facts. Infer them from the request when possible; ask only for
what is genuinely missing, in a single question:

1. **Mode** — single-player offline, local co-op/split-screen, or online?
   (Online changes everything and is the one answer that invalidates this
   playbook's assumptions.)
2. **Perspective & pace** — first-person arcade shooter, third-person, top-down?
3. **Scope** — a feel prototype, a personal sandbox, or something meant to ship?
4. **Art source** — bought asset packs, stylized primitives, or custom?

Do not run a long interview. One exchange, then commit to a plan.

## Step 1 — Lock the engine decision

**Unity 6 LTS + URP is the default and, on this hardware, almost always the
right call.** State the reasoning briefly, then move on:

| Engine | Use when | Reality on a 4 GB-VRAM laptop |
| --- | --- | --- |
| **Unity 6 + URP** (default) | Any FPS/3D game on this machine | Runs comfortably; C# is close to the user's TypeScript; biggest asset-store library for FPS content |
| **Unreal 5** | Only if the user upgrades to 8 GB+ VRAM and wants photoreal | Lumen/Nanite + the editor itself exceed 4 GB VRAM; disk footprint fights the ~170 GB free |
| **Godot 4** | Small 2D/stylized projects, or if the user wants fully open source | Lighter, but weakest 3D FPS tooling and asset ecosystem |

**Never propose HDRP** on this machine. Never propose UE5 without first noting
the VRAM problem.

After choosing, **read `references/unity-setup.md`** — it has the project
settings that are invisible until they cost hours (domain reload, Auto Generate
lighting, asmdef compile times, texture import caps, GPU-selection trap on
dual-GPU laptops).

## Step 2 — Apply the house conventions

**Read `references/conventions.md`** and apply it to every file generated. The
headline rules, so they are never skipped:

- **Every tunable number lives in a ScriptableObject asset, never in a
  MonoBehaviour field and never as a literal in code.** This is the game
  equivalent of the web playbook's "integer centimes / one date helper" rule and
  it is the single most important convention here. Weapon stats, movement
  speeds, enemy behaviour, wave composition — all data assets.
- One `GameConfig` SO holds global constants (player HP, gravity, base FOV).
  Nothing reads a magic number from a script.
- `Assets/_Project/` holds all first-party work; bought packs stay in their own
  untouched folders so they can be updated or deleted cleanly.
- Assembly definitions (`.asmdef`) per subsystem from day one — compile time is
  iteration speed, and iteration speed is the whole project.
- C# `<Nullable>enable</Nullable>`, warnings as errors on first-party asmdefs,
  no `GameObject.Find` / `FindObjectOfType` / `GetComponent` inside
  `Update`/`FixedUpdate`/`LateUpdate` — cache in `Awake`.
- New Input System only. Never the legacy `Input.GetKey` API.
- All `Debug.Log` in shipping code goes through one `GameLog` wrapper stripped
  by a `#if UNITY_EDITOR || DEVELOPMENT_BUILD` guard.
- Saves are versioned JSON with a `schemaVersion` field and a migration path
  from day one. Save-file breakage is the game equivalent of migration row loss.
- **Every new pooled/spawned object type is registered in the object pool in
  the same commit that creates it.** Un-pooled spawning is the GC-hitch factory.

## Step 3 — Set up the repo before writing code

Unity repos are destroyed by three specific things, all preventable in ten
minutes. **Read `assets/guards/README.md`** and do all of this before the first
commit:

1. Copy `assets/gitignore.template` → `.gitignore`. `Library/` alone is
   multiple GB and will not fit the disk budget twice.
2. Copy `assets/gitattributes.template` → `.gitattributes`, then
   `git lfs install`. Binaries committed without LFS cannot be removed from
   history later without a rewrite.
3. Editor Settings → **Asset Serialization: Force Text**, **Version Control:
   Visible Meta Files**. Non-text scenes/prefabs make every diff a black box.
4. Copy the guards from `assets/guards/` into `Tools/guards/` and wire the
   pre-commit hook shown in that README.

Guards included: build artifacts tracked in git, meta-file integrity checked
on disk **and** in git tracking, large binaries outside LFS,
`Find`/`GetComponent` inside per-frame methods, and mutable static state (the
top bug source once Domain Reload is disabled). Each script's header documents
the disaster it prevents. All five are cross-platform — no POSIX shell syntax,
they run identically on Windows.

## Step 4 — Write the repo's CLAUDE.md

Copy `assets/CLAUDE-TEMPLATE.md` to the repo as `CLAUDE.md` and fill the
placeholders (`{{PROJECT_NAME}}`, `{{GAME_TYPE}}`, `{{UNITY_VERSION}}`). This is
what makes future Claude Code sessions follow the playbook without this skill
installed there. Keep it under ~150 lines at birth; it grows with the project.

Once the project passes ~3 subsystems (weapons, enemies, waves, save, audio —
this happens fast), start `docs/systems/`: one code-verified markdown map per
subsystem, updated in the same task that changes the subsystem. Recommend this
proactively; it is the reason digitronics.ma stayed maintainable solo.

## Step 5 — Build in feel order, not feature order

The order matters more than any individual task. **Read
`references/gunfeel.md`** — it has the concrete starting numbers (fire rate,
TTK, ADS time, recoil per shot, spread, FOV) and the juice checklist that
separates a shooter that feels good from one that feels dead.

1. **One gun in a grey box room.** No textures, no menu, no enemies. Tune until
   firing it is satisfying on its own. This takes longer than it sounds and
   everything else is worthless without it.
2. **Impact feedback** — hitmarker, decals, surface-specific particles and
   sounds. Feel is 50% what the gun does and 50% what the world does back.
3. **One enemy** that pathfinds, takes cover, and shoots back with a human
   reaction delay.
4. **One map**, three-lane layout.
5. **One mode** — wave survival is the cheapest to build and the most replayable
   solo.
6. Only then: more weapons, more enemies, more maps.

## Step 6 — Definition of done for a scaffold

1. Project opens on the dedicated GPU (verify: Help → About shows the RTX, not
   Intel UHD).
2. `Tools/guards` pass; pre-commit hook installed and tested with a dummy
   commit.
3. A **smoke test scene** boots, spawns the player, and fires one shot with
   working impact feedback. This is the game's `/api/health`.
4. `.gitignore`, `.gitattributes` + LFS, `CLAUDE.md`, and a README stating the
   Unity version and render pipeline all committed.
5. First-party asmdefs compile with zero warnings.
6. No tuning number exists outside a ScriptableObject.
7. The repo has a **remote** and the first push — including LFS objects —
   succeeded. One laptop is zero backups, and LFS quotas are checked *before*
   history depends on them (see `references/unity-setup.md`).

## Scaling down without losing the plot

For a throwaway feel prototype it is fine to skip: `docs/systems/`, save
versioning, object pooling, and the asmdef split. It is **not** fine to skip:
the `.gitignore`, LFS, Force Text serialization, meta-file discipline, and
ScriptableObject tuning. Those cost minutes now and save days later — the same
sentence as the web playbook, learned the same way.

## Deliverable style

When this skill triggers in a chat rather than inside a repo, the natural
deliverable is a **kickoff package**: the engine decision with a one-paragraph
justification, the folder structure, the initial ScriptableObject sketch, the
adapted `CLAUDE.md`, and a ready-to-paste first-session prompt built from
`assets/KICKOFF-PROMPT-TEMPLATE.md`. Propose the plan, then build what is
approved — do not generate dozens of files unasked.
