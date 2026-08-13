#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// The seam between a drone and a rigged humanoid, and the ONLY new code a
    /// human soldier needs.
    ///
    /// A soldier is a `DroneConfig` + an `AttackModule` + a prefab — the same
    /// data a drone is. What it additionally has is a skeleton, and this is the
    /// three calls the controller makes to drive one: how fast it is moving, how
    /// far into an attack windup it is, and that it has died. Everything else —
    /// pathing, tokens, pooling, damage, the registry — is identical, which is
    /// the whole reason the drone layer was kept rather than renamed.
    ///
    /// Null on every drone prefab. `DroneController` null-checks all three call
    /// sites, so a cube pays nothing for a component it does not have.
    ///
    /// THE TELEGRAPH IS THE POINT. `SetTelegraph` is the fairness contract of
    /// the whole enemy design — the difference between "I died from nowhere" and
    /// "I got caught out". A drone expresses it as an emission ramp on its glowing
    /// core; a human has no glowing core, so it expresses the same 0..1 value as
    /// a POSE. That is the strongest argument for an Animator over procedural
    /// motion here: a big, distinct windup pose held for the ~0.4 s reaction
    /// delay is readable at 25 m, and nothing procedural is.
    ///
    /// ROOT MOTION IS FORCED OFF. The NavMeshAgent owns movement. An imported
    /// humanoid clip that also drives position fights the agent for it, and the
    /// result is the classic Unity humanoid that slides, moonwalks, or drifts off
    /// the navmesh entirely — a bug that looks like broken AI rather than a
    /// broken import setting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnimator : MonoBehaviour
    {
        [SerializeField] private Animator? _animator = null;
        [SerializeField] private HumanCombatConfig? _config = null;

        // Hashes, not strings. Animator.SetFloat(string) hashes on every call and
        // this runs per enemy per frame; `static readonly` is also the one form
        // of static the mutable-statics guard allows, and the established idiom
        // in this assembly (see DroneController's shader property ids).
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int TelegraphId = Animator.StringToHash("Telegraph");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int DeathId = Animator.StringToHash("Death");
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveYId = Animator.StringToHash("MoveY");
        private static readonly int AimingId = Animator.StringToHash("Aiming");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int HitRegionId = Animator.StringToHash("HitRegion");
        private static readonly int HitDirectionId = Animator.StringToHash("HitDirection");
        private static readonly int DeathDirectionId = Animator.StringToHash("DeathDirection");
        private static readonly int ReloadId = Animator.StringToHash("Reload");

        private void Awake()
        {
            if (_animator == null) return;
            _animator.applyRootMotion = false;
            // Keeps a soldier walking behind the player in the right PLACE while
            // skipping the expensive pose write. CullCompletely would freeze the
            // Animator outright, so an enemy that walked out of view would stop
            // moving and be waiting exactly where you left it.
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }

        /// <summary>Called every frame the enemy is steering. 0..1 into the locomotion blend.</summary>
        public void SetSpeed(float planarSpeed)
        {
            if (_animator == null) return;
            if (_config == null) return;
            float normalised = Mathf.Clamp01(planarSpeed / _config.speedAtFullBlend);
            _animator.SetFloat(SpeedId, normalised, _config.speedDamping, Time.deltaTime);
        }

        public void SetMovement(Vector3 localVelocity)
        {
            if (_animator == null || _config == null) return;
            float scale = Mathf.Max(0.1f, _config.speedAtFullBlend);
            _animator.SetFloat(MoveXId, Mathf.Clamp(localVelocity.x / scale, -1f, 1f),
                _config.speedDamping, Time.deltaTime);
            _animator.SetFloat(MoveYId, Mathf.Clamp(localVelocity.z / scale, -1f, 1f),
                _config.speedDamping, Time.deltaTime);
            SetSpeed(new Vector2(localVelocity.x, localVelocity.z).magnitude);
        }

        public void SetAiming(bool aiming)
        {
            if (_animator != null) _animator.SetBool(AimingId, aiming);
        }

        /// <summary>
        /// The same 0..1 the emission ramp gets. Held as a float rather than
        /// fired as a trigger so the pose can BLEND IN across the windup — a
        /// telegraph that snaps on at the last frame is not a telegraph.
        /// </summary>
        public void SetTelegraph(float amount)
        {
            if (_animator == null) return;
            _animator.SetFloat(TelegraphId, Mathf.Clamp01(amount));
        }

        /// <summary>The windup is over and the attack is committed.</summary>
        public void PlayAttack()
        {
            if (_animator == null) return;
            _animator.SetTrigger(AttackId);
        }

        /// <summary>
        /// Death. Deliberately a trigger and not a state the controller polls:
        /// the drone lifecycle retires through exactly one path, and the
        /// animation is a consequence of that, never a gate on it.
        /// </summary>
        public void PlayDeath()
            => PlayDeath(Vector3.back);

        public void PlayDeath(Vector3 incomingDirection)
        {
            if (_animator == null) return;
            _animator.ResetTrigger(AttackId);
            _animator.SetInteger(DeathDirectionId, DirectionIndex(incomingDirection));
            _animator.SetTrigger(DeathId);
        }

        public void PlayHit(HitRegion region, Vector3 incomingDirection)
        {
            if (_animator == null) return;
            _animator.SetInteger(HitRegionId, (int)region);
            _animator.SetInteger(HitDirectionId, DirectionIndex(incomingDirection));
            _animator.SetTrigger(HitId);
        }

        public void PlayReload()
        {
            if (_animator != null) _animator.SetTrigger(ReloadId);
        }

        public void SetEnabled(bool enabled)
        {
            if (_animator != null) _animator.enabled = enabled;
        }

        /// <summary>
        /// Back to a clean pose for the pool. Pooled objects are REUSED, so a
        /// soldier respawning mid-death-animation is the exact class of bug the
        /// pool's generation counter exists to prevent elsewhere.
        /// </summary>
        public void ResetForReuse()
        {
            if (_animator == null) return;
            _animator.ResetTrigger(DeathId);
            _animator.ResetTrigger(AttackId);
            _animator.ResetTrigger(HitId);
            _animator.ResetTrigger(ReloadId);
            _animator.SetFloat(SpeedId, 0f);
            _animator.SetFloat(MoveXId, 0f);
            _animator.SetFloat(MoveYId, 0f);
            _animator.SetFloat(TelegraphId, 0f);
            _animator.SetBool(AimingId, false);
            _animator.enabled = true;
            _animator.Rebind();
        }

        private int DirectionIndex(Vector3 incomingDirection)
        {
            if (_animator == null) return 0;
            Vector3 local = _animator.transform.InverseTransformDirection(incomingDirection);
            if (Mathf.Abs(local.x) > Mathf.Abs(local.z)) return local.x >= 0f ? 1 : 3;
            return local.z >= 0f ? 0 : 2;
        }

#if UNITY_EDITOR
        public void Configure(Animator animator, HumanCombatConfig config)
        {
            _animator = animator;
            _config = config;
        }
#endif
    }
}
