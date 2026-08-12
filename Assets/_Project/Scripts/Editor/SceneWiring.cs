#nullable enable
using System.Collections.Generic;
using CoD.Core;
using CoD.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoD.EditorTools
{
    /// <summary>
    /// Puts the components that have no builder into the arena scene: the
    /// player's <see cref="Footsteps"/> and the arena's <see cref="ArenaAmbience"/>.
    ///
    /// Run it from the CoD menu, or headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.SceneWiring.WireSceneExtrasHeadless
    ///
    /// THE DISASTER THIS FILE EXISTS FOR. Both components, both their config
    /// assets and a whole documentation page shipped in one commit, and the game
    /// stayed silent — because nothing anywhere put either component into a
    /// scene. Every gate was green: typecheck compiled them, the guards passed
    /// over them, the configs were on disk with correct defaults, and the feature
    /// did not exist at runtime. Code with no scene presence is not a feature
    /// that needs tuning, it is a feature that is not installed, and nothing in
    /// this project's toolchain reports the difference. That is the shape of
    /// failure this file closes, and it is why the last thing it does is re-open
    /// the saved scene and prove the components are in it.
    ///
    /// A SEPARATE FILE FROM GreyBoxBuilder, for the reason MissionBuilder and
    /// AudioBuilder are separate files. The grey box owns the scenes, the
    /// prefabs, the materials and the navmesh, and it is four thousand lines;
    /// this is two components and four references. Keeping it out means wiring
    /// audio can never cost the arena.
    ///
    /// RUN ORDER: GREY BOX FIRST, THEN THIS. GreyBoxBuilder does not edit
    /// 10_GreyBox — it calls EditorSceneManager.NewScene(EmptyScene) and writes a
    /// brand new one over the top. Every component added here is therefore GONE
    /// after a grey box rebuild, silently, with no error and no missing
    /// reference: the scene is simply whole and quiet. Re-run this after every
    /// `CoD → Build Grey Box`, the same way `CoD → Build Missions` has to be
    /// re-run after one. It is also why this pass is idempotent — running it
    /// again must always be safe, because "run it again" is the fix.
    ///
    /// IDEMPOTENT, and checked component-first rather than name-first. A second
    /// Footsteps on the Player would be a second set of steps at the same
    /// cadence — which is not twice as loud, it is a flam, and it reads as a
    /// broken clip rather than as a duplicated component. A second ArenaAmbience
    /// would build a second full set of emitters from the same asset, doubling
    /// every loop. So the existence test is "does this component already exist on
    /// this object", never "is there an object with the right name".
    ///
    /// REFERENCES ARE RE-ASSERTED, VALUES ARE NOT. WriteWave's rule, applied to a
    /// scene: every run re-points _config, _motor and _audio at what they must
    /// be, because a null reference here is a component that warns once at Awake
    /// and then does nothing for the rest of the run. Nothing a human tuned in
    /// the Inspector is touched — and SetRef only writes when the value actually
    /// differs, so a second run leaves the scene byte-identical and never re-saves
    /// it.
    ///
    /// WHAT IT DOES NOT DO. It adds no clips (there are none, and silence is the
    /// shipped state — see docs/systems/audio.md), it creates no AudioMixer (it
    /// cannot; that API is internal to UnityEditor), and it does not touch
    /// 20_MainMenu. The menu has no player and no arena.
    /// </summary>
    public static class SceneWiring
    {
        private const string GreyBoxScenePath = "Assets/_Project/Scenes/10_GreyBox.unity";
        private const string FootstepConfigPath = "Assets/_Project/Data/Game/Footsteps_Player.asset";
        private const string AmbienceConfigPath = "Assets/_Project/Data/Game/Ambience_Arena.asset";

        /// <summary>
        /// The root object ArenaAmbience hangs on. A NAME, not a tuning number —
        /// it exists so a human scanning the hierarchy can find the thing making
        /// the noise. Nothing matches against it except the "adopt an existing
        /// empty of this name" fallback below.
        /// </summary>
        private const string AmbienceObjectName = "Ambience";

        /// <summary>
        /// Returned when a human cancels at the "save the open scene?" prompt.
        /// Distinct from 0 on purpose: nothing was opened, nothing was checked,
        /// and reporting that as success would be a green light over an untouched
        /// scene. Unreachable in batch mode, where there is no prompt.
        /// </summary>
        private const int CANCELLED = -1;

        [MenuItem("CoD/Wire Scene Extras", false, 4)]
        public static void WireSceneExtras() => WireAndReport();

        /// <summary>
        /// The same pass, but it TELLS you: the number of references still
        /// unresolved after the save/reload round trip, 0 when everything landed.
        /// WireSceneExtras has to return void to be a [MenuItem], and a void
        /// return is how a proven-broken scene exits zero — the exact hole
        /// GreyBoxVerify.VerifyHeadless was written to close.
        /// </summary>
        public static int WireAndReport()
        {
            // EditorSceneManager.OpenScene DISCARDS whatever is open without
            // asking. From a menu item that is somebody's afternoon; ask first.
            // Skipped in batch mode, where there is nobody to ask and no scene
            // worth keeping — and where a modal dialog is a hang, not a prompt.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("SceneWiring: cancelled at the save prompt. Nothing was opened and nothing changed.");
                return CANCELLED;
            }

            Scene scene = EditorSceneManager.OpenScene(GreyBoxScenePath, OpenSceneMode.Single);

            // Loaded AFTER the scene is open, never before. Closing a scene lets
            // Unity unload every asset it was the last thing holding, and a C#
            // handle to an unloaded UnityEngine.Object compares equal to null — so
            // a handle taken before the switch would silently wire nothing while
            // reporting that it wired something. GreyBoxVerify paid a build round
            // to learn this; it is written down there too.
            FootstepConfig? footstepConfig = AssetDatabase.LoadAssetAtPath<FootstepConfig>(FootstepConfigPath);
            AmbienceConfig? ambienceConfig = AssetDatabase.LoadAssetAtPath<AmbienceConfig>(AmbienceConfigPath);

            var tally = new Tally();
            WireFootsteps(scene, footstepConfig, tally);
            WireAmbience(scene, ambienceConfig, tally);

            if (tally.Added > 0 || tally.Rewired > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }

            // Re-open from disk. The only proof a reference persisted is reading
            // it back after a round trip: this project has already shipped a scene
            // where every ASSET reference came back {fileID: 0} while every
            // SCENE-object reference survived, with nothing logged either way.
            // Run unconditionally, including on a pass that changed nothing —
            // "changed nothing" and "was already correct" are different states and
            // only the reload can tell them apart.
            scene = EditorSceneManager.OpenScene(GreyBoxScenePath, OpenSceneMode.Single);
            VerifyRoundTrip(scene, tally.Problems);

            Debug.Log(
                $"SceneWiring: added {tally.Added} component(s), rewired {tally.Rewired} reference(s), " +
                $"unresolved {tally.Problems.Count}  [{GreyBoxScenePath}]");

            if (tally.Problems.Count > 0)
            {
                Debug.LogError("SceneWiring: UNRESOLVED after save+reload:\n  " +
                               string.Join("\n  ", tally.Problems));
            }
            else
            {
                Debug.Log("SceneWiring: footsteps and ambience are in the scene, and every reference " +
                          "survived a save/reload round trip. There are still no clips — see docs/systems/audio.md.");
            }

            // The verified scene is deliberately left OPEN and clean rather than
            // replaced with an empty untitled one. Nothing here holds unsaved
            // state — the save happened above and this copy came straight off
            // disk — and closing it would leave a human staring at an empty
            // hierarchy wondering whether the pass ate their arena.
            return tally.Problems.Count;
        }

        /// <summary>Entry point for -executeMethod. Same work, non-zero exit on anything unresolved.</summary>
        public static void WireSceneExtrasHeadless()
        {
            try
            {
                int unresolved = WireAndReport();
                if (unresolved != 0)
                {
                    // LogError is not a failure; an exit code is. A headless run
                    // that printed the problem and exited 0 is how the silent
                    // scene shipped in the first place.
                    Debug.LogError($"SceneWiring: {unresolved} unresolved — failing the run.");
                    EditorApplication.Exit(1);
                    return;
                }
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("SceneWiring failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- footsteps ----------

        /// <summary>
        /// Footsteps on whatever carries the PlayerMotor, with an AudioSource
        /// beside it.
        ///
        /// FOUND BY COMPONENT, NEVER BY NAME. "Player" is a string the grey box
        /// happens to use; PlayerMotor is the thing Footsteps actually needs, and
        /// it is on the same GameObject by construction because Footsteps reads
        /// its speed, gait, grounding and landing impulse. Matching on the
        /// component means renaming the object in the builder cannot quietly turn
        /// this pass into a no-op.
        ///
        /// THE AUDIO SOURCE GOES ON THE PLAYER ROOT, not on a child of its own,
        /// and that is a robustness choice rather than tidiness. Footsteps.Awake
        /// falls back to GetComponent&lt;AudioSource&gt;() on its own GameObject,
        /// so a source on the root means a _audio reference that somehow failed
        /// to persist still resolves at runtime; a source on a child would leave
        /// the component disabled with one warning. It is safe here because
        /// exactly one script in the project calls GetComponent&lt;AudioSource&gt;()
        /// — that fallback — and nothing else on the Player owns a source: the
        /// weapon's two live on the camera and the HUD's lives on the canvas.
        /// Spatial blend does not matter either way; a footstep source is 2D.
        /// </summary>
        private static void WireFootsteps(Scene scene, FootstepConfig? config, Tally tally)
        {
            List<PlayerMotor> motors = FindAll<PlayerMotor>(scene);
            if (motors.Count == 0)
            {
                tally.Problems.Add(
                    "no PlayerMotor anywhere in " + GreyBoxScenePath +
                    " — run CoD -> Build Grey Box first, then this");
                return;
            }

            if (config == null)
            {
                // Deliberately adds NOTHING in this case. A Footsteps with a null
                // config warns once in Awake and disables itself, which is a
                // component in the scene that is not a feature — the exact
                // half-installed state this file exists to stop.
                tally.Problems.Add(
                    FootstepConfigPath + " is not on disk — run CoD -> Build Audio Config first");
                return;
            }

            foreach (PlayerMotor motor in motors)
            {
                GameObject owner = motor.gameObject;

                Footsteps? existing = owner.GetComponent<Footsteps>();
                Footsteps footsteps;
                if (existing == null)
                {
                    footsteps = owner.AddComponent<Footsteps>();
                    tally.Added++;
                }
                else
                {
                    footsteps = existing;
                }

                AudioSource? existingSource = owner.GetComponent<AudioSource>();
                AudioSource source;
                if (existingSource == null)
                {
                    source = owner.AddComponent<AudioSource>();
                    // Footsteps.Awake forces all four of these at runtime. They
                    // are written here as well so the scene tells the truth to a
                    // human reading the Inspector — and because playOnAwake
                    // defaults to TRUE on a fresh AudioSource, which is a
                    // property nobody expects to have to turn off.
                    source.playOnAwake = false;
                    source.loop = false;
                    source.spatialBlend = 0f;
                    source.dopplerLevel = 0f;
                    tally.Added++;
                }
                else
                {
                    source = existingSource;
                }

                // Field names are copied from Footsteps.cs, not remembered. A
                // wrong name here does not fail to compile and does not throw —
                // SerializedObject.FindProperty simply returns null, the
                // assignment goes nowhere, and the player walks in silence that
                // looks exactly like having no clips. SetRef turns that into a
                // logged, exit-code-bearing failure.
                SetRef(footsteps, "_config", config, tally);
                SetRef(footsteps, "_motor", motor, tally);
                SetRef(footsteps, "_audio", source, tally);
            }

            // A Footsteps somewhere it does not belong — the camera being the
            // tempting wrong answer. The probe origin is the component's own
            // transform, and the camera's transform carries the landing dip and
            // the shake, so a probe from there is a probe from a moving target.
            foreach (Footsteps stray in FindAll<Footsteps>(scene))
            {
                if (stray.GetComponent<PlayerMotor>() != null) continue;
                Debug.LogWarning(
                    $"SceneWiring: Footsteps on '{HierarchyPath(stray.transform)}' has no PlayerMotor beside it. " +
                    "It belongs on the Player root — the ground probe fires from its own transform, and a camera " +
                    "transform carries the landing dip and the shake.", stray);
            }
        }

        // ---------- ambience ----------

        /// <summary>
        /// One ArenaAmbience on its own root object at the world origin.
        ///
        /// ITS ONLY SERIALIZED FIELD IS _config, and there are no emitter
        /// transforms to wire — that is the component's whole design, not an
        /// omission. ArenaAmbience builds its own AudioSource children in Awake
        /// from the rows in the asset, because the arena scene is regenerated
        /// whenever the geometry moves and any emitter hand-placed in it is one
        /// rebuild away from being gone while the config still lists it.
        ///
        /// THE TRANSFORM IS LOAD-BEARING. Emitter positions in AmbienceConfig are
        /// LOCAL to this object, and they are authored as arena coordinates — the
        /// three lane lights and the centre bunker. Move, rotate or scale this
        /// object and every hum moves with it, silently and correctly as far as
        /// any code can tell. So a drift is reported rather than corrected: an
        /// offset might be a second arena, and quietly undoing somebody's
        /// deliberate move is how a builder and a human start disagreeing.
        /// </summary>
        private static void WireAmbience(Scene scene, AmbienceConfig? config, Tally tally)
        {
            if (config == null)
            {
                tally.Problems.Add(
                    AmbienceConfigPath + " is not on disk — run CoD -> Build Audio Config first");
                return;
            }

            List<ArenaAmbience> existing = FindAll<ArenaAmbience>(scene);
            if (existing.Count > 1)
            {
                Debug.LogWarning(
                    $"SceneWiring: {existing.Count} ArenaAmbience components in the scene. Each one builds a FULL " +
                    "set of sources from the config, so two of them is every loop playing twice — and with " +
                    "randomiseStartTime on they will not even be in phase, which reads as a bad clip rather than " +
                    "as a duplicated component. Delete all but one.");
            }

            if (existing.Count > 0)
            {
                // Every one of them, not just the first. The duplicate is
                // reported above and left for a human to delete — but a
                // duplicate holding a NULL config would then fail the round trip
                // below with a message about a missing reference, which points at
                // the wrong fault entirely.
                foreach (ArenaAmbience found in existing)
                {
                    WarnIfMoved(found.transform);
                    SetRef(found, "_config", config, tally);
                }
                return;
            }

            // Adopt an existing empty of the right name before making a new one:
            // a half-finished run that created the object and failed before
            // adding the component must not leave two of them behind.
            GameObject? host = FindRoot(scene, AmbienceObjectName);
            if (host == null)
            {
                host = new GameObject(AmbienceObjectName);
                // Explicit rather than relying on the default, because these
                // three values are the frame every emitter coordinate is
                // measured in — see the summary above.
                host.transform.position = Vector3.zero;
                host.transform.rotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;
            }
            else
            {
                WarnIfMoved(host.transform);
            }

            ArenaAmbience ambience = host.AddComponent<ArenaAmbience>();
            tally.Added++;
            SetRef(ambience, "_config", config, tally);
        }

        /// <summary>
        /// Reports an ambience root that is not the arena's own frame. Never
        /// corrects it — see WireAmbience for why a silent correction is the
        /// worse of the two failures.
        /// </summary>
        private static void WarnIfMoved(Transform transform)
        {
            bool moved = transform.position.sqrMagnitude > 0.0001f;
            bool turned = Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.01f;
            bool scaled = (transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f;
            if (!moved && !turned && !scaled) return;

            Debug.LogWarning(
                $"SceneWiring: '{HierarchyPath(transform)}' is not at the world origin with an identity rotation " +
                $"and unit scale (position {transform.position}, scale {transform.lossyScale}). Every emitter " +
                "position in AmbienceConfig is LOCAL to it and authored as an arena coordinate, so the room tone " +
                "is now somewhere the arena is not. Left as found on purpose — move it back, or re-author the " +
                "emitter rows.", transform);
        }

        // ---------- the round trip ----------

        /// <summary>
        /// Reads the saved scene back off disk and proves both components are in
        /// it with every reference intact. This is the half that turns a wiring
        /// script into a gate.
        /// </summary>
        private static void VerifyRoundTrip(Scene scene, List<string> problems)
        {
            List<Footsteps> footsteps = FindAll<Footsteps>(scene);
            if (footsteps.Count == 0)
            {
                problems.Add("Footsteps: not in the scene after save+reload");
            }
            foreach (Footsteps steps in footsteps)
            {
                Check(steps, "_config", problems);
                Check(steps, "_motor", problems);
                // The one that self-heals at runtime through Awake's
                // GetComponent fallback — checked anyway, because a reference
                // that needs healing is a reference that did not persist, and the
                // next one that fails this way may not have a fallback.
                Check(steps, "_audio", problems);
            }

            List<ArenaAmbience> ambience = FindAll<ArenaAmbience>(scene);
            if (ambience.Count == 0)
            {
                problems.Add("ArenaAmbience: not in the scene after save+reload");
            }
            foreach (ArenaAmbience arena in ambience)
            {
                Check(arena, "_config", problems);
            }
        }

        // ---------- helpers ----------

        /// <summary>Counters and failures for one pass. An instance, not statics — Domain Reload is off.</summary>
        private sealed class Tally
        {
            public int Added;
            public int Rewired;
            public readonly List<string> Problems = new();
        }

        /// <summary>
        /// Writes a serialized reference, and only when it differs.
        ///
        /// The equality test is what makes a second run a no-op: writing an
        /// identical value would still dirty the scene and re-save it, and a pass
        /// that rewrites a 300 KB scene file every time it is run is a pass
        /// nobody can tell apart from one that changed something.
        ///
        /// A MISSING FIELD IS A HARD FAILURE, not a shrug. Inspector fields here
        /// are [SerializeField] private, so they are reachable only by string —
        /// and a string that no longer names a field produces no compile error,
        /// no exception and no assignment. The symptom is silence.
        /// </summary>
        private static void SetRef(Object target, string field, Object value, Tally tally)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                tally.Problems.Add($"{target.GetType().Name}.{field} (no such serialized field)");
                Debug.LogError(
                    $"SceneWiring: {target.GetType().Name} has no serialized field '{field}'. Either it was " +
                    "renamed or this name was guessed — both produce a silent null that nothing else reports.",
                    target);
                return;
            }

            if (property.objectReferenceValue == value) return;

            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            tally.Rewired++;
        }

        /// <summary>
        /// A reference read back after the round trip. Reports a missing FIELD as
        /// loudly as a missing value: rename a serialized field and every check
        /// naming the old one would otherwise start passing, so the checks quietly
        /// stop covering what they were written for.
        /// </summary>
        private static void Check(Object target, string field, List<string> problems)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                problems.Add($"{target.GetType().Name}.{field} on '{target.name}' (no such field)");
                return;
            }
            if (property.objectReferenceValue == null)
            {
                // The owning object is named because a stray or duplicated
                // component fails here, and "Footsteps._config" alone does not
                // say WHICH Footsteps — which is the difference between a
                // one-line fix and a hunt through the hierarchy.
                problems.Add($"{target.GetType().Name}.{field} on '{target.name}'");
            }
        }

        /// <summary>Every component of a type in the scene, inactive objects included.</summary>
        private static List<T> FindAll<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return found;
        }

        private static GameObject? FindRoot(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName) return root;
            }
            return null;
        }

        /// <summary>Full hierarchy path, so a warning names one object rather than a type.</summary>
        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (Transform? parent = transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return path;
        }
    }
}
