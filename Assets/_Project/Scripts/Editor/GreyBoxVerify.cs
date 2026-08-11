#nullable enable
using System.Collections.Generic;
using System.Text;
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
using UnityEngine.SceneManagement;

namespace CoD.EditorTools
{
    /// <summary>
    /// Re-opens the built scene and checks that every reference the grey box needs
    /// is actually wired, repairing the ScriptableObject links if they are missing.
    ///
    /// WHY THIS EXISTS
    /// The first headless build reported success and produced a scene where every
    /// config asset reference was null. Nothing errored: the components existed,
    /// scene-object references were fine, and only the asset links were missing —
    /// so Play would have silently done nothing, because every Update() early-returns
    /// without its config. A build that says "done" while producing a dead scene is
    /// worse than one that fails, so the build now verifies itself.
    /// </summary>
    public static class GreyBoxVerify
    {
        private const string GreyBoxScenePath = "Assets/_Project/Scenes/10_GreyBox.unity";

        [MenuItem("CoD/Verify and Repair Grey Box", false, 1)]
        public static void VerifyAndRepair()
        {
            Scene scene = EditorSceneManager.OpenScene(GreyBoxScenePath, OpenSceneMode.Single);
            var report = new StringBuilder();
            int repaired = 0;
            int missing = 0;

            GameConfig? game = Load<GameConfig>("Assets/_Project/Data/Game/GameConfig.asset");
            PlayerLoadoutConfig? loadout = Load<PlayerLoadoutConfig>("Assets/_Project/Data/Weapons/Loadout_Default.asset");
            ImpactConfig? impact = Load<ImpactConfig>("Assets/_Project/Data/Game/Impact_Default.asset");
            HealthConfig? health = Load<HealthConfig>("Assets/_Project/Data/Game/Health_Target.asset");
            DifficultyConfig? difficulty = Load<DifficultyConfig>("Assets/_Project/Data/Game/Difficulty.asset");
            DroneConfig? rusher = Load<DroneConfig>("Assets/_Project/Data/Drones/Drone_Rusher.asset");
            DroneConfig? shooter = Load<DroneConfig>("Assets/_Project/Data/Drones/Drone_Shooter.asset");
            DroneConfig? tank = Load<DroneConfig>("Assets/_Project/Data/Drones/Drone_Tank.asset");
            RangedBurst? rangedBurst = Load<RangedBurst>("Assets/_Project/Data/Attacks/RangedBurst_Std.asset");
            HeavySlam? heavySlam = Load<HeavySlam>("Assets/_Project/Data/Attacks/HeavySlam_Std.asset");
            NavMeshData? navMesh = AssetDatabase.LoadAssetAtPath<NavMeshData>(
                "Assets/_Project/Scenes/NavMesh_GreyBox.asset");
            ShopConfig? shop = Load<ShopConfig>("Assets/_Project/Data/Game/Shop.asset");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerMotor motor in root.GetComponentsInChildren<PlayerMotor>(true))
                    repaired += Ensure(motor, "_config", game, report, ref missing);
                foreach (PlayerLook look in root.GetComponentsInChildren<PlayerLook>(true))
                    repaired += Ensure(look, "_config", game, report, ref missing);
                foreach (WeaponController weapon in root.GetComponentsInChildren<WeaponController>(true))
                {
                    repaired += Ensure(weapon, "_loadout", loadout, report, ref missing);
                    repaired += Ensure(weapon, "_impact", impact, report, ref missing);
                }
                foreach (Health h in root.GetComponentsInChildren<Health>(true))
                {
                    // The player's Health reads GameConfig; everything else reads
                    // a HealthConfig. Repairing the wrong one hides a dead link.
                    repaired += h.GetComponent<PlayerMotor>() != null
                        ? Ensure(h, "_playerConfig", game, report, ref missing)
                        : Ensure(h, "_config", health, report, ref missing);
                }
                foreach (CheatConsole console in root.GetComponentsInChildren<CheatConsole>(true))
                    repaired += Ensure(console, "_config", game, report, ref missing);
                foreach (PlayerDamageFeedback feedback in root.GetComponentsInChildren<PlayerDamageFeedback>(true))
                    repaired += Ensure(feedback, "_config", game, report, ref missing);
                foreach (DroneSpawner spawner in root.GetComponentsInChildren<DroneSpawner>(true))
                {
                    // Asset references in a scene are exactly the ones that go
                    // missing silently — a null DroneConfig means the spawner runs,
                    // logs nothing, and produces no drones.
                    repaired += Ensure(spawner, "_difficulty", difficulty, report, ref missing);
                    repaired += Ensure(spawner, "_defaultDrone", rusher, report, ref missing);
                }
                foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
                    repaired += Ensure(surface, "m_NavMeshData", navMesh, report, ref missing);
                foreach (RunContext context in root.GetComponentsInChildren<RunContext>(true))
                    repaired += Ensure(context, "_config", game, report, ref missing);
                foreach (WaveRunner waveRunner in root.GetComponentsInChildren<WaveRunner>(true))
                {
                    repaired += Ensure(waveRunner, "_difficulty", difficulty, report, ref missing);
                    repaired += Ensure(waveRunner, "_shopConfig", shop, report, ref missing);
                }
            }

            if (repaired > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }

            // Re-open from disk: the only proof that a reference actually persisted
            // is reading it back after a round trip.
            scene = EditorSceneManager.OpenScene(GreyBoxScenePath, OpenSceneMode.Single);
            var stillNull = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerMotor motor in root.GetComponentsInChildren<PlayerMotor>(true))
                    Check(motor, "_config", stillNull);
                foreach (PlayerLook look in root.GetComponentsInChildren<PlayerLook>(true))
                    Check(look, "_config", stillNull);
                foreach (WeaponController weapon in root.GetComponentsInChildren<WeaponController>(true))
                {
                    Check(weapon, "_loadout", stillNull);
                    Check(weapon, "_impact", stillNull);
                    Check(weapon, "_look", stillNull);
                    Check(weapon, "_input", stillNull);
                    Check(weapon, "_pool", stillNull);
                    Check(weapon, "_muzzle", stillNull);
                }
                foreach (Health h in root.GetComponentsInChildren<Health>(true))
                    Check(h, h.GetComponent<PlayerMotor>() != null ? "_playerConfig" : "_config", stillNull);
                foreach (PlayerInput input in root.GetComponentsInChildren<PlayerInput>(true))
                    Check(input, "_actions", stillNull);
                foreach (Hitmarker marker in root.GetComponentsInChildren<Hitmarker>(true))
                {
                    Check(marker, "_weapon", stillNull);
                    Check(marker, "_hitClip", stillNull);
                    Check(marker, "_killClip", stillNull);
                }
                foreach (Crosshair crosshair in root.GetComponentsInChildren<Crosshair>(true))
                {
                    Check(crosshair, "_weapon", stillNull);
                    Check(crosshair, "_group", stillNull);
                }
                foreach (DroneSpawner spawner in root.GetComponentsInChildren<DroneSpawner>(true))
                {
                    Check(spawner, "_pool", stillNull);
                    Check(spawner, "_registry", stillNull);
                    Check(spawner, "_target", stillNull);
                    Check(spawner, "_difficulty", stillNull);
                    Check(spawner, "_defaultDrone", stillNull);
                    CheckArray(spawner, "_spawnPoints", stillNull);
                }
                foreach (PlayerDamageFeedback feedback in root.GetComponentsInChildren<PlayerDamageFeedback>(true))
                {
                    Check(feedback, "_config", stillNull);
                    Check(feedback, "_health", stillNull);
                    Check(feedback, "_flash", stillNull);
                    Check(feedback, "_lowHealthTint", stillNull);
                    Check(feedback, "_cameraTransform", stillNull);
                    Check(feedback, "_hurtClip", stillNull);
                    CheckArray(feedback, "_directionBars", stillNull);
                }
                foreach (CheatConsole console in root.GetComponentsInChildren<CheatConsole>(true))
                {
                    Check(console, "_droneSpawner", stillNull);
                    Check(console, "_droneRegistry", stillNull);
                }
                foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
                    Check(surface, "m_NavMeshData", stillNull);
                foreach (RunContext context in root.GetComponentsInChildren<RunContext>(true))
                {
                    Check(context, "_config", stillNull);
                    Check(context, "_playerHealth", stillNull);
                }
                foreach (WaveRunner waveRunner in root.GetComponentsInChildren<WaveRunner>(true))
                {
                    Check(waveRunner, "_run", stillNull);
                    Check(waveRunner, "_spawner", stillNull);
                    Check(waveRunner, "_registry", stillNull);
                    Check(waveRunner, "_difficulty", stillNull);
                    Check(waveRunner, "_shopConfig", stillNull);
                    Check(waveRunner, "_playerHealth", stillNull);
                    // An empty wave list is a run that never spawns anything, and
                    // reads in the Inspector exactly like a full one.
                    CheckArray(waveRunner, "_waves", stillNull);
                }
                foreach (WaveHud waveHud in root.GetComponentsInChildren<WaveHud>(true))
                {
                    Check(waveHud, "_runner", stillNull);
                    Check(waveHud, "_run", stillNull);
                    Check(waveHud, "_bannerLabel", stillNull);
                }
                foreach (ShopPanel shopPanel in root.GetComponentsInChildren<ShopPanel>(true))
                {
                    Check(shopPanel, "_runner", stillNull);
                    Check(shopPanel, "_run", stillNull);
                    Check(shopPanel, "_root", stillNull);
                    Check(shopPanel, "_offersLabel", stillNull);
                }
                foreach (GameOverPanel overPanel in root.GetComponentsInChildren<GameOverPanel>(true))
                {
                    Check(overPanel, "_runner", stillNull);
                    Check(overPanel, "_run", stillNull);
                    Check(overPanel, "_root", stillNull);
                }
                foreach (PlayerMotor motor in root.GetComponentsInChildren<PlayerMotor>(true))
                    Check(motor, "_run", stillNull);
                foreach (WeaponController weapon in root.GetComponentsInChildren<WeaponController>(true))
                    Check(weapon, "_run", stillNull);
            }

            // The drone assets themselves. A DroneConfig with no prefab is the
            // same silent failure one level up: the spawner is wired, the wave
            // runs, and nothing ever appears.
            CheckAssetRef(rusher, "prefab", stillNull);
            CheckAssetRef(rusher, "attack", stillNull);
            CheckAssetRef(rusher, "deathVfx", stillNull);
            CheckAssetRef(shooter, "prefab", stillNull);
            CheckAssetRef(shooter, "attack", stillNull);
            CheckAssetRef(tank, "prefab", stillNull);
            CheckAssetRef(tank, "attack", stillNull);
            // A Shooter with no projectile prefab aims, fires, and produces
            // nothing — the drone looks like it is working and does no damage.
            CheckAssetRef(rangedBurst, "projectilePrefab", stillNull);
            CheckAssetRef(heavySlam, "slamVfx", stillNull);

            Debug.Log($"GreyBoxVerify: repaired {repaired}, unresolved {stillNull.Count}\n{report}");
            if (stillNull.Count > 0)
            {
                Debug.LogError("GreyBoxVerify: STILL NULL after save+reload:\n  " + string.Join("\n  ", stillNull));
            }
            else
            {
                Debug.Log("GreyBoxVerify: every checked reference survived a save/reload round trip.");
            }
        }

        public static void VerifyHeadless()
        {
            try
            {
                VerifyAndRepair();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("GreyBoxVerify failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static T? Load<T>(string path) where T : ScriptableObject
            => AssetDatabase.LoadAssetAtPath<T>(path);

        private static int Ensure(Object target, string field, Object? value, StringBuilder report, ref int missing)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                report.AppendLine($"  {target.GetType().Name}.{field}: NO SUCH FIELD");
                missing++;
                return 0;
            }
            if (property.objectReferenceValue != null) return 0;
            if (value == null)
            {
                report.AppendLine($"  {target.GetType().Name}.{field}: null and no asset to assign");
                missing++;
                return 0;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            report.AppendLine($"  {target.GetType().Name}.{field}: repaired -> {value.name}");
            return 1;
        }

        /// <summary>An empty array is as dead as a null reference, and reads the same in the Inspector.</summary>
        private static void CheckArray(Object target, string field, List<string> stillNull)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
            {
                stillNull.Add($"{target.GetType().Name}.{field} (no such array)");
                return;
            }
            if (property.arraySize == 0)
            {
                stillNull.Add($"{target.GetType().Name}.{field} (empty)");
                return;
            }
            for (int i = 0; i < property.arraySize; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    stillNull.Add($"{target.GetType().Name}.{field}[{i}]");
                }
            }
        }

        private static void CheckAssetRef(Object? asset, string field, List<string> stillNull)
        {
            if (asset == null)
            {
                stillNull.Add($"(missing asset).{field}");
                return;
            }
            Check(asset, field, stillNull);
        }

        private static void Check(Object target, string field, List<string> stillNull)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null && property.objectReferenceValue == null)
            {
                stillNull.Add($"{target.GetType().Name}.{field}");
            }
        }
    }
}
