#nullable enable
using System.Reflection;
using CoD.Core;
using CoD.Enemies;
using CoD.Waves;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoD.Tests
{
    public sealed class HumanizationTests
    {
        private const string MissionOnePath = "Assets/_Project/Data/Missions/Mission_01_Shakedown.asset";
        private const string ReactionPath = "Assets/_Project/Data/Drones/Reactions_Drone_Standard.asset";

        private static RadioLine Line(string id, RadioTrigger trigger, int priority = 50,
            float cooldown = 2f, int occurrence = 0,
            RadioInterruptionPolicy interruption = RadioInterruptionPolicy.AllowHigherPriority)
            => new()
            {
                stableId = id,
                speakerId = "operator_test",
                speakerName = "TEST OPERATOR",
                subtitle = "Test line.",
                trigger = trigger,
                priority = priority,
                cooldownSeconds = cooldown,
                subtitleSeconds = 1f,
                occurrence = occurrence,
                interruptionPolicy = interruption,
                audioClip = null,
            };

        private static RadioDialogueConfig Config(params RadioLine[] lines)
        {
            RadioDialogueConfig config = ScriptableObject.CreateInstance<RadioDialogueConfig>();
            config.lines = lines;
            return config;
        }

        [Test]
        public void NullAudio_IsACompleteValidSubtitleLine()
        {
            RadioDialogueConfig config = Config(Line("radio_null_audio", RadioTrigger.MissionEntry));

            Assert.AreEqual(RadioValidationIssue.None, config.Validate(out int invalidIndex));
            Assert.AreEqual(-1, invalidIndex);
            Assert.IsNull(config.lines[0].audioClip);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Validation_RejectsIncompleteAndAliasedLines()
        {
            RadioLine first = Line("same_id", RadioTrigger.MissionEntry);
            RadioLine second = Line("same_id", RadioTrigger.FirstObjective);
            second.speakerName = "";
            RadioDialogueConfig config = Config(first, second);

            RadioValidationIssue issues = config.Validate(out int invalidIndex);

            Assert.AreNotEqual(0, (int)(issues & RadioValidationIssue.DuplicateStableId));
            Assert.AreNotEqual(0, (int)(issues & RadioValidationIssue.MissingSpeaker));
            Assert.AreEqual(1, invalidIndex);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void HigherPriority_InterruptsOnlyWhenTheCurrentLineAllowsIt()
        {
            RadioDialogueConfig config = Config(
                Line("low", RadioTrigger.MissionEntry, 20),
                Line("high", RadioTrigger.PlayerBadlyHurt, 90));
            var arbiter = new RadioDialogueArbiter();
            arbiter.Configure(config);

            Assert.IsTrue(arbiter.Request(RadioTrigger.MissionEntry, 0f, out RadioLine? low, out bool firstInterrupted));
            Assert.AreEqual("low", low?.stableId);
            Assert.IsFalse(firstInterrupted);
            Assert.IsTrue(arbiter.Request(RadioTrigger.PlayerBadlyHurt, 0.1f,
                out RadioLine? high, out bool interrupted));
            Assert.AreEqual("high", high?.stableId);
            Assert.IsTrue(interrupted);
            Assert.AreEqual(0, arbiter.PendingCount);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void FinishPolicy_QueuesAHighPriorityLineInsteadOfMakingNoise()
        {
            RadioDialogueConfig config = Config(
                Line("locked", RadioTrigger.MissionEntry, 20, interruption: RadioInterruptionPolicy.Finish),
                Line("urgent", RadioTrigger.PlayerBadlyHurt, 90));
            var arbiter = new RadioDialogueArbiter();
            arbiter.Configure(config);

            arbiter.Request(RadioTrigger.MissionEntry, 0f, out _, out _);
            Assert.IsTrue(arbiter.Request(RadioTrigger.PlayerBadlyHurt, 0.1f, out RadioLine? started, out _));
            Assert.IsNull(started);
            Assert.AreEqual(1, arbiter.PendingCount);
            Assert.AreEqual("urgent", arbiter.CompleteCurrent(1f)?.stableId);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void CooldownAndDuplicateSuppression_BlockRepeatedSpam()
        {
            RadioDialogueConfig config = Config(Line("repeat", RadioTrigger.WaveClear, cooldown: 5f));
            var arbiter = new RadioDialogueArbiter();
            arbiter.Configure(config);

            Assert.IsTrue(arbiter.Request(RadioTrigger.WaveClear, 0f, out _, out _));
            Assert.IsFalse(arbiter.Request(RadioTrigger.WaveClear, 0.1f, out _, out _),
                "the same line cannot exist as current and pending dialogue");
            arbiter.CompleteCurrent(1f);
            Assert.IsFalse(arbiter.Request(RadioTrigger.WaveClear, 2f, out _, out _),
                "completion does not erase the authored cooldown");
            Assert.IsTrue(arbiter.Request(RadioTrigger.WaveClear, 6f, out _, out _));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void OccurrenceTargetsAContextWithoutHardcodingCopyInTheDirector()
        {
            RadioDialogueConfig config = Config(
                Line("first_clear", RadioTrigger.WaveClear, occurrence: 1),
                Line("second_clear", RadioTrigger.WaveClear, occurrence: 2));
            var arbiter = new RadioDialogueArbiter();
            arbiter.Configure(config);

            arbiter.Request(RadioTrigger.WaveClear, 0f, out RadioLine? first, out _);
            arbiter.CompleteCurrent(1f);
            arbiter.Request(RadioTrigger.WaveClear, 2f, out RadioLine? second, out _);

            Assert.AreEqual("first_clear", first?.stableId);
            Assert.AreEqual("second_clear", second?.stableId);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void EnemyReactionConfig_RequiresExactlyOneResponsePerEvent()
        {
            EnemyReactionConfig config = ScriptableObject.CreateInstance<EnemyReactionConfig>();
            config.responses = new EnemyReactionResponse[(int)EnemyReactionKind.LowHealth + 1];
            for (int i = 0; i < config.responses.Length; i++)
            {
                config.responses[i] = new EnemyReactionResponse
                {
                    kind = (EnemyReactionKind)i,
                    probability = 0.5f,
                    cooldownSeconds = 1f,
                    pulseSeconds = 0.1f,
                };
            }
            Assert.IsTrue(config.IsComplete);
            config.responses[^1].kind = EnemyReactionKind.DetectPlayer;
            Assert.IsFalse(config.IsComplete, "a duplicate event silently leaves another event with no response");
            Object.DestroyImmediate(config);
        }

        [Test]
        public void ShippedEnemyReactions_AreCooledDownAndDeliberatelyUnsynchronised()
        {
            EnemyReactionConfig? config = AssetDatabase.LoadAssetAtPath<EnemyReactionConfig>(ReactionPath);
            Assert.IsNotNull(config, "run GreyBoxBuilder before the suite");
            Assert.IsTrue(config!.IsComplete);
            bool hasProbabilityGate = false;
            for (int i = 0; i < config.responses.Length; i++)
            {
                EnemyReactionResponse response = config.responses[i];
                Assert.Greater(response.cooldownSeconds, 0f);
                Assert.Greater(response.probability, 0f);
                Assert.LessOrEqual(response.probability, 1f);
                hasProbabilityGate |= response.probability < 1f;
            }
            Assert.IsTrue(hasProbabilityGate,
                "at least one reaction must be probabilistic so a whole wave cannot answer in sync");
        }

        [Test]
        public void ReactionRuntime_ResetClearsEveryPooledSignal()
        {
            GameObject go = new("pooled-reaction-test");
            DroneController controller = go.AddComponent<DroneController>();
            EnemyReactionConfig config = ScriptableObject.CreateInstance<EnemyReactionConfig>();
            MethodInfo? reset = typeof(DroneController).GetMethod("ResetReactions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(reset);

            reset!.Invoke(controller, new object?[] { config });
            SetPrivate(controller, "_hadSight", true);
            SetPrivate(controller, "_lowHealthReacted", true);
            SetPrivate(controller, "_reactionPulse", 0.9f);
            SetPrivate(controller, "_lostSightAt", 12f);
            reset.Invoke(controller, new object?[] { config });

            Assert.IsFalse(GetPrivate<bool>(controller, "_hadSight"));
            Assert.IsFalse(GetPrivate<bool>(controller, "_lowHealthReacted"));
            Assert.AreEqual(0f, GetPrivate<float>(controller, "_reactionPulse"));
            Assert.AreEqual(0f, GetPrivate<float>(controller, "_lostSightAt"));
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MissionOne_OwnsRadioAndAHeadlessSafeQuietBeat()
        {
            MissionConfig? mission = AssetDatabase.LoadAssetAtPath<MissionConfig>(MissionOnePath);
            Assert.IsNotNull(mission, "run MissionBuilder before the suite");
            Assert.IsNotNull(mission!.radioDialogue);
            Assert.GreaterOrEqual(mission.radioDialogue!.lines.Length, 6);
            Assert.Greater(mission.steps[1].completionDelaySeconds, 0f,
                "the post-combat pause is mission data, not an AudioClip playback wait");
            for (int i = 0; i < mission.radioDialogue.lines.Length; i++)
                Assert.IsNull(mission.radioDialogue.lines[i].audioClip,
                    "the vertical slice honestly ships subtitle-only placeholders");
        }

        [Test]
        public void SubtitleAccessibility_RoundTripsIntoTheSaveRecord()
        {
            SettingsConfig bounds = ScriptableObject.CreateInstance<SettingsConfig>();
            var settings = new GameSettings(bounds, 0.12f, 62f, 1f, false, true,
                AntiAliasingMode.Smaa, true, SubtitleSize.Medium);
            settings.SetSubtitlesEnabled(false);
            settings.CycleSubtitleSize(1);
            var save = new SaveData();

            settings.WriteTo(save);

            Assert.IsTrue(save.accessibilityInitialised);
            Assert.IsFalse(save.subtitlesEnabled);
            Assert.AreEqual(SubtitleSize.Large, save.subtitleSize);
            Object.DestroyImmediate(bounds);
        }

        private static void SetPrivate<T>(object target, string field, T value)
        {
            FieldInfo? info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info!.SetValue(target, value);
        }

        private static T GetPrivate<T>(object target, string field)
        {
            FieldInfo? info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            return (T)info!.GetValue(target)!;
        }
    }
}
