#nullable enable
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

        [Tooltip("Planar speed that maps to 1.0 on the locomotion blend tree. Usually the archetype's sprint speed.")]
        [Min(0.1f)] [SerializeField] private float _speedAtFullBlend = 8f;

        [Tooltip("Seconds of damping on the locomotion blend. Zero makes a soldier snap between poses on every repath.")]
        [Range(0f, 0.5f)] [SerializeField] private float _speedDamping = 0.12f;

        // Hashes, not strings. Animator.SetFloat(string) hashes on every call and
        // this runs per enemy per frame; `static readonly` is also the one form
        // of static the mutable-statics guard allows, and the established idiom
        // in this assembly (see DroneController's shader property ids).
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int TelegraphId = Animator.StringToHash("Telegraph");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int DeathId = Animator.StringToHash("Death");

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
            float normalised = Mathf.Clamp01(planarSpeed / _speedAtFullBlend);
            _animator.SetFloat(SpeedId, normalised, _speedDamping, Time.deltaTime);
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
        {
            if (_animator == null) return;
            _animator.ResetTrigger(AttackId);
            _animator.SetTrigger(DeathId);
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
            _animator.SetFloat(SpeedId, 0f);
            _animator.SetFloat(TelegraphId, 0f);
            _animator.Rebind();
        }
    }
}
