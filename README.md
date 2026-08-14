# Call of Duty

A mission-based military FPS with a horde-survival core. Offline single-player,
Windows PC.

**Campaign** is the headline mode: ordered objectives, checkpoints and
comms-delivered story across fixed arenas. **Endless** is the original wave loop
and still the tuning ground for every combat number in the game — waves escalate,
a shop between them sells weapons, effect modules and passive upgrades, and
permadeath ends the run with the highest round reached as the record. Sandbox
crosses both: everything unlocked, cheat console on, nothing recorded.

You fight autonomous drones and the Meridian PMC soldiers being paid to cover
for them.

| | |
| --- | --- |
| Engine | Unity 6 LTS, **URP** (never HDRP) |
| Platform | Windows standalone, **offline only** — no netcode, ever |
| Hardware target | RTX 3050 Laptop, **4 GB VRAM** — the binding constraint on art and spawn counts |
| Namespace | `CoD.*` (`CoD.Core`, `CoD.Player`, `CoD.Weapons`, `CoD.Enemies`, `CoD.Waves`, `CoD.UI`) |
| Target TTK | ~250 ms on a standard drone with the starter rifle |

## Status — code-complete and shippable-shaped, and never played

Unity 6000.0.81f1 + URP, licence active. There is a main menu, a pause menu,
working settings, both modes, two arenas, and a Windows `.exe` that has been
built and run outside the editor. The whole loop is in: movement and an arsenal
with attachments and optics, drone and human enemies, timed waves feeding a
shop, permadeath with a saved best round, stacking effect modules, an
audio mixer, a post-processed image on imported CC0 surfaces — and, on top of
all of it, a mission director with objectives, zones and checkpoints driving two
authored missions.

**What is verified.** Nine assemblies compile with zero errors *and* zero
warnings, eight guards pass, 287 tests across both suites drive the real loop and
the real menu scene, every scene reference is proven by a save/reload round trip,
and a Windows player boots headlessly, reaches the menu, loads the arena and logs
zero errors — with no cheat-console code in the release binary at all.

**What is not.** Nobody has played it. The content gate — ship a slice, then play
it before authoring more — has been suspended twice by explicit instruction, so
the campaign, the wave identities and the shop consumables are covered by tests
and have never been *felt*. Frame time on the target laptop is unmeasured for the
same kind of reason: headless runs do no GPU work, so no automated gate can
answer it.

[CLAUDE.md](CLAUDE.md) carries the per-milestone table and marks which rows are
still unplayed; that table is the authoritative state, not this paragraph.
`Tools/screenshot.mjs` renders real frames from the real player and closes most
of the remaining gap — most, not all. It cannot say whether the game is fun, how
it holds up on a 3050, or whether a lighting scheme reads as atmospheric or as
blotchy while you move through it. Those stay human questions, and the tuning
card at the top of [docs/NEXT-SESSION-PROMPT.md](docs/NEXT-SESSION-PROMPT.md) is
how they get asked.

### Running it from a fresh clone

The scenes, prefabs and tuning assets are **generated**, never hand-built — the
builder is the source of truth, and a value that lives only in an `.asset`
silently reverts the next time it runs. So:

```
CoD → Build Grey Box            # 00_Boot, 20_MainMenu, 10_GreyBox, every prefab and
                                # tuning asset, Build Settings — and verifies itself
CoD → Build Missions            # the objective assets and the mission catalog
CoD → Build Tazir Pass Outpost  # 11_AtlasOutpost, mission 2's arena
```

`Build Grey Box` picks the outpost scene up into Build Settings if it already
exists, so on a first run do it once more after the outpost. Then open
`20_MainMenu` and press Play.

## Continuing the work

Paste [docs/NEXT-SESSION-PROMPT.md](docs/NEXT-SESSION-PROMPT.md) into a fresh
Claude Code session. It carries the current state, the tools, the roadmap, and the
gotchas that already cost time once — a new session starts with none of that.

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
node Tools/check.mjs        # eight guards
node Tools/typecheck.mjs    # compiles every assembly, no editor, no licence
```

`typecheck.mjs` drives Unity's bundled Roslyn against the editor's own reference
assemblies, so it answers "does this compile?" without launching Unity — which
matters because Unity refuses to start without an activated licence. It builds
ugui from the editor's source and fetches the pinned Input System from the Unity
registry, caching both under `Library/`. Warnings count as failure: first-party
code stays at zero. Both run automatically on every commit.

Eight guards, plain Node, no dependencies. They cover: build artifacts tracked in
git, `.meta` file integrity (on disk *and* in git), binaries outside LFS, LFS
hooks surviving the `core.hooksPath` redirect, per-frame lookups, mutable
statics, the 1024 texture budget, and the LFS quota. Each script's header
documents the disaster it prevents — read it before deleting one.

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
  guards/          ← the eight guards + their README
  hooks/           ← committed hooks, enabled via core.hooksPath
                     pre-commit (guards) + LFS's four (pre-push, post-*)
docs/
  UNITY-SETUP-CHECKLIST.md
  DATA-MODEL-SKETCH.md    ← the ScriptableObjects that ARE the game
  playbook/               ← vendored TenTen game playbook
  systems/                ← one code-verified map per subsystem, from Phase 3 on
```
