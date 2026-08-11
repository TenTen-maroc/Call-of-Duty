#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// The shop's pool AND its economy in one asset, because the shop cannot be
    /// balanced without both: an item's cost only means something relative to
    /// what a wave pays out, and a drop rate only means something relative to how
    /// many offers a break shows.
    /// </summary>
    [CreateAssetMenu(fileName = "Shop", menuName = "CoD/Shop Config", order = 61)]
    public sealed class ShopConfig : ScriptableObject
    {
        [System.Serializable]
        public struct PoolEntry
        {
            public ShopItemConfig? item;
            [Tooltip("Relative draw weight against the other eligible entries.")]
            [Min(0.01f)] public float weight;
            [Tooltip("Never offered before this wave.")]
            [Min(1)] public int minWave;
            [Tooltip("Never offered once the player owns this many. 0 = unlimited.")]
            [Min(0)] public int maxOwned;
        }

        [Header("Economy")]
        [Min(0)] public int startingMoney = 300;

        [Tooltip("What a Sandbox run starts with. 'Everything unlocked' in a shop-driven game IS money — one number instead of a parallel inventory system.")]
        [Min(0)] public int sandboxStartingMoney = 100000;
        [Tooltip("Four is the sweet spot: enough that the draw feels varied, few enough that the player reads them all in a break.")]
        [Range(1, 8)] public int offersPerBreak = 4;
        [Min(0)] public int rerollBaseCost = 50;
        [Tooltip("Each reroll in the same break costs this much more. Resets every break.")]
        [Range(1f, 3f)] public float rerollCostGrowth = 1.5f;
        [Tooltip("Multiplies every price by wave number, so late-game money still has somewhere to go.")]
        public AnimationCurve priceScalingByWave = AnimationCurve.Linear(1f, 1f, 30f, 3f);

        [Header("Pool")]
        public PoolEntry[] pool = System.Array.Empty<PoolEntry>();

        [Tooltip("Shown in EVERY break, on top of the drawn offers, and never entered into the weighted draw. " +
                 "This is what stops a bad roll from wasting a whole wave's income.")]
        public ShopItemConfig[] alwaysOffered = System.Array.Empty<ShopItemConfig>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            // The shop is bought with the number row, so a break can only show as
            // many rows as there are digits. Anything past the ninth is printed,
            // priced, and impossible to buy — which reads as the shop being broken
            // rather than as a configuration mistake.
            const int buyableRows = 9;
            int rows = offersPerBreak + alwaysOffered.Length;
            if (rows > buyableRows)
            {
                Debug.LogError(
                    $"[{name}] a break would show {rows} rows but only {buyableRows} can be bought from the " +
                    "number row. Lower offersPerBreak or shorten alwaysOffered.", this);
            }
        }
#endif

        public int PriceAtWave(ShopItemConfig item, int wave) =>
            Mathf.Max(1, Mathf.RoundToInt(item.cost * Mathf.Max(0.1f, priceScalingByWave.Evaluate(wave))));
    }
}
