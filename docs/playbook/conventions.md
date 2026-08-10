# House conventions — Unity / C#

Apply to every file generated. These are the game-side equivalents of the web
playbook's non-negotiables.

## The one rule that matters most: tuning lives in data

**Every number a human will tune belongs in a ScriptableObject asset. Never a
literal in a script. Never a public field on a MonoBehaviour.**

In the web playbook this rule is "money is integer centimes, dates go through
one helper". Here it is tuning data, and the reason is the same: a value that
exists in one place can be changed safely; a value scattered across twelve files
cannot.

The concrete payoff in a shooter: gun feel gets tuned *hundreds* of times. If
recoil lives on the prefab's MonoBehaviour, tuning means hunting through the
scene hierarchy, and changes made during Play Mode vanish on stop. If it lives
in a `WeaponConfig` asset, tuning is one Inspector panel, **changes made during
Play Mode persist**, and every weapon is diffable in git as text.

```
Assets/_Project/Data/
  Weapons/     AR_Standard.asset, SMG_Fast.asset, Shotgun.asset
  Enemies/     Grunt.asset, Rusher.asset, Heavy.asset
  Waves/       Wave_01.asset ... Wave_10.asset
  Game/        GameConfig.asset      ← player HP, gravity, base FOV, global multipliers
```

MonoBehaviours hold a reference to a config and read from it. They hold **state**
(current ammo, current health), never **settings**.

See `assets/snippets/WeaponConfig.cs` for the canonical shape.

## Folder layout

```
Assets/
  _Project/              ← all first-party work, leading underscore sorts it to top
    Art/                 (Materials, Models, Textures, VFX)
    Audio/
    Data/                ← the ScriptableObject assets above
    Prefabs/
    Scenes/
    Scripts/
      Core/              (GameConfig, GameLog, ObjectPool, SaveSystem, events)
      Player/            (movement, camera, input)
      Weapons/
      Enemies/
      UI/
    Settings/            (URP assets, input actions)
  ThirdParty/            ← bought packs, moved here, otherwise untouched
```

**Never edit files inside a bought pack.** If a change is needed, subclass it or
copy the specific file into `_Project/` and edit the copy. Editing in place makes
the pack un-updatable and un-deletable, and a year later nobody remembers which
files were modified.

## C# rules

- **`#nullable enable` is the first line of every first-party `.cs` file.**
  Unity asmdefs have no nullable switch, and a project-wide `csc.rsp` would
  force nullable onto ThirdParty code and break it — the per-file directive is
  the only mechanism that scopes correctly. Without it, `GameObject?`
  annotations produce CS8632 warnings.
- First-party code stays at **zero console warnings** — the console is the
  error list. (Unity offers no per-asmdef warnings-as-errors, so this is a
  quality gate, not a compiler flag.)
- **No `GameObject.Find`, `FindObjectOfType`, `GetComponent`, `Camera.main`, or
  `AddComponent` inside `Update` / `FixedUpdate` / `LateUpdate`.** Cache every
  reference in `Awake` or serialize it. This is enforced by
  `guard-no-find-in-update.mjs` and it is the single most common Unity
  performance defect.
- No mutable `static` state — enforced by `guard-no-mutable-statics.mjs`.
  Domain Reload is disabled for iteration speed (see `unity-setup.md`), so
  statics survive between Play Mode sessions and produce bugs that only appear
  on the second play. If a static is genuinely needed, reset it in a
  `[RuntimeInitializeOnLoadMethod]` and mark it `// guard-ok: <reason>`.
- `[SerializeField] private` over `public` for Inspector fields. Public fields
  are API; most of these are not.
- No allocation in per-frame code: no LINQ, no `new` for collections, no string
  concatenation, no `foreach` through an interface type (`IEnumerable<T>` boxes
  the struct enumerator; enumerating the concrete `List<T>`/`Dictionary<K,V>`
  does not). Use non-allocating physics
  overloads (`Physics.RaycastNonAlloc`, `SphereCastNonAlloc`) with a
  pre-sized buffer.
- Physics work goes in `FixedUpdate`. Input reading and camera work go in
  `Update` / `LateUpdate`. Camera follow is always `LateUpdate` — anything else
  produces jitter.
- **Everything that spawns goes through the object pool** — bullets, casings,
  impact VFX, damage numbers, enemies. Registered in the pool in the same commit
  that creates the prefab. `Instantiate`/`Destroy` in a shooter loop is the GC
  hitch factory.
- All logging through a `GameLog` wrapper stripped in release:
  `[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]`. Raw
  `Debug.Log` in a hot path costs real frame time even when nothing reads it.

## Naming

- Scripts and classes `PascalCase`, one public type per file, filename matches
  the type exactly (Unity requires this for MonoBehaviours).
- Private fields `_camelCase`. Constants `SCREAMING_SNAKE_CASE`.
- ScriptableObject assets `Category_Variant` — `AR_Standard`, `Enemy_Rusher`.
- Prefabs `PascalCase`, layers and tags `PascalCase`.
- Scenes `NN_Name` — `00_Boot`, `01_MainMenu`, `10_Arena_Warehouse`. The numeric
  prefix keeps the folder ordered and makes build-index bugs obvious.

## Scenes

- Always a `00_Boot` scene that initialises core systems and loads onward.
  Starting the game from an arbitrary scene must still work in the editor — put
  a "if core systems are missing, spawn them" path in `Core`. Losing the ability
  to hit Play in the scene being worked on costs more time than anything else on
  this list.
- One scene per level. Shared systems live in an additively-loaded persistent
  scene, not duplicated per level.

## Saves

Versioned JSON from day one:

```json
{ "schemaVersion": 3, "player": { ... }, "unlocks": [ ... ] }
```

Write to `Application.persistentDataPath`. Write to a temp file and then move
into place — a crash mid-write otherwise leaves an unparseable save and the run
is gone. Keep one `.bak` of the previous save. Handle an unknown/older
`schemaVersion` explicitly; never assume the file matches the current struct.

This is the game equivalent of the web playbook's migration discipline, and it
fails the same way: silently, and only for people who already have data.

## Debug and cheat console

For a personal offline sandbox this is a **feature, not a debug tool** — build
it early and treat it as first-class: godmode, infinite ammo, spawn any weapon,
spawn N enemies, slow-motion, noclip, skip to wave N, damage multiplier.

It also happens to be the fastest way to test everything else. Bind it to a key,
gate the whole thing behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` if a public
build is ever planned.

## Git hygiene

- Commit `Assets/`, `Packages/`, `ProjectSettings/`. Ignore everything else
  (see `assets/gitignore.template`).
- **Every asset file has a committed `.meta` sibling.** A missing `.meta` breaks
  every reference to that asset for anyone else — including the future self who
  clones the repo onto a new machine. Enforced by `guard-meta-files.mjs`.
- Binaries go through Git LFS from the first commit. Retrofitting LFS requires a
  history rewrite.
- Atomic commits, one subsystem each. A commit that touches a subsystem also
  updates that subsystem's file in `docs/systems/`.
