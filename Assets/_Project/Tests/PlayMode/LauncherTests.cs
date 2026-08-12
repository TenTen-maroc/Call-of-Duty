#nullable enable
using System.Collections;
using System.Reflection;
using CoD.Core;
using CoD.Player;
using CoD.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The launcher, fired in the real arena — the first weapon in this game
    /// whose shot does not resolve on the frame the trigger is pulled.
    ///
    /// WHAT MAKES A PROJECTILE WEAPON WORTH ITS OWN FIXTURE. Every other gun is a
    /// ray: the pull and the impact are the same instant, so anything true about
    /// one is true about the other. A rocket splits them apart by roughly a
    /// second, and everything the gun knew at the pull has to survive the gap.
    /// Three things can go wrong in that gap and NONE of them throws:
    ///
    /// 1. The round can resolve with the wrong weapon. Swap to the pistol while
    ///    a rocket is in the air and an impact that reads the CURRENT runtime
    ///    applies the pistol's damage, the pistol's falloff and the pistol's
    ///    effect modules to it. Nothing logs. See Projectile.Payload.
    /// 2. The round can resolve outside the weapon's damage model entirely — no
    ///    falloff, no stat sheet, no weakpoint, no effect modules — if the
    ///    projectile applies its own damage instead of handing the impact back.
    ///    A launcher whose blast never fires is a 100-damage rifle.
    /// 3. The round can detonate on the person who fired it.
    ///
    /// PLAYMODE for PelletScopingTests' reason: the fire path needs a physics
    /// scene to sweep through, a real aim ray to fly along, and a live Health to
    /// damage. It also fires through the SAME private FireOneShot that fixture
    /// drives, so what is tested is the shipping path rather than a test-only
    /// entry point on the weapon.
    /// </summary>
    public sealed class LauncherTests
    {
        /// <summary>Firing real rounds in the real arena can end a run, and a run that ends writes the record.</summary>
        private readonly SaveFileGuard _save = new();

        /// <summary>The launcher's authored damage, and the number every assertion below is written against.</summary>
        private const float ROCKET_DAMAGE = 100f;

        /// <summary>Wide enough to reach the player standing at the muzzle. That is the point — see TheBlast_ReachesNeighboursAndNeverTheShooter.</summary>
        private const float BLAST_RADIUS = 6f;

        private const float BLAST_FRACTION = 0.7f;

        private WeaponController? _weapon;
        private PlayerLook? _look;
        private WeaponConfig? _launcher;
        private WeaponConfig? _sidearm;
        private Explosive? _blast;
        private GameObject? _rocketPrefab;
        private GameObject? _tracerPrefab;
        private GameObject? _target;
        private GameObject? _bystander;

        [UnitySetUp]
        public IEnumerator LoadGreyBoxAndBuildATestLauncher()
        {
            _save.CaptureAndReset();

            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load, "'10_GreyBox' must be in the build settings — the builder registers it");
            while (load != null && !load.isDone) yield return null;
            yield return null;

            _weapon = Object.FindFirstObjectByType<WeaponController>();
            _look = Object.FindFirstObjectByType<PlayerLook>();
            Assert.IsNotNull(_weapon, "no WeaponController in the arena");
            Assert.IsNotNull(_look, "no PlayerLook — there is no aim ray to fire along");

            _rocketPrefab = BuildTestRocketPrefab();
            _tracerPrefab = BuildTestTracerPrefab();
            _blast = ScriptableObject.CreateInstance<Explosive>();
            _blast.maxDepth = 0;
            _blast.radius = BLAST_RADIUS;
            _blast.damageFraction = BLAST_FRACTION;
            _blast.minMultiplier = 1f;   // flat, so the arithmetic below is a number rather than a range
            _blast.explosionVfx = null;  // nothing to pool, and nothing to see in a headless run

            _launcher = BuildTestLauncher();
            _sidearm = BuildTestSidearm();

            _target = SpawnTargetInTheAimRay(_weapon!, _look!, health: 5000f);
            _bystander = SpawnBystanderBeside(_target!, health: 5000f);

            // A collider created this frame carries its transform's pose, but the
            // physics scene has not necessarily caught up — a sweep in the same
            // frame can miss a target that is visibly right there.
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator PutTheArenaBack()
        {
            if (_target != null) Object.Destroy(_target);
            if (_bystander != null) Object.Destroy(_bystander);
            if (_rocketPrefab != null) Object.Destroy(_rocketPrefab);
            if (_tracerPrefab != null) Object.Destroy(_tracerPrefab);
            _save.Restore();
            yield return null;

            // After the last frame this fixture runs: the weapon is still holding
            // the config, and a MonoBehaviour reaching into a destroyed asset
            // mid-Update is a failure about the test rather than about the game.
            if (_launcher != null) Object.Destroy(_launcher);
            if (_sidearm != null) Object.Destroy(_sidearm);
            if (_blast != null) Object.Destroy(_blast);
        }

        /// <summary>
        /// The defining property, and the one no hitscan weapon has: a pull puts
        /// exactly ONE round in the air and damages NOTHING yet.
        ///
        /// The second half is the real assertion. If the target were already hurt
        /// on the frame of the pull, delivery would be hitscan wearing a rocket's
        /// costume — and the travel time is the entire fairness argument for a
        /// weapon that one-shots.
        /// </summary>
        [UnityTest]
        public IEnumerator OnePull_PutsOneRoundInTheAir_AndDamagesNothingYet()
        {
            WeaponController weapon = _weapon!;
            Health target = TargetHealth();
            weapon.EquipWeapon(_launcher!);

            float before = target.Current;
            PullTheTrigger(weapon);

            Assert.AreEqual(1, RoundsInFlight(),
                "one trigger pull on a projectile weapon must produce exactly one round");
            Assert.AreEqual(before, target.Current, 0.01f,
                "a projectile weapon must not resolve its damage on the frame the trigger is pulled — " +
                "the travel time is the whole reason it is allowed to one-shot");
            yield return null;
        }

        /// <summary>
        /// The round arrives and is resolved BY THE WEAPON: full authored damage,
        /// and the instance back in the pool afterwards.
        ///
        /// The despawn half is not decoration. A round that resolves and does not
        /// retire holds a pooled instance forever, and the pool hands out a fresh
        /// one on the next shot — a leak of one instance per trigger pull.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRoundArrives_AndTheWeaponResolvesIt()
        {
            WeaponController weapon = _weapon!;
            Health target = TargetHealth();
            weapon.EquipWeapon(_launcher!);

            float before = target.Current;
            PullTheTrigger(weapon);
            yield return WaitForTheRoundToLand();

            Assert.AreEqual(0, RoundsInFlight(),
                "the round must return to the pool once it has resolved, not hang in the air");
            Assert.AreEqual(ROCKET_DAMAGE, before - target.Current, 1f,
                $"a direct hit at point blank must apply the launcher's authored {ROCKET_DAMAGE:F0} damage — " +
                "if this is short, the impact resolved outside the weapon's damage model");
        }

        /// <summary>
        /// THE TEST THIS WHOLE DESIGN EXISTS FOR. A rocket in flight OUTLIVES A
        /// WEAPON SWAP, and it must resolve with the weapon that fired it.
        ///
        /// The swap happens one frame after the pull, while the round is still
        /// travelling. Read the CURRENT runtime at impact and this rocket lands
        /// for the sidearm's 8 damage, with the sidearm's falloff and the
        /// sidearm's (empty) module list. Nothing throws, nothing logs, and the
        /// only evidence is a number nobody was watching — which is exactly why
        /// the config is carried on the round rather than looked up.
        /// </summary>
        [UnityTest]
        public IEnumerator ARoundInFlight_ResolvesWithTheWeaponThatFiredIt()
        {
            WeaponController weapon = _weapon!;
            Health target = TargetHealth();
            weapon.EquipWeapon(_launcher!);

            float before = target.Current;
            PullTheTrigger(weapon);
            Assert.AreEqual(1, RoundsInFlight(), "the round has to still be in the air for this test to mean anything");

            // The swap, mid-flight. EquipWeapon is the shipping call — it is what
            // the shop uses and what the sandbox console's weapon cycle uses.
            weapon.EquipWeapon(_sidearm!);
            yield return WaitForTheRoundToLand();

            float applied = before - target.Current;
            Assert.AreEqual(ROCKET_DAMAGE, applied, 1f,
                $"the round landed for {applied:F1} damage after a swap to a {_sidearm!.bodyDamage:F0}-damage " +
                "sidearm — it resolved with the weapon currently held rather than the one that fired it");
        }

        /// <summary>
        /// The effect modules run, from the impact of a round that landed frames
        /// after the pull — and the shooter is never a victim of their own blast.
        ///
        /// This is the half of the design the plan called "the one genuinely
        /// invasive part": a launcher must still run effect modules, so a
        /// projectile's impact has to reach the same ResolveHit a ray does. If it
        /// did not, a launcher would be a 100-damage rifle with a sound effect.
        ///
        /// The blast radius is deliberately wide enough to cover the player, and
        /// the test asserts they are INSIDE it before asserting they took nothing.
        /// Without that first assertion the second passes for the wrong reason the
        /// day somebody narrows the radius.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBlast_ReachesNeighboursAndNeverTheShooter()
        {
            WeaponController weapon = _weapon!;
            Health target = TargetHealth();
            Health bystander = BystanderHealth();
            Health shooter = ShooterHealth();

            weapon.EquipWeapon(_launcher!);
            float shooterBefore = shooter.Current;
            float bystanderBefore = bystander.Current;

            PullTheTrigger(weapon);
            yield return WaitForTheRoundToLand();

            Assert.Less(bystander.Current, bystanderBefore,
                "the blast must reach a body beside the one the round hit — if it did not, the effect modules " +
                "never ran, and a launcher is a rifle that makes a louder noise");
            Assert.AreEqual(ROCKET_DAMAGE * BLAST_FRACTION, bystanderBefore - bystander.Current, 2f,
                "the blast must be a fraction of the round that caused it, so it scales with the weapon");

            // Non-vacuity first: the shooter has to be somewhere the blast could
            // have reached, or "took no damage" says nothing at all.
            float distance = Vector3.Distance(shooter.transform.position, target.transform.position);
            Assert.Less(distance, BLAST_RADIUS,
                $"the shooter is {distance:F1} m from the impact and the blast is {BLAST_RADIUS:F1} m — " +
                "this fixture is no longer testing self-damage at all");
            Assert.AreEqual(shooterBefore, shooter.Current, 0.01f,
                "a launcher must never blow up the person holding it");
        }

        /// <summary>
        /// A projectile weapon throws no tracer, even when one is authored on it.
        ///
        /// VfxBuilder stamps `tracerPrefab` onto every WeaponConfig on disk, so
        /// the launcher HAS one — and a tracer here would draw a second, instant
        /// streak to a point no ray was ever cast at (`_tracerEnd` is only
        /// resolved by a hitscan pull, so it still holds the far end of the aim
        /// ray) while the round that matters is a metre out of the tube. The
        /// suppression is a line in WeaponController; this is what keeps it.
        /// </summary>
        [UnityTest]
        public IEnumerator AProjectileWeapon_ThrowsNoTracer()
        {
            WeaponController weapon = _weapon!;
            _launcher!.tracerPrefab = _tracerPrefab;
            weapon.EquipWeapon(_launcher);

            PullTheTrigger(weapon);
            yield return null;

            Assert.AreEqual(0, TracersInFlight(),
                "a projectile weapon must not also throw a hitscan tracer — the round IS the visible line, and " +
                "the tracer would fly to a point nothing was ever cast at");
        }

        // ---------- fixture plumbing ----------

        /// <summary>
        /// Pulls the trigger once, right now. Reflection for PelletScopingTests'
        /// reason: a headless run has no device to press, and a public
        /// "fire for tests" entry point is production API that exists for nobody
        /// and skips every gate TryFire enforces.
        /// </summary>
        private static void PullTheTrigger(WeaponController weapon)
        {
            MethodInfo? fire = typeof(WeaponController).GetMethod("FireOneShot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fire,
                "WeaponController.FireOneShot(float) is gone — this fixture drives the fire path through it");
            fire!.Invoke(weapon, new object[] { Time.time });
        }

        /// <summary>
        /// Waits for the round, in SECONDS rather than frames — a -batchmode run
        /// is uncapped and pushes thousands of frames a second, so a frame count
        /// is a few milliseconds of game time and the round has barely left the
        /// tube. The bound is far longer than the flight so a failure reads as
        /// "it never landed" rather than as a race.
        /// </summary>
        private static IEnumerator WaitForTheRoundToLand()
        {
            float firedAt = Time.time;
            while (Time.time - firedAt < 1.5f && RoundsInFlight() > 0) yield return null;
            // One more frame, so a follow-up queued by the impact has been drained
            // before anything is measured. (DrainFollowUps runs inside the impact
            // itself; this is belt and braces against a future reorder.)
            yield return null;
        }

        /// <summary>
        /// How many of OUR rounds are in the air.
        ///
        /// Filtered on Payload, and that is not fussiness: the arena is live
        /// during these tests, a wave is running, and a Shooter drone puts the
        /// same Projectile component in the air. A drone's round carries no
        /// payload; a weapon's always does.
        /// </summary>
        private static int RoundsInFlight()
        {
            Projectile[] all = Object.FindObjectsByType<Projectile>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].InFlight && all[i].Payload != null) count++;
            }
            return count;
        }

        private static int TracersInFlight()
        {
            Tracer[] all = Object.FindObjectsByType<Tracer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].InFlight) count++;
            }
            return count;
        }

        private Health TargetHealth()
        {
            Health? health = _target != null ? _target.GetComponent<Health>() : null;
            Assert.IsNotNull(health, "the test target lost its Health");
            return health!;
        }

        private Health BystanderHealth()
        {
            Health? health = _bystander != null ? _bystander.GetComponent<Health>() : null;
            Assert.IsNotNull(health, "the bystander lost its Health");
            return health!;
        }

        private static Health ShooterHealth()
        {
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.IsNotNull(motor, "no PlayerMotor in the arena");
            Health? health = motor!.GetComponent<Health>();
            Assert.IsNotNull(health, "the player has no Health");
            return health!;
        }

        /// <summary>
        /// A launcher built here rather than loaded from disk, for
        /// PelletScopingTests' reason: RL_Launcher.asset is written by
        /// ArsenalBuilder, and a test that only passes once somebody has clicked a
        /// menu item is a test that gets deleted. What it shares with the shipped
        /// asset is everything the fire path reads.
        ///
        /// Recoil and bloom are zeroed so the round flies exactly down the aim ray
        /// and the target is where the fixture put it. Falloff starts at 30 m and
        /// the target is under 3 m out, so `DamageAtDistance` returns the authored
        /// number and the assertions can be exact.
        /// </summary>
        private WeaponConfig BuildTestLauncher()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfig>();
            config.name = "Test_Launcher";
            config.stableId = "wpn_test_launcher";
            config.displayName = "Test Launcher";
            config.weaponClass = WeaponClass.Launcher;
            config.fireMode = FireMode.Single;

            config.delivery = DeliveryMode.Projectile;
            config.projectilePrefab = _rocketPrefab;
            config.projectileSpeed = 34f;
            config.projectileLifetime = 6f;
            config.projectileSpawnOffset = 0.9f;

            config.bodyDamage = ROCKET_DAMAGE;
            config.headshotMultiplier = 1f;
            config.falloffRange = new Vector2(30f, 80f);
            config.minDamageMultiplier = 0.85f;
            config.roundsPerMinute = 55f;
            config.magazineSize = 500;   // the fixture pulls the trigger directly; ammo must never be the gate
            config.reserveAmmo = 500;
            config.adsTime = 0.45f;

            config.baseSpread = 0f;
            config.spreadPerShot = 0f;
            config.maxSpread = 0f;
            config.pelletSpreadDegrees = 0f;
            config.verticalKickFirstShot = 0f;
            config.verticalKickAtShotEight = 0f;
            config.horizontalKickMax = 0f;
            config.cameraShakeAmplitude = 0f;

            config.effectModules = new EffectModule[] { _blast! };
            return config;
        }

        /// <summary>
        /// The weapon swapped TO mid-flight. Its damage is nothing like the
        /// launcher's on purpose — that difference is the whole signal in
        /// ARoundInFlight_ResolvesWithTheWeaponThatFiredIt.
        /// </summary>
        private static WeaponConfig BuildTestSidearm()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfig>();
            config.name = "Test_Sidearm";
            config.stableId = "wpn_test_sidearm";
            config.displayName = "Test Sidearm";
            config.weaponClass = WeaponClass.Pistol;
            config.fireMode = FireMode.Single;
            config.delivery = DeliveryMode.Hitscan;
            config.bodyDamage = 8f;
            config.headshotMultiplier = 1f;
            config.roundsPerMinute = 400f;
            config.magazineSize = 500;
            config.reserveAmmo = 500;
            config.baseSpread = 0f;
            config.spreadPerShot = 0f;
            config.maxSpread = 0f;
            return config;
        }

        /// <summary>
        /// The pooled round. Inactive, so its own Projectile never counts as one
        /// in flight, and colliderless because Projectile sweeps a ray between
        /// frames rather than relying on a trigger.
        ///
        /// PooledObject is added BEFORE Projectile: Projectile caches it in Awake,
        /// and Awake on an already-active GameObject runs the instant AddComponent
        /// returns.
        /// </summary>
        private static GameObject BuildTestRocketPrefab()
        {
            var go = new GameObject("LauncherTestRocket");
            go.AddComponent<PooledObject>();
            go.AddComponent<Projectile>();
            go.SetActive(false);
            return go;
        }

        /// <summary>See BuildTestRocketPrefab. A tracer only this fixture's launcher points at.</summary>
        private static GameObject BuildTestTracerPrefab()
        {
            var go = new GameObject("LauncherTestTracer");
            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.05f;
            trail.emitting = false;
            trail.autodestruct = false;
            go.AddComponent<PooledObject>();
            go.AddComponent<Tracer>();
            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// A plain damageable box in the weapon's line, close enough that no arena
        /// geometry gets there first — and far enough out that the round is
        /// genuinely in flight for a frame or two.
        /// </summary>
        private static GameObject SpawnTargetInTheAimRay(WeaponController weapon, PlayerLook look, float health)
        {
            Ray aim = look.AimRay;
            bool blocked = Physics.Raycast(aim.origin, aim.direction, out RaycastHit blocker, 100f,
                weapon.HitMask, QueryTriggerInteraction.Ignore);
            float clear = blocked ? blocker.distance : 100f;
            Assert.Greater(clear, 2.5f,
                "the player spawns with under 2.5 m of clear air ahead — a rocket has nowhere to fly");

            var go = new GameObject("LauncherTestTarget");
            go.transform.position = aim.origin + aim.direction * Mathf.Min(4f, clear - 0.6f);
            go.transform.localScale = Vector3.one * 0.6f;
            // The same layer as the geometry the aim ray already reaches, so the
            // weapon's hit mask is guaranteed to include it whatever the scene
            // sets that mask to.
            if (blocked) go.layer = blocker.collider.gameObject.layer;
            go.AddComponent<BoxCollider>();

            Health h = go.AddComponent<Health>();
            h.ConfigureMax(health);
            return go;
        }

        /// <summary>
        /// A second body beside the target, in the blast but NOT in the round's
        /// path — offset sideways rather than behind, so nothing it takes can have
        /// come from the direct hit.
        /// </summary>
        private static GameObject SpawnBystanderBeside(GameObject target, float health)
        {
            var go = new GameObject("LauncherTestBystander");
            go.transform.position = target.transform.position + Vector3.up * 1.6f;
            go.transform.localScale = Vector3.one * 0.4f;
            go.layer = target.layer;
            go.AddComponent<BoxCollider>();

            Health h = go.AddComponent<Health>();
            h.ConfigureMax(health);
            return go;
        }
    }
}
