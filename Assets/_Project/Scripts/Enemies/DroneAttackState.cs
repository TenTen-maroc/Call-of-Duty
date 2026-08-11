#nullable enable

namespace CoD.Enemies
{
    /// <summary>Where a drone is in its attack cycle. One shape for every archetype.</summary>
    public enum DroneAttackPhase
    {
        /// <summary>Chasing or holding range; no token held.</summary>
        Idle,
        /// <summary>Committed and telegraphing. The player's window to react.</summary>
        Windup,
        /// <summary>Mid-burst: shots are leaving on their own cadence.</summary>
        Firing,
        /// <summary>Attack spent, waiting out its cooldown.</summary>
        Recover,
    }

    /// <summary>
    /// Every scrap of per-drone attack state. It lives HERE, in a struct the drone
    /// owns, because AttackModules are ScriptableObjects — one asset is shared by
    /// every drone using it, so a module that stored a fuse timer on itself would
    /// have all forty rushers detonating on whichever one armed last.
    ///
    /// C# note: a plain mutable struct passed by `ref`. No allocation per drone
    /// per frame, and the module mutates the caller's copy rather than a clone.
    /// </summary>
    public struct DroneAttackState
    {
        public DroneAttackPhase Phase;
        /// <summary>Time.time at which the current phase ends.</summary>
        public float PhaseEndsAt;
        /// <summary>Earliest Time.time the next attack may begin.</summary>
        public float NextAttackAt;
        /// <summary>Shots left in the current burst (ranged archetypes).</summary>
        public int BurstRemaining;
        /// <summary>True while this drone holds one of the limited attack tokens.</summary>
        public bool HasToken;
        /// <summary>False until the drone's first attack resolves — the Shooter's deliberate opening miss reads this.</summary>
        public bool HasAttackedOnce;
    }
}
