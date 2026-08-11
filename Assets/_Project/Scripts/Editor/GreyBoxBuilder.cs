#nullable enable
using CoD.Core;
using CoD.Enemies;
using CoD.Player;
using CoD.UI;
using CoD.Weapons;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
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
        private const string Materials = "Assets/_Project/Art/Materials";
        private const string Prefabs = "Assets/_Project/Prefabs";
        private const string Scenes = "Assets/_Project/Scenes";
        private const string Audio = "Assets/_Project/Audio";

        private const string GreyBoxScenePath = Scenes + "/10_GreyBox.unity";
        private const string BootScenePath = Scenes + "/00_Boot.unity";
        private const string NavMeshPath = Scenes + "/NavMesh_GreyBox.asset";

        [MenuItem("CoD/Build Grey Box", false, 0)]
        public static void Build()
        {
            EnsureFolders();

            GameConfig game = LoadOrCreate<GameConfig>(DataGame + "/GameConfig.asset", ConfigureGame);
            HealthConfig targetHealth = LoadOrCreate<HealthConfig>(DataGame + "/Health_Target.asset", h =>
            {
                h.maxHealth = 100f;
            });
            ImpactConfig impact = LoadOrCreate<ImpactConfig>(DataGame + "/Impact_Default.asset", _ => { });
            WeaponConfig rifle = LoadOrCreate<WeaponConfig>(DataWeapons + "/AR_Standard.asset", ConfigureRifle);
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

            // Drone palette: a dark hull so the glowing core is the only thing the
            // eye tracks, and the core is what the telegraph tints.
            Material droneHull = LoadOrCreateMaterial(Materials + "/Drone_Hull.mat", new Color(0.13f, 0.14f, 0.17f));
            Material droneCore = LoadOrCreateEmissiveMaterial(Materials + "/Drone_Core.mat",
                new Color(0.75f, 0.12f, 0.10f), 1.6f);

            GameObject decal = BuildDecalPrefab(hot);
            GameObject sparks = BuildSparksPrefab();
            GameObject flash = BuildMuzzleFlashPrefab(hot);
            GameObject casing = BuildCasingPrefab(hot);
            GameObject dummy = BuildDummyTargetPrefab(targetMat, targetHealth);

            GameObject explosion = BuildExplosionPrefab(hot);
            GameObject droneDeath = BuildDroneDeathPrefab(hot);
            GameObject dronePrefab = BuildDronePrefab(droneHull, droneCore);

            DifficultyConfig difficulty = LoadOrCreate<DifficultyConfig>(DataGame + "/Difficulty.asset", ConfigureDifficulty);
            ContactDetonate detonate = LoadOrCreate<ContactDetonate>(
                DataAttacks + "/ContactDetonate_Std.asset", ConfigureContactDetonate);
            DroneConfig rusher = LoadOrCreate<DroneConfig>(DataDrones + "/Drone_Rusher.asset", ConfigureRusher);

            SetRef(detonate, "explosionVfx", explosion);
            SetRef(detonate, "alertClip", LoadClip("Drone_Alert"));
            EditorUtility.SetDirty(detonate);

            SetRef(rusher, "prefab", dronePrefab);
            SetRef(rusher, "attack", detonate);
            SetRef(rusher, "deathVfx", droneDeath);
            EditorUtility.SetDirty(rusher);

            var drones = new DroneAssets(rusher, dronePrefab, explosion, droneDeath, difficulty);

            SetRef(impact, "decalPrefab", decal);
            SetRef(impact, "particlePrefab", sparks);
            EditorUtility.SetDirty(impact);

            SetRef(rifle, "muzzleFlashPrefab", flash);
            SetRef(rifle, "shellCasingPrefab", casing);
            SetRef(rifle, "fireCloseLayer", LoadClip("Fire_AR_Close"));
            SetRef(rifle, "fireTailLayer", LoadClip("Fire_AR_Tail"));
            SetRef(rifle, "dryFireClip", LoadClip("DryFire"));
            SetRef(rifle, "reloadClip", LoadClip("Reload_AR"));
            EditorUtility.SetDirty(rifle);

            BuildGreyBoxScene(game, loadout, impact, grey, wall, targetMat, gunmetal, gunAccent,
                dummy, decal, sparks, flash, casing, drones);
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
            public readonly DroneConfig Rusher;
            public readonly GameObject Prefab;
            public readonly GameObject Explosion;
            public readonly GameObject DeathVfx;
            public readonly DifficultyConfig Difficulty;

            public DroneAssets(DroneConfig rusher, GameObject prefab, GameObject explosion,
                GameObject deathVfx, DifficultyConfig difficulty)
            {
                Rusher = rusher;
                Prefab = prefab;
                Explosion = explosion;
                DeathVfx = deathVfx;
                Difficulty = difficulty;
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
            config.deathVfxLifetime = 0.9f;
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

        /// <summary>
        /// The Rusher. A dark hull with one glowing core, because at 30 m through
        /// fog a silhouette plus a bright spot is all the player can actually
        /// read — and the core doubles as the weakpoint and the fuse telegraph.
        ///
        /// The NavMeshAgent ships DISABLED. A pooled agent enabled while its
        /// object sits off the navmesh throws on the first SetDestination, so the
        /// controller owns exactly when it comes alive.
        /// </summary>
        private static GameObject BuildDronePrefab(Material hull, Material core)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Drone_Rusher";
            root.transform.localScale = new Vector3(0.7f, 0.55f, 0.7f);
            MeshRenderer hullRenderer = root.GetComponent<MeshRenderer>();
            hullRenderer.sharedMaterial = hull;

            // Core: the weakpoint AND the telegraph. Sits forward so the reward for
            // aiming is on the face the player sees while it charges them.
            GameObject coreObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            coreObject.name = "Core";
            coreObject.transform.SetParent(root.transform, false);
            coreObject.transform.localPosition = new Vector3(0f, 0f, 0.42f);
            // Child scale compensates for the stretched parent, or the core comes
            // out as a slab rather than a cube.
            coreObject.transform.localScale = new Vector3(0.3f / 0.7f, 0.3f / 0.55f, 0.3f / 0.7f);
            MeshRenderer coreRenderer = coreObject.GetComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = core;

            GameObject finLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            finLeft.name = "Fin_L";
            finLeft.transform.SetParent(root.transform, false);
            finLeft.transform.localPosition = new Vector3(-0.6f, 0f, -0.1f);
            finLeft.transform.localScale = new Vector3(0.35f, 0.25f, 0.6f);
            finLeft.GetComponent<MeshRenderer>().sharedMaterial = hull;
            Object.DestroyImmediate(finLeft.GetComponent<Collider>());

            GameObject finRight = Object.Instantiate(finLeft, root.transform);
            finRight.name = "Fin_R";
            finRight.transform.localPosition = new Vector3(0.6f, 0f, -0.1f);

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;      // under the 0.5 the surface bakes for, so it fits everywhere the mesh exists
            agent.height = 1.2f;
            agent.baseOffset = 0.9f;
            agent.autoBraking = false;
            agent.enabled = false;

            Health health = root.AddComponent<Health>();  // max comes from DroneConfig at spawn

            Weakpoint weakpoint = coreObject.AddComponent<Weakpoint>();
            SetRef(weakpoint, "_owner", health);

            HitFlash hitFlash = root.AddComponent<HitFlash>();
            SetRef(hitFlash, "_renderer", hullRenderer);

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;      // the fuse has to come from a place in the room
            audio.maxDistance = 30f;
            audio.rolloffMode = AudioRolloffMode.Linear;

            root.AddComponent<PooledObject>();

            DroneController controller = root.AddComponent<DroneController>();
            SetRef(controller, "_agent", agent);
            SetRef(controller, "_health", health);
            SetRef(controller, "_pooled", root.GetComponent<PooledObject>());
            SetRef(controller, "_audio", audio);
            SetRef(controller, "_coreRenderer", coreRenderer);

            return SavePrefab(root, Prefabs + "/Drone_Rusher.prefab");
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

        private static void BuildGreyBoxScene(GameConfig game, PlayerLoadoutConfig loadout, ImpactConfig impact,
            Material floorMat, Material wallMat, Material targetMat, Material gunmetal, Material gunAccent,
            GameObject dummyPrefab, GameObject decal, GameObject sparks, GameObject flash, GameObject casing,
            DroneAssets drones)
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

            GameObject room = BuildRoom(floorMat, wallMat);
            BakeNavMesh(room);

            ObjectPool pool = new GameObject("ObjectPool").AddComponent<ObjectPool>();
            // Counts are sized for a full wave, not for the demo: the pool exists
            // so the first shot of round twelve costs the same as the first shot
            // of round one.
            SetPrewarm(pool,
                (decal, 48), (sparks, 24), (flash, 4), (casing, 24), (dummyPrefab, 8),
                (drones.Prefab, 24), (drones.Explosion, 8), (drones.DeathVfx, 8));

            (WeaponController weapon, PlayerLook look, Health playerHealth, Transform muzzle,
                Transform playerTransform, Transform cameraTransform) =
                BuildPlayerRig(game, loadout, impact, pool, gunmetal, gunAccent);

            BuildTargets(dummyPrefab, targetMat);
            (DroneSpawner spawner, DroneRegistry registry) = BuildDroneRig(drones, pool, playerTransform);
            BuildHud(weapon, playerHealth, game, pool, dummyPrefab, muzzle, spawner, registry, cameraTransform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, GreyBoxScenePath);
        }

        private static GameObject BuildRoom(Material floorMat, Material wallMat)
        {
            GameObject room = new("Room");

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(room.transform, false);
            floor.transform.localScale = new Vector3(40f, 0.5f, 40f);
            floor.transform.position = new Vector3(0f, -0.25f, 0f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Four walls plus a few cover blocks: enough to break line of sight,
            // which is what the arena will be made of later.
            AddBox(room, "Wall_N", new Vector3(0f, 2.5f, 20f), new Vector3(40f, 5f, 0.5f), wallMat);
            AddBox(room, "Wall_S", new Vector3(0f, 2.5f, -20f), new Vector3(40f, 5f, 0.5f), wallMat);
            AddBox(room, "Wall_E", new Vector3(20f, 2.5f, 0f), new Vector3(0.5f, 5f, 40f), wallMat);
            AddBox(room, "Wall_W", new Vector3(-20f, 2.5f, 0f), new Vector3(0.5f, 5f, 40f), wallMat);
            AddBox(room, "Cover_A", new Vector3(-6f, 1f, 6f), new Vector3(3f, 2f, 1f), wallMat);
            AddBox(room, "Cover_B", new Vector3(7f, 1.5f, 10f), new Vector3(1f, 3f, 4f), wallMat);
            AddBox(room, "Cover_C", new Vector3(0f, 0.75f, 14f), new Vector3(6f, 1.5f, 1f), wallMat);
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
                Debug.LogError("NavMesh bake produced no data — drones will spawn and never move.");
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
            SetRef(spawner, "_defaultDrone", drones.Rusher);
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
            Material gunmetal, Material gunAccent)
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
            cameraObject.AddComponent<AudioListener>();
            CameraShake shake = cameraObject.AddComponent<CameraShake>();

            PlayerLook look = player.AddComponent<PlayerLook>();
            SetRef(look, "_config", game);
            SetRef(look, "_input", input);
            SetRef(look, "_motor", motor);
            SetRef(look, "_cameraPivot", pivot.transform);
            SetRef(look, "_camera", camera);

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

        private static void BuildTargets(GameObject dummyPrefab, Material material)
        {
            GameObject root = new("Targets");
            Vector3[] spots =
            {
                new(-4f, 0.9f, 4f), new(0f, 0.9f, 8f), new(5f, 0.9f, 5f),
                new(-8f, 0.9f, 12f), new(9f, 0.9f, 14f),
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
            DroneSpawner spawner, DroneRegistry registry, Transform cameraTransform)
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

            CheatConsole console = canvasObject.AddComponent<CheatConsole>();
            SetRef(console, "_config", game);
            SetRef(console, "_weapon", weapon);
            SetRef(console, "_playerHealth", playerHealth);
            SetRef(console, "_pool", pool);
            SetRef(console, "_dummyTargetPrefab", dummyPrefab);
            SetRef(console, "_spawnOrigin", spawnOrigin);
            SetRef(console, "_droneSpawner", spawner);
            SetRef(console, "_droneRegistry", registry);
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
            TextAnchor alignment, Vector2 anchor)
        {
            GameObject label = new(name, typeof(RectTransform));
            label.transform.SetParent(parent.transform, false);
            Text text = label.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
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

        private static void BuildBootScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject boot = new("Boot");
            boot.AddComponent<BootLoader>();
            EditorSceneManager.SaveScene(scene, BootScenePath);
        }

        private static void RegisterScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(GreyBoxScenePath, true),
            };
        }

        // ---------- helpers ----------

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/_Project/Art", Materials, "Assets/_Project/Audio",
                "Assets/_Project/Data", DataGame, DataWeapons, DataDrones, DataAttacks, Audio,
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
