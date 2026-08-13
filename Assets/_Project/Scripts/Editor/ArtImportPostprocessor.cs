#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>
    /// Applies the project's 4 GB-card import policy before art reaches the
    /// Asset Database. Only designated imported-art and ThirdParty folders are
    /// managed; generated first-party assets keep their authored import state.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        public const string PresetFolder = "Assets/_Project/Art/Presets";
        public const string TexturePresetPath = PresetFolder + "/Texture_Art_1024.preset";
        public const string ModelPresetPath = PresetFolder + "/Model_Art_Static.preset";

        private const string ImportedArtPrefix = "Assets/_Project/Art/Imported/";
        private const string ThirdPartyPrefix = "Assets/ThirdParty/";

        private void OnPreprocessTexture()
        {
            if (!IsManagedPath(assetPath) || assetImporter is not TextureImporter importer) return;

            Preset? preset = AssetDatabase.LoadAssetAtPath<Preset>(TexturePresetPath);
            if (preset != null) preset.ApplyTo(importer);
            ApplyTexturePolicy(importer, assetPath);
        }

        private void OnPreprocessModel()
        {
            if (!IsManagedPath(assetPath) || assetImporter is not ModelImporter importer) return;

            Preset? preset = AssetDatabase.LoadAssetAtPath<Preset>(ModelPresetPath);
            if (preset != null) preset.ApplyTo(importer);
            ApplyModelPolicy(importer, assetPath);
        }

        /// <summary>
        /// Creates real Unity Preset assets from temporary importers. A .preset
        /// is the reviewable source of truth; the callbacks above still re-state
        /// the folder-specific exceptions after applying it.
        /// </summary>
        public static void EnsurePresets()
        {
            EnsureFolder(PresetFolder);
            EnsureTexturePreset();
            EnsureModelPreset();
            AssetDatabase.SaveAssets();
        }

        private static bool IsManagedPath(string path) =>
            path.StartsWith(ImportedArtPrefix, System.StringComparison.Ordinal) ||
            path.StartsWith(ThirdPartyPrefix, System.StringComparison.Ordinal);

        private static void ApplyTexturePolicy(TextureImporter importer, string path)
        {
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = false;
            importer.maxTextureSize = IsHighDetailTexture(path) ? 2048 : 1024;

            string lower = path.ToLowerInvariant();
            if (lower.Contains("_normal.") || lower.Contains("_normalgl.") ||
                lower.Contains("_normaldx.") || lower.Contains("_n.") ||
                lower.Contains("/normals/"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
        }

        private static void ApplyModelPolicy(ModelImporter importer, string path)
        {
            importer.isReadable = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;

            bool animated = IsAnimatedModel(path);
            importer.importAnimation = animated;
            importer.animationType = animated
                ? ModelImporterAnimationType.Human
                : ModelImporterAnimationType.None;
        }

        private static bool IsHighDetailTexture(string path)
        {
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i].ToLowerInvariant();
                if (segment is "weapon" or "weapons" or "hands" or "arms" or "viewmodel") return true;
            }
            return false;
        }

        private static bool IsAnimatedModel(string path)
        {
            string lower = path.ToLowerInvariant();
            return lower.Contains("/characters/") || lower.Contains("/humans/") ||
                   lower.Contains("/animations/") || lower.Contains("/mixamo/");
        }

        private static void EnsureTexturePreset()
        {
            if (AssetDatabase.LoadAssetAtPath<Preset>(TexturePresetPath) != null) return;

            const string temporaryPath = "Assets/_Project/Art/__TexturePresetSource.png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            File.WriteAllBytes(temporaryPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(temporaryPath) is not TextureImporter importer)
                throw new System.InvalidOperationException("Could not create the texture import preset source.");

            ApplyTexturePolicy(importer, ImportedArtPrefix + "Environment/Texture.png");
            AssetDatabase.CreateAsset(new Preset(importer), TexturePresetPath);
            AssetDatabase.DeleteAsset(temporaryPath);
        }

        private static void EnsureModelPreset()
        {
            if (AssetDatabase.LoadAssetAtPath<Preset>(ModelPresetPath) != null) return;

            const string temporaryPath = "Assets/_Project/Art/__ModelPresetSource.obj";
            File.WriteAllText(temporaryPath,
                "o PresetSource\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            AssetDatabase.ImportAsset(temporaryPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(temporaryPath) is not ModelImporter importer)
                throw new System.InvalidOperationException("Could not create the model import preset source.");

            ApplyModelPolicy(importer, ImportedArtPrefix + "Environment/Model.obj");
            AssetDatabase.CreateAsset(new Preset(importer), ModelPresetPath);
            AssetDatabase.DeleteAsset(temporaryPath);
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
