#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// One thing bolted to a gun: an optic, a suppressor, a grip, a bigger
    /// magazine.
    ///
    /// ⚠️ DELIBERATELY NOT AN <see cref="EffectModule"/>, and the distinction is
    /// the whole reason this file exists rather than four more module classes.
    /// `EffectModule` is a BEHAVIOUR HOOK: it gets called on every impact and
    /// decides what happens next, so a new module is a new C# class with a new
    /// `Resolve` — which is correct for explosive rounds and correct for a chain,
    /// because those are new behaviour. An attachment is 90% a stat delta. Routing
    /// it through the module pattern means a class per attachment, and seven slots
    /// times a handful of options each is the combinatorial mess this project
    /// exists to avoid. An attachment is DATA, and this asset is all of it.
    ///
    /// Read-only at runtime, like every config here. The deltas are folded onto
    /// <see cref="WeaponRuntime"/>'s own sheet — never back onto the
    /// <see cref="WeaponConfig"/>, which is authored balance data that Domain
    /// Reload being off would make permanent.
    /// </summary>
    [CreateAssetMenu(fileName = "Attach_", menuName = "CoD/Attachment", order = 2)]
    public sealed class AttachmentConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Save/unlock key. Never renamed once shipped — the same rule WeaponConfig.stableId lives by, for the same reason.")]
        public string stableId = "att_";
        public string displayName = "Attachment";

        [Tooltip("One per slot per weapon. Two optics on one rifle is not a build, it is a bug.")]
        public AttachmentSlot slot = AttachmentSlot.Optic;

        [Tooltip("Which weapon CLASSES may carry this. Empty = any. This is weaponClass's second real reader: before attachments it decided a balance law and nothing else.")]
        public WeaponClass[] allowedClasses = System.Array.Empty<WeaponClass>();

        [Header("What it changes")]
        [Tooltip("Ordered only for readability — flats all add, then multipliers all multiply, exactly as the passive sheet does.")]
        public Modifier[] modifiers = System.Array.Empty<Modifier>();

        /// <summary>
        /// One delta. A struct rather than a class so an array of them is one
        /// allocation, and shaped exactly like <c>PassiveConfig.Modifier</c> so
        /// that the two pipelines read the same way in the Inspector.
        /// </summary>
        [System.Serializable]
        public struct Modifier
        {
            public WeaponStat stat;
            public StatModifierKind kind;
            [Tooltip("Multiplier: 1.15 is +15%. FlatAdd: added to the base before the multipliers.")]
            public float value;
        }

        /// <summary>
        /// True when this attachment may be fitted to that weapon.
        ///
        /// An empty <see cref="allowedClasses"/> means ANY, which is the right
        /// default for a suppressor and the wrong one for a sniper scope — hence
        /// the field. Nothing enforces it but <see cref="WeaponRuntime"/> and the
        /// data tests, both of which refuse rather than warn: an optic that
        /// silently does nothing on the wrong gun is a purchase the player cannot
        /// tell from a broken game.
        /// </summary>
        public bool FitsOn(WeaponConfig weapon)
        {
            if (weapon == null) return false;
            if (allowedClasses.Length == 0) return true;

            foreach (WeaponClass allowed in allowedClasses)
            {
                if (allowed == weapon.weaponClass) return true;
            }
            return false;
        }

        /// <summary>Folds this attachment's deltas onto a runtime sheet. Called only when rebuilding the whole sheet.</summary>
        public void ApplyTo(WeaponStatSheet sheet)
        {
            foreach (Modifier modifier in modifiers)
            {
                if (modifier.kind == StatModifierKind.FlatAdd) sheet.AddFlat(modifier.stat, modifier.value);
                else sheet.AddMultiplier(modifier.stat, modifier.value);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (modifiers.Length == 0)
            {
                Debug.LogWarning(
                    $"[{name}] changes nothing at all — it can be fitted, it will occupy its slot, and the weapon " +
                    "will behave exactly as it did. That is indistinguishable from a broken attachment.", this);
            }

            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].kind != StatModifierKind.Multiplier || modifiers[i].value > 0f) continue;

                // A zero or negative multiplier is never a nerf, it is a break:
                // WeaponStatSheet clamps the result at zero, so x0 ADS time is a
                // weapon that is permanently aimed and x0 magazine is a weapon
                // that cannot be loaded.
                Debug.LogWarning(
                    $"[{name}] modifier {i} multiplies {modifiers[i].stat} by {modifiers[i].value} — a multiplier " +
                    "at or below zero collapses the value rather than reducing it. Use a fraction like 0.85.", this);
            }
        }
#endif
    }

    /// <summary>
    /// Where an attachment goes. One per slot per weapon.
    ///
    /// APPEND ONLY: Unity serialises an enum as its integer value, so inserting a
    /// member re-slots every asset authored after it — an optic that quietly
    /// becomes a muzzle device, with no import error to notice.
    /// </summary>
    public enum AttachmentSlot
    {
        Optic,
        Muzzle,
        Barrel,
        Underbarrel,
        Magazine,
        Stock,
        Ammo,
    }
}
