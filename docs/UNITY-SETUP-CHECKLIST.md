# Unity setup checklist

> Almost all of this is **already done**. It is kept as the record of what was
> configured and why, and because a second machine will need it again.
>
> Machine: MSI Katana GF76, i7-12650H, 32 GB RAM, **RTX 3050 Laptop / 4 GB VRAM**.
> Every setting below is chosen for that.

## What is left for you — one thing

**Sign in to Unity Hub with a Unity ID** (Personal is free). Unity refuses to
start without an activated licence — `No valid Unity Editor license found`,
exit code 198 — and no script can clear that. Nothing else is blocked on you.

Then, in the editor:

1. Open the project: Unity Hub → Open → `C:\Users\abuye\Call-of-Duty`
2. Menu: **CoD → Build Grey Box**
3. Open `Assets/_Project/Scenes/10_GreyBox.unity` and press **Play**

Step 2 generates every prefab, both scenes, the materials and the tuning assets,
and registers the scenes in Build Settings. It is idempotent — safe to re-run.

Prefer the command line? Same thing, headless:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath . \
  -executeMethod CoD.EditorTools.GreyBoxBuilder.BuildHeadless
```

---

## Done already

### 1. Dual-GPU fix ✅

The machine has Intel UHD *and* the RTX 3050, and Windows will happily run Unity
on the Intel chip. The symptom is not an error — it is a sluggish editor and a
mysterious 15 fps in Play Mode, and people lose days to it.

`HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences` now has `GpuPreference=2;`
(High performance) for Unity Hub and the editor. That registry key is exactly
what the Settings → Display → Graphics UI writes.

**Verify once the editor opens: Help → About Unity must name the RTX 3050.**
If it does not, add `Unity.exe` in NVIDIA Control Panel → Manage 3D Settings →
Program Settings → High-performance NVIDIA processor. That side has no
scriptable API.

While you are here: plug in, Windows power mode **Best performance**, MSI Center
→ **Cooler Boost** on. The GF76 throttles hard under sustained load, and a
throttled i7 turns a 2-minute build into an 8-minute one.

### 2. Unity 6000.0.81f1 + Windows IL2CPP ✅

Installed at `C:\Program Files\Unity\Hub\Editor\6000.0.81f1`, so the Hub finds it
automatically. Android/iOS/WebGL were skipped — several GB each, unused here.

Two LTS lines were available (6000.0.x and 6000.3.x). 6000.0.81f1 was chosen: it
is what "Unity 6" means to most asset packs and tutorials, which matters more on
a first Unity project than 6.3's longer support runway. It was patched four days
before install, so it is alive.

### 3. Project created from the URP template ✅

Unity 6 renamed the templates, so this was identified rather than guessed: the
file is `com.unity.template.3d-cross-platform-17.0.12.tgz`, and its `package.json`
says `com.unity.template.urp-blank`, displayName **3D URP**. That tarball is what
the Hub's "3D URP" button extracts, so unpacking it directly gives a project
identical to the one the GUI would have made.

`Assets/`, `Packages/` and `ProjectSettings/` sit at the repo root — the guards
resolve `Assets/` from there, so it is not really optional.

### 4. Project Settings ✅

Set by editing `ProjectSettings/*.asset` directly. Nothing was clicked.

| Setting | Value | Why |
| --- | --- | --- |
| Asset Serialization | Force Text | Scenes and prefabs stay diffable; without it every merge conflict is unresolvable |
| Version Control | Visible Meta Files | Non-negotiable for git |
| Enter Play Mode | enabled, **Reload Domain and Reload Scene both off** | Instant Play Mode entry. The cost is that statics survive between plays — which is why `guard-no-mutable-statics` exists |
| Colour space | Linear | URP default, verified rather than assumed |
| Line endings | Unix (LF) | Matches `.gitattributes` |
| Identity | TenTen / Call of Duty | — |

**Not applicable on Unity 6: "uncheck Auto Generate lighting".** The playbook
calls for it because auto-generate silently rebakes whenever a light or static
object moves and pins the GPU. Unity 6 removed the mode outright —
`Lightmapping.giWorkflowMode` and `LightingSettings.autoGenerate` are both
obsolete now. Baking is on-demand by default; there is nothing to turn off.

### 5. Packages ✅

Kept: Input System, AI Navigation (for drone pathing later), URP, uGUI, Test
Framework, Visual Studio integration.

Removed: **Timeline**, **Visual Scripting**, **Collab (Version Control)**,
**Rider integration** — compile time and editor weight for nothing here.

**Cinemachine is deliberately absent.** See CLAUDE.md: its impulse listener needs
a `CinemachineCamera` driven by a Brain, which fights `PlayerLook` for FOV and
rotation. The grey box uses a 40-line `CameraShake` instead, and the swap is one
file when the camera work gets richer.

### 6. Guards, hook, and type-checking ✅

```bash
node Tools/check.mjs        # six guards
node Tools/typecheck.mjs    # compiles every assembly, no editor needed
```

`core.hooksPath` is set to `Tools/hooks`, so both run on every commit and the
hook survives a fresh clone. It was verified by deliberately staging
`Library/dummy.txt` and confirming the commit was refused.

That redirect has a sharp edge, already handled: pointing `core.hooksPath`
elsewhere makes git ignore `.git/hooks/` **entirely**, which is where
`git lfs install` puts LFS's four hooks. They are mirrored into `Tools/hooks/`
and `guard-lfs-hooks.mjs` fails the build if any go missing. Without them a push
uploads pointer files while the actual binaries never leave the machine, and the
damage only shows up on the next clone.

**On a second machine**, re-run these two — both are per-clone git config:

```bash
git config core.hooksPath Tools/hooks
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p %O %B %A %A'
```

The second one matters: `.gitattributes` routes `.unity`/`.prefab`/`.asset`
through `merge=unityyamlmerge`, and without the driver registered git **silently**
falls back to a plain text merge, which corrupts Unity YAML.

---

## Still to decide — before the first binary asset

GitHub's free tier is **1 GB LFS storage and 1 GB bandwidth per month**. A single
weapon or environment pack blows through that, and hitting the cap blocks pushes
*and* clones. Decide before history depends on it:

- buy GitHub data packs (cheap, least friction), or
- move the remote to **Azure DevOps** — free private repos, no published hard LFS
  cap, the common choice for solo gamedev for exactly this reason.

Migrating LFS remotes later is possible but tedious.

## Texture discipline (per asset, ongoing)

4 GB of VRAM is spent on textures, not geometry.

- Max Size **1024** by default, **512** for anything not near the camera,
  **2048** only for weapons and hands — what the player actually looks at.
- **Read/Write Enabled off** on every mesh and texture that does not need CPU
  access; it doubles memory when on.
- Audio: Vorbis, streaming for anything over ~5 s, mono for short SFX.
- Check Window → Analysis → **Profiler → Memory** before adding the *next* asset
  pack, not after ten of them.
