#nullable enable
using System.IO;
using CoD.Core;
using CoD.Enemies;
using CoD.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The pure logic, tested without a scene: stat folding, the save round trip,
    /// the follow-up bounds that stop effect modules recursing forever, damage
    /// falloff, and wave scaling.
    ///
    /// These exist because "it compiles" was previously the only automated claim
    /// this project could make about its maths. Every number checked here is one
    /// the game silently gets wrong rather than crashing on.
    /// </summary>
    public sealed class StatSheetTests
    {
        [Test]
        public void Effective_AppliesFlatBeforeMultiplier()
        {
            var sheet = new StatSheet();
            sheet.AddFlat(Stat.MaxHealth, 25f);
            sheet.AddMultiplier(Stat.MaxHealth, 2f);

            // (100 + 25) * 2, not 100 + (25 * 2). The order is the documented
            // pipeline and the whole reason passives are predictable.
            Assert.AreEqual(250f, sheet.Effective(Stat.MaxHealth, 100f), 0.001f);
        }

        [Test]
        public void Multipliers_Stack_Multiplicatively()
        {
            var sheet = new StatSheet();
            sheet.AddMultiplier(Stat.DamageMult, 1.15f);
            sheet.AddMultiplier(Stat.DamageMult, 1.15f);

            Assert.AreEqual(1.3225f, sheet.Effective(Stat.DamageMult, 1f), 0.0001f);
        }

        [Test]
        public void Effective_NeverGoesNegative()
        {
            var sheet = new StatSheet();
            sheet.AddFlat(Stat.MoveSpeed, -999f);

            // A stacking negative that flips a speed or a damage number negative
            // is a bug factory; the clamp is deliberate.
            Assert.AreEqual(0f, sheet.Effective(Stat.MoveSpeed, 5f), 0.001f);
        }

        [Test]
        public void Clear_ResetsToIdentity()
        {
            var sheet = new StatSheet();
            sheet.AddFlat(Stat.MaxHealth, 50f);
            sheet.AddMultiplier(Stat.MaxHealth, 3f);
            sheet.Clear();

            Assert.AreEqual(100f, sheet.Effective(Stat.MaxHealth, 100f), 0.001f);
        }
    }

    public sealed class RunStateTests
    {
        private static PassiveConfig MakePassive(Stat stat, StatModifierKind kind, float value)
        {
            PassiveConfig passive = ScriptableObject.CreateInstance<PassiveConfig>();
            passive.modifiers = new[] { new PassiveConfig.Modifier { stat = stat, kind = kind, value = value } };
            return passive;
        }

        [Test]
        public void AddPassive_RebuildsTheSheet()
        {
            var state = new RunState();
            state.BeginRun(300);
            state.AddPassive(MakePassive(Stat.MaxHealth, StatModifierKind.FlatAdd, 25f));
            state.AddPassive(MakePassive(Stat.MaxHealth, StatModifierKind.FlatAdd, 25f));

            Assert.AreEqual(150f, state.Stats.Effective(Stat.MaxHealth, 100f), 0.001f);
        }

        [Test]
        public void BeginRun_ClearsPreviousPassives()
        {
            var state = new RunState();
            state.BeginRun(300);
            state.AddPassive(MakePassive(Stat.DamageMult, StatModifierKind.Multiplier, 2f));
            state.BeginRun(300);

            // Permadeath means a new run starts from nothing. A leaked passive
            // here would be a permanent buff nobody could find.
            Assert.AreEqual(1f, state.Stats.Effective(Stat.DamageMult, 1f), 0.001f);
            Assert.AreEqual(0, state.Owned.Count);
        }

        [Test]
        public void AddMoney_AppliesTheMoneyGainMultiplier()
        {
            var state = new RunState();
            state.BeginRun(0);
            state.AddPassive(MakePassive(Stat.MoneyGainMult, StatModifierKind.Multiplier, 1.25f));
            state.AddMoney(100);

            Assert.AreEqual(125, state.Money);
        }

        [Test]
        public void TrySpend_RefusesWhenShort_AndLeavesMoneyAlone()
        {
            var state = new RunState();
            state.BeginRun(100);

            Assert.IsFalse(state.TrySpend(150));
            Assert.AreEqual(100, state.Money);
            Assert.IsTrue(state.TrySpend(100));
            Assert.AreEqual(0, state.Money);
        }

        [Test]
        public void RoundReached_TracksTheHighWaterMark()
        {
            var state = new RunState();
            state.BeginRun(0);
            state.SetWave(5);
            state.SetWave(3);

            Assert.AreEqual(5, state.RoundReached);
        }
    }

    /// <summary>
    /// The save file is the only thing that survives a death, so its failure
    /// modes are the ones that matter: a half-written file, a corrupt file, and a
    /// file from a future build.
    /// </summary>
    public sealed class SaveSystemTests
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
        public void Save_ThenLoad_RoundTrips()
        {
            var data = new SaveData { bestRound = 12, totalKills = 340, totalRuns = 7 };
            SaveSystem.Save(data);

            SaveData loaded = SaveSystem.Load();
            Assert.AreEqual(12, loaded.bestRound);
            Assert.AreEqual(340, loaded.totalKills);
            Assert.AreEqual(SaveSystem.CurrentSchemaVersion, loaded.schemaVersion);
        }

        [Test]
        public void SecondSave_LeavesABackup()
        {
            SaveSystem.Save(new SaveData { bestRound = 1 });
            SaveSystem.Save(new SaveData { bestRound = 2 });

            Assert.IsTrue(File.Exists(_backupPath), "the .bak is the whole recovery story");
        }

        [Test]
        public void CorruptSave_RecoversFromTheBackup()
        {
            SaveSystem.Save(new SaveData { bestRound = 9 });
            SaveSystem.Save(new SaveData { bestRound = 11 });
            File.WriteAllText(_savePath, "{ this is not json");

            // The backup holds the previous write, which is the point of keeping it.
            Assert.AreEqual(9, SaveSystem.Load().bestRound);
        }

        [Test]
        public void MissingSave_ReturnsPlayableDefaults()
        {
            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (File.Exists(_backupPath)) File.Delete(_backupPath);

            SaveData loaded = SaveSystem.Load();
            Assert.AreEqual(0, loaded.bestRound);
            Assert.AreEqual(SaveSystem.CurrentSchemaVersion, loaded.schemaVersion);
        }

        [Test]
        public void FutureSchema_IsLeftAlone_RatherThanGuessedAt()
        {
            File.WriteAllText(_savePath, "{\"schemaVersion\":99,\"bestRound\":42}");

            SaveData loaded = SaveSystem.Load();
            Assert.AreEqual(42, loaded.bestRound, "records must survive a downgrade, not be rewritten");
            Assert.AreEqual(99, loaded.schemaVersion);
        }
    }

    /// <summary>
    /// The bounds that stop an effect-module stack from freezing a frame. These
    /// are the tests standing between "Explosive plus Chain is absurd fun" and
    /// "Explosive plus Chain hangs Unity".
    /// </summary>
    public sealed class FollowUpBufferTests
    {
        [Test]
        public void Dequeues_InOrder()
        {
            var buffer = new FollowUpBuffer(4);
            buffer.Enqueue(new FollowUp { Damage = 1f });
            buffer.Enqueue(new FollowUp { Damage = 2f });

            Assert.IsTrue(buffer.TryDequeue(out FollowUp first));
            Assert.IsTrue(buffer.TryDequeue(out FollowUp second));
            Assert.AreEqual(1f, first.Damage, 0.001f);
            Assert.AreEqual(2f, second.Damage, 0.001f);
            Assert.IsFalse(buffer.TryDequeue(out _));
        }

        [Test]
        public void DropsSilentlyWhenFull()
        {
            var buffer = new FollowUpBuffer(2);
            buffer.Enqueue(new FollowUp { Damage = 1f });
            buffer.Enqueue(new FollowUp { Damage = 2f });
            buffer.Enqueue(new FollowUp { Damage = 3f });

            // A dropped chain jump is a missing spark; an unbounded queue is a
            // frozen frame. The capacity is the second half of the depth guard.
            Assert.AreEqual(2, buffer.Count);
            Assert.IsTrue(buffer.IsFull);
        }

        [Test]
        public void WrapsAroundAfterDequeue()
        {
            var buffer = new FollowUpBuffer(2);
            buffer.Enqueue(new FollowUp { Damage = 1f });
            buffer.TryDequeue(out _);
            buffer.Enqueue(new FollowUp { Damage = 2f });
            buffer.Enqueue(new FollowUp { Damage = 3f });

            Assert.AreEqual(2, buffer.Count);
        }
    }

    public sealed class EffectModuleDepthTests
    {
        [Test]
        public void DefaultModule_RunsOnlyAtDepthZero()
        {
            Pierce module = ScriptableObject.CreateInstance<Pierce>();
            module.maxDepth = 0;

            Assert.IsTrue(module.RunsAtDepth(0));
            // The rule that makes Explosive -> Chain -> Explosive terminate.
            Assert.IsFalse(module.RunsAtDepth(1));
        }

        [Test]
        public void OptedInModule_RunsExactlyAsDeepAsItSays()
        {
            Chain module = ScriptableObject.CreateInstance<Chain>();
            module.maxDepth = 1;

            Assert.IsTrue(module.RunsAtDepth(1));
            Assert.IsFalse(module.RunsAtDepth(2));
        }

        [Test]
        public void Pierce_ContributesRayBudgetAndFalloff_NotAnAfterEffect()
        {
            Pierce module = ScriptableObject.CreateInstance<Pierce>();
            module.maxTargets = 2;
            module.damageFalloffPerTarget = 0.75f;

            Assert.AreEqual(2, module.ExtraRayBudget);
            Assert.AreEqual(0.75f, module.PierceDamageFalloff, 0.001f);
        }
    }

    public sealed class WeaponConfigTests
    {
        private static WeaponConfig MakeRifle()
        {
            WeaponConfig config = ScriptableObject.CreateInstance<WeaponConfig>();
            config.roundsPerMinute = 700f;
            config.bodyDamage = 25f;
            config.falloffRange = new Vector2(25f, 60f);
            config.minDamageMultiplier = 0.6f;
            return config;
        }

        [Test]
        public void AR_KillsInFourShots_AtRoughly257Milliseconds()
        {
            WeaponConfig rifle = MakeRifle();

            // The number the entire game is tuned around. If this test fails,
            // movement speed, arena scale and spawn distance are all wrong too.
            Assert.AreEqual(4, rifle.ShotsToKill(100f));
            Assert.AreEqual(0.257f, rifle.TimeToKill(100f), 0.002f);
        }

        [Test]
        public void DamageFalloff_IsFlatInsideTheStart_AndFloorsAtTheEnd()
        {
            WeaponConfig rifle = MakeRifle();

            Assert.AreEqual(25f, rifle.DamageAtDistance(10f), 0.001f);
            Assert.AreEqual(25f, rifle.DamageAtDistance(25f), 0.001f);
            Assert.AreEqual(15f, rifle.DamageAtDistance(60f), 0.001f);
            Assert.AreEqual(15f, rifle.DamageAtDistance(500f), 0.001f);
        }

        [Test]
        public void DamageFalloff_IsLinearBetweenTheTwoDistances()
        {
            WeaponConfig rifle = MakeRifle();

            Assert.AreEqual(20f, rifle.DamageAtDistance(42.5f), 0.01f);
        }
    }

    public sealed class WaveScalingTests
    {
        [Test]
        public void NonPositiveMultipliers_FallBackToOne()
        {
            var scaling = new WaveScaling(0f, -3f);

            // A zero health multiplier would spawn drones that die to a sneeze,
            // and the bug would read as "enemies are broken".
            Assert.AreEqual(1f, scaling.HealthMultiplier, 0.001f);
            Assert.AreEqual(1f, scaling.SpeedMultiplier, 0.001f);
        }

        [Test]
        public void ScalingForWave_ReadsTheCurves()
        {
            DifficultyConfig difficulty = ScriptableObject.CreateInstance<DifficultyConfig>();
            difficulty.healthMultiplierByWave = AnimationCurve.Linear(10f, 1f, 20f, 3f);
            difficulty.speedMultiplierByWave = AnimationCurve.Linear(10f, 1f, 20f, 1.5f);

            WaveScaling scaling = difficulty.ScalingForWave(20);
            Assert.AreEqual(3f, scaling.HealthMultiplier, 0.01f);
            Assert.AreEqual(1.5f, scaling.SpeedMultiplier, 0.01f);
        }

        [Test]
        public void TheHardCaps_AreWhatTheDocsSay()
        {
            DifficultyConfig difficulty = ScriptableObject.CreateInstance<DifficultyConfig>();

            // Not tuning knobs: 40 protects a 4 GB GPU, 3 is why a crowd is fair.
            // If a default here changes, it was not an accident and this test is
            // the place to argue about it.
            Assert.AreEqual(40, difficulty.maxAliveDrones);
            Assert.AreEqual(3, difficulty.maxSimultaneousAttackers);
        }
    }
}
