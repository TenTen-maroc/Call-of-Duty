#nullable enable
using System.Collections.Generic;
using System.IO;
using CoD.Core;
using CoD.Enemies;
using CoD.Player;
using CoD.Waves;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CoD.EditorTools
{
    /// <summary>
    /// Generates Tazir Pass from one layout asset. The proven grey-box runtime
    /// shell is cloned, while all environment geometry, navigation, human
    /// systems, cover, spawns, and mission markers are replaced here.
    /// </summary>
    public static class OutdoorArenaBuilder
    {
        public const string SceneName = "11_AtlasOutpost";
        public const string ScenePath = "Assets/_Project/Scenes/11_AtlasOutpost.unity";
        public const string NavMeshPath = "Assets/_Project/Scenes/NavMesh_AtlasOutpost.asset";
        private const string GreyBoxPath = "Assets/_Project/Scenes/10_GreyBox.unity";
        private const string ConfigPath = "Assets/_Project/Data/Arenas/Arena_TazirPassOutpost.asset";
        private const string MaterialsPath = "Assets/_Project/Art/Materials/Outdoor";

        private const string HumanConfigPath = "Assets/_Project/Data/Drones/Meridian_Rifleman.asset";
        private const string GoreProfilePath = "Assets/_Project/Data/Humans/Gore_Human.asset";

        [MenuItem("CoD/Build Tazir Pass Outpost", false, 3)]
        public static void Build()
        {
            RequireAsset<SceneAsset>(GreyBoxPath);
            EnsureFolder("Assets/_Project/Data/Arenas");
            EnsureFolder(MaterialsPath);

            OutdoorArenaConfig config = LoadOrCreateConfig();
            Scene source = EditorSceneManager.OpenScene(GreyBoxPath, OpenSceneMode.Single);
            if (!source.IsValid()) throw new System.InvalidOperationException("Could not open the grey-box source scene.");
            if (!EditorSceneManager.SaveScene(source, ScenePath, saveAsCopy: true))
                throw new System.InvalidOperationException("Could not create the Atlas outpost scene copy.");

            Scene scene = SceneManager.GetActiveScene();
            ReplaceEnvironment(config);
            ConfigurePlayer();
            ConfigureSpawnsAndHumanSystems(config);
            ConfigureMissionPlaces();
            ConfigureOutdoorAudio();
            RegisterScene();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifyOpenScene();
            Debug.Log("Tazir Pass Outpost built: 60 m outdoor arena, 10 hidden spawns, 14 cover points, humans and gore pooled.");
        }

        public static void BuildHeadless()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Outdoor arena build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void VerifyHeadless()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                VerifyOpenScene();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Outdoor arena verification failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static OutdoorArenaConfig LoadOrCreateConfig()
        {
            OutdoorArenaConfig? config = AssetDatabase.LoadAssetAtPath<OutdoorArenaConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<OutdoorArenaConfig>();
                ConfigureLayout(config);
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            else if (config.contentVersion < 2)
            {
                ConfigureLayout(config);
            }
            EditorUtility.SetDirty(config);
            return config;
        }

        private static void ConfigureLayout(OutdoorArenaConfig config)
        {
            config.displayLocation = "TAZIR PASS OUTPOST";
            config.contentVersion = 2;
            config.sunColor = new Color(1f, 0.75f, 0.52f);
            config.sunIntensity = 1.35f;
            config.sunEuler = new Vector3(42f, -32f, 0f);
            config.fogColor = new Color(0.34f, 0.4f, 0.43f);
            config.fogStart = 34f;
            config.fogEnd = 96f;
            config.blocks = new[]
            {
                Block("Ground", new Vector3(0f, -0.35f, 0f), new Vector3(60f, 0.7f, 60f), OutdoorArenaConfig.SurfaceKind.Soil),
                Block("Ridge_W", new Vector3(-30f, 2.5f, 0f), new Vector3(2f, 5f, 60f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("Ridge_E", new Vector3(30f, 2.5f, 0f), new Vector3(2f, 5f, 60f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("Ridge_S", new Vector3(0f, 2.5f, -30f), new Vector3(60f, 5f, 2f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("Ridge_NW", new Vector3(-19f, 2.5f, 30f), new Vector3(22f, 5f, 2f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("Ridge_NE", new Vector3(19f, 2.5f, 30f), new Vector3(22f, 5f, 2f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("CommsHut", new Vector3(0f, 2f, 4f), new Vector3(10f, 4f, 8f), OutdoorArenaConfig.SurfaceKind.Metal),
                Block("HutDoorCover", new Vector3(0f, 1f, -0.25f), new Vector3(3f, 2f, 0.6f), OutdoorArenaConfig.SurfaceKind.Metal),
                Block("Generator", new Vector3(-17f, 1.5f, 8f), new Vector3(6f, 3f, 5f), OutdoorArenaConfig.SurfaceKind.Metal),
                Block("GeneratorBarrier", new Vector3(-14f, 0.7f, 1f), new Vector3(7f, 1.4f, 0.8f), OutdoorArenaConfig.SurfaceKind.Metal),
                Block("WatchRock", new Vector3(17f, 2.2f, 10f), new Vector3(8f, 4.4f, 7f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("WatchBarrier", new Vector3(13f, 0.75f, 2f), new Vector3(7f, 1.5f, 0.8f), OutdoorArenaConfig.SurfaceKind.Wood),
                Block("LaneWest", new Vector3(-9f, 1.5f, -8f), new Vector3(2f, 3f, 11f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("LaneEast", new Vector3(9f, 1.5f, -8f), new Vector3(2f, 3f, 11f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("CrossCover_W", new Vector3(-8f, 0.65f, 16f), new Vector3(7f, 1.3f, 1f), OutdoorArenaConfig.SurfaceKind.Wood),
                Block("CrossCover_E", new Vector3(8f, 0.65f, 16f), new Vector3(7f, 1.3f, 1f), OutdoorArenaConfig.SurfaceKind.Metal),
                Block("RoadGate_W", new Vector3(-8f, 1.8f, 25f), new Vector3(1.2f, 3.6f, 6f), OutdoorArenaConfig.SurfaceKind.Rock),
                Block("RoadGate_E", new Vector3(8f, 1.8f, 25f), new Vector3(1.2f, 3.6f, 6f), OutdoorArenaConfig.SurfaceKind.Rock),
            };
            config.spawnPoints = new[]
            {
                Point("Spawn_NW_Ridge", new Vector3(-23f, 0f, 24f), Vector3.back, 0),
                Point("Spawn_N_GateW", new Vector3(-4f, 0f, 27f), Vector3.back, 1),
                Point("Spawn_N_GateE", new Vector3(4f, 0f, 27f), Vector3.back, 1),
                Point("Spawn_NE_Ridge", new Vector3(23f, 0f, 24f), Vector3.back, 2),
                Point("Spawn_W_Generator", new Vector3(-26f, 0f, 9f), Vector3.right, 0),
                Point("Spawn_E_Watch", new Vector3(26f, 0f, 10f), Vector3.left, 2),
                Point("Spawn_W_South", new Vector3(-25f, 0f, -15f), Vector3.right, 0),
                Point("Spawn_E_South", new Vector3(25f, 0f, -15f), Vector3.left, 2),
                Point("Spawn_NW_Hut", new Vector3(-9f, 0f, 10f), Vector3.back, 0),
                Point("Spawn_NE_Hut", new Vector3(9f, 0f, 10f), Vector3.back, 2),
            };
            config.coverPoints = new[]
            {
                Point("Cover_Hut_SW", new Vector3(-5.8f, 0f, -0.8f), Vector3.back, 0),
                Point("Cover_Hut_SE", new Vector3(5.8f, 0f, -0.8f), Vector3.back, 2),
                Point("Cover_Hut_NW", new Vector3(-5.8f, 0f, 8f), Vector3.forward, 0),
                Point("Cover_Hut_NE", new Vector3(5.8f, 0f, 8f), Vector3.forward, 2),
                Point("Cover_Generator_S", new Vector3(-17f, 0f, 4.6f), Vector3.back, 0),
                Point("Cover_Generator_E", new Vector3(-13.3f, 0f, 8f), Vector3.right, 1),
                Point("Cover_Watch_S", new Vector3(17f, 0f, 5.6f), Vector3.back, 2),
                Point("Cover_Watch_W", new Vector3(12.3f, 0f, 10f), Vector3.left, 1),
                Point("Cover_Cross_W1", new Vector3(-10f, 0f, 14.8f), Vector3.back, 0),
                Point("Cover_Cross_W2", new Vector3(-6f, 0f, 17.2f), Vector3.forward, 0),
                Point("Cover_Cross_E1", new Vector3(6f, 0f, 14.8f), Vector3.back, 2),
                Point("Cover_Cross_E2", new Vector3(10f, 0f, 17.2f), Vector3.forward, 2),
                Point("Cover_Gate_W", new Vector3(-6.5f, 0f, 23f), Vector3.back, 1),
                Point("Cover_Gate_E", new Vector3(6.5f, 0f, 23f), Vector3.back, 1),
            };
            config.decorations = Decorations();
        }

        private static OutdoorArenaConfig.Decoration[] Decorations()
        {
            const string nature = "Assets/_Project/Art/Imported/Kenney/Nature/";
            const string survival = "Assets/_Project/Art/Imported/Kenney/Survival/";
            return new[]
            {
                Decoration(nature + "tree_pineTallA.fbx", -24f, -22f, 1.35f, 18f, true, true),
                Decoration(nature + "tree_pineTallB.fbx", -18f, -25f, 1.1f, -12f, true, false),
                Decoration(nature + "tree_pineTallA.fbx", 23f, -22f, 1.25f, -22f, true, true),
                Decoration(nature + "tree_pineSmallA.fbx", 17f, -25f, 1.2f, 8f, true, false),
                Decoration(nature + "tree_pineTallB.fbx", -27f, 19f, 1.4f, 15f, true, true),
                Decoration(nature + "tree_pineTallA.fbx", 27f, 21f, 1.3f, -18f, true, true),
                Decoration(nature + "tree_pineSmallA.fbx", -18f, 21f, 1.15f, 32f, true, false),
                Decoration(nature + "tree_pineSmallA.fbx", 20f, 24f, 1.1f, -30f, true, false),
                Decoration(nature + "rock_largeA.fbx", -11f, -18f, 1.8f, 20f, true, true),
                Decoration(nature + "rock_largeC.fbx", 12f, -18f, 1.6f, -35f, true, true),
                Decoration(nature + "rock_tallA.fbx", 18f, 10f, 2.4f, 12f, true, true),
                Decoration(nature + "rock_largeC.fbx", -22f, 13f, 1.7f, 26f, true, false),
                Decoration(nature + "log_stack.fbx", -10f, 15f, 1.4f, 90f, false, false),
                Decoration(nature + "log_large.fbx", 10f, 15f, 1.4f, 82f, false, false),
                Decoration(nature + "plant_bushDetailed.fbx", -14f, -11f, 1.4f, 0f, false, false),
                Decoration(nature + "plant_bushDetailed.fbx", 14f, -12f, 1.5f, 0f, false, false),
                Decoration(nature + "plant_bushSmall.fbx", -21f, 2f, 1.5f, 0f, false, false),
                Decoration(nature + "plant_bushSmall.fbx", 22f, 1f, 1.5f, 0f, false, false),
                Decoration(survival + "structure-metal.fbx", 0f, 4f, 4.2f, 0f, false, true),
                Decoration(survival + "structure-metal-roof.fbx", 0f, 4f, 4.2f, 0f, false, true),
                Decoration(survival + "fence-fortified.fbx", -14f, 1f, 2.2f, 90f, false, true),
                Decoration(survival + "fence-fortified.fbx", 13f, 2f, 2.2f, 90f, false, true),
                Decoration(survival + "barrel.fbx", -17f, 5f, 1.7f, 0f, false, false),
                Decoration(survival + "box-large.fbx", 16f, 5f, 1.6f, 18f, false, false),
                Decoration(survival + "signpost.fbx", 0f, 22f, 2f, 0f, false, false),
            };
        }

        private static void ReplaceEnvironment(OutdoorArenaConfig config)
        {
            DestroyRoot("Room");
            DestroyRoot("Lights");
            DestroyRoot("Directional Light");
            DestroyRoot("Targets");
            DestroyRoot("MissionZones");

            Material soil = Material("Outdoor_Soil", new Color(0.25f, 0.22f, 0.16f), 0.05f, 0.45f);
            Material rock = Material("Outdoor_Rock", new Color(0.27f, 0.3f, 0.3f), 0.05f, 0.38f);
            Material wood = Material("Outdoor_Wood", new Color(0.25f, 0.17f, 0.1f), 0f, 0.32f);
            Material metal = Material("Outdoor_Metal", new Color(0.22f, 0.25f, 0.24f), 0.6f, 0.32f);

            GameObject sun = new("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = config.sunColor;
            light.intensity = config.sunIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;
            sun.transform.rotation = Quaternion.Euler(config.sunEuler);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.43f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.31f, 0.3f, 0.26f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.12f);
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = config.fogColor;
            RenderSettings.fogStartDistance = config.fogStart;
            RenderSettings.fogEndDistance = config.fogEnd;
            ConfigureSky();

            GameObject environment = new("OutdoorEnvironment");
            GameObject gameplay = new("GameplayGeometry");
            gameplay.transform.SetParent(environment.transform, false);
            for (int i = 0; i < config.blocks.Length; i++)
            {
                OutdoorArenaConfig.Block block = config.blocks[i];
                Material material = block.surface switch
                {
                    OutdoorArenaConfig.SurfaceKind.Rock => rock,
                    OutdoorArenaConfig.SurfaceKind.Wood => wood,
                    OutdoorArenaConfig.SurfaceKind.Metal => metal,
                    _ => soil,
                };
                AddGameplayBlock(gameplay.transform, block, material);
            }

            GameObject art = new("Art");
            art.transform.SetParent(environment.transform, false);
            for (int i = 0; i < config.decorations.Length; i++) AddDecoration(art.transform, config.decorations[i]);
            BakeNavMesh(gameplay);
        }

        private static void ConfigureSky()
        {
            Texture? skyTexture = AssetDatabase.LoadAssetAtPath<Texture>(
                "Assets/_Project/Art/Imported/PolyHaven/gamrig_1k.hdr");
            Shader? shader = skyTexture is Cubemap
                ? Shader.Find("Skybox/Cubemap")
                : Shader.Find("Skybox/Panoramic");
            if (skyTexture == null || shader == null) return;
            string path = MaterialsPath + "/Sky_Gamrig.mat";
            Material? sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(shader) { name = "Sky_Gamrig" };
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.shader = shader;
            sky.SetTexture(skyTexture is Cubemap ? "_Tex" : "_MainTex", skyTexture);
            sky.SetFloat("_Exposure", 0.85f);
            sky.SetFloat("_Rotation", 105f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.62f;
        }

        private static void AddGameplayBlock(Transform parent, OutdoorArenaConfig.Block block, Material material)
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = block.name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = block.position;
            gameObject.transform.localScale = block.size;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            gameObject.layer = block.surface switch
            {
                OutdoorArenaConfig.SurfaceKind.Rock => LayerMask.NameToLayer("Surface_Rock"),
                OutdoorArenaConfig.SurfaceKind.Wood => LayerMask.NameToLayer("Surface_Wood"),
                OutdoorArenaConfig.SurfaceKind.Metal => LayerMask.NameToLayer("Surface_Metal"),
                _ => LayerMask.NameToLayer("Surface_Soil"),
            };
            GameObjectUtility.SetStaticEditorFlags(gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        private static void AddDecoration(Transform parent, OutdoorArenaConfig.Decoration placement)
        {
            GameObject prefab = RequireAsset<GameObject>(placement.assetPath);
            GameObject art = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            art.name = System.IO.Path.GetFileNameWithoutExtension(placement.assetPath);
            art.transform.SetParent(parent, false);
            art.transform.position = placement.position;
            art.transform.rotation = Quaternion.Euler(placement.rotation);
            art.transform.localScale = placement.scale;
            Collider[] colliders = art.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Object.DestroyImmediate(colliders[i]);
            Renderer[] renderers = art.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = placement.castsShadow
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            GameObjectUtility.SetStaticEditorFlags(art, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            if (placement.lod && renderers.Length > 0)
            {
                LODGroup group = art.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.38f, renderers),
                    new LOD(0.12f, renderers),
                });
                group.fadeMode = LODFadeMode.CrossFade;
                group.RecalculateBounds();
            }
        }

        private static void BakeNavMesh(GameObject gameplay)
        {
            NavMeshSurface surface = gameplay.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                AssetDatabase.DeleteAsset(NavMeshPath);
                throw new System.InvalidOperationException("Atlas outpost NavMesh bake produced no data.");
            }
            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            AssetDatabase.SaveAssets();
            surface.navMeshData = RequireAsset<NavMeshData>(NavMeshPath);
            EditorUtility.SetDirty(surface);
        }

        private static void ConfigurePlayer()
        {
            GameObject player = RequireSceneObject("Player");
            player.transform.SetPositionAndRotation(new Vector3(0f, 1f, -25f), Quaternion.identity);
        }

        private static void ConfigureSpawnsAndHumanSystems(OutdoorArenaConfig config)
        {
            DroneSpawner spawner = Object.FindFirstObjectByType<DroneSpawner>()
                ?? throw new System.InvalidOperationException("Atlas scene has no DroneSpawner.");
            Transform oldPoints = spawner.transform.Find("SpawnPoints");
            if (oldPoints != null) Object.DestroyImmediate(oldPoints.gameObject);
            GameObject pointsRoot = new("SpawnPoints");
            pointsRoot.transform.SetParent(spawner.transform, false);
            var pointRefs = new Object[config.spawnPoints.Length];
            for (int i = 0; i < config.spawnPoints.Length; i++)
            {
                GameObject point = new(config.spawnPoints[i].name);
                point.transform.SetParent(pointsRoot.transform, false);
                point.transform.position = config.spawnPoints[i].position;
                pointRefs[i] = point.transform;
            }
            SetArray(spawner, "_spawnPoints", pointRefs);

            GameObject coverRoot = new("CoverSystem");
            CoverRegistry coverRegistry = coverRoot.AddComponent<CoverRegistry>();
            var covers = new CoverPoint[config.coverPoints.Length];
            for (int i = 0; i < config.coverPoints.Length; i++)
            {
                OutdoorArenaConfig.Point definition = config.coverPoints[i];
                GameObject point = new(definition.name);
                point.transform.SetParent(coverRoot.transform, false);
                point.transform.position = definition.position;
                covers[i] = point.AddComponent<CoverPoint>();
                covers[i].Configure(definition.outward, definition.lane);
            }
            coverRegistry.Configure(covers);

            ObjectPool pool = Object.FindFirstObjectByType<ObjectPool>()
                ?? throw new System.InvalidOperationException("Atlas scene has no ObjectPool.");
            SettingsHub settings = Object.FindFirstObjectByType<SettingsHub>()
                ?? throw new System.InvalidOperationException("Atlas scene has no SettingsHub.");
            GoreProfile goreProfile = RequireAsset<GoreProfile>(GoreProfilePath);
            GoreManager gore = new GameObject("GoreManager").AddComponent<GoreManager>();
            gore.Configure(goreProfile, pool, settings);
            spawner.ConfigureHumanSystems(coverRegistry, gore);

            DroneConfig human = RequireAsset<DroneConfig>(HumanConfigPath);
            if (human.prefab == null) throw new System.InvalidOperationException("Meridian Rifleman has no prefab.");
            DroneRegistry registry = spawner.GetComponent<DroneRegistry>();
            GameObject player = RequireSceneObject("Player");
            Health playerHealth = player.GetComponent<Health>();
            Mission2CaptureDirector capture = new GameObject("Mission2CaptureDirector")
                .AddComponent<Mission2CaptureDirector>();
            RangedBurst captureAttack = RequireAsset<RangedBurst>(
                "Assets/_Project/Data/Attacks/RangedBurst_Meridian.asset");
            capture.Configure(spawner, registry, human, player.transform, playerHealth, settings, captureAttack);
            AppendPrewarm(pool, human.prefab, 12,
                (goreProfile.bloodSprayPrefab, 24),
                (goreProfile.bloodDecalPrefab, goreProfile.bloodDecalCap),
                (goreProfile.woundPrefab, goreProfile.woundCap),
                (goreProfile.bloodPoolPrefab, goreProfile.bloodPoolCap),
                (goreProfile.stumpPrefab, goreProfile.woundCap),
                (goreProfile.severedPartPrefab, goreProfile.severedPartCap));
        }

        private static void ConfigureMissionPlaces()
        {
            MissionDirector director = Object.FindFirstObjectByType<MissionDirector>()
                ?? throw new System.InvalidOperationException("Atlas scene has no MissionDirector.");
            GameObject zones = new("MissionZones");
            Transform approach = NewMarker(zones.transform, "Zone_CommsHut", new Vector3(0f, 0.05f, -2f));
            Transform extract = NewMarker(zones.transform, "Zone_NorthExtraction", new Vector3(0f, 0.05f, 26f));
            SetZones(director, approach, extract);

            InteractableRegistry registry = Object.FindFirstObjectByType<InteractableRegistry>()
                ?? throw new System.InvalidOperationException("Atlas scene has no InteractableRegistry.");
            GameObject relay = GameObject.CreatePrimitive(PrimitiveType.Cube);
            relay.name = "OutpostRelay";
            relay.transform.SetPositionAndRotation(new Vector3(-16.5f, 1.2f, 4.8f), Quaternion.identity);
            relay.transform.localScale = new Vector3(1.3f, 2.4f, 1.1f);
            Object.DestroyImmediate(relay.GetComponent<Collider>());
            relay.GetComponent<MeshRenderer>().sharedMaterial = Material(
                "Outdoor_Relay", new Color(0.22f, 0.12f, 0.08f), 0.55f, 0.28f, new Color(0.9f, 0.12f, 0.06f));
            InteractPoint interact = relay.AddComponent<InteractPoint>();
            interact.Configure(registry, InteractKind.Terminal, "DISABLE OUTPOST RELAY", 2.5f, relay);
        }

        private static void ConfigureOutdoorAudio()
        {
            const string sourceFootstepsPath = "Assets/_Project/Data/Game/Footsteps_Player.asset";
            const string outdoorFootstepsPath = "Assets/_Project/Data/Arenas/Footsteps_Tazir.asset";
            const string sourceAmbiencePath = "Assets/_Project/Data/Game/Ambience_Arena.asset";
            const string outdoorAmbiencePath = "Assets/_Project/Data/Arenas/Ambience_Tazir.asset";
            const string windPath = "Assets/_Project/Audio/Generated/Tazir_Wind.wav";

            FootstepConfig sourceFootsteps = RequireAsset<FootstepConfig>(sourceFootstepsPath);
            FootstepConfig? footsteps = AssetDatabase.LoadAssetAtPath<FootstepConfig>(outdoorFootstepsPath);
            if (footsteps == null)
            {
                footsteps = Object.Instantiate(sourceFootsteps);
                footsteps.name = "Footsteps_Tazir";
                AssetDatabase.CreateAsset(footsteps, outdoorFootstepsPath);
            }
            AudioClip?[] earthClips = sourceFootsteps.surfaces.Length > 0
                ? sourceFootsteps.surfaces[0].stepClips : System.Array.Empty<AudioClip?>();
            AudioClip?[] metalClips = sourceFootsteps.surfaces.Length > 1
                ? sourceFootsteps.surfaces[1].stepClips : earthClips;
            footsteps.surfaces = new[]
            {
                Surface("Soil and dust", "Surface_Soil", earthClips, 0.9f, 0.92f),
                Surface("Rock", "Surface_Rock", earthClips, 1.05f, 0.86f),
                Surface("Wood", "Surface_Wood", earthClips, 0.8f, 1.08f),
                Surface("Metal", "Surface_Metal", metalClips, 1.15f, 1.08f),
            };
            footsteps.defaultSurface = 0;
            EditorUtility.SetDirty(footsteps);

            EnsureFolder("Assets/_Project/Audio/Generated");
            AudioClip wind = BuildWindLoop(windPath);
            AmbienceConfig sourceAmbience = RequireAsset<AmbienceConfig>(sourceAmbiencePath);
            AmbienceConfig? ambience = AssetDatabase.LoadAssetAtPath<AmbienceConfig>(outdoorAmbiencePath);
            if (ambience == null)
            {
                ambience = ScriptableObject.CreateInstance<AmbienceConfig>();
                ambience.name = "Ambience_Tazir";
                AssetDatabase.CreateAsset(ambience, outdoorAmbiencePath);
            }
            ambience.roomTone = wind;
            ambience.roomToneVolume = 0.22f;
            ambience.roomTonePitch = 1f;
            ambience.fadeInSeconds = 2.5f;
            ambience.randomiseStartTime = true;
            ambience.outputGroup = sourceAmbience.outputGroup;
            AudioClip? generator = sourceAmbience.emitters.Length > 3 ? sourceAmbience.emitters[3].clip : null;
            ambience.emitters = new[]
            {
                new AmbienceConfig.Emitter
                {
                    label = "Generator_Yard",
                    clip = generator,
                    localPosition = new Vector3(-17f, 1.5f, 8f),
                    volume = 0.28f,
                    pitch = 0.9f,
                    minDistance = 4f,
                    maxDistance = 24f,
                    spatialBlend = 1f,
                    spreadDegrees = 55f,
                    rolloff = AudioRolloffMode.Linear,
                },
            };
            EditorUtility.SetDirty(ambience);

            PlayerMotor motor = Object.FindFirstObjectByType<PlayerMotor>()
                ?? throw new System.InvalidOperationException("Atlas scene has no PlayerMotor.");
            Footsteps? existingSteps = motor.GetComponent<Footsteps>();
            Footsteps steps = existingSteps != null ? existingSteps : motor.gameObject.AddComponent<Footsteps>();
            AudioSource? existingSource = motor.GetComponent<AudioSource>();
            AudioSource source = existingSource != null ? existingSource : motor.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;

            ArenaAmbience? existingArena = Object.FindFirstObjectByType<ArenaAmbience>();
            ArenaAmbience arena = existingArena != null
                ? existingArena
                : new GameObject("Ambience").AddComponent<ArenaAmbience>();
            SetRef(steps, "_config", footsteps);
            SetRef(steps, "_motor", motor);
            SetRef(steps, "_audio", source);
            SetRef(arena, "_config", ambience);
        }

        private static FootstepConfig.SurfaceSet Surface(string label, string layerName,
            AudioClip?[] clips, float volume, float pitch) => new()
            {
                label = label,
                layers = 1 << LayerMask.NameToLayer(layerName),
                stepClips = clips,
                landClips = System.Array.Empty<AudioClip?>(),
                volumeScale = volume,
                pitchScale = pitch,
            };

        private static AudioClip BuildWindLoop(string path)
        {
            if (!File.Exists(path))
            {
                const int sampleRate = 22050;
                const int seconds = 12;
                int sampleCount = sampleRate * seconds;
                using FileStream stream = File.Create(path);
                using BinaryWriter writer = new(stream);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount * 2);
                uint state = 0x6d2b79f5u;
                float low = 0f;
                for (int i = 0; i < sampleCount; i++)
                {
                    state = state * 1664525u + 1013904223u;
                    float white = ((state >> 8) / 16777215f) * 2f - 1f;
                    low += (white - low) * 0.008f;
                    float envelope = 0.55f + 0.25f * Mathf.Sin(i * Mathf.PI * 2f / sampleRate / 5.7f);
                    writer.Write((short)Mathf.Clamp(low * envelope * 9000f, short.MinValue, short.MaxValue));
                }
                writer.Flush();
                stream.Flush();
                writer.Close();
                stream.Close();
            }
            if (AssetDatabase.LoadAssetAtPath<AudioClip>(path) == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    importer.forceToMono = true;
                    importer.loadInBackground = true;
                    importer.SaveAndReimport();
                }
            }
            return RequireAsset<AudioClip>(path);
        }

        private static void SetZones(MissionDirector director, Transform approach, Transform extract)
        {
            SerializedObject serialized = new(director);
            SerializedProperty zones = serialized.FindProperty("_zones");
            zones.arraySize = 2;
            SetZone(zones.GetArrayElementAtIndex(0), 0, approach, 5f);
            SetZone(zones.GetArrayElementAtIndex(1), 1, extract, 4f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetZone(SerializedProperty property, int id, Transform marker, float radius)
        {
            property.FindPropertyRelative("id").intValue = id;
            property.FindPropertyRelative("marker").objectReferenceValue = marker;
            property.FindPropertyRelative("radius").floatValue = radius;
        }

        private static Transform NewMarker(Transform parent, string name, Vector3 position)
        {
            GameObject marker = new(name);
            marker.transform.SetParent(parent, false);
            marker.transform.position = position;
            return marker.transform;
        }

        private static void AppendPrewarm(ObjectPool pool, GameObject human, int humanCount,
            params (GameObject? prefab, int count)[] extras)
        {
            SerializedObject serialized = new(pool);
            SerializedProperty array = serialized.FindProperty("_prewarm");
            var entries = new List<(GameObject prefab, int count)>(array.arraySize + extras.Length + 1);
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                GameObject? prefab = element.FindPropertyRelative("prefab").objectReferenceValue as GameObject;
                if (prefab != null && prefab != human) entries.Add((prefab, element.FindPropertyRelative("count").intValue));
            }
            entries.Add((human, humanCount));
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i].prefab != null) entries.Add((extras[i].prefab!, extras[i].count));
            }
            array.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
                element.FindPropertyRelative("count").intValue = entries[i].count;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pool);
        }

        private static Material Material(string name, Color color, float metallic, float smoothness,
            Color? emission = null)
        {
            string path = MaterialsPath + "/" + name + ".mat";
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new System.InvalidOperationException("URP Lit shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static OutdoorArenaConfig.Block Block(string name, Vector3 position, Vector3 size,
            OutdoorArenaConfig.SurfaceKind surface) => new()
            { name = name, position = position, size = size, surface = surface };

        private static OutdoorArenaConfig.Point Point(string name, Vector3 position, Vector3 outward, int lane)
            => new() { name = name, position = position, outward = outward, lane = lane };

        private static OutdoorArenaConfig.Decoration Decoration(string path, float x, float z,
            float scale, float yaw, bool lod, bool castsShadow) => new()
            {
                assetPath = path,
                position = new Vector3(x, 0f, z),
                rotation = new Vector3(0f, yaw, 0f),
                scale = Vector3.one * scale,
                lod = lod,
                castsShadow = castsShadow,
            };

        private static void SetArray(Object target, string field, Object[] values)
        {
            SerializedObject serialized = new(target);
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetRef(Object target, string field, Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void RegisterScene()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath) return;
            }
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void VerifyOpenScene()
        {
            RequireSceneObject("OutdoorEnvironment");
            RequireSceneObject("Player");
            DroneSpawner spawner = Object.FindFirstObjectByType<DroneSpawner>()
                ?? throw new System.InvalidOperationException("Missing DroneSpawner.");
            CoverRegistry covers = Object.FindFirstObjectByType<CoverRegistry>()
                ?? throw new System.InvalidOperationException("Missing CoverRegistry.");
            if (covers.Count < 14) throw new System.InvalidOperationException("Outdoor arena has fewer than 14 cover points.");
            if (Object.FindFirstObjectByType<GoreManager>() == null)
                throw new System.InvalidOperationException("Missing GoreManager.");
            if (Object.FindFirstObjectByType<NavMeshSurface>()?.navMeshData == null)
                throw new System.InvalidOperationException("Outdoor NavMesh is missing or unsaved.");
            SerializedObject serialized = new(spawner);
            if (serialized.FindProperty("_spawnPoints").arraySize < 8)
                throw new System.InvalidOperationException("Outdoor arena has fewer than eight spawn choices.");

            GameObject art = RequireSceneObject("Art");
            if (art.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new System.InvalidOperationException("Decorative outdoor art owns gameplay colliders.");
            if (RequireAsset<DroneConfig>(HumanConfigPath).prefab == null)
                throw new System.InvalidOperationException("Human config lost its prefab reference.");
        }

        private static GameObject RequireSceneObject(string name)
        {
            GameObject? found = GameObject.Find(name);
            return found ?? throw new System.InvalidOperationException("Missing scene object '" + name + "'.");
        }

        private static void DestroyRoot(string name)
        {
            GameObject? found = GameObject.Find(name);
            if (found != null) Object.DestroyImmediate(found);
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset ?? throw new System.InvalidOperationException("Required asset is missing: " + path);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            EnsureFolder(folder[..split]);
            AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
        }
    }
}
