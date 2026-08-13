#nullable enable
using CoD.Core;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>Applies the trimmed, royalty-free Sonniss firearm subset to the optional audio kit.</summary>
    public static class SonnissAudioBuilder
    {
        private const string Root = "Assets/_Project/Art/Imported/Sonniss/Weapons";
        private const string KitPath = "Assets/_Project/Data/Kits/Kit_Audio_Default.asset";

        [MenuItem("CoD/Art/Build Sonniss Weapon Audio", false, 53)]
        public static void Build()
        {
            AudioKitConfig? kit = AssetDatabase.LoadAssetAtPath<AudioKitConfig>(KitPath);
            if (kit == null)
                throw new System.InvalidOperationException("Build the Kenney audio kit first: " + KitPath);

            kit.rifleClose = Load("rifle_close.ogg");
            kit.rifleTail = Load("rifle_tail.ogg");
            kit.rifleReload = Load("rifle_reload.ogg");
            if (!kit.HasCompleteAssignments)
                throw new System.InvalidOperationException("Sonniss weapon audio did not complete the 21-clip kit.");

            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Sonniss audio: wired three trimmed firearm clips into the optional audio kit.");
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
                Debug.LogError("Sonniss audio build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static AudioClip Load(string name)
        {
            string path = Root + "/" + name;
            AudioClip? clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            return clip != null
                ? clip
                : throw new System.InvalidOperationException("Missing Sonniss clip: " + path);
        }
    }
}
