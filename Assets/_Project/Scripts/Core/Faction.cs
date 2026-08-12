#nullable enable

namespace CoD.Core
{
    /// <summary>
    /// Which side of the fight a body is on.
    ///
    /// <see cref="Unaligned"/> IS THE DEFAULT AND IT IS FIRST. Nothing serialises
    /// this enum today, but the day something does, a body that has said nothing
    /// must deserialise to "nobody's friend" rather than to somebody's.
    /// </summary>
    public enum Faction
    {
        /// <summary>
        /// Has not declared a side, so no round passes through it. The training
        /// dummy is the case that exists today, and it is the case that proves the
        /// rule: it is a thing you shoot, and any default that made it friendly to
        /// the player would make it immune to the player.
        /// </summary>
        Unaligned,

        /// <summary>The player. Declared by <c>PlayerMotor</c>.</summary>
        Player,

        /// <summary>Drones, and the Meridian soldiers that will share their controller.</summary>
        Hostile,
    }

    /// <summary>
    /// Implemented by the component that drives a body, so that CoD.Core can ask
    /// which side a <see cref="Health"/> is on WITHOUT referencing CoD.Enemies or
    /// CoD.Player.
    ///
    /// WHY THIS EXISTS AT ALL. <see cref="Projectile"/> used to live in
    /// CoD.Enemies and asked its question directly — `health.TryGetComponent(out
    /// DroneController _)` — because a drone's round must pass THROUGH other
    /// drones: they would otherwise kill each other, hand the player free money,
    /// and act as cover. Promoting the projectile to Core so the player's launcher
    /// could reuse it took that answer away, because CoD.Core references nothing
    /// and must keep referencing nothing.
    ///
    /// The rule the projectile actually wants is not "is it a drone" but "is it on
    /// MY side", which is also the rule a player's rocket needs in the opposite
    /// direction. One interface answers both.
    ///
    /// ⚠️ BOTH SIDES DECLARE, AND NEITHER IS INFERRED. An earlier version of this
    /// defaulted an undeclared body to <see cref="Faction.Player"/> on the
    /// argument that the failure fell safely — a hostile that forgot to implement
    /// it would only BLOCK friendly fire. That was true and it was half the
    /// picture: it also meant every prop, every training dummy and every future
    /// neutral object with a Health was permanently transparent to the player's
    /// own rockets. The launcher's first test fired point blank into the sandbox
    /// dummy and the round sailed straight through it. Three values, no
    /// inference: what has not said which side it is on is on nobody's.
    /// </summary>
    public interface IFactionMember
    {
        Faction Faction { get; }
    }
}
