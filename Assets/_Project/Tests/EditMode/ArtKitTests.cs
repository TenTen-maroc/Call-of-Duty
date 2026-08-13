#nullable enable
using System.IO;
using CoD.Core;
using CoD.Enemies;
using CoD.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace CoD.Tests
{
    public sealed class ArtKitTests
    {
        private const string KitFolder = "Assets/_Project/Data/Kits";

        [Test]
        public void ShippedKits_ArenaIsComplete_AndUnconvertedTracksRemainEmpty()
        {
            ArenaKitConfig? arena = AssetDatabase.LoadAssetAtPath<ArenaKitConfig>(
                KitFolder + "/Kit_Arena_Default.asset");
            WeaponKitConfig? weapon = AssetDatabase.LoadAssetAtPath<WeaponKitConfig>(
                KitFolder + "/Kit_Weapon_Default.asset");
            EnemyKitConfig? enemy = AssetDatabase.LoadAssetAtPath<EnemyKitConfig>(
                KitFolder + "/Kit_Enemy_Default.asset");

            Assert.That(arena, Is.Not.Null);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            Assert.That(arena!.HasCompleteAssignments && arena.IsValid, Is.True);
            Assert.That(weapon!.HasNoAssignments && weapon.IsValid, Is.True);
            Assert.That(enemy!.HasNoAssignments && enemy.IsValid, Is.True);

            Assert.That(AssetDatabase.GetAssetPath(arena.floorModule),
                Does.StartWith("Assets/_Project/Art/Imported/AmbientCG/"));
            Assert.That(AssetDatabase.GetAssetPath(arena.floorMaterial),
                Does.EndWith("/Concrete034.mat"));
            Assert.That(AssetDatabase.GetAssetPath(arena.wallModule),
                Does.StartWith("Assets/_Project/Art/Imported/AmbientCG/"));
            Assert.That(AssetDatabase.GetAssetPath(arena.wallMaterial),
                Does.EndWith("/Concrete031.mat"));
        }

        [Test]
        public void ArenaKit_RejectsMixedAssignments_AndAcceptsCompleteAssignments()
        {
            ArenaKitConfig kit = ScriptableObject.CreateInstance<ArenaKitConfig>();
            GameObject module = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = NewMaterial();
            try
            {
                kit.floorModule = module;
                Assert.That(kit.IsValid, Is.False);

                kit.floorMaterial = material;
                kit.wallModule = module;
                kit.wallMaterial = material;
                Assert.That(kit.HasCompleteAssignments && kit.IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(module);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(kit);
            }
        }

        [Test]
        public void WeaponAndEnemyKits_RejectMixedAssignments()
        {
            WeaponKitConfig weapon = ScriptableObject.CreateInstance<WeaponKitConfig>();
            EnemyKitConfig enemy = ScriptableObject.CreateInstance<EnemyKitConfig>();
            GameObject module = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = NewMaterial();
            try
            {
                weapon.viewmodelPrefab = module;
                enemy.rusherPrefab = module;
                Assert.That(weapon.IsValid, Is.False);
                Assert.That(enemy.IsValid, Is.False);

                weapon.viewmodelMaterial = material;
                enemy.shooterPrefab = module;
                enemy.tankPrefab = module;
                enemy.hullMaterial = material;
                Assert.That(weapon.HasCompleteAssignments && weapon.IsValid, Is.True);
                Assert.That(enemy.HasCompleteAssignments && enemy.IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(module);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void ImportPresets_AreCommittedUnityPresets()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<Preset>(
                "Assets/_Project/Art/Presets/Texture_Art_1024.preset"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Preset>(
                "Assets/_Project/Art/Presets/Model_Art_Static.preset"), Is.Not.Null);
        }

        [Test]
        public void AmbientCgSource_IsCompleteBudgetedAndColliderFree()
        {
            const string root = "Assets/_Project/Art/Imported/AmbientCG";
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { root + "/Materials" });
            Assert.That(materialGuids, Has.Length.EqualTo(10));

            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            Assert.That(textureGuids, Has.Length.EqualTo(20));
            foreach (string guid in textureGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer!.maxTextureSize, Is.EqualTo(1024), path);
                Assert.That(importer.isReadable, Is.False, path);
                Assert.That(importer.streamingMipmaps, Is.True, path);
                if (path.Contains("_NormalGL", System.StringComparison.Ordinal))
                {
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap), path);
                    Assert.That(importer.sRGBTexture, Is.False, path);
                }
            }

            GameObject? unitBlock = AssetDatabase.LoadAssetAtPath<GameObject>(
                root + "/Geometry/AmbientCG_UnitBlock.prefab");
            Assert.That(unitBlock, Is.Not.Null);
            Assert.That(unitBlock!.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
            Assert.That(unitBlock.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        [Test]
        public void ImportedArtFolder_StampsTextureAndStaticModelBudgets()
        {
            const string root = "Assets/_Project/Art/Imported/__ArtKitTest";
            const string texturePath = root + "/Concrete.png";
            const string weaponTexturePath = root + "/Weapons/Receiver.png";
            const string modelPath = root + "/Block.obj";
            EnsureAssetFolder(root);
            EnsureAssetFolder(root + "/Weapons");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                File.WriteAllBytes(weaponTexturePath, texture.EncodeToPNG());
                File.WriteAllText(modelPath,
                    "o Block\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(weaponTexturePath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);

                var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                var weaponTextureImporter = AssetImporter.GetAtPath(weaponTexturePath) as TextureImporter;
                Assert.That(textureImporter, Is.Not.Null);
                Assert.That(textureImporter!.maxTextureSize, Is.EqualTo(1024));
                Assert.That(textureImporter.isReadable, Is.False);
                Assert.That(textureImporter.mipmapEnabled, Is.True);
                Assert.That(textureImporter.streamingMipmaps, Is.True);
                Assert.That(weaponTextureImporter, Is.Not.Null);
                Assert.That(weaponTextureImporter!.maxTextureSize, Is.EqualTo(2048));

                Assert.That(modelImporter, Is.Not.Null);
                Assert.That(modelImporter!.isReadable, Is.False);
                Assert.That(modelImporter.importAnimation, Is.False);
                Assert.That(modelImporter.importCameras, Is.False);
                Assert.That(modelImporter.importLights, Is.False);
                Assert.That(modelImporter.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None));
            }
            finally
            {
                Object.DestroyImmediate(texture);
                AssetDatabase.DeleteAsset(root);
            }
        }

        private static Material NewMaterial()
        {
            Shader? shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            return new Material(shader!);
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path[..split];
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, path[(split + 1)..]);
        }
    }
}
