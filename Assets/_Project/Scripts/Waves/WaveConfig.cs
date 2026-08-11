#nullable enable
using CoD.Enemies;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// One hand-authored wave. Waves 1 to 10 are assets so the opening of the game
    /// — the part every run replays and the only part most runs ever see — is
    /// designed rather than generated. Past the last asset, DifficultyConfig's
    /// curves take over.
    /// </summary>
    [CreateAssetMenu(fileName = "Wave_", menuName = "CoD/Wave Config", order = 50)]
    public sealed class WaveConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public DroneConfig? drone;
            [Min(1)] public int count;
            [Tooltip("The count drips in evenly across this many seconds. 0 = all at once.")]
            [Min(0f)] public float spawnOverSeconds;
            [Tooltip("Seconds to wait after the wave starts before this entry begins spawning.")]
            [Min(0f)] public float startDelay;
        }

        [Min(1)] public int waveNumber = 1;

        [Tooltip("Shown next to the wave number. A wave the player cannot name is a wave with no identity.")]
        public string displayName = "";

        /// <summary>
        /// Which iteration of the authored plan this asset was written from.
        ///
        /// GreyBoxBuilder rebuilds a wave in full when this does not match the
        /// plan's version, and otherwise re-links drone references only. Without
        /// it a redesign that happens to keep the same NUMBER of entries lands
        /// silently nowhere: the builder's rebuild test was array length alone, so
        /// changing 7 rushers to 14 looked applied and was not.
        /// </summary>
        public int designVersion;

        public Entry[] entries = System.Array.Empty<Entry>();

        [Tooltip("Roughly how long the wave should take. Informational — the wave ends when the last drone dies.")]
        [Min(5f)] public float durationTarget = 45f;
        [Tooltip("0 = use DifficultyConfig.maxAliveDrones.")]
        [Min(0)] public int maxAliveOverride = 0;
        [Min(0)] public int moneyBonusOnClear = 100;

        public int TotalCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < entries.Length; i++) total += Mathf.Max(0, entries[i].count);
                return total;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].count < 1) entries[i].count = 1;
            }
            // A wave far larger than the alive cap does not get harder, it gets
            // longer: the queue drips the surplus in one death at a time and the
            // player fights the same three drones for two minutes.
            if (TotalCount > 120)
            {
                Debug.LogWarning(
                    $"[{name}] spawns {TotalCount} drones. Well past the alive cap, so most of the wave will " +
                    "queue behind deaths rather than add pressure.", this);
            }
        }
#endif
    }
}
