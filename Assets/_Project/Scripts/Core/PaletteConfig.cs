#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Every colour the arena is built from, in one asset.
    ///
    /// THE BUG THIS EXISTS TO KILL, permanently.
    /// The builder's LoadOrCreateMaterial returns an existing .mat UNTOUCHED —
    /// correct, because a value a human tuned in the Inspector must not be
    /// stomped by the next build. But it also meant the colour literals in the
    /// builder were only ever read ONCE, on the day each material was created.
    /// The "grey/red tactical palette" commit later changed those literals and
    /// they never reached disk: GreyBox_Floor shipped at 0.32 grey against an
    /// intended 0.17, roughly 1.9x too bright, for the entire life of the
    /// project. Everything compiled, every guard passed, all tests were green,
    /// and the fix silently did nothing.
    ///
    /// A literal in an editor script is not a tunable — it is a value that can
    /// drift away from the asset it claims to describe with no gate able to see
    /// it. Moving the palette here fixes the class rather than the instance:
    /// the numbers now live where CLAUDE.md says every tunable lives, and
    /// ApplyPalette re-asserts them on every build the same way ApplySurface
    /// already re-asserts smoothness and metallic.
    ///
    /// Tuning one of these in the Inspector is safe. Tuning the .mat directly
    /// is not — the next build overwrites it. That is the trade, and it is the
    /// right way round: the asset a human edits should be the one that wins.
    /// </summary>
    [CreateAssetMenu(fileName = "Palette_", menuName = "CoD/Palette Config", order = 60)]
    public sealed class PaletteConfig : ScriptableObject
    {
        [Header("Architecture — cool and dark on purpose")]
        [Tooltip("Every threat is read by the colour of its core. Nothing structural may be warm and bright, or the player learns to check walls for danger.")]
        public Color floor = new(0.17f, 0.18f, 0.20f);
        public Color wall = new(0.28f, 0.29f, 0.32f);
        [Tooltip("Edge trim. Cool marks places; warm means something is trying to kill you.")]
        public Color trim = new(0.30f, 0.62f, 0.92f);
        // Raised with the light rig drop below. Under a dim sun the trim line is
        // no longer a highlight on a block you can already see — it IS the
        // silhouette, and "can I shoot over that" has to be answerable across the
        // arena or the half-height cover stops being cover worth using.
        [Range(0f, 4f)] public float trimEmission = 1.6f;

        [Header("Targets and weapons")]
        public Color practiceTarget = new(0.62f, 0.13f, 0.11f);
        public Color weaponBody = new(0.10f, 0.105f, 0.115f);
        public Color weaponAccent = new(0.055f, 0.06f, 0.065f);

        [Header("Drones — hull dark so the core is the only thing the eye tracks")]
        public Color droneHull = new(0.13f, 0.14f, 0.17f);
        public Color rusherCore = new(0.75f, 0.12f, 0.10f);
        [Range(0f, 4f)] public float rusherEmission = 1.6f;
        public Color shooterCore = new(0.95f, 0.55f, 0.10f);
        [Range(0f, 4f)] public float shooterEmission = 1.8f;
        public Color tankCore = new(0.85f, 0.06f, 0.22f);
        [Range(0f, 4f)] public float tankEmission = 1.4f;

        [Header("Help — green, and nothing else in the game may be green")]
        public Color objectiveBeacon = new(0.20f, 0.90f, 0.55f);
        [Range(0f, 4f)] public float objectiveEmission = 1.8f;

        [Header("VFX — additive, so these are light rather than surface")]
        [Tooltip("Muzzle flash and impact sparks.")]
        public Color sparkHot = new(1f, 0.82f, 0.45f);
        [Tooltip("Explosions, slams, drone deaths.")]
        public Color fire = new(1f, 0.55f, 0.20f);

        [Header("The light rig — dim on purpose, because the enemies are the light")]
        // WHY THESE MOVED HERE, and why they are lower than they were.
        //
        // They were literals in GreyBoxBuilder.BuildArenaLights and BuildLighting,
        // which is the exact shape of the bug at the top of this file: a number in
        // an editor script that no gate can compare against the thing it claims to
        // describe. Now they are tunable in the Inspector, which matters more for
        // these than for any other value in the project, because lighting is the
        // one thing nobody can verify without looking at it.
        //
        // The levels themselves were BACKWARDS. Every enemy carries an emissive
        // core that ramps from ~0.4 idle to ~4.0 when it telegraphs an attack —
        // a 10x jump that CLAUDE.md calls a fairness contract rather than
        // decoration. The arena was lit at a sun of 0.85 with lane lights at 1.6
        // and a key at 2.2, so the room was brighter than the threat in it: the
        // telegraph washed out to a slightly lighter dot, and the one channel the
        // player is supposed to read danger from was the dimmest thing on screen.
        //
        // Dropping the rig to roughly 40% does three things at once and costs no
        // VRAM, which is the binding constraint on everything else in this
        // project: the arena reads as a place instead of a flat field, the
        // telegraph becomes genuinely alarming, and bloom finally has something
        // to bite on. The trim emission goes UP for the same reason — in a darker
        // room the lit edge along each block is what tells the player whether
        // they can shoot over it.
        [Tooltip("The only shadow caster in the arena. Low: it is here for shape, not for illumination.")]
        [Range(0f, 3f)] public float sunIntensity = 0.35f;
        public Color sunColor = new(0.82f, 0.86f, 1f);

        [Tooltip("The three warm lane lights. Warm marks a route; saturated warm is reserved for drone cores.")]
        public Color laneLight = new(1f, 0.72f, 0.45f);
        [Range(0f, 4f)] public float laneLightIntensity = 0.8f;
        [Min(1f)] public float laneLightRange = 15f;

        [Tooltip("The one cool key on the centre mass. It is what makes the bunker read as the thing to orbit.")]
        public Color keyLight = new(0.70f, 0.82f, 1f);
        [Range(0f, 4f)] public float keyLightIntensity = 1.1f;
        [Min(1f)] public float keyLightRange = 14f;

        [Header("Atmosphere")]
        // Ambient is halved alongside the rig. It is the single biggest enemy of
        // contrast in a URP scene: lights can be dropped to nothing and a bright
        // ambient term will still flood every surface evenly, which is exactly
        // the flat look this change exists to remove.
        public Color ambientSky = new(0.11f, 0.13f, 0.17f);
        public Color ambientEquator = new(0.07f, 0.075f, 0.09f);
        public Color ambientGround = new(0.035f, 0.035f, 0.04f);
        public Color fogColor = new(0.12f, 0.13f, 0.16f);
        [Min(0f)] public float fogStart = 14f;
        [Min(1f)] public float fogEnd = 55f;

        [Tooltip("What metallic surfaces reflect when there is no probe. The arena is a sealed interior; the default skybox made the gun mirror a bright blue sky.")]
        public Color indoorReflection = new(0.10f, 0.11f, 0.13f);

#if UNITY_EDITOR
        private void OnValidate()
        {
            // A fog range that ends before it starts renders as fully fogged
            // geometry at every distance — an arena you cannot see across, with
            // no error anywhere.
            if (fogEnd <= fogStart) fogEnd = fogStart + 1f;
        }
#endif
    }
}
