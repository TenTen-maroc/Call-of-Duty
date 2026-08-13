#nullable enable
using System.Collections.Generic;
using CoD.Core;
using CoD.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoD.EditorTools
{
    /// <summary>
    /// Authors what a shot LOOKS like: the tracer, the four per-surface impact
    /// responses, and the two extra pieces of the muzzle flash. Run it from the
    /// CoD menu, or headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.VfxBuilder.BuildVfxHeadless
    ///
    /// A SEPARATE FILE FROM GreyBoxBuilder, for the reason MissionBuilder gives:
    /// the grey box builds the arena — scenes, navmesh, the whole rig — and it is
    /// four thousand lines. This builds prefabs and fills two tables. It
    /// references assets the grey box already made (the impact decal, the
    /// palette, the weapon configs) and never writes one of them back.
    ///
    /// RUN ORDER. Grey box FIRST, then this. The palette and the decal prefab are
    /// its assets; the warnings below name them rather than leaving a surface
    /// quietly pointing at nothing.
    ///
    /// WHAT IT DELIBERATELY DOES NOT DO
    /// - It does not touch the ImpactConfig FALLBACK block (decalPrefab /
    ///   particlePrefab at the top level of the asset). GreyBoxBuilder re-asserts
    ///   those on every build; two builders writing one field is drift with a
    ///   coin-flip winner. This file owns `surfaces[]` and nothing else there.
    /// - It does not add layers. The surface table is keyed on physics layers
    ///   that live in ProjectSettings/TagManager.asset, and a build script
    ///   rewriting project settings is not a trade worth making. Missing layers
    ///   are REPORTED by name and their row is left unclaimed, which falls
    ///   through to the fallback rather than producing nothing.
    ///
    /// IDEMPOTENT, with GreyBoxBuilder's discipline: a value a human tuned in the
    /// Inspector survives a re-run, and REFERENCES are re-asserted every time. A
    /// broken reference is not a tuning difference — it is a bullet that lands in
    /// silence.
    /// </summary>
    public static class VfxBuilder
    {
        private const string DataGame = "Assets/_Project/Data/Game";
        private const string DataWeapons = "Assets/_Project/Data/Weapons";
        private const string Materials = "Assets/_Project/Art/Materials";
        private const string Prefabs = "Assets/_Project/Prefabs";
        private const string Audio = "Assets/_Project/Audio";

        private const string ImpactConfigPath = DataGame + "/Impact_Default.asset";
        private const string AudioKitPath = "Assets/_Project/Data/Kits/Kit_Audio_Default.asset";
        private const string PalettePath = DataGame + "/Palette_GreyBox.asset";
        private const string DecalPrefabPath = Prefabs + "/Fx_ImpactDecal.prefab";

        /// <summary>
        /// The gunmetal the weapon rig is made of. GreyBoxBuilder's asset, loaded
        /// rather than created for the reason the decal above is: two builders
        /// writing one material is drift with a coin-flip winner.
        /// </summary>
        private const string WeaponBodyMaterialPath = Materials + "/Weapon_Body.mat";

        /// <summary>
        /// The layer the gun lives on. Its NAME is the stable handle; the index
        /// is not. See GreyBoxBuilder for the full argument — the short version
        /// is that anything spawned at the muzzle has to be drawn by the overlay
        /// camera or it renders at the wrong FOV, half a metre inside whatever
        /// wall the player is backed against.
        /// </summary>
        private const string ViewmodelLayerName = "Viewmodel";

        /// <summary>
        /// The three layers this table wants and the project does not have yet.
        ///
        /// Concrete is keyed on Default, which always exists, so the table does
        /// real work the moment it is built. These three are the ask: add them to
        /// ProjectSettings/TagManager.asset, move the drone hulls onto
        /// Surface_Metal and the walkway meshes onto Surface_Grate, and re-run
        /// this builder. Until then their rows carry an empty mask, match
        /// nothing, and cost nothing.
        /// </summary>
        private const string MetalLayerName = "Surface_Metal";

        /// <summary>Walkway mesh and vents. See <see cref="MetalLayerName"/>.</summary>
        private const string GrateLayerName = "Surface_Grate";

        /// <summary>Nothing wears it yet, and that is the point. See <see cref="MetalLayerName"/>.</summary>
        private const string FleshLayerName = "Surface_Flesh";

        // Shipped defaults, re-asserted on every build the way ApplySurface is.
        //
        // NOT IN PaletteConfig, and that is a debt rather than a decision: the
        // palette asset is the right home for every colour in the game, and this
        // pass could not add fields to it. Three colours to move when somebody
        // next opens that file — dust, blood, and the grey of a smoke puff.
        private static readonly Color DustColor = new(0.42f, 0.41f, 0.39f);
        private static readonly Color BloodColor = new(0.42f, 0.05f, 0.05f);
        private static readonly Color SmokeColor = new(0.32f, 0.32f, 0.33f);

        /// <summary>Placeholder clips this table wants. Missing ones are reported once, together.</summary>
        private const string ConcreteClip = "Impact_Concrete";

        /// <summary>See <see cref="ConcreteClip"/>.</summary>
        private const string MetalClip = "Impact_Metal";

        /// <summary>See <see cref="ConcreteClip"/>.</summary>
        private const string GrateClip = "Impact_Grate";

        /// <summary>See <see cref="ConcreteClip"/>.</summary>
        private const string FleshClip = "Impact_Flesh";

        [MenuItem("CoD/Build VFX", false, 3)]
        public static void Build()
        {
            EnsureFolder(Materials);
            EnsureFolder(Prefabs);

            var palette = AssetDatabase.LoadAssetAtPath<PaletteConfig>(PalettePath);
            Color spark = palette != null ? palette.sparkHot : new Color(1f, 0.82f, 0.45f);
            if (palette == null)
            {
                Debug.LogWarning($"VfxBuilder: no palette at '{PalettePath}' — run CoD -> Build Grey Box first. " +
                                 "Falling back to the shipped spark colour, which will drift from the arena's.");
            }

            // ---- materials -------------------------------------------------
            Material sparkFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Spark.mat");
            ApplyAdditive(sparkFx, spark);

            Material dustFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Dust.mat");
            ApplyAlphaBlend(dustFx, DustColor);

            Material bloodFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Blood.mat");
            ApplyAlphaBlend(bloodFx, BloodColor);

            Material smokeFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Smoke.mat");
            ApplyAlphaBlend(smokeFx, SmokeColor);

            Material tracerFx = LoadOrCreateParticleMaterial(Materials + "/Fx_Tracer.mat");
            ApplyAdditive(tracerFx, spark);

            // ---- prefabs ---------------------------------------------------
            GameObject tracer = BuildTracerPrefab(tracerFx, spark);
            GameObject wideFlash = BuildWideFlashPrefab(sparkFx);
            GameObject smoke = BuildSmokePrefab(smokeFx);
            GameObject rocket = BuildRocketPrefab(tracerFx, spark);

            // Concrete throws DUST, not sparks — masonry does not spark, and the
            // whole point of the table is that two surfaces stop looking alike.
            // Slow, heavy, gravity-affected, and it lingers.
            GameObject concreteFx = BuildImpactPrefab("Fx_Impact_Concrete", dustFx,
                burst: 10, speed: 1.8f, size: 0.06f, lifetime: 0.55f, coneAngle: 55f, gravity: 0.35f);

            // Plate sparks: fast, small, short-lived, thrown in a tight cone back
            // along the surface normal.
            GameObject metalFx = BuildImpactPrefab("Fx_Impact_Metal", sparkFx,
                burst: 14, speed: 6.5f, size: 0.025f, lifetime: 0.22f, coneAngle: 26f, gravity: 0.9f);

            // A grate is metal with holes in it: fewer sparks, wider spray,
            // because most of the round went through.
            GameObject grateFx = BuildImpactPrefab("Fx_Impact_Grate", sparkFx,
                burst: 7, speed: 5.5f, size: 0.02f, lifetime: 0.18f, coneAngle: 70f, gravity: 1.1f);

            // Authored, and used by nothing. See SurfaceType.Flesh: the day a
            // human-shaped target exists, gore level is this reference, swapped.
            GameObject fleshFx = BuildImpactPrefab("Fx_Impact_Flesh", bloodFx,
                burst: 12, speed: 2.6f, size: 0.05f, lifetime: 0.45f, coneAngle: 42f, gravity: 1.4f);

            // ---- the surface table -----------------------------------------
            var impact = AssetDatabase.LoadAssetAtPath<ImpactConfig>(ImpactConfigPath);
            if (impact == null)
            {
                throw new System.InvalidOperationException(
                    $"VfxBuilder needs '{ImpactConfigPath}' and it is not there. Run CoD -> Build Grey Box " +
                    "first: that asset is its, and every WeaponController in the arena already points at it.");
            }

            var decal = AssetDatabase.LoadAssetAtPath<GameObject>(DecalPrefabPath);
            if (decal == null)
            {
                Debug.LogWarning($"VfxBuilder: no bullet-hole decal at '{DecalPrefabPath}'. Concrete and metal " +
                                 "will spark but leave no hole. Run CoD -> Build Grey Box.");
            }

            var missingClips = new List<string>();
            AudioKitConfig? audioKit = AssetDatabase.LoadAssetAtPath<AudioKitConfig>(AudioKitPath);
            if (audioKit != null && !audioKit.IsValid)
                throw new System.InvalidOperationException(AudioKitPath + " has mixed null/non-null references.");
            bool authoredAudio = audioKit != null && audioKit.HasCompleteAssignments;

            WriteSurface(impact, SurfaceType.Concrete, "Default", decal, concreteFx,
                authoredAudio ? audioKit!.impactConcrete : LoadClip(ConcreteClip, missingClips), volume: 0.5f);
            WriteSurface(impact, SurfaceType.Metal, MetalLayerName, decal, metalFx,
                authoredAudio ? audioKit!.impactMetal : LoadClip(MetalClip, missingClips), volume: 0.6f);
            // No hole in a grate, and none in a body: a decal is spawned into the
            // WORLD rather than parented to what it hit, so one stamped on a
            // drone hangs in mid-air after the drone dies and returns to the pool.
            WriteSurface(impact, SurfaceType.Grate, GrateLayerName, null, grateFx,
                authoredAudio ? audioKit!.impactGrate : LoadClip(GrateClip, missingClips), volume: 0.55f);
            WriteSurface(impact, SurfaceType.Flesh, FleshLayerName, null, fleshFx,
                authoredAudio ? audioKit!.impactFlesh : LoadClip(FleshClip, missingClips), volume: 0.65f);

            EditorUtility.SetDirty(impact);

            if (missingClips.Count > 0)
            {
                Debug.LogWarning(
                    "VfxBuilder: no impact audio for " + string.Join(", ", missingClips) + ". Every field is " +
                    "wired and read by WeaponController.PlayImpactSound — the clips themselves are missing from " +
                    Audio + "/. Add them to Tools/make-placeholder-audio.mjs, re-run it, then run this builder " +
                    "again. Until then bullets land silently, which is the defect this pass exists to close.");
            }

            // ---- the weapons -----------------------------------------------
            // Every WeaponConfig on disk, not a hard-coded pair: WeaponDataTests
            // already cross-checks the registry against a folder scan in both
            // directions precisely because a weapon that exists and is not
            // enumerated is the failure mode nobody sees.
            int wired = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponConfig", new[] { DataWeapons }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
                if (weapon == null) continue;

                weapon.tracerPrefab = tracer;
                weapon.muzzleFlashWidePrefab = wideFlash;
                weapon.muzzleSmokePrefab = smoke;

                // The round itself, and ONLY for a weapon that says it fires one.
                // Stamping it on every weapon would be harmless today and a trap
                // tomorrow: `projectilePrefab` is what OnValidate reads to decide
                // whether a launcher is finished, so a hitscan rifle carrying one
                // would make that warning unreachable for the weapon that needs it.
                //
                // Assigned unconditionally rather than only-when-empty, unlike
                // ArsenalBuilder's house feedback. This is not a choice a human
                // makes per weapon — it is THE round, and a launcher pointing at a
                // deleted prefab fires nothing at all.
                if (weapon.delivery == DeliveryMode.Projectile) weapon.projectilePrefab = rocket;

                EditorUtility.SetDirty(weapon);
                wired++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"VFX built: 4 surface rows on {impact.name}, tracer + wide flash + smoke on {wired} weapon(s), " +
                      $"and {rocket.name} on every projectile weapon. " +
                      "Two things this cannot do and a human must: add Fx_Tracer, Fx_Rocket, the three impact " +
                      "prefabs, the wide flash and the smoke puff to the arena ObjectPool prewarm list (Build Grey " +
                      $"Box does it), and add the '{MetalLayerName}' / '{GrateLayerName}' / '{FleshLayerName}' " +
                      "layers to TagManager.asset.");
        }

        /// <summary>Entry point for -executeMethod. Same work, non-zero exit on failure.</summary>
        public static void BuildVfxHeadless()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("VFX build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- prefabs ----------

        /// <summary>
        /// The tracer: a TrailRenderer and the component that flies it.
        ///
        /// TWO SETTINGS ARE LOAD-BEARING AND BOTH DEFAULT THE WRONG WAY ROUND
        /// FOR A POOLED OBJECT.
        ///
        /// `autodestruct` is the first thing anyone reaches for when a trail
        /// overstays — and it DESTROYS THE GAMEOBJECT when the trail empties. On
        /// a pooled instance that is not cleanup, it is the pool handing out a
        /// reference to a destroyed object on a later shot. Set explicitly here
        /// so nobody has to wonder what it was left at.
        ///
        /// `emitting` starts FALSE. A trail that emits from the moment the pool
        /// creates the instance lays a line from the pool root — the world origin
        /// — before a single round has been fired.
        /// </summary>
        private static GameObject BuildTracerPrefab(Material material, Color color)
        {
            GameObject root = new("Fx_Tracer");

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            // Short. The tracer is a streak, not a rope: 0.09 s at 250 m/s is a
            // 22 m line, which is most of the arena's diagonal already.
            trail.time = 0.09f;
            trail.widthMultiplier = 0.02f;
            // Tapered to a point at the tail, so it reads as travelling in one
            // direction rather than as a floating stick.
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
            trail.minVertexDistance = 0.25f;
            trail.numCapVertices = 0;
            trail.numCornerVertices = 0;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.autodestruct = false;
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = LightProbeUsage.Off;
            trail.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            root.AddComponent<Tracer>();
            root.AddComponent<PooledObject>();
            // Default layer, NOT Viewmodel: it starts at the gun and ends at a
            // wall forty metres away, so it belongs to the world camera. The
            // muzzle flash is the opposite case, on Viewmodel for the opposite
            // reason.
            return SavePrefab(root, Prefabs + "/Fx_Tracer.prefab");
        }

        /// <summary>
        /// The launcher's round: a body you can see coming, and the trail that
        /// tells you where it came from.
        ///
        /// A ROCKET HAS TO BE VISIBLE OR IT IS A RANDOM EXPLOSION. That is the
        /// whole argument for delivering it as a projectile instead of a hitscan
        /// ray with a big blast — the travel time is the readability, and it is
        /// only readable if the object is legible in flight. Hence a solid body at
        /// 0.42 m (four times the drone round's silhouette) plus an additive
        /// trail, on the Default layer, drawn by the world camera.
        ///
        /// NO COLLIDER, for CoD.Core.Projectile's reason: it sweeps a ray between
        /// frames, because a small fast trigger tunnels through a wall at any sane
        /// physics step. The rocket is FASTER than the drone round, so the trigger
        /// version would be worse here, not better.
        ///
        /// The TrailRenderer defaults that are wrong for a pooled object are set
        /// explicitly, exactly as BuildTracerPrefab sets them: `autodestruct`
        /// DESTROYS the GameObject, which on a pooled instance means the pool
        /// hands out a reference to a dead object later, and `emitting` true from
        /// creation lays a line from the pool root at the world origin before a
        /// single shot is fired. Projectile.Launch does the Clear().
        /// </summary>
        private static GameObject BuildRocketPrefab(Material trailMaterial, Color color)
        {
            var body = AssetDatabase.LoadAssetAtPath<Material>(WeaponBodyMaterialPath);
            if (body == null)
            {
                Debug.LogWarning($"VfxBuilder: no '{WeaponBodyMaterialPath}' — the rocket will render with " +
                                 "Unity's default material. Run CoD -> Build Grey Box, then this builder again.");
            }

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Fx_Rocket";
            root.transform.localScale = new Vector3(0.11f, 0.11f, 0.42f);
            if (body != null) root.GetComponent<MeshRenderer>().sharedMaterial = body;
            Object.DestroyImmediate(root.GetComponent<Collider>());

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            // Longer than a tracer's 0.09 s because a rocket is slower and the
            // trail is how it is TRACKED rather than how it is glimpsed: 0.5 s at
            // 34 m/s is a 17 m ribbon, about half the arena.
            trail.time = 0.5f;
            trail.widthMultiplier = 0.13f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
            trail.minVertexDistance = 0.2f;
            trail.numCapVertices = 0;
            trail.numCornerVertices = 0;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.autodestruct = false;
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = LightProbeUsage.Off;
            trail.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            PooledObject pooled = root.AddComponent<PooledObject>();
            Projectile projectile = root.AddComponent<Projectile>();
            // Wired by string AND resolved again in Awake by TryGetComponent. The
            // belt is here because SetRef binds by a string no compiler checks;
            // the braces are in Projectile.Awake because a silent null here is a
            // rocket that never returns to the pool.
            SetRef(projectile, "_pooled", pooled);
            SetRef(projectile, "_trail", trail);

            return SavePrefab(root, Prefabs + "/Fx_Rocket.prefab");
        }

        /// <summary>
        /// Writes a [SerializeField] private reference from an editor script.
        ///
        /// A fourth copy of GreyBoxBuilder's, for the reason this file exists at
        /// all: that one is private to a four-thousand-line builder, and the point
        /// of a separate VFX builder is that authoring a prefab never opens it.
        /// ⚠️ It binds BY STRING. A typo is a silent null that no compiler catches
        /// and no build reports — see the class header of GreyBoxVerify.
        /// </summary>
        private static void SetRef(Object target, string field, Object? value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new System.InvalidOperationException(
                    $"'{target.GetType().Name}' has no serialized field '{field}'. SetRef binds by string, so " +
                    "a renamed field fails here rather than silently wiring nothing.");
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// The stretched half of the muzzle flash: one quad, squashed on one
        /// axis, rolled independently of the round one by WeaponController.
        ///
        /// Two flat meshes at different aspect ratios, each rolled at random and
        /// scaled at random, is as far as a muzzle flash can be pushed without a
        /// texture — and a texture is 4 GB of VRAM budget plus an LFS object that
        /// is billed forever. It is a real improvement for zero bytes: what makes
        /// a repeated sprite read as a repeated sprite is a repeated SILHOUETTE,
        /// and this changes the silhouette on every shot.
        /// </summary>
        private static GameObject BuildWideFlashPrefab(Material material)
        {
            GameObject root = new("Fx_MuzzleFlash_Wide");
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Quad";
            quad.transform.SetParent(root.transform, false);
            // Wide and thin, against the round flash's uniform 0.22.
            quad.transform.localScale = new Vector3(0.52f, 0.075f, 1f);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            root.AddComponent<PooledObject>();
            SetLayerRecursive(root, RequireLayer(ViewmodelLayerName));
            return SavePrefab(root, Prefabs + "/Fx_MuzzleFlash_Wide.prefab");
        }

        /// <summary>
        /// The puff off the barrel at the end of a burst. Slow, wide, and gone
        /// inside a second — long enough to be seen, short enough that it is
        /// never between the player and the drone they are aiming at.
        /// </summary>
        private static GameObject BuildSmokePrefab(Material material)
        {
            GameObject root = new("Fx_MuzzleSmoke");
            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            // The defect this line exists for, lifted from BuildSparksPrefab: a
            // ParticleSystemRenderer with no material renders Unity default
            // magenta, and nothing anywhere fails.
            root.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = 0.7f;
            main.startSpeed = 0.55f;
            main.startSize = 0.06f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.35f));
            main.maxParticles = 8;
            main.playOnAwake = true;
            // Local, so the puff rides the gun for its first frames instead of
            // being left behind by a player who is already turning.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 5));

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.015f;

            // Grows and fades: smoke that keeps its size reads as a sprite.
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));

            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            root.AddComponent<PooledObject>();
            SetLayerRecursive(root, RequireLayer(ViewmodelLayerName));
            return SavePrefab(root, Prefabs + "/Fx_MuzzleSmoke.prefab");
        }

        /// <summary>
        /// One surface's spray. Every difference between concrete, plate, grate
        /// and flesh is a number in this call — which is the whole claim of the
        /// table: a new surface is DATA plus one line here, never new code.
        /// </summary>
        private static GameObject BuildImpactPrefab(string name, Material material, int burst, float speed,
            float size, float lifetime, float coneAngle, float gravity)
        {
            GameObject root = new(name);
            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            root.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.maxParticles = burst + 4;
            main.playOnAwake = true;
            // Gravity is what separates dust falling off a wall from sparks
            // arcing off plate. Both are the same three modules; only the numbers
            // differ.
            main.gravityModifier = gravity;
            // World: the debris stays where it was thrown even though the pooled
            // instance is parented back to the pool root on despawn.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)burst));

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.01f;

            root.AddComponent<PooledObject>();
            return SavePrefab(root, Prefabs + "/" + name + ".prefab");
        }

        // ---------- the table ----------

        /// <summary>
        /// Writes one row: references and the layer mask ALWAYS, the volume only
        /// when the row is new.
        ///
        /// The split is MissionBuilder's rule and it is the same problem. A
        /// prefab reference or a layer mask is a BINDING — a row bound to nothing
        /// is a surface that silently produces no spark and no sound, which reads
        /// as a missed shot rather than as a broken asset. A volume is a value
        /// somebody tuned by ear, and a build that quietly reverts it is the bug
        /// PaletteConfig exists to kill, arriving from the other side.
        ///
        /// The mask is written ONLY when the named layer resolves. NameToLayer
        /// answers -1 for a layer that does not exist, and `1 &lt;&lt; -1` is not
        /// zero — it is bit 31, which would silently claim whatever a future
        /// project puts in the last slot. An unresolvable name leaves the existing
        /// mask exactly as it is, so a re-run after the layer is added fills it in
        /// and a re-run before it does no harm.
        /// </summary>
        private static void WriteSurface(ImpactConfig impact, SurfaceType surface, string layerName,
            GameObject? decal, GameObject particles, AudioClip? clip, float volume)
        {
            ImpactConfig.SurfaceResponse row = EnsureRow(impact, surface, volume);

            row.decalPrefab = decal;
            row.particlePrefab = particles;
            // Null is the reversible fallback state when the optional audio kit
            // is empty. Keeping a previous source here would make nulling the kit
            // look successful while imported sound remained live.
            row.impactSound = clip;

            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                row.layers = 1 << layer;
                return;
            }

            Debug.LogWarning(
                $"VfxBuilder: there is no '{layerName}' layer, so the {surface} row claims nothing and every " +
                "hit on it falls through to the fallback. Add it to ProjectSettings/TagManager.asset (first free " +
                "user slot from index 9), put the matching colliders on it, and run CoD -> Build VFX again.");
        }

        /// <summary>
        /// Finds the row for a surface, appending a fresh one the first time.
        /// Order in the array is scan order, so appending keeps every existing
        /// row exactly where it is.
        /// </summary>
        private static ImpactConfig.SurfaceResponse EnsureRow(ImpactConfig impact, SurfaceType surface, float volume)
        {
            ImpactConfig.SurfaceResponse? existing = impact.Find(surface);
            if (existing != null) return existing;

            var row = new ImpactConfig.SurfaceResponse { surface = surface, volume = volume };
            var grown = new ImpactConfig.SurfaceResponse[impact.surfaces.Length + 1];
            System.Array.Copy(impact.surfaces, grown, impact.surfaces.Length);
            grown[^1] = row;
            impact.surfaces = grown;
            return row;
        }

        // ---------- helpers ----------

        /// <summary>
        /// A placeholder clip, with the import settings short SFX need: mono,
        /// uncompressed, decompressed on load. Lifted from GreyBoxBuilder.LoadClip
        /// because that method is private and this file exists so that authoring
        /// VFX never edits the builder that owns the scenes.
        ///
        /// A missing clip is collected rather than logged, so four absent files
        /// produce one actionable warning instead of four.
        /// </summary>
        private static AudioClip? LoadClip(string fileName, List<string> missing)
        {
            string path = Audio + "/" + fileName + ".wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                missing.Add(fileName + ".wav");
                return null;
            }

            if (AssetImporter.GetAtPath(path) is AudioImporter importer && !importer.forceToMono)
            {
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                importer.forceToMono = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            return clip;
        }

        private static Material LoadOrCreateParticleMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Additive, re-asserted every build for the reason GreyBoxBuilder's
        /// ApplyParticleSurface gives: URP's particle shaders are driven by BOTH
        /// float properties and shader KEYWORDS, and the material inspector is
        /// what normally keeps the two in sync. Set one without the other from a
        /// script and the material renders as opaque alpha-blend while every
        /// value in the Inspector reads correct.
        /// </summary>
        private static void ApplyAdditive(Material material, Color color)
        {
            const float TRANSPARENT = 1f;
            const float ADDITIVE = 2f;

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", TRANSPARENT);
            material.SetFloat("_Blend", ADDITIVE);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");

            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Alpha-blended, for the things that are SMOKE rather than LIGHT.
        ///
        /// Additive is right for sparks and wrong for dust: additive can only ever
        /// brighten what is behind it, so a dust puff in a dim corridor reads as a
        /// glowing cloud. Blood on an additive material is worse still — it comes
        /// out pink.
        /// </summary>
        private static void ApplyAlphaBlend(Material material, Color color)
        {
            const float TRANSPARENT = 1f;
            const float ALPHA = 0f;

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", TRANSPARENT);
            material.SetFloat("_Blend", ALPHA);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");

            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// A layer index, or a build that stops right here.
        ///
        /// LayerMask.NameToLayer answers -1 for a layer that does not exist, and
        /// Unity will cheerfully assign -1 to a GameObject: nothing throws, the
        /// build "succeeds", and the failure surfaces hours later as a muzzle
        /// flash that renders in no camera at all. Only used for the layers that
        /// MUST exist — the surface layers are allowed to be missing and are
        /// reported instead.
        /// </summary>
        private static int RequireLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                throw new System.InvalidOperationException(
                    $"VfxBuilder: there is no '{layerName}' layer in TagManager.asset. Anything spawned at the " +
                    "muzzle has to be on it or the overlay camera does not draw it.");
            }
            return layer;
        }

        /// <summary>Layers do NOT inherit down a hierarchy in Unity — every object in the subtree has to be moved by hand.</summary>
        private static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursive(child.gameObject, layer);
        }

        private static GameObject SavePrefab(GameObject instance, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
        }
    }
}
