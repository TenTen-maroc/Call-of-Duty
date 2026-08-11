#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// The bullet bounces. Works through follow-up RAYS rather than by picking a
    /// target, which is what makes it feel physical instead of homing: it reflects
    /// off the surface it hit and finds whatever is actually there.
    ///
    /// A bounce off a drone (which has no meaningful surface normal to reflect
    /// from) uses the shot's own direction mirrored around the hit normal too —
    /// the result is a spray into the crowd, which is the point.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_Ricochet", menuName = "CoD/Effects/Ricochet", order = 2)]
    public sealed class Ricochet : EffectModule
    {
        [Tooltip("Bounces produced per hit. Each bounce resolves one depth deeper, so maxDepth caps the real chain length too.")]
        [Range(1, 4)] public int bouncesPerHit = 1;
        [Tooltip("How far a bounced round travels before giving up.")]
        [Range(1f, 40f)] public float bounceRange = 12f;
        [Tooltip("Damage as a fraction of the shot that caused the bounce.")]
        [Range(0.1f, 2f)] public float damageFraction = 0.7f;
        [Tooltip("Random spread on the reflected direction, in degrees. A perfectly mirrored bounce reads as a bug.")]
        [Range(0f, 30f)] public float scatterDegrees = 8f;

        public override void Resolve(in HitContext context, FollowUpBuffer followUps)
        {
            Vector3 incoming = context.Direction;
            Vector3 normal = context.Normal.sqrMagnitude > 0.0001f ? context.Normal : -incoming;

            for (int i = 0; i < bouncesPerHit; i++)
            {
                Vector3 reflected = Vector3.Reflect(incoming, normal).normalized;
                if (scatterDegrees > 0.01f)
                {
                    reflected = Quaternion.Euler(
                        Random.Range(-scatterDegrees, scatterDegrees),
                        Random.Range(-scatterDegrees, scatterDegrees),
                        0f) * reflected;
                }

                followUps.Enqueue(new FollowUp
                {
                    Kind = FollowUpKind.Ray,
                    // Nudged off the surface, or the bounce immediately re-hits
                    // the thing it just left.
                    Origin = context.Point + normal * 0.05f,
                    Direction = reflected,
                    Damage = context.DamageDealt * damageFraction,
                    Range = bounceRange,
                    Depth = context.Depth + 1,
                });
            }
        }
    }
}
