# Unity setup — tuned for a 4 GB-VRAM laptop

Target machine: MSI Katana GF76, i7-12650H (10c), 32 GB RAM, RTX 3050 Laptop
**4 GB VRAM**, ~170 GB free. The CPU and RAM are generous. VRAM and sustained
thermals are the constraints. Every setting below is chosen for that.

## The dual-GPU trap — fix this first

The machine reports "Multiple GPUs installed": Intel UHD 128 MB + RTX 3050.
Windows will silently run the Unity editor on the **Intel** chip, and the
symptom is not an error — it is a vaguely slow editor and mysterious 15 fps in
Play Mode. Developers lose days to this.

Fix, in both places:

1. Settings → System → Display → Graphics → add **Unity Hub** and **Unity
   Editor** (the `Unity.exe` inside the version folder, not just the Hub) → set
   both to **High performance**.
2. NVIDIA Control Panel → Manage 3D Settings → Program Settings → same two
   executables → **High-performance NVIDIA processor**.

Verify: Unity → Help → About Unity shows the RTX 3050 as the active device.
Do not proceed until it does.

## Thermals

The GF76 throttles hard under sustained load, and game development *is*
sustained load. A throttled i7 turns a 2-minute build into an 8-minute build,
and long builds are what actually kill momentum on a full-time push.

- Always plugged in. Windows power mode: **Best performance**.
- MSI Center → **Cooler Boost** on during work sessions.
- Cooling pad, and never work with the laptop on a soft surface.
- Expect fan noise. That is the machine working correctly.

## Version and pipeline

- **Unity 6 LTS (6000.x)**. Install via Unity Hub.
- Template: **3D (URP)**. Never HDRP on 4 GB VRAM — it will not fit, and the
  editor alone will thrash.
- Modules to install: **Windows Build Support (IL2CPP)**. Skip Android/iOS/WebGL
  unless actually needed; each is several GB of the disk budget.
- Skip Visual Studio if it is already installed. **VS Code + the C# Dev Kit**
  extension is lighter and matches existing muscle memory.

## Project settings that pay for themselves immediately

**Edit → Project Settings → Editor**

- **Asset Serialization → Force Text.** Makes scenes and prefabs diffable.
  Without it, every merge conflict is unresolvable.
- **Version Control → Visible Meta Files.** Non-negotiable for git.
- **Enter Play Mode Settings → enabled, with Reload Domain and Reload Scene
  both unchecked.** This cuts play-mode entry from several seconds to near
  instant, and over a full-time project it is worth days. The cost: `static`
  fields no longer reset between plays. Follow the rule in
  `conventions.md` — no mutable static state, or reset it in a
  `[RuntimeInitializeOnLoadMethod]`.

**Window → Rendering → Lighting**

- **Uncheck Auto Generate.** On by default, and it silently rebakes lighting
  every time a light or static object moves, pinning the GPU. Bake manually when
  a level is actually ready.

**Edit → Project Settings → Quality**

- Keep two levels: a low one selected in the **editor**, and the real one for
  builds. Shadow resolution and distance are the biggest VRAM consumers; halve
  both in the editor profile.

**Edit → Project Settings → Player**

- Colour space: **Linear**.
- Scripting backend: **IL2CPP** for release builds, Mono for fast iteration
  builds.

## Import settings — where 4 GB of VRAM is won or lost

Bought asset packs ship at 2048 or 4096 textures. That is what will exhaust the
VRAM, not the geometry.

- Set a project-wide texture **Max Size of 1024**, and 512 for anything not held
  close to the camera. Weapons and hands can stay at 2048 — they are what the
  player actually looks at.
- Compression: **Normal Quality**, format **DXT/BC** (default on Windows).
- Disable **Read/Write Enabled** on every mesh and texture that does not need
  CPU access. It doubles memory when on.
- Audio: **Vorbis**, and **Streaming** load type for anything over ~5 seconds
  (music, ambience). Short SFX: **Decompress On Load**, mono.
- Mesh Compression: Medium on environment props, Off on weapons.

Do a VRAM sanity check with Window → Analysis → **Profiler → Memory** before
adding each new asset pack, not after ten of them.

## Compile times — assembly definitions

Without `.asmdef` files, every script change recompiles the entire project,
including bought packs. With them, a weapon tweak recompiles only the weapons
assembly. On a full-time project this is the difference between a 1-second and a
20-second feedback loop.

Create one per subsystem from day one:

```
Assets/_Project/Scripts/Core/TenTen.Core.asmdef
Assets/_Project/Scripts/Player/TenTen.Player.asmdef
Assets/_Project/Scripts/Weapons/TenTen.Weapons.asmdef
Assets/_Project/Scripts/Enemies/TenTen.Enemies.asmdef
Assets/_Project/Scripts/UI/TenTen.UI.asmdef
```

Dependencies point one direction only: Player/Weapons/Enemies/UI → Core. Never
Core → anything. A dependency cycle is a compile error, which is exactly the
point — it forces the architecture to stay clean.

## Disk budget

~170 GB free is workable but not comfortable. Rough costs:

| Item | Size |
| --- | --- |
| Unity 6 editor + Windows IL2CPP module | ~12 GB |
| A working FPS project's `Library/` | 5–20 GB, grows |
| A mid-size asset pack | 1–5 GB each |
| Build output per configuration | 2–8 GB |

Two actions worth taking early: put the project on a path with no spaces and no
OneDrive sync (OneDrive silently syncing `Library/` is its own category of
disaster), and consider the GF76's second M.2 slot — a 1 TB NVMe is cheap and
removes the constraint permanently.

## Git on this machine (Windows) — and the backup story

- The setup blocks in `assets/guards/README.md` are POSIX shell. Run them in
  **Git Bash** (ships with Git for Windows), not cmd or PowerShell.
- Wire Unity's Smart Merge once per machine, or the `merge=unityyamlmerge`
  lines in `.gitattributes` silently fall back to ordinary text merge:

  ```
  git config --global merge.unityyamlmerge.name "Unity SmartMerge"
  git config --global merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/<version>/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
  ```

- **Push to a remote from day one. One laptop is zero backups** — a dropped
  Katana or a dead SSD is the whole project. This is the game repo's
  equivalent of the web playbook's D1 backup rule.
- **Check the LFS quota before history depends on it.** GitHub's free tier is
  **1 GB LFS storage and 1 GB bandwidth per month** — a single weapon or
  environment pack exceeds that, and hitting the cap blocks pushes and clones.
  Options, in order of least friction: buy GitHub data packs (cheap), or host
  the repo on Azure DevOps (free private repos, no published hard LFS cap and
  the common choice for solo gamedev for exactly this reason). Decide before
  the first binary commit; migrating LFS remotes later is possible but tedious.

## Packages to add, and to remove

Add via Package Manager:

- **Input System** (and switch Active Input Handling to *Input System Package
  (New)*)
- **Cinemachine** — camera shake, recoil, and impulse for free
- **AI Navigation** — the modern NavMesh package for enemies

Remove from a fresh project if present and unused: Visual Scripting, Timeline,
Terrain tools. They add compile time and editor weight for nothing.
