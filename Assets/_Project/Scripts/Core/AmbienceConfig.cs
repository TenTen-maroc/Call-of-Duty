#nullable enable
using UnityEngine;
using UnityEngine.Audio;

namespace CoD.Core
{
    /// <summary>
    /// The arena's room tone: one 2D bed plus a handful of looping point sources
    /// placed at authored coordinates.
    ///
    /// WHY THIS IS THE CHEAPEST PRODUCTION VALUE IN THE PROJECT.
    /// A room with no sound in it does not read as quiet, it reads as unfinished —
    /// the player hears the game's silence rather than the facility's. A single
    /// low bed plus three or four hums an arena's width apart does more for "this
    /// is a real place" than any amount of geometry, costs four AudioSources and
    /// no frame time, and survives being on a 4 GB laptop because a mono loop at
    /// 22 kHz is a rounding error next to one texture.
    ///
    /// POSITIONS LIVE HERE, and that is deliberate rather than lazy. The arena is
    /// FIXED and it is GENERATED — nobody drags an emitter around in a scene view,
    /// because the scene is rebuilt from GreyBoxBuilder and any hand-placed object
    /// in it is an object one rebuild away from being gone. The coordinates in the
    /// shipped asset are the arena's own landmarks (the lane lights, the centre
    /// bunker), so an emitter and the light it belongs under are two rows in two
    /// assets rather than two objects somebody has to keep aligned by hand. They
    /// are LOCAL to the ArenaAmbience transform, so a second arena gets a second
    /// asset and not a rewrite.
    ///
    /// SILENCE IS A VALID STATE. Every clip here may be null; a null clip means
    /// "not authored yet" and produces no emitter and no warning. The project
    /// ships with no ambience WAVs, on purpose.
    ///
    /// THE MIXER IS DELIBERATELY ABSENT — see <see cref="outputGroup"/> and
    /// docs/systems/audio.md.
    /// </summary>
    [CreateAssetMenu(fileName = "Ambience_", menuName = "CoD/Ambience Config", order = 71)]
    public sealed class AmbienceConfig : ScriptableObject
    {
        /// <summary>
        /// One placed loop. A serializable class rather than a struct for the
        /// reason <see cref="FootstepConfig.SurfaceSet"/> gives: at langversion 9
        /// a struct cannot carry field initialisers, so every default here would
        /// deserialise as zero — including <c>volume</c> and <c>maxDistance</c>,
        /// which zeroed means a silent emitter that looks authored.
        /// </summary>
        [System.Serializable]
        public sealed class Emitter
        {
            [Tooltip("For humans reading the Inspector, and the name of the GameObject built for it. Never matched against anything.")]
            public string label = "Emitter";

            [Tooltip("Null is fine — it means this row is planned, not broken. No source is built and nothing is logged.")]
            public AudioClip? clip = null;

            [Tooltip("LOCAL to the ArenaAmbience transform. Place that component at the arena origin and these are arena coordinates.")]
            public Vector3 localPosition = Vector3.zero;

            [Range(0f, 1f)] public float volume = 0.4f;

            [Tooltip("A few percent off 1 between emitters stops two copies of the same loop phase-locking into one louder copy.")]
            [Range(0.25f, 4f)] public float pitch = 1f;

            [Tooltip("Metres. Inside this radius the emitter is at full volume — this is the size of the thing making the noise, not how far it carries.")]
            [Min(0.1f)] public float minDistance = 4f;

            [Tooltip("Metres. With Linear rolloff this is genuinely silent, which is what keeps four emitters in a 40 m arena from summing into mush.")]
            [Min(0.2f)] public float maxDistance = 20f;

            [Tooltip("1 is fully positional. Drop it slightly for something that should be felt everywhere but located loosely, like a floor-wide power hum.")]
            [Range(0f, 1f)] public float spatialBlend = 1f;

            [Tooltip("Degrees. 0 collapses the source to a single point that snaps between ears as the player turns; a little spread makes a machine feel like it has a size.")]
            [Range(0f, 360f)] public float spreadDegrees = 40f;

            [Tooltip("Linear reaches actual silence at maxDistance. Logarithmic never does, so every emitter stays faintly audible from everywhere — usually the wrong answer indoors.")]
            public AudioRolloffMode rolloff = AudioRolloffMode.Linear;
        }

        [Header("Room tone — the 2D bed under everything")]
        [Tooltip("Looped, non-positional, and quiet enough to be noticed only when it stops. Null means not authored yet.")]
        public AudioClip? roomTone = null;

        [Tooltip("Low. The bed's job is to remove silence, not to be heard.")]
        [Range(0f, 1f)] public float roomToneVolume = 0.3f;

        [Range(0.25f, 4f)] public float roomTonePitch = 1f;

        [Header("Fade")]
        [Tooltip("Seconds to reach full volume after the scene loads. Starting a loop at full volume on frame one is an audible click, and it arrives at the exact moment the player is judging whether the game is finished.")]
        [Range(0f, 20f)] public float fadeInSeconds = 1.5f;

        [Header("Placed loops")]
        [Tooltip("Small on purpose. Four hums an arena apart read as a facility; twelve read as a hum.")]
        public Emitter[] emitters = System.Array.Empty<Emitter>();

        [Tooltip("Starts every loop at a random point in the clip. Without it, N sources given the same clip start in perfect phase and sum into one source that is N times too loud and moves with the player.")]
        public bool randomiseStartTime = true;

        [Header("Mixer — not authored yet, on purpose")]
        [Tooltip("Left empty until a human hand-authors an AudioMixer: no builder can generate one. See docs/systems/audio.md. Dropping a group in here is the whole migration.")]
        public AudioMixerGroup? outputGroup = null;

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < emitters.Length; i++)
            {
                Emitter? emitter = emitters[i];
                if (emitter == null) continue;

                // maxDistance at or below minDistance makes Unity's rolloff curve
                // degenerate: the source is either full volume everywhere or
                // silent everywhere, with no error and no visible cause.
                if (emitter.maxDistance <= emitter.minDistance)
                {
                    emitter.maxDistance = emitter.minDistance + 1f;
                }
            }
        }
#endif
    }
}
