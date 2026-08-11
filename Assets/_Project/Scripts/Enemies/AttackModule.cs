#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// A drone's attack, as data. Stateless by contract: the asset holds only
    /// numbers, and every per-drone value travels in the <see cref="DroneAttackState"/>
    /// passed by ref. Drone #4 is then a DroneConfig plus one of these — data,
    /// never new code.
    ///
    /// The same discipline as EffectModule on the weapon side, for the same
    /// reason: one asset is shared by every drone that uses it, and configs are
    /// read-only at runtime (Domain Reload is off, so a runtime write to an asset
    /// survives into the next Play session and rewrites the balance).
    /// </summary>
    public abstract class AttackModule : ScriptableObject
    {
        /// <summary>
        /// How close the drone must be before this attack triggers. The drone's
        /// movement code reads it so approach and attack agree without the
        /// controller knowing which module it carries.
        /// </summary>
        public abstract float TriggerRange { get; }

        /// <summary>Called every frame by the drone that owns <paramref name="state"/>.</summary>
        public abstract void Tick(DroneController drone, ref DroneAttackState state, float now, float deltaTime);

        /// <summary>
        /// Called when the drone leaves play mid-attack (shot down during a
        /// windup, wave cleared, pool reclaimed it). Undo anything the windup
        /// started; the token itself is released by the drone.
        /// </summary>
        public virtual void Cancel(DroneController drone, ref DroneAttackState state) { }
    }
}
