#nullable enable
using System.IO;
using CoD.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The settings layer. Every one of these covers a way the old code was
    /// broken: SaveData carried mouseSensitivity and masterVolume that NOTHING
    /// read, so the values round-tripped to disk perfectly and changed nothing.
    /// A test that only checked serialisation would have passed on that build.
    /// </summary>
    public sealed class GameSettingsTests
    {
        private SettingsConfig _bounds = null!;

        [SetUp]
        public void MakeBounds()
        {
            // A throwaway instance, never the real asset: a test that wrote to
            // Settings.asset would rewrite the shipped balance, which is the exact
            // failure mode this project bans at runtime.
            _bounds = ScriptableObject.CreateInstance<SettingsConfig>();
            _bounds.sensitivityMin = 0.02f;
            _bounds.sensitivityMax = 0.60f;
            _bounds.sensitivityStep = 0.01f;
            _bounds.fovMin = 50f;
            _bounds.fovMax = 85f;
            _bounds.fovStep = 1f;
            _bounds.volumeMin = 0f;
            _bounds.volumeMax = 1f;
            _bounds.volumeStep = 0.05f;
        }

        [TearDown]
        public void DropBounds() => Object.DestroyImmediate(_bounds);

        private GameSettings New(float sensitivity = 0.12f, float fov = 62f, float volume = 1f, bool invert = false)
            => new(_bounds, sensitivity, fov, volume, invert);

        [Test]
        public void Construction_ClampsEveryValueIntoRange()
        {
            GameSettings low = New(sensitivity: -5f, fov: 1f, volume: -1f);
            Assert.AreEqual(_bounds.sensitivityMin, low.MouseSensitivity, 1e-5f);
            Assert.AreEqual(_bounds.fovMin, low.FovVertical, 1e-5f);
            Assert.AreEqual(_bounds.volumeMin, low.MasterVolume, 1e-5f);

            GameSettings high = New(sensitivity: 99f, fov: 179f, volume: 12f);
            Assert.AreEqual(_bounds.sensitivityMax, high.MouseSensitivity, 1e-5f);
            Assert.AreEqual(_bounds.fovMax, high.FovVertical, 1e-5f);
            Assert.AreEqual(_bounds.volumeMax, high.MasterVolume, 1e-5f);
        }

        [Test]
        public void Stepping_MovesByOneStep_AndStopsAtTheEdge()
        {
            GameSettings settings = New(fov: 62f);
            settings.StepFovVertical(1);
            Assert.AreEqual(63f, settings.FovVertical, 1e-5f);
            settings.StepFovVertical(-1);
            Assert.AreEqual(62f, settings.FovVertical, 1e-5f);

            // Walk it past the ceiling. A slider that wraps around to the minimum
            // is the kind of thing nobody notices until a player reports that
            // their FOV randomly resets.
            for (int i = 0; i < 200; i++) settings.StepFovVertical(1);
            Assert.AreEqual(_bounds.fovMax, settings.FovVertical, 1e-5f);

            for (int i = 0; i < 400; i++) settings.StepFovVertical(-1);
            Assert.AreEqual(_bounds.fovMin, settings.FovVertical, 1e-5f);
        }

        [Test]
        public void Fractions_SpanZeroToOne_AcrossTheRange()
        {
            GameSettings min = New(sensitivity: -1f, fov: 0f, volume: -1f);
            Assert.AreEqual(0f, min.SensitivityFraction, 1e-5f);
            Assert.AreEqual(0f, min.FovFraction, 1e-5f);
            Assert.AreEqual(0f, min.VolumeFraction, 1e-5f);

            GameSettings max = New(sensitivity: 99f, fov: 999f, volume: 99f);
            Assert.AreEqual(1f, max.SensitivityFraction, 1e-5f);
            Assert.AreEqual(1f, max.FovFraction, 1e-5f);
            Assert.AreEqual(1f, max.VolumeFraction, 1e-5f);
        }

        [Test]
        public void DegenerateRange_DoesNotDivideByZero()
        {
            _bounds.fovMin = 70f;
            _bounds.fovMax = 70f;
            GameSettings settings = New(fov: 70f);
            Assert.AreEqual(0f, settings.FovFraction, 1e-5f);
            Assert.IsFalse(float.IsNaN(settings.FovFraction), "a pinned range must not produce NaN");
        }

        [Test]
        public void WriteTo_MarksTheSaveInitialised()
        {
            var save = new SaveData();
            Assert.IsFalse(save.settingsInitialised, "a fresh save has chosen nothing yet");

            GameSettings settings = New(sensitivity: 0.3f, fov: 70f, volume: 0.5f, invert: true);
            settings.WriteTo(save);

            Assert.IsTrue(save.settingsInitialised);
            Assert.AreEqual(0.3f, save.mouseSensitivity, 1e-5f);
            Assert.AreEqual(70f, save.fovVertical, 1e-5f);
            Assert.AreEqual(0.5f, save.masterVolume, 1e-5f);
            Assert.IsTrue(save.invertLook);
        }
    }

    /// <summary>
    /// Schema 2. A v1 file is what is actually sitting on this machine, so the
    /// migration is not hypothetical.
    /// </summary>
    public sealed class SettingsMigrationTests
    {
        private string _savePath = string.Empty;
        private string _backupPath = string.Empty;
        private string? _originalSave;
        private string? _originalBackup;

        [SetUp]
        public void BackUpTheRealSave()
        {
            _savePath = Path.Combine(Application.persistentDataPath, "cod_save.json");
            _backupPath = Path.Combine(Application.persistentDataPath, "cod_save.bak.json");
            // Never destroy a real player record to run a test.
            _originalSave = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            _originalBackup = File.Exists(_backupPath) ? File.ReadAllText(_backupPath) : null;
        }

        [TearDown]
        public void RestoreTheRealSave()
        {
            if (_originalSave != null) File.WriteAllText(_savePath, _originalSave);
            else if (File.Exists(_savePath)) File.Delete(_savePath);

            if (_originalBackup != null) File.WriteAllText(_backupPath, _originalBackup);
            else if (File.Exists(_backupPath)) File.Delete(_backupPath);
        }

        [Test]
        public void V1Save_KeepsItsRecord_AndReSeedsItsSettings()
        {
            // Exactly the shape v1 wrote. Raw JSON on purpose: constructing a
            // SaveData would use today's fields and prove nothing about a file
            // already sitting on a player's disk.
            File.WriteAllText(_savePath,
                "{\"schemaVersion\":1,\"bestRound\":7,\"totalKills\":140,\"totalRuns\":3," +
                "\"sandboxUnlocked\":true,\"mouseSensitivity\":0.12,\"masterVolume\":1.0}");

            SaveData loaded = SaveSystem.Load();

            Assert.AreEqual(SaveSystem.CurrentSchemaVersion, loaded.schemaVersion, "the file must be migrated");
            Assert.AreEqual(7, loaded.bestRound, "the record is the one thing a migration may never lose");
            Assert.AreEqual(140, loaded.totalKills);
            Assert.AreEqual(3, loaded.totalRuns);
            Assert.IsFalse(loaded.settingsInitialised,
                "a v1 settings block was never applied to anything, so it is not a player choice to preserve");
            Assert.AreEqual(GameMode.Run, loaded.lastMode);
        }

        [Test]
        public void SettingsBlock_SurvivesARoundTrip()
        {
            var data = new SaveData
            {
                bestRound = 4,
                settingsInitialised = true,
                mouseSensitivity = 0.27f,
                fovVertical = 78f,
                masterVolume = 0.4f,
                invertLook = true,
                lastMode = GameMode.Sandbox,
            };
            SaveSystem.Save(data);

            SaveData loaded = SaveSystem.Load();
            Assert.IsTrue(loaded.settingsInitialised);
            Assert.AreEqual(0.27f, loaded.mouseSensitivity, 1e-4f);
            Assert.AreEqual(78f, loaded.fovVertical, 1e-4f);
            Assert.AreEqual(0.4f, loaded.masterVolume, 1e-4f);
            Assert.IsTrue(loaded.invertLook);
            Assert.AreEqual(GameMode.Sandbox, loaded.lastMode, "the mode must survive the scene load that follows it");
        }
    }
}
