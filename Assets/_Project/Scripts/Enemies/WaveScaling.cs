#nullable enable

namespace CoD.Enemies
{
    /// <summary>
    /// Per-spawn multipliers the wave system applies on top of a DroneConfig.
    /// This is how round 20 gets harder WITHOUT editing the config asset — the
    /// iron rule of this codebase is that runtime never writes to authored data,
    /// and "the endless ramp scales enemy HP" is exactly the feature that would
    /// otherwise be implemented by doing so.
    ///
    /// C# note: a readonly struct passed by value. `None` is `static readonly`,
    /// which the no-mutable-statics guard allows precisely because nothing can
    /// reassign it between Play sessions.
    /// </summary>
    public readonly struct WaveScaling
    {
        public readonly float HealthMultiplier;
        public readonly float SpeedMultiplier;

        public WaveScaling(float healthMultiplier, float speedMultiplier)
        {
            HealthMultiplier = healthMultiplier <= 0f ? 1f : healthMultiplier;
            SpeedMultiplier = speedMultiplier <= 0f ? 1f : speedMultiplier;
        }

        public static readonly WaveScaling None = new(1f, 1f);
    }
}
