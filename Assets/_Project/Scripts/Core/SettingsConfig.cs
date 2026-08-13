#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// The bounds and step sizes of every player-facing setting. One asset, in
    /// Assets/_Project/Data/Game/.
    ///
    /// WHY THIS EXISTS SEPARATELY FROM GameConfig
    /// GameConfig holds what the DESIGNER picked; this holds what the PLAYER is
    /// allowed to pick. They are different lifetimes: a GameConfig value is the
    /// shipped default and is read-only at runtime, while the player's choice is
    /// live state that changes mid-session and is written to disk. Keeping the
    /// range next to the default would invite exactly the runtime write to a
    /// ScriptableObject that Domain-Reload-off makes permanent.
    /// </summary>
    [CreateAssetMenu(fileName = "Settings", menuName = "CoD/Settings Config", order = 1)]
    public sealed class SettingsConfig : ScriptableObject
    {
        [Header("Mouse sensitivity (degrees per mouse-count)")]
        [Range(0.01f, 1f)] public float sensitivityMin = 0.02f;
        [Range(0.01f, 1f)] public float sensitivityMax = 0.60f;
        [Tooltip("How much one press of the adjust key moves the value.")]
        [Range(0.001f, 0.1f)] public float sensitivityStep = 0.01f;

        [Header("Field of view (VERTICAL degrees — 62 is roughly 95 horizontal)")]
        [Range(40f, 90f)] public float fovMin = 50f;
        [Range(40f, 90f)] public float fovMax = 85f;
        [Range(0.5f, 10f)] public float fovStep = 1f;

        [Header("Volume (0..1, linear)")]
        [Range(0f, 1f)] public float volumeMin = 0f;
        [Range(0f, 1f)] public float volumeMax = 1f;
        [Range(0.01f, 0.5f)] public float volumeStep = 0.05f;

        [Header("Graphics — the SHIPPED choice, not the player's")]
        [Tooltip("Post-processing on by default. The off switch is the escape hatch on a 4 GB laptop.")]
        public bool postProcessingDefault = true;
        [Tooltip("SMAA by default: the arena is built from hard-edged primitives, the worst case for edge crawl.")]
        public AntiAliasingMode antiAliasingDefault = AntiAliasingMode.Smaa;

        [Header("Subtitles — the SHIPPED accessibility defaults")]
        public bool subtitlesEnabledDefault = true;
        public SubtitleSize subtitleSizeDefault = SubtitleSize.Medium;
        [Range(22, 52)] public int subtitleSmallFontSize = 28;
        [Range(22, 52)] public int subtitleMediumFontSize = 34;
        [Range(22, 52)] public int subtitleLargeFontSize = 42;

        [Header("Violence — the SHIPPED accessibility default")]
        [Tooltip("Extreme is the authored presentation. Players can disable blood and dismemberment immediately.")]
        public GoreLevel goreLevelDefault = GoreLevel.Extreme;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // A max below its min silently pins every slider to one value and
            // reads in the Inspector exactly like a working range.
            if (sensitivityMax < sensitivityMin) sensitivityMax = sensitivityMin;
            if (fovMax < fovMin) fovMax = fovMin;
            if (volumeMax < volumeMin) volumeMax = volumeMin;
            if (subtitleMediumFontSize < subtitleSmallFontSize)
                subtitleMediumFontSize = subtitleSmallFontSize;
            if (subtitleLargeFontSize < subtitleMediumFontSize)
                subtitleLargeFontSize = subtitleMediumFontSize;
        }
#endif
    }
}
