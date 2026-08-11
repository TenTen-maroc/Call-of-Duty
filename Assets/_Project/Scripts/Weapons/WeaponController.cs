#nullable enable
using System;
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

        [Header("Wiring")]
        [SerializeField] private PlayerInput? _input = null;
        [SerializeField] private PlayerLook? _look = null;
        [SerializeField] private PlayerMotor? _motor = null;
        [SerializeField] private ObjectPool? _pool = null;
        [SerializeField] private CameraShake? _shake = null;
        [Tooltip("Where the muzzle flash and casings spawn from.")]
        [SerializeField] private Transform? _muzzle = null;
        [SerializeField] private Transform? _casingEject = null;
        [SerializeField] private Light? _muzzleLight = null;
        [SerializeField] private AudioSource? _audioClose = null;
        [SerializeField] private AudioSource? _audioTail = null;
        [Tooltip("What bullets can hit. Leave the player's own layer out of this.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        private WeaponRuntime? _runtime;
        private float _adsProgress;
        private float _sprintReleasedAt = -99f;
        private float _fovKickUntil;
        private float _muzzleLightUntil;
        private bool _wasSprinting;

        // Pre-sized buffer: RaycastNonAlloc never allocates, which matters once
        // hundreds of shots per minute are flying.
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

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

            if (_input.ReloadPressed) _runtime.BeginReload(now);
            if (WantsToFire()) TryFire(now);
        }

        private bool WantsToFire()
        {
            if (_input == null || _runtime == null) return false;
            return _runtime.Config.fireMode == FireMode.FullAuto
                ? _input.FireHeld
                : _input.FirePressedThisFrame;
        }

        private void TrackSprintRelease(float now)
        {
            if (_motor == null) return;
            if (_wasSprinting && !_motor.IsSprinting) _sprintReleasedAt = now;
            _wasSprinting = _motor.IsSprinting;
        }

        private void TryFire(float now)
        {
            if (_runtime == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            // Sprint-to-fire: the gap between releasing sprint and being able to
            // shoot. Too short and the game becomes a sprint-around-corners
            // festival; 150-250 ms is the arcade sweet spot.
            if (_motor != null && _motor.IsSprinting) return;
            if (now - _sprintReleasedAt < config.sprintToFireTime) return;

            if (_runtime.IsReloading && !_runtime.TryCancelReload(now)) return;

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

            FireOneShot(now);
        }

        private void FireOneShot(float now)
        {
            if (_runtime == null || _look == null) return;
            WeaponConfig config = _runtime.Config;

            int shotIndex = _runtime.ShotsInBurst;
            if (InfiniteAmmo)
            {
                _runtime.ShotsInBurst++;
                _runtime.LastShotAt = now;
                _runtime.NextShotAllowedAt = now + config.SecondsPerShot;
                _runtime.CurrentSpread = Mathf.Min(config.maxSpread, _runtime.CurrentSpread + config.spreadPerShot);
            }
            else
            {
                _runtime.ConsumeShot(now);
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
            if (_look == null) return;

            Ray aim = _look.AimRay;
            Vector3 direction = spreadDegrees <= 0f ? aim.direction : ApplyCone(aim.direction, spreadDegrees);

            int count = Physics.RaycastNonAlloc(aim.origin, direction, _hitBuffer, config.maxRange,
                _hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            // Nearest hit wins. Sorting the buffer would allocate; one pass does not.
            int nearest = 0;
            for (int i = 1; i < count; i++)
            {
                if (_hitBuffer[i].distance < _hitBuffer[nearest].distance) nearest = i;
            }

            ResolveHit(config, _hitBuffer[nearest], direction);
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

        private void ResolveHit(WeaponConfig config, in RaycastHit hit, Vector3 direction)
        {
            float damage = config.DamageAtDistance(hit.distance) * DamageMultiplier;

            bool killed = false;
            bool damaged = false;
            if (hit.collider.TryGetComponent(out IDamageable target) && target.IsAlive)
            {
                var info = new DamageInfo(damage, hit.point, hit.normal, direction, isWeakpoint: false);
                target.ApplyDamage(in info);
                damaged = true;
                killed = !target.IsAlive;
            }

            SpawnImpact(hit);
            if (damaged) Hit?.Invoke(killed);
        }

        private void SpawnImpact(in RaycastHit hit)
        {
            if (_pool == null || _impact == null) return;

            Quaternion rotation = Quaternion.LookRotation(hit.normal);
            Vector3 point = hit.point + hit.normal * _impact.surfaceOffset;

            if (_impact.decalPrefab != null)
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

        private void UpdateReload(float now)
        {
            if (_runtime == null) return;

            if (_runtime.IsReloading && now >= _runtime.ReloadEndsAt) _runtime.CompleteReload();

            // Auto-reload on empty, so the player never stands there clicking.
            if (_runtime.IsMagazineEmpty && _runtime.HasReserve && !_runtime.IsReloading)
            {
                _runtime.BeginReload(now);
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
                _pool.SpawnForSeconds(config.muzzleFlashPrefab, _muzzle.position, _muzzle.rotation, 0.08f);
            }

            if (_pool != null && _casingEject != null && config.shellCasingPrefab != null)
            {
                _pool.SpawnForSeconds(config.shellCasingPrefab, _casingEject.position, _casingEject.rotation,
                    config.casingLifetime);
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

            _runtime.NextShotAllowedAt = now + 0.25f;
            if (_audioClose != null && _runtime.Config.dryFireClip != null)
            {
                _audioClose.PlayOneShot(_runtime.Config.dryFireClip);
            }
        }
    }
}
