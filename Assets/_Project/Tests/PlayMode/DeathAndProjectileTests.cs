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
    /// A round, in a room, with a body in the way — asked from BOTH sides.
    ///
    /// The original bug froze a Shooter's projectile in mid-air permanently and
    /// never returned it to the pool, so a wave of shooters leaked the pool for
    /// the rest of the run. That rule ("a hostile round passes through hostiles")
    /// is now half of a general one: a round passes through its own SIDE and
    /// through its OWNER, and stops on everything else. The launcher depends on
    /// the other half — a player's rocket must detonate on the drone it hits, and
    /// must not detonate on the player who fired it — so both directions are
    /// asserted here rather than only the one that used to be broken.
    /// </summary>
    public sealed class ProjectilePassThroughTests
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
            Assert.IsTrue(instance.TryGetComponent(out Projectile projectile));

            float startZ = instance.CachedTransform.position.z;
            const float speed = 18f;
            projectile.Launch(new ProjectileShot
            {
                Pool = pool,
                Velocity = Vector3.forward * speed,
                Damage = 5f,
                Lifetime = 1.5f,
                HitMask = ~0,
                FiredBy = Faction.Hostile,
            });

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

        /// <summary>
        /// THE OTHER DIRECTION, and the one the launcher is built on. A round
        /// fired by the PLAYER must stop on a drone and damage it — the same
        /// sweep, the same prefab, the same drone, and the opposite answer.
        ///
        /// Without this, "passes through drones" would be a property of the
        /// projectile rather than of the side that fired it, and the first rocket
        /// would fly through the horde and detonate on the wall behind it.
        /// </summary>
        [UnityTest]
        public IEnumerator APlayerRound_StopsOnADrone_AndDamagesIt()
        {
            var pool = Object.FindFirstObjectByType<ObjectPool>();
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            Assert.IsNotNull(pool);
            Assert.IsNotNull(spawner);
            DroneConfig? drone = spawner!.DefaultDrone;
            Assert.IsNotNull(drone?.prefab);

            var origin = new Vector3(0f, 200f, 0f);
            _stage = Object.Instantiate(drone!.prefab!, origin + Vector3.forward * 4f, Quaternion.identity);
            yield return null;

            Health? body = _stage.GetComponent<Health>();
            Assert.IsNotNull(body, "the drone prefab must carry a Health for this test to mean anything");
            float before = body!.Current;

            PooledObject instance = pool!.Spawn(FindProjectilePrefab(), origin,
                Quaternion.LookRotation(Vector3.forward));
            Assert.IsTrue(instance.TryGetComponent(out Projectile projectile));
            projectile.Launch(new ProjectileShot
            {
                Pool = pool,
                Velocity = Vector3.forward * 18f,
                Damage = 7f,
                Lifetime = 1.5f,
                HitMask = ~0,
                FiredBy = Faction.Player,
            });

            // The drone is 4 m out at 18 m/s, so 0.6 s is nearly three times the
            // flight. Seconds, never frames — a -batchmode run is uncapped.
            float launchedAt = Time.time;
            while (Time.time - launchedAt < 0.6f && instance.IsSpawned) yield return null;

            Assert.IsFalse(instance.IsSpawned, "a player round must stop on a drone rather than pass through it");
            Assert.Less(body.Current, before,
                "a player round that stopped on a drone must have damaged it");
        }

        /// <summary>
        /// The OWNER rule, isolated from the faction rule.
        ///
        /// Fired as the PLAYER, so the faction test alone would let it stop on this
        /// drone — and it must not, because the drone is named as the shooter.
        /// This is the guard that stops a rocket detonating in the player's own
        /// face on the frame it leaves the tube, and testing it through a drone
        /// rather than the real player is deliberate: the player capsule sits at
        /// the arena origin, which is inside the centre bunker.
        /// </summary>
        [UnityTest]
        public IEnumerator ARound_PassesThroughItsOwner_EvenOnTheOtherSide()
        {
            var pool = Object.FindFirstObjectByType<ObjectPool>();
            var spawner = Object.FindFirstObjectByType<DroneSpawner>();
            Assert.IsNotNull(pool);
            Assert.IsNotNull(spawner);
            DroneConfig? drone = spawner!.DefaultDrone;
            Assert.IsNotNull(drone?.prefab);

            var origin = new Vector3(0f, 200f, 0f);
            _stage = Object.Instantiate(drone!.prefab!, origin + Vector3.forward * 4f, Quaternion.identity);
            yield return null;

            Health? owner = _stage.GetComponent<Health>();
            Assert.IsNotNull(owner);

            PooledObject instance = pool!.Spawn(FindProjectilePrefab(), origin,
                Quaternion.LookRotation(Vector3.forward));
            Assert.IsTrue(instance.TryGetComponent(out Projectile projectile));

            float startZ = instance.CachedTransform.position.z;
            projectile.Launch(new ProjectileShot
            {
                Pool = pool,
                Velocity = Vector3.forward * 18f,
                Damage = 7f,
                Lifetime = 1.5f,
                HitMask = ~0,
                FiredBy = Faction.Player,
                Owner = owner,
            });

            float launchedAt = Time.time;
            float furthest = startZ;
            while (Time.time - launchedAt < 0.4f)
            {
                if (instance.IsSpawned) furthest = Mathf.Max(furthest, instance.CachedTransform.position.z);
                yield return null;
            }

            Assert.Greater(furthest - startZ, 4.5f,
                "a round must pass through the body that fired it, whatever side that body is on");
            Assert.AreEqual(owner!.Max, owner.Current, 0.01f,
                "a round must never damage its own shooter");
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
