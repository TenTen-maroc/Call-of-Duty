# Unity setup checklist

> Everything in this file is a thing Claude Code cannot do for you — installers,
> Hub dialogs, and Project Settings toggles. Work top to bottom. The order
> matters in two places, both flagged.
>
> Machine: MSI Katana GF76, i7-12650H, 32 GB RAM, **RTX 3050 Laptop / 4 GB VRAM**,
> 175 GB free. Every setting below is chosen for that.

Tick these off as you go — the repo is at Phase 0 until step 8 passes.

---

## 1. Fix the dual-GPU trap — BEFORE installing anything

**Do this first.** The machine has Intel UHD *and* the RTX 3050, and Windows will
happily run the Unity editor on the Intel chip. The symptom is not an error — it
is a vaguely sluggish editor and a mysterious 15 fps in Play Mode. People lose
days to this before they think to check.

1. Windows Settings → System → Display → **Graphics**
   → add **Unity Hub** and, after step 2, the editor's `Unity.exe`
   → set both to **High performance**.
2. NVIDIA Control Panel → Manage 3D Settings → **Program Settings** → same two
   executables → **High-performance NVIDIA processor**.

Verify later, once the editor is open: **Help → About Unity** must name the
**RTX 3050**. Do not proceed past step 4 until it does.

While you are here: plug the laptop in, Windows power mode → **Best
performance**, MSI Center → **Cooler Boost** on. The GF76 throttles hard under
sustained load, and a throttled i7 turns a 2-minute build into an 8-minute one.

## 2. Install Unity

1. Install **Unity Hub**.
2. Hub → Installs → Install Editor → **Unity 6 LTS (6000.x)**.
3. Modules: **Windows Build Support (IL2CPP)** only.
   Skip Android / iOS / WebGL — several GB each, and unused here.
   Skip the Visual Studio module; VS Code + the **C# Dev Kit** extension is
   lighter and already installed.

Budget ~12 GB. Add the new `Unity.exe` to the two GPU lists from step 1.

## 3. Create the project — note the folder trap

Unity Hub refuses to create a project into a directory that already has files in
it, and this repo already contains `CLAUDE.md`, `Tools/`, and `docs/`. So create
it elsewhere and move the three folders in:

1. Hub → New Project → template **3D (URP)** → name `Call-of-Duty` → location
   `C:\Users\abuye\CoD-tmp` → Create. Wait for the first import to finish, then
   **close Unity**.
2. Move `Assets`, `Packages`, and `ProjectSettings` from
   `C:\Users\abuye\CoD-tmp\Call-of-Duty\` into `C:\Users\abuye\Call-of-Duty\`
   (the repo root — they end up as siblings of `.git`).
3. Delete `C:\Users\abuye\CoD-tmp` entirely, `Library/` and all.

   ```bash
   mv /c/Users/abuye/CoD-tmp/Call-of-Duty/{Assets,Packages,ProjectSettings} /c/Users/abuye/Call-of-Duty/
   rm -rf /c/Users/abuye/CoD-tmp
   ```

4. Open the repo folder from the Hub (Open → pick `C:\Users\abuye\Call-of-Duty`).
   Unity regenerates `Library/` here, which `.gitignore` already excludes.

*(If your Hub version accepts the non-empty repo folder directly, use it and skip
the move. The end state is identical: `Assets/` at the repo root, which is where
the guards look.)*

Two constraints already satisfied, worth knowing why: the path has **no spaces**,
and it is **not inside OneDrive**. OneDrive silently syncing a 20 GB `Library/`
is its own category of disaster.

## 4. Project Settings

### Edit → Project Settings → Editor

- Asset Serialization → Mode: **Force Text** — makes scenes and prefabs diffable.
  Without it, every merge conflict is unresolvable.
- Version Control → Mode: **Visible Meta Files**.
- Enter Play Mode Settings → **Enter Play Mode Options: enabled**, with
  **Reload Domain** and **Reload Scene** both **unchecked**. This takes Play Mode
  entry from seconds to instant, which over a full-time project is worth days.
  The cost is that `static` fields no longer reset between plays — which is why
  `guard-no-mutable-statics.mjs` exists.

### Window → Rendering → Lighting

- **Uncheck Auto Generate.** On by default; it silently rebakes every time a
  light or static object moves, pinning the GPU.

### Edit → Project Settings → Player

- Colour Space: **Linear**.
- Scripting Backend: **Mono** for iteration, **IL2CPP** for release builds.

### Edit → Project Settings → Quality

- Keep two levels: a low one selected for the **editor**, the real one for
  builds. Halve shadow resolution and shadow distance in the editor profile —
  they are the biggest VRAM consumers.

### Texture import defaults

- Max Size **1024** project-wide; **512** for anything not held near the camera;
  **2048** only for weapons and hands, which are what the player actually looks
  at. Compression Normal Quality, DXT/BC.
- Turn **Read/Write Enabled off** on every mesh and texture that does not need
  CPU access — it doubles memory when on.

## 5. Packages

**Window → Package Manager** — add:

- **Input System** — then accept the prompt to set Active Input Handling to
  *Input System Package (New)*. The editor restarts.
- **Cinemachine** — camera shake, recoil impulse, follow.
- **AI Navigation** — the modern NavMesh package, for drone pathing.

Remove if present and unused: **Visual Scripting**, **Timeline**, **Terrain**.
They cost compile time and editor weight for nothing here.

## 6. Unity Smart Merge

`.gitattributes` already routes `.unity`/`.prefab`/`.asset` through
`merge=unityyamlmerge`. Without the driver registered, git **silently falls back
to a plain text merge** — which corrupts Unity YAML. Not yet configured on this
machine; run once, filling in your installed version number:

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/<version>/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
```

## 7. Activate the pre-commit hook

Only now — the hook could not be switched on earlier, because
`guard-meta-files.mjs` fails while there is no `Assets/` folder.

```bash
cd /c/Users/abuye/Call-of-Duty
git config core.hooksPath Tools/hooks
node Tools/check.mjs                    # all six guards must pass now
```

`core.hooksPath` (rather than `.git/hooks/`) means the hook is committed and
survives a fresh clone. It is per-clone config, so re-run that line on any new
machine.

That redirect has a sharp edge, already handled: it makes git ignore
`.git/hooks/` **entirely**, which is where `git lfs install` puts LFS's own
`pre-push`, `post-checkout`, `post-commit`, and `post-merge`. Those four are
mirrored into `Tools/hooks/` and committed, so LFS keeps working. Losing them
would not look like an error — pushes would still succeed, uploading pointer
files while the actual binaries never leave the machine, and the damage would
only appear on the next clone. `guard-lfs-hooks.mjs` now fails the build if any
of them go missing.

## 8. Prove the hook actually blocks — do not skip

A guard that has never been seen to fail is a guard that might not work.

```bash
mkdir -p Library && echo test > Library/dummy.txt
git add -f Library/dummy.txt
git commit -m "should be blocked"       # must FAIL
git reset && rm -rf Library
```

If that commit succeeds, the hook is not wired — go back to step 7.

Then commit the real thing:

```bash
git add Assets Packages ProjectSettings
git commit -m "chore: unity 6 urp project"
git push
```

## 9. Before the first binary — decide the LFS story

GitHub's free tier is **1 GB LFS storage and 1 GB bandwidth per month**. A single
weapon or environment pack blows through that, and hitting the cap blocks pushes
*and* clones. Decide before history depends on it:

- buy GitHub data packs (cheap, least friction), or
- move the remote to **Azure DevOps** — free private repos, no published hard LFS
  cap, and the common choice for solo gamedev for exactly this reason.

Migrating LFS remotes later is possible but tedious.

---

## Done when

- [ ] Help → About Unity shows the **RTX 3050**
- [ ] `Assets/`, `Packages/`, `ProjectSettings/` sit at the repo root
- [ ] `node Tools/check.mjs` → **all five guards pass**
- [ ] A deliberate `Library/dummy.txt` commit is **blocked**
- [ ] Input System, Cinemachine, AI Navigation installed; Play Mode entry is instant

Then the next Claude Code session builds **Phase 2** (folders + `CoD.*` asmdefs)
and **Phase 3** (the grey box: one gun in a grey room that feels good).
