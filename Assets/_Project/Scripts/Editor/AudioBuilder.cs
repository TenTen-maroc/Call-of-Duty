#nullable enable
using CoD.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CoD.EditorTools
{
    /// <summary>
    /// Authors the two audio config assets — the player's footsteps and the
    /// arena's room tone — with shipped defaults.
    ///
    /// Run it from the CoD menu, or headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.AudioBuilder.BuildAudioHeadless
    ///
    /// A SEPARATE FILE, for the reason MissionBuilder is one. GreyBoxBuilder owns
    /// the scenes, the prefabs, the materials and the navmesh, and it is three
    /// thousand lines; audio config is none of those things. Keeping it here means
    /// re-authoring a footstep default cannot cost the arena, and it means this
    /// builder can run without re-baking anything.
    ///
    /// WHAT THIS BUILDER DELIBERATELY DOES NOT DO.
    /// It does not create an AudioMixer, and no future version of it can. Unity's
    /// AudioMixerController is internal and exposes no public creation API, so a
    /// .mixer asset can only be made by a human, by hand, once, and committed —
    /// there is no headless path and there never will be. Both configs carry an
    /// `outputGroup` field that is left null on purpose so that the day the mixer
    /// exists, wiring audio into it is two drags in the Inspector. Read
    /// docs/systems/audio.md before assuming otherwise; the single most likely way
    /// to waste a session here is to design around a mixer this builder was
    /// supposed to have produced.
    ///
    /// It also synthesises no clips. A complete optional AudioKitConfig supplies
    /// imported audio; a null or all-null kit clears those references back to the
    /// valid silent fallback. Audio is a sneaky Git-LFS killer, so the imported
    /// source stays deliberately small and measured.
    ///
    /// IDEMPOTENT, with GreyBoxBuilder's and MissionBuilder's discipline: the
    /// configure callback runs ON CREATE ONLY, so a volume a human moved in the
    /// Inspector — or a clip they assigned — survives every re-run. The trap that
    /// comes with it is the same one: RENAMING a path below does not rename the
    /// asset, it creates a fresh default one, orphans every tuned value in the old
    /// file, and reports success.
    /// </summary>
    public static class AudioBuilder
    {
        private const string DataGame = "Assets/_Project/Data/Game";
        private const string FootstepPath = DataGame + "/Footsteps_Player.asset";
        private const string AmbiencePath = DataGame + "/Ambience_Arena.asset";
        private const string AudioKitPath = "Assets/_Project/Data/Kits/Kit_Audio_Default.asset";

        /// <summary>
        /// The one asset in this project that no builder created and no builder
        /// can. See the class header, and see <see cref="VerifyMixer"/> for the
        /// gate that exists because of it.
        /// </summary>
        private const string MixerPath = "Assets/_Project/Audio/Master.mixer";

        /// <summary>
        /// The bus every group name is checked against, in tree order.
        ///
        /// Hard-coded HERE and nowhere else, and that is the point: a hand-authored
        /// asset has no builder to describe it, so this array is the only written
        /// statement of what the mixer is supposed to contain. Rename a group in
        /// the editor and this fails; delete one and this fails.
        /// </summary>
        private static readonly string[] ExpectedGroups =
        {
            "Master", "SFX", "Weapons", "Impacts", "Enemies", "World",
            "UI", "Music", "Ambience", "Reverb",
        };

        /// <summary>
        /// The names gameplay code looks up by STRING, which is what makes them
        /// worth a gate: <c>SettingsHub</c> calls <c>SetFloat("MasterVolume", ..)</c>
        /// and a mixer that has been re-authored without it fails silently — the
        /// call returns false, nothing logs, and the volume slider stops working.
        /// </summary>
        private static readonly string[] ExpectedParameters =
        {
            "MasterVolume", "SfxVolume", "MusicVolume", "AmbienceVolume",
        };

        /// <summary>Where footsteps go. See <see cref="ExpectedGroups"/>.</summary>
        private const string FootstepGroupName = "World";

        /// <summary>Where the room tone goes.</summary>
        private const string AmbienceGroupName = "Ambience";

        /// <summary>
        /// Everything is on the Default layer in this project — see
        /// FootstepConfig.ResolveSurface for why that makes physics materials the
        /// real surface mechanism and layers the coarse fallback. This is a layer
        /// INDEX, not a tunable: it is the id of a slot in TagManager.asset.
        /// </summary>
        private const int LAYER_DEFAULT = 0;

        [MenuItem("CoD/Build Audio Config", false, 4)]
        public static void Build()
        {
            EnsureFolder(DataGame);

            FootstepConfig footsteps = LoadOrCreate<FootstepConfig>(FootstepPath, ConfigureFootsteps);
            AmbienceConfig ambience = LoadOrCreate<AmbienceConfig>(AmbiencePath, ConfigureAmbience);
            AudioKitConfig? kit = AssetDatabase.LoadAssetAtPath<AudioKitConfig>(AudioKitPath);
            if (kit != null && !kit.IsValid)
                throw new System.InvalidOperationException(AudioKitPath + " has mixed null/non-null references.");
            ApplyOptionalKit(footsteps, ambience, kit);

            // The mixer routing, RE-ASSERTED every run rather than configured on
            // create. It is a REFERENCE, and this project's builders all draw the
            // same line: a tuned number is a human's decision and survives, a
            // broken reference is not a decision and is repaired. An unrouted
            // footstep is not a quieter footstep — it bypasses the bus, ignores
            // every send on it, and cannot be mixed against anything.
            int routed = RouteToMixer(footsteps, ambience);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string clipState = kit != null && kit.HasCompleteAssignments
                ? $"{AudioKitConfig.ExpectedAssignmentCount}-clip optional kit applied"
                : "silent fallback applied";
            Debug.Log(
                $"Audio config built: {FootstepPath} and {AmbiencePath}, {routed} output group(s) routed, " +
                clipState + ". See docs/systems/audio.md.");
        }

        /// <summary>
        /// Points both configs at their bus on the hand-authored mixer.
        ///
        /// This used to be documented as "two drags in the Inspector" because no
        /// mixer existed to drag. Now that one does, it is two lines — and doing
        /// it here rather than by hand means the routing survives somebody
        /// deleting and rebuilding either config, which a drag would not.
        ///
        /// A missing mixer is a WARNING and not a failure: this builder must keep
        /// working on a checkout where the asset has not been authored yet, which
        /// is exactly the state the project was in until today.
        /// </summary>
        private static int RouteToMixer(FootstepConfig footsteps, AmbienceConfig ambience)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                Debug.LogWarning(
                    $"No AudioMixer at '{MixerPath}', so footsteps and ambience play straight to the listener. " +
                    "That is the shipped behaviour, not a bug — see docs/systems/audio.md.");
                return 0;
            }

            int routed = 0;
            AudioMixerGroup? world = FindGroup(mixer, FootstepGroupName);
            if (world != null && footsteps.outputGroup != world)
            {
                footsteps.outputGroup = world;
                EditorUtility.SetDirty(footsteps);
                routed++;
            }

            AudioMixerGroup? room = FindGroup(mixer, AmbienceGroupName);
            if (room != null && ambience.outputGroup != room)
            {
                ambience.outputGroup = room;
                EditorUtility.SetDirty(ambience);
                routed++;
            }
            return routed;
        }

        /// <summary>
        /// One group, by exact name.
        ///
        /// `FindMatchingGroups` does a SUBSTRING match on the group's path, so
        /// asking it for "Music" would also answer with a group called
        /// "MusicStinger" and asking for "Master" answers with everything under
        /// it. Every caller here wants one specific bus, so the name is compared
        /// exactly and an ambiguous answer returns null rather than a guess.
        /// </summary>
        private static AudioMixerGroup? FindGroup(AudioMixer mixer, string name)
        {
            foreach (AudioMixerGroup group in mixer.FindMatchingGroups(string.Empty))
            {
                if (group != null && group.name == name) return group;
            }
            return null;
        }

        // ---------- the gate for the asset nobody builds ----------

        /// <summary>
        /// Checks the hand-authored mixer against what the code expects of it.
        ///
        /// WHY THIS EXISTS AT ALL. Every other asset in this project is generated,
        /// which means every other asset is described by the code that generates
        /// it and re-created if it drifts. The mixer cannot be — `AudioMixerController`
        /// is internal to UnityEditor and has no public creation API — so it is
        /// simultaneously the only hand-authored asset and the only one with
        /// nothing watching it. A renamed bus or a dropped exposed parameter would
        /// surface as "the volume slider stopped working", months later, with no
        /// error anywhere: `AudioMixer.SetFloat` returns a bool that nobody reads
        /// and logs nothing.
        ///
        /// It checks NAMES rather than structure on purpose. Whether Reverb sits
        /// beside SFX or under it is a mixing decision a human is allowed to
        /// change; whether a group called `World` exists is a contract with
        /// `AudioBuilder` and `SettingsHub`.
        /// </summary>
        [MenuItem("CoD/Verify Audio Mixer", false, 5)]
        public static void VerifyMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                throw new System.InvalidOperationException(
                    $"No AudioMixer at '{MixerPath}'. It is the one asset here that no builder can produce — " +
                    "see docs/systems/audio.md before assuming this is a build step somebody forgot.");
            }

            var complaints = new System.Text.StringBuilder();

            foreach (string expected in ExpectedGroups)
            {
                if (FindGroup(mixer, expected) != null) continue;
                complaints.Append("\n  - no group named '").Append(expected).Append('\'');
            }

            var exposed = new SerializedObject(mixer).FindProperty("m_ExposedParameters");
            var found = new System.Collections.Generic.List<string>();
            for (int i = 0; i < exposed.arraySize; i++)
            {
                SerializedProperty entry = exposed.GetArrayElementAtIndex(i);
                found.Add(entry.FindPropertyRelative("name").stringValue);
            }

            foreach (string expected in ExpectedParameters)
            {
                if (found.Contains(expected)) continue;
                complaints.Append("\n  - '").Append(expected).Append("' is not exposed to script");
            }

            if (complaints.Length > 0)
            {
                throw new System.InvalidOperationException(
                    "The AudioMixer does not match what the code expects of it:" + complaints +
                    "\nIt is hand-authored — AudioMixerController is internal, so nothing can regenerate it. " +
                    "Open it in the editor and fix it, or update AudioBuilder's expectations if the change is " +
                    "deliberate. See docs/systems/audio.md.");
            }

            Debug.Log($"AudioMixer verified: {ExpectedGroups.Length} groups and " +
                      $"{ExpectedParameters.Length} exposed parameter(s) present in {MixerPath}.");
        }

        /// <summary>Entry point for -executeMethod. Non-zero exit on failure, so a gate can read it.</summary>
        public static void VerifyMixerHeadless()
        {
            try
            {
                VerifyMixer();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("AudioMixer verification failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Entry point for -executeMethod. Same work, non-zero exit on failure.</summary>
        public static void BuildAudioHeadless()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Audio config build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- footsteps ----------

        /// <summary>
        /// The shipped footstep defaults.
        ///
        /// The stride is tuned against the arena rather than against a person:
        /// walkSpeed is 5.2 m/s, so a 0.85 m stride puts roughly six steps a
        /// second under a sprint and four under a walk. That is faster than a real
        /// human and correct for this game — arcade movement speeds are roughly
        /// double life, and footsteps that keep a realistic cadence at those
        /// speeds are the "running on the spot" defect wearing a stopwatch.
        /// Nothing here is verified in play; it is a starting point for the tuning
        /// pass, which is the only thing that can actually judge it.
        /// </summary>
        private static void ConfigureFootsteps(FootstepConfig config)
        {
            config.strideLength = 0.85f;
            config.minSpeed = 0.55f;
            config.firstStepFraction = 0.55f;

            // Everything except the gun. The viewmodel layer holds the weapon,
            // which sits 30 cm from the camera and would otherwise be the nearest
            // thing a downward ray could plausibly find.
            int viewmodel = LayerMask.NameToLayer("Viewmodel");
            int mask = ~0;
            if (viewmodel >= 0) mask &= ~(1 << viewmodel);
            mask &= ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            config.groundMask = mask;

            config.probeStartHeight = 0.6f;
            config.probeDistance = 1.6f;

            config.walkVolume = 0.55f;
            config.walkPitch = 1f;
            config.sprintVolume = 0.85f;
            config.sprintPitch = 0.95f;
            config.crouchVolume = 0.22f;
            config.crouchPitch = 1.05f;

            config.pitchJitter = 0.07f;
            config.volumeJitter = 0.06f;

            config.landMinImpact = 0.12f;
            config.landVolume = 0.8f;
            config.landPitch = 0.9f;

            // TWO surfaces, and the second one cannot match anything yet — that is
            // the point. Concrete is the arena, and it is what every step falls
            // back to. Metal exists so the per-surface mechanism is visible and
            // one drag away from being real: give a catwalk collider a
            // PhysicsMaterial, drop the same asset into slot 1, and the floor
            // changes under the player with no code involved. A mechanism nobody
            // can see is a mechanism the next session reimplements.
            config.surfaces = new[]
            {
                new FootstepConfig.SurfaceSet
                {
                    label = "Concrete",
                    layers = 1 << LAYER_DEFAULT,
                    volumeScale = 1f,
                    pitchScale = 1f,
                },
                new FootstepConfig.SurfaceSet
                {
                    label = "Metal grating",
                    // Nothing: matched by physics material only, once one exists.
                    // A layer mask here would steal every step from Concrete,
                    // because the arena is entirely on Default.
                    layers = 0,
                    volumeScale = 1.15f,
                    pitchScale = 1.12f,
                },
            };
            config.defaultSurface = 0;

            // Null until a human authors the mixer. See the class header.
            config.outputGroup = null;
        }

        private static void ApplyOptionalKit(FootstepConfig footsteps, AmbienceConfig ambience,
            AudioKitConfig? kit)
        {
            bool complete = kit != null && kit.HasCompleteAssignments;
            if (footsteps.surfaces.Length > 0)
            {
                footsteps.surfaces[0].stepClips = complete
                    ? new[] { kit!.footstepConcreteA, kit.footstepConcreteB,
                        kit.footstepConcreteC, kit.footstepConcreteD }
                    : System.Array.Empty<AudioClip?>();
            }
            if (footsteps.surfaces.Length > 1)
            {
                footsteps.surfaces[1].stepClips = complete
                    ? new[] { kit!.impactMetal, kit.impactGrate }
                    : System.Array.Empty<AudioClip?>();
            }

            ambience.roomTone = complete ? kit!.roomTone : null;
            for (int i = 0; i < ambience.emitters.Length; i++)
            {
                ambience.emitters[i].clip = complete
                    ? (i == ambience.emitters.Length - 1 ? kit!.powerLoop : kit!.ventLoop)
                    : null;
            }
            EditorUtility.SetDirty(footsteps);
            EditorUtility.SetDirty(ambience);
        }

        // ---------- ambience ----------

        /// <summary>
        /// The shipped ambience defaults.
        ///
        /// The four placed loops sit on the arena's own landmarks, taken from
        /// docs/systems/arena.md: the three lane lights at (±14.5, 4.2, 4) and
        /// (0, 4.2, 14), and the centre bunker at (0, 2). Putting sound where the
        /// light already is means the two agree about where the facility's
        /// machinery lives without anyone maintaining a second list of positions
        /// — and it gives each lane an audible identity, which is the same job the
        /// lane lights were added to do for the eye.
        ///
        /// Nothing here is the origin. (0, 0, 0) is INSIDE Core_Bunker; it is the
        /// arena's oldest trap and an emitter there would be a hum inside a wall.
        /// </summary>
        private static void ConfigureAmbience(AmbienceConfig config)
        {
            config.roomToneVolume = 0.3f;
            config.roomTonePitch = 1f;
            config.fadeInSeconds = 1.5f;
            config.randomiseStartTime = true;

            config.emitters = new[]
            {
                new AmbienceConfig.Emitter
                {
                    label = "Vent_WestLane",
                    localPosition = new Vector3(-14.5f, 4.2f, 4f),
                    volume = 0.35f,
                    // Each emitter is detuned a little from the next, so two rows
                    // pointed at the same clip do not sum into one louder copy of
                    // it that follows the player down the middle of the arena.
                    pitch = 0.97f,
                    minDistance = 4f,
                    maxDistance = 18f,
                },
                new AmbienceConfig.Emitter
                {
                    label = "Vent_EastLane",
                    localPosition = new Vector3(14.5f, 4.2f, 4f),
                    volume = 0.35f,
                    pitch = 1.04f,
                    minDistance = 4f,
                    maxDistance = 18f,
                },
                new AmbienceConfig.Emitter
                {
                    label = "Vent_NorthLane",
                    localPosition = new Vector3(0f, 4.2f, 14f),
                    volume = 0.32f,
                    pitch = 1f,
                    minDistance = 4f,
                    maxDistance = 18f,
                },
                new AmbienceConfig.Emitter
                {
                    label = "PowerHum_CoreBunker",
                    // Just above the 3 m bunker roof, so the hum reads as coming
                    // off the centre mass rather than from inside it.
                    localPosition = new Vector3(0f, 3.4f, 2f),
                    volume = 0.3f,
                    pitch = 0.92f,
                    // Wider than the vents and slightly less positional: a floor's
                    // power plant should be felt everywhere and located loosely.
                    minDistance = 6f,
                    maxDistance = 26f,
                    spatialBlend = 0.8f,
                    spreadDegrees = 70f,
                },
            };

            // Null until a human authors the mixer. See the class header.
            config.outputGroup = null;
        }

        // ---------- helpers ----------

        /// <summary>
        /// Loads an asset, or creates and configures one if it is not there.
        ///
        /// A third copy of GreyBoxBuilder.LoadOrCreate, and the duplication is
        /// deliberate for the reason MissionBuilder states: that method is private,
        /// and these builders exist precisely so that authoring content never edits
        /// the file that owns the scenes.
        ///
        /// CONFIGURE RUNS ON CREATE ONLY. An asset that already exists comes back
        /// untouched — that is what lets a human retune a footstep volume, or drop
        /// in the WAVs, and keep it across a re-run.
        /// </summary>
        private static T LoadOrCreate<T>(string path, System.Action<T> configure) where T : ScriptableObject
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                configure(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
        }
    }
}
