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

        /// <summary>Which mode the menu starts on. Carrying the choice through a scene load without a mutable static.</summary>
        public GameMode lastMode = GameMode.Run;

        // Settings live here too: they are the other thing that must survive a
        // death, and a second file would be a second thing to keep versioned.
        //
        // Every one of these defaults to zero rather than to a playable value.
        // A real default is a TUNING NUMBER, and tuning numbers live in a
        // ScriptableObject, never in a script — so an un-initialised save is
        // seeded from SettingsConfig and GameConfig by SettingsService, and the
        // flag below is how it knows the difference between "the player chose
        // silence" and "nobody has chosen anything yet".
        public bool settingsInitialised;
        public float mouseSensitivity;
        public float fovVertical;
        public float masterVolume;
        public bool invertLook;

        // The graphics block, added in schema 3. Same shape as the settings block
        // above and for the same reason: a real default is a TUNING NUMBER, so it
        // lives in SettingsConfig, and this flag is how the loader tells "the
        // player turned post-processing off" apart from "nobody has chosen yet".
        // That is also what lets a v2 save upgrade without a single literal in
        // SaveSystem.Migrate.
        public bool graphicsInitialised;
        public bool postProcessing;
        public AntiAliasingMode antiAliasing;
    }
}
