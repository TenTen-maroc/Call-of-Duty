#nullable enable

namespace CoD.Core
{
    /// <summary>
    /// Every number a passive is allowed to touch. Deliberately short: an enum
    /// value with nothing reading it is a promise the shop cannot keep, and the
    /// player finds out by buying it.
    ///
    /// Each entry below is wired to real gameplay code — see StatSheet for the
    /// pipeline and RunContext for who applies it.
    /// </summary>
    public enum Stat
    {
        /// <summary>Player max health. Applied by RunContext; raising it heals to full.</summary>
        MaxHealth = 0,
        /// <summary>Walk/sprint/crouch speed multiplier, applied to PlayerMotor.</summary>
        MoveSpeed = 1,
        /// <summary>Reload speed multiplier — higher is faster, so the duration is divided by it.</summary>
        ReloadSpeed = 2,
        /// <summary>Outgoing weapon damage multiplier.</summary>
        DamageMult = 3,
        /// <summary>Money earned from kills and wave clears.</summary>
        MoneyGainMult = 4,
    }

    public static class StatExtensions
    {
        /// <summary>How many entries the sheet needs. One place to change when a stat is added.</summary>
        public const int Count = 5;
    }
}
