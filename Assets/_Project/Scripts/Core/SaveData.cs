#nullable enable
using System;

namespace CoD.Core
{
    /// <summary>
    /// Everything that outlives a run. Notice what is NOT here: the run itself.
    /// Permadeath means money, wave and passives are never written to disk, which
    /// is why this file and its loader stay a page long instead of becoming a
    /// migration problem.
    ///
    /// [Serializable] and public fields on purpose — JsonUtility ignores
    /// properties and anything private without [SerializeField].
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>Bumped whenever the shape changes. Read before anything else; see SaveSystem.Migrate.</summary>
        public int schemaVersion = SaveSystem.CurrentSchemaVersion;

        public int bestRound;
        public int totalKills;
        public int totalRuns;
        public bool sandboxUnlocked = true;

        // Settings live here too: they are the other thing that must survive a
        // death, and a second file would be a second thing to keep versioned.
        public float mouseSensitivity = 0.12f;
        public float masterVolume = 1f;
    }
}
