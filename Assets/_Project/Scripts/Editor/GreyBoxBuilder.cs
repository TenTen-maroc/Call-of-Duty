#nullable enable
using System.Collections.Generic;
using CoD.Core;
using CoD.Enemies;
using CoD.Player;
using CoD.UI;
using CoD.Waves;
using CoD.Weapons;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoD.EditorTools
{
    /// <summary>
    /// Builds the entire grey-box milestone: tuning assets, pooled prefabs, and
    /// both scenes. Run it from the CoD menu, or headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.GreyBoxBuilder.BuildHeadless
    ///
    /// Why a builder instead of committed scene files: a .unity file is opaque
    /// YAML full of generated ids, so a scene assembled by hand cannot be
    /// reviewed, re-created, or explained. This can — the grey box is defined by
    /// readable C#, and rebuilding it after a mistake costs one menu click.
    ///
    /// Idempotent: existing assets are reused rather than duplicated, so running
    /// it twice is safe and never orphans references.
    /// </summary>
    public static class GreyBoxBuilder
    {
        private const string DataGame = "Assets/_Project/Data/Game";
        private const string DataWeapons = "Assets/_Project/Data/Weapons";
        private const string DataDrones = "Assets/_Project/Data/Drones";
        private const string DataAttacks = "Assets/_Project/Data/Attacks";
        private const string DataWaves = "Assets/_Project/Data/Waves";
        private const string DataShop = "Assets/_Project/Data/Shop";
        private const string DataPassives = "Assets/_Project/Data/Passives";
        private const string DataEffects = "Assets/_Project/Data/Effects";
        private const string Materials = "Assets/_Project/Art/Materials";
        private const string Prefabs = "Assets/_Project/Prefabs";
        private const string Scenes = "Assets/_Project/Scenes";
        private const string Audio = "Assets/_Project/Audio";

        private const string GreyBoxScenePath = Scenes + "/10_GreyBox.unity";
        private const string BootScenePath = Scenes + "/00_Boot.unity";
        private const string MainMenuScenePath = Scenes + "/20_MainMenu.unity";
        private const string NavMeshPath = Scenes + "/NavMesh_GreyBox.asset";

        [MenuItem("CoD/Build Grey Box", false, 0)]
        public static void Build()
        {
            EnsureFolders();

            GameConfig game = LoadOrCreate<GameConfig>(DataGame + "/GameConfig.asset", ConfigureGame);
            SettingsConfig settings = LoadOrCreate<SettingsConfig>(DataGame + "/Settings.asset", ConfigureSettings);
            HealthConfig targetHealth = LoadOrCreate<HealthConfig>(DataGame + "/Health_Target.asset", h =>
            {
                h.maxHealth = 100f;
            });
            ImpactConfig impact = LoadOrCreate<ImpactConfig>(DataGame + "/Impact_Default.asset", _ => { });
            VolumeProfile postFx = LoadOrCreateVolumeProfile(DataGame + "/PostFx_Arena.asset");
            WeaponConfig rifle = LoadOrCreate<WeaponConfig>(DataWeapons + "/AR_Standard.asset", ConfigureRifle);
            WeaponConfig smg = LoadOrCreate<WeaponConfig>(DataWeapons + "/SMG_Rapid.asset", ConfigureSmg);
            PlayerLoadoutConfig loadout = LoadOrCreate<PlayerLoadoutConfig>(DataWeapons + "/Loadout_Default.asset", l =>
            {
                l.startingWeapon = rifle;
                l.weaponSlots = 2;
            });

            // Grey/red tactical palette. The first pass was washed out: a near-white
            // floor under a bright directional light left nothing to read the
            // crosshair or the muzzle flash against.
            Material grey = LoadOrCreateMaterial(Materials + "/GreyBox_Floor.mat", new Color(0.17f, 0.18f, 0.20f));
            Material wall = LoadOrCreateMaterial(Materials + "/GreyBox_Wall.mat", new Color(0.28f, 0.29f, 0.32f));
            Material targetMat = LoadOrCreateMaterial(Materials + "/GreyBox_Target.mat", new Color(0.62f, 0.13f, 0.11f));
            Material hot = LoadOrCreateMaterial(Materials + "/Fx_Hot.mat", new Color(1f, 0.82f, 0.45f));
            Material gunmetal = LoadOrCreateMaterial(Materials + "/Weapon_Body.mat", new Color(0.10f, 0.105f, 0.115f));
            Material gunAccent = LoadOrCreateMaterial(Materials + "/Weapon_Accent.mat", new Color(0.055f, 0.06f, 0.065f));

            // Edge trim is COOL on purpose. Every threat in this game is read by
            // the colour of its core — Rusher red, Shooter amber, Tank crimson —
            // so nothing in the architecture is allowed to be warm and bright, or
            // the player learns to check a wall for danger. Cold light marks
            // places; warm light means something is trying to kill you.
            Material trim = LoadOrCreateEmissiveMaterial(Materials + "/Trim_Emissive.mat",
                new Color(0.30f, 0.62f, 0.92f), 1.2f);

            // Surfaces, re-asserted on every build. LoadOrCreateMaterial returns
            // an existing material untouched, which is right for values a human
            // tuned — but these are shipped defaults being introduced, so they are
            // applied the same way SetRef re-links a reference.
            ApplySurface(grey, smoothness: 0.28f, metallic: 0.0f);
            ApplySurface(wall, smoothness: 0.18f, metallic: 0.0f);
            ApplySurface(targetMat, smoothness: 0.35f, metallic: 0.1f);
            ApplySurface(gunmetal, smoothness: 0.62f, metallic: 0.85f);
            ApplySurface(gunAccent, smoothness: 0.45f, metallic: 0.70f);

            // Drone palette: a dark hull so the glowing core is the only thing the
            // eye tracks, and the core is what the telegraph tints.
            Material droneHull = LoadOrCreateMaterial(Materials + "/Drone_Hull.mat", new Color(0.13f, 0.14f, 0.17f));
            Material droneCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core.mat",
                new Color(0.75f, 0.12f, 0.10f), 1.6f);
            // Metallic and fairly smooth: a hull that catches a highlight reads as
            // a machine, and it is what makes the dark body legible at all against
            // a dark floor once the glowing core stops being the only lit pixel.
            ApplySurface(droneHull, smoothness: 0.55f, metallic: 0.75f);

            GameObject decal = BuildDecalPrefab(hot);
            GameObject sparks = BuildSparksPrefab();
            GameObject flash = BuildMuzzleFlashPrefab(hot);
            GameObject casing = BuildCasingPrefab(hot);
            GameObject dummy = BuildDummyTargetPrefab(targetMat, targetHealth);

            Material shooterCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core_Shooter.mat",
                new Color(0.95f, 0.55f, 0.10f), 1.8f);
            Material tankCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core_Tank.mat",
                new Color(0.85f, 0.06f, 0.22f), 1.4f);

            GameObject explosion = BuildExplosionPrefab(hot);
            GameObject droneDeath = BuildDroneDeathPrefab(hot);
            GameObject slamVfx = BuildSlamPrefab(hot);
            GameObject projectile = BuildDroneProjectilePrefab(shooterCore);

            GameObject rusherPrefab = BuildDronePrefab("Drone_Rusher", DroneShape.Rusher, droneHull, droneCore);
            GameObject shooterPrefab = BuildDronePrefab("Drone_Shooter", DroneShape.Shooter, droneHull, shooterCore);
            GameObject tankPrefab = BuildDronePrefab("Drone_Tank", DroneShape.Tank, droneHull, tankCore);

            DifficultyConfig difficulty = LoadOrCreate<DifficultyConfig>(DataGame + "/Difficulty.asset", ConfigureDifficulty);

            ContactDetonate detonate = LoadOrCreate<ContactDetonate>(
                DataAttacks + "/ContactDetonate_Std.asset", ConfigureContactDetonate);
            SetRef(detonate, "explosionVfx", explosion);
            SetRef(detonate, "alertClip", LoadClip("Drone_Alert"));
            EditorUtility.SetDirty(detonate);

            RangedBurst rangedBurst = LoadOrCreate<RangedBurst>(
                DataAttacks + "/RangedBurst_Std.asset", ConfigureRangedBurst);
            SetRef(rangedBurst, "projectilePrefab", projectile);
            SetRef(rangedBurst, "fireClip", LoadClip("Drone_Shot"));
            EditorUtility.SetDirty(rangedBurst);

            HeavySlam heavySlam = LoadOrCreate<HeavySlam>(
                DataAttacks + "/HeavySlam_Std.asset", ConfigureHeavySlam);
            SetRef(heavySlam, "slamVfx", slamVfx);
            SetRef(heavySlam, "windupClip", LoadClip("Slam_Windup"));
            EditorUtility.SetDirty(heavySlam);

            DroneConfig rusher = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Rusher.asset", ConfigureRusher);
            SetRef(rusher, "prefab", rusherPrefab);
            SetRef(rusher, "attack", detonate);
            SetRef(rusher, "deathVfx", droneDeath);
            EditorUtility.SetDirty(rusher);

            DroneConfig shooter = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Shooter.asset", ConfigureShooter);
            SetRef(shooter, "prefab", shooterPrefab);
            SetRef(shooter, "attack", rangedBurst);
            SetRef(shooter, "deathVfx", droneDeath);
            EditorUtility.SetDirty(shooter);

            DroneConfig tank = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Tank.asset", ConfigureTank);
            SetRef(tank, "prefab", tankPrefab);
            SetRef(tank, "attack", heavySlam);
            SetRef(tank, "deathVfx", droneDeath);
            EditorUtility.SetDirty(tank);

            // Prewarm counts follow the alive cap, not the demo: rushers dominate
            // every wave, shooters are common, tanks are rare and expensive.
            var drones = new DroneAssets(rusher, difficulty, new[]
            {
                (rusherPrefab, 24), (shooterPrefab, 12), (tankPrefab, 4),
                (explosion, 8), (droneDeath, 8), (slamVfx, 4), (projectile, 40),
            });

            // The run layer: passives, the shop that sells them, and the ten
            // authored waves the endless ramp takes over from.
            PassiveConfig[] passives = BuildPassives();
            EffectModule[] effects = BuildEffectModules(explosion);
            ShopItemConfig[] shopItems = BuildShopItems(passives, effects, smg);
            ShopConfig shopConfig = LoadOrCreate<ShopConfig>(DataGame + "/Shop.asset", ConfigureShop);
            EnsureShopPool(shopConfig, shopItems);
            // Always-offered rows are set on EVERY build rather than through an
            // Ensure: they are not a weighted pool with tuning to preserve, they
            // are a fixed pair of references that must simply be correct.
            SetArrayRef(shopConfig, "alwaysOffered", BuildAlwaysOffered());
            WaveConfig[] waves = BuildWaves(rusher, shooter, tank);
            EnsureEndlessMix(difficulty, rusher, shooter, tank);
            ObjectiveConfig objective = LoadOrCreate<ObjectiveConfig>(
                DataGame + "/Objective_Beacon.asset", _ => { });
            // Green, and the only green in the game. Red, amber and crimson are
            // threats and cool blue is architecture, so the one thing that helps
            // you gets a hue nothing else is allowed to use.
            Material beacon = LoadOrCreateEmissiveMaterial(Materials + "/Objective_Beacon.mat",
                new Color(0.20f, 0.90f, 0.55f), 1.8f);
            var runAssets = new RunAssets(shopConfig, waves, objective, beacon);

            SetRef(impact, "decalPrefab", decal);
            SetRef(impact, "particlePrefab", sparks);
            EditorUtility.SetDirty(impact);

            SetRef(smg, "muzzleFlashPrefab", flash);
            SetRef(smg, "shellCasingPrefab", casing);
            SetRef(smg, "fireCloseLayer", LoadClip("Fire_AR_Close"));
            SetRef(smg, "fireTailLayer", LoadClip("Fire_AR_Tail"));
            SetRef(smg, "dryFireClip", LoadClip("DryFire"));
            SetRef(smg, "reloadClip", LoadClip("Reload_AR"));
            EditorUtility.SetDirty(smg);

            SetRef(rifle, "muzzleFlashPrefab", flash);
            SetRef(rifle, "shellCasingPrefab", casing);
            SetRef(rifle, "fireCloseLayer", LoadClip("Fire_AR_Close"));
            SetRef(rifle, "fireTailLayer", LoadClip("Fire_AR_Tail"));
            SetRef(rifle, "dryFireClip", LoadClip("DryFire"));
            SetRef(rifle, "reloadClip", LoadClip("Reload_AR"));
            EditorUtility.SetDirty(rifle);

            BuildGreyBoxScene(game, settings, loadout, impact, grey, wall, targetMat, gunmetal, gunAccent,
                dummy, decal, sparks, flash, casing, drones, runAssets, postFx, trim);
            BuildMainMenuScene(game, settings, postFx);
            BuildBootScene();
            RegisterScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Never report success without reading the scene back from disk. The
            // first build claimed "done" and produced a scene with every config
            // reference null.
            GreyBoxVerify.VerifyAndRepair();

            Debug.Log("Grey box built. Open " + GreyBoxScenePath + " and press Play.");
        }

        /// <summary>Entry point for -executeMethod. Same work, non-zero exit on failure.</summary>
        public static void BuildHeadless()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Grey box build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Everything the Rusher milestone adds, in one parameter. The scene
        /// builder already takes a dozen references; grouping each milestone's
        /// assets keeps that from becoming a twenty-argument signature nobody can
        /// read. A readonly struct with a constructor, not an object initializer —
        /// under `#nullable enable` an initializer would leave the fields
        /// provably-unassigned and the build gate fails on warnings.
        /// </summary>
        private readonly struct DroneAssets
        {
            /// <summary>What the spawner and the sandbox console reach for. The Rusher.</summary>
            public readonly DroneConfig Default;
            public readonly DifficultyConfig Difficulty;
            /// <summary>Everything the drones spawn, with prewarm counts, ready for the pool.</summary>
            public readonly (GameObject prefab, int count)[] Pooled;

            public DroneAssets(DroneConfig defaultDrone, DifficultyConfig difficulty,
                (GameObject prefab, int count)[] pooled)
            {
                Default = defaultDrone;
                Difficulty = difficulty;
                Pooled = pooled;
            }
        }

        /// <summary>The wave/shop milestone's assets, grouped for the same reason DroneAssets is.</summary>
        private readonly struct RunAssets
        {
            public readonly ShopConfig Shop;
            public readonly WaveConfig[] Waves;
            public readonly ObjectiveConfig Objective;
            public readonly Material BeaconMaterial;

            public RunAssets(ShopConfig shop, WaveConfig[] waves, ObjectiveConfig objective, Material beacon)
            {
                Shop = shop;
                Waves = waves;
                Objective = objective;
                BeaconMaterial = beacon;
            }
        }

        // ---------- tuning data ----------

        private static void ConfigureGame(GameConfig config)
        {
            config.playerMaxHealth = 100f;
            config.walkSpeed = 5.2f;
            config.sprintSpeed = 8f;
            config.crouchSpeed = 2.6f;
            config.gravity = -20f;
            config.jumpHeight = 1.1f;
            config.baseFovVertical = 62f;   // ~95 horizontal at 16:9
            config.mouseSensitivity = 0.12f;
            config.slowMoTimeScale = 0.35f;
        }

        /// <summary>
        /// The second weapon, and the proof of the modular claim: an SMG is this
        /// method and nothing else — no new class, no new component, no new
        /// prefab. Numbers straight off the gunfeel table: 900 RPM at 20 damage is
        /// five shots to kill and ~267 ms, inside the same arcade window as the
        /// rifle but with a different texture (faster, twitchier, worse at range).
        /// </summary>
        /// <summary>
        /// What the player is allowed to pick, not what the designer picked —
        /// the defaults themselves stay on GameConfig. The sensitivity ceiling is
        /// five times the default rather than the Inspector's 1.0: at 1.0 a
        /// normal mouse sweep is roughly nine full turns, which is not a setting,
        /// it is a way to lose the game.
        /// </summary>
        private static void ConfigureSettings(SettingsConfig config)
        {
            config.sensitivityMin = 0.02f;
            config.sensitivityMax = 0.60f;
            config.sensitivityStep = 0.01f;
            // 50-85 vertical is roughly 80-115 horizontal at 16:9. Below 50 the
            // viewmodel eats the screen; above 85 the fisheye makes drone
            // distance unreadable, and distance is how you survive a Rusher.
            config.fovMin = 50f;
            config.fovMax = 85f;
            config.fovStep = 1f;
            config.volumeMin = 0f;
            config.volumeMax = 1f;
            config.volumeStep = 0.05f;
        }

        private static void ConfigureSmg(WeaponConfig config)
        {
            config.stableId = "wpn_smg_rapid";
            config.displayName = "SMG";
            config.weaponClass = WeaponClass.SMG;
            config.roundsPerMinute = 900f;
            config.bodyDamage = 20f;
            config.headshotMultiplier = 1.5f;
            config.magazineSize = 40;
            config.reserveAmmo = 240;
            config.fireMode = FireMode.FullAuto;
            config.adsTime = 0.2f;
            config.sprintToFireTime = 0.15f;
            config.reloadTime = 1.8f;
            config.reloadEmptyTime = 2.3f;
            // Falls off harder and sooner than the rifle: that, plus the wider
            // hipfire cone, is what makes picking one a decision.
            config.falloffRange = new Vector2(14f, 34f);
            config.minDamageMultiplier = 0.5f;
            config.baseSpread = 3.2f;
            config.spreadPerShot = 0.3f;
            config.maxSpread = 7f;
            config.verticalKickFirstShot = 0.45f;
            config.verticalKickAtShotEight = 0.95f;
            config.horizontalKickMax = 0.45f;
            config.recoilSeed = 4242;
        }

        private static void ConfigureRifle(WeaponConfig config)
        {
            // 700 RPM at 25 damage = 4 shots to kill a 100 HP target,
            // 3 gaps x 0.0857 s = ~257 ms TTK. The whole game is tuned around it.
            config.stableId = "wpn_ar_standard";
            config.displayName = "Assault Rifle";
            config.weaponClass = WeaponClass.AssaultRifle;
            config.roundsPerMinute = 700f;
            config.bodyDamage = 25f;
            config.headshotMultiplier = 1.5f;
            config.magazineSize = 30;
            config.reserveAmmo = 180;
            config.fireMode = FireMode.FullAuto;
            config.adsTime = 0.25f;
            config.sprintToFireTime = 0.2f;
            config.reloadTime = 2f;
            config.reloadEmptyTime = 2.6f;
        }

        private static void ConfigureDifficulty(DifficultyConfig config)
        {
            // Both caps are load-bearing, not tuning knobs: 40 protects a 4 GB
            // GPU, and 3 attackers is why a crowd reads as fair.
            config.maxAliveDrones = 40;
            config.maxSimultaneousAttackers = 3;
            config.minSpawnDistanceFromPlayer = 12f;
            config.spawnSampleRadius = 4f;
            config.attackTokenTimeout = 6f;

            // The late-game economy. Wave 10 pays 220, so the endless curve has to
            // start ABOVE that: the old in-code `100 + wave * 10` paid 210 at wave
            // 11, a pay CUT on the wave where enemy count, health and shop prices
            // all step up together.
            config.endlessClearBonusBase = 120;
            // 20, not 12. The authored waves now end at 320 on wave 10, and at 12
            // per wave the endless ramp opened on 252 — a pay CUT on exactly the
            // wave where count, health and shop prices all step up together.
            // CoreLogicTests guards this seam; if the wave plan's payouts move
            // again, this moves with them.
            config.endlessClearBonusPerWave = 20;
            config.endlessFallbackWaveSize = 8;
            config.endlessSpawnOverSeconds = 20f;
        }

        private static void ConfigureContactDetonate(ContactDetonate config)
        {
            config.triggerRadius = 2.2f;
            // The fuse is the difference between a threat and a coin flip. Half a
            // second is enough to shoot it or step away, not enough to ignore.
            config.fuseSeconds = 0.55f;
            config.lungeSpeedMultiplier = 1.35f;
            config.damage = 24f;      // 100 HP player: three hits, so two mistakes survive
            config.blastRadius = 3.5f;
            config.minBlastMultiplier = 0.33f;
        }

        private static void ConfigureRusher(DroneConfig config)
        {
            config.stableId = "drone_rusher";
            config.displayName = "Rusher";
            // 100 HP = four AR body shots = the same ~257 ms TTK the gun was tuned
            // around. The drone is the first thing that number is spent on.
            config.maxHealth = 100f;
            // Between walk (5.2) and sprint (8.0): backpedalling loses the race,
            // sprinting wins it. That one relationship is the whole chase.
            config.moveSpeed = 6f;
            config.acceleration = 24f;
            config.turnSpeed = 720f;
            config.hoverHeight = 0.9f;
            config.preferredRange = 0f;   // closes to contact
            config.stopDistance = 0.6f;
            config.repathInterval = 0.15f;
            config.scoreValue = 10;
            config.moneyReward = 12;
            // Matches Drone_Core.mat. The drone tints its OWN core every spawn,
            // so the idle end of the ramp has to agree with the material the
            // prefab ships with — otherwise every archetype repaints itself
            // Rusher-red on the first frame and the three become one silhouette.
            config.idleCoreColor = new Color(0.75f, 0.12f, 0.10f);
            config.telegraphCoreColor = new Color(1f, 0.95f, 0.75f);
            config.idleEmission = 0.4f;
            config.telegraphEmission = 3.9f;
            config.deathVfxLifetime = 0.9f;
        }

        private static void ConfigureShooter(DroneConfig config)
        {
            config.stableId = "drone_shooter";
            config.displayName = "Shooter";
            // Lighter than a Rusher: it is dangerous because of where it stands,
            // not because it is hard to kill. Three AR body shots.
            config.maxHealth = 75f;
            config.moveSpeed = 4.2f;
            config.acceleration = 18f;
            config.turnSpeed = 540f;
            config.hoverHeight = 1.25f;
            // The whole archetype, as one number: it holds this ring instead of
            // closing, so the player has to deal with it rather than outrun it.
            config.preferredRange = 14f;
            config.stopDistance = 1f;
            config.repathInterval = 0.25f;
            config.scoreValue = 20;
            config.moneyReward = 20;
            // Drone_Core_Shooter.mat: amber, so "the one at range" is readable
            // across the arena without reading its shape.
            config.idleCoreColor = new Color(0.95f, 0.55f, 0.10f);
            config.telegraphCoreColor = new Color(1f, 0.98f, 0.85f);
            config.idleEmission = 0.45f;
            config.telegraphEmission = 4.2f;
            config.deathVfxLifetime = 0.9f;
        }

        private static void ConfigureTank(DroneConfig config)
        {
            config.stableId = "drone_tank";
            config.displayName = "Tank";
            // 600 HP is 24 AR body shots — most of a magazine, and long enough
            // that standing still to finish one is the wrong answer.
            config.maxHealth = 600f;
            config.moveSpeed = 2.6f;
            config.acceleration = 10f;
            config.turnSpeed = 240f;
            config.hoverHeight = 0.75f;
            config.preferredRange = 0f;
            config.stopDistance = 1.6f;
            config.repathInterval = 0.3f;
            config.scoreValue = 60;
            config.moneyReward = 65;
            // Drone_Core_Tank.mat: crimson, and the slowest windup of the three,
            // so it glows longest before it lands.
            config.idleCoreColor = new Color(0.85f, 0.06f, 0.22f);
            config.telegraphCoreColor = new Color(1f, 0.9f, 0.7f);
            config.idleEmission = 0.35f;
            config.telegraphEmission = 4.5f;
            config.deathVfxLifetime = 1.2f;
        }

        private static void ConfigureRangedBurst(RangedBurst config)
        {
            config.triggerRange = 16f;
            // The gunfeel reference's enemy numbers, as data instead of folklore.
            config.reactionDelay = 0.4f;
            config.accuracy = 0.7f;
            config.maxSpreadDegrees = 9f;
            config.firstShotDeliberateMiss = true;
            config.firstShotMissDegrees = 7f;
            config.burstCount = 3;
            config.burstInterval = 0.18f;
            config.cooldown = 1.6f;
            config.damage = 12f;
            // Slow enough to dodge once seen. This is the number that decides
            // whether ranged fire is a threat or a tax.
            config.projectileSpeed = 18f;
            config.projectileLifetime = 3f;
            config.aimHeightOffset = 1.2f;
        }

        private static void ConfigureHeavySlam(HeavySlam config)
        {
            config.triggerRadius = 3.2f;
            config.windupSeconds = 0.9f;
            config.windupSpeedMultiplier = 0.15f;
            config.cooldown = 2.5f;
            config.damage = 34f;
            config.slamRadius = 4.5f;
            config.minMultiplier = 0.4f;
            config.slamVfxLifetime = 1f;
        }

        private static void ConfigureShop(ShopConfig config)
        {
            // 300 buys roughly one item after the first wave, which is the pacing
            // target: the first break is a real decision, not a formality.
            config.startingMoney = 300;
            config.offersPerBreak = 4;
            config.rerollBaseCost = 50;
            config.rerollCostGrowth = 1.5f;
            config.priceScalingByWave = AnimationCurve.Linear(1f, 1f, 30f, 3f);
        }

        /// <summary>
        /// The five starting passives. Each one is a single row of (stat, kind,
        /// value) — that is the whole upgrade system, and adding a sixth is an
        /// asset rather than code.
        /// </summary>
        private static PassiveConfig[] BuildPassives()
        {
            return new[]
            {
                MakePassive("Passive_MaxHP", "passive_max_hp", "Reinforced Plating",
                    "+25 max health, topped up on purchase",
                    Stat.MaxHealth, StatModifierKind.FlatAdd, 25f, 4),
                MakePassive("Passive_MoveSpeed", "passive_move_speed", "Servo Legs",
                    "+10% movement speed",
                    Stat.MoveSpeed, StatModifierKind.Multiplier, 1.10f, 3),
                MakePassive("Passive_Reload", "passive_reload", "Quick Hands",
                    "+25% reload speed",
                    Stat.ReloadSpeed, StatModifierKind.Multiplier, 1.25f, 3),
                MakePassive("Passive_Damage", "passive_damage", "Hollow Points",
                    "+15% weapon damage",
                    Stat.DamageMult, StatModifierKind.Multiplier, 1.15f, 5),
                MakePassive("Passive_Greed", "passive_greed", "Scrap Magnet",
                    "+25% money from kills and clears",
                    Stat.MoneyGainMult, StatModifierKind.Multiplier, 1.25f, 3),
            };
        }

        private static PassiveConfig MakePassive(string fileName, string stableId, string displayName,
            string description, Stat stat, StatModifierKind kind, float value, int maxStacks)
        {
            return LoadOrCreate<PassiveConfig>(DataPassives + "/" + fileName + ".asset", passive =>
            {
                passive.stableId = stableId;
                passive.displayName = displayName;
                passive.description = description;
                passive.stackable = true;
                passive.maxStacks = maxStacks;
                passive.modifiers = new[]
                {
                    new PassiveConfig.Modifier { stat = stat, kind = kind, value = value },
                };
            });
        }

        /// <summary>
        /// The four effect modules. Explosive and Chain ship with maxDepth 1 so
        /// they react to each other exactly once — that pair is precisely what the
        /// depth rule exists for, and setting it here makes the intent explicit
        /// rather than leaving the interesting case untested.
        /// </summary>
        private static EffectModule[] BuildEffectModules(GameObject explosionVfx)
        {
            Explosive explosive = LoadOrCreate<Explosive>(DataEffects + "/Effect_Explosive.asset", config =>
            {
                config.radius = 3f;
                // A fraction of the shot, not a flat number, so it scales with the
                // weapon and with damage passives instead of replacing them.
                config.damageFraction = 0.8f;
                config.minMultiplier = 0.35f;
                config.explosionLifetime = 1f;
                config.maxDepth = 1;
            });
            SetRef(explosive, "explosionVfx", explosionVfx);
            EditorUtility.SetDirty(explosive);

            Pierce pierce = LoadOrCreate<Pierce>(DataEffects + "/Effect_Pierce.asset", config =>
            {
                config.maxTargets = 2;
                config.damageFalloffPerTarget = 0.75f;
                config.maxDepth = 0;   // it changes the cast; depth is meaningless here
            });

            Ricochet ricochet = LoadOrCreate<Ricochet>(DataEffects + "/Effect_Ricochet.asset", config =>
            {
                config.bouncesPerHit = 1;
                config.bounceRange = 12f;
                config.damageFraction = 0.7f;
                config.scatterDegrees = 8f;
                config.maxDepth = 1;   // one bounce off a bounce, then it stops
            });

            Chain chain = LoadOrCreate<Chain>(DataEffects + "/Effect_Chain.asset", config =>
            {
                config.jumpsPerHit = 2;
                config.jumpRange = 8f;
                config.damageFraction = 0.6f;
                config.maxDepth = 1;
            });

            return new EffectModule[] { explosive, pierce, ricochet, chain };
        }

        private static ShopItemConfig[] BuildShopItems(PassiveConfig[] passives, EffectModule[] effects,
            WeaponConfig smg)
        {
            int[] costs = { 150, 175, 160, 220, 200 };
            var items = new ShopItemConfig[passives.Length + effects.Length + 1];
            for (int i = 0; i < passives.Length; i++)
            {
                PassiveConfig passive = passives[i];
                int cost = i < costs.Length ? costs[i] : 180;
                ShopItemConfig item = LoadOrCreate<ShopItemConfig>(
                    DataShop + "/Shop_" + passive.name.Replace("Passive_", string.Empty) + ".asset",
                    shopItem =>
                    {
                        shopItem.stableId = "shop_" + passive.stableId;
                        shopItem.displayName = passive.displayName;
                        shopItem.description = passive.description;
                        shopItem.cost = cost;
                        shopItem.kind = ShopItemKind.Passive;
                    });
                // Re-linked on every build: a broken payload reference is an offer
                // the player can buy and receive nothing for.
                SetRef(item, "passive", passive);
                items[i] = item;
            }

            // Effect modules: the expensive half of the shop, and the reason to
            // save rather than spend every break.
            (string label, string description, int cost)[] effectMeta =
            {
                ("Explosive Rounds", "every hit detonates for 80% of the shot", 400),
                ("Piercing Rounds", "punches through two more bodies", 350),
                ("Ricochet Rounds", "hits bounce into whatever is nearby", 375),
                ("Chain Lightning", "hits jump to two nearby drones", 450),
            };

            for (int i = 0; i < effects.Length; i++)
            {
                EffectModule effect = effects[i];
                (string label, string description, int cost) = i < effectMeta.Length
                    ? effectMeta[i]
                    : (effect.name, string.Empty, 400);

                ShopItemConfig item = LoadOrCreate<ShopItemConfig>(
                    DataShop + "/Shop_" + effect.name.Replace("Effect_", string.Empty) + ".asset",
                    shopItem =>
                    {
                        shopItem.stableId = "shop_" + effect.name.ToLowerInvariant();
                        shopItem.displayName = label;
                        shopItem.description = description;
                        shopItem.cost = cost;
                        shopItem.kind = ShopItemKind.EffectModule;
                    });
                SetRef(item, "effect", effect);
                items[passives.Length + i] = item;
            }

            ShopItemConfig weaponItem = LoadOrCreate<ShopItemConfig>(DataShop + "/Shop_SMG.asset", shopItem =>
            {
                shopItem.stableId = "shop_wpn_smg_rapid";
                shopItem.displayName = "SMG";
                shopItem.description = "900 RPM, faster handling, weak past 30 m";
                shopItem.cost = 500;
                shopItem.kind = ShopItemKind.Weapon;
            });
            SetRef(weaponItem, "weapon", smg);
            items[items.Length - 1] = weaponItem;

            return items;
        }

        /// <summary>
        /// The two rows that are in every shop break: repairs and resupply.
        ///
        /// They exist because a break could roll four offers the player did not
        /// want, and a wave's income was then simply wasted — which is the honest
        /// answer to tuning-card item 3 and the least interesting possible outcome
        /// of a shop. A repair is never the exciting choice, and that is the point:
        /// it is what makes the exciting choices cost something.
        ///
        /// Both are repeatable. One repair is rarely the whole answer, and a shelf
        /// that empties after a single click is the bad-roll problem again.
        /// </summary>
        private static ShopItemConfig[] BuildAlwaysOffered()
        {
            ConsumableConfig repair = LoadOrCreate<ConsumableConfig>(
                DataShop + "/Consumable_Repair.asset", config =>
                {
                    config.healFraction = 0.5f;
                    config.ammoReserveFraction = 0f;
                });

            ConsumableConfig resupply = LoadOrCreate<ConsumableConfig>(
                DataShop + "/Consumable_Ammo.asset", config =>
                {
                    config.healFraction = 0f;
                    config.ammoReserveFraction = 0.5f;
                });

            ShopItemConfig repairItem = LoadOrCreate<ShopItemConfig>(
                DataShop + "/Shop_Repair.asset", item =>
                {
                    item.stableId = "shop_repair";
                    item.displayName = "Field Repair";
                    item.description = "restores half your maximum health";
                    item.cost = 120;
                    item.kind = ShopItemKind.Consumable;
                    item.repeatable = true;
                });
            SetRef(repairItem, "consumable", repair);

            ShopItemConfig resupplyItem = LoadOrCreate<ShopItemConfig>(
                DataShop + "/Shop_Resupply.asset", item =>
                {
                    item.stableId = "shop_resupply";
                    item.displayName = "Resupply";
                    item.description = "half a full reserve, for whatever you are holding";
                    item.cost = 90;
                    item.kind = ShopItemKind.Consumable;
                    item.repeatable = true;
                });
            SetRef(resupplyItem, "consumable", resupply);

            return new[] { repairItem, resupplyItem };
        }

        /// <summary>
        /// Keeps the pool's references correct without stamping on tuning. A
        /// changed item count means the composition moved and the array is rebuilt
        /// with defaults; otherwise only the item references are re-linked, so
        /// weights and gates edited in the Inspector survive a rebuild.
        /// </summary>
        private static void EnsureShopPool(ShopConfig shop, ShopItemConfig[] items)
        {
            SerializedObject serialized = new(shop);
            SerializedProperty pool = serialized.FindProperty("pool");
            bool rebuild = pool.arraySize != items.Length;
            if (rebuild) pool.arraySize = items.Length;

            for (int i = 0; i < items.Length; i++)
            {
                SerializedProperty element = pool.GetArrayElementAtIndex(i);
                SerializedProperty itemRef = element.FindPropertyRelative("item");

                // Re-seed whenever THIS SLOT changes hands, not only when the array
                // length does. Keyed on length alone, adding and removing one item
                // in the same pass left every slot's weight/minWave/maxOwned
                // belonging to whoever used to sit there — so a module could
                // inherit a passive's "offer from wave 1, stack five times" and
                // become buyable five times on the first break.
                bool reseed = rebuild || itemRef.objectReferenceValue != items[i];
                itemRef.objectReferenceValue = items[i];
                if (!reseed) continue;

                bool isEffect = items[i].kind != ShopItemKind.Passive;
                // Modules are rarer, gated to wave 3+, and one per run: a second
                // copy of Pierce does nothing the first one did not.
                element.FindPropertyRelative("weight").floatValue = isEffect ? 0.6f : 1f;
                element.FindPropertyRelative("minWave").intValue = isEffect ? 3 : 1;
                element.FindPropertyRelative("maxOwned").intValue = isEffect ? 1
                    : items[i].passive != null ? items[i].passive!.maxStacks : 0;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(shop);
        }

        /// <summary>
        /// The endless mix, as curves over wave number. Rushers thin out but never
        /// vanish, Shooters climb steadily, Tanks arrive late and stay rare — a
        /// wave that is 40% Tanks is not harder, it is slower.
        /// </summary>
        private static void EnsureEndlessMix(DifficultyConfig difficulty, DroneConfig rusher,
            DroneConfig shooter, DroneConfig tank)
        {
            (DroneConfig drone, AnimationCurve weight)[] mixPlan =
            {
                (rusher, AnimationCurve.Linear(10f, 6f, 40f, 3f)),
                (shooter, AnimationCurve.Linear(10f, 2f, 40f, 4f)),
                (tank, AnimationCurve.Linear(10f, 0.5f, 40f, 2f)),
            };

            SerializedObject serialized = new(difficulty);
            SerializedProperty mix = serialized.FindProperty("endlessMix");
            bool rebuild = mix.arraySize != mixPlan.Length;
            if (rebuild) mix.arraySize = mixPlan.Length;

            for (int i = 0; i < mixPlan.Length; i++)
            {
                SerializedProperty element = mix.GetArrayElementAtIndex(i);
                SerializedProperty droneRef = element.FindPropertyRelative("drone");
                // Same rule as the shop pool: a slot that changed archetype must
                // not keep the previous one's weight curve.
                bool reseed = rebuild || droneRef.objectReferenceValue != mixPlan[i].drone;
                droneRef.objectReferenceValue = mixPlan[i].drone;
                if (!reseed) continue;
                element.FindPropertyRelative("weightByWave").animationCurveValue = mixPlan[i].weight;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(difficulty);
        }

        /// <summary>
        /// Waves 1-10, hand-authored, because the opening is the part every run
        /// replays and most runs never get past. Counts climb faster than the
        /// window they drip through, so later waves overlap instead of queueing.
        /// </summary>
        /// <summary>
        /// Waves 1-10, hand-authored, because the opening is the part every run
        /// replays and most runs never get past.
        ///
        /// The teaching order is the point: three waves of pure Rushers to learn
        /// the fuse, the first Shooters at 4 (arriving late in the wave, so the
        /// first thing that shoots you is not also the first thing you see), and
        /// one Tank at 7 alone with the crowd it forces you to move through.
        /// </summary>
        /// <summary>
        /// Bumped whenever the authored plan below changes. WaveConfig.designVersion
        /// records which iteration an asset was written from, and a mismatch is
        /// what licenses the builder to overwrite counts a human may have tuned.
        /// </summary>
        private const int WaveDesignVersion = 1;

        /// <summary>
        /// The first ten waves, authored. This is the part of the game every run
        /// replays and the only part most runs ever see, so it is designed rather
        /// than generated — past wave 10 DifficultyConfig's curves take over.
        ///
        /// The plan is written as IDENTITIES rather than as a smooth ramp. The
        /// previous version added roughly two drones per wave with the same mix
        /// throughout, which is a difficulty curve but not a memory: no wave was
        /// recognisable, so nothing taught the player anything specific and
        /// nothing was worth dreading. Now a swarm is a swarm, a siege is fought
        /// from cover, and an anvil is three tanks you walk away from.
        /// </summary>
        private static WaveConfig[] BuildWaves(DroneConfig rusher, DroneConfig shooter, DroneConfig tank)
        {
            (string name, int rushers, float rusherOver, int shooters, int tanks, int bonus)[] plan =
            {
                // Learn the rifle and the fuse. Nothing else is happening.
                ("CONTACT",   3,  6f, 0, 0,  80),
                ("PROBE",     5, 10f, 0, 0,  90),
                // Shooters arrive. Their opening shot misses on purpose, and this
                // is the wave that lesson has room to land in.
                ("OVERWATCH", 5, 12f, 3, 0, 110),
                // Pure pressure, and the first wave with a shape: no ranged threat
                // at all, so the only problem is how fast they close.
                ("SWARM",    14,  8f, 0, 0, 130),
                // The inverse. Few rushers, mostly ranged — the wave that makes
                // the lane dividers worth using instead of running the perimeter.
                ("SIEGE",     4, 10f, 7, 0, 150),
                ("BREACH",   10, 14f, 4, 1, 175),
                // Tank-heavy. Walking away is supposed to be the right answer.
                ("ANVIL",     6, 12f, 3, 3, 200),
                ("SWARM II", 20, 10f, 2, 0, 230),
                ("CROSSFIRE", 8, 14f, 9, 1, 265),
                ("OVERRUN",  16, 18f, 7, 3, 320),
            };

            var waves = new WaveConfig[plan.Length];
            for (int i = 0; i < plan.Length; i++)
            {
                int number = i + 1;
                (string name, int rushers, float rusherOver, int shooters, int tanks, int bonus) = plan[i];

                var entries = new List<(DroneConfig drone, int count, float over, float delay)>(3)
                {
                    (rusher, rushers, rusherOver, 0f),
                };
                // Shooters and Tanks come in AFTER the rushers have engaged: a new
                // threat that arrives with everything else is noise, not a lesson.
                if (shooters > 0) entries.Add((shooter, shooters, 10f, 4f));
                if (tanks > 0) entries.Add((tank, tanks, 6f, 8f));

                WaveConfig wave = LoadOrCreate<WaveConfig>(
                    DataWaves + "/Wave_" + number.ToString("00") + ".asset", config =>
                    {
                        config.waveNumber = number;
                        config.durationTarget = 45f;
                    });

                WriteWave(wave, number, name, bonus, entries);
                waves[i] = wave;
            }
            return waves;
        }

        /// <summary>
        /// Writes a wave, in full or not at all.
        ///
        /// The rule: drone references are ALWAYS re-linked, because a broken
        /// reference is a wave that spawns nothing. Everything else — counts,
        /// timings, the payout, the name — is rewritten only when the plan's
        /// designVersion has moved, so numbers tuned in the Inspector survive a
        /// rebuild but an intentional redesign still lands.
        ///
        /// The old test was array length alone, which meant a redesign keeping the
        /// same number of entries was silently ignored. LoadOrCreate has the same
        /// shape of trap: its configure callback runs on CREATE only, so the
        /// payout and the name have to be written here rather than there or they
        /// would never reach an asset that already exists.
        /// </summary>
        private static void WriteWave(WaveConfig wave, int number, string displayName, int bonus,
            List<(DroneConfig drone, int count, float over, float delay)> entries)
        {
            SerializedObject serialized = new(wave);
            SerializedProperty array = serialized.FindProperty("entries");
            bool rebuild = array.arraySize != entries.Count || wave.designVersion != WaveDesignVersion;
            if (rebuild) array.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("drone").objectReferenceValue = entries[i].drone;
                if (!rebuild) continue;
                element.FindPropertyRelative("count").intValue = entries[i].count;
                element.FindPropertyRelative("spawnOverSeconds").floatValue = entries[i].over;
                element.FindPropertyRelative("startDelay").floatValue = entries[i].delay;
            }
            serialized.ApplyModifiedProperties();

            if (rebuild)
            {
                wave.waveNumber = number;
                wave.displayName = displayName;
                wave.moneyBonusOnClear = bonus;
                wave.designVersion = WaveDesignVersion;
            }
            EditorUtility.SetDirty(wave);
        }

        // ---------- prefabs ----------

        private static GameObject BuildDecalPrefab(Material material)
        {
            GameObject root = new("Fx_ImpactDecal");
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Quad";
            quad.transform.SetParent(root.transform, false);
            quad.transform.localScale = Vector3.one * 0.12f;
            // A decal must never block a later shot.
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_ImpactDecal.prefab");
        }

        private static GameObject BuildSparksPrefab()
        {
            GameObject root = new("Fx_ImpactSparks");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = 0.25f;
            main.startSpeed = 4f;
            main.startSize = 0.03f;
            main.maxParticles = 12;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 8));

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.01f;

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_ImpactSparks.prefab");
        }

        private static GameObject BuildMuzzleFlashPrefab(Material material)
        {
            GameObject root = new("Fx_MuzzleFlash");
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(root.transform, false);
            quad.transform.localScale = Vector3.one * 0.22f;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_MuzzleFlash.prefab");
        }

        private static GameObject BuildCasingPrefab(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Fx_ShellCasing";
            // Ignore Raycast: a casing tumbling past the muzzle must never eat a
            // bullet or catch an impact decal. It keeps its collider for bounces.
            root.layer = 2;
            root.transform.localScale = new Vector3(0.012f, 0.012f, 0.03f);
            root.GetComponent<MeshRenderer>().sharedMaterial = material;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.02f;

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_ShellCasing.prefab");
        }

        private static GameObject BuildDummyTargetPrefab(Material material, HealthConfig healthConfig)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Target_Dummy";
            root.transform.localScale = new Vector3(0.8f, 1.8f, 0.8f);
            root.GetComponent<MeshRenderer>().sharedMaterial = material;

            Health health = root.AddComponent<Health>();
            SetRef(health, "_config", healthConfig);

            // The head: a separate collider carrying a Weakpoint relay, so the
            // hit path can pay the headshot multiplier. Child scale compensates
            // for the stretched parent, or the head would be a 1.8x tall slab.
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.58f, 0f);
            head.transform.localScale = new Vector3(0.35f / 0.8f, 0.35f / 1.8f, 0.35f / 0.8f);
            head.GetComponent<MeshRenderer>().sharedMaterial = material;
            Weakpoint weakpoint = head.AddComponent<Weakpoint>();
            SetRef(weakpoint, "_owner", health);

            HitFlash flash = root.AddComponent<HitFlash>();
            SetRef(flash, "_renderer", root.GetComponent<MeshRenderer>());

            // Shooting-range behaviour: dead targets pop back up after a pause
            // instead of standing in the room forever soaking bullets.
            TargetRespawn respawn = root.AddComponent<TargetRespawn>();
            SetRef(respawn, "_config", healthConfig);
            SetArrayRef(respawn, "_renderers",
                new Object[] { root.GetComponent<MeshRenderer>(), head.GetComponent<MeshRenderer>() });
            SetArrayRef(respawn, "_colliders",
                new Object[] { root.GetComponent<Collider>(), head.GetComponent<Collider>() });

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Target_Dummy.prefab");
        }

        /// <summary>Which silhouette to build. Read at 30 m through fog, shape and core colour are all the player has.</summary>
        private enum DroneShape { Rusher, Shooter, Tank }

        /// <summary>
        /// A drone. Dark hull, one glowing core — the core doubles as the weakpoint
        /// and the attack telegraph, so the thing you want to shoot is the thing
        /// that warns you.
        ///
        /// The three shapes are deliberately different at a glance: the Rusher is
        /// small and finned, the Shooter is tall with a barrel, the Tank is a slab.
        /// Enemy variety beats enemy count, and it only counts as variety if it is
        /// identifiable before it attacks.
        ///
        /// The NavMeshAgent ships DISABLED on every one of them. A pooled agent
        /// enabled while its object sits off the navmesh throws on the first
        /// SetDestination, so the controller owns exactly when it comes alive.
        /// </summary>
        private static GameObject BuildDronePrefab(string name, DroneShape shape, Material hull, Material core)
        {
            Vector3 bodyScale = shape switch
            {
                DroneShape.Shooter => new Vector3(0.6f, 0.85f, 0.6f),
                DroneShape.Tank => new Vector3(1.5f, 1.15f, 1.5f),
                _ => new Vector3(0.7f, 0.55f, 0.7f),
            };
            float coreSize = shape switch
            {
                DroneShape.Shooter => 0.26f,
                DroneShape.Tank => 0.45f,
                _ => 0.3f,
            };
            float hoverHeight = shape switch
            {
                DroneShape.Shooter => 1.25f,   // shoots over the rushers' heads
                DroneShape.Tank => 0.75f,
                _ => 0.9f,
            };

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = name;
            root.transform.localScale = bodyScale;
            MeshRenderer hullRenderer = root.GetComponent<MeshRenderer>();
            hullRenderer.sharedMaterial = hull;

            // Core: sits forward so the reward for aiming is on the face the drone
            // shows while it comes at you. Child scale compensates for the stretched
            // parent, or the core comes out as a slab rather than a cube.
            GameObject coreObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            coreObject.name = "Core";
            coreObject.transform.SetParent(root.transform, false);
            coreObject.transform.localPosition = new Vector3(0f, 0f, 0.42f);
            coreObject.transform.localScale = new Vector3(
                coreSize / bodyScale.x, coreSize / bodyScale.y, coreSize / bodyScale.z);
            MeshRenderer coreRenderer = coreObject.GetComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = core;

            AddShapeDetails(root, shape, hull, bodyScale);

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            // Every archetype keeps its agent radius at or under the 0.5 the
            // surface bakes for. The Tank's HULL is wider than its agent on
            // purpose: a fatter agent would refuse paths the mesh says exist and
            // the Tank would stand still looking broken.
            agent.radius = shape == DroneShape.Tank ? 0.5f : 0.4f;
            agent.height = shape == DroneShape.Tank ? 1.6f : 1.2f;
            agent.baseOffset = hoverHeight;
            agent.autoBraking = false;
            agent.enabled = false;

            Health health = root.AddComponent<Health>();  // max comes from DroneConfig at spawn

            Weakpoint weakpoint = coreObject.AddComponent<Weakpoint>();
            SetRef(weakpoint, "_owner", health);

            HitFlash hitFlash = root.AddComponent<HitFlash>();
            SetRef(hitFlash, "_renderer", hullRenderer);

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;      // a telegraph has to come from a place in the room
            audio.maxDistance = 35f;
            audio.rolloffMode = AudioRolloffMode.Linear;

            root.AddComponent<PooledObject>();

            DroneController controller = root.AddComponent<DroneController>();
            SetRef(controller, "_agent", agent);
            SetRef(controller, "_health", health);
            SetRef(controller, "_pooled", root.GetComponent<PooledObject>());
            SetRef(controller, "_audio", audio);
            SetRef(controller, "_coreRenderer", coreRenderer);

            return SavePrefab(root, Prefabs + "/" + name + ".prefab");
        }

        /// <summary>The bits that make one archetype unmistakable for another. Visual only — no colliders.</summary>
        private static void AddShapeDetails(GameObject root, DroneShape shape, Material hull, Vector3 bodyScale)
        {
            switch (shape)
            {
                case DroneShape.Rusher:
                    AddDetail(root, "Fin_L", new Vector3(-0.6f, 0f, -0.1f), new Vector3(0.35f, 0.25f, 0.6f), hull);
                    AddDetail(root, "Fin_R", new Vector3(0.6f, 0f, -0.1f), new Vector3(0.35f, 0.25f, 0.6f), hull);
                    break;
                case DroneShape.Shooter:
                    // A barrel, so "this one shoots" is legible before it does.
                    AddDetail(root, "Barrel", new Vector3(0f, -0.15f, 0.8f), new Vector3(0.25f, 0.18f, 0.7f), hull);
                    AddDetail(root, "Pod_L", new Vector3(-0.75f, 0.25f, 0f), new Vector3(0.3f, 0.3f, 0.5f), hull);
                    AddDetail(root, "Pod_R", new Vector3(0.75f, 0.25f, 0f), new Vector3(0.3f, 0.3f, 0.5f), hull);
                    break;
                case DroneShape.Tank:
                    AddDetail(root, "Plate_L", new Vector3(-0.55f, 0.1f, 0.1f), new Vector3(0.12f, 0.9f, 0.8f), hull);
                    AddDetail(root, "Plate_R", new Vector3(0.55f, 0.1f, 0.1f), new Vector3(0.12f, 0.9f, 0.8f), hull);
                    AddDetail(root, "Crest", new Vector3(0f, 0.55f, -0.1f), new Vector3(0.5f, 0.25f, 0.6f), hull);
                    break;
            }
        }

        private static void AddDetail(GameObject parent, string name, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            GameObject detail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            detail.name = name;
            detail.transform.SetParent(parent.transform, false);
            detail.transform.localPosition = localPosition;
            detail.transform.localScale = localScale;
            detail.GetComponent<MeshRenderer>().sharedMaterial = material;
            // No collider: hull and core are the only two things a bullet can find,
            // so where you have to aim never depends on decoration.
            Object.DestroyImmediate(detail.GetComponent<Collider>());
        }

        /// <summary>
        /// The Shooter's round. No collider — DroneProjectile sweeps a ray between
        /// frames instead, because a small fast trigger tunnels through walls at
        /// any sane physics step.
        /// </summary>
        private static GameObject BuildDroneProjectilePrefab(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Fx_DroneProjectile";
            root.transform.localScale = new Vector3(0.09f, 0.09f, 0.34f);
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(root.GetComponent<Collider>());

            root.AddComponent<PooledObject>();
            DroneProjectile projectile = root.AddComponent<DroneProjectile>();
            SetRef(projectile, "_pooled", root.GetComponent<PooledObject>());

            return SavePrefab(root, Prefabs + "/Fx_DroneProjectile.prefab");
        }

        /// <summary>The Tank's slam landing: a flat outward burst, so the radius it covers is visible.</summary>
        private static GameObject BuildSlamPrefab(Material material)
        {
            GameObject root = new("Fx_Slam");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 0.35f;
            main.startSpeed = 12f;
            main.startSize = 0.28f;
            main.maxParticles = 36;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 30));

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(90f, 0f, 0f);   // outward along the ground, not upward

            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = true;
            audio.spatialBlend = 1f;
            audio.maxDistance = 45f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.clip = LoadClip("Slam_Hit");

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_Slam.prefab");
        }

        /// <summary>
        /// The detonation. Carries its own AudioSource because the drone that set
        /// it off deactivates in the same frame — a clip played on the drone would
        /// be cut off mid-bang.
        /// </summary>
        private static GameObject BuildExplosionPrefab(Material material)
        {
            GameObject root = new("Fx_Explosion");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 9f;
            main.startSize = 0.35f;
            main.maxParticles = 40;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 26));

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            // A real light for a few frames is what sells a blast, exactly as it
            // does for the muzzle flash. One per explosion, not per particle.
            GameObject lightObject = new("Flash");
            lightObject.transform.SetParent(root.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.35f);
            light.range = 12f;
            light.intensity = 18f;

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = true;
            audio.spatialBlend = 1f;
            audio.maxDistance = 45f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.clip = LoadClip("Explosion");

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_Explosion.prefab");
        }

        /// <summary>
        /// Shot down, as opposed to detonated. Deliberately smaller and quieter
        /// than the explosion so "I killed it" and "it got me" never look or sound
        /// the same.
        /// </summary>
        private static GameObject BuildDroneDeathPrefab(Material material)
        {
            GameObject root = new("Fx_DroneDeath");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = 0.3f;
            main.startSpeed = 5f;
            main.startSize = 0.12f;
            main.maxParticles = 20;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 14));

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = true;
            audio.spatialBlend = 1f;
            audio.maxDistance = 35f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.clip = LoadClip("Drone_Death");

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_DroneDeath.prefab");
        }

        // ---------- scenes ----------

        private static void BuildGreyBoxScene(GameConfig game, SettingsConfig settingsConfig,
            PlayerLoadoutConfig loadout, ImpactConfig impact,
            Material floorMat, Material wallMat, Material targetMat, Material gunmetal, Material gunAccent,
            GameObject dummyPrefab, GameObject decal, GameObject sparks, GameObject flash, GameObject casing,
            DroneAssets drones, RunAssets runAssets, VolumeProfile postFx, Material trimMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // No lighting setup needed. The playbook says to disable Auto Generate
            // because it silently rebakes whenever a light or static object moves
            // and pins the GPU — but Unity 6 removed that mode outright, and both
            // Lightmapping.giWorkflowMode and LightingSettings.autoGenerate are
            // now obsolete. Baking is on-demand by default, so nothing to turn off.
            // Everything here is realtime lit regardless.

            GameObject sun = new("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.color = new Color(0.95f, 0.96f, 1f);
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 32f, 0f);

            // Ambient + fog do the heavy lifting for depth here. Fog especially:
            // it separates the far wall from the near one, which is what makes a
            // grey box readable instead of a flat field of the same colour.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.25f, 0.31f);
            RenderSettings.ambientEquatorColor = new Color(0.15f, 0.16f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.07f, 0.08f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.12f, 0.13f, 0.16f);
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 55f;

            BuildPostFx(postFx);

            GameObject room = BuildRoom(floorMat, wallMat, trimMat);
            BakeNavMesh(room);
            BuildArenaLights();

            ObjectPool pool = new GameObject("ObjectPool").AddComponent<ObjectPool>();
            // Counts are sized for a full wave, not for the demo: the pool exists
            // so the first shot of round twelve costs the same as the first shot
            // of round one.
            var prewarm = new List<(GameObject prefab, int count)>
            {
                (decal, 48), (sparks, 24), (flash, 4), (casing, 24), (dummyPrefab, 8),
            };
            prewarm.AddRange(drones.Pooled);
            SetPrewarm(pool, prewarm.ToArray());

            // The run layer is created BEFORE the player, because the player's
            // motor and weapon subscribe to its StatsChanged event and a
            // serialized reference cannot point at an object that does not exist
            // yet. Its own back-references are filled in once the player does.
            GameObject runObject = new("Run");
            RunContext run = runObject.AddComponent<RunContext>();
            WaveRunner runner = runObject.AddComponent<WaveRunner>();
            SetRef(run, "_config", game);

            // Settings come before the player: PlayerLook subscribes to this
            // component's Changed event, and a serialized reference cannot point
            // at an object that does not exist yet. The service itself resolves
            // lazily, so the Awake order between them does not matter.
            SettingsHub settingsHub = new GameObject("Settings").AddComponent<SettingsHub>();
            SetRef(settingsHub, "_bounds", settingsConfig);
            SetRef(settingsHub, "_defaults", game);
            // The record and the settings live in one file, so they must live in
            // one SaveData object too. Two copies each write the whole file.
            SetRef(run, "_settings", settingsHub);

            (WeaponController weapon, PlayerLook look, Health playerHealth, Transform muzzle,
                Transform playerTransform, Transform cameraTransform) =
                BuildPlayerRig(game, loadout, impact, pool, gunmetal, gunAccent, run, settingsHub);

            BuildTargets(dummyPrefab, targetMat);
            (DroneSpawner spawner, DroneRegistry registry) = BuildDroneRig(drones, pool, playerTransform);

            SetRef(run, "_playerHealth", playerHealth);
            SetRef(runner, "_run", run);
            SetRef(runner, "_spawner", spawner);
            SetRef(runner, "_registry", registry);
            SetRef(runner, "_difficulty", drones.Difficulty);
            SetRef(runner, "_shopConfig", runAssets.Shop);
            SetRef(runner, "_playerHealth", playerHealth);
            SetRef(runner, "_weapon", weapon);          // where bought modules install
            SetArrayRef(runner, "_waves", runAssets.Waves);
            SetRef(weapon, "_ownerHealth", playerHealth);   // modules never damage the shooter

            // The player's input component, so pause can switch the whole action
            // map off. GetComponent is fine here — the guard bans it inside
            // Update/FixedUpdate/LateUpdate, and this is editor build code.
            PlayerInput playerInput = playerTransform.GetComponent<PlayerInput>();

            BuildObjective(runAssets, runner, playerTransform, playerHealth);

            BuildHud(weapon, playerHealth, game, pool, dummyPrefab, muzzle, spawner, registry, cameraTransform,
                run, runner, settingsHub, playerInput);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, GreyBoxScenePath);
        }

        /// <summary>
        /// The scene half of post-processing: one global Volume pointing at the
        /// shared profile.
        ///
        /// sharedProfile, never profile — reading `.profile` CLONES the asset, and
        /// the scene would end up owning a private copy that silently stops
        /// tracking the one everything else tunes.
        /// </summary>
        private static void BuildPostFx(VolumeProfile profile)
        {
            GameObject root = new("PostFx");
            Volume volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        /// <summary>
        /// Turns the image pipeline on for a camera. Without a
        /// UniversalAdditionalCameraData component URP leaves renderPostProcessing
        /// false, which is why every scene in this project rendered with no
        /// tonemapping, no bloom and no anti-aliasing whatsoever.
        ///
        /// Anti-aliasing is the CAMERA's post AA rather than MSAA on purpose. MSAA
        /// lives on the UniversalRenderPipelineAsset, so changing it at runtime is
        /// a write to a ScriptableObject — banned here, because Domain Reload is
        /// off and the write would survive into the next Play session and rewrite
        /// the shipped default. Camera state is scene state and dies with the scene.
        /// The value below is only the shipped default; CameraGraphics overrides it
        /// from the player's saved choice.
        /// </summary>
        private static void EnablePostProcessing(Camera camera)
        {
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        private static GameObject BuildRoom(Material floorMat, Material wallMat, Material trimMat)
        {
            GameObject room = new("Room");

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(room.transform, false);
            floor.transform.localScale = new Vector3(40f, 0.5f, 40f);
            floor.transform.position = new Vector3(0f, -0.25f, 0f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            AddBox(room, "Wall_N", new Vector3(0f, 2.5f, 20f), new Vector3(40f, 5f, 0.5f), wallMat);
            AddBox(room, "Wall_S", new Vector3(0f, 2.5f, -20f), new Vector3(40f, 5f, 0.5f), wallMat);
            AddBox(room, "Wall_E", new Vector3(20f, 2.5f, 0f), new Vector3(0.5f, 5f, 40f), wallMat);
            AddBox(room, "Wall_W", new Vector3(-20f, 2.5f, 0f), new Vector3(0.5f, 5f, 40f), wallMat);

            // THE ARENA. One open room made the fight shapeless: every drone took
            // the same straight line, retreating was a straight line too, and the
            // Shooter had permanent line of sight from anywhere. Three lanes
            // around a solid centre fix all three — you break line of sight by
            // moving, and the crowd arrives split instead of as one mass.
            //
            // Full-height blocks (3 m) break sight completely; half-height cover
            // (1.2 m) sits below eye level, so you can shoot over it while a
            // pathing drone still has to go around. That asymmetry is what makes
            // cover worth using rather than worth hiding behind.

            // The centre mass. Everything orbits this, and nothing shoots across it.
            AddBox(room, "Core_Bunker", new Vector3(0f, 1.5f, 2f), new Vector3(8f, 3f, 6f), wallMat);

            // Lane dividers, with a deliberate 7 m crossing gap between each pair:
            // wide enough that a Tank fits, narrow enough to be a decision.
            AddBox(room, "Divider_W_South", new Vector3(-9f, 1.5f, -6f), new Vector3(1f, 3f, 10f), wallMat);
            AddBox(room, "Divider_E_South", new Vector3(9f, 1.5f, -6f), new Vector3(1f, 3f, 10f), wallMat);
            AddBox(room, "Divider_W_North", new Vector3(-9f, 1.5f, 11f), new Vector3(1f, 3f, 8f), wallMat);
            AddBox(room, "Divider_E_North", new Vector3(9f, 1.5f, 11f), new Vector3(1f, 3f, 8f), wallMat);

            // Shoot-over cover. The south block is in front of the player spawn on
            // purpose: the first thing you learn is that you can back behind it.
            AddBox(room, "Cover_S", new Vector3(0f, 0.6f, -10f), new Vector3(6f, 1.2f, 1f), wallMat);
            AddBox(room, "Cover_W", new Vector3(-14f, 0.6f, 4f), new Vector3(4f, 1.2f, 1f), wallMat);
            AddBox(room, "Cover_E", new Vector3(14f, 0.6f, 4f), new Vector3(4f, 1.2f, 1f), wallMat);
            AddBox(room, "Cover_NW", new Vector3(-5f, 0.6f, 14f), new Vector3(1f, 1.2f, 5f), wallMat);
            AddBox(room, "Cover_NE", new Vector3(5f, 0.6f, 14f), new Vector3(1f, 1.2f, 5f), wallMat);

            // Corner pillars: they stop the perimeter from being a free racetrack
            // and give a kiting Shooter somewhere to be forced out of.
            AddBox(room, "Pillar_NW", new Vector3(-16f, 2f, 16f), new Vector3(2f, 4f, 2f), wallMat);
            AddBox(room, "Pillar_NE", new Vector3(16f, 2f, 16f), new Vector3(2f, 4f, 2f), wallMat);
            AddBox(room, "Pillar_SW", new Vector3(-16f, 2f, -16f), new Vector3(2f, 4f, 2f), wallMat);
            AddBox(room, "Pillar_SE", new Vector3(16f, 2f, -16f), new Vector3(2f, 4f, 2f), wallMat);

            // Edge trim. Untextured grey blocks under fog lose their silhouette at
            // exactly the distance where knowing whether you can back behind one
            // matters, and cover you cannot see the edge of is cover you do not
            // use. A lit line along the top of every full-height mass is the
            // cheapest fix there is — and with bloom now resolving, it costs
            // nothing beyond the boxes themselves.
            AddTrim(room, "Trim_Bunker", new Vector3(0f, 3.02f, 2f), new Vector3(8.1f, 0.06f, 6.1f), trimMat);
            AddTrim(room, "Trim_Div_WS", new Vector3(-9f, 3.02f, -6f), new Vector3(1.1f, 0.06f, 10.1f), trimMat);
            AddTrim(room, "Trim_Div_ES", new Vector3(9f, 3.02f, -6f), new Vector3(1.1f, 0.06f, 10.1f), trimMat);
            AddTrim(room, "Trim_Div_WN", new Vector3(-9f, 3.02f, 11f), new Vector3(1.1f, 0.06f, 8.1f), trimMat);
            AddTrim(room, "Trim_Div_EN", new Vector3(9f, 3.02f, 11f), new Vector3(1.1f, 0.06f, 8.1f), trimMat);
            AddTrim(room, "Trim_Pillar_NW", new Vector3(-16f, 4.02f, 16f), new Vector3(2.1f, 0.06f, 2.1f), trimMat);
            AddTrim(room, "Trim_Pillar_NE", new Vector3(16f, 4.02f, 16f), new Vector3(2.1f, 0.06f, 2.1f), trimMat);
            AddTrim(room, "Trim_Pillar_SW", new Vector3(-16f, 4.02f, -16f), new Vector3(2.1f, 0.06f, 2.1f), trimMat);
            AddTrim(room, "Trim_Pillar_SE", new Vector3(16f, 4.02f, -16f), new Vector3(2.1f, 0.06f, 2.1f), trimMat);

            // Half-height cover gets it too: this is the row the player has to
            // judge "can I shoot over that" against, from across the arena.
            AddTrim(room, "Trim_Cover_S", new Vector3(0f, 1.22f, -10f), new Vector3(6.1f, 0.05f, 1.1f), trimMat);
            return room;
        }

        /// <summary>
        /// Bakes the drone navmesh over the room and PERSISTS it as an asset.
        ///
        /// The persistence is the fiddly half: NavMeshSurface.BuildNavMesh leaves
        /// the result in memory, and a scene reference to an unsaved object is
        /// dropped on save — the same class of silent failure that produced a
        /// scene full of null configs on the first build. Writing it to disk and
        /// re-assigning it makes the link something GreyBoxVerify can prove.
        ///
        /// Collect from CHILDREN, not the whole scene: baked after the room but
        /// before the player and the targets exist, an "all objects" bake would
        /// carve the dummy targets into the mesh as permanent obstacles.
        /// </summary>
        private static void BakeNavMesh(GameObject room)
        {
            NavMeshSurface surface = room.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                // Delete the stale asset on the way out. Returning early used to
                // leave the PREVIOUS arena's bake sitting at NavMeshPath, and
                // GreyBoxVerify.Ensure would dutifully find it and assign it to
                // this freshly built surface — so a failed bake produced a scene
                // that verified clean while the drones pathed around a room that
                // no longer existed. With the file gone there is nothing to
                // relink, the reference stays null, and the verifier fails the
                // build the way it should.
                Debug.LogError("NavMesh bake produced no data — drones will spawn and never move.");
                AssetDatabase.DeleteAsset(NavMeshPath);
                AssetDatabase.SaveAssets();
                return;
            }

            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            AssetDatabase.SaveAssets();

            NavMeshData? saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            if (saved != null) surface.navMeshData = saved;
            EditorUtility.SetDirty(surface);
        }

        /// <summary>
        /// The spawn ring, the registry and the spawner. Spawn points sit on a
        /// ring inside the walls; the spawner rejects any that are closer to the
        /// player than DifficultyConfig allows, so where the player stands decides
        /// which points are legal without any of them being special.
        /// </summary>
        private static (DroneSpawner, DroneRegistry) BuildDroneRig(DroneAssets drones, ObjectPool pool,
            Transform player)
        {
            GameObject root = new("Drones");
            DroneRegistry registry = root.AddComponent<DroneRegistry>();
            DroneSpawner spawner = root.AddComponent<DroneSpawner>();

            GameObject pointsRoot = new("SpawnPoints");
            pointsRoot.transform.SetParent(root.transform, false);

            const int count = 8;
            const float radius = 16f;
            var points = new Object[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                GameObject point = new("Spawn_" + i);
                point.transform.SetParent(pointsRoot.transform, false);
                point.transform.position = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                points[i] = point.transform;
            }

            SetRef(spawner, "_pool", pool);
            SetRef(spawner, "_registry", registry);
            SetRef(spawner, "_target", player);
            SetRef(spawner, "_difficulty", drones.Difficulty);
            SetRef(spawner, "_defaultDrone", drones.Default);
            SetArrayRef(spawner, "_spawnPoints", points);

            return (spawner, registry);
        }

        private static void AddBox(GameObject parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// One block of the viewmodel. Colliders are stripped: a collider on the
        /// player's own gun sits directly in front of the camera, so every shot
        /// would raycast into the weapon instead of the world. Shadows are off
        /// too — a viewmodel casting shadows into the scene looks like a floating
        /// prop, because that is exactly what it is.
        /// </summary>
        /// <summary>
        /// A cosmetic strip of light along an edge. NO COLLIDER, and that is the
        /// whole reason this is not just AddBox: BakeNavMesh collects from
        /// PhysicsColliders, so a trim box with the collider CreatePrimitive gives
        /// it would carve itself into the drone navmesh as a floating obstacle.
        /// Same rule the viewmodel parts and the drone shape details follow.
        /// </summary>
        private static void AddTrim(GameObject parent, string name, Vector3 position, Vector3 scale,
            Material material)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = name;
            strip.transform.SetParent(parent.transform, false);
            strip.transform.position = position;
            strip.transform.localScale = scale;
            Object.DestroyImmediate(strip.GetComponent<Collider>());

            MeshRenderer renderer = strip.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // A 6 cm strip contributes nothing to a shadow but still costs a draw
            // in the shadow pass, once per light, for every one of them.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// The arena's light rig. One directional sun plus ambient left every lane
        /// looking identical, and in a grey box the lighting IS the level art —
        /// it is the only thing that says "they come from over there" without
        /// putting a marker on the HUD.
        ///
        /// Every one of these has shadows OFF. The sun stays the only shadow
        /// caster: four more shadow maps is real frame time on a 3050 laptop, and
        /// item 9 of the tuning card is specifically about frame time.
        ///
        /// They are also warm and DIM. Bright saturated colour is reserved for
        /// drone cores, so that red always means something is trying to kill you.
        /// </summary>
        private static void BuildArenaLights()
        {
            GameObject root = new("Lights");

            AddLight(root, "Lane_W", new Vector3(-14.5f, 4.2f, 4f), new Color(1f, 0.72f, 0.45f), 1.6f, 15f);
            AddLight(root, "Lane_E", new Vector3(14.5f, 4.2f, 4f), new Color(1f, 0.72f, 0.45f), 1.6f, 15f);
            AddLight(root, "Lane_N", new Vector3(0f, 4.2f, 14f), new Color(1f, 0.72f, 0.45f), 1.6f, 15f);

            // The centre mass reads as the thing to orbit, so it gets the one cool
            // key. It also lights the face of the bunker the player backs against.
            AddLight(root, "Key_Core", new Vector3(0f, 4.6f, 2f), new Color(0.70f, 0.82f, 1f), 2.2f, 14f);
        }

        private static void AddLight(GameObject parent, string name, Vector3 position, Color color,
            float intensity, float range)
        {
            GameObject holder = new(name);
            holder.transform.SetParent(parent.transform, false);
            holder.transform.position = position;

            Light light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void AddViewmodelPart(GameObject parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;

            Object.DestroyImmediate(part.GetComponent<Collider>());

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static (WeaponController, PlayerLook, Health, Transform, Transform, Transform) BuildPlayerRig(
            GameConfig game, PlayerLoadoutConfig loadout, ImpactConfig impact, ObjectPool pool,
            Material gunmetal, Material gunAccent, RunContext run, SettingsHub settings)
        {
            GameObject player = new("Player");
            player.transform.position = new Vector3(0f, 0.1f, -12f);
            player.layer = LayerMask.NameToLayer("Default");

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = game.standingHeight;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, game.standingHeight * 0.5f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.35f;

            PlayerInput input = player.AddComponent<PlayerInput>();
            var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                "Assets/_Project/Settings/CoD.inputactions");
            SetRef(input, "_actions", actions);

            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            SetRef(motor, "_config", game);
            SetRef(motor, "_input", input);
            SetRef(motor, "_run", run);   // MoveSpeed passives

            Health health = player.AddComponent<Health>();
            // The player's max HP comes from GameConfig, the one asset that owns
            // global player numbers — not from a HealthConfig like props do.
            SetRef(health, "_playerConfig", game);

            GameObject pivot = new("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, game.standingHeight - 0.2f, 0f);

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pivot.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = game.baseFovVertical;
            camera.nearClipPlane = 0.05f;
            EnablePostProcessing(camera);
            cameraObject.AddComponent<AudioListener>();
            CameraShake shake = cameraObject.AddComponent<CameraShake>();

            CameraGraphics graphics = cameraObject.AddComponent<CameraGraphics>();
            SetRef(graphics, "_settings", settings);
            SetRef(graphics, "_camera", camera);

            PlayerLook look = player.AddComponent<PlayerLook>();
            SetRef(look, "_config", game);
            SetRef(look, "_input", input);
            SetRef(look, "_motor", motor);
            SetRef(look, "_cameraPivot", pivot.transform);
            SetRef(look, "_camera", camera);
            SetRef(look, "_settings", settings);   // saved sensitivity / FOV / invert

            // The viewmodel. There was no gun on screen at all before this, which
            // is most of why the grey box read as a tech demo rather than a
            // shooter: nothing occupies the lower-right, nothing moves when you
            // look around, and the muzzle flash spawns in empty air.
            GameObject weaponRig = new("WeaponRig");
            weaponRig.transform.SetParent(cameraObject.transform, false);
            weaponRig.transform.localPosition = new Vector3(0.145f, -0.125f, 0.28f);

            GameObject model = new("Viewmodel");
            model.transform.SetParent(weaponRig.transform, false);

            AddViewmodelPart(model, "Receiver", new Vector3(0f, 0f, 0.10f), new Vector3(0.055f, 0.075f, 0.30f), gunmetal);
            AddViewmodelPart(model, "Handguard", new Vector3(0f, -0.004f, 0.31f), new Vector3(0.045f, 0.052f, 0.23f), gunAccent);
            AddViewmodelPart(model, "Barrel", new Vector3(0f, 0.006f, 0.46f), new Vector3(0.019f, 0.019f, 0.13f), gunAccent);
            AddViewmodelPart(model, "Stock", new Vector3(0f, -0.006f, -0.13f), new Vector3(0.045f, 0.062f, 0.17f), gunmetal);
            AddViewmodelPart(model, "Grip", new Vector3(0f, -0.077f, 0.015f), new Vector3(0.04f, 0.105f, 0.05f), gunmetal);
            AddViewmodelPart(model, "Magazine", new Vector3(0f, -0.102f, 0.15f), new Vector3(0.036f, 0.135f, 0.062f), gunAccent);
            AddViewmodelPart(model, "SightRear", new Vector3(0f, 0.052f, 0.01f), new Vector3(0.022f, 0.028f, 0.03f), gunAccent);
            AddViewmodelPart(model, "SightFront", new Vector3(0f, 0.052f, 0.42f), new Vector3(0.016f, 0.032f, 0.022f), gunAccent);

            WeaponSway sway = weaponRig.AddComponent<WeaponSway>();
            SetRef(sway, "_input", input);
            SetRef(sway, "_motor", motor);

            // Muzzle now sits at the barrel tip, so the flash and the light come
            // out of the gun rather than out of the middle of the screen.
            GameObject muzzle = new("Muzzle");
            muzzle.transform.SetParent(model.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.006f, 0.53f);

            GameObject casingEject = new("CasingEject");
            casingEject.transform.SetParent(model.transform, false);
            casingEject.transform.localPosition = new Vector3(0.045f, 0.02f, 0.13f);
            casingEject.transform.localRotation = Quaternion.Euler(0f, 60f, 0f);

            GameObject lightObject = new("MuzzleLight");
            lightObject.transform.SetParent(muzzle.transform, false);
            Light muzzleLight = lightObject.AddComponent<Light>();
            muzzleLight.type = LightType.Point;
            muzzleLight.range = 8f;
            muzzleLight.color = new Color(1f, 0.85f, 0.55f);
            muzzleLight.enabled = false;

            AudioSource closeAudio = cameraObject.AddComponent<AudioSource>();
            closeAudio.playOnAwake = false;
            closeAudio.spatialBlend = 0f;
            AudioSource tailAudio = cameraObject.AddComponent<AudioSource>();
            tailAudio.playOnAwake = false;
            tailAudio.spatialBlend = 0f;

            WeaponController weapon = player.AddComponent<WeaponController>();
            SetRef(weapon, "_loadout", loadout);
            SetRef(weapon, "_impact", impact);
            SetRef(weapon, "_run", run);   // DamageMult and ReloadSpeed passives
            SetRef(weapon, "_input", input);
            SetRef(weapon, "_look", look);
            SetRef(weapon, "_motor", motor);
            SetRef(weapon, "_pool", pool);
            SetRef(weapon, "_shake", shake);
            SetRef(weapon, "_sway", sway);
            SetRef(weapon, "_muzzle", muzzle.transform);
            SetRef(weapon, "_casingEject", casingEject.transform);
            SetRef(weapon, "_muzzleLight", muzzleLight);
            SetRef(weapon, "_audioClose", closeAudio);
            SetRef(weapon, "_audioTail", tailAudio);

            return (weapon, look, health, muzzle.transform, player.transform, cameraObject.transform);
        }

        /// <summary>
        /// The repair beacon and the three lane anchors it moves between.
        ///
        /// Anchor positions are picked to sit clear of every block in BuildRoom:
        /// the west and east lanes south of their cover strips, and the north lane
        /// between the two NW/NE covers. Never the origin — that is inside the
        /// centre bunker, which is the arena's oldest trap.
        ///
        /// The pad carries NO collider. It sits on the floor the player walks
        /// across, and a collider there would either block movement or, worse, be
        /// something the aim ray hits.
        /// </summary>
        private static void BuildObjective(RunAssets runAssets, WaveRunner runner, Transform player,
            Health playerHealth)
        {
            GameObject root = new("Objective");
            ArenaObjective objective = root.AddComponent<ArenaObjective>();

            GameObject anchorsRoot = new("Anchors");
            anchorsRoot.transform.SetParent(root.transform, false);

            Vector3[] spots =
            {
                new(-14.5f, 0f, -4f),   // west lane
                new(14.5f, 0f, -4f),    // east lane
                new(0f, 0f, 15f),       // north, between the two covers
            };

            var anchors = new Object[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                GameObject point = new("Anchor_" + i);
                point.transform.SetParent(anchorsRoot.transform, false);
                point.transform.position = spots[i];
                anchors[i] = point.transform;
            }

            float radius = runAssets.Objective.radius;

            GameObject visual = new("Beacon");
            visual.transform.SetParent(root.transform, false);

            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            pad.transform.SetParent(visual.transform, false);
            // A cylinder primitive is 2 units tall and 1 across, so the diameter
            // is the radius doubled and the height is halved twice over.
            pad.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            Object.DestroyImmediate(pad.GetComponent<Collider>());

            MeshRenderer padRenderer = pad.GetComponent<MeshRenderer>();
            padRenderer.sharedMaterial = runAssets.BeaconMaterial;
            padRenderer.shadowCastingMode = ShadowCastingMode.Off;

            GameObject glow = new("Glow");
            glow.transform.SetParent(visual.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.30f, 1f, 0.60f);
            light.intensity = 2.4f;
            light.range = radius * 3f;
            light.shadows = LightShadows.None;

            SetRef(objective, "_config", runAssets.Objective);
            SetRef(objective, "_runner", runner);
            SetRef(objective, "_player", player);
            SetRef(objective, "_playerHealth", playerHealth);
            SetRef(objective, "_visual", visual.transform);
            SetArrayRef(objective, "_anchors", anchors);
        }

        private static void BuildTargets(GameObject dummyPrefab, Material material)
        {
            GameObject root = new("Targets");
            // Clear of the arena geometry, one per lane plus two at the north end,
            // so the tuning bench survived the arena rebuild.
            Vector3[] spots =
            {
                new(-13f, 0.9f, 8f), new(13f, 0.9f, 8f), new(0f, 0.9f, 12f),
                new(-6f, 0.9f, 17f), new(6f, 0.9f, 17f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                instance.transform.SetParent(root.transform, false);
                instance.transform.position = spots[i];
            }
        }

        private static void BuildHud(WeaponController weapon, Health playerHealth, GameConfig game,
            ObjectPool pool, GameObject dummyPrefab, Transform spawnOrigin,
            DroneSpawner spawner, DroneRegistry registry, Transform cameraTransform,
            RunContext run, WaveRunner runner, SettingsHub settingsHub, PlayerInput input)
        {
            GameObject canvasObject = new("HUD");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject markerRoot = new("Hitmarker", typeof(RectTransform));
            markerRoot.transform.SetParent(canvasObject.transform, false);
            RectTransform markerRect = markerRoot.GetComponent<RectTransform>();
            markerRect.anchoredPosition = Vector2.zero;
            markerRect.sizeDelta = new Vector2(32f, 32f);

            // Four short bars rotated into an X - no sprite asset, no binary in git.
            Graphic[] bars = new Graphic[4];
            float[] angles = { 45f, -45f, 45f, -45f };
            Vector2[] offsets = { new(-8f, 8f), new(8f, 8f), new(8f, -8f), new(-8f, -8f) };
            for (int i = 0; i < 4; i++)
            {
                GameObject bar = new("Bar" + i, typeof(RectTransform));
                bar.transform.SetParent(markerRoot.transform, false);
                Image image = bar.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f);
                RectTransform rect = bar.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(3f, 11f);
                rect.anchoredPosition = offsets[i];
                rect.localRotation = Quaternion.Euler(0f, 0f, angles[i]);
                bars[i] = image;
            }

            // Crosshair: centre dot plus four arms that open with bloom.
            GameObject crossRoot = new("Crosshair", typeof(RectTransform));
            crossRoot.transform.SetParent(canvasObject.transform, false);
            crossRoot.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            CanvasGroup crossGroup = crossRoot.AddComponent<CanvasGroup>();
            crossGroup.blocksRaycasts = false;
            crossGroup.interactable = false;

            // Every element gets a dark backing plate one pixel larger on each
            // side. A plain white crosshair vanishes against a bright floor —
            // which is exactly what happened on the first pass — and an outlined
            // one reads on light AND dark without a second thought.
            Graphic[] arms = new Graphic[4];
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i < 2; // order matches Crosshair.Directions: up, down, left, right
                Vector2 size = vertical ? new Vector2(2.5f, 9f) : new Vector2(9f, 2.5f);

                GameObject arm = new("Arm" + i, typeof(RectTransform));
                arm.transform.SetParent(crossRoot.transform, false);

                GameObject outline = new("Outline", typeof(RectTransform));
                outline.transform.SetParent(arm.transform, false);
                Image outlineImage = outline.AddComponent<Image>();
                outlineImage.color = new Color(0f, 0f, 0f, 0.65f);
                outlineImage.raycastTarget = false;
                outline.GetComponent<RectTransform>().sizeDelta = size + new Vector2(2f, 2f);

                Image image = arm.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.9f);
                image.raycastTarget = false;
                arm.GetComponent<RectTransform>().sizeDelta = size;
                arms[i] = image;
            }

            GameObject dot = new("CentreDot", typeof(RectTransform));
            dot.transform.SetParent(crossRoot.transform, false);

            GameObject dotOutline = new("Outline", typeof(RectTransform));
            dotOutline.transform.SetParent(dot.transform, false);
            Image dotOutlineImage = dotOutline.AddComponent<Image>();
            dotOutlineImage.color = new Color(0f, 0f, 0f, 0.65f);
            dotOutlineImage.raycastTarget = false;
            dotOutline.GetComponent<RectTransform>().sizeDelta = new Vector2(5f, 5f);

            Image dotImage = dot.AddComponent<Image>();
            dotImage.color = new Color(1f, 1f, 1f, 0.9f);
            dotImage.raycastTarget = false;
            dot.GetComponent<RectTransform>().sizeDelta = new Vector2(3f, 3f);

            Crosshair crosshair = canvasObject.AddComponent<Crosshair>();
            SetRef(crosshair, "_weapon", weapon);
            SetRef(crosshair, "_group", crossGroup);
            SetArrayRef(crosshair, "_arms", arms);

            AudioSource hudAudio = canvasObject.AddComponent<AudioSource>();
            hudAudio.playOnAwake = false;

            Hitmarker hitmarker = canvasObject.AddComponent<Hitmarker>();
            SetRef(hitmarker, "_weapon", weapon);
            SetRef(hitmarker, "_markerRoot", markerRoot.transform);
            SetRef(hitmarker, "_audio", hudAudio);
            SetRef(hitmarker, "_hitClip", LoadClip("Hitmarker"));
            SetRef(hitmarker, "_killClip", LoadClip("Hitmarker_Kill"));
            SetArrayRef(hitmarker, "_markerParts", bars);

            Text ammo = BuildLabel(canvasObject, "Ammo", new Vector2(-90f, 60f),
                TextAnchor.LowerRight, new Vector2(1f, 0f));
            Text healthLabel = BuildLabel(canvasObject, "Health", new Vector2(90f, 60f),
                TextAnchor.LowerLeft, new Vector2(0f, 0f));

            // Low-ammo warning: a red bar under the ammo count. The field existed
            // from the start and was never assigned, so the cue never appeared.
            GameObject lowAmmo = new("LowAmmoTint", typeof(RectTransform));
            lowAmmo.transform.SetParent(canvasObject.transform, false);
            Image lowAmmoImage = lowAmmo.AddComponent<Image>();
            lowAmmoImage.color = new Color(0.85f, 0.22f, 0.16f, 0.85f);
            lowAmmoImage.raycastTarget = false;
            RectTransform lowAmmoRect = lowAmmo.GetComponent<RectTransform>();
            lowAmmoRect.anchorMin = new Vector2(1f, 0f);
            lowAmmoRect.anchorMax = new Vector2(1f, 0f);
            lowAmmoRect.pivot = new Vector2(1f, 0f);
            lowAmmoRect.anchoredPosition = new Vector2(-90f, 48f);
            lowAmmoRect.sizeDelta = new Vector2(160f, 3f);
            lowAmmoImage.enabled = false;

            Hud hud = canvasObject.AddComponent<Hud>();
            SetRef(hud, "_weapon", weapon);
            SetRef(hud, "_playerHealth", playerHealth);
            SetRef(hud, "_ammoLabel", ammo);
            SetRef(hud, "_healthLabel", healthLabel);
            SetRef(hud, "_lowAmmoTint", lowAmmoImage);

            BuildDamageFeedback(canvasObject, game, playerHealth, cameraTransform, hudAudio);
            BuildRunUi(canvasObject, run, runner, weapon, hudAudio);
            BuildPauseUi(canvasObject, settingsHub, input, run, runner);

            CheatConsole console = canvasObject.AddComponent<CheatConsole>();
            SetRef(console, "_config", game);
            SetRef(console, "_weapon", weapon);
            SetRef(console, "_playerHealth", playerHealth);
            SetRef(console, "_pool", pool);
            SetRef(console, "_dummyTargetPrefab", dummyPrefab);
            SetRef(console, "_spawnOrigin", spawnOrigin);
            SetRef(console, "_droneSpawner", spawner);
            SetRef(console, "_droneRegistry", registry);
            SetRef(console, "_waveRunner", runner);
            SetRef(console, "_run", run);
            SetRef(console, "_pause", canvasObject.GetComponent<PausePanel>());
        }

        /// <summary>
        /// Being hurt, made visible. Until the Rusher existed nothing could damage
        /// the player at all, so this is the first time the HUD has to answer
        /// "what hit me, and from where" — a number dropping in the corner does
        /// not answer either question.
        ///
        /// Plain Images throughout: a full-screen flash, four screen-edge wedges
        /// for direction, and a low-health tint. No sprite assets, so no binaries
        /// in git and nothing to keep in sync.
        /// </summary>
        private static void BuildDamageFeedback(GameObject canvasObject, GameConfig game, Health playerHealth,
            Transform cameraTransform, AudioSource audio)
        {
            Image flash = BuildFullScreenImage(canvasObject, "DamageFlash", new Color(0.75f, 0.08f, 0.06f, 0f));
            Image lowHealth = BuildFullScreenImage(canvasObject, "LowHealthTint", new Color(0.55f, 0.02f, 0.02f, 0f));

            // Order matches PlayerDamageFeedback and the crosshair arms: up (the
            // hit came from in front), down (behind), left, right.
            var bars = new Object[4];
            Vector2[] anchors = { new(0.5f, 1f), new(0.5f, 0f), new(0f, 0.5f), new(1f, 0.5f) };
            Vector2[] sizes = { new(420f, 26f), new(420f, 26f), new(26f, 420f), new(26f, 420f) };
            Vector2[] offsets = { new(0f, -70f), new(0f, 70f), new(70f, 0f), new(-70f, 0f) };
            for (int i = 0; i < 4; i++)
            {
                GameObject bar = new("DamageDir" + i, typeof(RectTransform));
                bar.transform.SetParent(canvasObject.transform, false);
                Image image = bar.AddComponent<Image>();
                image.color = new Color(0.9f, 0.18f, 0.13f, 0f);
                image.raycastTarget = false;
                image.enabled = false;
                RectTransform rect = bar.GetComponent<RectTransform>();
                rect.anchorMin = anchors[i];
                rect.anchorMax = anchors[i];
                rect.pivot = anchors[i];
                rect.sizeDelta = sizes[i];
                rect.anchoredPosition = offsets[i];
                bars[i] = image;
            }

            PlayerDamageFeedback feedback = canvasObject.AddComponent<PlayerDamageFeedback>();
            SetRef(feedback, "_config", game);
            SetRef(feedback, "_health", playerHealth);
            SetRef(feedback, "_flash", flash);
            SetRef(feedback, "_lowHealthTint", lowHealth);
            SetArrayRef(feedback, "_directionBars", bars);
            SetRef(feedback, "_cameraTransform", cameraTransform);
            SetRef(feedback, "_audio", audio);
            SetRef(feedback, "_hurtClip", LoadClip("Player_Hurt"));
        }

        /// <summary>
        /// Wave readout, shop and game-over screen. All plain Text: the shop is
        /// keyboard-driven (1-4 buy, R reroll, Space continue), which avoids
        /// needing an EventSystem, an input module and cursor lock/unlock around
        /// every break for a four-line list.
        /// </summary>
        private static void BuildRunUi(GameObject canvasObject, RunContext run, WaveRunner runner,
            WeaponController weapon, AudioSource audio)
        {
            Text wave = BuildLabel(canvasObject, "WaveLabel", new Vector2(90f, -60f),
                TextAnchor.UpperLeft, new Vector2(0f, 1f), 34);
            Text enemies = BuildLabel(canvasObject, "EnemiesLabel", new Vector2(90f, -104f),
                TextAnchor.UpperLeft, new Vector2(0f, 1f), 26);
            Text money = BuildLabel(canvasObject, "MoneyLabel", new Vector2(-90f, -60f),
                TextAnchor.UpperRight, new Vector2(1f, 1f), 34);
            Text banner = BuildLabel(canvasObject, "BannerLabel", new Vector2(0f, 150f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), 46);
            banner.rectTransform.sizeDelta = new Vector2(900f, 70f);

            WaveHud hud = canvasObject.AddComponent<WaveHud>();
            SetRef(hud, "_runner", runner);
            SetRef(hud, "_run", run);
            SetRef(hud, "_waveLabel", wave);
            SetRef(hud, "_enemiesLabel", enemies);
            SetRef(hud, "_moneyLabel", money);
            SetRef(hud, "_bannerLabel", banner);

            // ---- shop ----
            GameObject shopRoot = new("ShopPanel", typeof(RectTransform));
            shopRoot.transform.SetParent(canvasObject.transform, false);
            StretchFull(shopRoot);
            Image shopBackdrop = shopRoot.AddComponent<Image>();
            shopBackdrop.color = new Color(0.04f, 0.045f, 0.05f, 0.86f);
            shopBackdrop.raycastTarget = false;

            Text shopTitle = BuildLabel(shopRoot, "Title", new Vector2(0f, -110f),
                TextAnchor.UpperCenter, new Vector2(0.5f, 1f), 40);
            shopTitle.rectTransform.sizeDelta = new Vector2(1200f, 60f);
            Text shopOffers = BuildLabel(shopRoot, "Offers", new Vector2(0f, -200f),
                TextAnchor.UpperLeft, new Vector2(0.5f, 1f), 30);
            shopOffers.rectTransform.sizeDelta = new Vector2(1100f, 460f);
            Text shopFooter = BuildLabel(shopRoot, "Footer", new Vector2(0f, 120f),
                TextAnchor.LowerCenter, new Vector2(0.5f, 0f), 28);
            shopFooter.rectTransform.sizeDelta = new Vector2(1100f, 60f);
            // The installed module list, in execution order — the stack IS the
            // build, so it gets its own line rather than being implied.
            Text shopLoadout = BuildLabel(shopRoot, "Loadout", new Vector2(0f, 190f),
                TextAnchor.LowerCenter, new Vector2(0.5f, 0f), 26);
            shopLoadout.rectTransform.sizeDelta = new Vector2(1100f, 50f);

            ShopPanel shop = canvasObject.AddComponent<ShopPanel>();
            SetRef(shop, "_runner", runner);
            SetRef(shop, "_run", run);
            SetRef(shop, "_root", shopRoot);
            SetRef(shop, "_titleLabel", shopTitle);
            SetRef(shop, "_offersLabel", shopOffers);
            SetRef(shop, "_footerLabel", shopFooter);
            SetRef(shop, "_loadoutLabel", shopLoadout);
            SetRef(shop, "_weapon", weapon);
            SetRef(shop, "_audio", audio);
            SetRef(shop, "_buyClip", LoadClip("Shop_Buy"));
            SetRef(shop, "_refusedClip", LoadClip("Shop_Refused"));

            // ---- game over ----
            GameObject overRoot = new("GameOverPanel", typeof(RectTransform));
            overRoot.transform.SetParent(canvasObject.transform, false);
            StretchFull(overRoot);
            Image overBackdrop = overRoot.AddComponent<Image>();
            overBackdrop.color = new Color(0.12f, 0.01f, 0.01f, 0.88f);
            overBackdrop.raycastTarget = false;

            Text overTitle = BuildLabel(overRoot, "Title", new Vector2(0f, 90f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), 72);
            overTitle.rectTransform.sizeDelta = new Vector2(900f, 100f);
            Text overDetail = BuildLabel(overRoot, "Detail", new Vector2(0f, -60f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), 34);
            overDetail.rectTransform.sizeDelta = new Vector2(900f, 220f);

            GameOverPanel gameOver = canvasObject.AddComponent<GameOverPanel>();
            SetRef(gameOver, "_runner", runner);
            SetRef(gameOver, "_run", run);
            SetRef(gameOver, "_root", overRoot);
            SetRef(gameOver, "_titleLabel", overTitle);
            SetRef(gameOver, "_detailLabel", overDetail);

            // Both panels start hidden; their components toggle them by phase.
            shopRoot.SetActive(false);
            overRoot.SetActive(false);
        }

        private static void StretchFull(GameObject target)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image BuildFullScreenImage(GameObject parent, string name, Color color)
        {
            GameObject overlay = new(name, typeof(RectTransform));
            overlay.transform.SetParent(parent.transform, false);
            Image image = overlay.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.enabled = false;
            RectTransform rect = overlay.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        private static Text BuildLabel(GameObject parent, string name, Vector2 position,
            TextAnchor alignment, Vector2 anchor) =>
            BuildLabel(parent, name, position, alignment, anchor, 34);

        private static Text BuildLabel(GameObject parent, string name, Vector2 position,
            TextAnchor alignment, Vector2 anchor, int fontSize)
        {
            GameObject label = new(name, typeof(RectTransform));
            label.transform.SetParent(parent.transform, false);
            Text text = label.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.92f, 0.92f, 0.85f);
            text.text = name;
            text.raycastTarget = false;

            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(320f, 48f);
            return text;
        }

        /// <summary>
        /// One menu screen: a full-screen backdrop with a title, a body and a
        /// footer. Both menus and the settings page are the same three labels, so
        /// they are built once here rather than three times with slightly
        /// different paddings.
        /// </summary>
        private readonly struct MenuScreen
        {
            public readonly GameObject Root;
            public readonly Text Title;
            public readonly Text Body;
            public readonly Text Footer;

            public MenuScreen(GameObject root, Text title, Text body, Text footer)
            {
                Root = root;
                Title = title;
                Body = body;
                Footer = footer;
            }
        }

        private static MenuScreen BuildMenuScreen(GameObject canvasObject, string name, Color backdrop,
            int titleSize, int bodySize)
        {
            GameObject root = new(name, typeof(RectTransform));
            root.transform.SetParent(canvasObject.transform, false);
            StretchFull(root);
            Image image = root.AddComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = false;

            Text title = BuildLabel(root, "Title", new Vector2(0f, -120f),
                TextAnchor.UpperCenter, new Vector2(0.5f, 1f), titleSize);
            title.rectTransform.sizeDelta = new Vector2(1400f, 110f);

            Text body = BuildLabel(root, "Body", new Vector2(0f, -300f),
                TextAnchor.UpperLeft, new Vector2(0.5f, 1f), bodySize);
            body.rectTransform.sizeDelta = new Vector2(1100f, 420f);

            Text footer = BuildLabel(root, "Footer", new Vector2(0f, 110f),
                TextAnchor.LowerCenter, new Vector2(0.5f, 0f), 26);
            footer.rectTransform.sizeDelta = new Vector2(1300f, 80f);

            root.SetActive(false);
            return new MenuScreen(root, title, body, footer);
        }

        /// <summary>
        /// Pause and the settings page it opens.
        ///
        /// Built AFTER the shop and game-over panels on purpose: uGUI draws
        /// siblings in hierarchy order, so a pause menu created earlier would be
        /// painted underneath the shop it is supposed to cover. The settings page
        /// is created last for the same reason.
        /// </summary>
        private static void BuildPauseUi(GameObject canvasObject, SettingsHub settingsHub,
            PlayerInput input, RunContext run, WaveRunner runner)
        {
            MenuScreen pause = BuildMenuScreen(canvasObject, "PausePanel",
                new Color(0.03f, 0.035f, 0.04f, 0.9f), 64, 34);
            MenuScreen settings = BuildMenuScreen(canvasObject, "SettingsPanel",
                new Color(0.03f, 0.035f, 0.04f, 0.94f), 48, 30);
            settings.Title.text = "SETTINGS";

            SettingsPanel settingsPanel = canvasObject.AddComponent<SettingsPanel>();
            SetRef(settingsPanel, "_settings", settingsHub);
            SetRef(settingsPanel, "_root", settings.Root);
            SetRef(settingsPanel, "_bodyLabel", settings.Body);
            SetRef(settingsPanel, "_footerLabel", settings.Footer);

            PausePanel pausePanel = canvasObject.AddComponent<PausePanel>();
            SetRef(pausePanel, "_root", pause.Root);
            SetRef(pausePanel, "_titleLabel", pause.Title);
            SetRef(pausePanel, "_bodyLabel", pause.Body);
            SetRef(pausePanel, "_footerLabel", pause.Footer);
            SetRef(pausePanel, "_settingsPanel", settingsPanel);
            SetRef(pausePanel, "_input", input);
            SetRef(pausePanel, "_runner", runner);
            SetRef(pausePanel, "_run", run);

            // Every other keyboard-driven panel has to know about pause, because
            // they share keys: SPACE is "next wave" in the shop and "confirm"
            // here, and R is "restart" on the death screen.
            ShopPanel? shop = canvasObject.GetComponent<ShopPanel>();
            if (shop != null) SetRef(shop, "_pause", pausePanel);
            GameOverPanel? gameOver = canvasObject.GetComponent<GameOverPanel>();
            if (gameOver != null) SetRef(gameOver, "_pause", pausePanel);
        }

        /// <summary>
        /// 20_MainMenu. Title, the record, Run vs Sandbox, settings, quit.
        ///
        /// A camera with an AudioListener even though nothing is rendered or
        /// heard: without them Unity logs "No cameras rendering" and "no audio
        /// listeners" every frame, which is noise in the one console this project
        /// keeps at zero warnings.
        /// </summary>
        private static void BuildMainMenuScene(GameConfig game, SettingsConfig settingsConfig, VolumeProfile postFx)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.055f, 0.065f);
            EnablePostProcessing(camera);
            cameraObject.AddComponent<AudioListener>();

            // The menu shares the arena's profile so the first thing the player
            // sees is graded like the game it launches. The Canvas is Screen Space
            // Overlay, which draws AFTER post, so the text stays crisp.
            BuildPostFx(postFx);

            SettingsHub settingsHub = new GameObject("Settings").AddComponent<SettingsHub>();
            SetRef(settingsHub, "_bounds", settingsConfig);
            SetRef(settingsHub, "_defaults", game);

            // The menu honours the graphics settings too — changing them here has
            // to show here, or the page reads as broken until a run starts.
            CameraGraphics menuGraphics = cameraObject.AddComponent<CameraGraphics>();
            SetRef(menuGraphics, "_settings", settingsHub);
            SetRef(menuGraphics, "_camera", camera);

            GameObject canvasObject = new("MenuCanvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            MenuScreen menu = BuildMenuScreen(canvasObject, "MainMenuPanel",
                new Color(0.05f, 0.055f, 0.065f, 1f), 80, 34);
            Text record = BuildLabel(menu.Root, "Record", new Vector2(0f, -230f),
                TextAnchor.UpperCenter, new Vector2(0.5f, 1f), 28);
            record.rectTransform.sizeDelta = new Vector2(1300f, 44f);

            MenuScreen settings = BuildMenuScreen(canvasObject, "SettingsPanel",
                new Color(0.05f, 0.055f, 0.065f, 1f), 48, 30);
            settings.Title.text = "SETTINGS";

            SettingsPanel settingsPanel = canvasObject.AddComponent<SettingsPanel>();
            SetRef(settingsPanel, "_settings", settingsHub);
            SetRef(settingsPanel, "_root", settings.Root);
            SetRef(settingsPanel, "_bodyLabel", settings.Body);
            SetRef(settingsPanel, "_footerLabel", settings.Footer);

            MainMenuPanel menuPanel = canvasObject.AddComponent<MainMenuPanel>();
            SetRef(menuPanel, "_settings", settingsHub);
            SetRef(menuPanel, "_root", menu.Root);
            SetRef(menuPanel, "_titleLabel", menu.Title);
            SetRef(menuPanel, "_recordLabel", record);
            SetRef(menuPanel, "_bodyLabel", menu.Body);
            SetRef(menuPanel, "_footerLabel", menu.Footer);
            SetRef(menuPanel, "_settingsPanel", settingsPanel);
            SetString(menuPanel, "_gameSceneName", "10_GreyBox");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        private static void BuildBootScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject boot = new("Boot");
            BootLoader loader = boot.AddComponent<BootLoader>();
            // Dormant unless the exe is launched with -codSmokeTest. It is what
            // lets a headless run prove the BUILT player boots, reaches the menu
            // and loads the arena — the one thing no editor gate can check.
            boot.AddComponent<BuildSmokeTest>();
            // Boot used to drop straight into the grey box. It now goes to the
            // menu, which is the only screen that can pick a mode.
            SetString(loader, "_firstScene", "20_MainMenu");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BootScenePath);
        }

        private static void RegisterScenes()
        {
            // Order matters: index 0 is what a built player loads first.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GreyBoxScenePath, true),
            };
        }

        // ---------- helpers ----------

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/_Project/Art", Materials, "Assets/_Project/Audio",
                "Assets/_Project/Data", DataGame, DataWeapons, DataDrones, DataAttacks,
                DataWaves, DataShop, DataPassives, DataEffects, Audio,
                Prefabs, Scenes,
            };
            foreach (string folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                int split = folder.LastIndexOf('/');
                AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
            }
        }

        /// <summary>
        /// Loads a placeholder clip and forces the import settings the playbook
        /// wants for short SFX: mono, uncompressed, decompressed on load. A
        /// gunshot decoded on the audio thread is a hitch in the one system where
        /// latency is most audible.
        /// </summary>
        private static AudioClip? LoadClip(string fileName)
        {
            string path = Audio + "/" + fileName + ".wav";
            AudioClip? clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"Missing placeholder clip '{path}'. Run: node Tools/make-placeholder-audio.mjs");
                return null;
            }

            if (AssetImporter.GetAtPath(path) is AudioImporter importer && !importer.forceToMono)
            {
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                importer.forceToMono = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            return clip;
        }

        private static T LoadOrCreate<T>(string path, System.Action<T> configure) where T : ScriptableObject
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                configure(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static Material LoadOrCreateMaterial(string path, Color color)
        {
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Surface response, re-applied on every build.
        ///
        /// Deliberately NOT folded into LoadOrCreateMaterial, which returns an
        /// existing material untouched so a value tuned in the Inspector survives
        /// a rebuild. These are shipped defaults being introduced rather than
        /// player-tuned numbers, so they are re-asserted the same way SetRef
        /// re-links a reference — otherwise the materials already on disk would
        /// keep the flat albedo-only look forever and the change would appear to
        /// do nothing.
        ///
        /// Everything here was pure _BaseColor before: no smoothness, no metallic,
        /// so every surface bounced light identically and the arena read as
        /// untextured primitives, because that is exactly what it was.
        /// </summary>
        private static void ApplySurface(Material material, float smoothness, float metallic)
        {
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// A material that actually glows. URP only reads _EmissionColor when the
        /// _EMISSION keyword is on, so a plain colour assignment produces a
        /// flat-looking drone core and the telegraph — which drives emission
        /// through a MaterialPropertyBlock — would do nothing visible.
        /// </summary>
        private static Material LoadOrCreateEmissiveMaterial(string path, Color color, float intensity)
        {
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * intensity);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// The post-processing stack, as one shared asset. Until this existed the
        /// project rendered with the image pipeline switched off entirely: the
        /// camera had no UniversalAdditionalCameraData, so renderPostProcessing was
        /// false, and the emissive drone cores — plus the emission ramp
        /// DroneController.SetTelegraph already drives through every attack windup —
        /// clipped flat instead of glowing. All of that was already paid for and
        /// none of it was visible.
        ///
        /// ONE profile for the arena AND the menu, deliberately: one place to tune,
        /// and a menu that looks like the game it launches.
        /// </summary>
        private static VolumeProfile LoadOrCreateVolumeProfile(string path)
        {
            VolumeProfile? existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) return existing;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            // Neutral, NOT ACES. ACES desaturates and hue-shifts reds, and every
            // threat in this game is read by the colour of its core through fog —
            // Rusher red, Shooter amber, Tank crimson. Filmic rolloff is worth
            // having; losing the palette that carries the readability is not.
            Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
            Override(tonemapping.mode, TonemappingMode.Neutral);

            // The one change that makes the existing emissive cores resolve.
            // High-quality filtering stays OFF: this targets a 4 GB 3050.
            Bloom bloom = AddOverride<Bloom>(profile);
            Override(bloom.threshold, 1.05f);
            Override(bloom.intensity, 0.35f);
            Override(bloom.scatter, 0.62f);
            Override(bloom.highQualityFiltering, false);

            Vignette vignette = AddOverride<Vignette>(profile);
            Override(vignette.intensity, 0.28f);
            Override(vignette.smoothness, 0.35f);

            // The grey/red tactical palette, pushed slightly.
            ColorAdjustments color = AddOverride<ColorAdjustments>(profile);
            Override(color.contrast, 8f);
            Override(color.saturation, -6f);

            // Nearly free, and it breaks up surfaces that carry no texture.
            FilmGrain grain = AddOverride<FilmGrain>(profile);
            Override(grain.type, FilmGrainLookup.Thin1);
            Override(grain.intensity, 0.15f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Adds a volume override AND persists it as a sub-asset of the profile.
        ///
        /// VolumeProfile.Add only puts the component in the in-memory list — the
        /// URP inspector is what normally calls AddObjectToAsset. Skip it from a
        /// script and the profile saves with a list of references to objects that
        /// were never written, which reads in the Inspector as an empty profile and
        /// at runtime as no post-processing at all. Same silent-null class as the
        /// scene asset references that VerifyAndRepair exists for.
        /// </summary>
        private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>();
            component.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>
        /// Sets a volume parameter AND flips its override flag. A VolumeParameter
        /// whose overrideState is false is ignored by the stack no matter what
        /// value it holds, so assigning without this does nothing and looks correct.
        /// </summary>
        private static void Override<T>(VolumeParameter<T> parameter, T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        private static GameObject SavePrefab(GameObject instance, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>
        /// Registers every pooled prefab with its prewarm count. Explicit pairs
        /// rather than a prefab array plus a parallel counts array: the old shape
        /// silently mismatched the moment a prefab was inserted in the middle, and
        /// a mis-sized pool only ever shows up as a hitch mid-wave.
        /// </summary>
        private static void SetPrewarm(ObjectPool pool, params (GameObject prefab, int count)[] entries)
        {
            SerializedObject serialized = new(pool);
            SerializedProperty array = serialized.FindProperty("_prewarm");
            array.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
                element.FindPropertyRelative("count").intValue = entries[i].count;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pool);
        }

        /// <summary>
        /// Inspector fields are [SerializeField] private on purpose — public
        /// fields are API and most of these are not — so wiring them from an
        /// editor script goes through SerializedObject rather than reflection.
        /// </summary>
        private static void SetRef(Object target, string field, Object? value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"No serialized field '{field}' on {target.GetType().Name}.", target);
                return;
            }
            property.objectReferenceValue = value;
            // ApplyModifiedProperties + SetDirty rather than ...WithoutUndo().
            // Note this is NOT sufficient on its own: assignments made while the
            // scene has never been saved still lose their ASSET references (scene
            // -object references survive, and nothing errors, so the first build
            // produced a scene that looked fine and did nothing on Play). What
            // actually guarantees the links is the VerifyAndRepair pass at the end
            // of Build(), which re-opens the saved scene and re-assigns anything
            // that did not survive. Both are kept: this is the cheap correct path,
            // that is the proof.
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        /// <summary>
        /// SetRef for a string field. Scene names are the only strings the builder
        /// has to write, and a private [SerializeField] is unreachable except
        /// through SerializedProperty.
        /// </summary>
        private static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"SetString: {target.GetType().Name} has no field '{field}'");
                return;
            }
            property.stringValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetArrayRef(Object target, string field, Object[] values)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"No serialized field '{field}' on {target.GetType().Name}.", target);
                return;
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }
}
