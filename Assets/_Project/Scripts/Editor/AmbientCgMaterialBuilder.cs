#nullable enable
using CoD.Core;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>
    /// Converts the deliberately small ambientCG source subset into URP material
    /// assets and wires the arena kit. The downloaded source remains data; this
    /// builder only performs Unity-specific conversion that must be reproducible.
    /// </summary>
    public static class AmbientCgMaterialBuilder
    {
        private const string SourceRoot = "Assets/_Project/Art/Imported/AmbientCG";
        private const string MaterialRoot = SourceRoot + "/Materials";
        private const string GeometryRoot = SourceRoot + "/Geometry";
        private const string KitPath = "Assets/_Project/Data/Kits/Kit_Arena_Default.asset";
        private const string UnitBlockPath = GeometryRoot + "/AmbientCG_UnitBlock.prefab";

        [MenuItem("CoD/Build ambientCG Materials", false, 6)]
        public static void Build()
        {
            EnsureFolder(MaterialRoot);
            EnsureFolder(GeometryRoot);

            Shader? shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader is null)
                throw new System.InvalidOperationException("URP Lit shader is unavailable.");

            // Scalar metal/smoothness replaces two more 1K maps per surface.
            // At this project's 1080p target the color and tangent normal carry
            // the useful detail; omitting unused maps saves both LFS and VRAM.
            (string Id, float Metallic, float Smoothness, float Tiling, float NormalScale)[] surfaces =
            {
                ("Concrete031",      0.00f, 0.18f,  4f, 0.65f),
                ("Concrete034",      0.00f, 0.22f, 12f, 0.60f),
                ("Concrete044D",     0.00f, 0.12f,  5f, 0.80f),
                ("DiamondPlate008C", 0.85f, 0.42f,  6f, 0.90f),
                ("Metal027",         0.80f, 0.50f,  5f, 0.45f),
                ("Metal053C",        0.75f, 0.28f,  5f, 0.75f),
                ("MetalPlates013",   0.80f, 0.35f,  4f, 0.70f),
                ("MetalWalkway014",  0.80f, 0.25f,  5f, 0.85f),
                ("PaintedMetal006",  0.35f, 0.32f,  5f, 0.70f),
                ("PaintedMetal016",  0.35f, 0.24f,  4f, 0.75f),
            };

            Material? floorMaterial = null;
            Material? wallMaterial = null;
            foreach ((string id, float metallic, float smoothness, float tiling, float normalScale) in surfaces)
            {
                Material material = BuildMaterial(shader, id, metallic, smoothness, tiling, normalScale);
                if (id == "Concrete034") floorMaterial = material;
                if (id == "Concrete031") wallMaterial = material;
            }

            if (floorMaterial is null || wallMaterial is null)
                throw new System.InvalidOperationException("The arena surface selection is incomplete.");

            GameObject unitBlock = BuildUnitBlock(wallMaterial);
            ArenaKitConfig? kit = AssetDatabase.LoadAssetAtPath<ArenaKitConfig>(KitPath);
            if (kit is null)
                throw new System.InvalidOperationException($"Missing arena kit at '{KitPath}'. Build G8 first.");

            kit.floorModule = unitBlock;
            kit.floorMaterial = floorMaterial;
            kit.wallModule = unitBlock;
            kit.wallMaterial = wallMaterial;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ambientCG: built 10 URP materials and wired the complete arena kit.");
        }

        public static void BuildHeadless() => Build();

        private static Material BuildMaterial(Shader shader, string id, float metallic,
            float smoothness, float tiling, float normalScale)
        {
            string colorPath = $"{SourceRoot}/{id}/{id}_1K-JPG_Color.jpg";
            string normalPath = $"{SourceRoot}/{id}/{id}_1K-JPG_NormalGL.jpg";
            EnsureNormalImport(normalPath);
            Texture2D? color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            Texture2D? normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (color is null || normal is null)
                throw new System.InvalidOperationException(
                    $"ambientCG asset '{id}' is incomplete. Expected its 1K Color and NormalGL maps.");

            string materialPath = $"{MaterialRoot}/{id}.mat";
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material is null)
            {
                material = new Material(shader) { name = id };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            var scale = new Vector2(tiling, tiling);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", color);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BumpMap", scale);
            material.SetFloat("_BumpScale", normalScale);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureNormalImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new System.InvalidOperationException($"Missing texture importer for '{path}'.");
            if (importer.textureType == TextureImporterType.NormalMap && !importer.sRGBTexture) return;

            // Existing assets are not guaranteed to reimport merely because an
            // AssetPostprocessor's filename policy changed. Reassert it here so
            // rebuilding the source converter repairs old Library state too.
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }

        private static GameObject BuildUnitBlock(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "AmbientCG_UnitBlock";
            Object.DestroyImmediate(root.GetComponent<Collider>());
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObject? prefab = PrefabUtility.SaveAsPrefabAsset(root, UnitBlockPath);
            Object.DestroyImmediate(root);
            if (prefab is null)
                throw new System.InvalidOperationException($"Could not save '{UnitBlockPath}'.");
            return prefab;
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
