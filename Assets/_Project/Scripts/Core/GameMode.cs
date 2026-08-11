#nullable enable
namespace CoD.Core
{
    /// <summary>
    /// The two modes named in CLAUDE.md. Same core scene, different starting
    /// inventory and rules — which is why this is an enum carried on the run
    /// rather than a second scene to keep in sync.
    /// </summary>
    public enum GameMode
    {
        /// <summary>Earned power, permadeath, the record is written. The default.</summary>
        Run = 0,
        /// <summary>Everything unlocked, cheat console on, and the record is NOT written.</summary>
        Sandbox = 1,
    }
}
