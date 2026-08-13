#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using CoD.Core;
using CoD.Enemies;
using CoD.Weapons;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace CoD.EditorTools
{
    /// <summary>
    /// Owns the imported Humanoid binding, shared rifleman controller, tactical
    /// presentation, anatomical hit rig, ragdoll, and first-party gore prefabs.
    /// It does not own either arena or any mission data.
    /// </summary>
    public static class MeridianHumanBuilder
    {
        public const string HumanPrefabPath = "Assets/_Project/Prefabs/Humans/Meridian_Rifleman.prefab";
        public const string HumanConfigPath = "Assets/_Project/Data/Drones/Meridian_Rifleman.asset";
        public const string HumanCombatPath = "Assets/_Project/Data/Humans/HumanCombat_Rifleman.asset";
        public const string HitZoneConfigPath = "Assets/_Project/Data/Humans/HitZones_Human.asset";
        public const string GoreProfilePath = "Assets/_Project/Data/Humans/Gore_Human.asset";
        public const string AnimatorControllerPath = "Assets/_Project/Art/Animations/Meridian_Rifleman.controller";

        private const string CharacterModelPath =
            "Assets/_Project/Art/Imported/Quaternius/Characters/Meridian_Base_Male.fbx";
        private const string CharacterBaseColorPath =
            "Assets/_Project/Art/Imported/Quaternius/Characters/Meridian_BaseColor.png";
        private const string CharacterNormalPath =
            "Assets/_Project/Art/Imported/Quaternius/Characters/Meridian_Normal.png";
        private const string AnimationModelPath =
            "Assets/_Project/Art/Imported/Quaternius/Animations/Meridian_Animations.fbx";
        private const string StandardAttackPath = "Assets/_Project/Data/Attacks/RangedBurst_Std.asset";
        private const string HumanAttackPath = "Assets/_Project/Data/Attacks/RangedBurst_Meridian.asset";
        private const string ReactionsPath = "Assets/_Project/Data/Drones/Reactions_Drone_Standard.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Humans";
        private const string AnimationFolder = "Assets/_Project/Art/Animations";
        private const string HumanDataFolder = "Assets/_Project/Data/Humans";
        private const string HumanPrefabFolder = "Assets/_Project/Prefabs/Humans";
        private const string GorePrefabFolder = "Assets/_Project/Prefabs/Gore";
        private const string GoreTextureFolder = "Assets/_Project/Art/Textures/Gore";

        [MenuItem("CoD/Build Meridian Human", false, 4)]
        public static void Build()
        {
            EnsureFolders();
            ArtImportPostprocessor.EnsurePresets();
            Avatar avatar = ConfigureImports();

            HitZoneConfig zones = LoadOrCreate<HitZoneConfig>(HitZoneConfigPath, ConfigureHitZones);
            HumanCombatConfig combat = LoadOrCreate<HumanCombatConfig>(HumanCombatPath, ConfigureCombat);
            AnimatorController controller = BuildAnimator(avatar);

            Material skin = BuildCharacterMaterial();
            Material gear = BuildLitMaterial(MaterialFolder + "/Meridian_Gear.mat", new Color(0.22f, 0.25f, 0.18f),
                metallic: 0.08f, smoothness: 0.28f);
            Material armor = BuildLitMaterial(MaterialFolder + "/Meridian_Armor.mat", new Color(0.15f, 0.17f, 0.14f),
                metallic: 0.42f, smoothness: 0.34f);
            Material blood = BuildBloodMaterial();

            GameObject bloodSpray = BuildBloodSpray(blood);
            GameObject bloodDecal = BuildGoreQuad("Gore_BloodDecal", blood, new Vector3(0.34f, 0.34f, 0.34f));
            GameObject wound = BuildGoreQuad("Gore_Wound", blood, new Vector3(0.16f, 0.16f, 0.16f));
            GameObject bloodPool = BuildGoreQuad("Gore_BloodPool", blood, new Vector3(1.2f, 1.2f, 1.2f));
            GameObject stump = BuildStump(blood);
            GameObject part = BuildSeveredPart(skin, blood);

            GoreProfile gore = LoadOrCreate<GoreProfile>(GoreProfilePath, ConfigureGoreDefaults);
            gore.bloodSprayPrefab = bloodSpray;
            gore.bloodDecalPrefab = bloodDecal;
            gore.woundPrefab = wound;
            gore.bloodPoolPrefab = bloodPool;
            gore.stumpPrefab = stump;
            gore.severedPartPrefab = part;
            gore.worldMask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Surface_Flesh"));
            EditorUtility.SetDirty(gore);

            GameObject prefab = BuildHumanPrefab(avatar, controller, combat, zones, skin, gear, armor);
            RangedBurst attack = BuildAttack();
            DroneConfig human = LoadOrCreate<DroneConfig>(HumanConfigPath, ConfigureRifleman);
            human.prefab = prefab;
            human.attack = attack;
            human.reactions = AssetDatabase.LoadAssetAtPath<EnemyReactionConfig>(ReactionsPath);
            human.deathVfx = null;
            EditorUtility.SetDirty(human);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Meridian Rifleman built: shared Humanoid avatar/controller, regional hit rig, ragdoll, gore pools.");
        }

        public static void BuildHeadless()
        {
            try
            {
                Build();
                Verify();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("Meridian human build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void VerifyHeadless()
        {
            try
            {
                Verify();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("Meridian human verification failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Verify()
        {
            GameObject prefab = Require<GameObject>(HumanPrefabPath);
            DroneConfig config = Require<DroneConfig>(HumanConfigPath);
            AnimatorController controller = Require<AnimatorController>(AnimatorControllerPath);
            GoreProfile gore = Require<GoreProfile>(GoreProfilePath);

            if (config.prefab != prefab || config.attack is not RangedBurst)
                throw new InvalidOperationException("Meridian_Rifleman is not bound to its shared prefab and RangedBurst.");
            if (prefab.GetComponent<DroneController>() == null || prefab.GetComponent<HumanEnemyPresentation>() == null)
                throw new InvalidOperationException("Meridian prefab is missing the shared controller or human presentation.");
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator.avatar == null || !animator.avatar.isHuman || animator.runtimeAnimatorController != controller)
                throw new InvalidOperationException("Meridian prefab does not use the shared Humanoid avatar/controller.");
            if (prefab.GetComponentsInChildren<HitZone>(true).Length < 6 ||
                prefab.GetComponentsInChildren<Weakpoint>(true).Length != 1)
                throw new InvalidOperationException("Meridian anatomical hit rig is incomplete.");
            if (gore.bloodSprayPrefab == null || gore.bloodDecalPrefab == null || gore.woundPrefab == null ||
                gore.bloodPoolPrefab == null || gore.stumpPrefab == null || gore.severedPartPrefab == null)
                throw new InvalidOperationException("Gore profile has an incomplete pool binding.");
            if (prefab.GetComponentInChildren<LODGroup>(true) == null)
                throw new InvalidOperationException("Meridian prefab has no LODGroup.");
        }

        private static Avatar ConfigureImports()
        {
            AssetDatabase.ImportAsset(CharacterModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Avatar avatar = AvatarAt(CharacterModelPath) ?? throw new InvalidOperationException(
                "Quaternius character imported without a Humanoid Avatar.");

            if (AssetImporter.GetAtPath(AnimationModelPath) is not ModelImporter animationImporter)
                throw new InvalidOperationException("Quaternius animation FBX has no ModelImporter.");
            animationImporter.animationType = ModelImporterAnimationType.Human;
            animationImporter.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            animationImporter.sourceAvatar = avatar;
            animationImporter.importAnimation = true;
            animationImporter.SaveAndReimport();
            return avatar;
        }

        private static AnimatorController BuildAnimator(Avatar avatar)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath) != null)
                AssetDatabase.DeleteAsset(AnimatorControllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Telegraph", AnimatorControllerParameterType.Float);
            controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitRegion", AnimatorControllerParameterType.Int);
            controller.AddParameter("HitDirection", AnimatorControllerParameterType.Int);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DeathDirection", AnimatorControllerParameterType.Int);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            machine.states = Array.Empty<ChildAnimatorState>();

            AnimationClip idle = Clip("Idle_Loop");
            AnimationClip walk = Clip("Walk_Loop");
            AnimationClip jog = Clip("Jog_Fwd_Loop");
            AnimationClip aim = Clip("Pistol_Idle_Loop");
            AnimationClip telegraph = Clip("Pistol_Aim_Neutral");
            AnimationClip fire = Clip("Pistol_Shoot");
            AnimationClip reload = Clip("Pistol_Reload");
            AnimationClip hitHead = Clip("Hit_Head");
            AnimationClip hitTorso = Clip("Hit_Chest");
            AnimationClip death = Clip("Death01");

            BlendTree locomotion = new() { name = "DirectionalLocomotion", blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = "MoveX", blendParameterY = "MoveY", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(locomotion, controller);
            locomotion.AddChild(idle, Vector2.zero);
            locomotion.AddChild(walk, new Vector2(0f, 1f));
            locomotion.AddChild(walk, new Vector2(0f, -1f));
            locomotion.AddChild(walk, new Vector2(-1f, 0f));
            locomotion.AddChild(walk, new Vector2(1f, 0f));

            AnimatorState locomotionState = State(machine, "Locomotion", locomotion);
            machine.defaultState = locomotionState;
            AnimatorState jogState = State(machine, "Jog", jog);
            AnimatorState aimState = State(machine, "Aim", aim);
            AnimatorState telegraphState = State(machine, "AttackPrepare", telegraph);
            AnimatorState fireState = State(machine, "Fire", fire);
            AnimatorState reloadState = State(machine, "Reload", reload);

            Transition(locomotionState, jogState, AnimatorConditionMode.Greater, 0.62f, "Speed", false);
            Transition(jogState, locomotionState, AnimatorConditionMode.Less, 0.58f, "Speed", false);
            Transition(locomotionState, aimState, AnimatorConditionMode.If, 0f, "Aiming", false);
            Transition(jogState, aimState, AnimatorConditionMode.If, 0f, "Aiming", false);
            Transition(aimState, locomotionState, AnimatorConditionMode.IfNot, 0f, "Aiming", false);
            Transition(aimState, telegraphState, AnimatorConditionMode.Greater, 0.05f, "Telegraph", false);
            Transition(telegraphState, aimState, AnimatorConditionMode.Less, 0.05f, "Telegraph", false);
            AnyTransition(machine, fireState, "Attack");
            AnyTransition(machine, reloadState, "Reload");

            for (int region = 0; region <= (int)HitRegion.Armor; region++)
            {
                AnimationClip clip = region == (int)HitRegion.Head ? hitHead : hitTorso;
                AnimatorState state = State(machine, "Hit_" + ((HitRegion)region), clip);
                AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
                transition.hasExitTime = false;
                transition.duration = 0.05f;
                transition.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                transition.AddCondition(AnimatorConditionMode.Equals, region, "HitRegion");
            }

            for (int direction = 0; direction < 4; direction++)
            {
                AnimatorState state = State(machine, "Death_" + direction, death);
                state.mirror = direction == 1 || direction == 3;
                AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
                transition.hasExitTime = false;
                transition.duration = 0.04f;
                transition.AddCondition(AnimatorConditionMode.If, 0f, "Death");
                transition.AddCondition(AnimatorConditionMode.Equals, direction, "DeathDirection");
            }

            AssetDatabase.SaveAssets();
            _ = avatar;
            return controller;
        }

        private static GameObject BuildHumanPrefab(Avatar avatar, AnimatorController controller,
            HumanCombatConfig combat, HitZoneConfig zoneConfig, Material skin, Material gear, Material armor)
        {
            GameObject modelAsset = Require<GameObject>(CharacterModelPath);
            GameObject root = new("Meridian_Rifleman");
            int fleshLayer = LayerMask.NameToLayer("Surface_Flesh");
            root.layer = fleshLayer;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.enabled = false;
            agent.radius = 0.34f;
            agent.height = 1.82f;
            agent.baseOffset = 0f;
            Health health = root.AddComponent<Health>();
            PooledObject pooled = root.AddComponent<PooledObject>();
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 2f;
            audio.maxDistance = 32f;
            audio.rolloffMode = AudioRolloffMode.Linear;

            GameObject model = UnityEngine.Object.Instantiate(modelAsset);
            model.name = "Rig";
            model.transform.SetParent(root.transform, false);
            // Quaternius authors this pack in centimetres while Unity reports
            // the imported hierarchy in metres. Without the explicit prefab
            // scale, the torso sits below the arena plane and only the helmet and
            // shoulders peek through it. Keep the correction on the Art child so
            // the gameplay agent and hit volumes stay in metre-scale world space.
            model.transform.localPosition = Vector3.up * 0.95f;
            model.transform.localScale = Vector3.one * 1.8f;
            SetLayerRecursively(model, fleshLayer);
            if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            Renderer[] importedRenderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < importedRenderers.Length; i++)
            {
                Material[] materials = importedRenderers[i].sharedMaterials;
                for (int m = 0; m < materials.Length; m++) materials[m] = skin;
                importedRenderers[i].sharedMaterials = materials;
            }

            Transform head = Find(model.transform, "Head");
            Transform spine = Find(model.transform, "spine_03");
            Transform pelvis = Find(model.transform, "pelvis");
            Transform leftArm = Find(model.transform, "upperarm_l");
            Transform rightArm = Find(model.transform, "upperarm_r");
            Transform leftLeg = Find(model.transform, "thigh_l");
            Transform rightLeg = Find(model.transform, "thigh_r");

            var hitColliders = new List<Collider>(8);
            CapsuleCollider torso = root.AddComponent<CapsuleCollider>();
            torso.center = new Vector3(0f, 0.95f, 0f);
            torso.height = 1.7f;
            torso.radius = 0.3f;
            root.AddComponent<HitZone>().Configure(health, zoneConfig, HitRegion.Torso);
            hitColliders.Add(torso);
            hitColliders.Add(AddSphereZone(head, health, zoneConfig, HitRegion.Head, 0.16f, true));
            hitColliders.Add(AddCapsuleZone(leftArm, health, zoneConfig, HitRegion.LeftArm, 0.09f, 0.46f));
            hitColliders.Add(AddCapsuleZone(rightArm, health, zoneConfig, HitRegion.RightArm, 0.09f, 0.46f));
            hitColliders.Add(AddCapsuleZone(leftLeg, health, zoneConfig, HitRegion.LeftLeg, 0.12f, 0.62f));
            hitColliders.Add(AddCapsuleZone(rightLeg, health, zoneConfig, HitRegion.RightLeg, 0.12f, 0.62f));

            GameObject armorZone = new("ArmorHitZone");
            armorZone.layer = LayerMask.NameToLayer("Surface_Metal");
            armorZone.transform.SetParent(spine, false);
            armorZone.transform.localPosition = new Vector3(0f, 0.08f, 0.09f);
            BoxCollider plate = armorZone.AddComponent<BoxCollider>();
            plate.size = new Vector3(0.48f, 0.54f, 0.14f);
            armorZone.AddComponent<HitZone>().Configure(health, zoneConfig, HitRegion.Armor);
            hitColliders.Add(plate);

            var gearRenderers = new List<Renderer>(12);
            AddGearPrimitive(spine, "Vest", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0f),
                new Vector3(0.54f, 0.54f, 0.28f), armor, gearRenderers);
            AddGearPrimitive(spine, "Backpack", PrimitiveType.Cube, new Vector3(0f, 0.02f, -0.2f),
                new Vector3(0.42f, 0.52f, 0.2f), gear, gearRenderers);
            AddGearPrimitive(head, "Helmet", PrimitiveType.Sphere, new Vector3(0f, 0.09f, 0f),
                new Vector3(0.34f, 0.22f, 0.38f), armor, gearRenderers);
            for (int i = -1; i <= 1; i++)
            {
                AddGearPrimitive(spine, "Pouch_" + i, PrimitiveType.Cube, new Vector3(i * 0.16f, -0.2f, 0.17f),
                    new Vector3(0.13f, 0.15f, 0.09f), gear, gearRenderers);
            }
            AddGearPrimitive(rightArm, "Rifle", PrimitiveType.Cube, new Vector3(0.05f, -0.35f, 0.12f),
                new Vector3(0.09f, 0.65f, 0.1f), armor, gearRenderers);

            EnemyAnimator enemyAnimator = root.AddComponent<EnemyAnimator>();
            enemyAnimator.Configure(animator, combat);
            HumanVisualVariant variant = root.AddComponent<HumanVisualVariant>();
            variant.Configure(combat, gearRenderers.ToArray());

            BuildLods(root, importedRenderers, gearRenderers);
            BuildRagdoll(pelvis, head, spine, leftArm, rightArm, leftLeg, rightLeg,
                out Rigidbody[] ragdollBodies, out Collider[] ragdollColliders);

            HumanEnemyPresentation human = root.AddComponent<HumanEnemyPresentation>();
            human.Configure(combat, enemyAnimator, hitColliders.ToArray(), ragdollBodies, ragdollColliders,
                head, leftArm, rightArm, leftLeg, rightLeg);

            DroneController controllerComponent = root.AddComponent<DroneController>();
            SetRef(controllerComponent, "_agent", agent);
            SetRef(controllerComponent, "_animator", enemyAnimator);
            SetRef(controllerComponent, "_human", human);
            SetRef(controllerComponent, "_health", health);
            SetRef(controllerComponent, "_pooled", pooled);
            SetRef(controllerComponent, "_audio", audio);

            PrefabUtility.SaveAsPrefabAsset(root, HumanPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return Require<GameObject>(HumanPrefabPath);
        }

        private static RangedBurst BuildAttack()
        {
            RangedBurst source = Require<RangedBurst>(StandardAttackPath);
            RangedBurst attack = LoadOrCreate<RangedBurst>(HumanAttackPath, created =>
            {
                EditorUtility.CopySerialized(source, created);
                created.name = "RangedBurst_Meridian";
                created.triggerRange = 28f;
                created.reactionDelay = 0.48f;
                created.aimHeightOffset = 1.35f;
                created.burstCount = 4;
                created.burstInterval = 0.14f;
                created.cooldown = 1.35f;
                created.accuracy = 0.74f;
                created.firstShotMissDegrees = 6f;
                created.damage = 10f;
                created.projectileSpeed = 24f;
                created.reloadEveryBursts = 3;
                created.reloadSeconds = 1.55f;
            });
            attack.projectilePrefab = source.projectilePrefab;
            attack.fireClip = source.fireClip;
            EditorUtility.SetDirty(attack);
            return attack;
        }

        private static void ConfigureRifleman(DroneConfig config)
        {
            config.stableId = "meridian_rifleman";
            config.displayName = "Meridian Rifleman";
            config.maxHealth = 115f;
            config.moveSpeed = 4.5f;
            config.acceleration = 18f;
            config.turnSpeed = 540f;
            config.hoverHeight = 0f;
            config.preferredRange = 18f;
            config.stopDistance = 1.1f;
            config.repathInterval = 0.18f;
            config.scoreValue = 24;
            config.moneyReward = 20;
            config.idleCoreColor = new Color(0.65f, 0.08f, 0.06f);
            config.telegraphCoreColor = new Color(1f, 0.25f, 0.12f);
            config.idleEmission = 0.2f;
            config.telegraphEmission = 4.2f;
        }

        private static void ConfigureCombat(HumanCombatConfig config)
        {
            config.speedAtFullBlend = 4.5f;
            config.speedDamping = 0.1f;
            config.variantA = new Color(0.20f, 0.23f, 0.16f);
            config.variantB = new Color(0.34f, 0.27f, 0.16f);
            config.decisionInterval = 0.65f;
            config.coverChecksPerDecision = 8;
            config.coverArrivalDistance = 0.7f;
            config.coverSearchRadius = 22f;
            config.flankLaneBonus = 3f;
            config.firingSpeedMultiplier = 0f;
            config.facingDegreesPerSecond = 320f;
            config.betweenBurstStrafeSeconds = 0.9f;
            config.strafeDistance = 2.2f;
            config.suppressionSeconds = 0.85f;
            config.aimDisruptionSeconds = 0.42f;
            config.legStumbleSeconds = 0.55f;
            config.legStumbleSpeedMultiplier = 0.42f;
            config.corpseLifetime = 12f;
            config.ragdollLifetime = 6f;
            config.bloodPoolDelay = 1.2f;
        }

        private static void ConfigureHitZones(HitZoneConfig config)
        {
            config.entries = new[]
            {
                Zone(HitRegion.Head, 1f, true),
                Zone(HitRegion.Torso, 1f, true),
                Zone(HitRegion.LeftArm, 0.75f, true),
                Zone(HitRegion.RightArm, 0.75f, true),
                Zone(HitRegion.LeftLeg, 0.70f, true),
                Zone(HitRegion.RightLeg, 0.70f, true),
                Zone(HitRegion.Armor, 0.45f, false),
            };
        }

        private static HitZoneConfig.Entry Zone(HitRegion region, float factor, bool flesh) => new()
        {
            region = region,
            damageFactor = factor,
            fleshImpact = flesh,
        };

        private static void ConfigureGoreDefaults(GoreProfile profile)
        {
            profile.sprayLifetime = 1.2f;
            profile.decalLifetime = 18f;
            profile.woundLifetime = 14f;
            profile.poolDelay = 1.2f;
            profile.poolLifetime = 20f;
            profile.severedPartLifetime = 10f;
            profile.headDismemberDamage = 55f;
            profile.limbDismemberDamage = 65f;
            profile.explosiveImpulse = 4.5f;
            profile.bloodDecalCap = 96;
            profile.woundCap = 24;
            profile.bloodPoolCap = 12;
            profile.corpseCap = 8;
            profile.ragdollCap = 4;
            profile.severedPartCap = 24;
            profile.surfaceProjectionDistance = 3f;
            profile.surfaceOffset = 0.01f;
        }

        private static Material BuildCharacterMaterial()
        {
            Material material = BuildLitMaterial(MaterialFolder + "/Meridian_Body.mat", Color.white,
                metallic: 0f, smoothness: 0.22f);
            Texture2D baseColor = Require<Texture2D>(CharacterBaseColorPath);
            Texture2D normal = Require<Texture2D>(CharacterNormalPath);
            material.SetTexture("_BaseMap", baseColor);
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildBloodMaterial()
        {
            Material material = BuildLitMaterial(MaterialFolder + "/Gore_Blood.mat",
                new Color(0.30f, 0.015f, 0.012f), metallic: 0f, smoothness: 0.32f);
            material.SetTexture("_BaseMap", BuildGoreAtlas());
            material.SetTexture("_MainTex", BuildGoreAtlas());
            material.SetColor("_Color", new Color(0.30f, 0.015f, 0.012f, 0.9f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D BuildGoreAtlas()
        {
            const string path = GoreTextureFolder + "/Gore_Atlas.png";
            if (!File.Exists(path))
            {
                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float nx = (x - size * 0.5f) / (size * 0.5f);
                        float ny = (y - size * 0.5f) / (size * 0.5f);
                        float radius = Mathf.Sqrt(nx * nx + ny * ny);
                        float noise = Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                        float edge = Mathf.Clamp01((0.92f - radius + (noise - 0.5f) * 0.34f) * 7f);
                        byte alpha = (byte)Mathf.RoundToInt(edge * 238f);
                        pixels[y * size + x] = new Color32((byte)(92 + noise * 24f), 5, 4, alpha);
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.alphaIsTransparency = true;
                    importer.sRGBTexture = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.maxTextureSize = 128;
                    importer.SaveAndReimport();
                }
            }
            return Require<Texture2D>(path);
        }

        private static Material BuildLitMaterial(string path, Color color, float metallic, float smoothness)
        {
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                    throw new InvalidOperationException("URP Lit shader is unavailable.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject BuildBloodSpray(Material blood)
        {
            GameObject root = new("Gore_BloodSpray");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.35f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = 0.5f;
            main.startSpeed = 3.4f;
            main.startSize = 0.055f;
            main.startColor = new Color(0.45f, 0.02f, 0.015f, 0.92f);
            main.maxParticles = 22;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = 0.02f;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = blood;
            root.AddComponent<PooledObject>();
            return SavePrefab(root, GorePrefabFolder + "/Gore_BloodSpray.prefab");
        }

        private static GameObject BuildGoreQuad(string name, Material material, Vector3 scale)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Quad);
            root.name = name;
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());
            root.transform.localScale = scale;
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            root.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            root.AddComponent<PooledObject>();
            return SavePrefab(root, GorePrefabFolder + "/" + name + ".prefab");
        }

        private static GameObject BuildStump(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            root.name = "Gore_Stump";
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());
            root.transform.localScale = new Vector3(0.11f, 0.035f, 0.11f);
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            root.AddComponent<PooledObject>();
            return SavePrefab(root, GorePrefabFolder + "/Gore_Stump.prefab");
        }

        private static GameObject BuildSeveredPart(Material skin, Material blood)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "Gore_SeveredPart";
            // Parts may collide with the world, but never participate in weapon
            // raycasts: aftermath cannot become bulletproof cover.
            root.layer = LayerMask.NameToLayer("Ignore Raycast");
            root.transform.localScale = new Vector3(0.15f, 0.36f, 0.15f);
            root.GetComponent<MeshRenderer>().sharedMaterial = skin;
            CapsuleCollider collider = root.GetComponent<CapsuleCollider>();
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 1.2f;
            GameObject end = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            end.name = "Wound";
            UnityEngine.Object.DestroyImmediate(end.GetComponent<Collider>());
            end.transform.SetParent(root.transform, false);
            end.transform.localPosition = new Vector3(0f, 0.96f, 0f);
            end.transform.localScale = new Vector3(0.8f, 0.05f, 0.8f);
            end.GetComponent<MeshRenderer>().sharedMaterial = blood;
            _ = collider;
            root.AddComponent<PooledObject>();
            root.AddComponent<PooledGorePart>().Configure(body);
            return SavePrefab(root, GorePrefabFolder + "/Gore_SeveredPart.prefab");
        }

        private static Collider AddSphereZone(Transform bone, Health health, HitZoneConfig config,
            HitRegion region, float radius, bool weakpoint)
        {
            GameObject zone = new(region + "HitZone");
            zone.layer = LayerMask.NameToLayer("Surface_Flesh");
            zone.transform.SetParent(bone, false);
            SphereCollider collider = zone.AddComponent<SphereCollider>();
            collider.radius = radius;
            if (weakpoint)
            {
                Weakpoint point = zone.AddComponent<Weakpoint>();
                SetRef(point, "_owner", health);
                // The existing weakpoint remains the sole owner of the weapon's
                // headshot bonus. HitZone coexists as anatomical metadata; the
                // resolver takes the Weakpoint branch first and never multiplies
                // this 1.0 factor a second time.
                zone.AddComponent<HitZone>().Configure(health, config, region);
            }
            else
            {
                zone.AddComponent<HitZone>().Configure(health, config, region);
            }
            return collider;
        }

        private static Collider AddCapsuleZone(Transform bone, Health health, HitZoneConfig config,
            HitRegion region, float radius, float height)
        {
            GameObject zone = new(region + "HitZone");
            zone.layer = LayerMask.NameToLayer("Surface_Flesh");
            zone.transform.SetParent(bone, false);
            CapsuleCollider collider = zone.AddComponent<CapsuleCollider>();
            collider.radius = radius;
            collider.height = height;
            collider.direction = 1;
            zone.AddComponent<HitZone>().Configure(health, config, region);
            return collider;
        }

        private static void AddGearPrimitive(Transform parent, string name, PrimitiveType primitive,
            Vector3 localPosition, Vector3 localScale, Material material, List<Renderer> renderers)
        {
            GameObject gear = GameObject.CreatePrimitive(primitive);
            gear.name = name;
            UnityEngine.Object.DestroyImmediate(gear.GetComponent<Collider>());
            gear.transform.SetParent(parent, false);
            gear.transform.localPosition = localPosition;
            gear.transform.localRotation = Quaternion.identity;
            gear.transform.localScale = localScale;
            Renderer renderer = gear.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderers.Add(renderer);
        }

        private static void BuildLods(GameObject root, Renderer[] body, List<Renderer> gear)
        {
            var lod0 = new List<Renderer>(body.Length + gear.Count);
            lod0.AddRange(body);
            lod0.AddRange(gear);
            var lod1 = new List<Renderer>(body.Length);
            var lod2 = new List<Renderer>(body.Length);

            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] is not SkinnedMeshRenderer source) continue;
                SkinnedMeshRenderer middle = DuplicateSkinnedRenderer(source, "LOD1", false);
                SkinnedMeshRenderer far = DuplicateSkinnedRenderer(source, "LOD2", false);
                lod1.Add(middle);
                lod2.Add(far);
            }

            LODGroup group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(new[]
            {
                new LOD(0.22f, lod0.ToArray()),
                new LOD(0.10f, lod1.ToArray()),
                new LOD(0.035f, lod2.ToArray()),
            });
            group.RecalculateBounds();
        }

        private static SkinnedMeshRenderer DuplicateSkinnedRenderer(SkinnedMeshRenderer source, string suffix,
            bool shadows)
        {
            GameObject duplicate = new(source.name + "_" + suffix);
            duplicate.layer = source.gameObject.layer;
            duplicate.transform.SetParent(source.transform.parent, false);
            SkinnedMeshRenderer renderer = duplicate.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = source.sharedMesh;
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.bones = source.bones;
            renderer.rootBone = source.rootBone;
            renderer.localBounds = source.localBounds;
            renderer.updateWhenOffscreen = source.updateWhenOffscreen;
            renderer.quality = source.quality;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            return renderer;
        }

        private static void BuildRagdoll(Transform pelvis, Transform head, Transform spine,
            Transform leftArm, Transform rightArm, Transform leftLeg, Transform rightLeg,
            out Rigidbody[] bodies, out Collider[] colliders)
        {
            Transform[] bones = { pelvis, spine, head, leftArm, rightArm, leftLeg, rightLeg };
            bodies = new Rigidbody[bones.Length];
            colliders = new Collider[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i].gameObject.TryGetComponent(out Rigidbody body))
                    body = bones[i].gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.mass = i == 0 ? 4f : 1.2f;
                bodies[i] = body;

                GameObject collision = new("RagdollCollider");
                collision.layer = LayerMask.NameToLayer("Ignore Raycast");
                collision.transform.SetParent(bones[i], false);
                CapsuleCollider capsule = collision.AddComponent<CapsuleCollider>();
                capsule.radius = i == 0 || i == 1 ? 0.18f : 0.11f;
                capsule.height = i == 0 || i == 1 ? 0.45f : 0.35f;
                capsule.enabled = false;
                colliders[i] = capsule;

                if (i == 0) continue;
                CharacterJoint joint = bones[i].gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = bodies[i == 1 || i == 3 || i == 4 || i == 5 || i == 6 ? 0 : 1];
                joint.enableCollision = false;
            }
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = motion;
            return state;
        }

        private static void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode,
            float threshold, string parameter, bool exitTime)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = exitTime;
            transition.duration = 0.08f;
            transition.AddCondition(mode, threshold, parameter);
        }

        private static void AnyTransition(AnimatorStateMachine machine, AnimatorState to, string trigger)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.04f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static AnimationClip Clip(string suffix)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(AnimationModelPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && clip.name.EndsWith(suffix, StringComparison.Ordinal)) return clip;
            }
            throw new InvalidOperationException("Animation library is missing required clip: " + suffix);
        }

        private static Avatar? AvatarAt(string path)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar avatar) return avatar;
            }
            return null;
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }
            throw new InvalidOperationException("Imported Quaternius rig has no bone named " + name + ".");
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = layer;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return Require<GameObject>(path);
        }

        private static T LoadOrCreate<T>(string path, Action<T> configure) where T : ScriptableObject
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            configure(asset);
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
            => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing required asset: " + path);

        private static void SetRef(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty field = serialized.FindProperty(property) ?? throw new InvalidOperationException(
                target.GetType().Name + " has no serialized field " + property + ".");
            field.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                HumanDataFolder, HumanPrefabFolder, GorePrefabFolder, MaterialFolder,
                AnimationFolder, GoreTextureFolder,
            };
            for (int i = 0; i < folders.Length; i++) EnsureFolder(folders[i]);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path[..split];
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path[(split + 1)..]);
        }
    }
}
