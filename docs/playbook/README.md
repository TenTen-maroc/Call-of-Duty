# Vendored: TenTen game playbook

Copied verbatim from the `tenten-game-playbook` Claude Code skill so this repo is
self-contained on a machine that does not have the skill installed. **Treat these
files as read-only reference.** Project-specific rules that override or extend
them live in [../../CLAUDE.md](../../CLAUDE.md).

| File | What it holds |
| --- | --- |
| [SKILL.md](SKILL.md) | The playbook itself — engine choice, setup order, feel order |
| [conventions.md](conventions.md) | Unity/C# house rules: SO tuning, folder layout, naming, saves |
| [gunfeel.md](gunfeel.md) | Concrete starting numbers — TTK, recoil, spread, ADS, the juice checklist |
| [unity-setup.md](unity-setup.md) | Project settings, VRAM budget, asmdefs, LFS quota, the dual-GPU trap |
| [snippets/WeaponConfig.cs](snippets/WeaponConfig.cs) | Canonical ScriptableObject shape. Reference only — outside `Assets/`, so never compiled. The real one lands in `Assets/_Project/Scripts/Weapons/` under namespace `CoD.Weapons` at Phase 3. |

The five guard scripts are **not** duplicated here — they live where they run, in
[../../Tools/guards/](../../Tools/guards/), each with a header documenting the
disaster it prevents.
