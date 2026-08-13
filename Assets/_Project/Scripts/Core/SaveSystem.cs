#nullable enable
using System.IO;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Versioned JSON, written the only safe way: to a temp file, then moved into
    /// place, keeping one .bak. A crash or a power cut halfway through a direct
    /// write leaves an unparseable file, and the player's record is gone — the
    /// failure is silent, permanent, and only ever hits people who already had
    /// something worth losing.
    ///
    /// A static class with only methods and consts: allowed under the no-mutable-
    /// statics rule, and there is deliberately no cached SaveData instance here.
    /// Domain Reload is off, so a static cache would carry the previous Play
    /// session's record into this one.
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>
        /// 1 → 2 added the settings block (fov, invert, the initialised flag) and
        /// the remembered mode. Bumped rather than added silently, because the
        /// migration below has to decide what a v1 file's settings meant.
        ///
        /// 2 → 3 added the graphics block (post-processing, anti-aliasing). Bumped
        /// even though the migration is a no-op, because the version is what tells
        /// a downgraded build that this file holds fields it does not understand.
        ///
        /// 3 → 4 added the campaign block (campaignSelected, selectedMissionId,
        /// missionRecords). Campaign is a SECOND AXIS rather than a third GameMode
        /// for exactly the reason this version number exists: the enum is written
        /// as a raw int and C# enums are not range-checked, so a shipped build
        /// reading lastMode: 2 would answer "not Sandbox", treat a campaign
        /// mission as a Run, and write its wave number into bestRound. As a bool
        /// it ignores three unknown fields and starts an endless run instead. See
        /// SaveData for the long version.
        ///
        /// 4 → 5 added the accessibility block (subtitles enabled and subtitle
        /// size). The migration is deliberately empty so SettingsHub can seed the
        /// player-facing defaults from SettingsConfig rather than freezing tuning
        /// values into migration code.
        /// </summary>
        public const int CurrentSchemaVersion = 6;

        private const string FileName = "cod_save.json";
        private const string BackupName = "cod_save.bak.json";
        private const string TempName = "cod_save.tmp.json";

        private static string Directory => Application.persistentDataPath;
        private static string SavePath => Path.Combine(Directory, FileName);
        private static string BackupPath => Path.Combine(Directory, BackupName);
        private static string TempPath => Path.Combine(Directory, TempName);

        /// <summary>Never returns null: a missing, corrupt or future save all resolve to something playable.</summary>
        public static SaveData Load()
        {
            SaveData? data = TryRead(SavePath);
            if (data == null)
            {
                // The .bak exists precisely for this moment.
                data = TryRead(BackupPath);
                if (data != null) GameLog.Warn("Save was unreadable; recovered from the backup.");
            }
            if (data == null) return new SaveData();

            return Normalise(Migrate(data));
        }

        /// <summary>
        /// Makes the reference fields honest. #nullable enable checks THIS code,
        /// not the deserialiser that fills it: JsonUtility assigns the fields the
        /// JSON actually names and leaves the rest at whatever the constructed
        /// object had, so a file written before schema 4 can hand back a null
        /// array and a null string through fields declared non-null. The first
        /// thing to iterate missionRecords would then throw, on load, on the
        /// machine of the one player who had a save from before the campaign.
        ///
        /// This lives outside Migrate on purpose. A save from the FUTURE returns
        /// from Migrate before any migration step runs, and it can be exactly as
        /// null — "must not be null" is a property of the field, not of a version.
        ///
        /// An empty array is not a default value, it is the identity: no missions
        /// played. There is no tuning number anywhere in here.
        /// </summary>
        private static SaveData Normalise(SaveData data)
        {
            if (data.missionRecords is null) data.missionRecords = System.Array.Empty<MissionRecord>();
            if (data.selectedMissionId is null) data.selectedMissionId = string.Empty;

            // The entries too. An array can survive the parse with a null slot or
            // a record whose JSON object simply omitted missionId, and a save file
            // is the one input in this game that a player can open in Notepad.
            for (int i = 0; i < data.missionRecords.Length; i++)
            {
                MissionRecord? record = data.missionRecords[i];
                if (record is null)
                {
                    record = new MissionRecord();
                    data.missionRecords[i] = record;
                }
                if (record.missionId is null) record.missionId = string.Empty;
            }

            return data;
        }

        public static void Save(SaveData data)
        {
            // Migrate refuses to force-fit a save from the FUTURE into this
            // build's struct, and writing would undo that: JsonUtility serialises
            // only the fields THIS build knows about, so a v3 file overwritten by
            // a v2 build loses every v3 field AND gets relabelled v2 — the loss is
            // then invisible to the newer build that comes back to read it. A
            // downgraded build declining to write is the whole point of the
            // version check; see Migrate.
            if (data.schemaVersion > CurrentSchemaVersion)
            {
                // Error, not Warn. GameLog.Warn is [Conditional] on the editor and
                // development builds, so in the shipped exe the call site is
                // DELETED — and this branch makes every save a no-op. A player on
                // a downgraded build would lose every record with no signal at all.
                GameLog.Error(
                    $"Refusing to write over a v{data.schemaVersion} save with this build's v{CurrentSchemaVersion} " +
                    "shape — the newer fields would be silently dropped. Nothing was saved.");
                return;
            }

            data.schemaVersion = CurrentSchemaVersion;
            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                File.WriteAllText(TempPath, json);

                if (File.Exists(SavePath))
                {
                    // Replace keeps the previous file as the backup in one atomic
                    // operation. On a fresh install there is nothing to replace,
                    // so the move is the whole write.
                    File.Replace(TempPath, SavePath, BackupPath);
                }
                else
                {
                    File.Move(TempPath, SavePath);
                }
            }
            catch (IOException exception)
            {
                GameLog.Error("Could not write the save file: " + exception.Message);
            }
            catch (System.UnauthorizedAccessException exception)
            {
                GameLog.Error("Save file is not writable: " + exception.Message);
            }
        }

        private static SaveData? TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (System.Exception exception)
            {
                GameLog.Warn($"Save at '{path}' could not be parsed: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Handles a file written by a different version of the game. A save from
        /// the FUTURE is left alone rather than force-fitted into the current
        /// struct — refusing to guess is what keeps a downgrade from destroying a
        /// record it merely failed to understand.
        /// </summary>
        private static SaveData Migrate(SaveData data)
        {
            if (data.schemaVersion == CurrentSchemaVersion) return data;

            if (data.schemaVersion > CurrentSchemaVersion)
            {
                GameLog.Warn(
                    $"Save schema v{data.schemaVersion} is newer than this build's v{CurrentSchemaVersion}. " +
                    "Leaving it as-is; records may be incomplete rather than wrong.");
                return data;
            }

            // Older-than-current path. Each version adds one step and falls
            // through the rest, which is why the version is read first.
            if (data.schemaVersion < 2)
            {
                // v1 carried mouseSensitivity and masterVolume that NOTHING ever
                // read — they were written once at default and never applied. So
                // there is no player choice to preserve here: clearing the flag
                // makes SettingsService re-seed the whole block from the configs,
                // which is the only source of a correct default.
                data.settingsInitialised = false;
                data.lastMode = GameMode.Run;
            }

            if (data.schemaVersion < 3)
            {
                // Deliberately nothing. The graphics block defaults to
                // graphicsInitialised = false, which is precisely what makes
                // SettingsHub seed it from SettingsConfig on the next resolve —
                // the same path a brand-new save takes. Writing real values here
                // instead would put a tuning number in a migration, and tuning
                // numbers live in ScriptableObjects.
            }

            if (data.schemaVersion < 4)
            {
                // Deliberately nothing, and for a DIFFERENT reason than the < 3
                // branch above. That one is empty because a real default is a
                // tuning number that has to come from a ScriptableObject. This one
                // is empty because the campaign block has no real default at all:
                // false means the menu has never been pointed at the campaign,
                // empty means no mission chosen, and no records means no mission
                // played. A save from before the campaign IS an endless save, and
                // the zero values already say so — which is also why there is no
                // campaignInitialised flag to clear here.
                //
                // The one thing a v3 file can genuinely hand over is a NULL array
                // where this build declares a non-null one, and that is fixed in
                // Normalise rather than here. A save from the future returns above
                // without ever reaching a migration step and can be just as null,
                // so the guard belongs on the load path, not on this version's.
            }

            if (data.schemaVersion < 5)
            {
                // Deliberately empty. accessibilityInitialised=false makes
                // SettingsHub seed the player-facing defaults from SettingsConfig.
            }

            if (data.schemaVersion < 6)
            {
                // Deliberately empty. violenceInitialised=false makes
                // SettingsHub seed the authored default from SettingsConfig.
            }

            data.schemaVersion = CurrentSchemaVersion;
            return data;
        }
    }
}
