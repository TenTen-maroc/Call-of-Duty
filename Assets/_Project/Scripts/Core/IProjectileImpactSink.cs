#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Whoever owns what an impact MEANS, for a projectile that is not allowed to
    /// decide for itself.
    ///
    /// A drone's round is simple enough to resolve alone: it carries a damage
    /// number and applies it. The player's launcher is not. One rocket has to run
    /// falloff, the stat sheet, the cheat multiplier, the weakpoint bonus, the
    /// impact VFX, the surface sound, the hitmarker rule and the whole ordered
    /// effect-module list — every one of which already lives in
    /// <c>WeaponController.ResolveHit</c>, and none of which may be forked into a
    /// second implementation that drifts.
    ///
    /// So the projectile stays dumb and hands the impact back. The sink is an
    /// INTERFACE REFERENCE rather than a delegate on purpose: a lambda closing
    /// over the shooter would allocate per shot, on the firing path.
    ///
    /// ⚠️ A sink is very likely a MonoBehaviour, and an interface-typed reference
    /// cannot see Unity's "destroyed but not collected" state — `sink != null` is
    /// true for a component whose GameObject was torn down by a scene change while
    /// the rocket was still in the air. <see cref="Projectile"/> casts back to
    /// <see cref="Object"/> to ask properly; anything else holding one of these
    /// must do the same.
    /// </summary>
    public interface IProjectileImpactSink
    {
        /// <summary>
        /// Called exactly once, on the frame the projectile stopped, immediately
        /// before it returns to the pool. Read
        /// <see cref="Projectile.Payload"/> for the config that fired it — never
        /// the weapon's CURRENT one, which a swap may already have replaced.
        /// </summary>
        void OnProjectileImpact(Projectile projectile, in RaycastHit hit, Vector3 direction);
    }
}
