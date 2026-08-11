#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Every hit detonates. The crowd-clearing module, and the one most likely to
    /// be stacked with something else — which is exactly why it queues damage
    /// instead of applying it: the weapon decides what a blast victim triggers,
    /// and the depth rule decides whether it triggers anything at all.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_Explosive", menuName = "CoD/Effects/Explosive", order = 0)]
    public sealed class Explosive : EffectModule
    {
        [Header("Blast")]
        [Range(0.5f, 12f)] public float radius = 3f;
        [Tooltip("Blast damage as a fraction of the shot that caused it, so it scales with the weapon rather than replacing it.")]
        [Range(0.1f, 3f)] public float damageFraction = 0.8f;
        [Tooltip("Damage multiplier at the edge of the blast.")]
        [Range(0f, 1f)] public float minMultiplier = 0.35f;

        [Header("Feedback")]
        [Tooltip("Pooled. The drones' explosion prefab is reused here — it already carries its own sound.")]
        public GameObject? explosionVfx;
        [Range(0.1f, 4f)] public float explosionLifetime = 1f;

        public override void Resolve(in HitContext context, FollowUpBuffer followUps)
        {
            WeaponController shooter = context.Shooter;

            if (explosionVfx != null && shooter.EffectPool != null)
            {
                shooter.EffectPool.SpawnForSeconds(explosionVfx, context.Point, Quaternion.identity, explosionLifetime);
            }

            Collider[] buffer = shooter.EffectOverlapBuffer;
            int count = Physics.OverlapSphereNonAlloc(context.Point, radius, buffer, shooter.HitMask,
                QueryTriggerInteraction.Ignore);

            // A FULL buffer means an arbitrary subset came back and the rest
            // is gone, with no overflow flag to read. Blast.Apply already says so
            // on the drone side; a blast quietly reaching nobody in a crowd is the
            // same failure and was the only one of the three that stayed silent.
            if (count >= buffer.Length)
            {
                GameLog.Warn($"{name} filled its {buffer.Length}-collider buffer — " +
                    "targets past that were dropped. Raise WeaponController's effect overlap buffer.", this);
            }

            for (int i = 0; i < count; i++)
            {
                Collider hit = buffer[i];
                if (hit == null) continue;

                // Root colliders carrying Health only: weakpoint children would
                // match the same target twice, and an explosion does not headshot.
                if (!hit.TryGetComponent(out Health health) || !health.IsAlive) continue;
                // The target that took the original bullet is already paid for.
                if (shooter.HasHit(health)) continue;
                // The player standing next to their own explosive rounds is a
                // different design decision; this one says no self-damage.
                if (health == shooter.OwnerHealth) continue;

                float distance = Vector3.Distance(context.Point, hit.ClosestPoint(context.Point));
                float falloff = Mathf.Lerp(1f, minMultiplier, Mathf.Clamp01(distance / Mathf.Max(0.01f, radius)));

                Vector3 toTarget = health.transform.position - context.Point;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector3.up;

                followUps.Enqueue(new FollowUp
                {
                    Kind = FollowUpKind.Damage,
                    Origin = context.Point,
                    Direction = toTarget.normalized,
                    Damage = context.DamageDealt * damageFraction * falloff,
                    Target = health,
                    Depth = context.Depth + 1,
                });

                // Claimed the moment it is queued, exactly as Chain does. This
                // asset ships maxDepth 1, so each blast victim detonates again —
                // and without the claim those secondary blasts kept re-finding the
                // same neighbours the first one had already queued. One round put
                // several full blasts on one drone, and a Chain sharing the weapon
                // re-hit them all a third time. The already-hit set is the whole
                // mechanism that stops it; Explosive simply was not using it.
                shooter.MarkHit(health);
            }
        }
    }
}
