#nullable enable
using CoD.Core;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>Imports one measured Poly Haven HDRI as arena reflection data.</summary>
    public static class PolyHavenEnvironmentBuilder
    {
        private const string SourcePath =
            "Assets/_Project/Art/Imported/PolyHaven/autoshop_01_1k.hdr";
        private const string KitPath = "Assets/_Project/Data/Kits/Kit_Arena_Default.asset";

        [MenuItem("CoD/Build Poly Haven Reflection", false, 7)]
        public static void Build()
        {
            AssetDatabase.ImportAsset(SourcePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Cubemap? reflection = AssetDatabase.LoadAssetAtPath<Cubemap>(SourcePath);
            if (reflection is null)
                throw new System.InvalidOperationException(
                    $"Poly Haven source '{SourcePath}' did not import as a Cubemap.");

            ArenaKitConfig? kit = AssetDatabase.LoadAssetAtPath<ArenaKitConfig>(KitPath);
            if (kit is null)
                throw new System.InvalidOperationException($"Missing arena kit at '{KitPath}'. Build G8 first.");

            kit.reflectionCubemap = reflection;
            kit.reflectionIntensity = 0.35f;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log("Poly Haven: wired Autoshop 01 as the 128 px arena reflection cubemap.");
        }

        public static void BuildHeadless() => Build();
    }
}
