#nullable enable
using System.Collections.Generic;
using System.Text;
using CoD.Core;
using CoD.Player;
using CoD.UI;
using CoD.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
                    repaired += Ensure(h, "_config", health, report, ref missing);
                foreach (CheatConsole console in root.GetComponentsInChildren<CheatConsole>(true))
                    repaired += Ensure(console, "_config", game, report, ref missing);
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
                    Check(h, "_config", stillNull);
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
                    Check(crosshair, "_centreDot", stillNull);
                }
            }

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
