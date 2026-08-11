#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// The odd one out, and deliberately so: Pierce changes the RAY, not the
    /// aftermath. Ricochet and Chain work by queuing follow-ups after a hit
    /// resolves; a piercing bullet has to keep going through the first target
    /// during the same cast, so this module contributes a ray budget the weapon
    /// reads before it fires and a damage falloff it applies per target passed.
    ///
    /// Resolve does nothing at all. That is not an oversight — there is no
    /// after-effect to produce.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_Pierce", menuName = "CoD/Effects/Pierce", order = 1)]
    public sealed class Pierce : EffectModule
    {
        [Tooltip("Extra targets beyond the first. 2 = three bodies on a good line.")]
        [Range(1, 8)] public int maxTargets = 2;
        [Tooltip("Damage multiplier per target already passed through. 0.75 = the third body takes about half.")]
        [Range(0.1f, 1f)] public float damageFalloffPerTarget = 0.75f;

        public override int ExtraRayBudget => maxTargets;
        public override float PierceDamageFalloff => damageFalloffPerTarget;

        public override void Resolve(in HitContext context, FollowUpBuffer followUps)
        {
            // Nothing. The effect happened in the cast.
        }
    }
}
