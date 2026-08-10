# Data model sketch v2 — the ScriptableObjects that ARE the game

Everything tunable is an asset. This is the shape to aim for; Claude Code
generates the actual C#. Hand over one phase's section when you reach it —
never all of it up front.

## The two iron rules (read these twice)

**1. Configs are read-only at runtime. Always.**
ScriptableObjects are shared assets. A runtime write to one — a passive
"improving" `reloadTime`, a module caching targets on itself — edits your
*authored game data*. And because this project disables Domain Reload, those
writes **persist between Play sessions in the editor**: play twice, and your
balance numbers have silently drifted. In builds they behave differently again.
The same property that makes SO tuning great (Play Mode edits persist) makes
accidental writes catastrophic.

**2. Runtime state lives in a thin runtime layer, never on assets.**

```
RunState        (plain C# object, one per run)
  money, currentWave, bestRoundSoFar
  ownedWeaponIds[], ownedPassives[]        # by stableId — see Registry
  statSheet          → StatSheet

StatSheet       (plain C# object)
  per stat: flatAdd, multiplier            # composed from all owned passives
  Effective(stat, baseValue) = (baseValue + flatAdd) * multiplier, clamped

WeaponRuntime   (plain C# object, one per carried weapon)
  config          → WeaponConfig (read-only)
  currentAmmo, reserveAmmo, heat, reloadTimer...
```

Everything below is settings. Everything above is state. A MonoBehaviour that
wants "reload time" asks `statSheet.Effective(ReloadSpeed, config.reloadTime)` —
it never reads a mutated config and never writes one.

## Phase map

| Phase | Configs needed |
| --- | --- |
| 3 — grey box | GameConfig, WeaponConfig, one AR asset |
| Rusher | DroneConfig + ContactDetonate, DifficultyConfig (caps only) |
| Waves + shop | WaveConfig, DifficultyConfig (full), ShopConfig, PassiveConfig, Registry |
| Wild arsenal | EffectModules |

## GameConfig  (one asset, global)

```
playerMaxHealth        100
gravity                -20
baseFovVertical        62      # ~95 horizontal — Unity's FOV field is VERTICAL
walkSpeed / sprintSpeed
weaponSlots            2       # carried at once; swap in shop
slowMoTimeScale        0.35
startingWeapon         → WeaponConfig ref
```

## WeaponConfig  (one per weapon)

Full shape in `docs/playbook/snippets/WeaponConfig.cs`. Add:

```
stableId               "wpn_ar_standard"   # saves/registry key, never renamed
effectModules[]        → ordered list of EffectModule refs (usually empty)
```

A list, not a single ref — stacking IS the product: railgun + Pierce + Chain
is one asset with two entries. Order = execution order.

## EffectModule  (the "without limits" engine)

Modules are **stateless rules**. All per-shot state travels in the context;
a module asset holds only numbers. (A module that stores runtime data on
itself is shared across every weapon using it — rule 1 violated.)

```
abstract EffectModule : ScriptableObject
    abstract void Resolve(in HitContext ctx, ref FollowUpBuffer followUps)

HitContext   (readonly struct, built per impact)
    shooter, weaponConfig, statSheet
    point, normal, hitCollider, damageDealt
    depth                      # 0 = primary hit
    rngStream                  # seeded per shot → deterministic chains
    alreadyHit                 # pre-sized buffer, prevents double-dipping

FollowUpBuffer  (pooled)
    damage events / extra casts the weapon executes AFTER this resolution
```

**Recursion rule: follow-ups resolve at depth+1, and modules only run at
depth 0 unless the module sets `maxDepth > 0`.** Without this, Explosive →
Chain → Explosive is an infinite loop. Depth is data, so even "chains that
chain" is a deliberate number, not an accident.

**Pierce is special — it changes the ray, not the aftermath.** The weapon asks
its modules for a hit budget before casting:
`RaycastNonAlloc(buffer, size = 1 + pierce.maxTargets)`. Ricochet and Chain
work through follow-ups; Pierce works through resolution.

```
Explosive : radius, falloffCurve, damage, vfxId
Pierce    : maxTargets, damageFalloffPerTarget
Ricochet  : maxBounces, bounceRange, damagePerBounce      # follow-up rays
Chain     : maxJumps, jumpRange, damagePerJump, maxDepth  # OverlapSphereNonAlloc,
                                                          # excludes ctx.alreadyHit
```

## DroneConfig + AttackModule  (drones use the SAME modular pattern)

No behaviour enum, no dead fields. A drone = base stats + movement numbers +
an AttackModule asset. One `DroneController` reads both; drone #4 is data.

```
DroneConfig : ScriptableObject
    stableId, displayName
    maxHealth, contactDamage?  (only if its attack is contact-based)
    moveSpeed, acceleration, turnSpeed        # NavMesh agent
    preferredRange             # 0 = closes to melee; >0 = holds distance (kiting
    stopDistance               #   Shooter and charging Rusher differ by DATA)
    scoreValue, moneyReward
    prefab                     → pooled drone prefab
    attack                     → AttackModule ref
    weakpointMultiplier        2.0            # headshots should pay on drones too
    deathVfx / deathSfx

abstract AttackModule : ScriptableObject     # stateless, like EffectModule

ContactDetonate : triggerRadius, fuseSeconds, damage, blastRadius, vfx   # Rusher
RangedBurst     : projectilePrefab, burstCount, burstPause, cooldown,    # Shooter
                  reactionDelay 0.4, firstShotDeliberateMiss true,
                  accuracy 0.7               # the gunfeel enemy-feel numbers,
                                             # now data instead of folklore
HeavySlam       : windupSeconds, telegraphVfx, slamRadius, damage        # Tank
```

## WaveConfig  (Wave_01 … Wave_10 hand-authored, then formula)

```
waveNumber
entries[]              # { DroneConfig, count, spawnOverSeconds }
                       #   count drips in evenly across spawnOverSeconds —
                       #   no ambiguous per-entry delays
durationTarget         ~45s
maxAliveOverride?      # optional per-wave cap; else DifficultyConfig rules
moneyBonusOnClear
```

## DifficultyConfig  (one asset — the caps and the endless ramp)

```
# Hard caps — these protect the 4 GB GPU and the game's feel. Non-negotiable.
maxAliveDrones             40     # spawner throttles; queue waits for deaths
maxSimultaneousAttackers   3      # attack tokens: only 3 drones may actively
                                  # attack at once; the rest reposition.
                                  # This is why 20 enemies feels fair.

# Spawning
minSpawnDistanceFromPlayer 12
preferOffscreenSpawns      true   # spawn points = tagged transforms in scene

# Endless ramp (used past the last hand-authored wave)
countMultiplierByWave      AnimationCurve
hpMultiplierByWave         AnimationCurve
speedMultiplierByWave      AnimationCurve (gentle — speed inflation feels cheap)
mixWeightsByWave           per-DroneConfig curves (Tanks rise late)
```

## ShopConfig  (one asset — pool AND economy; the shop can't run without both)

```
startingMoney          300
offersPerBreak         4
rerollBaseCost         50
rerollCostGrowth       ×1.5 per reroll, resets each break
priceScalingByWave     AnimationCurve     # same item costs more at wave 12

pool[]                 # { ShopItemConfig, weight, minWave, maxOwned }
                       #   weighted draw, gated by wave, capped per run
```

## ShopItemConfig  (one per purchasable)

```
stableId, displayName, description, icon, cost
kind                   enum { Weapon, EffectModule, Passive }
weapon? / effect? / passive?    # exactly ONE set; OnValidate enforces
                                # kind matches the populated ref
```

## PassiveConfig  (one per passive)

```
stableId, displayName
modifiers[]            # { stat enum, kind: FlatAdd | Mult, value }
stackable, maxStacks

# Pipeline (fixed, documented once, never re-litigated):
#   effective = (base + Σ flatAdds) × Π mults, clamped per stat
# Applied by rebuilding the StatSheet from owned passives on every purchase.
# NEVER by editing a config asset. (Iron rule 1.)
Stat enum v1: MaxHealth, MoveSpeed, ReloadSpeed, AdsSpeed, DamageMult,
              SlowMoCharges, MoneyGainMult
```

## ContentRegistry  (one asset)

```
allContent[]           # every WeaponConfig / EffectModule / PassiveConfig
```

Saves and the shop reference content by `stableId`, never by asset name —
renames stop breaking saves. Registry's OnValidate fails on duplicate or empty
ids. (Cheap to add a guard for later if it ever bites.)

## Save shape  (JSON, versioned — see playbook save rules in docs/playbook/conventions.md)

```
{ "schemaVersion": 1,
  "bestRound": 0,
  "totalKills": 0, "totalRuns": 0,
  "sandboxUnlocked": true,
  "settings": { "sensitivity": ..., "volume": ... } }
```

Permadeath means runs are NOT saved — only records and settings. That's a
feature: the save system stays one page of code.

## Validation (every config implements OnValidate)

- WeaponConfig: TTK outside 200–400 ms → Inspector warning (already in snippet)
- WaveConfig: total entry count > maxAliveDrones × 3 → warn (queue will drag)
- DroneConfig: preferredRange > 0 but attack is ContactDetonate → warn
- ShopItemConfig: kind/ref mismatch → error
- Registry: duplicate stableId → error

## Folder layout

```
Assets/_Project/Data/
  Game/        GameConfig.asset, Difficulty.asset, Shop.asset, Registry.asset
  Weapons/     AR_Standard.asset, Railgun.asset, ...
  Effects/     Explosive.asset, Pierce.asset, Ricochet.asset, Chain.asset
  Drones/      Drone_Rusher.asset, Drone_Shooter.asset, Drone_Tank.asset
  Attacks/     ContactDetonate_Std.asset, RangedBurst_Std.asset, HeavySlam_Std.asset
  Waves/       Wave_01.asset ... Wave_10.asset
  Shop/        Shop_Railgun.asset, Shop_Explosive.asset, Shop_MaxHP.asset, ...
  Passives/    Passive_MaxHP.asset, Passive_ReloadSpeed.asset, ...
```

Every asset is text, diffable, editable in the Inspector during Play Mode.
Balancing the game is editing numbers in a folder. The runtime layer exists so
that stays true — the folder is the game's truth, and nothing at runtime is
allowed to rewrite the truth.
