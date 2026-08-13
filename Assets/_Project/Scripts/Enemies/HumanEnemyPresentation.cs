#nullable enable
using CoD.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CoD.Enemies
{
    /// <summary>
    /// Optional humanoid layer on the shared DroneController. It owns cover,
    /// temporary regional reactions, death/ragdoll presentation, and pool reset;
    /// navigation, health, attacks, registry, and rewards remain shared.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HumanEnemyPresentation : MonoBehaviour
    {
        [SerializeField] private HumanCombatConfig? _config = null;
        [SerializeField] private EnemyAnimator? _animator = null;
        [SerializeField] private Collider[] _hitColliders = System.Array.Empty<Collider>();
        [SerializeField] private Rigidbody[] _ragdollBodies = System.Array.Empty<Rigidbody>();
        [SerializeField] private Collider[] _ragdollColliders = System.Array.Empty<Collider>();
        [SerializeField] private Transform? _head = null;
        [SerializeField] private Transform? _leftArm = null;
        [SerializeField] private Transform? _rightArm = null;
        [SerializeField] private Transform? _leftLeg = null;
        [SerializeField] private Transform? _rightLeg = null;

        private readonly PooledObject?[] _attachedEffects = new PooledObject?[8];
        private readonly Vector3[] _boneScales = new Vector3[5];
        private DroneController? _controller;
        private NavMeshAgent? _agent;
        private Transform? _target;
        private CoverRegistry? _coverRegistry;
        private GoreManager? _gore;
        private CoverPoint? _cover;
        private float _nextDecisionAt;
        private float _suppressedUntil;
        private float _aimDisruptedUntil;
        private float _legStumbleUntil;
        private float _strafeUntil;
        private float _deathEndsAt;
        private int _lane;
        private int _attachedCursor;
        private bool _firing;
        private bool _dead;
        private bool _ragdoll;
        private readonly bool[] _hiddenBones = new bool[5];

        public Vector3 Position => transform.position;
        public bool IsDeadPresentation => _dead;
        public bool IsRagdoll => _ragdoll;
        public float SuppressedUntil => _suppressedUntil;
        public float AimDisruptedUntil => _aimDisruptedUntil;
        public float LegStumbleUntil => _legStumbleUntil;

        private void Awake()
        {
            TryGetComponent(out _controller);
            TryGetComponent(out _agent);
            CaptureBoneScales();
            SetRagdoll(false, Vector3.zero, 0f);
        }

        private void Update()
        {
            if (!_dead || Time.time < _deathEndsAt) return;
            _controller?.FinishDeathPresentation();
        }

        public void ConfigureRuntime(Transform target, CoverRegistry? coverRegistry, GoreManager? gore)
        {
            _target = target;
            _coverRegistry = coverRegistry;
            _gore = gore;
            _lane = Mathf.Abs(GetInstanceID()) % 3;
            ResetForReuse();
        }

        public bool TrySteer(float now, float baseSpeed, float waveSpeedMultiplier)
        {
            if (_agent == null || _target == null || _controller == null || _config == null) return false;
            if (!_agent.enabled || !_agent.isOnNavMesh || _dead) return false;

            float speedMultiplier = now < _legStumbleUntil ? _config.legStumbleSpeedMultiplier : 1f;
            if (now < _suppressedUntil) speedMultiplier = Mathf.Min(speedMultiplier, 0.8f);
            _agent.speed = baseSpeed * waveSpeedMultiplier * speedMultiplier;

            if (_firing)
            {
                _agent.speed *= _config.firingSpeedMultiplier;
                _agent.isStopped = _config.firingSpeedMultiplier <= 0.01f;
                FaceTarget();
                _animator?.SetMovement(Vector3.zero);
                return true;
            }

            _agent.isStopped = false;
            _agent.updateRotation = true;
            if (now >= _nextDecisionAt && now >= _strafeUntil)
            {
                _nextDecisionAt = now + _config.decisionInterval;
                ChooseCover();
            }

            Vector3 destination;
            if (_cover != null)
            {
                destination = _cover.Position;
                if ((destination - Position).sqrMagnitude <=
                    _config.coverArrivalDistance * _config.coverArrivalDistance)
                {
                    _agent.isStopped = true;
                    FaceTarget();
                }
                else
                {
                    _agent.SetDestination(destination);
                }
            }
            else
            {
                Vector3 toTarget = _target.position - Position;
                toTarget.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up,
                    toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.forward);
                float sign = (_lane & 1) == 0 ? 1f : -1f;
                destination = Position + side * sign * _config.strafeDistance;
                _agent.SetDestination(destination);
                _strafeUntil = now + _config.betweenBurstStrafeSeconds;
            }

            Vector3 localVelocity = transform.InverseTransformDirection(_agent.velocity);
            _animator?.SetMovement(localVelocity);
            return true;
        }

        public void SetFiring(bool firing)
        {
            _firing = firing;
            _animator?.SetAiming(firing);
            if (!firing && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.updateRotation = true;
                _strafeUntil = Time.time + (_config != null ? _config.betweenBurstStrafeSeconds : 0f);
            }
        }

        public void React(in DamageInfo info)
        {
            if (_dead || _config == null) return;
            float now = Time.time;
            _suppressedUntil = Mathf.Max(_suppressedUntil, now + _config.suppressionSeconds);
            if (info.Region is HitRegion.LeftArm or HitRegion.RightArm)
                _aimDisruptedUntil = Mathf.Max(_aimDisruptedUntil, now + _config.aimDisruptionSeconds);
            if (info.Region is HitRegion.LeftLeg or HitRegion.RightLeg)
                _legStumbleUntil = Mathf.Max(_legStumbleUntil, now + _config.legStumbleSeconds);
            _animator?.PlayHit(info.Region, info.Direction);
            _gore?.PresentHit(this, in info, RegionAnchor(info.Region));
            _nextDecisionAt = 0f;
        }

        public bool BeginDeath(in DamageInfo info)
        {
            if (_dead || _config == null) return false;
            _dead = true;
            _firing = false;
            ReleaseCover();
            SetHitboxes(false);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.updateRotation = false;
            }
            _gore?.BeginDeath(this, in info);
            if (_gore == null) ApplyDeathPresentation(in info, info.Kind == DamageKind.Explosive, false, 0f);
            _deathEndsAt = Time.time + (_ragdoll ? _config.ragdollLifetime : _config.corpseLifetime);
            return true;
        }

        public void ApplyDeathPresentation(in DamageInfo info, bool ragdoll, bool dismember, float impulse)
        {
            _ragdoll = ragdoll;
            if (ragdoll)
            {
                _animator?.SetEnabled(false);
                SetRagdoll(true, info.Direction, impulse);
            }
            else
            {
                _animator?.PlayDeath(info.Direction);
            }

            if (dismember) HideRegionForGore(info.Region);
        }

        public void RemoveGorePresentation()
        {
            RestoreHiddenRegions();
            ReleaseAttachedEffects();
        }

        public void EndRagdollEarly()
        {
            if (!_dead || !_ragdoll) return;
            _deathEndsAt = Time.time;
        }

        public void ForceRecycle()
        {
            if (!_dead) return;
            _deathEndsAt = Time.time;
        }

        public void ResetForReuse()
        {
            ReleaseCover();
            ReleaseAttachedEffects();
            RestoreHiddenRegions();
            SetRagdoll(false, Vector3.zero, 0f);
            SetHitboxes(true);
            _animator?.SetEnabled(true);
            _animator?.ResetForReuse();
            _nextDecisionAt = 0f;
            _suppressedUntil = 0f;
            _aimDisruptedUntil = 0f;
            _legStumbleUntil = 0f;
            _strafeUntil = 0f;
            _deathEndsAt = 0f;
            _firing = false;
            _dead = false;
            _ragdoll = false;
        }

        public Transform? RegionAnchor(HitRegion region) => region switch
        {
            HitRegion.Head => _head,
            HitRegion.LeftArm => _leftArm,
            HitRegion.RightArm => _rightArm,
            HitRegion.LeftLeg => _leftLeg,
            HitRegion.RightLeg => _rightLeg,
            _ => transform,
        };

        public void TrackAttachedEffect(PooledObject effect)
        {
            PooledObject? previous = _attachedEffects[_attachedCursor];
            if (previous != null && previous.IsSpawned) _gore?.Release(previous);
            _attachedEffects[_attachedCursor] = effect;
            _attachedCursor = (_attachedCursor + 1) % _attachedEffects.Length;
        }

        private void ChooseCover()
        {
            if (_coverRegistry == null || _controller == null || _target == null || _config == null) return;
            if (_cover != null && _cover.Claimant == _controller) return;
            ReleaseCover();
            _coverRegistry.TryClaimBest(_controller, Position, _target.position,
                _config.coverSearchRadius, _lane, _config.flankLaneBonus,
                _config.coverChecksPerDecision, out _cover);
            if (_cover != null) _lane = _cover.Lane;
        }

        private void FaceTarget()
        {
            if (_target == null || _config == null) return;
            Vector3 direction = _target.position - Position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return;
            if (_agent != null) _agent.updateRotation = false;
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation,
                _config.facingDegreesPerSecond * Time.deltaTime);
        }

        private void ReleaseCover()
        {
            if (_cover != null && _controller != null) _cover.Release(_controller);
            _cover = null;
        }

        private void SetHitboxes(bool enabled)
        {
            for (int i = 0; i < _hitColliders.Length; i++)
            {
                if (_hitColliders[i] != null) _hitColliders[i].enabled = enabled;
            }
        }

        private void SetRagdoll(bool enabled, Vector3 direction, float impulse)
        {
            for (int i = 0; i < _ragdollBodies.Length; i++)
            {
                Rigidbody body = _ragdollBodies[i];
                if (body == null) continue;
                body.isKinematic = !enabled;
                body.useGravity = enabled;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (enabled && impulse > 0f) body.AddForce(direction.normalized * impulse, ForceMode.VelocityChange);
            }
            for (int i = 0; i < _ragdollColliders.Length; i++)
            {
                if (_ragdollColliders[i] != null) _ragdollColliders[i].enabled = enabled;
            }
        }

        private void CaptureBoneScales()
        {
            _boneScales[0] = _head != null ? _head.localScale : Vector3.one;
            _boneScales[1] = _leftArm != null ? _leftArm.localScale : Vector3.one;
            _boneScales[2] = _rightArm != null ? _rightArm.localScale : Vector3.one;
            _boneScales[3] = _leftLeg != null ? _leftLeg.localScale : Vector3.one;
            _boneScales[4] = _rightLeg != null ? _rightLeg.localScale : Vector3.one;
        }

        public void HideRegionForGore(HitRegion region)
        {
            Transform? bone = RegionAnchor(region);
            int index = RegionIndex(region);
            if (bone == null || bone == transform || index < 0) return;
            _hiddenBones[index] = true;
            bone.localScale = Vector3.zero;
        }

        private void RestoreHiddenRegions()
        {
            for (int i = 0; i < _hiddenBones.Length; i++)
            {
                if (!_hiddenBones[i]) continue;
                HitRegion region = i switch
                {
                    0 => HitRegion.Head,
                    1 => HitRegion.LeftArm,
                    2 => HitRegion.RightArm,
                    3 => HitRegion.LeftLeg,
                    _ => HitRegion.RightLeg,
                };
                Transform? bone = RegionAnchor(region);
                if (bone != null) bone.localScale = _boneScales[i];
                _hiddenBones[i] = false;
            }
        }

        private static int RegionIndex(HitRegion region) => region switch
        {
            HitRegion.Head => 0,
            HitRegion.LeftArm => 1,
            HitRegion.RightArm => 2,
            HitRegion.LeftLeg => 3,
            HitRegion.RightLeg => 4,
            _ => -1,
        };

        private void ReleaseAttachedEffects()
        {
            for (int i = 0; i < _attachedEffects.Length; i++)
            {
                PooledObject? effect = _attachedEffects[i];
                if (effect != null && effect.IsSpawned) _gore?.Release(effect);
                _attachedEffects[i] = null;
            }
            _attachedCursor = 0;
        }

#if UNITY_EDITOR
        public void Configure(HumanCombatConfig config, EnemyAnimator animator, Collider[] hitColliders,
            Rigidbody[] ragdollBodies, Collider[] ragdollColliders, Transform head, Transform leftArm,
            Transform rightArm, Transform leftLeg, Transform rightLeg)
        {
            _config = config;
            _animator = animator;
            _hitColliders = hitColliders;
            _ragdollBodies = ragdollBodies;
            _ragdollColliders = ragdollColliders;
            _head = head;
            _leftArm = leftArm;
            _rightArm = rightArm;
            _leftLeg = leftLeg;
            _rightLeg = rightLeg;
        }
#endif
    }
}
