#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Enemies;
using CoD.Player;
using CoD.UI;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The two audit findings that only the real scene can prove: what happens to
    /// the player's controls when the run ends, and whether a Shooter's round can
    /// get stuck on another drone.
    ///
    /// Both are silent. Neither logs anything, neither throws, and both are
    /// invisible in every gate this project had before them.
    /// </summary>
    public sealed class DeathHandoverTests
    {
        // Killing the player runs the real persistence path, and the mode it
        // finds there decides whether anything is written at all.
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator LoadGreyBoxAndBackUpTheSave()
        {
            _save.CaptureAndReset();

            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load);
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreEverything()
        {
            Time.timeScale = 1f;
            PlayerLook.SetCursorLocked(false);
            _save.Restore();
            yield return null;
        }

        private static Health FindPlayerHealth()
        {
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.IsNotNull(motor, "no PlayerMotor in the arena");
            Health? health = motor!.GetComponent<Health>();
            Assert.IsNotNull(health, "the player has no Health");
            return health!;
        }

        [UnityTest]
        public IEnumerator Death_TakesTheKeyboardAndGivesBackTheCursor()
        {
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var input = Object.FindFirstObjectByType<PlayerInput>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(input);
            Assert.IsFalse(input!.IsBlocked, "the player should start the run holding the controls");

            FindPlayerHealth().ApplyDamage(
                new DamageInfo(99999f, Vector3.zero, Vector3.up, Vector3.forward, false));
            yield return null;

            Assert.AreEqual(RunPhase.GameOver, runner!.Phase, "killing the player must end the run");

            // Nothing used to do this. The death screen drew over an arena the
            // corpse could still walk and shoot around, with the mouse still
            // captured — and pausing is refused at game over, so the only key that
            // did anything at all was R.
            Assert.IsTrue(input.IsBlocked, "a dead player must not keep the action map");
            Assert.AreNotEqual(CursorLockMode.Locked, Cursor.lockState,
                "the cursor must come back when the run ends");
        }

        [UnityTest]
        public IEnumerator Death_DoesNotFreezeTheClock()
        {
            FindPlayerHealth().ApplyDamage(
                new DamageInfo(99999f, Vector3.zero, Vector3.up, Vector3.forward, false));
            yield return null;

            // Blocking input must not be confused with pausing: the death screen
            // still needs to animate, and the next scene inherits timeScale.
            Assert.AreEqual(1f, Time.timeScale, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ABoughtPassive_DoesNotHealTheDyingPlayer()
        {
            var run = Object.FindFirstObjectByType<RunContext>();
            Assert.IsNotNull(run);
            Health health = FindPlayerHealth();

            health.ApplyDamage(new DamageInfo(health.Max - 8f, Vector3.zero, Vector3.up, Vector3.forward, false));
            Assert.AreEqual(8f, health.Current, 0.5f, "the player should be nearly dead");

            // A passive that has nothing to do with health. Through ConfigureMax
            // this refilled the bar, so the cheapest thing in the shop was a full
            // heal and the shop break was never a real decision.
            PassiveConfig reload = ScriptableObject.CreateInstance<PassiveConfig>();
            reload.modifiers = new[]
            {
                new PassiveConfig.Modifier
                {
                    stat = Stat.ReloadSpeed, kind = StatModifierKind.Multiplier, value = 1.2f,
                },
            };
            run!.BuyPassive(reload);
            yield return null;

            Assert.AreEqual(8f, health.Current, 0.5f,
                "buying a reload upgrade must not restore health");
            Object.DestroyImmediate(reload);
        }
    }

    /// <summary>
    /// A Shooter's round, in a room, with another drone in the way. The bug this
    /// pins froze the projectile in mid-air permanently and never returned it to
    /// the pool, so a wave of shooters leaked the pool for the rest of the run.
    /// </summary>
    public sealed class DroneProjectilePassThroughTests
    {
        // Loads the real grey box and lets it run, so a Rusher can reach the
        // player and end a run — which writes the record. See SaveFileGuard.
        private readonly SaveFileGuard _save = new();

        private GameObject? _stage;

        [UnitySetUp]
        public IEnumerator LoadGreyBox()
        {
            _save.CaptureAndReset();
            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load);
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ClearTheStage()
        {
            _save.Restore();
            if (_stage != null) Object.Destroy(_stage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARoundThatCrossesADrone_KeepsGoing_RatherThanStalling()
        {
            var pool = Object.FindFirstObjectByType<ObjectPool>();
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            Assert.IsNotNull(pool);
            Assert.IsNotNull(spawner);

            DroneConfig? drone = spawner!.DefaultDrone;
            Assert.IsNotNull(drone, "the spawner needs a default drone for this test");
            Assert.IsNotNull(drone!.prefab);

            GameObject projectilePrefab = FindProjectilePrefab();

            // Park a drone directly in the round's path, well clear of the arena
            // geometry so the only thing the sweep can meet is the drone.
            var origin = new Vector3(0f, 200f, 0f);
            _stage = Object.Instantiate(drone.prefab!, origin + Vector3.forward * 4f, Quaternion.identity);
            yield return null;

            PooledObject instance = pool!.Spawn(projectilePrefab, origin, Quaternion.LookRotation(Vector3.forward));
            Assert.IsTrue(instance.TryGetComponent(out DroneProjectile projectile));

            float startZ = instance.CachedTransform.position.z;
            const float speed = 18f;
            projectile.Launch(pool, Vector3.forward * speed, 5f, lifetime: 1.5f, hitMask: ~0);

            // Measured in SECONDS, never in frames. A -batchmode run is uncapped
            // and pushes thousands of frames a second, so "30 frames" was about
            // fifteen milliseconds of game time — the round had barely left the
            // barrel and the test read that as a stall.
            float launchedAt = Time.time;
            float furthest = startZ;
            while (Time.time - launchedAt < 0.4f)
            {
                if (instance.IsSpawned) furthest = Mathf.Max(furthest, instance.CachedTransform.position.z);
                yield return null;
            }

            // Two separate failures, both from the same line. The round used to
            // treat a drone as a hit, and Resolve returned early for its own kind
            // WITHOUT despawning and WITHOUT advancing — so it hung at its spawn
            // point forever, holding a pooled instance. After that was fixed but
            // the weakpoint Core was still treated as solid, it despawned on
            // contact instead, and drones became cover for the player.
            // The drone sits 4 m out and the round covers that in 0.22 s, so
            // anything past it proves the sweep did not treat a drone as a wall.
            Assert.Greater(furthest - startZ, 4.5f,
                "the round must travel past a drone in its path, not stall on it or die against it");

            while (Time.time - launchedAt < 2.5f && instance.IsSpawned) yield return null;
            Assert.IsFalse(instance.IsSpawned,
                "the round must reach its lifetime and return to the pool rather than hang in the air");
        }

        private static GameObject FindProjectilePrefab()
        {
            var burst = Object.FindFirstObjectByType<DroneSpawner>();
            Assert.IsNotNull(burst);
            foreach (DroneController controller in Resources.FindObjectsOfTypeAll<DroneController>())
            {
                DroneConfig? config = controller.Config;
                if (config?.attack is RangedBurst ranged && ranged.projectilePrefab != null)
                {
                    return ranged.projectilePrefab;
                }
            }

            // The spawner's own archetypes, which is where the Shooter lives.
            foreach (RangedBurst ranged in Resources.FindObjectsOfTypeAll<RangedBurst>())
            {
                if (ranged.projectilePrefab != null) return ranged.projectilePrefab;
            }

            Assert.Fail("no RangedBurst with a projectile prefab found");
            return null!;
        }
    }
}
