#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Enemies;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The loop, actually running. This is the test suite that lets a session say
    /// "verified" instead of "compiles": it loads the real grey box, waits for the
    /// wave system to start, and asserts drones spawn, path, take damage, pay out,
    /// and hand the run to the shop.
    ///
    /// It cannot tell you whether any of that is FUN. It can tell you the thing
    /// you are about to judge is not broken, which is the difference between a
    /// tuning session and a debugging session.
    /// </summary>
    public sealed class GreyBoxLoopTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/10_GreyBox.unity";

        // These tests let real Rushers reach a real player for up to 45 seconds.
        // If one kills them, the run ends and the record is written.
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator LoadGreyBox()
        {
            _save.CaptureAndReset();
            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load, $"'{ScenePath}' must be in the build settings — the builder registers it");
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreTheSave()
        {
            _save.Restore();
            yield return null;
        }

        /// <summary>Polls a condition with a real-time budget, so a hang fails the test instead of the run.</summary>
        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds, string what,
            System.Action? eachFrame = null)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail($"timed out waiting for {what}");
                eachFrame?.Invoke();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SceneBoots_WithEveryCoreSystemPresent()
        {
            Assert.IsNotNull(Object.FindFirstObjectByType<WaveRunner>(), "no WaveRunner");
            Assert.IsNotNull(Object.FindFirstObjectByType<DroneSpawner>(), "no DroneSpawner");
            Assert.IsNotNull(Object.FindFirstObjectByType<DroneRegistry>(), "no DroneRegistry");
            Assert.IsNotNull(Object.FindFirstObjectByType<ObjectPool>(), "no ObjectPool");
            Assert.IsNotNull(Object.FindFirstObjectByType<RunContext>(), "no RunContext");
            yield return null;
        }

        [UnityTest]
        public IEnumerator NavMesh_CoversTheArena_WithNoIslands()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            Assert.IsNotNull(spawner);

            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Assert.Greater(triangulation.vertices.Length, 0,
                "the navmesh asset did not load — drones would spawn and never move");

            // Every spawn point must be able to reach THE PLAYER. Not the origin:
            // the arena has a solid centre block, so the origin is inside
            // geometry. An island is invisible in the editor and fatal at runtime —
            // the wave spawns and nothing ever arrives.
            var motor = Object.FindFirstObjectByType<CoD.Player.PlayerMotor>();
            Assert.IsNotNull(motor, "no player to path to");
            Assert.IsTrue(NavMesh.SamplePosition(motor!.transform.position, out NavMeshHit playerHit, 4f,
                NavMesh.AllAreas), "the player does not stand on the navmesh");

            var path = new NavMeshPath();
            foreach (Transform point in spawner.GetComponentsInChildren<Transform>())
            {
                if (!point.name.StartsWith("Spawn_")) continue;
                Assert.IsTrue(NavMesh.SamplePosition(point.position, out NavMeshHit hit, 4f, NavMesh.AllAreas),
                    $"{point.name} is not on the navmesh");
                Assert.IsTrue(NavMesh.CalculatePath(hit.position, playerHit.position, NavMesh.AllAreas, path)
                              && path.status == NavMeshPathStatus.PathComplete,
                    $"{point.name} cannot reach the player — that is a navmesh island");
            }
            yield return null;
        }

        /// <summary>
        /// The catwalks are contested ground, not a safe perch.
        ///
        /// THE FAILURE THIS CATCHES, which no other test in the project can see.
        /// The decks sit 3.5 m up and are reached by five steps, each rising
        /// 0.7 m against a baked step height of 0.75. That margin is five
        /// centimetres. Widen a step, deepen the deck, change the agent settings,
        /// or let the arena kit swap a module for one with a different pivot, and
        /// the bake stops joining the stairs — the deck becomes an island.
        ///
        /// The island is SILENT and it is fatal in a specific, humiliating way:
        /// the player walks up, no drone can follow, the wave never clears, and
        /// the run hangs with a full health bar. NavMesh_CoversTheArena above
        /// cannot catch it, because every spawn point and the player's start are
        /// on the floor — the path it checks never goes near the stairs.
        ///
        /// So this one paths TO the deck rather than across the floor.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCatwalks_CanBeReachedByDrones()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            Assert.IsNotNull(spawner, "no spawner to path from");

            // Both decks, sampled a little above the walking surface so a sample
            // that lands on the FLOOR underneath fails loudly instead of quietly
            // passing. The vertical tolerance is deliberately tight for the same
            // reason: 1.5 m cannot reach the floor 3.5 m below.
            var decks = new[]
            {
                new Vector3(-13.5f, 3.6f, 4f),
                new Vector3(13.5f, 3.6f, 4f),
            };

            var path = new NavMeshPath();
            foreach (Vector3 deck in decks)
            {
                Assert.IsTrue(NavMesh.SamplePosition(deck, out NavMeshHit deckHit, 1.5f, NavMesh.AllAreas),
                    $"no navmesh on the catwalk deck at {deck} — the deck did not bake as walkable at all, " +
                    "so the player can stand somewhere the drones do not know exists");

                Assert.Greater(deckHit.position.y, 2f,
                    $"the sample at {deck} landed at y={deckHit.position.y:F2}, which is the floor rather " +
                    "than the deck — the deck itself is not on the navmesh");

                bool anySpawnReaches = false;
                foreach (Transform point in spawner!.GetComponentsInChildren<Transform>())
                {
                    if (!point.name.StartsWith("Spawn_")) continue;
                    if (!NavMesh.SamplePosition(point.position, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                    if (NavMesh.CalculatePath(hit.position, deckHit.position, NavMesh.AllAreas, path)
                        && path.status == NavMeshPathStatus.PathComplete)
                    {
                        anySpawnReaches = true;
                        break;
                    }
                }

                Assert.IsTrue(anySpawnReaches,
                    $"no spawn point can path onto the catwalk at {deck}. The stairs did not connect to the " +
                    "deck, so a player standing up there cannot be reached by anything — the wave never " +
                    "clears and the run hangs.");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Wave_Starts_SpawnsDrones_AndTheyPathTowardThePlayer()
        {
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(registry);

            yield return WaitUntil(() => runner!.Phase == RunPhase.Wave, 20f, "the first wave to start");
            yield return WaitUntil(() => registry!.AliveCount > 0, 20f, "the first drone to spawn");

            DroneController drone = registry!.Alive[0];
            Assert.IsTrue(drone.IsActive);

            // A drone whose agent never took a path is the pooled-NavMeshAgent bug
            // this project has already paid for once.
            var agent = drone.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.enabled, "the agent must be enabled after Initialize");
            Assert.IsTrue(agent.isOnNavMesh, "the agent must be placed on the navmesh");

            Vector3 startDistanceTo = drone.Target != null
                ? drone.Target.position - drone.Position
                : Vector3.zero;
            float before = startDistanceTo.magnitude;

            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline && drone.IsActive) yield return null;

            if (drone.IsActive && drone.Target != null)
            {
                float after = (drone.Target.position - drone.Position).magnitude;
                Assert.Less(after, before, "a Rusher that does not close the distance is not chasing");
            }
        }

        [UnityTest]
        public IEnumerator KillingDrones_PaysMoney_AndClearsTheWaveIntoTheShop()
        {
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            var run = Object.FindFirstObjectByType<RunContext>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(registry);
            Assert.IsNotNull(run);

            yield return WaitUntil(() => runner!.Phase == RunPhase.Wave, 20f, "the first wave");
            yield return WaitUntil(() => registry!.AliveCount > 0, 20f, "the first drone");

            int moneyBefore = run!.State.Money;

            // Kill everything the wave produces until the runner says it is done.
            float deadline = Time.realtimeSinceStartup + 45f;
            while (runner!.Phase == RunPhase.Wave && Time.realtimeSinceStartup < deadline)
            {
                for (int i = registry!.Alive.Count - 1; i >= 0; i--)
                {
                    Health? health = registry.Alive[i].HealthComponent;
                    if (health == null || !health.IsAlive) continue;
                    var info = new DamageInfo(9999f, health.transform.position, Vector3.up, Vector3.forward, false);
                    health.ApplyDamage(in info);
                }
                yield return null;
            }

            Assert.AreNotEqual(RunPhase.Wave, runner.Phase, "the wave never ended after everything died");
            Assert.Greater(run.State.Kills, 0, "kills must be counted through the registry");
            Assert.Greater(run.State.Money, moneyBefore, "a kill has to pay");

            yield return WaitUntil(() => runner.Phase == RunPhase.Shop, 20f, "the shop to open");

            Assert.IsNotNull(runner.Shop);
            Assert.Greater(runner.Shop!.Offers.Count, 0, "an empty shop break is a break with nothing in it");
            Assert.AreEqual(runner.Shop.Offers.Count, runner.Shop.Prices.Count, "offers and prices must stay aligned");
        }

        /// <summary>
        /// Skipping a break leaves the shop, arms the bonus, and spends it exactly
        /// once. The arithmetic is covered by WaveDesignTests against the pure
        /// helper — a money total here already has kill rewards folded into it, so
        /// it could never prove the multiplication on its own.
        /// </summary>
        [UnityTest]
        public IEnumerator SkippingTheShop_ArmsTheBonus_AndSpendsItOnce()
        {
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(registry);
            Assert.Greater(runner!.SkipBonusMultiplier, 1f, "skipping is not worth anything in this build");

            yield return WaitUntil(() => runner.Phase == RunPhase.Shop, 90f, "the first shop break",
                () => KillEverything(registry!));

            Assert.AreEqual(1f, runner.PendingClearMultiplier, 1e-3f, "nothing should be armed yet");

            runner.SkipShopForBonus();
            Assert.AreNotEqual(RunPhase.Shop, runner.Phase, "skipping must leave the break");
            Assert.Greater(runner.PendingClearMultiplier, 1f, "the bonus was not armed");

            yield return WaitUntil(() => runner.Phase == RunPhase.Cleared || runner.Phase == RunPhase.Shop,
                90f, "the next wave to clear", () => KillEverything(registry!));

            Assert.AreEqual(1f, runner.PendingClearMultiplier, 1e-3f,
                "the bonus must be spent on the clear, not carried into every wave after it");
        }

        /// <summary>Kills whatever is alive this frame. Used to drive a wave to its end quickly.</summary>
        private static void KillEverything(DroneRegistry registry)
        {
            for (int i = registry.Alive.Count - 1; i >= 0; i--)
            {
                Health? health = registry.Alive[i].HealthComponent;
                if (health == null || !health.IsAlive) continue;
                var info = new DamageInfo(9999f, health.transform.position, Vector3.up, Vector3.forward, false);
                health.ApplyDamage(in info);
            }
        }

        /// <summary>
        /// The beacon moves between waves and heals a hurt player standing on it,
        /// but only up to the wave's budget.
        ///
        /// The budget is the part worth guarding: without it the beacon is a free
        /// full reset every wave, which removes the decision it was added to
        /// create rather than adding one.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBeacon_Relocates_AndHealsWithinItsBudget()
        {
            var objective = Object.FindFirstObjectByType<ArenaObjective>();
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            var motor = Object.FindFirstObjectByType<CoD.Player.PlayerMotor>();
            Assert.IsNotNull(objective, "no ArenaObjective — the lanes have nothing to reward");
            Assert.IsNotNull(runner);
            Assert.IsNotNull(motor);

            yield return WaitUntil(() => runner!.Phase == RunPhase.Wave, 30f, "the first wave");

            Vector3 first = objective!.Position;
            Assert.AreNotEqual(Vector3.zero, first,
                "the beacon is at the origin, which is inside the centre bunker");

            // Hurt the player, then stand them on the pad.
            Health? health = motor!.GetComponent<Health>();
            Assert.IsNotNull(health);
            var wound = new DamageInfo(60f, motor.transform.position, Vector3.up, Vector3.forward, false);
            health!.ApplyDamage(in wound);
            float hurt = health.Current;
            Assert.Less(hurt, health.Max);

            // Godmode for the duration. The player has to stand still on the pad
            // while a live wave converges on them, so without this the test is a
            // coin flip on whether a Rusher reaches them first — and a dead player
            // leaves RunPhase.Wave, which stops the beacon healing at all. This
            // fixes the test's determinism, not the behaviour being asserted:
            // Invulnerable blocks ApplyDamage and does nothing to Heal.
            health.Invulnerable = true;

            motor.transform.position = first + Vector3.up * 0.1f;
            float budget = objective.BudgetRemaining;
            Assert.Greater(budget, 0f, "the beacon starts a wave with nothing to give");

            // GLUED TO THE PAD, EVERY FRAME, and this is a determinism fix rather
            // than a convenience. Setting the position once was a race against two
            // things the test does not control: a CharacterController resolving a
            // capsule dropped half into the floor, and the beacon RELOCATING if a
            // wave boundary happened to cross the wait. Either one leaves the
            // player off the pad, the budget undrained, and the failure reported
            // as "timed out" — which says nothing about what went wrong. Observed
            // failing once in fifteen runs; the assertions below are unchanged.
            yield return WaitUntil(
                () => objective.BudgetRemaining <= 0f || health.Current >= health.Max,
                20f, "the beacon to spend its budget",
                () =>
                {
                    // The wave ending is the OTHER way this stalls: the beacon
                    // only heals during RunPhase.Wave. Said out loud, because a
                    // timeout here would otherwise read as a broken beacon.
                    Assert.AreEqual(RunPhase.Wave, runner!.Phase,
                        "the wave ended before the beacon spent its budget — it only heals during a wave, " +
                        "so this is the test losing a race rather than the beacon failing");
                    motor.transform.position = objective.Position + Vector3.up * 0.1f;
                });

            Assert.Greater(health.Current, hurt, "standing on the beacon healed nothing");
            Assert.LessOrEqual(health.Current, health.Max, "healing went past the maximum");
            Assert.LessOrEqual(health.Current - hurt, budget + 1f,
                "the beacon gave more than its per-wave budget allows");

            health.Invulnerable = false;

            // And it moves. Never the same lane twice in a row, so one wave is enough.
            yield return WaitUntil(() => runner!.Phase == RunPhase.Shop, 90f, "the break",
                () => KillEverything(registry!));
            runner!.ContinueFromShop();
            yield return WaitUntil(() => runner.Phase == RunPhase.Wave, 30f, "the second wave");

            Assert.AreNotEqual(first, objective.Position,
                "the beacon stayed put, so camping one corner is still free");
        }

        [UnityTest]
        public IEnumerator ThePoolReusesDrones_RatherThanGrowing()
        {
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            var registry = Object.FindFirstObjectByType<DroneRegistry>();
            Assert.IsNotNull(spawner);
            Assert.IsNotNull(registry);

            DroneConfig? config = spawner!.DefaultDrone;
            Assert.IsNotNull(config);

            yield return WaitUntil(() => spawner.Spawn(config!) != null, 10f, "a sandbox spawn");
            DroneController first = registry!.Alive[registry.Alive.Count - 1];
            int firstId = first.gameObject.GetInstanceID();

            first.DespawnNow();
            yield return null;

            DroneController? second = spawner.Spawn(config!);
            Assert.IsNotNull(second);
            // If this fails the pool is growing instead of recycling, which is the
            // GC-hitch factory the whole pool exists to prevent.
            Assert.AreEqual(firstId, second!.gameObject.GetInstanceID(),
                "the despawned instance should have come straight back out of the pool");

            second.DespawnNow();
            yield return null;
        }
    }
}
