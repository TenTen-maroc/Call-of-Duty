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
        private const string MainMenuScenePath = "Assets/_Project/Scenes/20_MainMenu.unity";

        [MenuItem("CoD/Verify and Repair Grey Box", false, 1)]
        public static void VerifyAndRepair() => VerifyAndReport();

        /// <summary>
        /// The same pass, but it TELLS you: the number of references still
        /// unresolved after the save/reload round trip. VerifyAndRepair has to
        /// return void to be a [MenuItem], and that void was how a proven-broken
        /// scene exited zero — see VerifyHeadless.
        /// </summary>
        public static int VerifyAndReport()
        {
            Scene scene = EditorSceneManager.OpenScene(GreyBoxScenePath, OpenSceneMode.Single);
            var report = new StringBuilder();
            int repaired = 0;
            int missing = 0;

            GameConfig? game = Load<GameConfig>("Assets/_Project/Data/Game/GameConfig.asset");
            SettingsConfig? settings = Load<SettingsConfig>("Assets/_Project/Data/Game/Settings.asset");
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
            ObjectiveConfig? objectiveConfig = Load<ObjectiveConfig>(
                "Assets/_Project/Data/Game/Objective_Beacon.asset");
            Explosive? explosive = Load<Explosive>("Assets/_Project/Data/Effects/Effect_Explosive.asset");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerMotor motor in root.GetComponentsInChildren<PlayerMotor>(true))
                    repaired += Ensure(motor, "_config", game, report, ref missing);
                foreach (PlayerLook look in root.GetComponentsInChildren<PlayerLook>(true))
                    repaired += Ensure(look, "_config", game, report, ref missing);
                foreach (SettingsHub service in root.GetComponentsInChildren<SettingsHub>(true))
                {
                    // A SettingsHub with no bounds falls back to a throwaway
                    // SettingsConfig at runtime: every slider still moves, and
                    // every value it produces is wrong.
                    repaired += Ensure(service, "_bounds", settings, report, ref missing);
                    repaired += Ensure(service, "_defaults", game, report, ref missing);
                }
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
                foreach (ArenaObjective objective in root.GetComponentsInChildren<ArenaObjective>(true))
                {
                    // Caught by a failing test, exactly as the gotcha predicts:
                    // every SCENE reference on this component survived the save
                    // and the one ASSET reference came back {fileID: 0}. A null
                    // config means the beacon never sets a budget, so it silently
                    // heals nothing and never moves — no error, no warning.
                    repaired += Ensure(objective, "_config", objectiveConfig, report, ref missing);
                }
                foreach (WaveRunner waveRunner in root.GetComponentsInChildren<WaveRunner>(true))
                {
                    repaired += Ensure(waveRunner, "_difficulty", difficulty, report, ref missing);
                    repaired += Ensure(waveRunner, "_shopConfig", shop, report, ref missing);
                }
                // SettingsPanel._settings points at a SCENE object, not an asset.
                // Scene-object references are the ones that survive a save; the
                // asset ones are what Ensure exists to repair. It is checked
                // after the round trip below instead.
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
                {
                    Check(look, "_config", stillNull);
                    // Without this link the saved sensitivity and FOV are read,
                    // clamped, saved — and never reach the camera.
                    Check(look, "_settings", stillNull);
                }
                foreach (SettingsHub service in root.GetComponentsInChildren<SettingsHub>(true))
                {
                    Check(service, "_bounds", stillNull);
                    Check(service, "_defaults", stillNull);
                }
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
                    // Unwired, the run loads a SECOND SaveData and writes the whole
                    // file over the settings on every death.
                    Check(context, "_settings", stillNull);
                }
                // The ammo and health readout. Every one of its Update paths
                // early-returns on a null reference, so a Hud wired to nothing
                // looks exactly like a Hud with nothing to say — and it was the
                // one component the builder wires that this verifier never read.
                foreach (Hud hud in root.GetComponentsInChildren<Hud>(true))
                {
                    Check(hud, "_weapon", stillNull);
                    Check(hud, "_playerHealth", stillNull);
                    Check(hud, "_ammoLabel", stillNull);
                    Check(hud, "_healthLabel", stillNull);
                    Check(hud, "_lowAmmoTint", stillNull);
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
                    // Without this, R restarts the run from behind the pause menu.
                    Check(overPanel, "_pause", stillNull);
                }
                foreach (PausePanel pausePanel in root.GetComponentsInChildren<PausePanel>(true))
                {
                    Check(pausePanel, "_root", stillNull);
                    Check(pausePanel, "_titleLabel", stillNull);
                    Check(pausePanel, "_bodyLabel", stillNull);
                    Check(pausePanel, "_footerLabel", stillNull);
                    Check(pausePanel, "_settingsPanel", stillNull);
                    // A null input reference is the nastiest one here: the menu
                    // opens, the game freezes, and the mouse still turns the
                    // camera underneath it.
                    Check(pausePanel, "_input", stillNull);
                    Check(pausePanel, "_runner", stillNull);
                    Check(pausePanel, "_run", stillNull);
                }
                foreach (SettingsPanel settingsPanel in root.GetComponentsInChildren<SettingsPanel>(true))
                {
                    Check(settingsPanel, "_settings", stillNull);
                    Check(settingsPanel, "_root", stillNull);
                    Check(settingsPanel, "_bodyLabel", stillNull);
                    Check(settingsPanel, "_footerLabel", stillNull);
                }
                foreach (PlayerMotor motor in root.GetComponentsInChildren<PlayerMotor>(true))
                    Check(motor, "_run", stillNull);
                foreach (WeaponController weapon in root.GetComponentsInChildren<WeaponController>(true))
                {
                    Check(weapon, "_run", stillNull);
                    Check(weapon, "_ownerHealth", stillNull);
                }
                foreach (ShopPanel shopPanel in root.GetComponentsInChildren<ShopPanel>(true))
                {
                    Check(shopPanel, "_weapon", stillNull);
                    // SPACE is "next wave" here and "confirm" in the pause menu.
                    Check(shopPanel, "_pause", stillNull);
                }
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
            CheckAssetRef(explosive, "explosionVfx", stillNull);

            // The menu scene gets the same save/reload treatment, and only NOW.
            // It opens other scenes, and opening a near-empty one lets Unity
            // unload every asset the grey box was the last thing holding — the
            // DroneConfig handles above would come back null and every check
            // after this point would report a missing asset that is really on
            // disk. Order is load-scene-relative, not cosmetic.
            VerifyMenuScene(stillNull);

            Debug.Log($"GreyBoxVerify: repaired {repaired}, unresolved {stillNull.Count}\n{report}");
            if (stillNull.Count > 0)
            {
                Debug.LogError("GreyBoxVerify: STILL NULL after save+reload:\n  " + string.Join("\n  ", stillNull));
            }
            else
            {
                Debug.Log("GreyBoxVerify: every checked reference survived a save/reload round trip.");
            }

            return stillNull.Count;
        }

        /// <summary>
        /// 20_MainMenu, read back from disk. Its failure mode is the worst one in
        /// the project: a menu whose START RUN row is wired to nothing looks
        /// completely normal and does nothing when you press Enter.
        /// </summary>
        private static void VerifyMenuScene(List<string> stillNull)
        {
            // Repair first, exactly as the grey box does. The menu's SettingsHub
            // holds two ASSET references, and asset references assigned into a
            // scene that has never been saved do not persist — the failure this
            // whole file exists for. Caught by this pass on its first run.
            Scene menu = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

            // Load the assets AFTER the scene is open, never before. Closing a
            // scene lets Unity unload every asset it was the last thing holding,
            // and a C# handle to an unloaded UnityEngine.Object compares equal to
            // null — so a handle taken before the switch silently repairs
            // nothing. This cost one build round to find.
            SettingsConfig? settings = Load<SettingsConfig>("Assets/_Project/Data/Game/Settings.asset");
            GameConfig? game = Load<GameConfig>("Assets/_Project/Data/Game/GameConfig.asset");

            var report = new StringBuilder();
            int repaired = 0;
            int missing = 0;
            foreach (GameObject root in menu.GetRootGameObjects())
            {
                foreach (SettingsHub hub in root.GetComponentsInChildren<SettingsHub>(true))
                {
                    repaired += Ensure(hub, "_bounds", settings, report, ref missing);
                    repaired += Ensure(hub, "_defaults", game, report, ref missing);
                }
            }
            if (repaired > 0)
            {
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu);
                AssetDatabase.SaveAssets();
                Debug.Log($"GreyBoxVerify: menu scene repaired {repaired}\n{report}");
            }

            menu = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            foreach (GameObject root in menu.GetRootGameObjects())
            {
                foreach (SettingsHub hub in root.GetComponentsInChildren<SettingsHub>(true))
                {
                    Check(hub, "_bounds", stillNull);
                    Check(hub, "_defaults", stillNull);
                }
                foreach (MainMenuPanel panel in root.GetComponentsInChildren<MainMenuPanel>(true))
                {
                    Check(panel, "_settings", stillNull);
                    Check(panel, "_root", stillNull);
                    Check(panel, "_titleLabel", stillNull);
                    Check(panel, "_recordLabel", stillNull);
                    Check(panel, "_bodyLabel", stillNull);
                    Check(panel, "_footerLabel", stillNull);
                    Check(panel, "_settingsPanel", stillNull);
                    CheckString(panel, "_gameSceneName", stillNull);
                }
                foreach (SettingsPanel panel in root.GetComponentsInChildren<SettingsPanel>(true))
                {
                    Check(panel, "_settings", stillNull);
                    Check(panel, "_root", stillNull);
                    Check(panel, "_bodyLabel", stillNull);
                    Check(panel, "_footerLabel", stillNull);
                }
            }

            // The boot scene is two components and one string, and that string is
            // the difference between booting to the menu and booting to a run.
            Scene boot = EditorSceneManager.OpenScene("Assets/_Project/Scenes/00_Boot.unity", OpenSceneMode.Single);
            foreach (GameObject root in boot.GetRootGameObjects())
            {
                foreach (BootLoader loader in root.GetComponentsInChildren<BootLoader>(true))
                    CheckString(loader, "_firstScene", stillNull);
            }
        }

        /// <summary>An empty scene name is as dead as a null reference and reads the same.</summary>
        private static void CheckString(Object target, string field, List<string> stillNull)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || string.IsNullOrWhiteSpace(property.stringValue))
            {
                stillNull.Add($"{target.GetType().Name}.{field} (empty string)");
            }
        }

        public static void VerifyHeadless()
        {
            try
            {
                int unresolved = VerifyAndReport();
                if (unresolved > 0)
                {
                    // The whole reason this file exists is that a build reported
                    // success over a scene with every asset reference null. Exiting
                    // 0 after PROVING that again put the gate right back where it
                    // started: LogError is not a failure, an exit code is.
                    Debug.LogError(
                        $"GreyBoxVerify: {unresolved} unresolved reference(s) — failing the build.");
                    EditorApplication.Exit(1);
                    return;
                }
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
            if (property == null)
            {
                // A field that no longer exists is the failure this verifier is
                // LEAST able to survive: rename a serialized reference and every
                // Check naming the old one silently starts passing, so the checks
                // quietly stop covering the thing they were written for. Ensure
                // and CheckArray both already report it; this one used to shrug.
                stillNull.Add($"{target.GetType().Name}.{field} (no such field)");
                return;
            }
            if (property.objectReferenceValue == null)
            {
                stillNull.Add($"{target.GetType().Name}.{field}");
            }
        }
    }
}
