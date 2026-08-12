# Guard scripts — each one is a Unity disaster, automated away

Plain Node (v22 here), no npm packages, no `package.json`. No shell redirection
anywhere, so they behave identically on Windows and Linux.

Run them all: `node Tools/check.mjs`

## What each guard prevents

| Guard | Prevents | Why a guard and not discipline |
| --- | --- | --- |
| `guard-no-build-artifacts.mjs` | `Library/`, `Temp/`, `obj/`, `Build/`, `*.csproj` tracked in git | Library/ is 5–20 GB and grows on every import. Committed once, it is in history forever. |
| `guard-meta-files.mjs` | Missing/orphaned `.meta` files — on disk **and in git tracking** | Breaks GUID references — but only for the *next* clone. The classic cause is `.gitignore` excluding `*.meta`, which only the git-tracking check catches. |
| `guard-lfs-binaries.mjs` | Binaries committed outside Git LFS | Silent. Repo just gets slower until clones take twenty minutes, and by then history is poisoned. |
| `guard-no-find-in-update.mjs` | `Find`/`GetComponent`/`Camera.main`/`Instantiate` inside `Update`/`FixedUpdate`/`LateUpdate` | Invisible with 3 objects in a test scene, fatal with 40 in a real wave. |
| `guard-no-mutable-statics.mjs` | Mutable static fields/events/settable properties in first-party code | Domain Reload is off for iteration speed, so statics survive between Play sessions — double-fired events and stale singletons that appear on the second play only. |
| `guard-lfs-hooks.mjs` | The Git LFS hooks going missing when `core.hooksPath` is redirected | Redirecting hooks makes git ignore `.git/hooks/` entirely, where `git lfs install` puts them. Pushes still succeed — pointers push fine — but the objects never upload, and the break surfaces as a corrupt *clone*, later, elsewhere. |
| `guard-texture-budget.mjs` | First-party textures imported above 1024 (2048 for weapons/hands) | Texture memory scales with area, so 4K instead of 1K is 16× the VRAM. Forty 4K textures is ~900 MB of a 4 GB card. Nothing goes wrong in the editor — it goes wrong in the built player, on the target laptop, after the 4K source is already in LFS history forever. |
| `guard-lfs-budget.mjs` | The LFS working set passing 400 MB, or any one object passing 25 MB | LFS storage is **cumulative and never reclaimed**: re-exporting a file adds a second copy, billed forever. Blowing GitHub's free 1 GB/month bandwidth breaks clones across the whole account, and the quota page is the only place the truth is visible. |

Read a guard's header before deleting it. Each header documents the failure
mode, not just the rule.

## Current state: all eight pass, and the hook is live

`core.hooksPath` is set to `Tools/hooks`, so `pre-commit` runs every guard on
every commit. This was not true during setup: `guard-meta-files.mjs` and
`guard-texture-budget.mjs` both exit 1 with

```text
guard-<name>: no Assets/ folder found — run from the project root
```

when there is no Unity project yet, and **that is the guard working correctly** —
it is how they catch being run from the wrong directory. Do not "fix" a future
recurrence of that message by weakening the check; fix the directory.

## Activating the hook (on a new machine or a fresh clone)

```bash
git config core.hooksPath Tools/hooks
node Tools/check.mjs                 # all eight must pass
```

`core.hooksPath` is used instead of `.git/hooks/` so the hook is committed and
survives a fresh clone. It is a per-clone git config setting, so re-run that one
line on any new machine.

**Why `Tools/hooks/` also contains `pre-push`, `post-checkout`, `post-commit`,
and `post-merge`:** those are Git LFS's own hooks, normally installed into
`.git/hooks/`. The redirect above makes git stop reading `.git/hooks/`
completely, so they are mirrored here — otherwise LFS would quietly stop
uploading objects. `guard-lfs-hooks.mjs` fails the build if they go missing.

Then prove it works, before trusting it:

```bash
mkdir -p Library && echo test > Library/dummy.txt
git add -f Library/dummy.txt
git commit -m "should be blocked"   # must FAIL
git reset && rm -rf Library
```

A guard that has never been seen to fail is a guard that might not work.

## Adding a new guard

When something breaks in a way that could break again, write a guard for it in
the same commit as the fix. `Tools/check.mjs` picks up any file matching
`guard-*.mjs` automatically. Header comment format:

```text
/**
 * GUARD: <the rule, in one line>
 *
 * THE DISASTER THIS PREVENTS
 * <what actually happened, what it looked like, why it was not obvious>
 *
 * Run:  node Tools/guards/guard-<name>.mjs
 */
```

The story in the header is the point. A rule with no story gets deleted by a
future session that does not know what it cost.
