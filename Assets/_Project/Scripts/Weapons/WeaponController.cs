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
        [SerializeField] private AudioSource? _audioClose = null;
        [SerializeField] private AudioSource? _audioTail = null;
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
        private readonly List<Health> _alreadyHit = new(24);
        // 24 covered about twelve drones: every drone puts TWO colliders in an
        // overlap (hull and weakpoint Core) and only the hull carries Health.
        // OverlapSphere fills a full buffer with an arbitrary subset and reports
        // no overflow, so a blast or a chain in a real crowd silently stopped
        // finding targets it was standing next to.
        private readonly Collider[] _effectOverlap = new Collider[64];

        /// <summary>
        /// Hard stop on follow-up work per shot. Not a tuning value — a hang guard.
        /// The depth rules are the real limit; this is what catches a mis-authored
        /// module before it freezes a frame.
        /// </summary>
        private const int MAX_FOLLOW_UPS_PER_SHOT = 96;

        /// <summary>Fired for every confirmed hit; the bool is true when it killed. The UI listens, and the weapon never learns the UI exists.</summary>
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

        /// <summary>True when this shot has already damaged that target. The reason a chain cannot bounce between two drones forever.</summary>
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

            float spread = CurrentSpreadDegrees();
            int pellets = Mathf.Max(1, config.pelletsPerShot);
            for (int pellet = 0; pellet < pellets; pellet++) CastOneRay(config, spread);

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

            Ray aim = _look.AimRay;
            Vector3 direction = spreadDegrees <= 0f ? aim.direction : ApplyCone(aim.direction, spreadDegrees);

            int count = Physics.RaycastNonAlloc(aim.origin, direction, _hitBuffer, config.maxRange,
                _hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0)
            {
                DrainFollowUps(config);
                return;
            }

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

            DrainFollowUps(config);
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
            if (target is Health owner && HasHit(owner)) return HitOutcome.AlreadyPierced;

            bool damaged = false;
            if (target != null && target.IsAlive)
            {
                var info = new DamageInfo(damage, hit.point, hit.normal, direction, isWeakpoint);
                target.ApplyDamage(in info);
                damaged = true;
                killed = !target.IsAlive;
                if (target is Health health) MarkHit(health);
            }

            SpawnImpact(hit, onBody: target != null);
            if (damaged) Hit?.Invoke(killed);

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
                if (!module.RunsAtDepth(context.Depth)) continue;
                module.Resolve(in context, _followUps);
            }
        }

        /// <summary>
        /// Applies everything the modules queued, then lets modules react to those
        /// hits one depth further down. Bounded twice — by the buffer's capacity
        /// and by this loop's iteration cap — because a hang is a worse bug than a
        /// missing spark.
        /// </summary>
        private void DrainFollowUps(WeaponConfig config)
        {
            int guard = 0;
            while (guard++ < MAX_FOLLOW_UPS_PER_SHOT && _followUps.TryDequeue(out FollowUp followUp))
            {
                if (followUp.Kind == FollowUpKind.Damage) ApplyFollowUpDamage(config, in followUp);
                else ApplyFollowUpRay(config, in followUp);
            }

            // Per-shot state, cleared per shot: the already-hit set must not leak
            // into the next trigger pull or chains stop working after a magazine.
            _followUps.Clear();
            _alreadyHit.Clear();
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
            Hit?.Invoke(!target.IsAlive);

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
            Hit?.Invoke(!target.IsAlive);

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

            if (!onBody && _impact.decalPrefab != null)
            {
                _pool.SpawnForSeconds(_impact.decalPrefab, point, rotation, _impact.decalLifetime);
            }
            if (_impact.particlePrefab != null)
            {
                _pool.SpawnForSeconds(_impact.particlePrefab, point, rotation, _impact.particleLifetime);
            }
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

        private void UpdateMuzzleLight(float now)
        {
            if (_muzzleLight == null) return;
            bool shouldBeOn = now < _muzzleLightUntil;
            if (_muzzleLight.enabled != shouldBeOn) _muzzleLight.enabled = shouldBeOn;
        }

        private void SpawnMuzzleEffects(WeaponConfig config, float now)
        {
            _fovKickUntil = now + config.fovKickDuration;

            if (_muzzleLight != null)
            {
                _muzzleLight.intensity = config.muzzleLightIntensity;
                _muzzleLightUntil = now + config.muzzleLightDuration;
            }

            if (_pool != null && _muzzle != null && config.muzzleFlashPrefab != null)
            {
                // Random roll so back-to-back flashes read as fire, not as the
                // same sprite blinking.
                Quaternion roll = _muzzle.rotation * Quaternion.Euler(0f, 0f, UnityEngine.Random.value * 360f);
                _pool.SpawnForSeconds(config.muzzleFlashPrefab, _muzzle.position, roll, config.muzzleFlashLifetime);
            }

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
