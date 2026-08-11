#nullable enable

namespace CoD.Enemies
{
    /// <summary>
    /// The three-attacker rule, as an interface. However many drones are alive,
    /// only a few may be ACTIVELY attacking at once; the rest close, circle and
    /// wait. Without it twenty enemies means instant death and the fight has no
    /// shape — with it, twenty enemies feels fair.
    ///
    /// The drone asks for a token before it commits to an attack, and releases it
    /// through exactly one exit path (death, despawn, or finishing the attack) —
    /// a leaked token permanently shrinks the pool and the horde slowly turns
    /// into a staring contest.
    /// </summary>
    public interface IAttackTokenSource
    {
        bool TryAcquire(DroneController drone);
        void Release(DroneController drone);
    }

    /// <summary>
    /// Grants every request. Used before the wave system exists, and by the
    /// sandbox when the cap is cheated off — the drone code never learns which
    /// source it is talking to.
    /// </summary>
    public sealed class UnlimitedAttackTokens : IAttackTokenSource
    {
        public bool TryAcquire(DroneController drone) => true;
        public void Release(DroneController drone) { }
    }
}
