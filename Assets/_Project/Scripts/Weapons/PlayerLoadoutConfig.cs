#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// What the player starts a run holding. Lives here rather than on GameConfig
    /// so CoD.Core keeps depending on nothing — the dependency rule is one
    /// direction only, and a Core that reaches into Weapons is how a codebase
    /// turns into a knot.
    /// </summary>
    [CreateAssetMenu(fileName = "Loadout_", menuName = "CoD/Player Loadout", order = 5)]
    public sealed class PlayerLoadoutConfig : ScriptableObject
    {
        public WeaponConfig? startingWeapon;
        [Tooltip("Weapons carried at once. Swapping happens in the shop.")]
        [Range(1, 4)] public int weaponSlots = 2;
    }
}
