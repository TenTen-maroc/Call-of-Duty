#nullable enable

namespace CoD.Core
{
    /// <summary>
    /// Anything a weapon can hurt. The weapon does not care whether it hit a
    /// drone, a training dummy, or the player — which is what keeps the weapon
    /// system free of enemy-specific code.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        /// <summary>Returns the damage actually applied, after clamping to remaining health.</summary>
        float ApplyDamage(in DamageInfo info);
    }
}
