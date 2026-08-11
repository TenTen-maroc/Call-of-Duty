#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// A shop item you buy for its effect right now rather than for a permanent
    /// upgrade: repairs and ammo.
    ///
    /// These exist because a shop break could roll four offers the player did not
    /// want, and a wave's income was then simply wasted. That is the failure mode
    /// tuning-card item 3 asks about — "is four offers plus a reroll a real
    /// decision, or an obvious pick" — and the answer is neither when the honest
    /// move is to buy nothing. A repair is never the exciting choice, which is
    /// exactly why it makes the exciting choices cost something.
    ///
    /// Fractions rather than flat amounts, so one asset stays correct after a
    /// MaxHP passive or a weapon swap changes what "full" means.
    /// </summary>
    [CreateAssetMenu(fileName = "Consumable_", menuName = "CoD/Consumable", order = 62)]
    public sealed class ConsumableConfig : ScriptableObject
    {
        [Tooltip("Fraction of MAX health restored. 0 = this item does not repair.")]
        [Range(0f, 1f)] public float healFraction;

        [Tooltip("Fraction of the weapon's FULL reserve added back. 0 = this item does not resupply.")]
        [Range(0f, 1f)] public float ammoReserveFraction;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (healFraction <= 0f && ammoReserveFraction <= 0f)
            {
                Debug.LogError(
                    $"[{name}] restores nothing at all — this is an item the player can buy and receive nothing for.",
                    this);
            }
        }
#endif
    }
}
