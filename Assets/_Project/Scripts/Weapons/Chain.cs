#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Lightning. Each hit jumps to nearby targets the shot has not already
    /// touched — the already-hit set is the whole reason this does not oscillate
    /// between two drones forever, and it lives on the weapon rather than here
    /// because this asset is shared by every weapon carrying it.
    ///
    /// Chain is the module most likely to be stacked with Explosive, which is
    /// exactly the combination the depth rule exists for: with maxDepth 1 the
    /// chain fires off blast victims once and stops. Raise it deliberately.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_Chain", menuName = "CoD/Effects/Chain", order = 3)]
    public sealed class Chain : EffectModule
    {
        [Tooltip("Targets each hit jumps to.")]
        [Range(1, 6)] public int jumpsPerHit = 2;
        [Tooltip("How far a jump reaches from the hit point.")]
        [Range(1f, 25f)] public float jumpRange = 8f;
        [Tooltip("Damage as a fraction of the hit that caused the jump.")]
        [Range(0.1f, 2f)] public float damageFraction = 0.6f;

        public override void Resolve(in HitContext context, FollowUpBuffer followUps)
        {
            WeaponController shooter = context.Shooter;
            Collider[] buffer = shooter.EffectOverlapBuffer;

            int count = Physics.OverlapSphereNonAlloc(context.Point, jumpRange, buffer, shooter.HitMask,
                QueryTriggerInteraction.Ignore);
            int queued = 0;

            for (int i = 0; i < count && queued < jumpsPerHit; i++)
            {
                Collider hit = buffer[i];
                if (hit == null) continue;
                if (!hit.TryGetComponent(out Health health) || !health.IsAlive) continue;
                if (health == shooter.OwnerHealth) continue;
                // The set that stops a two-drone chain from bouncing forever.
                if (shooter.HasHit(health)) continue;

                Vector3 toTarget = health.transform.position - context.Point;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector3.up;

                followUps.Enqueue(new FollowUp
                {
                    Kind = FollowUpKind.Damage,
                    Origin = context.Point,
                    Direction = toTarget.normalized,
                    Damage = context.DamageDealt * damageFraction,
                    Target = health,
                    Depth = context.Depth + 1,
                });

                // Claimed as soon as it is queued, not when it is applied: two
                // jumps resolved in the same pass would otherwise both pick the
                // nearest drone.
                shooter.MarkHit(health);
                queued++;
            }
        }
    }
}
