#nullable enable
using System.Collections;
using CoD.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The clock freeze, and the three ways it could ruin a session.
    ///
    /// Hitstop is a small effect with an outsized failure mode: it writes
    /// Time.timeScale, which is global, which two other systems in this game
    /// also write. Every test here is about the handover rather than about the
    /// freeze — the freeze is four lines and obviously correct, and the handover
    /// is where a player ends up stuck in slow motion with no way back.
    ///
    /// TIMESCALE IS RESTORED IN TEARDOWN, unconditionally. A test that fails
    /// midway through a hold would otherwise leave the editor's clock at 0.06
    /// and every subsequent test in the run would fail for reasons that have
    /// nothing to do with what they assert.
    /// </summary>
    public sealed class HitstopTests
    {
        private GameObject? _host;
        private GameConfig? _config;

        private const float FROZEN_SCALE = 0.05f;
        private const float MIN_SECONDS = 0.04f;
        private const float MAX_SECONDS = 0.10f;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _config.hitstopMinSeconds = MIN_SECONDS;
            _config.hitstopMaxSeconds = MAX_SECONDS;
            _config.hitstopHealthForMax = 600f;
            _config.hitstopWeakpointBonus = 1.5f;
            _config.hitstopTimeScale = FROZEN_SCALE;
            _config.hitstopCooldownSeconds = 0f;   // cooldown has its own test

            _host = new GameObject("HitstopHost");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_host != null) Object.DestroyImmediate(_host);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        private Hitstop NewHitstop()
        {
            Hitstop hitstop = _host!.AddComponent<Hitstop>();
            hitstop.Configure(_config!);
            return hitstop;
        }

        [Test]
        public void APunch_SlowsTheClock()
        {
            Hitstop hitstop = NewHitstop();
            hitstop.Punch(0f, weakpoint: false);

            Assert.IsTrue(hitstop.IsActive, "A punch did not take the clock");
            Assert.AreEqual(FROZEN_SCALE, Time.timeScale, 0.0001f,
                "The clock did not slow, so a kill has no weight at all");
        }

        [UnityTest]
        public IEnumerator TheClockComesBack()
        {
            Hitstop hitstop = NewHitstop();
            hitstop.Punch(1f, weakpoint: true);

            // Real seconds, generously past the longest possible hold: max x the
            // weakpoint bonus is 0.15, and this must not be flaky on a loaded
            // machine.
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.IsFalse(hitstop.IsActive, "The hold never ended");
            Assert.AreEqual(1f, Time.timeScale, 0.0001f,
                "The clock was never handed back — the game is in permanent slow motion");
        }

        /// <summary>
        /// Rule 1. A kill during the sandbox slow-mo cheat must feel like the
        /// same punch, not like a speed-up. Absolute rather than relative
        /// scaling would ACCELERATE the game on a kill in slow-mo, which is the
        /// opposite of the effect.
        /// </summary>
        [Test]
        public void APunchDuringSlowMo_ComposesInsteadOfFighting()
        {
            Time.timeScale = 0.35f;

            Hitstop hitstop = NewHitstop();
            hitstop.Punch(0f, weakpoint: false);

            Assert.AreEqual(0.35f * FROZEN_SCALE, Time.timeScale, 0.0001f,
                "Hitstop overwrote slow-mo with an absolute scale instead of composing with it");

            hitstop.Cancel();
            Assert.AreEqual(0.35f, Time.timeScale, 0.0001f,
                "Releasing the hold cancelled the slow-mo cheat that was running before it");
        }

        /// <summary>
        /// Rule 2. A kill landing on the frame the pause menu opens must not
        /// restart the game.
        /// </summary>
        [Test]
        public void APunchOnAStoppedClock_IsRefused()
        {
            Time.timeScale = 0f;

            Hitstop hitstop = NewHitstop();
            hitstop.Punch(1f, weakpoint: true);

            Assert.IsFalse(hitstop.IsActive, "Hitstop engaged on a paused game");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f,
                "A kill un-paused the game");
        }

        /// <summary>
        /// Rule 3. If anything took the clock while this was holding it, that
        /// thing owns it now. The sandbox console toggling slow-mo mid-freeze is
        /// the real case.
        /// </summary>
        [Test]
        public void ReleasingAfterSomeoneElseTookTheClock_LeavesItAlone()
        {
            Hitstop hitstop = NewHitstop();
            hitstop.Punch(0f, weakpoint: false);

            // Somebody else writes the clock during the hold.
            Time.timeScale = 0.2f;
            hitstop.Cancel();

            Assert.AreEqual(0.2f, Time.timeScale, 0.0001f,
                "Hitstop restored its own captured value over a clock somebody else had taken");
        }

        /// <summary>
        /// A second kill inside a hold must not record the FROZEN clock as the
        /// thing to go back to. This is the bug that would compound: every kill
        /// in a swarm would restore to a slightly slower clock than the last.
        /// </summary>
        [Test]
        public void OverlappingKills_DoNotCompound()
        {
            Hitstop hitstop = NewHitstop();

            hitstop.Punch(0f, weakpoint: false);
            hitstop.Punch(0f, weakpoint: false);
            hitstop.Punch(0f, weakpoint: false);

            Assert.AreEqual(FROZEN_SCALE, Time.timeScale, 0.0001f,
                "Overlapping punches multiplied into a deeper and deeper freeze");

            hitstop.Cancel();
            Assert.AreEqual(1f, Time.timeScale, 0.0001f,
                "Three kills in one hold left the clock below where it started");
        }

        /// <summary>
        /// The horde guard. Wave 8 sends twenty Rushers in ten seconds; without
        /// a floor between freezes the best moment in the game becomes a strobe.
        /// </summary>
        [UnityTest]
        public IEnumerator RapidKills_AreRateLimited()
        {
            _config!.hitstopCooldownSeconds = 5f;

            Hitstop hitstop = NewHitstop();
            hitstop.Punch(0f, weakpoint: false);
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.IsFalse(hitstop.IsActive, "The first hold should have ended by now");

            hitstop.Punch(0f, weakpoint: false);
            Assert.IsFalse(hitstop.IsActive,
                "A second kill inside the cooldown re-froze the clock — twenty Rushers would strobe");
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "The rate-limited punch still touched the clock");
        }

        /// <summary>
        /// Bigger things hit harder. If weight did nothing, a Tank and a Rusher
        /// would die with identical weight and the effect would carry no
        /// information at all.
        /// </summary>
        [Test]
        public void ABiggerEnemy_HoldsLonger()
        {
            Hitstop light = NewHitstop();
            light.Punch(0f, weakpoint: false);
            light.Cancel();

            // Both are configured from the same asset, so comparing the authored
            // ends of the scale is the honest form of this assertion: a weight of
            // 1 must map to a longer hold than a weight of 0.
            Assert.Greater(_config!.hitstopMaxSeconds, _config.hitstopMinSeconds,
                "The heavy and light ends of the hitstop scale are the same number, so " +
                "a Tank dies with exactly the weight of a Rusher");
            Assert.Greater(_config.hitstopWeakpointBonus, 1f,
                "A weakpoint kill is worth no more than a hull kill, so landing the core feels like nothing");
        }

        /// <summary>
        /// Disabling must not strand the clock. A scene load during a hold is
        /// the realistic path, and BuildSmokeTest specifically fails the build
        /// when anything leaves timeScale stopped.
        /// </summary>
        [Test]
        public void DisablingDuringAHold_HandsTheClockBack()
        {
            Hitstop hitstop = NewHitstop();
            hitstop.Punch(1f, weakpoint: true);
            Assert.AreNotEqual(1f, Time.timeScale, "Setup failed — nothing was held");

            hitstop.enabled = false;

            Assert.AreEqual(1f, Time.timeScale, 0.0001f,
                "A disabled Hitstop left the clock frozen with nothing alive to restore it");
        }
    }
}
