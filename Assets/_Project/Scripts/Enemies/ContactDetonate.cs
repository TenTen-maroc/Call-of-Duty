#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// The Rusher's attack: close to contact, arm with an audible and visible
    /// fuse, then detonate. The fuse is the whole design — an enemy that removes
    /// a third of your health the instant it touches you is a coin flip, while the
    /// same enemy with half a second of warning is a decision (shoot it, or move).
    ///
    /// Stateless, like every AttackModule: this asset is shared by every rusher in
    /// the wave, so the fuse timer lives in the drone's DroneAttackState.
    /// </summary>
    [CreateAssetMenu(fileName = "ContactDetonate_", menuName = "CoD/Attacks/Contact Detonate", order = 0)]
    public sealed class ContactDetonate : AttackModule
    {
        [Header("Trigger")]
        [Tooltip("Distance at which the drone commits. Wider than the blast so the player can still outrun a lit fuse.")]
        [Range(0.5f, 8f)] public float triggerRadius = 2.2f;
        [Tooltip("The warning. Below ~0.35s the player cannot react; above ~0.8s the drone is free damage.")]
        [Range(0.1f, 3f)] public float fuseSeconds = 0.55f;
        [Tooltip("Speed multiplier while the fuse burns — the commit reads as a lunge, not a stroll.")]
        [Range(0.5f, 3f)] public float lungeSpeedMultiplier = 1.35f;

        [Header("Blast")]
        [Tooltip("Full damage at the centre. 24 of 100 means three detonations to kill, so two mistakes are survivable.")]
        [Range(1f, 200f)] public float damage = 24f;
        [Range(0.5f, 12f)] public float blastRadius = 3.5f;
        [Tooltip("Damage multiplier at the very edge of the blast.")]
        [Range(0f, 1f)] public float minBlastMultiplier = 0.33f;
        [Tooltip("What the blast can damage. Leave everything on for the grey box.")]
        public LayerMask damageMask = ~0;

        [Header("Feedback")]
        [Tooltip("Pooled. Carries its own AudioSource — the drone deactivates on despawn, so a clip played on the drone would be cut off.")]
        public GameObject? explosionVfx;
        [Range(0.1f, 4f)] public float explosionLifetime = 1.2f;
        [Tooltip("Played on the drone the moment the fuse lights. The audible half of the telegraph.")]
        public AudioClip? alertClip;

        public override float TriggerRange => triggerRadius;

        public override void Tick(DroneController drone, ref DroneAttackState state, float now, float deltaTime)
        {
            switch (state.Phase)
            {
                case DroneAttackPhase.Idle:
                    if (drone.SqrDistanceToTarget() > triggerRadius * triggerRadius) return;
                    // A token is what caps how many drones may be mid-attack at
                    // once. Denied means keep chasing, not stop.
                    if (!drone.TryAcquireAttackToken(ref state)) return;

                    state.Phase = DroneAttackPhase.Windup;
                    state.PhaseEndsAt = now + fuseSeconds;
                    drone.SetSpeedMultiplier(lungeSpeedMultiplier);
                    drone.PlayCue(alertClip);
                    break;

                case DroneAttackPhase.Windup:
                    float remaining = state.PhaseEndsAt - now;
                    drone.SetTelegraph(1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, fuseSeconds)));
                    if (remaining > 0f) return;

                    Detonate(drone);
                    state.HasAttackedOnce = true;
                    // SelfDestruct routes through the drone's single exit path, so
                    // the token comes back and the pool slot is freed exactly once.
                    drone.SelfDestruct();
                    break;
            }
        }

        public override void Cancel(DroneController drone, ref DroneAttackState state)
        {
            // Shot down mid-fuse: the blast does NOT go off. Killing a lit rusher
            // has to be a reward, or there is no reason to shoot one.
            state.Phase = DroneAttackPhase.Idle;
            drone.SetSpeedMultiplier(1f);
            drone.SetTelegraph(0f);
        }

        private void Detonate(DroneController drone)
        {
            Vector3 origin = drone.Position;

            if (drone.Pool != null && explosionVfx != null)
            {
                drone.Pool.SpawnForSeconds(explosionVfx, origin, Quaternion.identity, explosionLifetime);
            }

            // Shared with the Tank's slam. Both are radial damage and must resolve
            // identically; two copies would drift.
            Blast.Apply(drone, origin, blastRadius, damage, minBlastMultiplier, damageMask);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // A blast SMALLER than the trigger means the drone commits from
            // outside its own kill radius: the fuse burns, the bang goes off, and
            // the player standing still takes nothing. Confusing rather than fair.
            if (blastRadius < triggerRadius)
            {
                blastRadius = triggerRadius;
            }
        }
#endif
    }
}
