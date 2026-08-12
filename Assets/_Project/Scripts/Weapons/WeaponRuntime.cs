#nullable enable
using System.Collections.Generic;
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
        /// <summary>How long the CURRENT reload takes, after ReloadSpeed passives. Stored so cancelling measures against the same number it started with.</summary>
        public float ReloadDuration;
        public bool IsReloading;
        public int BurstShotsRemaining;

        /// <summary>
        /// The effect modules this weapon is actually carrying right now. Seeded
        /// from the config and then appended to by shop purchases — the config
        /// asset is never modified, because a runtime write to authored data
        /// persists between Play sessions with Domain Reload off.
        /// </summary>
        public readonly List<EffectModule> Modules = new(4);

        /// <summary>
        /// What is bolted to the gun right now. Seeded from the config's authored
        /// defaults, then changed by <see cref="TryFit"/> and
        /// <see cref="Remove"/> — never written back to the asset.
        ///
        /// A list rather than a fixed array indexed by slot, because the natural
        /// operations are "what is fitted" (iterate) and "replace what is in this
        /// slot" (find then swap), and there are at most seven entries.
        /// </summary>
        public readonly List<AttachmentConfig> Attachments = new(4);

        /// <summary>
        /// The attachments' deltas, folded. Rebuilt from scratch whenever the list
        /// changes rather than incremented — StatSheet's rule, for StatSheet's
        /// reason: a bad add can never accumulate, and there is no "un-apply"
        /// path to get wrong.
        /// </summary>
        public readonly WeaponStatSheet Stats = new();

        public WeaponRuntime(WeaponConfig config)
        {
            Config = config;
            ReserveAmmo = config.reserveAmmo;
            CurrentSpread = config.baseSpread;

            for (int i = 0; i < config.effectModules.Length; i++)
            {
                EffectModule? module = config.effectModules[i];
                if (module != null) Modules.Add(module);
            }

            for (int i = 0; i < config.attachments.Length; i++)
            {
                AttachmentConfig? attachment = config.attachments[i];
                // Authored defaults still go through TryFit rather than straight
                // into the list: the slot rule and the class rule are the same
                // rules whoever put it there, and an asset authored with two
                // optics should behave like a player trying to fit two.
                if (attachment != null) TryFit(attachment);
            }

            // AFTER the attachments, because a magazine extension changes what a
            // full magazine is. Seeding it from the config first and rebuilding
            // later would have started every run with an extended magazine
            // holding a stock magazine's rounds.
            CurrentAmmo = MagazineSize;
        }

        // ---------- attachments ----------

        /// <summary>
        /// Fits an attachment, replacing whatever held its slot.
        ///
        /// Returns false when it does not belong on this weapon, so a shop can
        /// refuse the sale rather than charge for nothing — the same contract
        /// <c>WeaponController.AddEffectModule</c> and <c>RefillReserve</c> keep,
        /// and for the same reason: a purchase that silently does nothing is
        /// indistinguishable from a broken game.
        /// </summary>
        public bool TryFit(AttachmentConfig attachment)
        {
            if (attachment == null || !attachment.FitsOn(Config)) return false;
            if (Attachments.Contains(attachment)) return false;

            for (int i = 0; i < Attachments.Count; i++)
            {
                if (Attachments[i].slot != attachment.slot) continue;
                Attachments[i] = attachment;
                RebuildStats();
                return true;
            }

            Attachments.Add(attachment);
            RebuildStats();
            return true;
        }

        public bool Remove(AttachmentConfig attachment)
        {
            if (!Attachments.Remove(attachment)) return false;
            RebuildStats();
            return true;
        }

        public AttachmentConfig? InSlot(AttachmentSlot slot)
        {
            for (int i = 0; i < Attachments.Count; i++)
            {
                if (Attachments[i].slot == slot) return Attachments[i];
            }
            return null;
        }

        /// <summary>
        /// From scratch, every time. Never called per frame — only when the
        /// fitted set changes, which is a shop visit.
        /// </summary>
        private void RebuildStats()
        {
            Stats.Clear();
            for (int i = 0; i < Attachments.Count; i++) Attachments[i].ApplyTo(Stats);
        }

        // ---------- the effective numbers ----------
        //
        // Every one of these is what gameplay code must read instead of the
        // config field behind it. The config keeps the AUTHORED value, which is
        // what the balance laws and the arsenal gate are written against; these
        // are what the gun in the player's hands is doing right now.
        //
        // ⚠️ There is deliberately no effective fire rate. See WeaponStat.

        /// <summary>Per-pellet body damage before falloff. Read by WeaponController.ResolveHit.</summary>
        public float Damage => Stats.Effective(WeaponStat.Damage, Config.bodyDamage);

        /// <summary>
        /// Seconds hip-to-aimed, floored well above zero. An ADS time of 0 is a
        /// weapon that is permanently aimed — every recoil, spread and FOV rule in
        /// the game branches on `_adsProgress`, so collapsing it is not a fast
        /// weapon, it is a broken one.
        /// </summary>
        public float AdsTime => Mathf.Max(0.03f, Stats.Effective(WeaponStat.AdsTime, Config.adsTime));

        /// <summary>Multiplier, base 1. Folded WITH the player's passive reload speed rather than replacing it.</summary>
        public float ReloadSpeedMultiplier => Mathf.Max(0.05f, Stats.Effective(WeaponStat.ReloadSpeed, 1f));

        public float RecoilVerticalMultiplier => Stats.Effective(WeaponStat.RecoilVertical, 1f);
        public float RecoilHorizontalMultiplier => Stats.Effective(WeaponStat.RecoilHorizontal, 1f);
        public float HipSpreadMultiplier => Stats.Effective(WeaponStat.HipSpread, 1f);

        /// <summary>
        /// Rounds in the magazine, rounded and floored at one. Rounded rather than
        /// truncated so a x1.5 on a 30-round magazine is 45 and not 44, and
        /// floored because a zero-round magazine is a gun that can never fire and
        /// never finish a reload.
        /// </summary>
        public int MagazineSize =>
            Mathf.Max(1, Mathf.RoundToInt(Stats.Effective(WeaponStat.MagazineSize, Config.magazineSize)));

        /// <summary>Aimed FOV multiplier. LOWER IS MORE ZOOM; this is what an optic is.</summary>
        public float AdsFovMultiplier => Mathf.Clamp(
            Stats.Effective(WeaponStat.AdsFov, Config.adsFovMultiplier), 0.1f, 1f);

        public float AdsSensitivityMultiplier => Mathf.Clamp(
            Stats.Effective(WeaponStat.AdsSensitivity, Config.adsSensitivityMultiplier), 0.05f, 1f);

        public float SprintToFireTime => Mathf.Max(0f, Stats.Effective(WeaponStat.SprintToFire, Config.sprintToFireTime));

        /// <summary>
        /// The weapon's falloff, stretched by any Range attachment, and the reason
        /// this lives here rather than on the config: `WeaponConfig.DamageAtDistance`
        /// is what the balance laws and `ArsenalBuilder`'s gate read, and it must
        /// keep answering for the AUTHORED weapon. This answers for the built one.
        /// </summary>
        public float DamageAtDistance(float distance)
        {
            float scale = Mathf.Max(0.1f, Stats.Effective(WeaponStat.Range, 1f));
            float start = Config.falloffRange.x * scale;
            float end = Mathf.Max(start + 0.01f, Config.falloffRange.y * scale);
            float t = Mathf.Clamp01((distance - start) / (end - start));
            return Damage * Mathf.Lerp(1f, Config.minDamageMultiplier, t);
        }

        public bool IsMagazineEmpty => CurrentAmmo <= 0;
        public bool HasReserve => ReserveAmmo > 0;
        public bool IsFull => CurrentAmmo >= MagazineSize;

        /// <summary>Returns true when a reload actually started this call.</summary>
        public bool BeginReload(float now) => BeginReload(now, 1f);

        /// <summary>
        /// `speedMultiplier` comes from the StatSheet — higher is faster, so the
        /// duration is divided by it. The multiplier is applied HERE, to this
        /// runtime, never by editing the config's reloadTime.
        /// </summary>
        public bool BeginReload(float now, float speedMultiplier)
        {
            if (IsReloading || IsFull || !HasReserve) return false;
            IsReloading = true;
            BurstShotsRemaining = 0;
            ReloadDuration = (IsMagazineEmpty ? Config.reloadEmptyTime : Config.reloadTime)
                             / Mathf.Max(0.1f, speedMultiplier);
            ReloadEndsAt = now + ReloadDuration;
            return true;
        }

        /// <summary>
        /// Reload cancelling: past the commit point the ammo is already in, so
        /// cancelling keeps it. Players who discover this feel skilled, and it
        /// costs nothing to implement.
        /// </summary>
        public bool TryCancelReload(float now)
        {
            if (!IsReloading) return false;
            float total = ReloadDuration > 0f ? ReloadDuration
                : (IsMagazineEmpty ? Config.reloadEmptyTime : Config.reloadTime);
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
            int needed = MagazineSize - CurrentAmmo;
            int taken = Mathf.Min(needed, ReserveAmmo);
            CurrentAmmo += taken;
            ReserveAmmo -= taken;
        }

        public void ConsumeShot(float now)
        {
            CurrentAmmo = Mathf.Max(0, CurrentAmmo - 1);
            ShotsInBurst++;
            LastShotAt = now;

            // Scheduled from the shot that was DUE, not from the frame that
            // noticed it. `now + SecondsPerShot` threw away the overshoot every
            // time, so each shot rounded up to a whole frame: a 700 RPM rifle
            // fired at 600 on a 60 Hz display and at nearly 700 on 144 Hz. Fire
            // rate is one half of the TTK this entire game is tuned around, and
            // it must not be a function of the player's monitor.
            //
            // Only carried forward while the gun is actually firing on cadence —
            // after a pause, a hitch or a reload, the next shot starts from now
            // rather than firing a burst to "catch up".
            float period = Config.SecondsPerShot;
            bool onCadence = NextShotAllowedAt > 0f && now - NextShotAllowedAt < period;
            NextShotAllowedAt = (onCadence ? NextShotAllowedAt : now) + period;

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
