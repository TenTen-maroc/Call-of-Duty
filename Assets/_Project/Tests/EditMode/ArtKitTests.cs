#nullable enable
using System.IO;
using CoD.Core;
using CoD.Enemies;
using CoD.Waves;
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
        public void ShippedKits_ArenaAndAudioAreComplete_AndUnconvertedVisualTracksRemainEmpty()
        {
            ArenaKitConfig? arena = AssetDatabase.LoadAssetAtPath<ArenaKitConfig>(
                KitFolder + "/Kit_Arena_Default.asset");
            WeaponKitConfig? weapon = AssetDatabase.LoadAssetAtPath<WeaponKitConfig>(
                KitFolder + "/Kit_Weapon_Default.asset");
            EnemyKitConfig? enemy = AssetDatabase.LoadAssetAtPath<EnemyKitConfig>(
                KitFolder + "/Kit_Enemy_Default.asset");
            AudioKitConfig? audio = AssetDatabase.LoadAssetAtPath<AudioKitConfig>(
                KitFolder + "/Kit_Audio_Default.asset");

            Assert.That(arena, Is.Not.Null);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            Assert.That(audio, Is.Not.Null);
            Assert.That(arena!.HasCompleteAssignments && arena.IsValid, Is.True);
            Assert.That(weapon!.HasNoAssignments && weapon.IsValid, Is.True);
            Assert.That(enemy!.HasNoAssignments && enemy.IsValid, Is.True);
            Assert.That(audio!.HasCompleteAssignments && audio.IsValid, Is.True);

            Assert.That(AssetDatabase.GetAssetPath(arena.floorModule),
                Does.StartWith("Assets/_Project/Art/Imported/AmbientCG/"));
            Assert.That(AssetDatabase.GetAssetPath(arena.floorMaterial),
                Does.EndWith("/Concrete034.mat"));
            Assert.That(AssetDatabase.GetAssetPath(arena.wallModule),
                Does.StartWith("Assets/_Project/Art/Imported/AmbientCG/"));
            Assert.That(AssetDatabase.GetAssetPath(arena.wallMaterial),
                Does.EndWith("/Concrete031.mat"));
            Assert.That(AssetDatabase.GetAssetPath(arena.reflectionCubemap),
                Does.EndWith("/PolyHaven/autoshop_01_1k.hdr"));
            Assert.That(arena.reflectionIntensity, Is.EqualTo(0.35f));
            Assert.That(AssetDatabase.GetAssetPath(audio.roomTone),
                Does.EndWith("/Kenney/Ambience/facility_room.ogg"));
            Assert.That(AssetDatabase.GetAssetPath(audio.confirm),
                Does.EndWith("/Kenney/Interface/confirm.ogg"));
            Assert.That(AssetDatabase.GetAssetPath(audio.rifleClose),
                Does.EndWith("/Sonniss/Weapons/rifle_close.ogg"));
            Assert.That(AssetDatabase.GetAssetPath(audio.rifleTail),
                Does.EndWith("/Sonniss/Weapons/rifle_tail.ogg"));
            Assert.That(AssetDatabase.GetAssetPath(audio.rifleReload),
                Does.EndWith("/Sonniss/Weapons/rifle_reload.ogg"));
        }

        [Test]
        public void ArenaKit_RejectsMixedAssignments_AndAcceptsCompleteAssignments()
        {
            ArenaKitConfig kit = ScriptableObject.CreateInstance<ArenaKitConfig>();
            GameObject module = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = NewMaterial();
            var reflection = new Cubemap(4, TextureFormat.RGBA32, false);
            try
            {
                kit.floorModule = module;
                Assert.That(kit.IsValid, Is.False);

                kit.floorMaterial = material;
                kit.wallModule = module;
                kit.wallMaterial = material;
                Assert.That(kit.IsValid, Is.False);

                kit.reflectionCubemap = reflection;
                Assert.That(kit.HasCompleteAssignments && kit.IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(module);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(reflection);
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
        public void AudioKit_RejectsMixedAssignments()
        {
            AudioKitConfig kit = ScriptableObject.CreateInstance<AudioKitConfig>();
            AudioClip clip = AudioClip.Create("test", 32, 1, 8000, false);
            try
            {
                Assert.That(kit.HasNoAssignments && kit.IsValid, Is.True);
                kit.confirm = clip;
                Assert.That(kit.IsValid, Is.False);
                Assert.That(kit.HasNoAssignments, Is.False);
                Assert.That(kit.HasCompleteAssignments, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(kit);
            }
        }

        [Test]
        public void AudioKit_AcceptsCompleteSourceSectionsIndependently()
        {
            AudioKitConfig kit = ScriptableObject.CreateInstance<AudioKitConfig>();
            AudioClip clip = AudioClip.Create("test", 32, 1, 8000, false);
            try
            {
                kit.rifleClose = clip;
                kit.rifleTail = clip;
                kit.rifleReload = clip;

                Assert.That(kit.HasSonnissAssignments, Is.True);
                Assert.That(kit.HasNoKenneyAssignments, Is.True);
                Assert.That(kit.HasCompleteAssignments, Is.False);
                Assert.That(kit.IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(kit);
            }
        }

        [Test]
        public void KenneyAudio_UsesLatencyAndLoopSpecificImportPolicies()
        {
            const string root = "Assets/_Project/Art/Imported/Kenney";
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { root });
            Assert.That(guids, Has.Length.EqualTo(AudioKitConfig.KenneyAssignmentCount));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer!.forceToMono, Is.True, path);
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                bool ambience = path.Contains("/Ambience/", System.StringComparison.Ordinal);
                Assert.That(settings.loadType, Is.EqualTo(ambience
                    ? AudioClipLoadType.CompressedInMemory
                    : AudioClipLoadType.DecompressOnLoad), path);
                Assert.That(settings.compressionFormat, Is.EqualTo(ambience
                    ? AudioCompressionFormat.Vorbis
                    : AudioCompressionFormat.PCM), path);
            }
        }

        [Test]
        public void KenneyAudio_IsWiredIntoEveryOwnedDataAssetAndFeedbackPrefab()
        {
            AudioKitConfig kit = Load<AudioKitConfig>(KitFolder + "/Kit_Audio_Default.asset");
            FootstepConfig footsteps = Load<FootstepConfig>("Assets/_Project/Data/Game/Footsteps_Player.asset");
            AmbienceConfig ambience = Load<AmbienceConfig>("Assets/_Project/Data/Game/Ambience_Arena.asset");
            ImpactConfig impacts = Load<ImpactConfig>("Assets/_Project/Data/Game/Impact_Default.asset");
            ContactDetonate detonate = Load<ContactDetonate>(
                "Assets/_Project/Data/Attacks/ContactDetonate_Std.asset");
            RangedBurst ranged = Load<RangedBurst>("Assets/_Project/Data/Attacks/RangedBurst_Std.asset");
            HeavySlam slam = Load<HeavySlam>("Assets/_Project/Data/Attacks/HeavySlam_Std.asset");

            Assert.That(footsteps.surfaces[0].stepClips,
                Is.EqualTo(new[] { kit.footstepConcreteA, kit.footstepConcreteB,
                    kit.footstepConcreteC, kit.footstepConcreteD }));
            Assert.That(ambience.roomTone, Is.SameAs(kit.roomTone));
            Assert.That(ambience.emitters, Has.Length.EqualTo(4));
            for (int i = 0; i < 3; i++) Assert.That(ambience.emitters[i].clip, Is.SameAs(kit.ventLoop));
            Assert.That(ambience.emitters[3].clip, Is.SameAs(kit.powerLoop));

            Assert.That(impacts.surfaces, Has.Length.GreaterThanOrEqualTo(4));
            Assert.That(ImpactClip(impacts, SurfaceType.Concrete), Is.SameAs(kit.impactConcrete));
            Assert.That(ImpactClip(impacts, SurfaceType.Metal), Is.SameAs(kit.impactMetal));
            Assert.That(ImpactClip(impacts, SurfaceType.Grate), Is.SameAs(kit.impactGrate));
            Assert.That(ImpactClip(impacts, SurfaceType.Flesh), Is.SameAs(kit.impactFlesh));
            Assert.That(detonate.alertClip, Is.SameAs(kit.droneAlert));
            Assert.That(ranged.fireClip, Is.SameAs(kit.droneShot));
            Assert.That(slam.windupClip, Is.SameAs(kit.slamWindup));

            Assert.That(PrefabClip("Fx_Explosion"), Is.SameAs(kit.explosion));
            Assert.That(PrefabClip("Fx_Slam"), Is.SameAs(kit.explosion));
            Assert.That(PrefabClip("Fx_DroneDeath"), Is.SameAs(kit.droneDeath));
            GameObject interact = Load<GameObject>("Assets/_Project/Prefabs/Interact_Point.prefab");
            InteractPoint? point = interact.GetComponent<InteractPoint>();
            Assert.That(point, Is.Not.Null);
            Assert.That(SerializedReference(point!, "_useClip"), Is.SameAs(kit.confirm));
        }

        [Test]
        public void SonnissWeaponAudio_IsTrimmedAndWiredAcrossTheArsenal()
        {
            const string root = "Assets/_Project/Art/Imported/Sonniss/Weapons";
            AudioKitConfig kit = Load<AudioKitConfig>(KitFolder + "/Kit_Audio_Default.asset");
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { root });
            Assert.That(guids, Has.Length.EqualTo(3));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip clip = Load<AudioClip>(path);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert.That(clip.length, Is.LessThan(1.9f), path);
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer!.forceToMono, Is.True, path);
                Assert.That(importer.defaultSampleSettings.loadType,
                    Is.EqualTo(AudioClipLoadType.DecompressOnLoad), path);
                Assert.That(importer.defaultSampleSettings.compressionFormat,
                    Is.EqualTo(AudioCompressionFormat.Vorbis), path);
            }

            WeaponRegistry registry = Load<WeaponRegistry>("Assets/_Project/Data/Weapons/Weapons.asset");
            Assert.That(registry.allWeapons, Has.Length.EqualTo(8));
            foreach (WeaponConfig weapon in registry.allWeapons)
            {
                Assert.That(weapon.fireCloseLayer, Is.SameAs(kit.rifleClose), weapon.name);
                Assert.That(weapon.fireTailLayer, Is.SameAs(kit.rifleTail), weapon.name);
                Assert.That(weapon.reloadClip, Is.SameAs(kit.rifleReload), weapon.name);
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
        public void PolyHavenReflection_IsA128PixelLinearSpecularCubemap()
        {
            const string path = "Assets/_Project/Art/Imported/PolyHaven/autoshop_01_1k.hdr";
            Cubemap? reflection = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            Assert.That(reflection, Is.Not.Null);
            Assert.That(reflection!.width, Is.EqualTo(128));
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer!.maxTextureSize, Is.EqualTo(128));
            Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.TextureCube));
            Assert.That(importer.generateCubemap, Is.EqualTo(TextureImporterGenerateCubemap.AutoCubemap));
            Assert.That(importer.sRGBTexture, Is.False);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.cubemapConvolution,
                Is.EqualTo(TextureImporterCubemapConvolution.Specular));
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

        private static T Load<T>(string path) where T : Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset!;
        }

        private static AudioClip? ImpactClip(ImpactConfig config, SurfaceType surface)
        {
            foreach (ImpactConfig.SurfaceResponse row in config.surfaces)
                if (row.surface == surface) return row.impactSound;
            Assert.Fail("No impact row for " + surface);
            return null;
        }

        private static AudioClip? PrefabClip(string prefabName)
        {
            GameObject prefab = Load<GameObject>("Assets/_Project/Prefabs/" + prefabName + ".prefab");
            AudioSource? source = prefab.GetComponent<AudioSource>();
            Assert.That(source, Is.Not.Null, prefabName);
            return source!.clip;
        }

        private static Object? SerializedReference(Object owner, string field)
        {
            SerializedProperty? property = new SerializedObject(owner).FindProperty(field);
            Assert.That(property, Is.Not.Null, owner.name + "." + field);
            return property!.objectReferenceValue;
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
