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
        private const string DataAttachments = "Assets/_Project/Data/Attachments";
        private const string DataDrones = "Assets/_Project/Data/Drones";
        private const string DataAttacks = "Assets/_Project/Data/Attacks";
        private const string DataWaves = "Assets/_Project/Data/Waves";
        private const string DataShop = "Assets/_Project/Data/Shop";
        private const string DataPassives = "Assets/_Project/Data/Passives";
        private const string DataEffects = "Assets/_Project/Data/Effects";
        private const string DataMissions = "Assets/_Project/Data/Missions";
        private const string DataKits = "Assets/_Project/Data/Kits";
        private const string Materials = "Assets/_Project/Art/Materials";
        private const string Textures = "Assets/_Project/Art/Textures";
        private const string Prefabs = "Assets/_Project/Prefabs";
        private const string Scenes = "Assets/_Project/Scenes";
        private const string Audio = "Assets/_Project/Audio";

        /// <summary>
        /// The layer the gun lives on, and the only layer the overlay camera
        /// draws. Declared in ProjectSettings/TagManager.asset (user slot 8) —
        /// a layer NAME is the only stable handle; the index is not, because
        /// anyone reordering that file would silently repoint everything.
        /// </summary>
        private const string ViewmodelLayerName = "Viewmodel";

        private const string GreyBoxScenePath = Scenes + "/10_GreyBox.unity";
        private const string BootScenePath = Scenes + "/00_Boot.unity";
        private const string MainMenuScenePath = Scenes + "/20_MainMenu.unity";
        private const string NavMeshPath = Scenes + "/NavMesh_GreyBox.asset";

        [MenuItem("CoD/Build Grey Box", false, 0)]
        public static void Build()
        {
            EnsureFolders();
            ArtImportPostprocessor.EnsurePresets();

            GameConfig game = LoadOrCreate<GameConfig>(DataGame + "/GameConfig.asset", ConfigureGame);
            SettingsConfig settings = LoadOrCreate<SettingsConfig>(DataGame + "/Settings.asset", ConfigureSettings);
            HealthConfig targetHealth = LoadOrCreate<HealthConfig>(DataGame + "/Health_Target.asset", h =>
            {
                h.maxHealth = 100f;
            });
            ImpactConfig impact = LoadOrCreate<ImpactConfig>(DataGame + "/Impact_Default.asset", _ => { });
            VolumeProfile postFx = LoadOrCreateVolumeProfile(DataGame + "/PostFx_Arena.asset");
            ArenaKitConfig arenaKit = LoadOrCreate<ArenaKitConfig>(DataKits + "/Kit_Arena_Default.asset", _ => { });
            WeaponKitConfig weaponKit = LoadOrCreate<WeaponKitConfig>(DataKits + "/Kit_Weapon_Default.asset", _ => { });
            EnemyKitConfig enemyKit = LoadOrCreate<EnemyKitConfig>(DataKits + "/Kit_Enemy_Default.asset", _ => { });
            AudioKitConfig audioKit = LoadOrCreate<AudioKitConfig>(DataKits + "/Kit_Audio_Default.asset", _ => { });
            RequireValidKits(arenaKit, weaponKit, enemyKit, audioKit);
            WeaponConfig rifle = LoadOrCreate<WeaponConfig>(DataWeapons + "/AR_Standard.asset", ConfigureRifle);
            WeaponConfig smg = LoadOrCreate<WeaponConfig>(DataWeapons + "/SMG_Rapid.asset", ConfigureSmg);
            PlayerLoadoutConfig loadout = LoadOrCreate<PlayerLoadoutConfig>(DataWeapons + "/Loadout_Default.asset", l =>
            {
                l.startingWeapon = rifle;
                l.weaponSlots = 2;
            });

            // ---- the palette ----------------------------------------------
            // Every colour in the arena comes from one asset now, and is
            // RE-ASSERTED on every build. See PaletteConfig for the drift this
            // kills: the literals that used to live right here were only ever
            // read on the day each .mat was created, so the "tactical palette"
            // change never reached disk and the floor shipped ~1.9x too bright
            // with every gate green.
            //
            // The configure callback is deliberately empty: PaletteConfig's own
            // field initialisers ARE the shipped defaults, which keeps one
            // number in one place instead of two that can disagree.
            PaletteConfig palette = LoadOrCreate<PaletteConfig>(DataGame + "/Palette_GreyBox.asset", _ => { });

            Material grey = LoadOrCreateMaterial(Materials + "/GreyBox_Floor.mat", palette.floor);
            Material wall = LoadOrCreateMaterial(Materials + "/GreyBox_Wall.mat", palette.wall);
            Material targetMat = LoadOrCreateMaterial(Materials + "/GreyBox_Target.mat", palette.practiceTarget);
            Material gunmetal = LoadOrCreateMaterial(Materials + "/Weapon_Body.mat", palette.weaponBody);
            Material gunAccent = LoadOrCreateMaterial(Materials + "/Weapon_Accent.mat", palette.weaponAccent);
            Material droneHull = LoadOrCreateMaterial(Materials + "/Drone_Hull.mat", palette.droneHull);

            // A shell casing is a physical object that bounces off the floor, so
            // it stays lit. Everything else that used to share this material is
            // LIGHT, not surface, and moves to the additive pair below.
            Material hot = LoadOrCreateMaterial(Materials + "/Fx_Hot.mat", palette.sparkHot);

            // A bullet hole is the one impact element that must be DARK. It was
            // sharing the muzzle-flash material, which painted a bright orange
            // dot on every wall the player shot.
            Material impactMark = LoadOrCreateMaterial(Materials + "/Fx_ImpactMark.mat", new Color(0.03f, 0.03f, 0.035f));

            ApplyPalette(grey, palette.floor);
            ApplyPalette(wall, palette.wall);
            ApplyPalette(targetMat, palette.practiceTarget);
            ApplyPalette(gunmetal, palette.weaponBody);
            ApplyPalette(gunAccent, palette.weaponAccent);
            ApplyPalette(droneHull, palette.droneHull);
            ApplyPalette(hot, palette.sparkHot);

            // Edge trim is COOL on purpose. Every threat in this game is read by
            // the colour of its core — Rusher red, Shooter amber, Tank crimson —
            // so nothing in the architecture is allowed to be warm and bright, or
            // the player learns to check a wall for danger. Cold light marks
            // places; warm light means something is trying to kill you.
            Material trim = LoadOrCreateEmissiveMaterial(Materials + "/Trim_Emissive.mat", palette.trim, palette.trimEmission);
            Material droneCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core.mat", palette.rusherCore, palette.rusherEmission);
            Material shooterCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core_Shooter.mat", palette.shooterCore, palette.shooterEmission);
            Material tankCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core_Tank.mat", palette.tankCore, palette.tankEmission);
            ApplyEmission(trim, palette.trim, palette.trimEmission);
            ApplyEmission(droneCore, palette.rusherCore, palette.rusherEmission);
            ApplyEmission(shooterCore, palette.shooterCore, palette.shooterEmission);
            ApplyEmission(tankCore, palette.tankCore, palette.tankEmission);

            // ---- VFX materials --------------------------------------------
            // Every particle system in the project used to share ONE OPAQUE Lit
            // material, and the sparks system had no material assigned at all —
            // so every bullet impact rendered Unity's magenta error particles,
            // and nothing that was supposed to glow blended or glowed. Additive
            // transparent is what makes a spark read as light instead of as a
            // small orange brick.
            Material sparkFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Spark.mat", palette.sparkHot);
            Material fireFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Fire.mat", palette.fire);
            ApplyParticleSurface(sparkFx, palette.sparkHot);
            ApplyParticleSurface(fireFx, palette.fire);

            // Surfaces, re-asserted on every build. LoadOrCreateMaterial returns
            // an existing material untouched, which is right for values a human
            // tuned — but these are shipped defaults being introduced, so they are
            // applied the same way SetRef re-links a reference.
            // One shared 1024 detail normal for the whole box. Generated once and
            // reused; see EnsureDetailNormal for why it is never rewritten.
            //
            // Tiling differs per material and cannot be per-object: AddBox scales
            // cubes, and a scaled cube's UVs stay 0..1 per face, so every wall
            // shares one material and therefore one tiling. Close enough for a
            // detail map, and the alternative is a material per block.
            Texture2D? detail = EnsureDetailNormal();
            ApplySurface(grey, smoothness: 0.28f, metallic: 0.0f, detail, tiling: 24f, normalScale: 0.8f);
            ApplySurface(wall, smoothness: 0.18f, metallic: 0.0f, detail, tiling: 10f, normalScale: 0.6f);
            ApplySurface(targetMat, smoothness: 0.35f, metallic: 0.1f);
            ApplySurface(gunmetal, smoothness: 0.62f, metallic: 0.85f);
            ApplySurface(gunAccent, smoothness: 0.45f, metallic: 0.70f);
            ApplySurface(impactMark, smoothness: 0.1f, metallic: 0.0f);
            // Metallic and fairly smooth: a hull that catches a highlight reads as
            // a machine, and it is what makes the dark body legible at all against
            // a dark floor once the glowing core stops being the only lit pixel.
            ApplySurface(droneHull, smoothness: 0.55f, metallic: 0.75f);

            GameObject decal = BuildDecalPrefab(impactMark);
            GameObject sparks = BuildSparksPrefab(sparkFx);
            GameObject flash = BuildMuzzleFlashPrefab(sparkFx);
            GameObject casing = BuildCasingPrefab(hot);
            GameObject dummy = BuildDummyTargetPrefab(targetMat, targetHealth);

            GameObject explosion = BuildExplosionPrefab(fireFx, audioKit);
            GameObject droneDeath = BuildDroneDeathPrefab(fireFx, audioKit);
            GameObject slamVfx = BuildSlamPrefab(fireFx, audioKit);
            GameObject projectile = BuildDroneProjectilePrefab(shooterCore);

            GameObject rusherPrefab = BuildDronePrefab("Drone_Rusher", DroneShape.Rusher, droneHull, droneCore, enemyKit);
            GameObject shooterPrefab = BuildDronePrefab("Drone_Shooter", DroneShape.Shooter, droneHull, shooterCore, enemyKit);
            GameObject tankPrefab = BuildDronePrefab("Drone_Tank", DroneShape.Tank, droneHull, tankCore, enemyKit);

            DifficultyConfig difficulty = LoadOrCreate<DifficultyConfig>(DataGame + "/Difficulty.asset", ConfigureDifficulty);

            ContactDetonate detonate = LoadOrCreate<ContactDetonate>(
                DataAttacks + "/ContactDetonate_Std.asset", ConfigureContactDetonate);
            SetRef(detonate, "explosionVfx", explosion);
            SetRef(detonate, "alertClip", Prefer(audioKit.droneAlert, "Drone_Alert"));
            EditorUtility.SetDirty(detonate);

            RangedBurst rangedBurst = LoadOrCreate<RangedBurst>(
                DataAttacks + "/RangedBurst_Std.asset", ConfigureRangedBurst);
            SetRef(rangedBurst, "projectilePrefab", projectile);
            SetRef(rangedBurst, "fireClip", Prefer(audioKit.droneShot, "Drone_Shot"));
            EditorUtility.SetDirty(rangedBurst);

            HeavySlam heavySlam = LoadOrCreate<HeavySlam>(
                DataAttacks + "/HeavySlam_Std.asset", ConfigureHeavySlam);
            SetRef(heavySlam, "slamVfx", slamVfx);
            SetRef(heavySlam, "windupClip", Prefer(audioKit.slamWindup, "Slam_Windup"));
            EditorUtility.SetDirty(heavySlam);

            EnemyReactionConfig reactions = LoadOrCreate<EnemyReactionConfig>(
                DataDrones + "/Reactions_Drone_Standard.asset", ConfigureEnemyReactions);

            DroneConfig rusher = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Rusher.asset", ConfigureRusher);
            SetRef(rusher, "prefab", rusherPrefab);
            SetRef(rusher, "attack", detonate);
            SetRef(rusher, "reactions", reactions);
            SetRef(rusher, "deathVfx", droneDeath);
            EditorUtility.SetDirty(rusher);

            DroneConfig shooter = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Shooter.asset", ConfigureShooter);
            SetRef(shooter, "prefab", shooterPrefab);
            SetRef(shooter, "attack", rangedBurst);
            SetRef(shooter, "reactions", reactions);
            SetRef(shooter, "deathVfx", droneDeath);
            EditorUtility.SetDirty(shooter);

            DroneConfig tank = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Tank.asset", ConfigureTank);
            SetRef(tank, "prefab", tankPrefab);
            SetRef(tank, "attack", heavySlam);
            SetRef(tank, "reactions", reactions);
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

            // ---- the mission layer ----------------------------------------
            // Three assets and one prefab, and every one of them is inert until a
            // save says campaign. Endless mode gets the same scene with the same
            // components in it and does not notice: MissionDirector disables
            // itself in Awake, the catalog it would read is empty, and nothing
            // ever spawns an interact point.
            //
            // The configure callbacks are deliberately empty, exactly like
            // PaletteConfig's: the field initialisers on InteractionConfig ARE the
            // shipped defaults, and a second copy of those three numbers here is
            // one more place for them to disagree with the asset a human tuned.
            InteractionConfig interaction = LoadOrCreate<InteractionConfig>(
                DataGame + "/Interaction_Default.asset", _ => { });

            // Left EMPTY on purpose. The missions themselves are authored assets;
            // this only has to EXIST, so the menu has something to read and the
            // director has something to resolve a saved mission id against. An
            // empty catalog is a campaign with no missions, which is exactly what
            // this project has until they are written — and MissionSelectPanel
            // already prints "NO MISSIONS AUTHORED YET" for it.
            MissionCatalog missionCatalog = LoadOrCreate<MissionCatalog>(
                DataMissions + "/Missions.asset", _ => { });

            GameObject interactPoint = BuildInteractPointPrefab(beacon, audioKit);
            var missionAssets = new MissionAssets(missionCatalog, interaction, interactPoint);

            SetRef(impact, "decalPrefab", decal);
            SetRef(impact, "particlePrefab", sparks);
            EditorUtility.SetDirty(impact);

            SetRef(smg, "muzzleFlashPrefab", flash);
            SetRef(smg, "shellCasingPrefab", casing);
            SetRef(smg, "fireCloseLayer", Prefer(audioKit.rifleClose, "Fire_AR_Close"));
            SetRef(smg, "fireTailLayer", Prefer(audioKit.rifleTail, "Fire_AR_Tail"));
            SetRef(smg, "dryFireClip", LoadClip("DryFire"));
            SetRef(smg, "reloadClip", Prefer(audioKit.rifleReload, "Reload_AR"));
            EditorUtility.SetDirty(smg);

            SetRef(rifle, "muzzleFlashPrefab", flash);
            SetRef(rifle, "shellCasingPrefab", casing);
            SetRef(rifle, "fireCloseLayer", Prefer(audioKit.rifleClose, "Fire_AR_Close"));
            SetRef(rifle, "fireTailLayer", Prefer(audioKit.rifleTail, "Fire_AR_Tail"));
            SetRef(rifle, "dryFireClip", LoadClip("DryFire"));
            SetRef(rifle, "reloadClip", Prefer(audioKit.rifleReload, "Reload_AR"));
            EditorUtility.SetDirty(rifle);

            BuildGreyBoxScene(game, settings, loadout, impact, grey, wall, targetMat, gunmetal, gunAccent,
                dummy, decal, sparks, flash, casing, drones, runAssets, missionAssets, postFx, trim, palette,
                arenaKit, weaponKit, audioKit);
            BuildMainMenuScene(game, settings, missionCatalog, postFx);
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

        /// <summary>The mission layer's assets, grouped for the same reason the two above are.</summary>
        private readonly struct MissionAssets
        {
            /// <summary>Every mission there is. Empty until they are authored, and legal empty.</summary>
            public readonly MissionCatalog Catalog;

            /// <summary>Range, facing cone, hold decay. The only three numbers interaction has.</summary>
            public readonly InteractionConfig Interaction;

            /// <summary>Pooled, never placed by this builder — a mission decides where and when.</summary>
            public readonly GameObject InteractPointPrefab;

            public MissionAssets(MissionCatalog catalog, InteractionConfig interaction,
                GameObject interactPointPrefab)
            {
                Catalog = catalog;
                Interaction = interaction;
                InteractPointPrefab = interactPointPrefab;
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

        private static void ConfigureEnemyReactions(EnemyReactionConfig config)
        {
            config.sightSampleInterval = 0.25f;
            config.detectionRange = 24f;
            config.lostSightSeconds = 1.2f;
            config.sightMask = Physics.DefaultRaycastLayers;
            config.heavyDamageFraction = 0.28f;
            config.lowHealthFraction = 0.25f;
            config.allyDeathRadius = 9f;
            config.responses = new[]
            {
                Reaction(EnemyReactionKind.DetectPlayer, 0.62f, 8f, 0.35f, 0.16f),
                Reaction(EnemyReactionKind.NearbyAllyDeath, 0.38f, 5f, 0.42f, 0.18f),
                Reaction(EnemyReactionKind.HeavyDamage, 0.72f, 2.5f, 0.65f, 0.12f),
                Reaction(EnemyReactionKind.LostSight, 0.45f, 6f, 0.28f, 0.22f),
                Reaction(EnemyReactionKind.AttackCommit, 0.58f, 2f, 0.52f, 0.14f),
                Reaction(EnemyReactionKind.LowHealth, 0.80f, 30f, 0.78f, 0.24f),
            };
        }

        private static EnemyReactionResponse Reaction(EnemyReactionKind kind, float probability,
            float cooldownSeconds, float corePulse, float pulseSeconds)
            => new()
            {
                kind = kind,
                probability = probability,
                cooldownSeconds = cooldownSeconds,
                corePulse = corePulse,
                pulseSeconds = pulseSeconds,
                cue = null,
            };

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

        private static GameObject BuildSparksPrefab(Material material)
        {
            GameObject root = new("Fx_ImpactSparks");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            // The defect this parameter exists for: this renderer had NO material
            // at all, so every bullet impact in the game rendered Unity default
            // magenta. Nothing failed; it just looked broken.
            root.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

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

        /// <summary>
        /// The flash belongs to the GUN, so it lives on the viewmodel layer.
        ///
        /// It spawns at the muzzle — 0.8 m in front of the lens, inside the wall
        /// the player is standing against — and the world camera's 0.05 near
        /// plane does not save it. It is also the one pooled effect whose
        /// position is only meaningful against the viewmodel's projection: drawn
        /// by the world camera at a different FOV it would sit off the barrel tip.
        /// The layer goes on the PREFAB rather than at spawn time, so the pool
        /// stays a dumb spawner and WeaponController never learns about layers.
        ///
        /// Fx_ShellCasing deliberately does NOT get this treatment — it has a
        /// Rigidbody and has to bounce off the real floor.
        ///
        /// IT ALSO CARRIES THE HALF OF THE MUZZLE FLASH THAT LIGHTS THE GUN.
        /// A camera's culling mask culls LIGHTS by the light's GameObject layer,
        /// not only renderers, and the two cameras have disjoint masks — so no
        /// single light can reach both the room and the viewmodel. Splitting the
        /// rig onto its own layer therefore silently cost the gun its muzzle
        /// flash: the world MuzzleLight under the Muzzle transform stayed on
        /// Default (correct — it lights the room and the drones) and became
        /// invisible to the only camera that draws the weapon. On the most
        /// repeated visual event in the game.
        ///
        /// WHY THE SECOND LIGHT LIVES HERE AND NOT UNDER THE MUZZLE
        /// WeaponController drives exactly one serialized Light, by toggling
        /// `Behaviour.enabled` — and `enabled` is per COMPONENT: it does not
        /// cascade to children, so a second Light parented under MuzzleLight would
        /// simply burn permanently. Giving the gun its own flash light from under
        /// the Muzzle transform therefore needs a second serialized field on
        /// WeaponController. The pool already provides the identical lifetime for
        /// free: this prefab is spawned by SpawnMuzzleEffects on the same shot and
        /// despawned by the pool after `muzzleFlashLifetime`, so the light and the
        /// flash sprite are one object and can never desync.
        ///
        /// Range and intensity are scene construction, like BuildArenaLights — the
        /// light only ever reaches eight cubes half a metre away, so a wide range
        /// buys nothing. NOT Light.cullingMask: URP ignores per-light culling
        /// masks (it uses rendering layers), and the camera mask above is what
        /// actually keeps this off the world.
        /// </summary>
        private static GameObject BuildMuzzleFlashPrefab(Material material)
        {
            GameObject root = new("Fx_MuzzleFlash");
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(root.transform, false);
            quad.transform.localScale = Vector3.one * 0.22f;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            // NO LIGHT ON THIS PREFAB, deliberately. It is pooled, so its
            // lifetime is muzzleFlashLifetime — the SPRITE's number, which is
            // allowed to be long because overlapping sprites look fine. A light
            // inheriting it does not: at the SMG's 900 rpm there are 0.0667 s
            // between shots and an 0.08 s light never goes out, so sustained
            // fire put the viewmodel under a continuous glow while the room
            // strobed correctly. The gun's flash light lives on the rig instead,
            // on WeaponController's own muzzleLightDuration clock, beside the
            // world one. See UpdateMuzzleLight.
            root.AddComponent<PooledObject>();
            SetLayerRecursive(root, RequireViewmodelLayer());
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

        /// <summary>
        /// A thing a mission can put in the arena for the player to use: a
        /// terminal, a charge site, an extract pad, a data pad, a door. One prefab
        /// for all of them, because the difference is DATA — kind, prompt, hold
        /// length — and InteractPoint carries all of it as serialized fields.
        ///
        /// POOLED, and deliberately never placed in the scene by this builder. An
        /// interact point that exists from the first frame of every run is one the
        /// player can use before the objective that wants it, and one that costs
        /// endless mode a prompt it has no business showing.
        ///
        /// NO COLLIDERS ANYWHERE ON IT, for the reason the repair beacon's pad
        /// destroys its own: this sits on the floor the player walks across, and a
        /// collider there either blocks movement or — worse, because it looks like
        /// a weapon bug rather than a level bug — eats the aim ray that was meant
        /// for the drone standing behind it.
        ///
        /// _registry is NOT wired here, and cannot be: it points at a scene
        /// object, and a prefab asset can only hold references to assets. Whatever
        /// spawns one of these has to hand it the scene's InteractableRegistry, or
        /// it registers with nothing and the prompt never appears.
        /// </summary>
        private static GameObject BuildInteractPointPrefab(Material material, AudioKitConfig audioKit)
        {
            GameObject root = new("Interact_Point");

            // The visual is a CHILD rather than the root: InteractPoint hides this
            // once the point is spent, and hiding the root would take the
            // component, its OnDisable and the pooled instance's own bookkeeping
            // down with it.
            GameObject visual = new("Visual");
            visual.transform.SetParent(root.transform, false);

            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            pad.transform.SetParent(visual.transform, false);
            // A cylinder primitive is 2 units tall and 1 across, so x/z IS the
            // diameter and y is halved twice over — same arithmetic as the beacon.
            pad.transform.localScale = new Vector3(1.3f, 0.02f, 1.3f);
            pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            Object.DestroyImmediate(pad.GetComponent<Collider>());

            MeshRenderer padRenderer = pad.GetComponent<MeshRenderer>();
            padRenderer.sharedMaterial = material;
            padRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // A post, so the thing is findable from across the arena instead of
            // only from standing on top of it. Green, and sharing the beacon's
            // material rather than minting a second one: green is this game's
            // "this is for you" hue, nothing else is allowed to use it, and two
            // green materials are two things that can drift apart.
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(visual.transform, false);
            post.transform.localScale = new Vector3(0.12f, 1.1f, 0.12f);
            post.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            Object.DestroyImmediate(post.GetComponent<Collider>());

            MeshRenderer postRenderer = post.GetComponent<MeshRenderer>();
            postRenderer.sharedMaterial = material;
            postRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // Its own AudioSource, for the reason the explosion has one: the thing
            // being used may hide itself in the same frame, and a clip played on a
            // renderer that just went away is a clip nobody hears.
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 30f;
            audio.rolloffMode = AudioRolloffMode.Linear;

            root.AddComponent<PooledObject>();

            InteractPoint point = root.AddComponent<InteractPoint>();
            SetRef(point, "_audio", audio);
            SetRef(point, "_visual", visual);
            // The shop's confirm blip, standing in until there is a dedicated one.
            // A silent interaction reads as a REFUSED interaction, which is the
            // one thing the feedback here has to rule out.
            SetRef(point, "_useClip", Prefer(audioKit.confirm, "Shop_Buy"));

            return SavePrefab(root, Prefabs + "/Interact_Point.prefab");
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
        private static GameObject BuildDronePrefab(string name, DroneShape shape, Material hull, Material core,
            EnemyKitConfig kit)
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
            GameObject? importedPrefab = shape switch
            {
                DroneShape.Shooter => kit.shooterPrefab,
                DroneShape.Tank => kit.tankPrefab,
                _ => kit.rusherPrefab,
            };

            Renderer hullRenderer;
            if (importedPrefab == null)
            {
                MeshRenderer primitiveRenderer = root.GetComponent<MeshRenderer>();
                primitiveRenderer.sharedMaterial = hull;
                hullRenderer = primitiveRenderer;
            }
            else
            {
                Material importedMaterial = kit.hullMaterial ?? throw new System.InvalidOperationException(
                    $"Enemy kit '{kit.name}' has a prefab but no hull material.");
                Object.DestroyImmediate(root.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(root.GetComponent<MeshFilter>());
                hullRenderer = AddArtChild(root, importedPrefab, importedMaterial, disableShadows: false);
            }

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

            if (importedPrefab == null) AddShapeDetails(root, shape, hull, bodyScale);

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
        /// The Shooter's round. No collider — CoD.Core.Projectile sweeps a ray
        /// between frames instead, because a small fast trigger tunnels through
        /// walls at any sane physics step.
        ///
        /// The component used to be CoD.Enemies.DroneProjectile and was promoted
        /// to Core so the player's launcher could fire the same object. The prefab
        /// keeps its path and its guid: the SCRIPT file kept the old .meta through
        /// the move, so this prefab's script reference resolved to the new type
        /// without a repair pass and without a scene touching it.
        /// </summary>
        private static GameObject BuildDroneProjectilePrefab(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Fx_DroneProjectile";
            root.transform.localScale = new Vector3(0.09f, 0.09f, 0.34f);
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(root.GetComponent<Collider>());

            root.AddComponent<PooledObject>();
            Projectile projectile = root.AddComponent<Projectile>();
            SetRef(projectile, "_pooled", root.GetComponent<PooledObject>());

            return SavePrefab(root, Prefabs + "/Fx_DroneProjectile.prefab");
        }

        /// <summary>The Tank's slam landing: a flat outward burst, so the radius it covers is visible.</summary>
        private static GameObject BuildSlamPrefab(Material material, AudioKitConfig audioKit)
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
            audio.clip = Prefer(audioKit.explosion, "Slam_Hit");

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_Slam.prefab");
        }

        /// <summary>
        /// The detonation. Carries its own AudioSource because the drone that set
        /// it off deactivates in the same frame — a clip played on the drone would
        /// be cut off mid-bang.
        /// </summary>
        private static GameObject BuildExplosionPrefab(Material material, AudioKitConfig audioKit)
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
            audio.clip = Prefer(audioKit.explosion, "Explosion");

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_Explosion.prefab");
        }

        /// <summary>
        /// Shot down, as opposed to detonated. Deliberately smaller and quieter
        /// than the explosion so "I killed it" and "it got me" never look or sound
        /// the same.
        /// </summary>
        private static GameObject BuildDroneDeathPrefab(Material material, AudioKitConfig audioKit)
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
            audio.clip = Prefer(audioKit.droneDeath, "Drone_Death");

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/Fx_DroneDeath.prefab");
        }

        // ---------- scenes ----------

        private static void BuildGreyBoxScene(GameConfig game, SettingsConfig settingsConfig,
            PlayerLoadoutConfig loadout, ImpactConfig impact,
            Material floorMat, Material wallMat, Material targetMat, Material gunmetal, Material gunAccent,
            GameObject dummyPrefab, GameObject decal, GameObject sparks, GameObject flash, GameObject casing,
            DroneAssets drones, RunAssets runAssets, MissionAssets missions, VolumeProfile postFx,
            Material trimMat, PaletteConfig palette, ArenaKitConfig arenaKit, WeaponKitConfig weaponKit,
            AudioKitConfig audioKit)
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
            RenderSettings.ambientSkyColor = palette.ambientSky;
            RenderSettings.ambientEquatorColor = palette.ambientEquator;
            RenderSettings.ambientGroundColor = palette.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = palette.fogColor;
            RenderSettings.fogStartDistance = palette.fogStart;
            RenderSettings.fogEndDistance = palette.fogEnd;

            // A DARK INTERIOR ENVIRONMENT, and it has to be an environment
            // rather than nothing at all.
            //
            // The first attempt at this nulled the skybox and set the
            // reflection mode to Custom with a null texture. That correctly
            // stopped a sealed underground arena reflecting a bright blue
            // procedural sky -- and replaced it with something just as wrong in
            // the other direction. A Custom reflection of NOTHING is a
            // reflection of BLACK, so every metal in the game went matte black:
            // Weapon_Body at metallic 0.85 became a silhouette, an unlit hole in
            // the middle of the screen, on the one object that is visible in
            // every single frame. And with no skybox the camera fell through to
            // its own background colour, which is Unity default BLUE -- so the
            // "sealed facility" still had bright blue sky over its walls.
            //
            // Both failures were invisible to every gate and were found by
            // rendering a frame and looking at it.
            //
            // One dim procedural sky fixes both: it darkens what sits above the
            // walls, AND it gives metal something plausible and dim to reflect.
            // Procedural rather than a cubemap because it needs no texture -- no
            // import settings, no LFS object, no VRAM.
            Material interiorSky = LoadOrCreateSkybox(Materials + "/Sky_Interior.mat", palette);
            ApplyInteriorSky(interiorSky, palette);
            RenderSettings.skybox = interiorSky;
            if (arenaKit.reflectionCubemap == null)
            {
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                RenderSettings.customReflectionTexture = null;
                RenderSettings.reflectionIntensity = 0.45f;
            }
            else
            {
                // The cubemap is reflection DATA, not the visible sky. Showing a
                // photographed garage above these walls would reopen the sealed-
                // bunker bug the procedural sky exists to prevent.
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = arenaKit.reflectionCubemap;
                RenderSettings.reflectionIntensity = arenaKit.reflectionIntensity;
            }
            RenderSettings.ambientIntensity = 1f;

            BuildPostFx(postFx);

            GameObject room = BuildRoom(floorMat, wallMat, trimMat, arenaKit);
            BakeNavMesh(room);
            BuildArenaLights();

            ObjectPool pool = new GameObject("ObjectPool").AddComponent<ObjectPool>();
            // Counts are sized for a full wave, not for the demo: the pool exists
            // so the first shot of round twelve costs the same as the first shot
            // of round one.
            //
            // RE-SIZED FOR THE REAL ARSENAL, and the old numbers were a trap
            // hiding behind a true statement. Adding weapons needed no new pool
            // ENTRY KINDS, which is easy to mistake for needing no pool change at
            // all -- but every count here was sized around one weapon: the AR at
            // 700 rpm, a 30-round magazine, one pellet per shot.
            //
            // The LMG fires 750 rpm out of a 100-round magazine, so at the shipped
            // 3 s casing lifetime it holds ~38 casings alive against 24 prewarmed.
            // The shotgun is worse: twelve impacts PER PULL against a 20 s decal
            // lifetime, so one six-round magazine wants ~72 decals against 48.
            // Every instance past the prewarm is a runtime Instantiate on the
            // firing path -- the exact GC hitch the pool exists to prevent, on the
            // exact path it exists to protect -- and ObjectPool's leak warning
            // sits at 512, so none of it would have reported anything.
            var prewarm = new List<(GameObject prefab, int count)>
            {
                (decal, 96), (sparks, 32), (flash, 4), (casing, 48), (dummyPrefab, 8),
                // Eight is a mission's worth of terminals, charges and pads with
                // room to spare. They cost nothing in endless mode — a prewarmed
                // instance is an inactive GameObject with no Update.
                (missions.InteractPointPrefab, 8),
            };
            prewarm.AddRange(drones.Pooled);
            AddVfxPrewarm(prewarm);
            SetPrewarm(pool, prewarm.ToArray());

            // Beside the pool, and the same shape as DroneRegistry for the same
            // reason: Domain Reload is off, so a static list would still hold the
            // previous Play session's destroyed objects. Interactables put
            // themselves in it when they turn on, which is what keeps
            // PlayerInteractor from running a scene search every frame.
            InteractableRegistry interactables =
                new GameObject("Interactables").AddComponent<InteractableRegistry>();

            // The run layer is created BEFORE the player, because the player's
            // motor and weapon subscribe to its StatsChanged event and a
            // serialized reference cannot point at an object that does not exist
            // yet. Its own back-references are filled in once the player does.
            GameObject runObject = new("Run");
            RunContext run = runObject.AddComponent<RunContext>();
            WaveRunner runner = runObject.AddComponent<WaveRunner>();
            // The campaign, on the same object as the run it drives. Its Awake
            // disables itself unless the save says campaign, and Unity skips
            // OnEnable entirely for a component disabled during its own Awake — so
            // in endless mode this subscribes to nothing, ticks nothing and
            // touches the runner not at all. Component ORDER on this object is
            // irrelevant: Unity does not promise Awake order, only that every
            // Awake lands before any Start, and that is the guarantee
            // MissionDirector.Suspend relies on.
            MissionDirector director = runObject.AddComponent<MissionDirector>();
            AudioSource radioAudio = runObject.AddComponent<AudioSource>();
            radioAudio.playOnAwake = false;
            radioAudio.loop = false;
            radioAudio.spatialBlend = 0f;
            RadioDialogueScheduler radio = runObject.AddComponent<RadioDialogueScheduler>();
            SetRef(radio, "_audio", radioAudio);
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
                BuildPlayerRig(game, loadout, impact, pool, gunmetal, gunAccent, run, settingsHub, palette,
                    weaponKit);

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

            // The mission layer's five references, every one of them resolvable
            // only now: the runner needs its spawner, the director needs the drone
            // registry to count kills, and both need a player that did not exist
            // when the Run object was created.
            SetRef(director, "_run", run);
            SetRef(director, "_runner", runner);
            SetRef(director, "_catalog", missions.Catalog);
            SetRef(director, "_registry", registry);
            SetRef(director, "_player", playerTransform);
            SetRef(director, "_playerHealth", playerHealth);
            SetRef(director, "_radio", radio);
            SetRef(director, "_settings", settingsHub);
            SetRef(director, "_interactables", interactables);
            BuildMissionZones(director);

            // The player's input component, so pause can switch the whole action
            // map off. GetComponent is fine here — the guard bans it inside
            // Update/FixedUpdate/LateUpdate, and this is editor build code.
            PlayerInput playerInput = playerTransform.GetComponent<PlayerInput>();

            // Interaction lives on the Player but is wired HERE rather than inside
            // BuildPlayerRig, because three of its five references — the registry,
            // the interaction config and the player's own input handle — belong to
            // the scene rather than to the rig. Threading them down would cost two
            // more parameters and a seventh element on a tuple that is already at
            // the edge of readable.
            PlayerInteractor interactor = playerTransform.gameObject.AddComponent<PlayerInteractor>();
            SetRef(interactor, "_config", missions.Interaction);
            SetRef(interactor, "_registry", interactables);
            SetRef(interactor, "_input", playerInput);
            SetRef(interactor, "_look", look);
            SetRef(interactor, "_health", playerHealth);

            BuildObjective(runAssets, runner, playerTransform, playerHealth);

            BuildHud(weapon, playerHealth, game, pool, dummyPrefab, muzzle, spawner, registry, cameraTransform,
                run, runner, settingsHub, playerInput, director, interactor, radio, audioKit);

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

        private static GameObject BuildRoom(Material floorMat, Material wallMat, Material trimMat,
            ArenaKitConfig kit)
        {
            GameObject room = new("Room");

            AddBlock(room, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f),
                floorMat, kit.floorModule, kit.floorMaterial);

            AddArenaBlock(room, "Wall_N", new Vector3(0f, 2.5f, 20f), new Vector3(40f, 5f, 0.5f), wallMat, kit);
            AddArenaBlock(room, "Wall_S", new Vector3(0f, 2.5f, -20f), new Vector3(40f, 5f, 0.5f), wallMat, kit);
            AddArenaBlock(room, "Wall_E", new Vector3(20f, 2.5f, 0f), new Vector3(0.5f, 5f, 40f), wallMat, kit);
            AddArenaBlock(room, "Wall_W", new Vector3(-20f, 2.5f, 0f), new Vector3(0.5f, 5f, 40f), wallMat, kit);

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
            AddArenaBlock(room, "Core_Bunker", new Vector3(0f, 1.5f, 2f), new Vector3(8f, 3f, 6f), wallMat, kit);

            // Lane dividers, with a deliberate 7 m crossing gap between each pair:
            // wide enough that a Tank fits, narrow enough to be a decision.
            AddArenaBlock(room, "Divider_W_South", new Vector3(-9f, 1.5f, -6f), new Vector3(1f, 3f, 10f), wallMat, kit);
            AddArenaBlock(room, "Divider_E_South", new Vector3(9f, 1.5f, -6f), new Vector3(1f, 3f, 10f), wallMat, kit);
            AddArenaBlock(room, "Divider_W_North", new Vector3(-9f, 1.5f, 11f), new Vector3(1f, 3f, 8f), wallMat, kit);
            AddArenaBlock(room, "Divider_E_North", new Vector3(9f, 1.5f, 11f), new Vector3(1f, 3f, 8f), wallMat, kit);

            // Shoot-over cover. The south block is in front of the player spawn on
            // purpose: the first thing you learn is that you can back behind it.
            AddArenaBlock(room, "Cover_S", new Vector3(0f, 0.6f, -10f), new Vector3(6f, 1.2f, 1f), wallMat, kit);
            AddArenaBlock(room, "Cover_W", new Vector3(-14f, 0.6f, 4f), new Vector3(4f, 1.2f, 1f), wallMat, kit);
            AddArenaBlock(room, "Cover_E", new Vector3(14f, 0.6f, 4f), new Vector3(4f, 1.2f, 1f), wallMat, kit);
            AddArenaBlock(room, "Cover_NW", new Vector3(-5f, 0.6f, 14f), new Vector3(1f, 1.2f, 5f), wallMat, kit);
            AddArenaBlock(room, "Cover_NE", new Vector3(5f, 0.6f, 14f), new Vector3(1f, 1.2f, 5f), wallMat, kit);

            // Corner pillars: they stop the perimeter from being a free racetrack
            // and give a kiting Shooter somewhere to be forced out of.
            AddArenaBlock(room, "Pillar_NW", new Vector3(-16f, 2f, 16f), new Vector3(2f, 4f, 2f), wallMat, kit);
            AddArenaBlock(room, "Pillar_NE", new Vector3(16f, 2f, 16f), new Vector3(2f, 4f, 2f), wallMat, kit);
            AddArenaBlock(room, "Pillar_SW", new Vector3(-16f, 2f, -16f), new Vector3(2f, 4f, 2f), wallMat, kit);
            AddArenaBlock(room, "Pillar_SE", new Vector3(16f, 2f, -16f), new Vector3(2f, 4f, 2f), wallMat, kit);

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
            BuildMissionOneStoryCorner(room, wallMat, trimMat);
            return room;
        }

        /// <summary>
        /// One restrained environmental sentence: somebody dragged equipment
        /// behind a damaged workstation and tried to hold the west service lane.
        /// Every piece is presentation-only. No collider means no NavMesh input,
        /// no aim-ray blocker, and no gameplay change when this vignette moves.
        /// </summary>
        private static void BuildMissionOneStoryCorner(GameObject room, Material equipment, Material screen)
        {
            GameObject root = new("StoryCorner_LastStand");
            root.transform.SetParent(room.transform, false);

            AddStoryProp(root, "Workstation_Base", new Vector3(-18.1f, 0.55f, -5.8f),
                new Vector3(0.7f, 1.1f, 2.5f), new Vector3(0f, -7f, 0f), equipment);
            AddStoryProp(root, "Workstation_BrokenScreen", new Vector3(-17.65f, 1.35f, -5.8f),
                new Vector3(0.06f, 0.8f, 1.45f), new Vector3(0f, -7f, 18f), screen);
            AddStoryProp(root, "PowerUnit_TornFree", new Vector3(-16.9f, 0.4f, -7.0f),
                new Vector3(0.9f, 0.8f, 0.8f), new Vector3(0f, 28f, 12f), equipment);

            // Three scavenged plates face the lane; two dropped cases behind
            // them show the defenders left in a hurry rather than decorating a room.
            AddStoryProp(root, "ImprovisedPlate_A", new Vector3(-16.8f, 0.7f, -4.6f),
                new Vector3(0.12f, 1.4f, 1.8f), new Vector3(0f, -18f, -8f), equipment);
            AddStoryProp(root, "ImprovisedPlate_B", new Vector3(-16.5f, 0.62f, -2.9f),
                new Vector3(0.12f, 1.25f, 1.5f), new Vector3(0f, -8f, 6f), equipment);
            AddStoryProp(root, "ImprovisedPlate_Fallen", new Vector3(-15.9f, 0.18f, -3.8f),
                new Vector3(0.12f, 1.6f, 1.2f), new Vector3(72f, 10f, 4f), equipment);
            AddStoryProp(root, "AbandonedCase_A", new Vector3(-17.4f, 0.22f, -8.1f),
                new Vector3(0.9f, 0.44f, 0.6f), new Vector3(0f, 16f, 0f), equipment);
            AddStoryProp(root, "AbandonedCase_B", new Vector3(-16.5f, 0.18f, -8.35f),
                new Vector3(0.7f, 0.36f, 0.5f), new Vector3(5f, -24f, 12f), equipment);
        }

        private static void AddStoryProp(GameObject parent, string name, Vector3 position, Vector3 scale,
            Vector3 eulerAngles, Material material)
        {
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = name;
            prop.transform.SetParent(parent.transform, false);
            prop.transform.position = position;
            prop.transform.localScale = scale;
            prop.transform.localRotation = Quaternion.Euler(eulerAngles);
            Object.DestroyImmediate(prop.GetComponent<Collider>());
            MeshRenderer renderer = prop.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
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

        private static void AddArenaBlock(GameObject parent, string name, Vector3 position, Vector3 scale,
            Material fallbackMaterial, ArenaKitConfig kit)
            => AddBlock(parent, name, position, scale, fallbackMaterial, kit.wallModule, kit.wallMaterial);

        /// <summary>
        /// Gameplay geometry is always the same unit box transformed by the
        /// builder. An empty kit takes the original primitive path exactly. A
        /// complete kit keeps only that box's collider and puts imported art in
        /// a child named Art after stripping every collider from its subtree.
        /// </summary>
        private static void AddBlock(GameObject parent, string name, Vector3 position, Vector3 scale,
            Material fallbackMaterial, GameObject? artPrefab, Material? artMaterial)
        {
            if (artPrefab == null)
            {
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                primitive.name = name;
                primitive.transform.SetParent(parent.transform, false);
                primitive.transform.position = position;
                primitive.transform.localScale = scale;
                primitive.GetComponent<MeshRenderer>().sharedMaterial = fallbackMaterial;
                return;
            }

            Material material = artMaterial ?? throw new System.InvalidOperationException(
                $"Art block '{name}' has a prefab but no material.");
            GameObject box = new(name);
            box.transform.SetParent(parent.transform, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.AddComponent<BoxCollider>();
            AddArtChild(box, artPrefab, material, disableShadows: false);
        }

        private static Renderer AddArtChild(GameObject parent, GameObject prefab, Material material,
            bool disableShadows)
        {
            GameObject art = Object.Instantiate(prefab);
            art.name = "Art";
            art.transform.SetParent(parent.transform, false);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;
            art.transform.localScale = Vector3.one;

            Collider[] colliders = art.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Object.DestroyImmediate(colliders[i]);

            Renderer[] renderers = art.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new System.InvalidOperationException(
                    $"Art prefab '{prefab.name}' contains no renderer.");

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    materials[materialIndex] = material;
                renderers[i].sharedMaterials = materials;

                if (!disableShadows) continue;
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
            return renderers[0];
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

        /// <summary>
        /// The Viewmodel layer index, or a build that stops right here.
        ///
        /// LayerMask.NameToLayer returns -1 for a layer that does not exist, and
        /// Unity will cheerfully assign -1 to a GameObject: nothing throws, the
        /// build "succeeds", and the failure surfaces as a gun that renders in no
        /// camera at all — hours later, in play, with every gate green. A missing
        /// row in TagManager.asset is a five-second fix and an all-afternoon
        /// diagnosis, so it is worth failing loudly at the moment it is read.
        /// </summary>
        private static int RequireViewmodelLayer()
        {
            int layer = LayerMask.NameToLayer(ViewmodelLayerName);
            if (layer < 0)
            {
                Debug.LogError($"GreyBoxBuilder: there is no '{ViewmodelLayerName}' layer. " +
                               "Add it to ProjectSettings/TagManager.asset (first free user slot, index 8) " +
                               "and build again — without it the viewmodel camera has nothing to draw.");
                throw new System.InvalidOperationException(
                    $"Missing '{ViewmodelLayerName}' layer in TagManager.asset");
            }
            return layer;
        }

        /// <summary>
        /// Layers do NOT inherit down a hierarchy in Unity — reparenting the rig
        /// under the overlay camera leaves all eight cubes on Default, where the
        /// world camera still draws them and the overlay camera does not. Every
        /// object in the subtree has to be moved by hand.
        /// </summary>
        private static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursive(child.gameObject, layer);
        }

        /// <summary>
        /// The camera that draws the gun, and nothing but the gun.
        ///
        /// WHY A SECOND CAMERA AT ALL
        /// One camera cannot do both jobs. The world wants a 0.05 near plane and
        /// an FOV that moves — the sprint bonus widens it, ADS and the fire kick
        /// pull it in. The gun wants a near plane in front of its own muzzle and
        /// an FOV that never moves. Sharing one camera gave us both defects at
        /// once: the barrel intersected every wall the player backed into, and
        /// the whole model stretched on every sprint and every shot. A URP
        /// overlay is the cheap fix — same transform, its own projection, drawn
        /// on top with a depth-only clear.
        ///
        /// ORDER MATTERS: renderType goes to Overlay BEFORE the camera joins the
        /// stack. URP rejects a Base camera added to a stack and logs an error,
        /// and this builder runs headlessly in CI where an error is the verdict.
        ///
        /// NOT tagged MainCamera and NO AudioListener. Camera.main must keep
        /// returning the world camera — RenderingTests asserts post-processing on
        /// whatever carries that tag — and a second listener is a permanent
        /// console warning plus undefined 3D audio.
        /// </summary>
        private static Camera BuildViewmodelCamera(GameObject baseCameraObject, Camera baseCamera,
            GameConfig game, int viewmodelLayer)
        {
            GameObject holder = new("ViewmodelCamera");
            holder.transform.SetParent(baseCameraObject.transform, false);

            Camera camera = holder.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Depth;
            camera.cullingMask = 1 << viewmodelLayer;
            camera.fieldOfView = game.viewmodelFovVertical;
            // 1 cm to 5 m. The near plane is the anti-clipping half of the fix;
            // the far plane is free performance, because nothing on this layer is
            // ever more than an arm's length away.
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 5f;
            camera.useOcclusionCulling = false;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            // Post ON for both cameras in the stack, AA only on the base. URP
            // takes the stack's anti-aliasing from the base camera, and asking
            // two cameras for SMAA is how you pay for it twice.
            //
            // This `true` is only the SHIPPED DEFAULT, exactly like the one in
            // EnablePostProcessing. CameraGraphics owns the flag from OnEnable
            // onwards and writes the player's saved choice to BOTH cameras — it
            // has to, because URP resolves the stack's post at the last camera in
            // it with the flag set, which is this one.
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;

            UniversalAdditionalCameraData baseData = baseCamera.GetUniversalAdditionalCameraData();
            baseData.cameraStack.Add(camera);

            // The gun needs its own light. A culling mask culls lights too, so the
            // sun and all four arena lights are invisible to this camera and the
            // viewmodel would render on ambient alone — near black, on a metallic
            // 0.85 material in a bunker with a flat dark reflection. One key light
            // parented to the camera is also the correct look: the gun stays lit
            // identically wherever the player stands, which is what every shooter
            // does and what makes a viewmodel read as held rather than as placed.
            // Numbers here are scene construction, like BuildArenaLights above.
            GameObject keyObject = new("ViewmodelKey");
            keyObject.transform.SetParent(holder.transform, false);
            keyObject.transform.localRotation = Quaternion.Euler(38f, -34f, 0f);
            keyObject.layer = viewmodelLayer;

            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.color = new Color(1f, 0.97f, 0.92f);
            // Nothing on this layer casts or receives shadows — AddViewmodelPart
            // turns both off per renderer — so a shadow map here is pure cost.
            key.shadows = LightShadows.None;

            return camera;
        }

        private static (WeaponController, PlayerLook, Health, Transform, Transform, Transform) BuildPlayerRig(
            GameConfig game, PlayerLoadoutConfig loadout, ImpactConfig impact, ObjectPool pool,
            Material gunmetal, Material gunAccent, RunContext run, SettingsHub settings, PaletteConfig palette,
            WeaponKitConfig weaponKit)
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

            // Read before anything is built: a missing layer must stop the build,
            // not produce a scene that only fails once someone plays it.
            int viewmodelLayer = RequireViewmodelLayer();

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pivot.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = game.baseFovVertical;
            camera.nearClipPlane = 0.05f;
            // The world camera must never see the gun. Everything else it sees is
            // untouched — this clears one bit, it does not rewrite the mask.
            camera.cullingMask &= ~(1 << viewmodelLayer);
            EnablePostProcessing(camera);
            cameraObject.AddComponent<AudioListener>();
            CameraShake shake = cameraObject.AddComponent<CameraShake>();

            Camera viewmodelCamera = BuildViewmodelCamera(cameraObject, camera, game, viewmodelLayer);

            // Wired AFTER the overlay exists, and it takes BOTH cameras. URP
            // resolves the stack's post-processing at the last camera in it that
            // has renderPostProcessing on, so a CameraGraphics holding only the
            // base camera cannot turn post off: the player clears the setting, the
            // base goes false, the overlay stays true, and the frame is still
            // graded. The player-facing R2 row would be inert on the one path that
            // ships. See CameraGraphics.Apply for why AA is base-only.
            CameraGraphics graphics = cameraObject.AddComponent<CameraGraphics>();
            SetRef(graphics, "_settings", settings);
            SetRef(graphics, "_camera", camera);
            SetRef(graphics, "_viewmodelCamera", viewmodelCamera);

            PlayerLook look = player.AddComponent<PlayerLook>();
            SetRef(look, "_config", game);
            SetRef(look, "_input", input);
            SetRef(look, "_motor", motor);
            SetRef(look, "_cameraPivot", pivot.transform);
            SetRef(look, "_camera", camera);
            SetRef(look, "_viewmodelCamera", viewmodelCamera);
            SetRef(look, "_settings", settings);   // saved sensitivity / FOV / invert

            // The viewmodel. There was no gun on screen at all before this, which
            // is most of why the grey box read as a tech demo rather than a
            // shooter: nothing occupies the lower-right, nothing moves when you
            // look around, and the muzzle flash spawns in empty air.
            // Parented to the OVERLAY camera, not the world one. Same transform,
            // same pose, different projection and a different near plane — which
            // is the whole fix: at a 0.05 near clip on the world camera the barrel
            // tip sits 0.53 m out and pushes straight through any wall the player
            // stands against.
            GameObject weaponRig = new("WeaponRig");
            weaponRig.transform.SetParent(viewmodelCamera.transform, false);
            weaponRig.transform.localPosition = new Vector3(0.145f, -0.125f, 0.28f);

            GameObject model = new("Viewmodel");
            model.transform.SetParent(weaponRig.transform, false);

            if (weaponKit.viewmodelPrefab == null)
            {
                AddViewmodelPart(model, "Receiver", new Vector3(0f, 0f, 0.10f), new Vector3(0.055f, 0.075f, 0.30f), gunmetal);
                AddViewmodelPart(model, "Handguard", new Vector3(0f, -0.004f, 0.31f), new Vector3(0.045f, 0.052f, 0.23f), gunAccent);
                AddViewmodelPart(model, "Barrel", new Vector3(0f, 0.006f, 0.46f), new Vector3(0.019f, 0.019f, 0.13f), gunAccent);
                AddViewmodelPart(model, "Stock", new Vector3(0f, -0.006f, -0.13f), new Vector3(0.045f, 0.062f, 0.17f), gunmetal);
                AddViewmodelPart(model, "Grip", new Vector3(0f, -0.077f, 0.015f), new Vector3(0.04f, 0.105f, 0.05f), gunmetal);
                AddViewmodelPart(model, "Magazine", new Vector3(0f, -0.102f, 0.15f), new Vector3(0.036f, 0.135f, 0.062f), gunAccent);
                AddViewmodelPart(model, "SightRear", new Vector3(0f, 0.052f, 0.01f), new Vector3(0.022f, 0.028f, 0.03f), gunAccent);
                AddViewmodelPart(model, "SightFront", new Vector3(0f, 0.052f, 0.42f), new Vector3(0.016f, 0.032f, 0.022f), gunAccent);
            }
            else
            {
                Material material = weaponKit.viewmodelMaterial ?? throw new System.InvalidOperationException(
                    $"Weapon kit '{weaponKit.name}' has a prefab but no material.");
                AddArtChild(model, weaponKit.viewmodelPrefab, material, disableShadows: true);
            }

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
            muzzleLight.color = palette.sparkHot;
            muzzleLight.enabled = false;

            // The SECOND muzzle light, and the reason there has to be one: a
            // camera culls lights by the light's layer, so the world light above
            // is invisible to the overlay camera that draws the gun, and this one
            // is invisible to the world. Short range because it is centimetres
            // from the barrel rather than metres from a wall. Both are driven off
            // one timer in UpdateMuzzleLight, so they can never desync.
            GameObject viewmodelLightObject = new("MuzzleLight_Viewmodel");
            viewmodelLightObject.transform.SetParent(muzzle.transform, false);
            Light viewmodelMuzzleLight = viewmodelLightObject.AddComponent<Light>();
            viewmodelMuzzleLight.type = LightType.Point;
            viewmodelMuzzleLight.range = 1.6f;
            viewmodelMuzzleLight.color = palette.sparkHot;
            viewmodelMuzzleLight.shadows = LightShadows.None;
            viewmodelMuzzleLight.enabled = false;

            // The whole rig moves to the viewmodel layer LAST, once every child
            // exists — the parts, the muzzle, the eject port and the flash light.
            SetLayerRecursive(weaponRig, viewmodelLayer);

            // ...and the muzzle light comes straight back out of it. A camera's
            // culling mask culls LIGHTS as well as renderers, so a light left on
            // the viewmodel layer would be invisible to the world camera and the
            // muzzle flash would stop lighting the room — the one thing this
            // light exists to do, and the reason its duration and intensity are
            // per-weapon numbers on WeaponConfig.
            //
            // The masks are disjoint, so this light cannot also light the gun: no
            // single light can be seen by both cameras. The gun's half of the
            // flash is a second point light on the Fx_MuzzleFlash prefab, which is
            // spawned on the same shot and lives on the viewmodel layer — see
            // BuildMuzzleFlashPrefab for why it cannot hang here instead. The
            // steady key light for the gun is ViewmodelKey.
            lightObject.layer = LayerMask.NameToLayer("Default");

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
            SetRef(weapon, "_viewmodelMuzzleLight", viewmodelMuzzleLight);
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

        /// <summary>
        /// The margin every corner-anchored HUD label sits in, in canvas units.
        ///
        /// UNCHANGED AT 90, AND THE STORY IS WORTH KEEPING. A screenshot from
        /// the first real play session appeared to show the objective line
        /// clipped off the left edge -- it read "EADY / THE CONTROL POINT"
        /// instead of "REACH THE CONTROL POINT". The obvious reading was a
        /// layout bug, and the obvious fix was to widen this inset.
        ///
        /// It was neither. Parsing the generated scene shows every HUD label
        /// strictly inside the canvas at 16:9, 4:3 and 21:9, and the objective
        /// box is 660 units holding a string that needs about 373. What was
        /// actually cropping the text was the Unity EDITOR: the Game view was
        /// set to Scale 1.8x on Free Aspect, which zooms in and cuts every
        /// edge. The ammo counter and the money readout were cut too, in the
        /// same frame, which is the tell -- a layout bug does not clip all four
        /// sides at once.
        ///
        /// So this exists as one named source of truth rather than nine copies
        /// of the number 90, and it deliberately did NOT move. Changing it
        /// would have been a fix for a defect that does not exist, justified by
        /// a comment that was not true.
        ///
        /// Not a ScriptableObject tunable: this is authored scene geometry,
        /// baked into the scene the moment the builder runs, and no runtime
        /// code ever reads it. Same reason every other position here is a
        /// literal.
        /// </summary>
        private const float HUD_SAFE_X = 90f;

        /// <summary>The same title-safe inset on the short axis. See <see cref="HUD_SAFE_X"/>.</summary>
        private const float HUD_SAFE_Y = 60f;

        /// <summary>
        /// One width for the whole top-left column, and it is sized by the
        /// LONGEST string the game can put in it, not by the shortest.
        ///
        /// THE SECOND DEFECT: BuildLabel hands out a 320x48 placeholder and
        /// trusts the caller to replace it, and the wave line never did. Once
        /// waves got identities, "WAVE 9 — CROSSFIRE" at 34 pt measured wider
        /// than 320 — so it wrapped, and a 48-tall box with Truncate overflow
        /// then threw the second line away. The wave name simply did not exist
        /// on screen, and nothing failed. 720 holds that line and holds
        /// "HOLD THE CONTROL POINT 0:45" at 26 pt, which is the longest
        /// objective line either shipped mission can produce.
        /// </summary>
        private const float HUD_COLUMN_WIDTH = 720f;

        private const float HUD_WAVE_HEIGHT = 48f;
        private const float HUD_ENEMIES_HEIGHT = 40f;

        /// <summary>Four one-line objectives plus their counters, which is the most a parallel step ever shows.</summary>
        private const float HUD_OBJECTIVE_HEIGHT = 240f;

        // The column stacks DOWNWARD from the safe inset, each row derived from
        // the one above it. Hand-typed tops (-60, -104, -150) had already drifted
        // into a two-unit overlap between the enemies count and the objective
        // list; derived ones cannot.
        private const float HUD_COLUMN_WAVE_Y = -HUD_SAFE_Y;
        private const float HUD_COLUMN_ENEMIES_Y = HUD_COLUMN_WAVE_Y - HUD_WAVE_HEIGHT;
        private const float HUD_COLUMN_OBJECTIVE_Y = HUD_COLUMN_ENEMIES_Y - HUD_ENEMIES_HEIGHT;

        private static void BuildHud(WeaponController weapon, Health playerHealth, GameConfig game,
            ObjectPool pool, GameObject dummyPrefab, Transform spawnOrigin,
            DroneSpawner spawner, DroneRegistry registry, Transform cameraTransform,
            RunContext run, WaveRunner runner, SettingsHub settingsHub, PlayerInput input,
            MissionDirector director, PlayerInteractor interactor, RadioDialogueScheduler radio,
            AudioKitConfig audioKit)
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

            // All four screen corners share one inset. They were four separate
            // hand-typed 90s and 60s, which is how the left column ended up
            // inside the title-safe band without anyone comparing it to
            // anything. See HUD_SAFE_X.
            Text ammo = BuildLabel(canvasObject, "Ammo", new Vector2(-HUD_SAFE_X, HUD_SAFE_Y),
                TextAnchor.LowerRight, new Vector2(1f, 0f));
            Text healthLabel = BuildLabel(canvasObject, "Health", new Vector2(HUD_SAFE_X, HUD_SAFE_Y),
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
            // Twelve units under the ammo box's own bottom edge, so the bar
            // tracks the count rather than a coordinate someone has to keep in
            // step with it by hand.
            lowAmmoRect.anchoredPosition = new Vector2(-HUD_SAFE_X, HUD_SAFE_Y - 12f);
            lowAmmoRect.sizeDelta = new Vector2(160f, 3f);
            lowAmmoImage.enabled = false;

            Hud hud = canvasObject.AddComponent<Hud>();
            SetRef(hud, "_weapon", weapon);
            SetRef(hud, "_playerHealth", playerHealth);
            SetRef(hud, "_ammoLabel", ammo);
            SetRef(hud, "_healthLabel", healthLabel);
            SetRef(hud, "_lowAmmoTint", lowAmmoImage);

            // The objective list and the interact prompt are HUD, not menu, so
            // they are created BEFORE the shop, pause and death panels and are
            // painted underneath them — the same sibling-order rule that makes
            // BuildPauseUi the last call in this method. The mission BANNER is the
            // exception and is built after all three; see BuildObjectiveHud.
            Text objectiveLabel = BuildObjectiveLabel(canvasObject);
            BuildInteractPrompt(canvasObject, interactor);
            BuildRadioSubtitles(canvasObject, radio, settingsHub);

            BuildDamageFeedback(canvasObject, game, playerHealth, cameraTransform, hudAudio);
            BuildRunUi(canvasObject, run, runner, weapon, hudAudio, audioKit);
            BuildPauseUi(canvasObject, settingsHub, input, run, runner);
            BuildObjectiveHud(canvasObject, director, objectiveLabel);

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

            // The arsenal, so digit 0 can walk it. LOADED, NOT CREATED, and
            // missing is a warning rather than a failure: ArsenalBuilder owns
            // Weapons.asset and runs AFTER this builder, so on a first-ever build
            // of an empty project it genuinely is not there yet. Re-running the
            // grey box once the arsenal exists picks it up, which is the same
            // no-run-order-dependency rule AddVfxPrewarm follows.
            var arsenal = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(DataWeapons + "/Weapons.asset");
            if (arsenal == null)
            {
                Debug.LogWarning(
                    "No weapon registry at " + DataWeapons + "/Weapons.asset — the sandbox console will have " +
                    "no weapon to cycle to, so six of the eight guns stay unreachable. Run CoD -> Build Arsenal, " +
                    "then run this builder again.");
            }
            SetRef(console, "_weaponRegistry", arsenal);

            // Every attachment on disk, scanned rather than listed — the reason
            // WeaponDataTests scans the weapons folder: a hardcoded list makes
            // attachment number six a builder edit, and one nobody remembered to
            // add is one nobody can ever fit.
            SetArray(console, "_attachments", LoadAllAttachments());
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
            WeaponController weapon, AudioSource audio, AudioKitConfig audioKit)
        {
            // The top-left column, top row. Widened off BuildLabel's 320
            // placeholder because "WAVE 9 — CROSSFIRE" does not fit in it: it
            // wrapped, and the 48-tall box then truncated the line the wave
            // identity was on. See HUD_COLUMN_WIDTH.
            Text wave = BuildLabel(canvasObject, "WaveLabel", new Vector2(HUD_SAFE_X, HUD_COLUMN_WAVE_Y),
                TextAnchor.UpperLeft, new Vector2(0f, 1f), 34);
            wave.rectTransform.sizeDelta = new Vector2(HUD_COLUMN_WIDTH, HUD_WAVE_HEIGHT);
            Text enemies = BuildLabel(canvasObject, "EnemiesLabel", new Vector2(HUD_SAFE_X, HUD_COLUMN_ENEMIES_Y),
                TextAnchor.UpperLeft, new Vector2(0f, 1f), 26);
            enemies.rectTransform.sizeDelta = new Vector2(HUD_COLUMN_WIDTH, HUD_ENEMIES_HEIGHT);
            Text money = BuildLabel(canvasObject, "MoneyLabel", new Vector2(-HUD_SAFE_X, HUD_COLUMN_WAVE_Y),
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
            SetRef(shop, "_buyClip", Prefer(audioKit.confirm, "Shop_Buy"));
            SetRef(shop, "_refusedClip", Prefer(audioKit.refused, "Shop_Refused"));

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

        /// <summary>
        /// The objective list — the third row of the top-left column, under the
        /// wave line and the enemies count it shares a margin with.
        ///
        /// WHERE IT SITS IS THE BUG THAT SHIPPED. It was placed at x 90, an
        /// inset the rest of the HUD had used since the grey box, and 90 of 1920
        /// is inside the 5% a display is allowed to crop. The first mission's
        /// first instruction — "REACH THE CONTROL POINT" — lost its opening word
        /// off the left edge of a real play session. Every row of the column now
        /// derives from HUD_SAFE_X and HUD_COLUMN_WIDTH so none of them can drift
        /// apart again, and CampaignTests measures the rendered rect against the
        /// canvas rect so a future drift fails a test instead of a screenshot.
        ///
        /// Shipped EMPTY, and that is not cosmetic. BuildLabel seeds every label
        /// with its own object name so an unwired one is obvious in the editor,
        /// but ObjectiveHud only ever assigns this field when the text it would
        /// write DIFFERS from what it wrote last — and in endless mode it has
        /// nothing to write on the first pass, correctly leaves the label alone,
        /// and the word "ObjectiveLabel" would sit in the corner for the whole run.
        /// </summary>
        private static Text BuildObjectiveLabel(GameObject canvasObject)
        {
            Text objective = BuildLabel(canvasObject, "ObjectiveLabel",
                new Vector2(HUD_SAFE_X, HUD_COLUMN_OBJECTIVE_Y),
                TextAnchor.UpperLeft, new Vector2(0f, 1f), 26);
            objective.rectTransform.sizeDelta = new Vector2(HUD_COLUMN_WIDTH, HUD_OBJECTIVE_HEIGHT);
            objective.text = string.Empty;
            return objective;
        }

        private static void BuildRadioSubtitles(GameObject canvasObject, RadioDialogueScheduler radio,
            SettingsHub settingsHub)
        {
            GameObject backgroundObject = new("RadioSubtitleBackground", typeof(RectTransform));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            Image background = backgroundObject.AddComponent<Image>();
            background.color = new Color(0.015f, 0.02f, 0.028f, 0.82f);
            background.raycastTarget = false;
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.5f, 0f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0f);
            backgroundRect.pivot = new Vector2(0.5f, 0f);
            backgroundRect.anchoredPosition = new Vector2(0f, 96f);
            backgroundRect.sizeDelta = new Vector2(1180f, 136f);

            Text label = BuildLabel(canvasObject, "RadioSubtitle", new Vector2(0f, 108f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), 34);
            // Two full lines at the Large accessibility size. A shorter box
            // rendered a wrapped final word below its own truncate boundary.
            label.rectTransform.sizeDelta = new Vector2(1100f, 112f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.text = string.Empty;

            RadioSubtitleHud hud = canvasObject.AddComponent<RadioSubtitleHud>();
            SetRef(hud, "_radio", radio);
            SetRef(hud, "_settings", settingsHub);
            SetRef(hud, "_label", label);
            SetRef(hud, "_background", background);

            label.enabled = false;
            background.enabled = false;
        }

        /// <summary>
        /// "HOLD F" and the bar that fills while you do.
        ///
        /// Deliberately NOT on the title-safe inset the corner labels use, and
        /// measured against the canvas rather than assumed: this is the one HUD
        /// element whose job is to sit a fixed distance under the crosshair, so
        /// it is anchored to the centre and stays there. Centre-anchored is only
        /// dangerous when the offset can outrun half the canvas — 120 units below
        /// centre cannot, on any aspect ratio, which is why the mission banner
        /// needed re-anchoring and this did not.
        ///
        /// Both ship blank and hidden for the same reason the objective list does:
        /// InteractPrompt writes the label only when the TARGET changes, and with
        /// no interactable in the scene at load there is no first change to write.
        ///
        /// The bar is the fiddly half. UnityEngine.UI.Image falls straight through
        /// to a plain quad when its sprite is null — the filled path is never
        /// reached and fillAmount is never read — so a bar built without one
        /// renders FULL on the first frame and stays full through every hold.
        /// Nothing is null, nothing errors, and the only symptom is a progress bar
        /// that is always done. Unity's built-in UI sprite is what every Image
        /// created from the GameObject menu gets, it ships inside the player, and
        /// it costs this repo no binary.
        /// </summary>
        private static void BuildInteractPrompt(GameObject canvasObject, PlayerInteractor interactor)
        {
            Text prompt = BuildLabel(canvasObject, "InteractPrompt", new Vector2(0f, -120f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), 30);
            prompt.rectTransform.sizeDelta = new Vector2(700f, 44f);
            prompt.text = string.Empty;

            GameObject barObject = new("InteractHoldBar", typeof(RectTransform));
            barObject.transform.SetParent(canvasObject.transform, false);
            Image bar = barObject.AddComponent<Image>();
            bar.sprite = UiSprite();
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillOrigin = (int)Image.OriginHorizontal.Left;
            bar.fillAmount = 0f;
            bar.color = new Color(0.92f, 0.94f, 0.96f, 0.92f);
            bar.raycastTarget = false;
            // Hidden rather than drawn empty. An empty bar under every prompt
            // reads as broken, and an instant interactable has no hold at all —
            // InteractPrompt turns it back on the moment the fill leaves zero.
            bar.enabled = false;

            RectTransform barRect = barObject.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.pivot = new Vector2(0.5f, 0.5f);
            barRect.anchoredPosition = new Vector2(0f, -164f);
            barRect.sizeDelta = new Vector2(280f, 6f);

            InteractPrompt promptHud = canvasObject.AddComponent<InteractPrompt>();
            SetRef(promptHud, "_interactor", interactor);
            SetRef(promptHud, "_promptLabel", prompt);
            SetRef(promptHud, "_holdBar", bar);
        }

        /// <summary>
        /// The mission banner, and the component that drives both it and the
        /// objective list.
        ///
        /// Built LAST, after the shop, the pause menu and the death screen, and
        /// that is the whole reason it is not created alongside the label it
        /// shares a component with. MISSION COMPLETE has to be legible at the
        /// moment the mission ends — which is also the moment WaveRunner enters
        /// GameOver and GameOverPanel drops a full-screen backdrop over the HUD.
        /// A banner painted underneath that is a banner nobody ever sees.
        ///
        /// Its height clears both things it now sits on top of: the wave banner at
        /// y 150 and the death screen's title at y 90.
        /// </summary>
        /// <summary>
        /// The named places a mission can send the player.
        ///
        /// THE DEFECT THIS EXISTS FOR: MissionProgress.RegisterZone had no caller
        /// anywhere in the game, so IsInsideZone answered false forever —
        /// correctly, by its own design — and every ReachZone, HoldZone and
        /// Extract objective was uncompletable. Mission 1 stalled on its FIRST
        /// step with the runner suspended and the arena empty, which is the state
        /// MissionDirector's own comments call indistinguishable from a hang. The
        /// missions validated clean and shipped in the catalog as locked rooms.
        ///
        /// Markers, not trigger volumes, and no colliders: this project has no
        /// trigger colliders anywhere, and a collider on the arena floor either
        /// blocks movement or eats the aim ray — which is why the repair beacon's
        /// pad destroys its own.
        ///
        /// Ids are the indirection that lets one authored "hold the control
        /// point" asset mean a different pad in every arena, because an objective
        /// is a ScriptableObject shared by every mission and cannot hold a
        /// Transform.
        /// </summary>
        private static void BuildMissionZones(MissionDirector director)
        {
            GameObject root = new("MissionZones");

            // 0 — the control point. On the open floor just north of the centre
            // bunker: reaching it means crossing the arena, and holding it means
            // holding the one place with sightlines down all three lanes.
            Transform control = NewZoneMarker(root, "Zone_ControlPoint", new Vector3(0f, 0.05f, 7.5f));

            // 1 — extraction, back at the mouth the player entered by. Walking
            // OUT is the shape of every extraction, and it means the last thing a
            // mission asks is a fighting retreat across ground already fought over.
            Transform extract = NewZoneMarker(root, "Zone_Extract", new Vector3(0f, 0.05f, -15f));

            SetZones(director, control, extract);
        }

        private static Transform NewZoneMarker(GameObject parent, string name, Vector3 position)
        {
            GameObject marker = new(name);
            marker.transform.SetParent(parent.transform, false);
            marker.transform.position = position;
            return marker.transform;
        }

        /// <summary>
        /// Writes the MissionZone[] by hand, because SetArrayRef only handles
        /// arrays of object references and a MissionZone is a struct with three
        /// fields. Radii are shipped defaults, not tuning: a mission that wants a
        /// different one edits the scene.
        /// </summary>
        private static void SetZones(MissionDirector director, Transform control, Transform extract)
        {
            var serialized = new SerializedObject(director);
            SerializedProperty zones = serialized.FindProperty("_zones");
            zones.arraySize = 2;

            SerializedProperty first = zones.GetArrayElementAtIndex(0);
            first.FindPropertyRelative("id").intValue = 0;
            first.FindPropertyRelative("marker").objectReferenceValue = control;
            first.FindPropertyRelative("radius").floatValue = 3f;

            SerializedProperty second = zones.GetArrayElementAtIndex(1);
            second.FindPropertyRelative("id").intValue = 1;
            second.FindPropertyRelative("marker").objectReferenceValue = extract;
            second.FindPropertyRelative("radius").floatValue = 3f;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// How far the mission banner hangs below the top edge. Lands within
        /// three units of where "centre plus 300" used to put it on a 16:9
        /// screen, which is the point: the LOOK is unchanged, the failure mode is
        /// not. Anchored to the centre, the banner's distance from the top grew
        /// with half the canvas height, and the canvas gets shorter as the screen
        /// gets wider — on a 32:9 canvas (540 reference units tall) "centre plus
        /// 300" is 75 units off the top of the screen and MISSION COMPLETE is
        /// simply never seen. Anchored to the top, it is 192 units down on every
        /// aspect ratio there is.
        /// </summary>
        private const float MISSION_BANNER_Y = -(HUD_SAFE_Y + 120f);

        private static void BuildObjectiveHud(GameObject canvasObject, MissionDirector director,
            Text objectiveLabel)
        {
            Text banner = BuildLabel(canvasObject, "MissionBanner", new Vector2(0f, MISSION_BANNER_Y),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), 56);
            banner.rectTransform.sizeDelta = new Vector2(1200f, 90f);
            // Cleared by ObjectiveHud's own OnEnable too; shipped empty so the
            // scene on disk never contains a placeholder the player could see.
            banner.text = string.Empty;

            ObjectiveHud hud = canvasObject.AddComponent<ObjectiveHud>();
            SetRef(hud, "_director", director);
            SetRef(hud, "_objectiveLabel", objectiveLabel);
            SetRef(hud, "_bannerLabel", banner);
        }

        /// <summary>
        /// Unity's built-in UI sprite — the same one a GameObject &gt; UI &gt; Image
        /// gets, resolved from the editor's built-in extra resources and shipped
        /// inside the player. The only sprite in this project, and the only reason
        /// there is one at all is that a Filled Image without a sprite silently
        /// stops being filled. See BuildInteractPrompt.
        /// </summary>
        private static Sprite? UiSprite()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

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
            // A PLACEHOLDER, not a default — every caller with a string longer
            // than about fifteen characters has to replace it, and the one that
            // forgot is why the wave identity was invisible for five commits.
            // 320 at 34 pt is roughly "WAVE 12", and Unity gives no hint when a
            // Text wraps out of a Truncate box: it just stops drawing the rest.
            // CampaignTests measures preferredWidth against the rect for the two
            // labels a mission writes, so a forgotten override fails a test now.
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
        private static void BuildMainMenuScene(GameConfig game, SettingsConfig settingsConfig,
            MissionCatalog missionCatalog, VolumeProfile postFx)
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

            // Created after the settings page, so it is the LAST child of the
            // canvas and paints over both screens it can be opened from. Same
            // three labels, same backdrop, same construction — a mission list is
            // not a different kind of screen, it is a different body of text.
            MenuScreen campaign = BuildMenuScreen(canvasObject, "MissionSelectPanel",
                new Color(0.05f, 0.055f, 0.065f, 1f), 48, 30);
            campaign.Title.text = "CAMPAIGN";

            SettingsPanel settingsPanel = canvasObject.AddComponent<SettingsPanel>();
            SetRef(settingsPanel, "_settings", settingsHub);
            SetRef(settingsPanel, "_root", settings.Root);
            SetRef(settingsPanel, "_bodyLabel", settings.Body);
            SetRef(settingsPanel, "_footerLabel", settings.Footer);

            MissionSelectPanel missionPanel = canvasObject.AddComponent<MissionSelectPanel>();
            SetRef(missionPanel, "_settings", settingsHub);
            SetRef(missionPanel, "_catalog", missionCatalog);
            SetRef(missionPanel, "_root", campaign.Root);
            SetRef(missionPanel, "_titleLabel", campaign.Title);
            SetRef(missionPanel, "_bodyLabel", campaign.Body);
            SetRef(missionPanel, "_footerLabel", campaign.Footer);
            // The arena a mission falls back to when it does not name its own. An
            // empty string here is a Launch that loads nothing and looks like a
            // dead ENTER key, which is why GreyBoxVerify checks it as a string.
            SetString(missionPanel, "_defaultSceneName", "10_GreyBox");

            MainMenuPanel menuPanel = canvasObject.AddComponent<MainMenuPanel>();
            SetRef(menuPanel, "_settings", settingsHub);
            SetRef(menuPanel, "_root", menu.Root);
            SetRef(menuPanel, "_titleLabel", menu.Title);
            SetRef(menuPanel, "_recordLabel", record);
            SetRef(menuPanel, "_bodyLabel", menu.Body);
            SetRef(menuPanel, "_footerLabel", menu.Footer);
            SetRef(menuPanel, "_settingsPanel", settingsPanel);
            // Unwired, the CAMPAIGN row hides the main menu and opens nothing —
            // a black screen that ignores every key except ESC, which the panel
            // that is not open cannot handle either.
            SetRef(menuPanel, "_missionPanel", missionPanel);
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
                "Assets/_Project/Art", Materials, Textures, "Assets/_Project/Audio",
                "Assets/_Project/Data", DataGame, DataWeapons, DataDrones, DataAttacks,
                DataWaves, DataShop, DataPassives, DataEffects, DataMissions, DataKits, Audio,
                Prefabs, Scenes,
            };
            foreach (string folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                int split = folder.LastIndexOf('/');
                AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
            }
        }

        private static void RequireValidKits(ArenaKitConfig arena, WeaponKitConfig weapon,
            EnemyKitConfig enemy, AudioKitConfig audio)
        {
            var invalid = new List<string>(4);
            if (!arena.IsValid) invalid.Add(arena.name);
            if (!weapon.IsValid) invalid.Add(weapon.name);
            if (!enemy.IsValid) invalid.Add(enemy.name);
            if (!audio.IsValid) invalid.Add(audio.name);
            if (invalid.Count == 0) return;

            throw new System.InvalidOperationException(
                "Art kits must be either entirely empty or entirely assigned. Mixed kit(s): " +
                string.Join(", ", invalid));
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

        private static AudioClip? Prefer(AudioClip? authored, string fallbackName) =>
            authored != null ? authored : LoadClip(fallbackName);

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
        /// One shared detail normal map, generated ONCE and then left alone.
        ///
        /// Generated rather than authored because nothing in this project is
        /// hand-made — the scenes, prefabs, materials and navmesh are all built by
        /// this file, and a binary someone dragged in is the one asset nobody
        /// could review or reproduce.
        ///
        /// "Once" is load-bearing. A .png goes through Git LFS, the free quota is
        /// 1 GB of storage and 1 GB of bandwidth a month, and regenerating on every
        /// build would push a fresh one-to-two megabyte object every time the menu
        /// item is clicked. Existing file, existing texture, no write.
        /// </summary>
        private static Texture2D? EnsureDetailNormal()
        {
            const string path = Textures + "/Surface_Detail_N.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 1024;

            // Deterministic: a fixed seed and an integer hash, never
            // UnityEngine.Random. Regenerating after a delete has to produce the
            // same bytes, or the diff is noise and the LFS object is wasted.
            const int seed = 20260811;

            var height = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    float value = 0f;
                    float amplitude = 1f;
                    float total = 0f;
                    // Four octaves. Each one's lattice period equals its frequency,
                    // which is what makes the result tile seamlessly.
                    for (int octave = 0; octave < 4; octave++)
                    {
                        int frequency = 8 << octave;
                        value += amplitude * ValueNoise(u * frequency, v * frequency, frequency, seed + octave);
                        total += amplitude;
                        amplitude *= 0.5f;
                    }
                    height[y * size + x] = value / total;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[size * size];
            const float strength = 6f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Central differences, wrapped, so the derivative is seamless
                    // at the edges too. A visible seam is the one thing a tiling
                    // detail map must never have.
                    float left = height[y * size + (x + size - 1) % size];
                    float right = height[y * size + (x + 1) % size];
                    float down = height[((y + size - 1) % size) * size + x];
                    float up = height[((y + 1) % size) * size + x];

                    Vector3 normal = new Vector3(-(right - left) * strength, -(up - down) * strength, 1f).normalized;
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f), 0, 255),
                        255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            // Import settings straight from the playbook's VRAM rules. Without the
            // NormalMap type this lands as a colour texture and lights the surface
            // wrongly while looking perfectly fine in the project window.
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.maxTextureSize = 1024;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.isReadable = false;   // readable doubles the memory, for nothing
                importer.SaveAndReimport();
            }

            Debug.Log("Generated " + path + " (once — it is never rewritten).");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Tiling value noise. The lattice wraps at `period`, so sampling
        /// 0..period across the image produces a seamless result.
        /// </summary>
        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            // Smoothstep, so the lattice does not read as a grid of diamonds.
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float a = Hash01(Wrap(x0, period), Wrap(y0, period), seed);
            float b = Hash01(Wrap(x0 + 1, period), Wrap(y0, period), seed);
            float c = Hash01(Wrap(x0, period), Wrap(y0 + 1, period), seed);
            float d = Hash01(Wrap(x0 + 1, period), Wrap(y0 + 1, period), seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Wrap(int value, int period) => ((value % period) + period) % period;

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 1274126177;
                n = (n ^ (n >> 13)) * 1274126177;
                n ^= n >> 16;
                return (n & 0x7FFFFFF) / (float)0x7FFFFFF;
            }
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
        /// <summary>
        /// Re-asserts a material's base colour from the palette, every build.
        ///
        /// The counterpart to LoadOrCreateMaterial's "return an existing .mat
        /// untouched". That rule is right for a value a human tuned in the
        /// Inspector — and wrong for a shipped default, which is exactly how
        /// the tactical palette shipped as its own pre-tuning values for the
        /// whole life of the project. The tuning now lives in PaletteConfig, so
        /// re-asserting from it stomps nobody: the asset a human edits is the
        /// one that wins.
        /// </summary>
        private static void ApplyPalette(Material material, Color color)
        {
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Same, for a material that glows. The _EMISSION keyword is re-asserted
        /// too: without it URP ignores _EmissionColor entirely, which would make
        /// DroneController.SetTelegraph — the attack fairness contract — do
        /// nothing visible while looking completely correct in the Inspector.
        /// </summary>
        private static void ApplyEmission(Material material, Color color, float intensity)
        {
            material.SetColor("_BaseColor", color);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * intensity);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// An ADDITIVE, soft particle material.
        ///
        /// Every particle system in the project used to share one OPAQUE Lit
        /// material, which is why nothing ever glowed, nothing blended, and
        /// particles cut a hard edge into the floor they intersected. A spark is
        /// light, not surface: additive is the correct blend, and soft particles
        /// are what stop the intersection edge.
        ///
        /// Soft particles read _CameraDepthTexture, and the PC pipeline asset
        /// already has m_RequireDepthTexture on for SSAO — so this is free here
        /// and would silently do nothing on a pipeline without it.
        /// </summary>
        /// <summary>
        /// A skybox material, created once. Same create-then-leave-alone rule as
        /// every other material here: a value a human tuned must survive the next
        /// build.
        /// </summary>
        private static Material LoadOrCreateSkybox(string path, PaletteConfig palette)
        {
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Skybox/Procedural");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Tunes the sky down to "dim room", re-asserted every build like
        /// ApplySurface.
        ///
        /// Procedural, so it costs no texture at all — no import settings, no LFS
        /// object, no VRAM — and it is doing two jobs at once: it is what sits
        /// above the arena walls, and it is the only thing metal has to reflect,
        /// because there are no reflection probes yet.
        ///
        /// The sun disk is OFF. This is an interior; a visible sun in the sky of a
        /// sealed facility is the same category of mistake as the blue sky it
        /// replaced, and it would also put a hard specular dot on the gun that
        /// tracks the camera.
        /// </summary>
        private static void ApplyInteriorSky(Material material, PaletteConfig palette)
        {
            const float NO_SUN_DISK = 0f;

            material.SetFloat("_SunDisk", NO_SUN_DISK);
            material.SetColor("_SkyTint", palette.indoorReflection);
            // Darker underfoot than overhead, which is what a room does and what
            // stops the reflection reading as a flat grey card.
            material.SetColor("_GroundColor", palette.indoorReflection * 0.55f);
            // Thin and dim: thickness drives how much the procedural sky blooms
            // toward the horizon, and a fat atmosphere in a bunker reads as fog
            // fighting the real fog.
            material.SetFloat("_AtmosphereThickness", 0.35f);
            material.SetFloat("_Exposure", 0.55f);
            EditorUtility.SetDirty(material);
        }

        private static Material LoadOrCreateParticleMaterial(string path, Color color)
        {
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// The additive/transparent/soft setup, re-asserted every build for the
        /// same reason ApplySurface is.
        ///
        /// URP's particle shaders are driven by BOTH float properties and shader
        /// keywords, and the material inspector is what normally keeps the two in
        /// sync. Set one without the other from a script and the material renders
        /// as opaque alpha-blend while every value in the Inspector reads correct
        /// — the same silent-null class as _EMISSION and _NORMALMAP.
        /// </summary>
        private static void ApplyParticleSurface(Material material, Color color)
        {
            const float TRANSPARENT = 1f;
            const float ADDITIVE = 2f;

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", TRANSPARENT);
            material.SetFloat("_Blend", ADDITIVE);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetFloat("_SoftParticleNearFadeDistance", 0f);
            material.SetFloat("_SoftParticleFarFadeDistance", 0.6f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");

            // Additive geometry must draw after opaques or it blends against an
            // unfinished frame.
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Gets a volume override, ADDING it only if the profile has none.
        ///
        /// The difference between this and AddOverride matters: AddOverride on a
        /// profile that already has the component produces a SECOND copy, and a
        /// stack with two Blooms is not a stack with one Bloom. This is what lets
        /// new shipped defaults land on the existing PostFx_Arena.asset without
        /// recreating it — the same get-or-add discipline SetRef uses for scene
        /// references.
        /// </summary>
        private static T EnsureOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet(out T existing) ? existing : AddOverride<T>(profile);
        }

        /// <summary>
        /// Sets a volume parameter ONLY if nobody has overridden it.
        ///
        /// Override() is for introducing a value; this is for introducing a value
        /// without stomping one a human tuned. A parameter whose overrideState is
        /// already true was chosen deliberately — by the previous build or by a
        /// person in the Inspector — and a build that silently reverts tuning is
        /// the bug PaletteConfig exists to kill, arriving from the other side.
        /// </summary>
        private static void EnsureValue<T>(VolumeParameter<T> parameter, T value)
        {
            if (parameter.overrideState) return;
            parameter.overrideState = true;
            parameter.value = value;
        }

        private static void ApplySurface(Material material, float smoothness, float metallic,
            Texture2D? normalMap = null, float tiling = 1f, float normalScale = 1f)
        {
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);

            if (normalMap != null)
            {
                // URP Lit reads _BumpMap only with the _NORMALMAP keyword on —
                // the same trap as _EMISSION on the drone cores, where a plain
                // assignment produces a material that looks entirely untouched.
                material.SetTexture("_BumpMap", normalMap);
                material.SetFloat("_BumpScale", normalScale);
                material.EnableKeyword("_NORMALMAP");
                // URP Lit tiles the normal map from _BaseMap_ST, which is what
                // mainTextureScale writes, so one number drives both.
                material.mainTextureScale = new Vector2(tiling, tiling);
            }

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
            VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            ApplyPostFxDefaults(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// The shipped post-processing defaults, applied on every build but only
        /// where nobody has chosen otherwise.
        ///
        /// This used to run once, on the day the profile was created, which meant
        /// a new override could never reach an existing profile — you would have
        /// had to delete the asset to get it, and deleting it throws away every
        /// tuned value at the same time. EnsureOverride/EnsureValue make adding a
        /// default a non-event: absent overrides land, present ones are left
        /// exactly as they are.
        /// </summary>
        private static void ApplyPostFxDefaults(VolumeProfile profile)
        {
            // Neutral, NOT ACES. ACES desaturates and hue-shifts reds, and every
            // threat in this game is read by the colour of its core through fog —
            // Rusher red, Shooter amber, Tank crimson. Filmic rolloff is worth
            // having; losing the palette that carries the readability is not.
            Tonemapping tonemapping = EnsureOverride<Tonemapping>(profile);
            EnsureValue(tonemapping.mode, TonemappingMode.Neutral);

            // The one change that makes the existing emissive cores resolve.
            // High-quality filtering stays OFF: this targets a 4 GB 3050.
            Bloom bloom = EnsureOverride<Bloom>(profile);
            EnsureValue(bloom.threshold, 1.05f);
            EnsureValue(bloom.intensity, 0.35f);
            EnsureValue(bloom.scatter, 0.62f);
            EnsureValue(bloom.highQualityFiltering, false);
            // Pinned deliberately, and NOT left to the pipeline default. The PC
            // pipeline asset used to point at Unity's SampleSceneProfile as the
            // stack's base, and that template overrode bloom iteration count — so
            // bloom cost more and read tighter and hotter than every comment here
            // described, from a file nobody thought of as game content. The
            // template is deleted; pinning the value here makes it ours, so no
            // future base profile can move it without somebody noticing.
            // (skipIterations was the URP 16 spelling and is obsolete in 17.)
            EnsureValue(bloom.maxIterations, 6);

            Vignette vignette = EnsureOverride<Vignette>(profile);
            EnsureValue(vignette.intensity, 0.28f);
            EnsureValue(vignette.smoothness, 0.35f);

            // The grey/red tactical palette, pushed slightly.
            ColorAdjustments color = EnsureOverride<ColorAdjustments>(profile);
            EnsureValue(color.contrast, 8f);
            EnsureValue(color.saturation, -6f);

            // Nearly free, and it breaks up surfaces that carry no texture.
            FilmGrain grain = EnsureOverride<FilmGrain>(profile);
            EnsureValue(grain.type, FilmGrainLookup.Thin1);
            EnsureValue(grain.intensity, 0.15f);
            // ---- the grade -------------------------------------------------
            // Everything below folds into the 32^3 HDR grading LUT the pipeline
            // already builds every frame, so it costs no additional milliseconds
            // whatsoever. It is the best look-per-frame-time in the project, and
            // it is why Tonemapping stays Neutral: a LUT can add filmic rolloff
            // on top of Neutral without ACES's red hue shift.

            // Cool shadows, warm highlights. If there is one move that reads as
            // "modern military shooter" rather than "grey box with bloom", it is
            // this one. It also reinforces the palette rule the arena is built
            // on — cool is architecture, warm is a threat — by pushing the two
            // apart in every pixel rather than only in the emissive ones.
            ShadowsMidtonesHighlights split = EnsureOverride<ShadowsMidtonesHighlights>(profile);
            EnsureValue(split.shadows, new Vector4(0.92f, 0.97f, 1.08f, 0f));
            EnsureValue(split.midtones, new Vector4(1f, 1f, 1f, 0f));
            EnsureValue(split.highlights, new Vector4(1.06f, 1.01f, 0.93f, 0f));

            // Cools the whole image toward the tactical palette. Free in the LUT.
            WhiteBalance balance = EnsureOverride<WhiteBalance>(profile);
            EnsureValue(balance.temperature, -6f);

            // A small lift so the blacks do not crush to nothing underneath the
            // vignette and PlayerDamageFeedback's low-health tint, which stack on
            // the same corners of the screen. Crushed corners hide drones.
            LiftGammaGain levels = EnsureOverride<LiftGammaGain>(profile);
            EnsureValue(levels.lift, new Vector4(1f, 1f, 1.01f, 0.012f));

            // Reads as a lens rather than as a bug at this strength: roughly
            // three taps, and identity at screen centre so it never smears the
            // crosshair or the point of impact.
            ChromaticAberration aberration = EnsureOverride<ChromaticAberration>(profile);
            EnsureValue(aberration.intensity, 0.06f);

            // DELIBERATELY ABSENT, and each for a reason worth keeping written
            // down, because every one of them is a tempting one-click add:
            //   MotionBlur     - URP's is camera-only. On a fast mouse turn it
            //                    smears the whole screen, which in a horde game
            //                    hides the drone that is about to reach you.
            //                    It was sitting dormant in the template profile
            //                    that used to sit under this stack.
            //   DepthOfField   - 1.5-3 ms on a 3050 and it fights target
            //                    readability. ADS-only is defensible later;
            //                    always-on never is.
            //   PaniniProjection - for ultra-wide FOV. 62 vertical is not wide
            //                    enough to need it, and it is a fullscreen pass.
            //   ColorLookup    - wanted, but it needs an authored LUT strip
            //                    graded from a real screenshot. Do it from the
            //                    game, not from imagination.
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
        /// <summary>
        /// Pools the VFX prefabs that a DIFFERENT builder creates.
        ///
        /// VfxBuilder owns tracers, the wide muzzle flash, the burst-end smoke and
        /// the per-surface impacts, and this builder owns the pool. Left
        /// unbridged that split is a straight regression: ImpactConfig would point
        /// every wall hit at a prefab the pool has never seen, so ObjectPool.Spawn
        /// falls through to Instantiate on the firing path -- roughly twenty-odd
        /// allocations during the first sustained burst of every run, which is the
        /// precise GC hitch the pool exists to prevent, on the precise path it
        /// exists to protect. It would also leave the 24 prewarmed spark instances
        /// as dead weight.
        ///
        /// Looked up BY PATH and skipped when absent, so the two builders have no
        /// run-order dependency in either direction: run VfxBuilder first and
        /// these are pooled, run it later and the next Grey Box pass picks them
        /// up. A missing prefab is "not authored yet", never an error.
        ///
        /// Counts are per-surface impact lifetimes against fire rate, same
        /// arithmetic as the entries above.
        /// </summary>
        private static void AddVfxPrewarm(List<(GameObject prefab, int count)> prewarm)
        {
            (string name, int count)[] optional =
            {
                // One in three rounds at 900 rpm, alive for its flight time.
                ("Fx_Tracer", 24),
                // A one-round magazine at 55 RPM with a ~1 s flight never puts
                // more than two in the air. Four is the reload-cancel case plus
                // slack, and a rocket is one cube and one trail.
                ("Fx_Rocket", 4),
                ("Fx_MuzzleFlash_Wide", 4),
                ("Fx_MuzzleSmoke", 4),
                // Sized like the decal entry: the shotgun puts twelve impacts on
                // the board per pull, and a 2 s particle lifetime keeps them there.
                ("Fx_Impact_Concrete", 32),
                ("Fx_Impact_Metal", 32),
                ("Fx_Impact_Grate", 16),
                ("Fx_Impact_Flesh", 16),
            };

            foreach ((string name, int count) in optional)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + name + ".prefab");
                if (prefab != null) prewarm.Add((prefab, count));
            }
        }

        /// <summary>
        /// Every AttachmentConfig under Data/Attachments, in a stable order.
        ///
        /// Sorted by path rather than left in FindAssets order, which is not
        /// specified: an unsorted array means the sandbox console's fit order —
        /// and therefore the scene file — changes between machines for no reason,
        /// which is a diff nobody can explain.
        /// </summary>
        private static AttachmentConfig[] LoadAllAttachments()
        {
            string[] guids = AssetDatabase.FindAssets("t:AttachmentConfig", new[] { DataAttachments });
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);

            var found = new List<AttachmentConfig>(paths.Count);
            foreach (string path in paths)
            {
                var attachment = AssetDatabase.LoadAssetAtPath<AttachmentConfig>(path);
                if (attachment != null) found.Add(attachment);
            }
            return found.ToArray();
        }

        /// <summary>
        /// Writes a [SerializeField] private ARRAY of object references. SetRef's
        /// sibling, and it binds by the same unchecked string.
        /// </summary>
        private static void SetArray(Object target, string field, Object[] values)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new System.InvalidOperationException(
                    $"'{target.GetType().Name}' has no serialized field '{field}'.");
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

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
