#nullable enable
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The seam a MissionDirector drives WaveRunner through, and — more
    /// importantly — the promise that it is inert without one.
    ///
    /// The whole design rests on a claim that is invisible in a diff: with no
    /// director in the scene, endless mode behaves exactly as it did before the
    /// seam existed. A regression there does not throw, does not log and does not
    /// fail a build; it quietly changes the game the player is playing. So the
    /// defaults are asserted here as facts rather than assumed.
    ///
    /// The members are internal and this is a different assembly, so they are
    /// reached by reflection rather than by widening the API or adding an
    /// InternalsVisibleTo that the shipping code would carry forever. That has a
    /// bonus: the lookups themselves fail loudly if anyone renames or deletes a
    /// seam member, which is the failure this file most wants to catch.
    ///
    /// WHAT THIS FILE CANNOT PROVE. Awake does not fire for a component added in
    /// edit mode, so the runner here has no token pool, no shop and no wiring.
    /// Death routing (PlayerDown against RunEnded), DespawnAll, and a hold
    /// carrying a live queue across real frames all need a scene — PlayMode.
    /// </summary>
    public sealed class WaveRunnerSeamTests
    {
        private GameObject? _host;
        private WaveRunner? _runner;

        [SetUp]
        public void CreateRunner()
        {
            _host = new GameObject("TestWaveRunner");
            _runner = _host.AddComponent<WaveRunner>();
        }

        [TearDown]
        public void DestroyRunner()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        private WaveRunner Runner => _runner!;

        // ---------- reflection plumbing ----------

        private static MethodInfo Method(string name)
        {
            MethodInfo? info = typeof(WaveRunner).GetMethod(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"WaveRunner.{name} is gone — the mission-director seam was renamed or removed");
            return info!;
        }

        /// <summary>
        /// Not a params list on purpose: arrays are covariant, so passing a
        /// WaveConfig[] to `params object[]` spreads it into one argument per
        /// element and the call silently fails to bind.
        /// </summary>
        private void Invoke(string name, object[] args) => Method(name).Invoke(Runner, args);

        /// <summary>
        /// Phase has a private setter and no public way in. Forcing it is how a
        /// pure test reaches "mid-wave" with no spawner, no navmesh and no frame.
        /// </summary>
        private void ForcePhase(RunPhase phase)
        {
            PropertyInfo? property = typeof(WaveRunner).GetProperty(nameof(WaveRunner.Phase));
            MethodInfo? setter = property?.GetSetMethod(nonPublic: true);
            Assert.IsNotNull(setter, "WaveRunner.Phase lost its setter");
            setter!.Invoke(Runner, new object[] { phase });
        }

        private static WaveConfig MakeWave(int number, int bonus)
        {
            WaveConfig wave = ScriptableObject.CreateInstance<WaveConfig>();
            wave.waveNumber = number;
            wave.displayName = $"TEST_{number}";
            wave.moneyBonusOnClear = bonus;
            return wave;
        }

        // ---------- the outcome ----------

        [Test]
        public void RunOutcome_DefaultsToDied_BecauseThatIsTheOnlyEndlessEnding()
        {
            // Died must stay at zero. Every uninitialised RunOutcome in the game
            // reads as "the player died", which is the correct answer in the mode
            // that has no director — reorder this and an endless death starts
            // reporting itself as a completed mission.
            Assert.AreEqual(RunOutcome.Died, default(RunOutcome));
            Assert.AreEqual(0, (int)RunOutcome.Died);
            Assert.AreEqual(4, Enum.GetValues(typeof(RunOutcome)).Length,
                "an outcome was added or removed — check every reader of RunOutcome before changing this");
        }

        [Test]
        public void AFreshRunner_IsTheEndlessConfiguration()
        {
            // The inertness contract, as four facts. A director sets all of them;
            // the absence of one must leave the game exactly as it shipped.
            Assert.IsFalse(Runner.Suspended, "a runner with no director must tick");
            Assert.AreEqual(RunOutcome.Died, Runner.Outcome);
            Assert.AreEqual(RunPhase.Countdown, Runner.Phase);
            Assert.AreEqual(0, Runner.WaveNumber);
        }

        [Test]
        public void EverySeamMember_StillExists_WithTheSignatureTheDirectorExpects()
        {
            // The director is written in a later task against exactly these. A
            // rename here is a compile error over there and a mystery in between.
            Assert.AreEqual(typeof(void), Method("SetWaves").ReturnType);
            Assert.AreEqual(typeof(WaveConfig[]), Method("SetWaves").GetParameters()[0].ParameterType);
            Assert.AreEqual(typeof(int), Method("StartFrom").GetParameters()[0].ParameterType);
            Assert.AreEqual(typeof(bool), Method("SetDeathEndsRun").GetParameters()[0].ParameterType);
            Assert.AreEqual(typeof(RunOutcome), Method("FinishRun").GetParameters()[0].ParameterType);
            Assert.AreEqual(0, Method("Suspend").GetParameters().Length);
            Assert.AreEqual(0, Method("Resume").GetParameters().Length);
            Assert.AreEqual(0, Method("AbortWave").GetParameters().Length);

            EventInfo? down = typeof(WaveRunner).GetEvent("PlayerDown");
            Assert.IsNotNull(down, "PlayerDown is gone — a campaign death has nowhere to report");
            Assert.AreEqual(typeof(Action), down!.EventHandlerType);

            EventInfo? ended = typeof(WaveRunner).GetEvent("RunEnded");
            Assert.IsNotNull(ended);
            Assert.AreEqual(typeof(Action<RunOutcome>), ended!.EventHandlerType,
                "RunEnded lost its payload, so a completed mission and a corpse look identical again");
        }

        // ---------- the hold ----------

        [Test]
        public void SuspendAndResume_ToggleTheHold_AndRepeatCallsAreHarmless()
        {
            Invoke("Suspend", Array.Empty<object>());
            Assert.IsTrue(Runner.Suspended);

            // A director that suspends twice for two reasons must not be punished
            // for it, and the second call must not overwrite the clock the first
            // one saved.
            Invoke("Suspend", Array.Empty<object>());
            Assert.IsTrue(Runner.Suspended);

            Invoke("Resume", Array.Empty<object>());
            Assert.IsFalse(Runner.Suspended);
            Invoke("Resume", Array.Empty<object>());
            Assert.IsFalse(Runner.Suspended);
        }

        [Test]
        public void Update_DoesNothingWhileSuspended()
        {
            // With no spawner and no difficulty asset, one un-suspended Update off
            // an expired countdown runs a whole wave's worth of logic: it starts
            // wave 1, finds that it planned nothing, says so, and lands in Cleared.
            // Which makes it a very loud detector of "the hold did not hold".
            Invoke("Suspend", Array.Empty<object>());
            Invoke("Update", Array.Empty<object>());
            Assert.AreEqual(RunPhase.Countdown, Runner.Phase,
                "a suspended runner advanced its phase — a wave would spawn behind the briefing");
            Assert.AreEqual(0, Runner.WaveNumber);

            // The control. Same call, hold released, and now it moves — which is
            // what proves the assertion above was a hold and not simple inertness.
            Invoke("Resume", Array.Empty<object>());
            LogAssert.Expect(LogType.Error, new Regex(".*planned no drones.*"));
            Invoke("Update", Array.Empty<object>());
            Assert.AreEqual(RunPhase.Cleared, Runner.Phase);
            Assert.AreEqual(1, Runner.WaveNumber);
        }

        // ---------- the wave list ----------

        [Test]
        public void SetWaves_LandsOutsideAWave_AndStartFromAimsAtIt()
        {
            WaveConfig mission = MakeWave(2, 111);
            Invoke("SetWaves", new object[] { new[] { mission } });

            // StartFrom backs the counter up one, because a countdown starts wave
            // _wave + 1. StartFrom(3) therefore means "the next wave fought is 3",
            // and CurrentWave still reports the one just left behind.
            Invoke("StartFrom", new object[] { 3 });
            Assert.AreEqual(2, Runner.WaveNumber);
            Assert.AreEqual(RunPhase.Countdown, Runner.Phase);
            Assert.AreSame(mission, Runner.CurrentWave, "the swapped-in list is not what ConfigForWave reads");
        }

        /// <summary>
        /// The question WaveNumber cannot answer, and the off-by-one that came
        /// of every caller answering it themselves.
        ///
        /// WaveNumber means "the last wave that STARTED". During a fight that is
        /// also the wave being fought; in Countdown, Cleared and Shop it is the
        /// one already behind you. So "which wave comes next" is sometimes the
        /// same number and sometimes one more, and nothing said so out loud until
        /// a campaign checkpoint had to write it down: MissionDirector recorded
        /// WaveNumber and replayed it through StartFrom, whose contract is stated
        /// in terms of the wave FOUGHT, so every checkpoint taken between waves
        /// sent the player back one wave too far.
        /// </summary>
        [Test]
        public void NextWaveNumber_AsksThePhase_BecauseWaveNumberMeansTheLastOneStarted()
        {
            // Nothing fought yet, and the countdown on screen is for wave 1.
            Assert.AreEqual(0, Runner.WaveNumber);
            Assert.AreEqual(RunPhase.Countdown, Runner.Phase);
            Assert.AreEqual(1, Runner.NextWaveNumber,
                "a runner that has fought nothing is about to fight wave 1");

            Invoke("StartFrom", new object[] { 4 });
            Assert.AreEqual(3, Runner.WaveNumber, "StartFrom backs the counter up one — see the test above");
            Assert.AreEqual(4, Runner.NextWaveNumber,
                "StartFrom(4) means the next wave fought is 4, so NextWaveNumber has to say 4. A checkpoint " +
                "that records one of these and is replayed through the other loses a wave every time it is taken.");

            // Mid-fight: the wave about to be fought is the one in progress.
            // This is the half that a plain WaveNumber + 1 gets wrong, and it is
            // why this property exists instead of an addition at each call site.
            ForcePhase(RunPhase.Wave);
            Assert.AreEqual(3, Runner.NextWaveNumber,
                "mid-wave, the wave to come back to is the one being fought, not the one after it");

            // And every phase that is not a wave is a gap between two of them.
            ForcePhase(RunPhase.Cleared);
            Assert.AreEqual(4, Runner.NextWaveNumber);
            ForcePhase(RunPhase.Shop);
            Assert.AreEqual(4, Runner.NextWaveNumber,
                "a checkpoint taken during a shop break belongs to the wave after the break, not the one before it");
            ForcePhase(RunPhase.Countdown);
            Assert.AreEqual(4, Runner.NextWaveNumber);
        }

        [Test]
        public void StartFrom_NeverAimsBelowWaveOne()
        {
            Invoke("StartFrom", new object[] { 0 });
            Assert.AreEqual(0, Runner.WaveNumber, "wave 0 is counted down to as wave 1 anyway");

            Invoke("StartFrom", new object[] { -7 });
            Assert.AreEqual(0, Runner.WaveNumber,
                "a corrupt checkpoint must not put a negative wave number into the loop");
        }

        [Test]
        public void SetWaves_IsRefusedMidWave_AndTheOldListStands()
        {
            WaveConfig authored = MakeWave(2, 111);
            WaveConfig replacement = MakeWave(2, 999);

            Invoke("SetWaves", new object[] { new[] { authored } });
            Invoke("StartFrom", new object[] { 3 });
            ForcePhase(RunPhase.Wave);

            LogAssert.Expect(LogType.Error, new Regex(".*SetWaves refused.*"));
            Invoke("SetWaves", new object[] { new[] { replacement } });

            Assert.AreSame(authored, Runner.CurrentWave,
                "the wave list changed under a live queue — the clear bonus would come from the wrong asset");
            Assert.AreEqual(111, Runner.CurrentWave!.moneyBonusOnClear);

            // And it is a refusal, not a permanent lock: out of the wave it lands.
            ForcePhase(RunPhase.Shop);
            Invoke("SetWaves", new object[] { new[] { replacement } });
            Assert.AreSame(replacement, Runner.CurrentWave);
        }

        // ---------- ending the run ----------

        [Test]
        public void FinishRun_RecordsTheOutcome_EndsThePhase_AndRaisesRunEndedOnce()
        {
            int raised = 0;
            RunOutcome seen = RunOutcome.Died;
            Runner.RunEnded += outcome => { raised++; seen = outcome; };

            Invoke("FinishRun", new object[] { RunOutcome.MissionComplete });

            Assert.AreEqual(RunOutcome.MissionComplete, Runner.Outcome);
            Assert.AreEqual(RunPhase.GameOver, Runner.Phase);
            Assert.AreEqual(1, raised);
            Assert.AreEqual(RunOutcome.MissionComplete, seen,
                "the outcome must travel with the event, or the screen behind it has to guess");

            // A second ending is not a second ending. Two objectives resolving on
            // the same frame must not fire the end-of-run screen twice.
            Invoke("FinishRun", new object[] { RunOutcome.MissionFailed });
            Assert.AreEqual(1, raised);
            Assert.AreEqual(RunOutcome.MissionComplete, Runner.Outcome,
                "a completed mission was overwritten by a later failure");
        }

        [Test]
        public void AbortWave_LeavesNothingQueued_AndDoesNotPickAPhase()
        {
            // Honest about its reach: with no registry and no pooled drones this
            // proves the queue side and that the call is safe on a bare runner.
            // DespawnAll and the token release need PlayMode.
            Invoke("StartFrom", new object[] { 3 });
            Invoke("AbortWave", Array.Empty<object>());
            Assert.AreEqual(0, Runner.EnemiesRemaining);
            Assert.AreEqual(RunPhase.Countdown, Runner.Phase, "AbortWave must not decide the phase itself");
        }

        [Test]
        public void SetDeathEndsRun_ChangesNothingObservableOnItsOwn()
        {
            // Its whole effect lives inside OnPlayerDied, which needs a Health and
            // a fired Awake — PlayMode. What is worth pinning here is that calling
            // it disturbs nothing, so a director can set it during its own Awake
            // before anything at all has started.
            Invoke("SetDeathEndsRun", new object[] { false });
            Assert.AreEqual(RunPhase.Countdown, Runner.Phase);
            Assert.AreEqual(RunOutcome.Died, Runner.Outcome);
            Assert.IsFalse(Runner.Suspended);
        }
    }
}
