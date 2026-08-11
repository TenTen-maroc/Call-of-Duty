#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Deterministic recoil. The same weapon always kicks the same way for the
    /// same shot index, because the horizontal component comes from a seeded
    /// hash of (seed, shotIndex) rather than Random.
    ///
    /// This is the difference between a skill ceiling and noise: learnable
    /// recoil lets a player master the weapon, while pure randomness is
    /// something they can only endure. Static methods only — no state, so
    /// nothing survives between Play Mode sessions.
    /// </summary>
    public static class RecoilPattern
    {
        /// <summary>Vertical climb ramps from the first-shot value to the shot-8 value, then holds.</summary>
        public static float VerticalKick(WeaponConfig config, int shotIndex)
        {
            float t = Mathf.Clamp01(shotIndex / 8f);
            return Mathf.Lerp(config.verticalKickFirstShot, config.verticalKickAtShotEight, t);
        }

        /// <summary>
        /// Horizontal kick in [-max, +max], stable per (seed, shotIndex).
        /// A cheap integer hash keeps it allocation-free and identical on every
        /// machine — Random would drift with engine version and call order.
        /// </summary>
        public static float HorizontalKick(WeaponConfig config, int shotIndex)
        {
            unchecked
            {
                uint h = (uint)(config.recoilSeed * 73856093) ^ (uint)(shotIndex * 19349663);
                h ^= h >> 13;
                h *= 0x85EBCA6B;
                h ^= h >> 16;
                float normalized = (h % 20001u) / 10000f - 1f; // -1..+1
                return normalized * config.horizontalKickMax;
            }
        }
    }
}
