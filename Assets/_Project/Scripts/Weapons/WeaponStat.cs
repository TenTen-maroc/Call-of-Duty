#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Every number an ATTACHMENT is allowed to touch.
    ///
    /// ⚠️ DELIBERATELY NOT <c>CoD.Core.Stat</c>, AND THIS IS THE MOST IMPORTANT
    /// LINE IN THE FILE. `Stat` is the PASSIVE sheet: five values that describe
    /// the player — health, move speed, reload speed, damage, money — and
    /// `StatExtensions.Count` sizes two arrays inside `StatSheet`, which
    /// `RunContext` rebuilds on every purchase and `WeaponController` and
    /// `PlayerMotor` read every frame. Adding eleven weapon values to that enum
    /// would resize those arrays, widen the shop's modifier surface to numbers no
    /// passive should ever reach, and put the whole passive pipeline in the blast
    /// radius of a scope's zoom level. Two concerns, two enums, two sheets.
    ///
    /// The two DO meet, in exactly one place each and on purpose: reload speed and
    /// damage are multiplied by both, because "the player reloads faster" and
    /// "this magazine reloads faster" are genuinely different claims that
    /// genuinely compose.
    ///
    /// EVERY ENTRY HERE HAS A READER, and that rule is inherited from `Stat`'s own
    /// header: an enum value nothing reads is a promise the shop cannot keep, and
    /// the player finds out by buying it. The reader is named on each line.
    ///
    /// ⚠️ NOTHING HERE TOUCHES FIRE RATE. `WeaponRuntime.ConsumeShot` schedules
    /// cadence off `Config.SecondsPerShot` — the AUTHORED number — and
    /// `WeaponCadenceRegressionTests` pins the arithmetic that keeps a 700 RPM
    /// rifle firing at 700 RPM on a 144 Hz monitor. A `FireRate` attachment stat
    /// would move that schedule onto a runtime value the regression test does not
    /// exercise, and rate of fire is one half of the time-to-kill this entire game
    /// is tuned around. If a weapon should fire faster, that is a new weapon.
    ///
    /// APPEND ONLY: the values index arrays in <see cref="WeaponStatSheet"/>.
    /// </summary>
    public enum WeaponStat
    {
        /// <summary>Per-pellet body damage, before falloff. Read by WeaponController.ResolveHit.</summary>
        Damage = 0,

        /// <summary>Seconds hip-to-aimed. LOWER IS BETTER — a x0.85 modifier is faster. Read by UpdateAds.</summary>
        AdsTime = 1,

        /// <summary>Reload speed multiplier, base 1. Higher is faster. Folded with the passive sheet's in TryBeginReload.</summary>
        ReloadSpeed = 2,

        /// <summary>Vertical kick multiplier, base 1. Lower is better. Read by ApplyRecoil.</summary>
        RecoilVertical = 3,

        /// <summary>Horizontal kick multiplier, base 1. Lower is better. Read by ApplyRecoil.</summary>
        RecoilHorizontal = 4,

        /// <summary>Hipfire bloom multiplier, base 1. Lower is tighter. Read by CurrentSpreadDegrees.</summary>
        HipSpread = 5,

        /// <summary>Rounds in the magazine. Read by WeaponRuntime — see MagazineSize for the rounding rule.</summary>
        MagazineSize = 6,

        /// <summary>Falloff distance multiplier, base 1. Pushes BOTH ends of falloffRange out. Read by DamageAtDistance.</summary>
        Range = 7,

        /// <summary>Aimed FOV multiplier against the base. LOWER IS MORE ZOOM. Read by UpdateFovOffset — this is what an optic IS.</summary>
        AdsFov = 8,

        /// <summary>Aimed look sensitivity multiplier. Read by UpdateAds. An optic that zooms and does not slow the look is unusable.</summary>
        AdsSensitivity = 9,

        /// <summary>Seconds between releasing sprint and being allowed to fire. LOWER IS BETTER. Read by TryFire.</summary>
        SprintToFire = 10,
    }

    public static class WeaponStatExtensions
    {
        /// <summary>How many entries the sheet needs. One place to change when a stat is added.</summary>
        public const int Count = 11;
    }

    /// <summary>
    /// The same pipeline <c>CoD.Core.StatSheet</c> runs, over its own arrays:
    ///
    ///     effective = (base + sum of flatAdds) * product of mults
    ///
    /// A plain C# object owned by <see cref="WeaponRuntime"/>, rebuilt from
    /// scratch whenever the attachment list changes rather than incremented — so
    /// a bad add can never accumulate and there is no "remove an attachment" path
    /// to get wrong. Nothing here is ever written back to a `WeaponConfig`:
    /// Domain Reload is off, and a runtime write to authored balance data
    /// persists between Play sessions.
    ///
    /// Not a subclass of StatSheet and not a shared generic. The duplication is
    /// twenty lines; the coupling would put a weapon change one keystroke from the
    /// array that sizes the player's passives.
    /// </summary>
    public sealed class WeaponStatSheet
    {
        private readonly float[] _flatAdd = new float[WeaponStatExtensions.Count];
        private readonly float[] _multiplier = new float[WeaponStatExtensions.Count];

        public WeaponStatSheet() => Clear();

        public void Clear()
        {
            for (int i = 0; i < _flatAdd.Length; i++)
            {
                _flatAdd[i] = 0f;
                _multiplier[i] = 1f;
            }
        }

        public void AddFlat(WeaponStat stat, float amount) => _flatAdd[(int)stat] += amount;

        public void AddMultiplier(WeaponStat stat, float multiplier) => _multiplier[(int)stat] *= multiplier;

        public float FlatAdd(WeaponStat stat) => _flatAdd[(int)stat];
        public float Multiplier(WeaponStat stat) => _multiplier[(int)stat];

        /// <summary>
        /// Clamped at zero, for StatSheet's reason: a stacking negative multiplier
        /// that flips a damage or a duration negative is a bug factory. Callers
        /// that need a floor above zero — an ADS time of 0 s is a weapon that is
        /// always aimed — apply it themselves at the read site, where the unit is
        /// known.
        /// </summary>
        public float Effective(WeaponStat stat, float baseValue)
        {
            int index = (int)stat;
            return Mathf.Max(0f, (baseValue + _flatAdd[index]) * _multiplier[index]);
        }
    }
}
