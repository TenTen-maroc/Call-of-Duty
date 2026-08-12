#nullable enable
using System;
using System.Collections.Generic;
using CoD.Core;
using CoD.Player;
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Fires the gun. Owns no tuning values of its own — every number is read
    /// from WeaponConfig, and every mutable value lives in WeaponRuntime.
    ///
    /// The whole first milestone is judged on how this feels, so the order of
    /// operations matters: input, then gating (sprint-to-fire, reload, cadence),
    /// then the shot, then the feedback. Feedback is not decoration; it is half
    /// the product.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private PlayerLoadoutConfig? _loadout = null;
        [SerializeField] private ImpactConfig? _impact = null;
        [Tooltip("Optional. Supplies the DamageMult and ReloadSpeed passives; the weapon works without it.")]
        [SerializeField] private RunContext? _run = null;

        [Header("Wiring")]
        [SerializeField] private PlayerInput? _input = null;
        [SerializeField] private PlayerLook? _look = null;
        [SerializeField] private PlayerMotor? _motor = null;
        [SerializeField] private ObjectPool? _pool = null;
        [SerializeField] private CameraShake? _shake = null;
        [SerializeField] private WeaponSway? _sway = null;
        [Tooltip("Where the muzzle flash and casings spawn from.")]
        [SerializeField] private Transform? _muzzle = null;
        [SerializeField] private Transform? _casingEject = null;
        [SerializeField] private Light? _muzzleLight = null;
        [Tooltip("The second muzzle light, on the Viewmodel layer. A camera culls lights by layer, so the world light cannot reach the gun and this one cannot reach the room.")]
        [SerializeField] private Light? _viewmodelMuzzleLight = null;
        [SerializeField] private AudioSource? _audioClose = null;
        [SerializeField] private AudioSource? _audioTail = null;
        [Tooltip("Moved to the impact point before every surface hit, so a round into the far wall sounds like it is over there. Optional: without it the impact plays on the close layer, at the gun, which is wrong-but-audible rather than silent.")]
        [SerializeField] private AudioSource? _audioImpact = null;
        [Tooltip("The shooter's own Health, so effect modules never damage the player who fired.")]
        [SerializeField] private Health? _ownerHealth = null;
        [Tooltip("What bullets can hit. Leave the player's own layer out of this.")]
        [SerializeField] private LayerMask _hitMask = Physics.DefaultRaycastLayers;

        private WeaponRuntime? _runtime;
        private float _adsProgress;
        private float _sprintReleasedAt = -99f;
        private float _fovKickUntil;
        private float _muzzleLightUntil;
        private bool _wasSprinting;
        private float _statDamageMultiplier = 1f;
        private float _statReloadSpeed = 1f;

        /// <summary>
        /// Cached in Awake. The impact source is REPOSITIONED per hit, and
        /// `transform` is a property call into native code on a path that runs
        /// once per pellet.
        /// </summary>
        private Transform? _audioImpactTransform;

        /// <summary>
        /// Where the round STOPPED — the point a tracer flies to.
        ///
        /// Recorded during the cast and consumed afterwards, because the tracer
        /// is spawned from SpawnMuzzleEffects and by then the rays have already
        /// resolved. Seeded every pull with the far end of the aim ray, so a shot
        /// into open air still throws a tracer that goes somewhere: a round that
        /// misses and produces nothing is the exact feedback hole this work
        /// exists to close.
        /// </summary>
        private Vector3 _tracerEnd;
        private bool _tracerEndResolved;

        /// <summary>
        /// Rounds still to fire before the next tracer, and before the next
        /// smoke puff. Counters rather than `shotCount % n`, so the very first
        /// round of a run carries a tracer and no counter can overflow into a
        /// negative modulo after a long session.
        /// </summary>
        private int _roundsUntilTracer;
        private int _roundsUntilSmoke;

        /// <summary>
        /// Set once, the first time a tracer prefab turns out to carry no Tracer
        /// component. Without the latch the error would be logged on every third
        /// round for the rest of the run, which is how a console stops being read.
        /// </summary>
        private bool _tracerPrefabReported;

        // Pre-sized buffer: RaycastNonAlloc never allocates, which matters once
        // hundreds of shots per minute are flying.
        //
        // Sized against the WORST authored case, not the typical one. A fully
        // authored Pierce allows 9 targets, every drone puts two colliders on the
        // line (hull plus its weakpoint Core), and the wall behind them needs a
        // slot too — 19 minimum. RaycastNonAlloc does not return the nearest hits
        // when it overflows, it returns an arbitrary subset and no overflow
        // signal, so a short buffer does not clip the far end of the line: it
        // silently drops the wall and lets the round pass through the arena.
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        // Effect-module scratch, all owned here rather than by the modules:
        // modules are shared assets, so a buffer on one would be written by every
        // weapon carrying it.
        private readonly FollowUpBuffer _followUps = new(64);

        /// <summary>
        /// Extra resolution depth granted in Sandbox. Zero in a Run, always.
        ///
        /// MAX_FOLLOW_UPS_PER_PULL and the fixed-capacity buffer above are
        /// untouched on purpose: they are the hard backstop that makes deeper
        /// recursion a bigger effect rather than a frame-rate event, and the whole
        /// reason it is safe to let Sandbox off the leash at all.
        /// </summary>
        private int _extraEffectDepth;

        /// <summary>
        /// Every body this TRIGGER PULL has already paid for — the set modules
        /// read through HasHit/MarkHit. One pull, one payment per target, however
        /// many pellets and follow-ups reach it.
        /// </summary>
        private readonly List<Health> _alreadyHit = new(24);

        /// <summary>
        /// Bodies THIS ONE RAY has already resolved. Deliberately not the set
        /// above, and the distinction is what keeps a shotgun a shotgun: this one
        /// exists only to skip the SECOND collider of a body the same ray already
        /// went through (every drone puts two on the line — the hull, which
        /// carries the Health, and the small `Core` child whose Weakpoint relays
        /// to it). Read the per-pull set here instead and pellet two would find
        /// pellet one's mark and pass straight through, so twelve pellets would
        /// deal one pellet of damage.
        /// </summary>
        private readonly List<Health> _piercedThisRay = new(12);

        /// <summary>
        /// Targets whose FIRST contact this pull has already been announced.
        ///
        /// Not the same question as _alreadyHit, which is about PAYMENT and gets
        /// marked speculatively by Explosive and Chain at queue time. This one is
        /// about the hitmarker, and the difference is audible: one pull that puts
        /// twelve pellets into one drone is one hit, but a pull that kills a drone
        /// directly and a second one through a chain is TWO kills and must sound
        /// like two. Collapsing to a single event per pull got the first case
        /// right and silently broke the second on the shipped rifle, where
        /// pierce, chain, ricochet and explosive are all live shop items today.
        /// </summary>
        private readonly List<Health> _announcedThisPull = new(24);

        /// <summary>
        /// Modules that declare OncePerPull and have already had their turn.
        /// A List rather than a set because a weapon carries at most a handful of
        /// modules and Contains over four references costs nothing.
        /// </summary>
        private readonly List<EffectModule> _oncePerPullSpent = new(4);

        // 24 covered about twelve drones: every drone puts TWO colliders in an
        // overlap (hull and weakpoint Core) and only the hull carries Health.
        // OverlapSphere fills a full buffer with an arbitrary subset and reports
        // no overflow, so a blast or a chain in a real crowd silently stopped
        // finding targets it was standing next to.
        private readonly Collider[] _effectOverlap = new Collider[64];

        /// <summary>
        /// Hard stop on follow-up work per TRIGGER PULL. Not a tuning value — a
        /// hang guard. The depth rules are the real limit; this is what catches a
        /// mis-authored module before it freezes a frame.
        ///
        /// Named for the pull rather than the shot because it used to be neither:
        /// the drain lived inside CastOneRay, so the guard was per PELLET and a
        /// twelve-pellet weapon raised the real ceiling to 1152 — the exact frame
        /// freeze the number exists to prevent.
        /// </summary>
        private const int MAX_FOLLOW_UPS_PER_PULL = 96;

        /// <summary>Fired once per trigger pull that landed damage; the bool is true when any of it killed. The UI listens, and the weapon never learns the UI exists.</summary>
        public event Action<bool>? Hit;
        public event Action? Fired;

        public WeaponRuntime? Runtime => _runtime;
        public float AdsProgress => _adsProgress;

        /// <summary>
        /// The cone actually used for the next shot, movement and stance included.
        /// Public so the crosshair can show bloom — otherwise accuracy degrades
        /// invisibly and the player has no idea why they are missing.
        /// </summary>
        public float EffectiveSpreadDegrees => CurrentSpreadDegrees();
        public bool IsAiming => _adsProgress > 0.5f;

        /// <summary>Sandbox cheats flip these. The console that sets them is dev-build gated.</summary>
        public bool InfiniteAmmo { get; set; }
        public float DamageMultiplier { get; set; } = 1f;

        // ---------- what effect modules are allowed to touch ----------

        public ObjectPool? EffectPool => _pool;
        public LayerMask HitMask => _hitMask;
        /// <summary>Shared scratch for module overlap queries. Contents are only valid inside one Resolve call.</summary>
        public Collider[] EffectOverlapBuffer => _effectOverlap;
        /// <summary>The shooter's own Health, so modules never damage the player who fired.</summary>
        public Health? OwnerHealth => _ownerHealth;

        /// <summary>True when this TRIGGER PULL has already damaged that target. The reason a chain cannot bounce between two drones forever.</summary>
        public bool HasHit(Health health) => _alreadyHit.Contains(health);

        public void MarkHit(Health health)
        {
            if (_alreadyHit.Contains(health)) return;
            _alreadyHit.Add(health);
        }

        /// <summary>
        /// Bought in the shop. The module list lives on the RUNTIME, never on the
        /// WeaponConfig asset — appending to the asset would edit authored data
        /// that persists between Play sessions.
        ///
        /// Returns false when the module could not be installed, so the shop can
        /// refund instead of charging for nothing. It used to return void and
        /// silently no-op on a null runtime — the player paid, heard the buy
        /// chime, watched the offer disappear, and received no module.
        /// </summary>
        public bool AddEffectModule(EffectModule module)
        {
            if (_runtime == null || module == null) return false;
            _runtime.Modules.Add(module);
            return true;
        }

        /// <summary>
        /// Swaps the carried weapon at runtime, keeping every effect module the
        /// player has bought. Modules are ammunition tech, not part of the gun:
        /// buying Explosive Rounds and then a new rifle must never silently throw
        /// the purchase away.
        ///
        /// This is the whole "new weapons are DATA" claim in one method — a second
        /// weapon is a WeaponConfig asset and nothing else.
        /// </summary>
        public bool EquipWeapon(WeaponConfig config)
        {
            if (config == null) return false;

            var next = new WeaponRuntime(config);
            if (_runtime != null)
            {
                for (int i = 0; i < _runtime.Modules.Count; i++)
                {
                    EffectModule module = _runtime.Modules[i];
                    if (module != null && !next.Modules.Contains(module)) next.Modules.Add(module);
                }
            }

            _runtime = next;
            _adsProgress = 0f;
            GameLog.Info($"equipped {config.displayName}", this);
            return true;
        }

        /// <summary>
        /// Tops the held weapon's reserve up by a fraction of its full reserve.
        ///
        /// Returns false when there is nothing to add, so the shop can refuse the
        /// sale rather than take money for nothing. The fraction is of the CONFIG's
        /// reserve, not of what is left, so one consumable asset stays correct
        /// across both weapons and any future one.
        /// </summary>
        public bool RefillReserve(float fraction)
        {
            if (_runtime == null || fraction <= 0f) return false;

            int full = _runtime.Config.reserveAmmo;
            if (full <= 0 || _runtime.ReserveAmmo >= full) return false;

            // At least one round: a fraction that rounds to zero would charge the
            // player for a purchase that changed nothing.
            int added = Mathf.Max(1, Mathf.RoundToInt(full * fraction));
            _runtime.ReserveAmmo = Mathf.Min(full, _runtime.ReserveAmmo + added);
            return true;
        }

        public int EffectModuleCount => _runtime != null ? _runtime.Modules.Count : 0;
        public EffectModule? EffectModuleAt(int index) =>
            _runtime != null && index >= 0 && index < _runtime.Modules.Count ? _runtime.Modules[index] : null;

        private void Awake()
        {
            WeaponConfig? config = _loadout != null ? _loadout.startingWeapon : null;
            if (config == null)
            {
                GameLog.Error("WeaponController has no starting weapon assigned.", this);
                return;
            }
            _runtime = new WeaponRuntime(config);
            if (_muzzleLight != null) _muzzleLight.enabled = false;
            if (_viewmodelMuzzleLight != null) _viewmodelMuzzleLight.enabled = false;
            if (_audioImpact != null) _audioImpactTransform = _audioImpact.transform;
        }

        private void Start()
        {
            // Start, not Awake: RunContext resolves the save file in ITS Awake,
            // and Mode is read from that save. Reading it a frame earlier would
            // depend on script execution order, which is undefined.
            GameConfig? config = _run != null ? _run.Config : null;
            if (_run == null || config == null) return;
            _extraEffectDepth = _run.Mode == GameMode.Sandbox
                ? Mathf.Max(0, config.sandboxExtraEffectDepth)
                : 0;
        }

        private void OnEnable()
        {
            // Cached on change rather than read per shot: the sheet only moves
            // when something is bought, and that is once per shop break.
            if (_run == null) return;
            _run.StatsChanged += OnStatsChanged;
            OnStatsChanged(_run.Stats);
        }

        private void OnDisable()
        {
            if (_run != null) _run.StatsChanged -= OnStatsChanged;
        }

        private void OnStatsChanged(StatSheet stats)
        {
            _statDamageMultiplier = stats.Effective(Stat.DamageMult, 1f);
            _statReloadSpeed = stats.Effective(Stat.ReloadSpeed, 1f);
        }

        private void Update()
        {
            if (_runtime == null || _input == null || _look == null) return;

            float now = Time.time;
            float deltaTime = Time.deltaTime;

            TrackSprintRelease(now);
            UpdateReload(now);
            UpdateAds(deltaTime);
            UpdateRecoilRecovery(now, deltaTime);
            _runtime.DecaySpread(now, deltaTime);
            UpdateMuzzleLight(now);
            UpdateFovOffset(now);

            if (_input.ReloadPressed) TryBeginReload(now);
            if (WantsToFire()) TryFire(now);
        }

        private bool WantsToFire()
        {
            if (_input == null || _runtime == null) return false;
            return _runtime.Config.fireMode switch
            {
                FireMode.FullAuto => _input.FireHeld,
                // A started burst finishes itself; the trigger only starts one.
                FireMode.Burst => _input.FirePressedThisFrame || _runtime.BurstShotsRemaining > 0,
                _ => _input.FirePressedThisFrame,
            };
        }

        private void TrackSprintRelease(float now)
        {
            if (_motor == null) return;
            if (_wasSprinting && !_motor.IsSprinting) _sprintReleasedAt = now;
            if (!_wasSprinting && _motor.IsSprinting && _runtime != null)
            {
                // Sprinting abandons a queued burst rather than parking it.
                _runtime.BurstShotsRemaining = 0;
            }
            _wasSprinting = _motor.IsSprinting;
        }

        private void TryFire(float now)
        {
            if (_runtime == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            // A corpse does not shoot. The menu blocks the action map on death,
            // so this is the second lock rather than the first — but the cheat
            // console and any future non-input firing path go through here too,
            // and the death screen is not a place to keep playing from.
            if (_ownerHealth != null && !_ownerHealth.IsAlive) return;

            // Sprint-to-fire: the gap between releasing sprint and being able to
            // shoot. Too short and the game becomes a sprint-around-corners
            // festival; 150-250 ms is the arcade sweet spot.
            if (_motor != null && _motor.IsSprinting) return;
            if (now - _sprintReleasedAt < config.sprintToFireTime) return;

            if (_runtime.IsReloading)
            {
                // Never cancel a reload the magazine needs: with an empty mag the
                // "cancel" costs the reload and gains nothing, and holding the
                // trigger would otherwise re-cancel the auto-reload every frame —
                // an empty gun that never reloads while the player holds fire.
                if (_runtime.IsMagazineEmpty) return;

                // Cancelling takes a FRESH pull, not a held trigger. Update starts
                // the reload and then reaches here in the same frame, so with
                // full-auto held the cancel fired at elapsed = 0 — far below the
                // commit point — and destroyed the reload on the frame it began.
                // Tapping R with the trigger down did nothing at all, every time,
                // and the gun could only ever be reloaded by running it dry.
                if (_input == null || !_input.FirePressedThisFrame) return;
                if (!_runtime.TryCancelReload(now)) return;
            }

            if (now < _runtime.NextShotAllowedAt) return;

            if (_runtime.IsMagazineEmpty)
            {
                if (!InfiniteAmmo)
                {
                    PlayDryFire(now);
                    return;
                }
                _runtime.CurrentAmmo = config.magazineSize;
            }

            // A press starts a burst; the remaining shots run on cadence alone.
            if (config.fireMode == FireMode.Burst && _runtime.BurstShotsRemaining <= 0)
            {
                if (_input == null || !_input.FirePressedThisFrame) return;
                _runtime.BurstShotsRemaining = config.burstCount;
            }

            FireOneShot(now);
        }

        private void FireOneShot(float now)
        {
            if (_runtime == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            int shotIndex = _runtime.ShotsInBurst;
            _runtime.ConsumeShot(now);
            if (InfiniteAmmo)
            {
                // Same cadence and bloom as a real shot; only the round comes back.
                _runtime.CurrentAmmo = Mathf.Min(config.magazineSize, _runtime.CurrentAmmo + 1);
            }

            if (config.fireMode == FireMode.Burst && _runtime.BurstShotsRemaining > 0)
            {
                _runtime.BurstShotsRemaining--;
                if (_runtime.BurstShotsRemaining == 0)
                {
                    _runtime.NextShotAllowedAt += config.burstPause;
                }
            }

            // ONE TRIGGER PULL IS ONE SHOT, even when the shot is twelve pellets.
            //
            // Everything in this block used to live at the bottom of CastOneRay,
            // which made every buffer whose comment said "per shot" actually per
            // PELLET. With pelletsPerShot 1 — both shipped weapons — the two
            // scopes are the same object and nothing ever surfaced. Author a
            // twelve-pellet shotgun and one pull paid twelve times for one effect
            // module: twelve detonations from one Explosive, twelve chains each
            // free to re-hit what the pellet before had already claimed because
            // the set was wiped in between, twelve hitmarker clicks stacked in one
            // frame, and a 96-follow-up hang guard quietly raised to 1152.
            //
            // Cleared at the START of the pull rather than the end, so an early
            // return anywhere below cannot leak marks into the next one.
            _followUps.Clear();
            _alreadyHit.Clear();
            _announcedThisPull.Clear();
            _oncePerPullSpent.Clear();

            // The tracer's destination, seeded before anything is cast. A pull
            // that hits nothing keeps this value and throws its tracer down the
            // aim ray to maximum range, which is what a miss looks like.
            Ray aim = _look.AimRay;
            _tracerEnd = aim.origin + aim.direction * config.maxRange;
            _tracerEndResolved = false;

            // The PATTERN floors the bloom, and this line is what makes a shotgun
            // a shotgun rather than a very loud rifle.
            //
            // CurrentSpreadDegrees returns exactly 0 while aiming, deliberately —
            // ADS is supposed to be precise. Applied to a twelve-pellet weapon
            // that meant all twelve pellets were the SAME RAY: one impact point
            // wearing twelve decals and twelve spark systems, and twelve
            // PlayOneShot calls of one clip on one AudioSource in one frame, which
            // sum phase-aligned into roughly twelve times the amplitude and eat a
            // third of Unity's voice budget on a single trigger pull.
            float spread = Mathf.Max(config.pelletSpreadDegrees, CurrentSpreadDegrees());
            int pellets = Mathf.Max(1, config.pelletsPerShot);
            for (int pellet = 0; pellet < pellets; pellet++) CastOneRay(config, spread);

            // After every pellet, never between them. Each pellet still resolved
            // its own ray and its own damage above; what happens once is the
            // aftermath.
            DrainFollowUps(config);


            ApplyRecoil(config, shotIndex);
            SpawnMuzzleEffects(config, now);
            Fired?.Invoke();
        }

        private float CurrentSpreadDegrees()
        {
            if (_runtime == null) return 0f;

            // ADS spread is always zero: accuracy while aimed is controlled by
            // recoil alone. A random cone while aiming feels like the game cheating.
            if (IsAiming) return 0f;

            WeaponConfig config = _runtime.Config;
            float spread = _runtime.CurrentSpread;
            if (_motor != null)
            {
                if (!_motor.IsGrounded) spread *= config.airborneMultiplier;
                else if (_motor.IsCrouched) spread *= config.crouchedMultiplier;
                else if (_motor.HorizontalSpeed > 0.5f) spread *= config.movingMultiplier;
            }
            return Mathf.Min(spread, config.maxSpread * config.airborneMultiplier);
        }

        private void CastOneRay(WeaponConfig config, float spreadDegrees)
        {
            if (_look == null || _runtime == null) return;

            // The one thing that IS per pellet: a ray may not resolve the same
            // body twice, and the next pellet is a new ray with a clean slate.
            // Everything else the shot accumulates lives one level up, in
            // FireOneShot.
            _piercedThisRay.Clear();

            Ray aim = _look.AimRay;
            Vector3 direction = spreadDegrees <= 0f ? aim.direction : ApplyCone(aim.direction, spreadDegrees);

            int count = Physics.RaycastNonAlloc(aim.origin, direction, _hitBuffer, config.maxRange,
                _hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            // Pierce is resolved BEFORE the cast, not after: it changes how many
            // targets this one ray is allowed to find. Everything else works
            // through follow-ups.
            int budget = 1;
            float pierceFalloff = 1f;
            for (int i = 0; i < _runtime.Modules.Count; i++)
            {
                EffectModule? module = _runtime.Modules[i];
                if (module == null) continue;
                budget += module.ExtraRayBudget;
                pierceFalloff *= module.PierceDamageFalloff;
            }

            SortHitsByDistance(count);

            // ONE TRACER PER ROUND, not per pellet. A round is what leaves the
            // barrel; twelve pellets are how it is modelled. Twelve trails from
            // one pull would be a shotgun that fires a searchlight, so the FIRST
            // pellet to find anything owns the line and every pellet after it
            // leaves this alone.
            if (!_tracerEndResolved)
            {
                _tracerEnd = _hitBuffer[0].point;
                _tracerEndResolved = true;
            }

            float multiplier = 1f;
            int resolved = 0;
            for (int i = 0; i < count && resolved < budget; i++)
            {
                HitOutcome outcome = ResolveHit(config, _hitBuffer[i], direction, multiplier, depth: 0);

                // A SECOND collider on a body this ray has already gone through.
                // Every drone puts two on the line — the hull, which carries the
                // Health, and the small `Core` child, which carries the Weakpoint
                // that relays to it. Resolving both applied a headshot AND a body
                // shot from one bullet and spent two of the pierce budget on one
                // drone, so a Pierce round stopped a body early and hit for
                // roughly double on the way. Pass through without paying either.
                if (outcome == HitOutcome.AlreadyPierced) continue;

                resolved++;
                // A bullet passes through bodies, never through the wall behind
                // them: a pierce budget spent on geometry would shoot through the
                // arena.
                if (outcome != HitOutcome.Damaged) break;
                multiplier *= pierceFalloff;
            }
        }

        /// <summary>Insertion sort over the live part of the buffer. In-place, so it never allocates, and n is at most the buffer length.</summary>
        private void SortHitsByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit current = _hitBuffer[i];
                int j = i - 1;
                while (j >= 0 && _hitBuffer[j].distance > current.distance)
                {
                    _hitBuffer[j + 1] = _hitBuffer[j];
                    j--;
                }
                _hitBuffer[j + 1] = current;
            }
        }

        private static Vector3 ApplyCone(Vector3 forward, float degrees)
        {
            // Uniform point in a cone: random angle around the axis, radius scaled
            // by sqrt so the distribution is not centre-heavy.
            float radians = degrees * Mathf.Deg2Rad;
            float theta = UnityEngine.Random.value * Mathf.PI * 2f;
            float r = Mathf.Sqrt(UnityEngine.Random.value) * Mathf.Tan(radians);

            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, forward);

            Vector3 offset = (right * Mathf.Cos(theta) + up * Mathf.Sin(theta)) * r;
            return (forward + offset).normalized;
        }

        /// <summary>What one collider along a ray did to the bullet.</summary>
        private enum HitOutcome
        {
            /// <summary>Geometry, or a corpse. The bullet stops here.</summary>
            Blocked,
            /// <summary>A live body took damage. This is what a pierce budget is spent on.</summary>
            Damaged,
            /// <summary>Another collider belonging to a body this same ray already resolved. Pass through it for free.</summary>
            AlreadyPierced,
        }

        /// <summary>Resolves one impact along a ray. See <see cref="HitOutcome"/> for what the caller does with each answer.</summary>
        private HitOutcome ResolveHit(WeaponConfig config, in RaycastHit hit, Vector3 direction,
            float pierceMultiplier, int depth)
        {
            // Four multipliers, four owners: falloff is the weapon, the stat sheet
            // is what the player bought, DamageMultiplier is the cheat, and pierce
            // falloff is how many bodies this round has already passed through.
            float damage = config.DamageAtDistance(hit.distance) * DamageMultiplier
                           * _statDamageMultiplier * pierceMultiplier;

            // A weakpoint collider relays to its owner's Health, and the bonus is
            // applied HERE and only here: WeaponConfig.headshotMultiplier is the
            // single owner of that number. A second multiplier on the target
            // (HealthConfig used to carry one) double-dipped every headshot, so
            // it was deleted rather than balanced around.
            bool isWeakpoint = false;
            IDamageable? target = null;
            if (hit.collider.TryGetComponent(out Weakpoint weakpoint) && weakpoint.Owner != null)
            {
                target = weakpoint.Owner;
                damage *= config.headshotMultiplier;
                isWeakpoint = true;
            }
            else if (hit.collider.TryGetComponent(out IDamageable direct))
            {
                target = direct;
            }

            bool killed = false;
            // Two colliders, one body: the hull and its weakpoint Core both sit on
            // the line. The first of them to resolve claims the body — and it is
            // the nearer one, so a Core hit still scores the headshot it earned.
            // Everything below (damage, impact spark, effect modules, hitmarker)
            // is skipped for the second, which is what stops one bullet paying
            // twice on one drone.
            //
            // Read from the per-RAY set. The per-pull set would answer yes for
            // every pellet after the first, which is a shotgun that fires one
            // pellet of damage — and for a body a follow-up had already claimed,
            // a bullet that does nothing at all.
            if (target is Health owner && _piercedThisRay.Contains(owner)) return HitOutcome.AlreadyPierced;

            bool damaged = false;
            if (target != null && target.IsAlive)
            {
                var info = new DamageInfo(damage, hit.point, hit.normal, direction, isWeakpoint);
                target.ApplyDamage(in info);
                damaged = true;
                killed = !target.IsAlive;
                if (target is Health health)
                {
                    _piercedThisRay.Add(health);
                    MarkHit(health);
                }
            }

            SpawnImpact(hit, onBody: target != null);
            if (damaged) RegisterHit(target as Health, killed);

            RunEffectModules(new HitContext(this, config, hit.point, hit.normal, direction,
                target as Health, damage, depth));
            return damaged ? HitOutcome.Damaged : HitOutcome.Blocked;
        }

        /// <summary>
        /// Gives every module carried by this weapon a look at the hit, in the
        /// order they were bought. Order is the stacking rule: a module that
        /// queues damage can only be reacted to by a module that runs deeper.
        /// </summary>
        private void RunEffectModules(in HitContext context)
        {
            if (_runtime == null) return;

            for (int i = 0; i < _runtime.Modules.Count; i++)
            {
                EffectModule? module = _runtime.Modules[i];
                if (module == null) continue;
                // The recursion guard, enforced in one place: without it,
                // Explosive -> Chain -> Explosive never terminates.
                // The sandbox bonus shifts the depth the module SEES, rather than
                // the maxDepth it declares: maxDepth lives on a shared config
                // asset, and Domain Reload is off, so writing to it would rewrite
                // the shipped balance for every future Play session.
                if (!module.RunsAtDepth(context.Depth - _extraEffectDepth)) continue;
                // An explosion is one event per pull, not one per pellet. See
                // EffectModule.OncePerPull for the twelve stacked booms this
                // stops. Claimed here, in the controller, so modules stay
                // stateless ScriptableObjects shared by every weapon.
                if (module.OncePerPull)
                {
                    if (_oncePerPullSpent.Contains(module)) continue;
                    _oncePerPullSpent.Add(module);
                }
                module.Resolve(in context, _followUps);
            }
        }

        /// <summary>
        /// Applies everything the modules queued, then lets modules react to those
        /// hits one depth further down. Called exactly ONCE per trigger pull, from
        /// FireOneShot — which is the whole point of the iteration cap below, and
        /// what it was not while this ran at the bottom of every pellet's ray.
        ///
        /// Bounded twice — by the buffer's capacity and by this loop's iteration
        /// cap — because a hang is a worse bug than a missing spark. Whatever the
        /// cap leaves in the queue is dropped by the next pull's clear, so a
        /// module that overruns its budget cannot bleed work into the shot after.
        /// </summary>
        private void DrainFollowUps(WeaponConfig config)
        {
            int guard = 0;
            while (guard++ < MAX_FOLLOW_UPS_PER_PULL && _followUps.TryDequeue(out FollowUp followUp))
            {
                if (followUp.Kind == FollowUpKind.Damage) ApplyFollowUpDamage(config, in followUp);
                else ApplyFollowUpRay(config, in followUp);
            }
        }

        /// <summary>
        /// One hit confirm per TARGET per trigger pull.
        ///
        /// Every path that lands damage funnels through here rather than raising
        /// the event itself, so there is one place the rule lives. Hitmarker does
        /// a PlayOneShot per event, and the rule has to satisfy two cases that
        /// pull in opposite directions: twelve pellets into one drone is ONE hit
        /// and must not be twelve stacked clicks, while a shot that kills a drone
        /// directly and a second one through a chain is TWO kills and must sound
        /// like two. Per-target is the answer to both; per-pull only answered the
        /// first, and quietly broke the second on the rifle that ships today.
        /// </summary>
        private void RegisterHit(Health? target, bool killed)
        {
            // A KILL always confirms. A body can only die once, so there is no
            // duplicate to suppress, and suppressing it would be the worst
            // outcome of the lot: twelve pellets where the first one connects
            // and the ninth one kills would play a hit click and then nothing at
            // all, so the shot that actually killed the drone would be the
            // silent one.
            if (killed)
            {
                Hit?.Invoke(true);
                return;
            }

            // A plain hit dedupes per target. A null target is an IDamageable
            // that is not a Health; nothing in the game is one yet, and it
            // announces every time rather than never, because a missing
            // hitmarker reads as a missed shot.
            if (target != null)
            {
                if (_announcedThisPull.Contains(target)) return;
                _announcedThisPull.Add(target);
            }
            Hit?.Invoke(false);
        }

        private void ApplyFollowUpDamage(WeaponConfig config, in FollowUp followUp)
        {
            Health? target = followUp.Target;
            if (target == null || !target.IsAlive) return;
            // Explosive and Chain both refuse the shooter when they queue, but the
            // guard belongs on the APPLY side too: it is the one place every
            // follow-up passes through, and a future module gets it for free.
            if (target == _ownerHealth) return;

            var info = new DamageInfo(followUp.Damage, followUp.Origin, -followUp.Direction,
                followUp.Direction, false);
            target.ApplyDamage(in info);
            MarkHit(target);
            RegisterHit(target, !target.IsAlive);

            RunEffectModules(new HitContext(this, config, target.transform.position, -followUp.Direction,
                followUp.Direction, target, followUp.Damage, followUp.Depth));
        }

        private void ApplyFollowUpRay(WeaponConfig config, in FollowUp followUp)
        {
            int count = Physics.RaycastNonAlloc(followUp.Origin, followUp.Direction, _hitBuffer,
                followUp.Range, _hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            SortHitsByDistance(count);
            RaycastHit hit = _hitBuffer[0];

            Health? target = null;
            if (hit.collider.TryGetComponent(out Weakpoint weakpoint) && weakpoint.Owner != null)
            {
                target = weakpoint.Owner;
            }
            else if (hit.collider.TryGetComponent(out Health direct))
            {
                target = direct;
            }

            SpawnImpact(hit, onBody: target != null);
            if (target == null || !target.IsAlive) return;

            // The shooter is never a valid follow-up target. Explosive and Chain
            // both refuse OwnerHealth; this path did not, and a Ricochet is the
            // one module whose follow-up AIMS BACK — bounce off the wall you are
            // standing against and the round came home and killed you. With
            // maxDepth raised it could bounce home repeatedly inside one shot.
            if (target == _ownerHealth) return;

            // Same double-dip protection the Damage follow-ups get. Without it a
            // bounce that lands on a drone this shot already hit pays full
            // follow-up damage a second time, which is exactly the leak the
            // already-hit set exists to close.
            if (HasHit(target)) return;

            var info = new DamageInfo(followUp.Damage, hit.point, hit.normal, followUp.Direction, false);
            target.ApplyDamage(in info);
            MarkHit(target);
            RegisterHit(target, !target.IsAlive);

            RunEffectModules(new HitContext(this, config, hit.point, hit.normal, followUp.Direction,
                target, followUp.Damage, followUp.Depth));
        }

        /// <summary>
        /// Sparks always; a bullet HOLE only on geometry.
        ///
        /// A decal lives 20 seconds and a rifle fires about twelve rounds a
        /// second, so an unconditional decal per hit meant ~230 live instances
        /// against a pool prewarmed for 48 — five times the intended footprint on
        /// a 4 GB budget. Worse, a decal stamped on a drone is spawned into the
        /// world, not parented to it: the drone dies, returns to the pool, and its
        /// bullet holes hang in mid-air for the rest of the wave. Bodies get the
        /// spark, walls get the hole.
        /// </summary>
        private void SpawnImpact(in RaycastHit hit, bool onBody)
        {
            if (_pool == null || _impact == null) return;

            Quaternion rotation = Quaternion.LookRotation(hit.normal);
            Vector3 point = hit.point + hit.normal * _impact.surfaceOffset;

            // KEYED ON THE COLLIDER'S LAYER, and that is the whole design.
            // `Collider.gameObject.layer` is an int the physics engine already
            // had to know: no component lookup, no allocation, and nothing here
            // that guard-no-find-in-update would have to forgive. A SurfaceTag
            // MonoBehaviour would have been a GetComponent per pellet on a path
            // reached from Update, forty drones deep.
            //
            // A null response means no row claimed this layer; the config's
            // fallback block answers, so an unmapped surface still sparks. A
            // silent impact is indistinguishable from a missed shot.
            ImpactConfig.SurfaceResponse? surface = _impact.ResponseFor(hit.collider.gameObject.layer, onBody);
            GameObject? decal = surface != null ? surface.decalPrefab : _impact.decalPrefab;
            GameObject? particles = surface != null ? surface.particlePrefab : _impact.particlePrefab;
            AudioClip? sound = surface != null ? surface.impactSound : _impact.impactSound;
            float volume = surface != null ? surface.volume : _impact.impactVolume;

            if (!onBody && decal != null)
            {
                _pool.SpawnForSeconds(decal, point, rotation, _impact.decalLifetime);
            }
            if (particles != null)
            {
                _pool.SpawnForSeconds(particles, point, rotation, _impact.particleLifetime);
            }
            PlayImpactSound(sound, volume, point);
        }

        /// <summary>
        /// The impact crack, at the impact.
        ///
        /// ImpactConfig has carried an `impactSound` field since the day the file
        /// was written and NOTHING read it, so every bullet in this game has
        /// landed in silence for the whole life of the project. The gun made a
        /// noise; the world never answered.
        ///
        /// WHY A DEDICATED SOURCE IS MOVED RATHER THAN A SOURCE PER IMPACT
        /// `AudioSource.PlayClipAtPoint` is the obvious call and it is banned
        /// here: it Instantiates a GameObject and Destroys it per hit, which is
        /// the GC-hitch factory the object pool exists to replace. One source
        /// repositioned before each PlayOneShot costs a transform write.
        ///
        /// The trade it accepts, stated so nobody rediscovers it as a bug: a
        /// one-shot already playing moves with the source, so two impacts a room
        /// apart inside the same half-second both sound like they are at the
        /// second one. At a rifle's cadence the two are milliseconds and metres
        /// apart, which is inaudible; if it ever stops being inaudible, the fix
        /// is a small ring of pooled sources, not a source per bullet.
        ///
        /// The gun's own close-layer source is the fallback and is NEVER moved —
        /// dragging it to the far wall would take the gunshot with it.
        /// </summary>
        private void PlayImpactSound(AudioClip? clip, float volume, Vector3 point)
        {
            if (clip == null || volume <= 0f) return;

            if (_audioImpact != null)
            {
                if (_audioImpactTransform != null) _audioImpactTransform.position = point;
                _audioImpact.PlayOneShot(clip, volume);
                return;
            }
            if (_audioClose != null) _audioClose.PlayOneShot(clip, volume);
        }

        private void ApplyRecoil(WeaponConfig config, int shotIndex)
        {
            if (_look == null) return;

            float multiplier = 1f;
            if (IsAiming) multiplier *= config.adsRecoilMultiplier;
            if (_motor != null && _motor.IsCrouched) multiplier *= config.crouchRecoilMultiplier;

            float pitch = -RecoilPattern.VerticalKick(config, shotIndex) * multiplier;
            float yaw = RecoilPattern.HorizontalKick(config, shotIndex) * multiplier;
            _look.AddRecoil(pitch, yaw);
        }

        private void UpdateRecoilRecovery(float now, float deltaTime)
        {
            if (_runtime == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            if (now - _runtime.LastShotAt < config.recoveryDelay) return;

            // Recover to 85%, not 100%. Full recovery makes sustained fire free and
            // the gun feels weightless; the residual drift is what forces bursts.
            // The unrecovered slice is committed into the real aim point.
            float step = deltaTime / Mathf.Max(0.01f, config.recoveryDuration);
            _look.CommitRecoilToAim((1f - config.recoveryCompleteness) * step);
            _look.RecoverRecoil(config.verticalKickAtShotEight * step * 2f,
                                config.horizontalKickMax * step * 2f);
        }

        private void UpdateAds(float deltaTime)
        {
            if (_runtime == null || _input == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            bool wantsAds = _input.AimHeld && (_motor == null || !_motor.IsSprinting);
            float rate = deltaTime / Mathf.Max(0.01f, config.adsTime);
            _adsProgress = Mathf.MoveTowards(_adsProgress, wantsAds ? 1f : 0f, rate);

            _look.SetSensitivityMultiplier(Mathf.Lerp(1f, config.adsSensitivityMultiplier, _adsProgress));
            // The viewmodel follows the same blend, so the gun rises into the
            // sight instead of teleporting between two poses.
            if (_sway != null) _sway.SetAdsProgress(_adsProgress);
        }

        private void UpdateFovOffset(float now)
        {
            if (_runtime == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            float baseFov = _look.BaseFov;
            float adsOffset = -(1f - config.adsFovMultiplier) * baseFov * _adsProgress;
            float kick = now < _fovKickUntil ? config.fovKickOnFire : 0f;
            _look.SetFovOffset(adsOffset + kick);
        }

        /// <summary>
        /// The one reload entry point. Abandons a queued burst first — a player
        /// who hits reload mid-burst wants the magazine, not two more rounds and
        /// a click — and plays the reload sound exactly when a reload starts.
        /// </summary>
        private void TryBeginReload(float now)
        {
            if (_runtime == null) return;
            _runtime.BurstShotsRemaining = 0;
            if (!_runtime.BeginReload(now, _statReloadSpeed)) return;
            if (_audioClose != null && _runtime.Config.reloadClip != null)
            {
                _audioClose.PlayOneShot(_runtime.Config.reloadClip);
            }
        }

        private void UpdateReload(float now)
        {
            if (_runtime == null) return;

            if (_runtime.IsReloading && now >= _runtime.ReloadEndsAt) _runtime.CompleteReload();

            // Auto-reload on empty, so the player never stands there clicking.
            if (_runtime.IsMagazineEmpty && _runtime.HasReserve && !_runtime.IsReloading)
            {
                TryBeginReload(now);
            }
        }

        /// <summary>
        /// Both halves of the muzzle flash, on ONE clock.
        ///
        /// There have to be two lights because a camera culls lights by the
        /// light's layer: the world light cannot reach a gun drawn by the
        /// overlay camera, and a viewmodel light cannot light the room. They
        /// must not, however, be two TIMERS. The gun's light first shipped on
        /// the pooled flash prefab, so it lived for muzzleFlashLifetime (0.08 s)
        /// while the room light lived for muzzleLightDuration (0.03 s) — and at
        /// the SMG's 900 rpm, 0.0667 s between shots, an 0.08 s light never
        /// finished before the next one began. Sustained fire put the viewmodel
        /// under a continuous glow while the room strobed correctly. A flash
        /// that does not go out is not a flash.
        /// </summary>
        private void UpdateMuzzleLight(float now)
        {
            bool shouldBeOn = now < _muzzleLightUntil;
            if (_muzzleLight != null && _muzzleLight.enabled != shouldBeOn) _muzzleLight.enabled = shouldBeOn;
            if (_viewmodelMuzzleLight != null && _viewmodelMuzzleLight.enabled != shouldBeOn) _viewmodelMuzzleLight.enabled = shouldBeOn;
        }

        private void SpawnMuzzleEffects(WeaponConfig config, float now)
        {
            _fovKickUntil = now + config.fovKickDuration;

            // The clock is set unconditionally: it drives BOTH lights, so hanging
            // it off the world light existing would silently disable the gun's
            // flash on any rig that happens not to have one.
            _muzzleLightUntil = now + config.muzzleLightDuration;
            if (_viewmodelMuzzleLight != null)
            {
                _viewmodelMuzzleLight.intensity = config.viewmodelMuzzleLightIntensity;
            }
            if (_muzzleLight != null)
            {
                _muzzleLight.intensity = config.muzzleLightIntensity;
            }

            if (_pool != null && _muzzle != null && config.muzzleFlashPrefab != null)
            {
                // Random roll so back-to-back flashes read as fire, not as the
                // same sprite blinking.
                Quaternion roll = _muzzle.rotation * Quaternion.Euler(0f, 0f, UnityEngine.Random.value * 360f);
                PooledObject flash = _pool.SpawnForSeconds(config.muzzleFlashPrefab, _muzzle.position, roll,
                    config.muzzleFlashLifetime);
                JitterScale(flash, config.muzzleFlashScaleJitter);
            }

            // THE SECOND, STRETCHED QUAD. One untextured quad rolled to a random
            // angle is still one shape, and the eye reads a repeated shape as a
            // repeated sprite within about three shots. A second quad at a
            // different aspect ratio, rolled independently, produces a different
            // silhouette every shot out of the same two flat meshes — the whole
            // improvement, and it costs no texture and no VRAM on a 4 GB card.
            if (_pool != null && _muzzle != null && config.muzzleFlashWidePrefab != null)
            {
                Quaternion roll = _muzzle.rotation * Quaternion.Euler(0f, 0f, UnityEngine.Random.value * 360f);
                PooledObject wide = _pool.SpawnForSeconds(config.muzzleFlashWidePrefab, _muzzle.position, roll,
                    config.muzzleFlashLifetime);
                JitterScale(wide, config.muzzleFlashScaleJitter);
            }

            SpawnMuzzleSmoke(config);
            SpawnTracer(config);

            if (_pool != null && _casingEject != null && config.shellCasingPrefab != null)
            {
                PooledObject casing = _pool.SpawnForSeconds(config.shellCasingPrefab,
                    _casingEject.position, _casingEject.rotation, config.casingLifetime);

                // Overwrite, never add: a pooled rigidbody keeps last use's
                // velocity, and a casing that inherits it flies off like a bullet.
                Rigidbody? body = casing.CachedRigidbody;
                if (body != null)
                {
                    body.linearVelocity = _casingEject.right * config.casingEjectSpeed
                        + _casingEject.up * config.casingEjectUpKick;
                    body.angularVelocity = new Vector3(
                        (UnityEngine.Random.value - 0.5f) * 2f * config.casingSpinMax,
                        (UnityEngine.Random.value - 0.5f) * 2f * config.casingSpinMax,
                        (UnityEngine.Random.value - 0.5f) * 2f * config.casingSpinMax);
                }
            }

            // Two layers: a close mechanical crack plus a distance tail. One-layer
            // gunshots are the number-one reason a shooter sounds cheap.
            if (_audioClose != null && config.fireCloseLayer != null)
            {
                _audioClose.PlayOneShot(config.fireCloseLayer);
            }
            if (_audioTail != null && config.fireTailLayer != null)
            {
                _audioTail.PlayOneShot(config.fireTailLayer);
            }

            if (_shake != null) _shake.AddTrauma(config.cameraShakeAmplitude * 0.35f);
        }

        /// <summary>
        /// Random scale on a pooled flash, written on EVERY spawn.
        ///
        /// Unconditional because the pool never resets a transform's scale: an
        /// instance comes back carrying whatever the last shot left on it, so a
        /// "only when jitter is non-zero" write would freeze one random size in
        /// place forever the moment somebody turned the jitter off. Writing it
        /// every time makes jitter 0 mean exactly scale 1, which is what an
        /// author typing 0 expects.
        /// </summary>
        private static void JitterScale(PooledObject instance, float jitter)
        {
            float scale = 1f + (UnityEngine.Random.value - 0.5f) * 2f * Mathf.Clamp01(jitter);
            instance.CachedTransform.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>
        /// The puff off the barrel, on the last round of a burst and every N
        /// rounds of sustained fire.
        ///
        /// Never on every shot, and the reason is not cost: smoke in front of
        /// the sight is fog over the thing the player is aiming at. Held on a
        /// counter rather than a probability so it is the SAME round every time
        /// — a random puff that occasionally lands on the shot that mattered is
        /// a feel bug nobody can reproduce.
        /// </summary>
        private void SpawnMuzzleSmoke(WeaponConfig config)
        {
            if (_pool == null || _muzzle == null || config.muzzleSmokePrefab == null) return;

            // A finished burst always puffs: that is the beat the fire mode is
            // built around, and it is what makes the burstPause read as the gun
            // resetting rather than as input lag.
            bool due = config.fireMode == FireMode.Burst && _runtime != null && _runtime.BurstShotsRemaining == 0;

            if (config.muzzleSmokeEveryNRounds > 0)
            {
                if (_roundsUntilSmoke > 0) _roundsUntilSmoke--;
                else
                {
                    _roundsUntilSmoke = config.muzzleSmokeEveryNRounds - 1;
                    due = true;
                }
            }

            if (!due) return;
            _pool.SpawnForSeconds(config.muzzleSmokePrefab, _muzzle.position, _muzzle.rotation,
                config.muzzleSmokeLifetime);
        }

        /// <summary>
        /// One tracer every Nth round, muzzle to wherever the round stopped.
        ///
        /// The counter lives here rather than on WeaponRuntime because it is a
        /// property of the GUN as a physical object — a belt with every third
        /// round loaded hot — and not of the loadout: swapping weapons mid-run
        /// must not restart the pattern, and reloading must not either.
        ///
        /// The prefab is fetched through the pool like everything else that
        /// spawns. If it is not prewarmed the pool will Instantiate one on the
        /// first shot of the run, which is the hitch pooling exists to prevent —
        /// Fx_Tracer belongs in ObjectPool's prewarm list.
        /// </summary>
        private void SpawnTracer(WeaponConfig config)
        {
            if (_pool == null || _muzzle == null || config.tracerPrefab == null) return;

            // Counted down only when a tracer could actually be produced, so a
            // weapon with no tracer prefab never silently burns through the
            // pattern and then starts mid-cycle the moment one is assigned.
            if (_roundsUntilTracer > 0)
            {
                _roundsUntilTracer--;
                return;
            }
            _roundsUntilTracer = Mathf.Max(1, config.tracerEveryNRounds) - 1;

            // Spawn, not SpawnForSeconds: a tracer owns its own clock, because
            // its lifetime is a function of how far it has to fly and only it
            // knows how long its trail takes to fade. See Tracer.Launch, and
            // MAX_LIFETIME_SECONDS for the backstop that stops a mis-authored
            // speed stranding a pooled instance alive for the rest of the run.
            Vector3 from = _muzzle.position;
            PooledObject instance = _pool.Spawn(config.tracerPrefab, from, Quaternion.identity);
            if (instance.TryGetComponent(out Tracer tracer))
            {
                tracer.Launch(_pool, from, _tracerEnd, config.tracerSpeed, config.tracerWidth);
                return;
            }

            // A prefab with no Tracer would never despawn itself, and the pool
            // would hand out a fresh instance on every third round for the rest
            // of the run. Put it straight back and say so, once.
            _pool.Despawn(instance);
            if (_tracerPrefabReported) return;
            _tracerPrefabReported = true;
            GameLog.Error($"'{config.tracerPrefab.name}' is assigned as {config.displayName}'s tracer but carries " +
                          "no Tracer component — nothing would ever return it to the pool.", this);
        }

        private void PlayDryFire(float now)
        {
            if (_runtime == null) return;

            _runtime.NextShotAllowedAt = now + _runtime.Config.dryFireCooldown;
            if (_audioClose != null && _runtime.Config.dryFireClip != null)
            {
                _audioClose.PlayOneShot(_runtime.Config.dryFireClip);
            }
        }
    }
}
