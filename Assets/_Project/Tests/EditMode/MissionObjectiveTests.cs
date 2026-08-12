#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CoD.Core;
using CoD.Enemies;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The mission objective family, driven with NO SCENE.
    ///
    /// That is the claim this file exists to prove, not just to use. Objectives
    /// never subscribe to anything and never touch the world (rules 2 and 3 in
    /// MissionObjective's header), so a hand-built MissionProgress plus
    /// CreateInstance is a complete environment: no arena, no navmesh, no runner,
    /// no frame. If a future objective quietly starts needing a scene, several of
    /// these stop compiling or start throwing, which is the alarm.
    ///
    /// The failures being defended against here are all silent ones. A missing
    /// baseline does not crash — it completes step three the instant it begins. A
    /// hold that keeps banking time in the shop break does not crash — it makes
    /// the mission trivial. A hold that ZEROES the moment the wave ends under a
    /// motionless player's feet does not crash either — it makes a 45-second
    /// objective impossible, and the player blames themselves. A NoAlarm that
    /// completes instead of failing does not crash — it makes stealth cosmetic.
    /// </summary>
    public sealed class MissionObjectiveTests
    {
        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void DestroyAssets()
        {
            foreach (UnityEngine.Object asset in _created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
            _created.Clear();
        }

        // ---------- fixture ----------

        private T Make<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = typeof(T).Name;
            _created.Add(asset);
            return asset;
        }

        private MissionObjective Make(Type type)
        {
            var asset = (MissionObjective)ScriptableObject.CreateInstance(type);
            asset.name = type.Name;
            _created.Add(asset);
            return asset;
        }

        /// <summary>A context with no runner at all — the shape the whole file is written to prove is enough.</summary>
        private static ObjectiveContext Context(MissionProgress progress) =>
            new(progress, null, Vector3.zero);

        private static ObjectiveContext Context(MissionProgress progress, Vector3 playerPosition) =>
            new(progress, null, playerPosition);

        /// <summary>One second of objective time, at a rate no real frame would produce — which is the point.</summary>
        private static void Tick(MissionObjective objective, in ObjectiveContext context, ref ObjectiveState state,
            float seconds, float now = 0f) =>
            objective.Tick(in context, ref state, now, seconds);

        // ---------- baselining ----------

        [Test]
        public void SurviveWaves_CountsWavesFromTheStep_NotFromTheMission()
        {
            var progress = new MissionProgress();
            for (int i = 0; i < 5; i++) progress.RecordWaveCleared();

            Obj_SurviveWaves objective = Make<Obj_SurviveWaves>();
            objective.waves = 3;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            Assert.AreEqual(5, state.Baseline, "the baseline is the whole mechanism — it is taken in Begin");

            for (int i = 0; i < 2; i++) progress.RecordWaveCleared();
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Active, state.Status,
                "two of three waves is not three — a missing baseline would have completed this at wave 3");
            Assert.AreEqual(2, state.Counter);

            progress.RecordWaveCleared();
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
            Assert.AreEqual(1f, state.Progress, 0.0001f, "a complete objective must not show a part-filled bar");
        }

        [Test]
        public void SurviveWaves_SurvivesACheckpointRewind_WithoutRunningBackwards()
        {
            var progress = new MissionProgress();
            for (int i = 0; i < 4; i++) progress.RecordWaveCleared();

            Obj_SurviveWaves objective = Make<Obj_SurviveWaves>();
            objective.waves = 2;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            // The director rewound to a checkpoint and cleared the record while
            // this state slot lived on. Counter must clamp, not go negative.
            progress.Reset();
            Tick(objective, in context, ref state, 1f);

            Assert.AreEqual(0, state.Counter);
            Assert.AreEqual(0f, state.Progress, 0.0001f);
        }

        [Test]
        public void DestroyTargets_AlsoBaselines()
        {
            var progress = new MissionProgress();
            progress.RecordTargetDestroyed();
            progress.RecordTargetDestroyed();

            Obj_DestroyTargets objective = Make<Obj_DestroyTargets>();
            objective.count = 2;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            progress.RecordTargetDestroyed();
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Active, state.Status);

            progress.RecordTargetDestroyed();
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        // ---------- kill quota ----------

        [Test]
        public void KillQuota_Unfiltered_CountsEveryArchetype()
        {
            DroneConfig rusher = Make<DroneConfig>();
            DroneConfig tank = Make<DroneConfig>();
            var progress = new MissionProgress();

            Obj_KillQuota objective = Make<Obj_KillQuota>();
            objective.quota = 3;
            objective.droneFilter = null;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            progress.RecordKill(rusher);
            progress.RecordKill(tank);
            progress.RecordKill(null);
            Tick(objective, in context, ref state, 1f);

            Assert.AreEqual(ObjectiveStatus.Complete, state.Status,
                "an unconfigured spawn is still a kill the player made");
        }

        [Test]
        public void KillQuota_Filtered_IgnoresEveryOtherArchetype()
        {
            DroneConfig rusher = Make<DroneConfig>();
            DroneConfig tank = Make<DroneConfig>();
            var progress = new MissionProgress();

            Obj_KillQuota objective = Make<Obj_KillQuota>();
            objective.quota = 2;
            objective.droneFilter = tank;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            for (int i = 0; i < 20; i++) progress.RecordKill(rusher);
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(0, state.Counter, "twenty rushers are not one tank");
            Assert.AreEqual(ObjectiveStatus.Active, state.Status);

            progress.RecordKill(tank);
            progress.RecordKill(tank);
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        [Test]
        public void KillQuota_Filtered_BaselinesPerType()
        {
            DroneConfig tank = Make<DroneConfig>();
            var progress = new MissionProgress();
            progress.RecordKill(tank);
            progress.RecordKill(tank);

            Obj_KillQuota objective = Make<Obj_KillQuota>();
            objective.quota = 1;
            objective.droneFilter = tank;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);
            Tick(objective, in context, ref state, 1f);

            Assert.AreEqual(ObjectiveStatus.Active, state.Status,
                "the two tanks killed before the step began must not satisfy it");
        }

        // ---------- hold zone ----------

        private const int ZONE = 4;

        private static MissionProgress ProgressWithZone(Vector3 center, float radius)
        {
            var progress = new MissionProgress();
            progress.RegisterZone(ZONE, center, radius);
            return progress;
        }

        private Obj_HoldZone MakeHold(float seconds, bool resetOnLeave)
        {
            Obj_HoldZone objective = Make<Obj_HoldZone>();
            objective.zoneId = ZONE;
            objective.holdSeconds = seconds;
            objective.resetOnLeave = resetOnLeave;
            // The phase gate needs a runner to be satisfied, and most of these
            // tests deliberately have none. It gets three tests of its own below.
            objective.requireWavePhase = false;
            return objective;
        }

        [Test]
        public void HoldZone_AccumulatesInside_AndCompletesAtTheThreshold()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: true);

            ObjectiveContext inside = Context(progress, new Vector3(1f, 0f, 1f));
            var state = default(ObjectiveState);
            objective.BeginStep(in inside, ref state, 0f, 0f);

            Tick(objective, in inside, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Active, state.Status);
            Assert.AreEqual(1f / 3f, state.Progress, 0.001f);

            Tick(objective, in inside, ref state, 2f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        [Test]
        public void HoldZone_ResetOnLeave_ZeroesTheClockTheFrameThePlayerSteppedOff()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: true);

            ObjectiveContext inside = Context(progress, Vector3.zero);
            ObjectiveContext outside = Context(progress, new Vector3(30f, 0f, 0f));
            var state = default(ObjectiveState);
            objective.BeginStep(in inside, ref state, 0f, 0f);

            Tick(objective, in inside, ref state, 2.5f);
            Tick(objective, in outside, ref state, 0.1f);
            Assert.AreEqual(0f, state.Accumulator, 0.0001f, "stepping off restarts the hold");

            Tick(objective, in inside, ref state, 2.5f);
            Assert.AreEqual(ObjectiveStatus.Active, state.Status,
                "and the 2.5 s banked before leaving must not still be there");
        }

        [Test]
        public void HoldZone_Cumulative_KeepsTheClockAcrossATripOutside()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: false);

            ObjectiveContext inside = Context(progress, Vector3.zero);
            ObjectiveContext outside = Context(progress, new Vector3(30f, 0f, 0f));
            var state = default(ObjectiveState);
            objective.BeginStep(in inside, ref state, 0f, 0f);

            Tick(objective, in inside, ref state, 2f);
            Tick(objective, in outside, ref state, 5f);
            Assert.AreEqual(2f, state.Accumulator, 0.0001f, "cumulative means the clock pauses, not resets");

            Tick(objective, in inside, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        [Test]
        public void HoldZone_RequireWavePhase_BanksNothingWithNoWaveRunning()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: true);
            objective.requireWavePhase = true;

            // No runner, so the context reports Countdown — the shop break, the
            // briefing, and every other moment nothing is shooting at the player.
            ObjectiveContext inside = Context(progress, Vector3.zero);
            var state = default(ObjectiveState);
            objective.BeginStep(in inside, ref state, 0f, 0f);

            Tick(objective, in inside, ref state, 60f);
            Assert.AreEqual(0f, state.Accumulator, 0.0001f,
                "a hold that banks during the break is a hold the player buys with time they were not spending");
        }

        /// <summary>
        /// A real WaveRunner with its phase forced, on a throwaway GameObject that
        /// TearDown destroys alongside the ScriptableObjects. The phase gate can
        /// only be exercised through one of these — a context with no runner
        /// reports Countdown forever — so exactly three tests in this file pay for
        /// a component, and only those three.
        /// </summary>
        private WaveRunner MakeRunner(RunPhase phase)
        {
            var host = new GameObject("PhaseRunner");
            _created.Add(host);
            WaveRunner runner = host.AddComponent<WaveRunner>();
            SetPhase(runner, phase);
            return runner;
        }

        /// <summary>The runner owns the phase and nothing else may set it. Reflection is the only honest way in.</summary>
        private static void SetPhase(WaveRunner runner, RunPhase phase)
        {
            PropertyInfo? property = typeof(WaveRunner).GetProperty(nameof(WaveRunner.Phase));
            MethodInfo? setter = property?.GetSetMethod(nonPublic: true);
            Assert.IsNotNull(setter, "WaveRunner.Phase lost its setter");
            setter!.Invoke(runner, new object[] { phase });
        }

        [Test]
        public void HoldZone_RequireWavePhase_BanksTimeOnceAWaveIsRunning()
        {
            // The phase gate's TRUE branch can only be reached through a real
            // WaveRunner, and a gate only ever tested in its blocking direction is
            // a gate that could be permanently shut without anyone noticing.
            WaveRunner runner = MakeRunner(RunPhase.Wave);

            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: true);
            objective.requireWavePhase = true;

            var context = new ObjectiveContext(progress, runner, Vector3.zero);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            Tick(objective, in context, ref state, 3f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        [Test]
        public void HoldZone_RequireWavePhase_PausesTheHold_AndNeverSpendsProgressThePlayerEarned()
        {
            // THE REGRESSION THIS FILE WAS MISSING. The phase gate and the
            // leave-reset were once a single condition, so a player standing
            // perfectly still on the pad when the wave ended took the RESET branch
            // and lost the hold. With the shipped defaults — 45 s,
            // requireWavePhase, resetOnLeave — the whole hold then had to fit
            // inside one uninterrupted Wave phase, and a wave ends when the last
            // drone dies rather than on a clock the player controls. The
            // default-authored objective was plausibly impossible to complete, and
            // every test above this one happened to hold the phase still.
            WaveRunner runner = MakeRunner(RunPhase.Wave);

            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: true);
            objective.requireWavePhase = true;

            var onPad = new ObjectiveContext(progress, runner, Vector3.zero);
            var state = default(ObjectiveState);
            objective.BeginStep(in onPad, ref state, 0f, 0f);

            Tick(objective, in onPad, ref state, 2f);
            Assert.AreEqual(2f, state.Accumulator, 0.0001f, "two of the three seconds, fought for");

            // The wave ends under the player's feet. They have not moved a step.
            SetPhase(runner, RunPhase.Cleared);
            Tick(objective, in onPad, ref state, 1f);
            Assert.AreEqual(2f, state.Accumulator, 0.0001f,
                "the phase gate PAUSES — a player who never left must keep every second they banked");

            SetPhase(runner, RunPhase.Shop);
            Tick(objective, in onPad, ref state, 30f);
            Assert.AreEqual(2f, state.Accumulator, 0.0001f,
                "and a whole shop break moves it in neither direction");
            Assert.AreEqual(ObjectiveStatus.Active, state.Status);

            // Next wave. It resumes from two seconds, not from zero.
            SetPhase(runner, RunPhase.Wave);
            Tick(objective, in onPad, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status,
                "2 s banked plus 1 s is the 3 s hold — a gate that reset would demand the full 3 s again");
        }

        [Test]
        public void HoldZone_RequireWavePhase_StillResetsWhenThePlayerActuallyStepsOff()
        {
            // The complement, and the reason the two conditions cannot be collapsed
            // the other way either: pausing out of phase must not quietly turn
            // resetOnLeave into a no-op.
            WaveRunner runner = MakeRunner(RunPhase.Wave);

            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(3f, resetOnLeave: true);
            objective.requireWavePhase = true;

            var onPad = new ObjectiveContext(progress, runner, Vector3.zero);
            var away = new ObjectiveContext(progress, runner, new Vector3(30f, 0f, 0f));
            var state = default(ObjectiveState);
            objective.BeginStep(in onPad, ref state, 0f, 0f);

            Tick(objective, in onPad, ref state, 2.5f);
            Tick(objective, in away, ref state, 0.1f);
            Assert.AreEqual(0f, state.Accumulator, 0.0001f, "stepping off mid-wave still restarts the hold");

            // And out of phase as well: leaving is leaving, whatever the runner
            // happens to be doing at the time.
            Tick(objective, in onPad, ref state, 2.5f);
            SetPhase(runner, RunPhase.Shop);
            Tick(objective, in away, ref state, 0.1f);
            Assert.AreEqual(0f, state.Accumulator, 0.0001f,
                "stepping off during the break resets it too — the pause is not an amnesty");
            Assert.AreEqual(ObjectiveStatus.Active, state.Status);
        }

        // ---------- zones are floor-plane ----------

        [Test]
        public void ReachZone_IsAPadNotASphere()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 2.5f);
            Obj_ReachZone objective = Make<Obj_ReachZone>();
            objective.zoneId = ZONE;

            // Standing on a crate directly over the marker. A spherical test would
            // say the player is 4 m away and refuse; the player would swear they
            // were on it, and they would be right.
            ObjectiveContext onACrate = Context(progress, new Vector3(0.5f, 4f, 0.5f));
            var state = default(ObjectiveState);
            objective.BeginStep(in onACrate, ref state, 0f, 0f);
            Tick(objective, in onACrate, ref state, 0.1f);

            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        [Test]
        public void ReachZone_AnUnregisteredZoneIsNeverReached()
        {
            var progress = new MissionProgress();
            Obj_ReachZone objective = Make<Obj_ReachZone>();
            objective.zoneId = 99;

            ObjectiveContext context = Context(progress, Vector3.zero);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);
            Tick(objective, in context, ref state, 1f);

            Assert.AreEqual(ObjectiveStatus.Active, state.Status,
                "a zone this arena never registered must not read as 'the player is inside it'");
        }

        // ---------- extraction ----------

        [Test]
        public void Extract_ResetsTheDwellWhenThePlayerStepsOff()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 2f);
            Obj_Extract objective = Make<Obj_Extract>();
            objective.zoneId = ZONE;
            objective.dwellSeconds = 4f;

            ObjectiveContext onPad = Context(progress, Vector3.zero);
            ObjectiveContext away = Context(progress, new Vector3(20f, 0f, 0f));
            var state = default(ObjectiveState);
            objective.BeginStep(in onPad, ref state, 0f, 0f);

            Tick(objective, in onPad, ref state, 3.9f);
            Tick(objective, in away, ref state, 0.1f);
            Assert.AreEqual(0f, state.Accumulator, 0.0001f);

            Tick(objective, in onPad, ref state, 3.9f);
            Assert.AreEqual(ObjectiveStatus.Active, state.Status, "extraction always restarts");

            Tick(objective, in onPad, ref state, 0.2f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        [Test]
        public void Extract_ZeroDwell_StillRequiresStandingOnThePad()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 2f);
            Obj_Extract objective = Make<Obj_Extract>();
            objective.zoneId = ZONE;
            objective.dwellSeconds = 0f;

            ObjectiveContext away = Context(progress, new Vector3(20f, 0f, 0f));
            var state = default(ObjectiveState);
            objective.BeginStep(in away, ref state, 0f, 0f);
            Tick(objective, in away, ref state, 1f);

            Assert.AreEqual(ObjectiveStatus.Active, state.Status,
                "0 >= 0 must not end the mission from across the arena");

            ObjectiveContext onPad = Context(progress, Vector3.zero);
            Tick(objective, in onPad, ref state, 0.016f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
        }

        // ---------- the constraint ----------

        [Test]
        public void NoAlarm_Fails_RatherThanEverCompletingOnItsOwn()
        {
            var progress = new MissionProgress();
            Obj_NoAlarm objective = Make<Obj_NoAlarm>();

            Assert.IsTrue(objective.CompletesWithMission,
                "without this the director would wait forever on a rule the player was obeying");

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            for (int i = 0; i < 1000; i++) Tick(objective, in context, ref state, 0.1f);
            Assert.AreEqual(ObjectiveStatus.Active, state.Status, "it can never complete by itself");

            progress.RaiseAlarm();
            Tick(objective, in context, ref state, 0.1f);
            Assert.AreEqual(ObjectiveStatus.Failed, state.Status);
            Assert.IsTrue(objective.Critical, "a failed stealth constraint has to end the mission");
        }

        [Test]
        public void Alarm_CannotBeUnraised()
        {
            var progress = new MissionProgress();
            progress.RaiseAlarm();
            progress.RaiseAlarm();
            Assert.IsTrue(progress.AlarmRaised);

            // Only a full rewind clears it, and that restarts the mission anyway.
            progress.Reset();
            Assert.IsFalse(progress.AlarmRaised);
        }

        // ---------- interaction ----------

        [Test]
        public void Interact_CountsOnlyItsOwnKind()
        {
            var progress = new MissionProgress();
            Obj_Interact objective = Make<Obj_Interact>();
            objective.kind = InteractKind.Charge;
            objective.count = 2;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            progress.RecordInteraction(InteractKind.Terminal);
            progress.RecordInteraction(InteractKind.Intel);
            progress.RecordInteraction(InteractKind.Charge);
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(1, state.Counter);

            progress.RecordInteraction(InteractKind.Charge);
            Tick(objective, in context, ref state, 1f);
            Assert.AreEqual(ObjectiveStatus.Complete, state.Status);
            Assert.AreEqual(4, progress.Interactions, "the total counts every kind");
        }

        /// <summary>
        /// The counter array is sized FROM InteractKind rather than from a
        /// hand-kept constant, and this is what proves it stayed that way.
        ///
        /// Both halves are silent failures, which is why they need a test at all.
        /// A slot the array is too short for is DROPPED, so an objective counting
        /// that kind can never complete and nothing logs. And an out-of-range
        /// value now arrives from a mission ASSET — a serialized int the compiler
        /// never saw — so the range guard is the only thing between a
        /// mis-authored file and an IndexOutOfRangeException mid-mission.
        /// </summary>
        [Test]
        public void MissionProgress_HasASlotForEveryInteractKind_AndDropsValuesThatAreNotMembers()
        {
            var progress = new MissionProgress();
            var kinds = (InteractKind[])Enum.GetValues(typeof(InteractKind));
            Assert.Greater(kinds.Length, 0, "the enum cannot be empty");

            foreach (InteractKind kind in kinds) progress.RecordInteraction(kind);

            foreach (InteractKind kind in kinds)
            {
                Assert.AreEqual(1, progress.InteractionsOf(kind),
                    $"{kind} has no counter slot — the array is sized from the enum, so this means it drifted");
            }
            Assert.AreEqual(kinds.Length, progress.Interactions, "every kind counts towards the total");

            // Casting an out-of-range int to an enum is legal C#. A mis-authored
            // asset can therefore hand over a value that is not a member at all.
            var bogus = (InteractKind)9999;
            progress.RecordInteraction(bogus);
            Assert.AreEqual(kinds.Length + 1, progress.Interactions,
                "an unknown kind still counts as an interaction");
            Assert.AreEqual(0, progress.InteractionsOf(bogus), "but it owns no slot");
            Assert.AreEqual(0, progress.InteractionsOf((InteractKind)(-1)),
                "and a negative cast must never read backwards off the front of the array");
        }

        // ---------- the step's deadline ----------

        [Test]
        public void BeginStep_StampsTheStepsDeadline_AndZeroMeansUntimed()
        {
            var progress = new MissionProgress();
            Obj_ReachZone objective = Make<Obj_ReachZone>();
            ObjectiveContext context = Context(progress);

            var timed = default(ObjectiveState);
            objective.BeginStep(in context, ref timed, now: 100f, timeLimitSeconds: 30f);
            Assert.AreEqual(130f, timed.Deadline, 0.0001f);
            Assert.IsFalse(timed.IsPastDeadline(129.9f));
            Assert.IsTrue(timed.IsPastDeadline(130f));
            Assert.AreEqual(30f, timed.SecondsRemaining(100f), 0.0001f);

            var untimed = default(ObjectiveState);
            objective.BeginStep(in context, ref untimed, now: 100f, timeLimitSeconds: 0f);
            Assert.AreEqual(ObjectiveState.NO_DEADLINE, untimed.Deadline, 0.0001f);
            Assert.IsFalse(untimed.IsPastDeadline(999999f),
                "an untimed step must never expire — `now + 0` would have failed it on its first frame");
        }

        [Test]
        public void BeginStep_WipesWhateverWasInTheStateSlot()
        {
            MissionProgress progress = ProgressWithZone(Vector3.zero, 3f);
            Obj_HoldZone objective = MakeHold(5f, resetOnLeave: true);
            ObjectiveContext context = Context(progress, Vector3.zero);

            var reused = new ObjectiveState
            {
                Status = ObjectiveStatus.Complete,
                Accumulator = 99f,
                Counter = 42,
                Progress = 1f,
            };
            objective.BeginStep(in context, ref reused, 0f, 0f);

            Assert.AreEqual(ObjectiveStatus.Active, reused.Status);
            Assert.AreEqual(0f, reused.Accumulator, 0.0001f,
                "a leftover accumulator reads as progress nobody made");
            Assert.AreEqual(0f, reused.Progress, 0.0001f);
        }

        // ---------- describe ----------

        [Test]
        public void Describe_WritesIntoTheCallersBuilder_AndNeverReturnsAString()
        {
            MethodInfo? describe = typeof(MissionObjective).GetMethod(nameof(MissionObjective.Describe));
            Assert.IsNotNull(describe, "MissionObjective.Describe is gone");
            Assert.AreEqual(typeof(void), describe!.ReturnType,
                "a string-returning Describe allocates once per objective per frame, forever");

            var progress = new MissionProgress();
            Obj_SurviveWaves objective = Make<Obj_SurviveWaves>();
            objective.title = "SURVIVE";
            objective.waves = 3;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);
            progress.RecordWaveCleared();
            Tick(objective, in context, ref state, 1f);

            var builder = new StringBuilder(256);
            builder.Append("OBJ: ");
            int capacity = builder.Capacity;

            objective.Describe(builder, in state);
            Assert.AreEqual("OBJ: SURVIVE 1/3", builder.ToString(),
                "Describe appends; it never clears a builder it does not own");

            // The panel redraws every frame. Two hundred of them must not grow the
            // buffer, because a growth is a fresh char[] and a copy of the old one.
            for (int i = 0; i < 200; i++)
            {
                builder.Clear();
                objective.Describe(builder, in state);
            }
            Assert.AreEqual(capacity, builder.Capacity,
                "the caller's buffer was re-allocated, which means Describe is writing more than it should");
        }

        [Test]
        public void Describe_CountsAreClampedToTheGoal()
        {
            var progress = new MissionProgress();
            Obj_KillQuota objective = Make<Obj_KillQuota>();
            objective.title = "KILL";
            objective.quota = 2;

            ObjectiveContext context = Context(progress);
            var state = default(ObjectiveState);
            objective.BeginStep(in context, ref state, 0f, 0f);

            for (int i = 0; i < 9; i++) progress.RecordKill(null);
            Tick(objective, in context, ref state, 1f);

            var builder = new StringBuilder(64);
            objective.Describe(builder, in state);
            Assert.AreEqual("KILL 2/2", builder.ToString(), "9/2 is not a readable objective line");
        }

        // ---------- ObjectiveMath ----------

        [Test]
        public void WithinFloorRadius_IgnoresHeightEntirely()
        {
            Assert.IsTrue(ObjectiveMath.WithinFloorRadius(new Vector3(1f, 50f, 0f), Vector3.zero, 2f),
                "the player's origin is at their feet and a zone is a pad — height is not part of the test");
            Assert.IsFalse(ObjectiveMath.WithinFloorRadius(new Vector3(2.01f, 0f, 0f), Vector3.zero, 2f));
            Assert.IsTrue(ObjectiveMath.WithinFloorRadius(new Vector3(2f, 0f, 0f), Vector3.zero, 2f),
                "the boundary is inclusive, so a player standing exactly on the edge is in");
        }

        [Test]
        public void PickDifferent_NeverRepeats_AndNeverLoopsForever()
        {
            Assert.AreEqual(0, ObjectiveMath.PickDifferent(1, 0), "one lane means one answer, with no reroll loop");
            Assert.AreEqual(0, ObjectiveMath.PickDifferent(0, -1));

            bool sawZero = false;
            bool sawTwo = false;
            for (int i = 0; i < 300; i++)
            {
                int index = ObjectiveMath.PickDifferent(3, 1);
                Assert.AreNotEqual(1, index);
                Assert.IsTrue(index >= 0 && index < 3);
                sawZero |= index == 0;
                sawTwo |= index == 2;
            }
            Assert.IsTrue(sawZero && sawTwo, "the pick must stay uniform over what is left, not collapse to one lane");

            for (int i = 0; i < 100; i++)
            {
                int index = ObjectiveMath.PickDifferent(3, -1);
                Assert.IsTrue(index >= 0 && index < 3, "a negative 'previous' means nothing to avoid");
            }
        }

        [Test]
        public void Progress01_IsClampedAndNeverDividesByZero()
        {
            Assert.AreEqual(0.5f, ObjectiveMath.Progress01(1f, 2f), 0.0001f);
            Assert.AreEqual(1f, ObjectiveMath.Progress01(9f, 2f), 0.0001f);
            Assert.AreEqual(0f, ObjectiveMath.Progress01(-3f, 2f), 0.0001f);
            Assert.AreEqual(1f, ObjectiveMath.Progress01(0f, 0f), 0.0001f,
                "asking for nothing is already done — and must not produce NaN on a bar");
        }

        [Test]
        public void AppendInt_MatchesToString_IncludingTheOneValueThatCannotBeNegated()
        {
            var builder = new StringBuilder(64);
            foreach (int value in new[] { 0, 7, 10, 99, 100, 2147483647, -1, -256, int.MinValue })
            {
                builder.Clear();
                ObjectiveMath.AppendInt(builder, value);
                Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    builder.ToString());
            }
        }

        [Test]
        public void AppendSeconds_RoundsUp_SoATimerNeverLiesAboutZero()
        {
            var builder = new StringBuilder(16);
            ObjectiveMath.AppendSeconds(builder, 0.3f);
            Assert.AreEqual("1s", builder.ToString(), "0.3 s left is not 0 s left");

            builder.Clear();
            ObjectiveMath.AppendSeconds(builder, -5f);
            Assert.AreEqual("0s", builder.ToString());
        }

        // ---------- MissionProgress ----------

        [Test]
        public void MissionProgress_TracksPerTypeKillsWithoutLosingTheTotal()
        {
            DroneConfig a = Make<DroneConfig>();
            DroneConfig b = Make<DroneConfig>();
            var progress = new MissionProgress();

            progress.RecordKill(a);
            progress.RecordKill(a);
            progress.RecordKill(b);

            Assert.AreEqual(3, progress.Kills);
            Assert.AreEqual(2, progress.KillsOf(a));
            Assert.AreEqual(1, progress.KillsOf(b));
            Assert.AreEqual(3, progress.KillsOf(null), "null means 'any archetype'");
            Assert.AreEqual(0, progress.KillsOf(Make<DroneConfig>()), "a type nobody killed is zero, not the total");
        }

        [Test]
        public void MissionProgress_Reset_ClearsEverything()
        {
            DroneConfig drone = Make<DroneConfig>();
            var progress = new MissionProgress();
            progress.RecordWaveCleared();
            progress.RecordKill(drone);
            progress.RecordTargetDestroyed();
            progress.RecordInteraction(InteractKind.Door);
            progress.RaiseAlarm();
            progress.RegisterZone(1, Vector3.zero, 5f);

            progress.Reset();

            Assert.AreEqual(0, progress.WavesCleared);
            Assert.AreEqual(0, progress.Kills);
            Assert.AreEqual(0, progress.KillsOf(drone));
            Assert.AreEqual(0, progress.TargetsDestroyed);
            Assert.AreEqual(0, progress.Interactions);
            Assert.AreEqual(0, progress.InteractionsOf(InteractKind.Door));
            Assert.IsFalse(progress.AlarmRaised);
            Assert.IsFalse(progress.TryGetZone(1, out _, out _), "a rewind re-registers zones; it does not keep them");
        }

        [Test]
        public void MissionProgress_RegisteringAZoneTwice_MovesItRatherThanDuplicatingIt()
        {
            var progress = new MissionProgress();
            progress.RegisterZone(2, Vector3.zero, 1f);
            progress.RegisterZone(2, new Vector3(50f, 0f, 0f), 3f);

            Assert.IsTrue(progress.TryGetZone(2, out Vector3 center, out float radius));
            Assert.AreEqual(50f, center.x, 0.0001f);
            Assert.AreEqual(3f, radius, 0.0001f);
            Assert.IsFalse(progress.IsInsideZone(2, Vector3.zero), "the old position must not still answer yes");
        }

        // ---------- MissionConfig authoring ----------

        private MissionConfig MakeMission(params MissionConfig.Step[] steps)
        {
            MissionConfig mission = Make<MissionConfig>();
            mission.stableId = "mission_test";
            mission.displayName = "TEST";
            mission.steps = steps;
            mission.waves = Array.Empty<WaveConfig>();
            return mission;
        }

        private static void Validate(MissionConfig mission)
        {
            MethodInfo? validate = typeof(MissionConfig).GetMethod(
                "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(validate, "MissionConfig.OnValidate is gone — every authoring mistake is silent again");
            validate!.Invoke(mission, Array.Empty<object>());
        }

        [Test]
        public void OnValidate_RejectsAnEmptyStepList()
        {
            MissionConfig mission = MakeMission();
            LogAssert.Expect(LogType.Error, new Regex(".*has no steps.*"));
            Validate(mission);
        }

        [Test]
        public void OnValidate_RejectsAStepWithNoObjective()
        {
            MissionConfig mission = MakeMission(new MissionConfig.Step { objective = null });
            LogAssert.Expect(LogType.Error, new Regex(".*step 0 has no objective.*"));
            Validate(mission);
        }

        [Test]
        public void OnValidate_RejectsAWaveCountingStepInAMissionWithNoWaves()
        {
            MissionConfig mission = MakeMission(new MissionConfig.Step { objective = Make<Obj_SurviveWaves>() });
            LogAssert.Expect(LogType.Error, new Regex(".*counts waves but no waves to run.*"));
            Validate(mission);
        }

        [Test]
        public void OnValidate_RejectsAMissionWithNoStableId()
        {
            MissionConfig mission = MakeMission(new MissionConfig.Step { objective = Make<Obj_ReachZone>() });
            mission.stableId = "  ";
            LogAssert.Expect(LogType.Error, new Regex(".*has no stableId.*"));
            Validate(mission);
        }

        [Test]
        public void OnValidate_AcceptsAWellFormedMission_Silently()
        {
            MissionConfig mission = MakeMission(
                new MissionConfig.Step { objective = Make<Obj_ReachZone>() },
                new MissionConfig.Step { objective = Make<Obj_NoAlarm>(), parallel = true },
                new MissionConfig.Step { objective = Make<Obj_Extract>(), timeLimitSeconds = 90f });

            Validate(mission);
            Assert.AreEqual(3, mission.StepCount);
            Assert.AreEqual(0, mission.UsableWaveCount);
        }

        [Test]
        public void OnValidate_ClampsANegativeTimeLimit_ButNeverSilently()
        {
            MissionConfig mission = MakeMission(
                new MissionConfig.Step { objective = Make<Obj_ReachZone>(), timeLimitSeconds = -30f });

            // BOTH halves, because the clamp on its own is the bug. BeginStep
            // already reads any non-positive limit as untimed, so the clamp changes
            // no behaviour — it only stops the stored number disagreeing with the
            // played one. What the author who typed -30 actually needs is the log
            // line telling them the deadline they meant is never going to fire.
            LogAssert.Expect(LogType.Error, new Regex(".*negative time limit.*"));
            Validate(mission);

            Assert.AreEqual(0f, mission.steps[0].timeLimitSeconds, 0.0001f,
                "a negative limit reads as untimed, and a step whose deadline silently never fires " +
                "is a mission nobody can explain");
        }

        [Test]
        public void OnValidate_RejectsAConstraintAuthoredAsAStepTheMissionWaitsOn()
        {
            // Obj_NoAlarm is CompletesWithMission: by design it can only ever fail.
            // Authored as a NON-parallel step the director holds the list at it,
            // and the single thing that could advance it — the mission ending — is
            // downstream of the step it is stuck at. The mission plays perfectly
            // and then simply never finishes, which is the most expensive kind of
            // authoring mistake there is: it costs a whole playtest to find.
            MissionConfig mission = MakeMission(
                new MissionConfig.Step { objective = Make<Obj_ReachZone>() },
                new MissionConfig.Step { objective = Make<Obj_NoAlarm>() });

            LogAssert.Expect(LogType.Error, new Regex(".*step 1.*never completes on its own.*"));
            Validate(mission);
        }

        [Test]
        public void OnValidate_AcceptsTheSameConstraintOnceItIsMarkedParallel()
        {
            // The guard's OFF direction. A validator that fires on the correct
            // authoring too is a validator everyone learns to ignore.
            MissionConfig mission = MakeMission(
                new MissionConfig.Step { objective = Make<Obj_ReachZone>() },
                new MissionConfig.Step { objective = Make<Obj_NoAlarm>(), parallel = true });

            Validate(mission);
            Assert.AreEqual(2, mission.StepCount);
        }

        [Test]
        public void UsableWaveCount_IgnoresEmptyInspectorSlots()
        {
            MissionConfig mission = MakeMission(new MissionConfig.Step { objective = Make<Obj_ReachZone>() });
            // Slot 1 is left as it comes out of the inspector: empty.
            var waves = new WaveConfig[2];
            waves[0] = Make<WaveConfig>();
            mission.waves = waves;

            Assert.AreEqual(1, mission.UsableWaveCount, "an empty array slot is not a wave");
        }

        // ---------- the family, as a family ----------

        private static List<Type> ObjectiveTypes()
        {
            var types = new List<Type>();
            foreach (Type type in typeof(MissionObjective).Assembly.GetTypes())
            {
                if (!type.IsAbstract && typeof(MissionObjective).IsAssignableFrom(type)) types.Add(type);
            }
            return types;
        }

        [Test]
        public void EveryObjective_RunsWithNoSceneAndNoRunner()
        {
            List<Type> types = ObjectiveTypes();
            Assert.GreaterOrEqual(types.Count, 8, "an objective type went missing");

            var progress = new MissionProgress();
            progress.RegisterZone(0, Vector3.zero, 3f);
            ObjectiveContext context = Context(progress, Vector3.zero);
            var builder = new StringBuilder(128);

            foreach (Type type in types)
            {
                MissionObjective objective = Make(type);
                var state = default(ObjectiveState);

                objective.BeginStep(in context, ref state, 0f, 0f);
                objective.Tick(in context, ref state, 0f, 0.016f);
                objective.End(in context, ref state);

                builder.Clear();
                objective.Describe(builder, in state);
                Assert.Greater(builder.Length, 0, $"{type.Name}.Describe wrote nothing — the HUD would show a blank row");
            }
        }

        [Test]
        public void NoObjective_HoldsADelegate_BecauseNoObjectiveMaySubscribe()
        {
            // Rule 3, as a test rather than as a comment. With Domain Reload off a
            // ScriptableObject that subscribes keeps the subscription into the next
            // Play session — the mutable-static bug in the one shape the guard
            // cannot see, because nothing about the line is static. A delegate
            // field is the first symptom of somebody trying.
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (Type type in ObjectiveTypes())
            {
                foreach (FieldInfo field in type.GetFields(all))
                {
                    Assert.IsFalse(typeof(Delegate).IsAssignableFrom(field.FieldType),
                        $"{type.Name}.{field.Name} is a delegate — objectives poll MissionProgress, they never subscribe");
                }
                Assert.AreEqual(0, type.GetEvents(all).Length,
                    $"{type.Name} declares an event — see rule 3 in MissionObjective");
            }
        }
    }
}
