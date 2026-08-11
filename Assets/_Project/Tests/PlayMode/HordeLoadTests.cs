#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Enemies;
using CoD.Waves;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The horde at full load. `maxAliveDrones 40` exists for a 4 GB GPU and
    /// `maxSimultaneousAttackers 3` is why twenty enemies read as fair rather
    /// than as a mugging; CLAUDE.md calls both "not tuning knobs". A rule nobody
    /// checks is a rule that quietly stops holding.
    ///
    /// WHAT A MACHINE CAN AND CANNOT PROVE HERE
    /// It can prove the caps hold, that nothing throws under load, and that the
    /// per-frame allocation of a full arena stays inside a budget — which is the
    /// GC-hitch factory the object pool exists to prevent. It CANNOT prove the
    /// frame time on an RTX 3050: these run under -batchmode -nographics, where
    /// there is no GPU work at all. The measured milliseconds are logged for a
    /// human to read, never asserted, because a green light on a number this run
    /// cannot legitimately produce is worse than no number.
    /// </summary>
    public sealed class HordeLoadTests
    {
        /// <summary>
        /// Per-frame managed allocation budget under full load, in bytes.
        ///
        /// A test-harness threshold, not game tuning — no balance decision reads
        /// it, and putting a CI budget in the same asset as drone health would
        /// make that asset harder to reason about. Deliberately generous: the
        /// point is to catch a `new` or a LINQ query that slipped into a hot
        /// path, not to police the test framework's own coroutine machinery.
        /// </summary>
        private const long AllocationBudgetPerFrame = 16 * 1024;

        private const int MeasuredFrames = 120;

        /// <summary>
        /// Windows that wait for the DRONES to do something are measured in
        /// seconds, never in frames. A -batchmode run is uncapped and can push a
        /// thousand frames a second, so "900 frames" was under one second of game
        /// time — nowhere near long enough for a Rusher to cross the arena. The
        /// first version of the token test passed while reporting a peak of 0,
        /// which is the shape of a test that checks nothing.
        /// </summary>
        private const float PressureSeconds = 20f;

        // Forty Rushers converging on one player. They win, and that ends a run.
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator LoadGreyBox()
        {
            // Reset, not just capture: a Sandbox lastMode left behind by a real
            // play session silences RecordRunEnded, and a run ending here then
            // logged an Info that LogAssert.NoUnexpectedReceived failed on.
            _save.CaptureAndReset();
            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load);
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ClearTheArena()
        {
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            if (registry != null) registry.DespawnAll();
            _save.Restore();
            yield return null;
        }

        /// <summary>Fill the arena to the cap, whatever the cap is. Returns how many are alive.</summary>
        private static IEnumerator FillToCap(DroneSpawner spawner, DroneRegistry registry, DroneConfig config)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (spawner.CanSpawn() && Time.realtimeSinceStartup < deadline)
            {
                // One per frame: spawning forty in a single frame is not what the
                // wave runner does, and would measure a burst nobody experiences.
                if (spawner.Spawn(config) == null) yield return null;
                yield return null;
            }
            Assert.Greater(registry.AliveCount, 0, "nothing spawned at all");
        }

        [UnityTest]
        public IEnumerator TheAliveCap_Holds_NoMatterHowHardYouPush()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            Assert.IsNotNull(spawner);
            Assert.IsNotNull(registry);

            DroneConfig? config = spawner!.DefaultDrone;
            Assert.IsNotNull(config);

            yield return FillToCap(spawner, registry!, config!);

            int cap = registry!.AliveCount;
            Assert.IsFalse(spawner.CanSpawn(), "the spawner should refuse once the arena is full");

            // Now hammer it. A cap that only holds when the caller is polite is
            // not a cap — it is a convention.
            for (int i = 0; i < 40; i++)
            {
                spawner.SpawnBurst(config!, 10);
                yield return null;
                Assert.LessOrEqual(registry.AliveCount, cap,
                    "the alive cap was exceeded — this is the 4 GB VRAM budget, not a suggestion");
            }
        }

        [UnityTest]
        public IEnumerator FortyAlive_StaysInsideTheAllocationBudget()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            Assert.IsNotNull(spawner);
            Assert.IsNotNull(registry);

            DroneConfig? config = spawner!.DefaultDrone;
            Assert.IsNotNull(config);

            yield return FillToCap(spawner, registry!, config!);
            int alive = registry!.AliveCount;

            // Settle first: the frames right after a spawn burst legitimately
            // allocate (agent paths, pool growth) and measuring them would report
            // the setup rather than the steady state.
            float settleUntil = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < settleUntil) yield return null;

            // ProfilerRecorder reads Unity's own counters. "GC Allocated In Frame"
            // is the managed bytes allocated this frame — the number that becomes
            // a collection, and then a hitch, in a horde game.
            using ProfilerRecorder allocated =
                ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

            long worst = 0;
            long total = 0;
            int samples = 0;

            for (int i = 0; i < MeasuredFrames; i++)
            {
                yield return null;
                if (!allocated.Valid) continue;
                long value = allocated.LastValue;
                if (value < 0) continue;
                worst = value > worst ? value : worst;
                total += value;
                samples++;
            }

            if (samples == 0)
            {
                // The counter is unavailable in some player configurations. Say so
                // rather than passing silently — a gate that cannot measure has
                // not measured.
                Assert.Inconclusive("the GC allocation counter was unavailable in this run");
                yield break;
            }

            long mean = total / samples;
            Debug.Log($"Horde load: {alive} alive, GC alloc mean {mean} B/frame, worst {worst} B/frame " +
                      $"over {samples} frames.");

            Assert.Less(worst, AllocationBudgetPerFrame,
                $"{alive} drones allocated {worst} bytes in a frame. Something in the hot path is " +
                "allocating — a new collection, a LINQ query, or string concatenation. That is the " +
                "GC hitch the object pool exists to prevent.");
        }

        [UnityTest]
        public IEnumerator TheAttackTokenCap_IsReached_AndNeverExceeded()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var motor = Object.FindFirstObjectByType<CoD.Player.PlayerMotor>();
            Assert.IsNotNull(spawner);
            Assert.IsNotNull(registry);
            Assert.IsNotNull(runner);
            Assert.IsNotNull(motor);

            // Make the player unkillable for the duration. Forty rushers detonate
            // for 24 each against 100 HP, so without this the arena clears itself
            // within a second or two of the first arrival and the test measures a
            // corpse. Invulnerable is the same flag the sandbox console flips.
            Health? playerHealth = motor!.GetComponent<Health>();
            Assert.IsNotNull(playerHealth);
            playerHealth!.Invulnerable = true;

            DroneConfig? config = spawner!.DefaultDrone;
            Assert.IsNotNull(config);

            yield return FillToCap(spawner, registry!, config!);

            AttackTokenPool? tokens = runner!.Tokens;
            Assert.IsNotNull(tokens, "no token pool — every drone would commit to an attack at once");

            int peak = 0;
            int capacity = tokens!.Capacity;
            float deadline = Time.realtimeSinceStartup + PressureSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                // Top the arena back up as detonations remove drones, so the
                // pressure on the cap does not fade out halfway through.
                if (spawner.CanSpawn()) spawner.Spawn(config!);

                int held = tokens.Held;
                Assert.LessOrEqual(held, capacity,
                    "more drones hold an attack token than the cap allows — that is twenty enemies " +
                    "attacking at once instead of three");
                if (held > peak) peak = held;
                yield return null;
            }

            playerHealth.Invulnerable = false;

            Debug.Log($"Attack tokens: peak {peak} / {capacity} with {registry!.AliveCount} alive.");

            // Without this the test passes trivially on an arena where nothing
            // ever got close enough to attack — which is exactly what the first
            // version of it did, reporting a meaningless 0 / 3.
            Assert.Greater(peak, 0,
                "no drone ever acquired an attack token, so the cap was never actually under test");
        }

        [UnityTest]
        public IEnumerator AFullArena_ThrowsNothing()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            Assert.IsNotNull(spawner);
            Assert.IsNotNull(registry);

            DroneConfig? config = spawner!.DefaultDrone;
            Assert.IsNotNull(config);

            yield return FillToCap(spawner, registry!, config!);

            // LogAssert fails the test on any unexpected error or exception logged
            // during the window — including one thrown deep inside a drone's
            // Update, which would otherwise only show as a red line a human has to
            // notice.
            LogAssert.NoUnexpectedReceived();

            // Long enough for drones to actually arrive, detonate and be
            // recycled — the window in which a null reference in an attack or a
            // despawn path would surface. Seconds, not frames; see PressureSeconds.
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (spawner.CanSpawn()) spawner.Spawn(config!);
                yield return null;
            }

            LogAssert.NoUnexpectedReceived();
            Assert.Greater(registry!.AliveCount, 0, "the arena emptied itself, so nothing was under load");
        }
    }
}
