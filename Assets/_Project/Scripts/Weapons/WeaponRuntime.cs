#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// The mutable half of a weapon: what is true right now. Plain C# object,
    /// one per carried weapon, thrown away when the run ends.
    ///
    /// This type exists so that WeaponConfig never has to be written to. A
    /// passive that "improves reload time" changes numbers here or in the
    /// StatSheet — never in the asset, which is authored game data.
    /// </summary>
    public sealed class WeaponRuntime
    {
        public readonly WeaponConfig Config;

        public int CurrentAmmo;
        public int ReserveAmmo;

        /// <summary>Shots fired without a pause — drives the recoil pattern index and bloom.</summary>
        public int ShotsInBurst;
        public float CurrentSpread;
        public float NextShotAllowedAt;
        public float LastShotAt;
        public float ReloadEndsAt;
        public bool IsReloading;
        public int BurstShotsRemaining;

        public WeaponRuntime(WeaponConfig config)
        {
            Config = config;
            CurrentAmmo = config.magazineSize;
            ReserveAmmo = config.reserveAmmo;
            CurrentSpread = config.baseSpread;
        }

        public bool IsMagazineEmpty => CurrentAmmo <= 0;
        public bool HasReserve => ReserveAmmo > 0;
        public bool IsFull => CurrentAmmo >= Config.magazineSize;

        public void BeginReload(float now)
        {
            if (IsReloading || IsFull || !HasReserve) return;
            IsReloading = true;
            ReloadEndsAt = now + (IsMagazineEmpty ? Config.reloadEmptyTime : Config.reloadTime);
        }

        /// <summary>
        /// Reload cancelling: past the commit point the ammo is already in, so
        /// cancelling keeps it. Players who discover this feel skilled, and it
        /// costs nothing to implement.
        /// </summary>
        public bool TryCancelReload(float now)
        {
            if (!IsReloading) return false;
            float total = IsMagazineEmpty ? Config.reloadEmptyTime : Config.reloadTime;
            float elapsed = total - (ReloadEndsAt - now);
            if (elapsed >= total * Config.reloadCommitPoint)
            {
                CompleteReload();
                return true;
            }
            IsReloading = false;
            return false;
        }

        public void CompleteReload()
        {
            IsReloading = false;
            int needed = Config.magazineSize - CurrentAmmo;
            int taken = Mathf.Min(needed, ReserveAmmo);
            CurrentAmmo += taken;
            ReserveAmmo -= taken;
        }

        public void ConsumeShot(float now)
        {
            CurrentAmmo = Mathf.Max(0, CurrentAmmo - 1);
            ShotsInBurst++;
            LastShotAt = now;
            NextShotAllowedAt = now + Config.SecondsPerShot;
            CurrentSpread = Mathf.Min(Config.maxSpread, CurrentSpread + Config.spreadPerShot);
        }

        /// <summary>Bloom decays only after a short pause, so tap-firing is rewarded.</summary>
        public void DecaySpread(float now, float deltaTime)
        {
            if (now - LastShotAt < 0.1f) return;
            CurrentSpread = Mathf.Max(Config.baseSpread, CurrentSpread - Config.spreadDecayRate * deltaTime);
            if (now - LastShotAt > 0.35f) ShotsInBurst = 0;
        }
    }
}
