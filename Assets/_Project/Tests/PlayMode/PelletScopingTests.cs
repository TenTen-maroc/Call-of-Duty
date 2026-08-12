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
    /// What a trigger pull costs, and what a pellet costs.
    ///
    /// Both shipped weapons fire one pellet, which is why this was invisible for
    /// the whole life of the project: at pelletsPerShot 1, "per shot" and "per
    /// pellet" are the same object. The follow-up drain, the already-hit set and
    /// the follow-up hang guard all lived at the bottom of CastOneRay, so the
    /// moment anyone authored a twelve-pellet shotgun — and WeaponConfig carries a
    /// `// shotgun: 12` comment inviting exactly that — one trigger pull paid
    /// twelve times for one effect module, raised twelve hitmarkers, and lifted a
    /// 96-follow-up hang guard to 1152.
    ///
    /// PLAYMODE, not EditMode, and not by choice: WeaponController serialises
    /// PlayerInput, PlayerLook and PlayerMotor out of CoD.Player, which the
    /// EditMode assembly deliberately does not reference — and even with that
    /// reference, the fire path needs a physics scene to cast into, a real aim ray
    /// to cast along, and a live Health to damage. There is no honest way to
    /// assemble one of those outside the running arena.
    /// </summary>
    public sealed class PelletScopingTests
    {
        /// <summary>A shotgun's worth, and what every "twelve" in the messages below refers to.</summary>
        private const int PELLETS = 12;

        // These tests fire real rounds in the real arena, and the arena writes the
        // player's record when a run ends.
        private readonly SaveFileGuard _save = new();

        private WeaponController? _weapon;
        private PlayerLook? _look;
        private WeaponConfig? _single;
        private WeaponConfig? _shotgun;
        private BystanderStrike? _module;
        private GameObject? _target;
        private GameObject? _bystander;

        [UnitySetUp]
        public IEnumerator LoadGreyBoxAndBuildATestShotgun()
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

            _single = BuildTestWeapon("Test_OnePellet", 1);
            _shotgun = BuildTestWeapon("Test_TwelvePellet", PELLETS);
            _module = ScriptableObject.CreateInstance<BystanderStrike>();

            _target = SpawnTargetInTheAimRay(_weapon!, _look!, health: 5000f);
            _bystander = SpawnBystander(_look!);

            // A collider created this frame carries the pose its transform had
            // when AddComponent ran, but the physics scene has not necessarily
            // caught up: a raycast in the same frame can miss a target that is
            // visibly right there. Sync, then let one physics step run.
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator PutTheArenaBack()
        {
            if (_target != null) Object.Destroy(_target);
            if (_bystander != null) Object.Destroy(_bystander);
            _save.Restore();
            yield return null;

            // Destroyed after the last frame this fixture runs. The weapon is
            // still carrying the config, and a MonoBehaviour reaching into a
            // destroyed asset mid-Update is a failure about the test rather than
            // about the game.
            if (_single != null) Object.Destroy(_single);
            if (_shotgun != null) Object.Destroy(_shotgun);
            if (_module != null) Object.Destroy(_module);
        }

        /// <summary>
        /// The bug is one pull paying twelve times; the fix must not turn into one
        /// pull dealing one pellet of damage. Twelve pellets are twelve rays and
        /// twelve resolutions, and this is the test that says so.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryPelletStillLandsItsOwnDamage()
        {
            WeaponController weapon = _weapon!;
            Health target = TargetHealth();

            weapon.EquipWeapon(_single!);
            float onePellet = DamageFromOnePull(weapon, target);
            Assert.Greater(onePellet, 0f,
                "the test target took nothing — it is not in the aim ray, so nothing below this line means anything");
            yield return null;

            weapon.EquipWeapon(_shotgun!);
            float wholePull = DamageFromOnePull(weapon, target);

            // The already-hit set is now cleared once per pull instead of once per
            // pellet, which is why the hull/Core de-duplication inside ResolveHit
            // had to move to a set of its own. Share them and pellet two reads
            // pellet one's mark, passes straight through, and the shotgun fires a
            // single pellet.
            Assert.AreEqual(onePellet * PELLETS, wholePull, onePellet * 0.05f,
                $"{PELLETS} pellets must deal {PELLETS} pellets of damage");
            yield return null;
        }

        /// <summary>
        /// The headline. Explosive and Chain both work by asking the weapon "have
        /// you already hit this one?", and that set was wiped between pellets — so
        /// all twelve got a fresh "no" and one trigger pull put twelve blasts on
        /// the same bystander.
        /// </summary>
        [UnityTest]
        public IEnumerator OneTriggerPullPaysForItsEffectModuleOnce()
        {
            WeaponController weapon = _weapon!;
            BystanderStrike module = _module!;
            Health bystander = BystanderHealth();

            weapon.EquipWeapon(_shotgun!);
            module.bystander = bystander;
            Assert.IsTrue(weapon.AddEffectModule(module), "the module did not install");

            float before = bystander.Current;
            PullTheTrigger(weapon);

            // Every pellet still SEES the module — that is the damage arriving.
            // What stops is the module being PAID for each time. (Sandbox grants
            // one extra depth, which would let the follow-up hit resolve modules
            // too; the count is a floor for that reason, the payment below is not.)
            Assert.GreaterOrEqual(module.resolveCalls, PELLETS,
                "each pellet resolves its own hit, and every module sees every hit");
            Assert.AreEqual(1, module.queued,
                $"one pull, one payment — this was {PELLETS}, because the already-hit set was cleared between pellets");
            Assert.AreEqual(module.damage, before - bystander.Current, 0.01f,
                $"the bystander took {PELLETS} blasts from one trigger pull");
            yield return null;

            // ...and the scope really is the PULL. Cleared too rarely and a chain
            // stops working after the first shot of a magazine.
            before = bystander.Current;
            PullTheTrigger(weapon);
            Assert.AreEqual(2, module.queued, "the second pull must be allowed to pay again");
            Assert.AreEqual(module.damage, before - bystander.Current, 0.01f,
                "the second pull must land its own blast");
            yield return null;
        }

        /// <summary>
        /// Hitmarker does a PlayOneShot per Hit event. Twelve pellets meant twelve
        /// clicks stacked inside one frame under a single punch animation — and
        /// Hitmarker already carried a workaround for a plain pellet overwriting a
        /// sibling pellet's kill confirmation, which is this same bug seen from
        /// the far end.
        /// </summary>
        [UnityTest]
        public IEnumerator OneTriggerPullRaisesOneHitmarker()
        {
            WeaponController weapon = _weapon!;
            weapon.EquipWeapon(_shotgun!);

            int events = 0;
            bool killed = false;
            void Count(bool k)
            {
                events++;
                killed |= k;
            }

            weapon.Hit += Count;
            try
            {
                PullTheTrigger(weapon);
            }
            finally
            {
                weapon.Hit -= Count;
            }

            Assert.AreEqual(1, events, $"one target, one hit click — this was {PELLETS}");
            Assert.IsFalse(killed, "nothing died, so the pull must not report a kill");
            yield return null;
        }

        /// <summary>
        /// The kill confirm must SURVIVE the deduplication.
        ///
        /// This is the case that decides the whole rule. Suppressing repeat
        /// events per target is right for the twelve-clicks problem, but applied
        /// naively it eats the one event that matters: the first pellet connects
        /// and announces a plain hit, the ninth pellet kills, and — having
        /// already announced this target — says nothing. The shot that actually
        /// killed the drone would be the silent one, in a game whose own UI notes
        /// call the kill confirm the thing that makes clearing a wave legible
        /// without a single number on screen.
        ///
        /// So a kill always confirms. A body dies once, so there is no duplicate
        /// to suppress in the first place.
        /// </summary>
        [UnityTest]
        public IEnumerator AKillOnAnyPelletMakesTheWholePullAKill()
        {
            WeaponController weapon = _weapon!;
            Health target = TargetHealth();

            weapon.EquipWeapon(_single!);
            float onePellet = DamageFromOnePull(weapon, target);
            Assert.Greater(onePellet, 0f, "the test target is not in the aim ray");

            // Enough for two pellets and change, so the target dies partway
            // through the pull and the pellets after it find a corpse.
            target.ConfigureMax(onePellet * 2.5f);
            yield return null;

            weapon.EquipWeapon(_shotgun!);
            int events = 0;
            int kills = 0;
            bool killed = false;
            void Count(bool k)
            {
                events++;
                if (k) kills++;
                killed |= k;
            }

            weapon.Hit += Count;
            try
            {
                PullTheTrigger(weapon);
            }
            finally
            {
                weapon.Hit -= Count;
            }

            Assert.IsFalse(target.IsAlive, "three pellets of damage into two and a half pellets of health");
            Assert.IsTrue(killed, "the pellet that killed the target raised no kill confirm");
            Assert.AreEqual(1, kills, "a body dies once, so it must confirm exactly once");
            // One plain click for the pellet that connected first, one kill
            // confirm for the pellet that finished it. Never one per pellet.
            Assert.LessOrEqual(events, 2, $"at most one click plus one kill confirm — this was {PELLETS} pellets");
            Assert.GreaterOrEqual(events, 1, "the pull landed damage and said nothing at all");
            yield return null;
        }

        /// <summary>
        /// A module that declares OncePerPull is INVOKED once per pull, not once
        /// per pellet.
        ///
        /// Deduplicating the damage was never enough for Explosive. Its Resolve
        /// spawns the blast prefab — which carries its own sound — before any
        /// already-hit check, so a twelve-pellet weapon produced twelve stacked
        /// explosions and twelve stacked booms delivering exactly one
        /// explosion's worth of damage. Silent, invisible to every gate, and the
        /// loudest possible version of the bug the per-pull scoping was supposed
        /// to have fixed.
        ///
        /// The dedup lives in the controller rather than inside Explosive
        /// because modules are stateless ScriptableObjects shared by every
        /// weapon: a module cannot remember anything between two pellets.
        /// </summary>
        [UnityTest]
        public IEnumerator AOncePerPullModule_IsInvokedOncePerPull_NotOncePerPellet()
        {
            WeaponController weapon = _weapon!;
            Health bystander = BystanderHealth();

            BystanderStrike module = ScriptableObject.CreateInstance<BystanderStrike>();
            module.bystander = bystander;
            module.oncePerPull = true;
            Assert.IsTrue(weapon.AddEffectModule(module), "the module did not install");

            weapon.EquipWeapon(_shotgun!);
            PullTheTrigger(weapon);

            Assert.AreEqual(1, module.resolveCalls,
                $"a once-per-pull module ran {module.resolveCalls} times for {PELLETS} pellets — " +
                "this is twelve explosion VFX and twelve booms for one blast");
            Assert.AreEqual(1, module.queued, "and it must still do its job exactly once");

            // The claim is once per PULL, not once ever.
            PullTheTrigger(weapon);
            Assert.AreEqual(2, module.resolveCalls, "the next pull must be allowed to detonate again");
            yield return null;
        }

        /// <summary>
        /// And the default is unchanged: Pierce, Ricochet and Chain run per
        /// pellet on purpose. Stacking is the product, and a shotgun that chains
        /// from every pellet is the fantasy, not a bug — the damage is already
        /// deduplicated by the per-pull already-hit set.
        /// </summary>
        [UnityTest]
        public IEnumerator AnOrdinaryModule_StillRunsPerPellet()
        {
            WeaponController weapon = _weapon!;
            Health bystander = BystanderHealth();

            BystanderStrike module = ScriptableObject.CreateInstance<BystanderStrike>();
            module.bystander = bystander;
            module.oncePerPull = false;
            Assert.IsTrue(weapon.AddEffectModule(module), "the module did not install");

            weapon.EquipWeapon(_shotgun!);
            PullTheTrigger(weapon);

            Assert.GreaterOrEqual(module.resolveCalls, PELLETS,
                "the default contract is per hit, and a pellet is a hit");
            Assert.AreEqual(1, module.queued, "but it still only PAYS once");
            yield return null;
        }

        // ---------- fixture plumbing ----------

        /// <summary>
        /// Pulls the trigger once, right now.
        ///
        /// Reflection, deliberately. Firing is driven by PlayerInput reading the
        /// New Input System, and a headless test run has no device to press; the
        /// alternative was a public "fire for tests" entry point on the weapon,
        /// which is production API that exists for nobody and skips every gate
        /// TryFire enforces. FireOneShot is exactly where TryFire arrives once the
        /// gating has passed, and the assert below turns a rename into a loud
        /// failure rather than a test that quietly stops testing anything.
        /// </summary>
        private static void PullTheTrigger(WeaponController weapon)
        {
            MethodInfo? fire = typeof(WeaponController).GetMethod("FireOneShot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fire,
                "WeaponController.FireOneShot(float) is gone — this fixture drives the fire path through it");
            fire!.Invoke(weapon, new object[] { Time.time });
        }

        private static float DamageFromOnePull(WeaponController weapon, Health target)
        {
            float before = target.Current;
            PullTheTrigger(weapon);
            return before - target.Current;
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

        /// <summary>
        /// A weapon that differs from the shipped ones in exactly one thing that
        /// matters — the pellet count. The cone is zeroed so all twelve rays are
        /// the same ray and pellet scoping is the only variable left, and the
        /// recoil is zeroed so the second pull lands where the first one did.
        /// </summary>
        private static WeaponConfig BuildTestWeapon(string assetName, int pellets)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfig>();
            config.name = assetName;
            config.stableId = "wpn_test_pellet_scoping";
            config.displayName = assetName;
            config.weaponClass = WeaponClass.Shotgun;
            config.pelletsPerShot = pellets;

            config.baseSpread = 0f;
            config.spreadPerShot = 0f;
            config.maxSpread = 0f;
            config.verticalKickFirstShot = 0f;
            config.verticalKickAtShotEight = 0f;
            config.horizontalKickMax = 0f;
            config.cameraShakeAmplitude = 0f;

            // 25 damage at 700 RPM is the shipped rifle's TTK, which keeps this
            // config out of the window WeaponConfig warns about in the editor.
            config.bodyDamage = 25f;
            config.headshotMultiplier = 1f;
            config.roundsPerMinute = 700f;
            config.magazineSize = 500;
            config.reserveAmmo = 0;
            return config;
        }

        /// <summary>Parks a plain damageable box in the weapon's line, close enough that no arena geometry gets there first.</summary>
        private static GameObject SpawnTargetInTheAimRay(WeaponController weapon, PlayerLook look, float health)
        {
            Ray aim = look.AimRay;
            bool blocked = Physics.Raycast(aim.origin, aim.direction, out RaycastHit blocker, 100f,
                weapon.HitMask, QueryTriggerInteraction.Ignore);
            float clear = blocked ? blocker.distance : 100f;
            Assert.Greater(clear, 1.5f,
                "the player spawns with under 1.5 m of clear air ahead — this fixture has nowhere to put a target");

            var go = new GameObject("PelletScopingTarget");
            go.transform.position = aim.origin + aim.direction * Mathf.Min(3f, clear - 0.6f);
            go.transform.localScale = Vector3.one * 0.4f;
            // The same layer as the geometry the aim ray already reaches, so the
            // weapon's own hit mask is guaranteed to include it whatever the scene
            // sets that mask to.
            if (blocked) go.layer = blocker.collider.gameObject.layer;
            go.AddComponent<BoxCollider>();

            Health h = go.AddComponent<Health>();
            h.ConfigureMax(health);
            return go;
        }

        /// <summary>
        /// The module's victim, and deliberately COLLIDERLESS: nothing can find it
        /// with a ray or an overlap query, so the only thing that can damage it is
        /// a follow-up this fixture's own module queued. That is what makes "how
        /// many times did one pull pay?" a number rather than a guess.
        /// </summary>
        private static GameObject SpawnBystander(PlayerLook look)
        {
            var go = new GameObject("PelletScopingBystander");
            go.transform.position = look.AimRay.origin + Vector3.up * 50f;
            go.AddComponent<Health>().ConfigureMax(1000f);
            return go;
        }

        /// <summary>
        /// Stands in for Explosive and Chain, which both do exactly this: ask the
        /// weapon whether this pull has already claimed a target, claim it, and
        /// queue the damage. That question is the whole test — wipe the set
        /// between pellets and every pellet gets a fresh "no".
        ///
        /// It holds mutable fields, which the real modules must never do. It is
        /// created per test with CreateInstance and never written to disk, so
        /// there is no asset for a value to persist into.
        /// </summary>
        private sealed class BystanderStrike : EffectModule
        {
            public Health? bystander;
            public float damage = 7f;
            public int resolveCalls;
            public int queued;
            public bool oncePerPull;

            public override bool OncePerPull => oncePerPull;

            public override void Resolve(in HitContext context, FollowUpBuffer followUps)
            {
                resolveCalls++;

                Health? victim = bystander;
                if (victim == null || !victim.IsAlive) return;
                if (context.Shooter.HasHit(victim)) return;

                context.Shooter.MarkHit(victim);
                followUps.Enqueue(new FollowUp
                {
                    Kind = FollowUpKind.Damage,
                    Origin = context.Point,
                    Direction = context.Direction,
                    Damage = damage,
                    Target = victim,
                    Depth = context.Depth + 1,
                });
                queued++;
            }
        }
    }
}
