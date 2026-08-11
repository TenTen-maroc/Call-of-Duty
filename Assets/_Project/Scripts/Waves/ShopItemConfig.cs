#nullable enable
using CoD.Core;
using CoD.Weapons;
using UnityEngine;

namespace CoD.Waves
{
    public enum ShopItemKind { Passive, Weapon, EffectModule }

    /// <summary>
    /// One thing the shop can offer. Exactly one payload reference is set, and
    /// which one is decided by `kind` — OnValidate enforces the pair, because a
    /// Passive-kind item holding a weapon reference is an offer the player can buy
    /// and receive nothing for.
    /// </summary>
    [CreateAssetMenu(fileName = "Shop_", menuName = "CoD/Shop Item", order = 60)]
    public sealed class ShopItemConfig : ScriptableObject
    {
        [Header("Identity")]
        public string stableId = "shop_";
        public string displayName = "Item";
        [TextArea] public string description = "";
        [Min(0)] public int cost = 150;

        [Header("Payload — set exactly the one matching `kind`")]
        public ShopItemKind kind = ShopItemKind.Passive;
        public PassiveConfig? passive;
        public WeaponConfig? weapon;
        public EffectModule? effect;

        /// <summary>Human-readable line for the shop list. Built once per offer, not per frame.</summary>
        public string Summary => string.IsNullOrEmpty(description) ? displayName : displayName + " — " + description;

        public bool IsValid => kind switch
        {
            ShopItemKind.Passive => passive != null,
            ShopItemKind.Weapon => weapon != null,
            ShopItemKind.EffectModule => effect != null,
            _ => false,
        };

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!IsValid)
            {
                Debug.LogError(
                    $"[{name}] kind is {kind} but the matching reference is empty. This item would sell nothing.",
                    this);
            }
        }
#endif
    }
}
