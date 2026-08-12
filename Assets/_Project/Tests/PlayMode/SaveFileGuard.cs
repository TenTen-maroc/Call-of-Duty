#nullable enable
using System.IO;
using CoD.Core;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// Backs up the real save file for the duration of a test and puts it back
    /// afterwards.
    ///
    /// WHY THIS EXISTS
    /// A PlayMode test that loads the grey box is running the REAL game against
    /// the REAL save path. Anything that ends a run — the player dying, a quit
    /// handler — writes to the player's record. The EditMode suite was careful
    /// about this from the start; the PlayMode suite was not, and it showed up as
    /// a human's totalRuns climbing from 2 to 5 between two play sessions in
    /// which they had only played twice. The counter was recording test runs.
    ///
    /// Nothing here is a test. It is a plain class every PlayMode fixture that can
    /// touch the save composes, so there is one implementation of "do not eat the
    /// player's record" rather than one per fixture.
    /// </summary>
    internal sealed class SaveFileGuard
    {
        private const string SaveName = "cod_save.json";
        private const string BackupName = "cod_save.bak.json";

        private string _savePath = string.Empty;
        private string _backupPath = string.Empty;
        private string? _originalSave;
        private string? _originalBackup;

        /// <summary>Call from [UnitySetUp], before the scene loads.</summary>
        public void Capture()
        {
            _savePath = Path.Combine(Application.persistentDataPath, SaveName);
            _backupPath = Path.Combine(Application.persistentDataPath, BackupName);
            _originalSave = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            _originalBackup = File.Exists(_backupPath) ? File.ReadAllText(_backupPath) : null;
        }

        /// <summary>
        /// Capture, then put a KNOWN save in place — the same one on every machine
        /// and in every run order.
        ///
        /// Capture alone was not enough, and the difference is a real failure this
        /// project hit: whoever last played left `lastMode: Sandbox` in the real
        /// save file, and RecordRunEnded deliberately writes nothing in Sandbox.
        /// So a test that killed the player got no record written and asserted
        /// against a bestRound that came from the developer's own play session —
        /// passing on one machine, failing on the next, for reasons nothing in the
        /// test mentioned. A test that reads the tester's save file is not testing
        /// the game.
        /// </summary>
        public void CaptureAndReset()
        {
            Capture();

            // A NEW SaveData FIELD MEANS A NEW LINE BELOW. This literal is
            // hand-listed rather than `new SaveData()` precisely so that adding a
            // field is a decision someone makes here, and the cost of forgetting
            // is the quietest failure in the suite: the field defaults to zero, a
            // test asserts on it, and it passes because the code under test never
            // had to write anything. Both bugs in the header above were that
            // shape — a value nobody chose, read as if someone had.
            File.WriteAllText(_savePath, JsonUtility.ToJson(new SaveData
            {
                schemaVersion = SaveSystem.CurrentSchemaVersion,
                bestRound = 0,
                totalKills = 0,
                totalRuns = 0,
                lastMode = GameMode.Run,
                // Already chosen, so SettingsHub does not re-seed from the configs
                // and the values under test are the ones written here.
                settingsInitialised = true,
                mouseSensitivity = 0.12f,
                fovVertical = 62f,
                masterVolume = 1f,
                invertLook = false,

                // The campaign block, schema 4. Endless, no mission, no history —
                // the configuration every PlayMode fixture in this project assumes
                // when it loads the arena and expects the wave loop to just run.
                campaignSelected = false,
                selectedMissionId = string.Empty,
                missionRecords = System.Array.Empty<MissionRecord>(),
            }, prettyPrint: true));

            if (File.Exists(_backupPath)) File.Delete(_backupPath);
        }

        /// <summary>
        /// Call from [UnityTearDown]. Restores byte-for-byte, and DELETES the file
        /// if there was none — a test must not leave a save behind on a machine
        /// that had never run the game.
        /// </summary>
        public void Restore()
        {
            if (_savePath.Length == 0) return;

            if (_originalSave != null) File.WriteAllText(_savePath, _originalSave);
            else if (File.Exists(_savePath)) File.Delete(_savePath);

            if (_originalBackup != null) File.WriteAllText(_backupPath, _originalBackup);
            else if (File.Exists(_backupPath)) File.Delete(_backupPath);
        }
    }
}
