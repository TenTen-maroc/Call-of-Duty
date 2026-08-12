#nullable enable

namespace CoD.Waves
{
    /// <summary>
    /// How a run stopped.
    ///
    /// WHY THIS EXISTS
    /// Until the mission layer there was exactly one way a run could end — the
    /// player died — so <see cref="WaveRunner.RunEnded"/> carried no payload and
    /// every screen behind it could safely assume a corpse. Missions end in ways
    /// that are not deaths: every objective satisfied, a mission rule broken (a
    /// timer, an escort lost), or a run the player simply walked out of. Those
    /// want different screens, different music and different save writes, and a
    /// bare signal cannot tell them apart. Encoding it as a flag on the director
    /// instead would put the answer somewhere the game-over UI cannot reach
    /// without knowing the campaign exists.
    ///
    /// <see cref="Died"/> is deliberately first. It is therefore the value of an
    /// uninitialised field, and it is the ONLY ending endless mode has — so with
    /// no director in the scene the default is already the correct answer and
    /// nothing has to be set for endless to stay endless.
    ///
    /// Order matters for that reason alone. This enum is not serialized to the
    /// save file today; if it ever is, adding a member is fine and reordering
    /// one is a silent save corruption, the same trap
    /// <see cref="CoD.Core.GameMode"/> carries.
    /// </summary>
    public enum RunOutcome
    {
        /// <summary>Permadeath. The only ending endless mode has, and the default.</summary>
        Died,

        /// <summary>Every objective in the mission satisfied.</summary>
        MissionComplete,

        /// <summary>A mission rule was broken. Not a death, and not the player's fault in the same way.</summary>
        MissionFailed,

        /// <summary>The player left deliberately. Nothing failed.</summary>
        Abandoned,
    }
}
