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
        [Range(0f, 4f)] public float trimEmission = 1.2f;

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

        [Header("Atmosphere")]
        public Color ambientSky = new(0.22f, 0.25f, 0.31f);
        public Color ambientEquator = new(0.15f, 0.16f, 0.18f);
        public Color ambientGround = new(0.07f, 0.07f, 0.08f);
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
