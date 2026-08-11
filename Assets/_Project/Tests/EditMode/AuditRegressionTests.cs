#nullable enable
using System.IO;
using CoD.Core;
using CoD.Weapons;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// One test per defect found in the code audit, each named for the thing that
    /// was actually wrong rather than for the method it happens to call.
    ///
    /// Every one of these is a SILENT failure — no crash, no console error, just a
    /// number quietly wrong or a rule quietly not applied. That is exactly the
    /// class of bug a compile gate cannot see, and the reason they are pinned here
    /// instead of being fixed and forgotten.
    /// </summary>
    public sealed class EconomyRegressionTests
    {
        private static PassiveConfig Greed(float multiplier)
        {
            PassiveConfig passive = ScriptableObject.CreateInstance<PassiveConfig>();
            passive.modifiers = new[]
            {
                new PassiveConfig.Modifier
                {
                    stat = Stat.MoneyGainMult, kind = StatModifierKind.Multiplier, value = multiplier,
                },
            };
            return passive;
        }

        [Test]
        public void Refund_GivesBackExactlyWhatWasSpent_NotWhatGreedWouldEarn()
        {
            var state = new RunState();
            state.BeginRun(1000);
            PassiveConfig greed = Greed(1.5f);
            state.AddPassive(greed);

            Assert.IsTrue(state.TrySpend(400), "the run should be able to afford the purchase");
            Assert.AreEqual(600, state.Money);

            state.Refund(400);

            // Through AddMoney this returned 600 — the MoneyGainMult passive
            // applies to EARNINGS, and a refund is not an earning. Any shop item
            // that could be refused after being charged was a money press.
            Assert.AreEqual(1000, state.Money,
                "a refund must return face value, never the Greed-multiplied amount");

            Object.DestroyImmediate(greed);
        }

        [Test]
        public void Refund_IgnoresZeroAndNegativeAmounts()
        {
            var state = new RunState();
            state.BeginRun(100);
            state.Refund(0);
            state.Refund(-50);
            Assert.AreEqual(100, state.Money);
        }

        [Test]
        public void AddMoney_StillApplies_TheGreedMultiplier()
        {
            var state = new RunState();
            state.BeginRun(0);
            PassiveConfig greed = Greed(2f);
            state.AddPassive(greed);

            state.AddMoney(50);

            // The other half of the pair: fixing the refund must not have
            // flattened the passive it was being confused with.
            Assert.AreEqual(100, state.Money);
            Object.DestroyImmediate(greed);
        }
    }

    public sealed class HealthRegressionTests
    {
        private GameObject _host = null!;
        private Health _health = null!;

        [SetUp]
        public void MakeHealth()
        {
            _host = new GameObject("TestHealth");
            _health = _host.AddComponent<Health>();
            _health.ConfigureMax(100f);
        }

        [TearDown]
        public void DropHealth() => Object.DestroyImmediate(_host);

        [Test]
        public void AdjustMax_RaisingTheCeiling_GrantsTheDifference_AndNothingMore()
        {
            _health.ApplyDamage(new DamageInfo(92f, Vector3.zero, Vector3.up, Vector3.forward, false));
            Assert.AreEqual(8f, _health.Current, 0.001f);

            _health.AdjustMax(125f);

            // The upgrade is worth its own +25 and not a free full heal. Through
            // ConfigureMax this read 125 — so buying ANY passive at 8 HP, even a
            // reload upgrade, topped the player up.
            Assert.AreEqual(33f, _health.Current, 0.001f);
            Assert.AreEqual(125f, _health.Max, 0.001f);
        }

        [Test]
        public void AdjustMax_WithNoChange_LeavesCurrentAlone()
        {
            _health.ApplyDamage(new DamageInfo(60f, Vector3.zero, Vector3.up, Vector3.forward, false));
            _health.AdjustMax(100f);

            // The common case: every purchase pushes the stat sheet, and only
            // health upgrades move this number at all.
            Assert.AreEqual(40f, _health.Current, 0.001f);
        }

        [Test]
        public void AdjustMax_LoweringTheCeiling_ClampsRatherThanLeavingCurrentAbove()
        {
            _health.AdjustMax(40f);
            Assert.AreEqual(40f, _health.Current, 0.001f);
            Assert.AreEqual(40f, _health.Max, 0.001f);
        }

        [Test]
        public void ConfigureMax_StillRefills_BecauseAPooledDroneNeedsItTo()
        {
            _health.ApplyDamage(new DamageInfo(90f, Vector3.zero, Vector3.up, Vector3.forward, false));
            _health.ConfigureMax(100f);

            // The drone spawner calls this on every reuse. A pooled instance that
            // kept the last drone's damage would die to one bullet.
            Assert.AreEqual(100f, _health.Current, 0.001f);
        }

        [Test]
        public void NegativeDamage_DoesNotHeal()
        {
            _health.ApplyDamage(new DamageInfo(50f, Vector3.zero, Vector3.up, Vector3.forward, false));
            Assert.AreEqual(50f, _health.Current, 0.001f);

            _health.ApplyDamage(new DamageInfo(-40f, Vector3.zero, Vector3.up, Vector3.forward, false));

            // Clamped at zero. A falloff curve authored backwards or a stat that
            // slipped below zero would otherwise heal the target it hit, and a
            // drone that heals on every bullet is unkillable while the hitmarker
            // keeps confirming hits.
            Assert.AreEqual(50f, _health.Current, 0.001f);
        }

        [Test]
        public void Overkill_ReportsOnlyTheHealthThatWasThere()
        {
            float applied = _health.ApplyDamage(
                new DamageInfo(9999f, Vector3.zero, Vector3.up, Vector3.forward, false));

            Assert.AreEqual(100f, applied, 0.001f);
            Assert.IsFalse(_health.IsAlive);
        }
    }

    public sealed class WeaponCadenceRegressionTests
    {
        private WeaponConfig _config = null!;

        [SetUp]
        public void MakeConfig()
        {
            _config = ScriptableObject.CreateInstance<WeaponConfig>();
            _config.roundsPerMinute = 700f;
            _config.magazineSize = 30;
            _config.reserveAmmo = 180;
        }

        [TearDown]
        public void DropConfig() => Object.DestroyImmediate(_config);

        [Test]
        public void SustainedFire_HoldsTheAuthoredRate_WhateverTheFrameRate()
        {
            const float frame = 1f / 60f;
            const float seconds = 3f;

            var runtime = new WeaponRuntime(_config) { ReserveAmmo = 100000 };
            int shots = 0;
            for (float now = 0f; now < seconds; now += frame)
            {
                if (now < runtime.NextShotAllowedAt) continue;
                runtime.ConsumeShot(now);
                runtime.CurrentAmmo = _config.magazineSize;   // ammo is not what is under test
                shots++;
            }

            // 700 RPM is 35 rounds in three seconds. Scheduling from Time.time
            // rounded every shot up to a whole frame and produced 30 — a rifle
            // firing at 600 RPM on a 60 Hz monitor and at nearly its rated speed
            // on 144 Hz. Fire rate is half of the TTK the game is tuned around.
            float expected = seconds * _config.roundsPerMinute / 60f;
            Assert.That(shots, Is.EqualTo(Mathf.RoundToInt(expected)).Within(1),
                $"expected ~{expected:F0} shots at {_config.roundsPerMinute} RPM, fired {shots}");
        }

        [Test]
        public void TheSameRate_ComesOutOfAFasterFrameRate()
        {
            const float seconds = 3f;
            int Fire(float frame)
            {
                var runtime = new WeaponRuntime(_config) { ReserveAmmo = 100000 };
                int shots = 0;
                for (float now = 0f; now < seconds; now += frame)
                {
                    if (now < runtime.NextShotAllowedAt) continue;
                    runtime.ConsumeShot(now);
                    runtime.CurrentAmmo = _config.magazineSize;
                    shots++;
                }
                return shots;
            }

            // The point of the fix stated as the player would notice it: the gun
            // does not get stronger when the machine does.
            Assert.That(Fire(1f / 144f), Is.EqualTo(Fire(1f / 60f)).Within(1),
                "fire rate must not depend on the display the game happens to run on");
        }

        [Test]
        public void AfterAPause_TheNextShotStartsFromNow_RatherThanCatchingUp()
        {
            var runtime = new WeaponRuntime(_config);
            runtime.ConsumeShot(0f);

            // Ten seconds of not firing. Carrying the schedule forward blindly
            // would bank a hundred rounds' worth of credit and dump them in one
            // frame the moment the trigger came back down.
            runtime.ConsumeShot(10f);

            Assert.AreEqual(10f + _config.SecondsPerShot, runtime.NextShotAllowedAt, 0.0001f);
        }
    }

    public sealed class SaveDowngradeRegressionTests
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
        public void ASaveFromTheFuture_IsNotOverwrittenByThisBuild()
        {
            // A file this build cannot fully represent. JsonUtility only writes
            // the fields THIS version knows about, so writing would drop whatever
            // the newer build added and relabel the result as current — a loss the
            // newer build could never detect when it came back.
            var future = new SaveData
            {
                schemaVersion = SaveSystem.CurrentSchemaVersion + 1,
                bestRound = 42,
            };
            File.WriteAllText(_savePath, JsonUtility.ToJson(future, true));

            SaveData loaded = SaveSystem.Load();
            Assert.AreEqual(SaveSystem.CurrentSchemaVersion + 1, loaded.schemaVersion,
                "Migrate must leave a future save alone");

            loaded.bestRound = 1;

            // The refusal is logged at ERROR on purpose: GameLog.Warn is compiled
            // out of a shipping build, and a save path that silently does nothing
            // forever is exactly the failure this guard exists to prevent.
            LogAssert.Expect(LogType.Error, new Regex("Refusing to write over a v3 save"));
            SaveSystem.Save(loaded);

            string onDisk = File.ReadAllText(_savePath);
            SaveData reread = JsonUtility.FromJson<SaveData>(onDisk);
            Assert.AreEqual(SaveSystem.CurrentSchemaVersion + 1, reread.schemaVersion,
                "the version must not be stamped down to this build's");
            Assert.AreEqual(42, reread.bestRound,
                "a downgraded build must not overwrite a record it cannot fully read");
        }

        [Test]
        public void ACurrentSave_StillWritesNormally()
        {
            // The guard must refuse the future, not refuse everything.
            var data = new SaveData { bestRound = 7, totalRuns = 3 };
            SaveSystem.Save(data);

            SaveData reread = SaveSystem.Load();
            Assert.AreEqual(7, reread.bestRound);
            Assert.AreEqual(3, reread.totalRuns);
            Assert.AreEqual(SaveSystem.CurrentSchemaVersion, reread.schemaVersion);
        }
    }

    public sealed class SettingsStepRegressionTests
    {
        private SettingsConfig _bounds = null!;

        [SetUp]
        public void MakeBounds()
        {
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

        [Test]
        public void SteppingByZero_ChangesNothing()
        {
            var settings = new GameSettings(_bounds, 0.12f, 62f, 0.5f, false);

            settings.StepMouseSensitivity(0);
            settings.StepFovVertical(0);
            settings.StepMasterVolume(0);

            // Mathf.Sign(0) is +1 in Unity, so "no input this frame" — which is
            // exactly what the menu passes — used to nudge every slider upward.
            Assert.AreEqual(0.12f, settings.MouseSensitivity, 1e-4f);
            Assert.AreEqual(62f, settings.FovVertical, 1e-4f);
            Assert.AreEqual(0.5f, settings.MasterVolume, 1e-4f);
        }

        [Test]
        public void SteppingUpAndDown_StillMovesByExactlyOneStep()
        {
            var settings = new GameSettings(_bounds, 0.12f, 62f, 0.5f, false);

            settings.StepFovVertical(1);
            Assert.AreEqual(63f, settings.FovVertical, 1e-4f);
            settings.StepFovVertical(-1);
            Assert.AreEqual(62f, settings.FovVertical, 1e-4f);
        }
    }
}
