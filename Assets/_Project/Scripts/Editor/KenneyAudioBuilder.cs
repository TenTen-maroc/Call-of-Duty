#nullable enable
using CoD.Core;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>Wires the deliberately retained Kenney CC0 subset into one optional kit.</summary>
    public static class KenneyAudioBuilder
    {
        private const string Root = "Assets/_Project/Art/Imported/Kenney";
        private const string KitPath = "Assets/_Project/Data/Kits/Kit_Audio_Default.asset";

        [MenuItem("CoD/Art/Build Kenney Audio Kit", false, 52)]
        public static void Build()
        {
            AudioKitConfig? kit = AssetDatabase.LoadAssetAtPath<AudioKitConfig>(KitPath);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<AudioKitConfig>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }

            kit.footstepConcreteA = Load("Footsteps/footstep_concrete_000.ogg");
            kit.footstepConcreteB = Load("Footsteps/footstep_concrete_001.ogg");
            kit.footstepConcreteC = Load("Footsteps/footstep_concrete_002.ogg");
            kit.footstepConcreteD = Load("Footsteps/footstep_concrete_003.ogg");
            kit.impactConcrete = Load("Impacts/impact_concrete.ogg");
            kit.impactMetal = Load("Impacts/impact_metal.ogg");
            kit.impactGrate = Load("Impacts/impact_grate.ogg");
            kit.impactFlesh = Load("Impacts/impact_flesh.ogg");
            kit.roomTone = Load("Ambience/facility_room.ogg");
            kit.ventLoop = Load("Ambience/facility_vent.ogg");
            kit.powerLoop = Load("Ambience/facility_power.ogg");
            kit.droneAlert = Load("Cues/drone_alert.ogg");
            kit.droneShot = Load("Cues/drone_shot.ogg");
            kit.slamWindup = Load("Cues/slam_windup.ogg");
            kit.explosion = Load("Cues/explosion.ogg");
            kit.droneDeath = Load("Cues/drone_death.ogg");
            kit.confirm = Load("Interface/confirm.ogg");
            kit.refused = Load("Interface/refused.ogg");

            if (!kit.HasCompleteAssignments)
                throw new System.InvalidOperationException("Kenney audio kit did not resolve all 18 retained clips.");

            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AudioBuilder.Build();
            Debug.Log("Kenney audio: wired 18 retained CC0 clips into the optional audio kit.");
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
                Debug.LogError("Kenney audio build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static AudioClip Load(string relativePath)
        {
            string path = Root + "/" + relativePath;
            AudioClip? clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            return clip != null
                ? clip
                : throw new System.InvalidOperationException("Missing Kenney clip: " + path);
        }
    }
}
