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

        // The campaign block, added in schema 4. It is a SECOND AXIS, not a third
        // GameMode, and the difference is the whole reason this block exists in
        // this shape.
        //
        // GameMode has exactly two values and means RULES — permadeath and a
        // written record, or a cheat console and no record. It is serialised by
        // JsonUtility as a raw int, and C# enums are NOT range-checked: a
        // Campaign = 2 would reach an already-shipped build as lastMode: 2, and
        // every one of the six sites that branches on "is this Sandbox" would
        // answer no and treat a campaign mission as a Run. RecordRunEnded would
        // then write a mission's wave number into bestRound and permanently
        // pollute the permadeath record — from a build that can no longer be
        // patched. That is precisely the harm SaveSystem.Save's future-version
        // refusal exists to prevent, self-inflicted.
        //
        // A bool means CONTENT instead: missions, or the endless ramp. The same
        // old build reads lastMode: Run, ignores three fields it has never heard
        // of, and starts a normal endless run. Safe degradation on the side that
        // cannot be fixed.
        //
        // There is deliberately NO campaignInitialised flag, and the absence is a
        // decision rather than an oversight. That flag exists above wherever the
        // real default is a TUNING NUMBER — a sensitivity of 0 is not "the player
        // chose nothing", it is a dead mouse, so the loader has to tell the two
        // apart and re-seed from a ScriptableObject. Nothing here works that way:
        // false means "the menu has never been pointed at the campaign", empty
        // means "no mission chosen", and no records means "no mission played".
        // The zero value IS the correct answer, an old save IS an endless save,
        // and a flag would only be a second thing to keep in sync.
        public bool campaignSelected;

        /// <summary>The stableId of the mission the menu launches. Empty = none chosen.</summary>
        public string selectedMissionId = string.Empty;

        /// <summary>
        /// One entry per mission the player has a result for. Never null by the
        /// time anything reads it — SaveSystem.Load normalises it, because a file
        /// written before schema 4 has no such key and #nullable enable checks
        /// this code, not the deserialiser that fills it.
        /// </summary>
        public MissionRecord[] missionRecords = Array.Empty<MissionRecord>();
    }

    /// <summary>
    /// One mission's best result, keyed by the mission's stableId and never by
    /// index. A record found by position dies the first time a mission is
    /// inserted, renamed or cut, and the player's whole history silently shifts
    /// one mission to the left — the same reason drones, passives and shop
    /// entries all carry a stableId.
    ///
    /// A [Serializable] CLASS with public fields, because that is the only nested
    /// shape JsonUtility can see. It serialises neither interfaces nor properties
    /// nor Dictionary, so the obvious Dictionary&lt;string, MissionRecord&gt; would
    /// round trip as nothing at all and take every record with it on the first
    /// save — silently, which is the failure mode this file is built to avoid.
    /// </summary>
    [Serializable]
    public sealed class MissionRecord
    {
        /// <summary>MissionConfig.stableId. Never renamed once shipped — this is a save key.</summary>
        public string missionId = string.Empty;

        /// <summary>Has this mission ever been finished. The gate on every field below.</summary>
        public bool completed;

        /// <summary>Best rating earned. 0 means never rated, and sits below whatever the scale turns out to be.</summary>
        public int bestRating;

        /// <summary>
        /// Best clear time. Meaningless unless <see cref="completed"/> is true:
        /// for a time LOWER is better, so an unplayed mission's 0 would otherwise
        /// read as an unbeatable record.
        /// </summary>
        public float bestTimeSeconds;

        /// <summary>Deaths on this mission across every attempt. Counts up; a later clear does not reset it.</summary>
        public int deaths;
    }
}
