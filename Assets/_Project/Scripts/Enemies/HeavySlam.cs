#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// The Tank's attack: plant, telegraph for the best part of a second, then hit
    /// a wide radius hard.
    ///
    /// The whole point of the archetype is that it is not a trade. A Tank has too
    /// much health to burn down while standing next to it, and the slam does too
    /// much damage to eat — so the correct answer is to move, keep shooting, and
    /// come back. That only reads if the windup is long enough to leave during and
    /// obvious enough to notice, which is why the drone nearly stops moving while
    /// it charges.
    /// </summary>
    [CreateAssetMenu(fileName = "HeavySlam_", menuName = "CoD/Attacks/Heavy Slam", order = 2)]
    public sealed class HeavySlam : AttackModule
    {
        [Header("Trigger")]
        [Range(1f, 12f)] public float triggerRadius = 3.2f;
        [Tooltip("The window to leave in. Below ~0.6s it stops being a decision.")]
        [Range(0.2f, 3f)] public float windupSeconds = 0.9f;
        [Tooltip("Speed while charging. Near-zero is what makes the commit readable.")]
        [Range(0f, 1f)] public float windupSpeedMultiplier = 0.15f;
        [Range(0.2f, 8f)] public float cooldown = 2.5f;

        [Header("Slam")]
        [Range(1f, 200f)] public float damage = 34f;
        [Tooltip("Wider than the trigger, so backing up one step is not enough — you have to actually leave.")]
        [Range(1f, 14f)] public float slamRadius = 4.5f;
        [Range(0f, 1f)] public float minMultiplier = 0.4f;
        public LayerMask damageMask = ~0;

        [Header("Feedback")]
        [Tooltip("Pooled, spawned at the drone's feet when the slam lands. Carries its own AudioSource.")]
        public GameObject? slamVfx;
        [Range(0.1f, 4f)] public float slamVfxLifetime = 1f;
        [Tooltip("Played on the drone as the windup starts — the audible half of the telegraph.")]
        public AudioClip? windupClip;

        public override float TriggerRange => triggerRadius;

        public override void Tick(DroneController drone, ref DroneAttackState state, float now, float deltaTime)
        {
            switch (state.Phase)
            {
                case DroneAttackPhase.Idle:
                    if (now < state.NextAttackAt) return;
                    if (drone.SqrDistanceToTarget() > triggerRadius * triggerRadius) return;
                    if (!drone.TryAcquireAttackToken(ref state)) return;

                    state.Phase = DroneAttackPhase.Windup;
                    state.PhaseEndsAt = now + windupSeconds;
                    drone.SetSpeedMultiplier(windupSpeedMultiplier);
                    drone.PlayCue(windupClip);
                    break;

                case DroneAttackPhase.Windup:
                    float remaining = state.PhaseEndsAt - now;
                    drone.SetTelegraph(1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, windupSeconds)));
                    if (remaining > 0f) return;

                    Slam(drone);
                    state.HasAttackedOnce = true;
                    state.Phase = DroneAttackPhase.Idle;
                    state.NextAttackAt = now + cooldown;
                    drone.SetSpeedMultiplier(1f);
                    drone.SetTelegraph(0f);
                    // Released immediately: the cooldown is this drone's problem,
                    // not the pack's.
                    drone.ReleaseAttackToken(ref state);
                    break;
            }
        }

        public override void Cancel(DroneController drone, ref DroneAttackState state)
        {
            // Killed mid-windup means no slam — same reward as killing a lit
            // Rusher: the player beat the telegraph.
            state.Phase = DroneAttackPhase.Idle;
            drone.SetSpeedMultiplier(1f);
            drone.SetTelegraph(0f);
        }

        private void Slam(DroneController drone)
        {
            Vector3 origin = drone.Position;
            if (drone.Pool != null && slamVfx != null)
            {
                drone.Pool.SpawnForSeconds(slamVfx, origin, Quaternion.identity, slamVfxLifetime);
            }
            Blast.Apply(drone, origin, slamRadius, damage, minMultiplier, damageMask);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Same rule as the Rusher's blast: a slam smaller than its own trigger
            // charges up and hits nothing.
            if (slamRadius < triggerRadius) slamRadius = triggerRadius;
        }
#endif
    }
}
