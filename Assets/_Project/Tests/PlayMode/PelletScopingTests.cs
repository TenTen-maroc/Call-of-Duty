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

        /// <summary>
        /// A stand-in for Fx_Tracer, built here rather than loaded: the shipped
        /// prefab is authored by VfxBuilder and a test that only passes once
        /// somebody has clicked a menu item is a test that gets deleted. What it
        /// shares with the real one is the only part the fire path cares about —
        /// a TrailRenderer, a Tracer, and a PooledObject.
        /// </summary>
        private GameObject? _tracerPrefab;

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
            _tracerPrefab = BuildTestTracerPrefab();

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
            if (_tracerPrefab != null) Object.Destroy(_tracerPrefab);
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

        /// <summary>
        /// A ROUND throws one tracer. Twelve pellets are how one round is
        /// modelled, not twelve rounds.
        ///
        /// This is the same class of bug the rest of this fixture covers, in the
        /// one system where it would have been the most visible: the follow-up
        /// drain, the already-hit set and the hitmarker all used to be scoped per
        /// pellet, and a tracer spawned from inside the ray cast would have been
        /// twelve glowing lines out of one barrel on every trigger pull —
        /// a shotgun that fires a searchlight.
        ///
        /// The second half is what proves the tracer flies to the IMPACT rather
        /// than straight through the arena. Its whole lifetime is fixed at launch
        /// from the distance it was handed, so the remaining budget is a
        /// measurement of that distance: a tracer aimed at a target three metres
        /// away must not be carrying the flight time of a 200 m maxRange miss.
        /// </summary>
        [UnityTest]
        public IEnumerator OneTriggerPullThrowsOneTracer_HoweverManyPelletsItFires()
        {
            WeaponController weapon = _weapon!;
            WeaponConfig shotgun = _shotgun!;
            shotgun.tracerPrefab = _tracerPrefab;
            shotgun.tracerEveryNRounds = 1;   // every round, so the count is the pellet count or 1

            weapon.EquipWeapon(shotgun);
            Assert.AreEqual(0, TracersInFlight(), "something was already in flight before the first pull");

            PullTheTrigger(weapon);

            Assert.AreEqual(1, TracersInFlight(),
                $"one round, one tracer — this was {PELLETS}, one per pellet. (Zero means the arena's " +
                "WeaponController has no _pool or no _muzzle wired, and the fire path never reached SpawnTracer.)");

            Tracer tracer = TheTracerInFlight();
            float budget = tracer.DespawnAt - Time.time;
            // maxRange is 200 m at the config default and tracerSpeed is 250 m/s,
            // so a tracer that ignored the resolved hit point would be carrying
            // 0.8 s of flight. The target sits three metres away.
            Assert.Less(budget, 0.5f,
                "the tracer is carrying a full-maxRange flight, so it is flying to the end of the aim ray " +
                "instead of to the point the round actually stopped at");
            Assert.Greater(budget, 0f, "the tracer retired before it left the barrel");
            yield return null;
        }

        /// <summary>
        /// EVERY THIRD ROUND, and every third round after that.
        ///
        /// A tracer on every round is a continuous ribbon of light out of the
        /// barrel: it reads as a laser rather than as gunfire, and it flattens
        /// the muzzle flash it is drawn over. The cadence is the feature, so it
        /// is asserted round by round rather than by counting at the end — an
        /// off-by-one that fires on rounds 3, 6, 9 instead of 1, 4, 7 produces
        /// the same total and a visibly different gun.
        ///
        /// Every pull happens inside ONE frame on purpose: nothing despawns
        /// without an Update, so the in-flight count is a running total and the
        /// test never has to wait on wall-clock time.
        /// </summary>
        [UnityTest]
        public IEnumerator ATracerIsEveryNthRound_NeverEveryRound()
        {
            const int everyN = 3;

            WeaponController weapon = _weapon!;
            WeaponConfig single = _single!;
            single.tracerPrefab = _tracerPrefab;
            single.tracerEveryNRounds = everyN;

            weapon.EquipWeapon(single);

            // Round 1 carries one: the first round of a run is the one that
            // tells the player where this gun shoots.
            int[] expected = { 1, 1, 1, 2, 2, 2, 3 };
            for (int round = 0; round < expected.Length; round++)
            {
                PullTheTrigger(weapon);
                Assert.AreEqual(expected[round], TracersInFlight(),
                    $"after {round + 1} round(s) at one tracer per {everyN}, the count is wrong — " +
                    "the cadence has slipped, which is a different-looking gun even though the total matches");
            }
            yield return null;
        }

        /// <summary>
        /// A tracer aimed at something absurdly far away must still die.
        ///
        /// Its lifetime is computed at launch from the distance it was handed,
        /// which is right until somebody authors a tracerSpeed near the bottom of
        /// its range and a maxRange near the top. A pooled instance that never
        /// retires is not a glitch, it is a leak: the pool hands out a fresh one
        /// on every third round for the rest of the run and the arena fills with
        /// stationary glowing lines. The ceiling is a hang guard in the same
        /// family as MAX_FOLLOW_UPS_PER_PULL.
        ///
        /// Asserted on the clock rather than by watching, because watching means
        /// waiting eight seconds of game time in a batch run that has no frame
        /// rate to speak of.
        /// </summary>
        [UnityTest]
        public IEnumerator ATracerCannotOutliveItsOwnFlight()
        {
            var pool = Object.FindFirstObjectByType<ObjectPool>();
            Assert.IsNotNull(pool, "no ObjectPool in the arena");

            PooledObject instance = pool!.Spawn(_tracerPrefab!, Vector3.zero, Quaternion.identity);
            Tracer tracer = instance.GetComponent<Tracer>();
            Assert.IsNotNull(tracer, "the test tracer prefab lost its Tracer component");

            // An honest shot: 200 m — the shipped maxRange — at the shipped speed.
            tracer.Launch(pool, Vector3.zero, Vector3.forward * 200f, 250f, 0.02f);
            Assert.IsTrue(tracer.InFlight, "Launch did not put it in flight");
            float honest = tracer.DespawnAt - Time.time;
            Assert.GreaterOrEqual(honest, 200f / 250f,
                "a tracer that retires before it arrives is a round that vanishes in mid-air");
            Assert.LessOrEqual(honest, 8f, "even the honest case has to sit under the ceiling");

            // And the mis-authored one: a hundred kilometres at the slowest speed
            // the config will accept is 2000 seconds of flight.
            tracer.Launch(pool, Vector3.zero, Vector3.forward * 100000f, 50f, 0.02f);
            float absurd = tracer.DespawnAt - Time.time;
            Assert.LessOrEqual(absurd, 8f,
                "the hard ceiling did not clamp — a mis-authored speed strands a pooled instance alive forever");

            pool.Despawn(instance);
            yield return null;
        }

        /// <summary>
        /// The surface a bullet gets is decided by the collider's LAYER, and a
        /// body is never architecture.
        ///
        /// Three separate claims, and every one of them was a silent wrong answer
        /// before the table existed. A layer a row claims wins outright — that is
        /// what makes gore level a data swap when a human-shaped target arrives,
        /// rather than a branch in the fire path. A layer NOTHING claims falls
        /// through to null so the caller can use the config's fallback, because a
        /// silent, invisible impact is indistinguishable from a missed shot. And
        /// a BODY on an unclaimed layer is metal rather than concrete: every
        /// enemy in this game is a machine and every one of them currently shares
        /// the Default layer with the walls, so a plain layer scan would puff
        /// masonry dust off a drone hull on every hit in the game.
        /// </summary>
        [UnityTest]
        public IEnumerator ImpactResponseIsKeyedOnLayer_AndABodyIsNeverConcrete()
        {
            const int concreteLayer = 0;    // Default, where the whole arena lives today
            const int metalLayer = 9;       // the first free user slot; see VfxBuilder
            const int unclaimedLayer = 5;   // UI, which no surface will ever claim

            var impact = ScriptableObject.CreateInstance<ImpactConfig>();
            try
            {
                impact.surfaces = new[]
                {
                    Row(SurfaceType.Concrete, concreteLayer),
                    Row(SurfaceType.Metal, metalLayer),
                    Row(SurfaceType.Flesh, 11),
                };

                ImpactConfig.SurfaceResponse? wall = impact.ResponseFor(concreteLayer, onBody: false);
                Assert.IsNotNull(wall, "the concrete row claims Default and did not answer for it");
                Assert.AreEqual(SurfaceType.Concrete, wall!.surface);

                ImpactConfig.SurfaceResponse? plate = impact.ResponseFor(metalLayer, onBody: false);
                Assert.IsNotNull(plate, "a claimed layer must resolve whether or not it is a body");
                Assert.AreEqual(SurfaceType.Metal, plate!.surface);

                Assert.IsNull(impact.ResponseFor(unclaimedLayer, onBody: false),
                    "an unclaimed layer must fall through to the config's fallback, not to an arbitrary row");

                ImpactConfig.SurfaceResponse? drone = impact.ResponseFor(concreteLayer, onBody: true);
                Assert.IsNotNull(drone, "a body on an unclaimed layer produced nothing at all");
                Assert.AreEqual(SurfaceType.Metal, drone!.surface,
                    "a drone hull on the arena's own layer resolved to CONCRETE — every hit on every enemy in " +
                    "the game would puff masonry dust off a machine");

                // ...and a body whose layer IS claimed still wins by layer. This
                // is the line that keeps gore a data question: the day a
                // human-shaped target exists it gets a flesh layer and the
                // machine fallback above never fires for it.
                ImpactConfig.SurfaceResponse? flesh = impact.ResponseFor(11, onBody: true);
                Assert.IsNotNull(flesh, "the flesh row claims layer 11 and did not answer for it");
                Assert.AreEqual(SurfaceType.Flesh, flesh!.surface,
                    "a claimed layer must beat the body fallback, or no target can ever be anything but metal");
            }
            finally
            {
                Object.Destroy(impact);
            }
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

        /// <summary>
        /// How many tracers are mid-flight right now.
        ///
        /// Counts the COMPONENT rather than the pool, because "in flight" is the
        /// question — a pooled instance that has already retired is inactive and
        /// says so. Inactive instances are included in the search on purpose:
        /// missing them would mean this returns the same answer whether the pool
        /// is reusing one instance or leaking a new one per shot.
        /// </summary>
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

        private static Tracer TheTracerInFlight()
        {
            Tracer[] all = Object.FindObjectsByType<Tracer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].InFlight) return all[i];
            }
            Assert.Fail("nothing is in flight");
            return all[0];
        }

        private static ImpactConfig.SurfaceResponse Row(SurfaceType surface, int layer) =>
            new() { surface = surface, layers = 1 << layer };

        /// <summary>
        /// The template the pool clones tracers from. Inactive, so its own Tracer
        /// never counts as one in flight, and given a short trail time so the
        /// lifetime arithmetic the tests read is dominated by the flight rather
        /// than by a five-second default fade.
        /// </summary>
        private static GameObject BuildTestTracerPrefab()
        {
            var go = new GameObject("PelletScopingTracerTemplate");
            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.05f;
            trail.emitting = false;
            trail.autodestruct = false;
            // PooledObject before Tracer: Tracer caches it in Awake, and Awake on
            // an already-active GameObject runs the instant AddComponent returns.
            go.AddComponent<PooledObject>();
            go.AddComponent<Tracer>();
            go.SetActive(false);
            return go;
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
