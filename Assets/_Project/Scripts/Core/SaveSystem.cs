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
        /// </summary>
        public const int CurrentSchemaVersion = 2;

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

            return Migrate(data);
        }

        public static void Save(SaveData data)
        {
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

            data.schemaVersion = CurrentSchemaVersion;
            return data;
        }
    }
}
