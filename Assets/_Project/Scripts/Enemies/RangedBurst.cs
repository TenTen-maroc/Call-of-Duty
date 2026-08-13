#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// The Shooter's attack: hold range, take a beat to react, then fire a short
    /// burst of slow projectiles.
    ///
    /// THE FIRST SHOT MISSES ON PURPOSE. It is offset past the player rather than
    /// aimed at them, and that single decision is the difference between "I died
    /// from nowhere" and "I got caught out" — the same damage event, a completely
    /// different feeling, and the only warning a player gets that something is
    /// shooting at them from across the arena. Turn it off and the Shooter
    /// immediately feels unfair, which is exactly what the option is for: it makes
    /// the reason visible instead of folkloric.
    ///
    /// Stateless like every AttackModule; the burst counter and cooldown live in
    /// the drone's DroneAttackState.
    /// </summary>
    [CreateAssetMenu(fileName = "RangedBurst_", menuName = "CoD/Attacks/Ranged Burst", order = 1)]
    public sealed class RangedBurst : AttackModule
    {
        [Header("Engagement")]
        [Tooltip("Fires from anywhere inside this. Pair it with DroneConfig.preferredRange so the drone holds the distance it shoots from.")]
        [Range(2f, 40f)] public float triggerRange = 16f;
        [Tooltip("Beat between deciding to shoot and shooting. 300-500 ms is the readable window.")]
        [Range(0f, 2f)] public float reactionDelay = 0.4f;
        [Tooltip("Aim height above the drone's target transform, which sits at the player's feet.")]
        [Range(0f, 3f)] public float aimHeightOffset = 1.2f;

        [Header("Burst")]
        [Range(1, 8)] public int burstCount = 3;
        [Range(0.05f, 1f)] public float burstInterval = 0.18f;
        [Range(0.2f, 6f)] public float cooldown = 1.6f;
        [Tooltip("0 disables reloads. Humans use three bursts per magazine; drones may leave it at zero.")]
        [Range(0, 12)] public int reloadEveryBursts;
        [Range(0.1f, 4f)] public float reloadSeconds = 1.4f;

        [Header("Accuracy")]
        [Tooltip("1 = dead on. Lower widens the cone; 0.7 is the arcade-fair number.")]
        [Range(0f, 1f)] public float accuracy = 0.7f;
        [Tooltip("Cone width at zero accuracy, in degrees.")]
        [Range(0f, 30f)] public float maxSpreadDegrees = 9f;
        [Tooltip("The opening shot is thrown wide by this much, deliberately. It is a warning, not a mistake.")]
        [Range(0f, 30f)] public float firstShotMissDegrees = 7f;
        public bool firstShotDeliberateMiss = true;

        [Header("Projectile")]
        [Range(1f, 100f)] public float damage = 12f;
        [Tooltip("Slow enough to sidestep once seen. Hitscan enemies are unreadable and unavoidable at the same time.")]
        [Range(4f, 60f)] public float projectileSpeed = 18f;
        [Range(0.5f, 10f)] public float projectileLifetime = 3f;
        [Tooltip("Pooled prefab carrying Projectile.")]
        public GameObject? projectilePrefab;
        [Tooltip("Physics.DefaultRaycastLayers, not Everything: it excludes Ignore Raycast, which is where spent shell casings live. Everything let the player's own brass absorb incoming fire and eat blast slots.")]
        public LayerMask hitMask = Physics.DefaultRaycastLayers;
        public AudioClip? fireClip;

        public override float TriggerRange => triggerRange;

        public override void Tick(DroneController drone, ref DroneAttackState state, float now, float deltaTime)
        {
            switch (state.Phase)
            {
                case DroneAttackPhase.Idle:
                    if (now < state.NextAttackAt) return;
                    if (drone.SqrDistanceToTarget() > triggerRange * triggerRange) return;
                    if (!drone.TryAcquireAttackToken(ref state)) return;

                    state.Phase = DroneAttackPhase.Windup;
                    drone.SetFiringPosture(true);
                    state.PhaseEndsAt = now + reactionDelay;
                    break;

                case DroneAttackPhase.Windup:
                    // The core brightens through the reaction delay: the visible
                    // half of the same warning the first shot gives.
                    float progress = reactionDelay <= 0f ? 1f
                        : 1f - Mathf.Clamp01((state.PhaseEndsAt - now) / reactionDelay);
                    drone.SetTelegraph(progress);
                    if (now < state.PhaseEndsAt) return;

                    drone.PlayAttackAnimation();
                    state.Phase = DroneAttackPhase.Firing;
                    state.BurstRemaining = Mathf.Max(1, burstCount);
                    state.PhaseEndsAt = now;   // first round leaves immediately
                    break;

                case DroneAttackPhase.Firing:
                    if (now < state.PhaseEndsAt) return;

                    FireOne(drone, ref state);
                    state.BurstRemaining--;
                    state.PhaseEndsAt = now + burstInterval;

                    if (state.BurstRemaining > 0) return;

                    drone.SetTelegraph(0f);
                    state.Phase = DroneAttackPhase.Idle;
                    state.CompletedBursts++;
                    bool reload = reloadEveryBursts > 0 && state.CompletedBursts % reloadEveryBursts == 0;
                    if (reload) drone.PlayReloadAnimation();
                    state.NextAttackAt = now + (reload ? reloadSeconds : cooldown);
                    // Hand the token back the moment the burst ends, so the next
                    // drone in the pack gets its turn instead of waiting out a
                    // cooldown it is not serving.
                    drone.ReleaseAttackToken(ref state);
                    drone.SetFiringPosture(false);
                    break;
            }
        }

        public override void Cancel(DroneController drone, ref DroneAttackState state)
        {
            state.Phase = DroneAttackPhase.Idle;
            state.BurstRemaining = 0;
            drone.SetTelegraph(0f);
            drone.SetFiringPosture(false);
        }

        private void FireOne(DroneController drone, ref DroneAttackState state)
        {
            ObjectPool? pool = drone.Pool;
            Transform? target = drone.Target;
            if (pool == null || target == null || projectilePrefab == null) return;

            Vector3 origin = drone.Position;
            Vector3 aimPoint = target.position + Vector3.up * aimHeightOffset;
            Vector3 direction = aimPoint - origin;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            bool deliberateMiss = firstShotDeliberateMiss && !state.HasAttackedOnce;
            direction = deliberateMiss
                // Thrown wide on a fixed angle rather than randomly: a warning
                // shot has to miss reliably, and a random cone eventually kills
                // the player with the shot that was supposed to teach them.
                ? Quaternion.AngleAxis(firstShotMissDegrees, Vector3.up) * direction
                : ApplyAccuracyCone(direction);

            state.HasAttackedOnce = true;

            PooledObject instance = pool.Spawn(projectilePrefab, origin + direction * 0.6f,
                Quaternion.LookRotation(direction));
            if (instance.TryGetComponent(out Projectile projectile))
            {
                // No sink and no payload: a drone's round is simple enough to
                // resolve itself, and carrying its own damage number is the whole
                // of its damage model. The player's launcher is the case that
                // needs the other half — see IProjectileImpactSink.
                projectile.Launch(new ProjectileShot
                {
                    Pool = pool,
                    Velocity = direction * projectileSpeed,
                    Damage = damage,
                    Lifetime = projectileLifetime,
                    HitMask = hitMask,
                    FiredBy = Faction.Hostile,
                    Owner = drone.HealthComponent,
                });
            }
            else
            {
                GameLog.Error($"'{projectilePrefab.name}' has no Projectile component.", projectilePrefab);
                pool.Despawn(instance);
            }

            drone.PlayCue(fireClip);
        }

        private Vector3 ApplyAccuracyCone(Vector3 forward)
        {
            float degrees = Mathf.Lerp(maxSpreadDegrees, 0f, Mathf.Clamp01(accuracy));
            if (degrees <= 0.01f) return forward;

            float radians = degrees * Mathf.Deg2Rad;
            float theta = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Random.value) * Mathf.Tan(radians);

            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, forward);

            return (forward + (right * Mathf.Cos(theta) + up * Mathf.Sin(theta)) * radius).normalized;
        }
    }
}
