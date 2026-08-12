#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Every weapon in the game, in one asset — the list the balance tests walk
    /// and the list a save resolves a `stableId` against.
    ///
    /// It exists because the alternative was a hardcoded array of asset paths
    /// inside a test. That array made adding weapon number three a TEST EDIT, and
    /// a balance law you have to remember to opt a weapon into is a balance law
    /// that the seventh weapon quietly escapes. The arsenal is about to grow to
    /// pistol / marksman / LMG / shotgun / sniper / launcher; the gate has to
    /// enumerate itself.
    ///
    /// `stableId` is the save/registry key and is never renamed once shipped, so
    /// two entries carrying the same one is the failure worth catching here: the
    /// save would resolve to whichever came first and the player's other weapon
    /// would silently become a copy of it, with no error anywhere.
    ///
    /// Read-only at runtime like every config asset. Domain Reload is off, so a
    /// runtime write would persist into the next Play session.
    /// </summary>
    [CreateAssetMenu(fileName = "Weapons", menuName = "CoD/Weapon Registry", order = 1)]
    public sealed class WeaponRegistry : ScriptableObject
    {
        [Tooltip("Every shipped weapon. Order is presentation only — nothing resolves a weapon by index, because an index is not stable across an insert.")]
        public WeaponConfig[] allWeapons = System.Array.Empty<WeaponConfig>();

        public int Count => allWeapons.Length;

        /// <summary>
        /// The save-key lookup. Ordinal comparison on purpose: a stableId is an
        /// identifier, not display text, and a culture-aware compare is how a
        /// Turkish locale turns "wpn_ar_standard" into a weapon that cannot be
        /// found. Returns null rather than throwing — a save naming a weapon that
        /// no longer ships should fall back to the loadout, not end the run.
        /// </summary>
        public WeaponConfig? ByStableId(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return null;

            // Linear over a handful of entries, and never called per frame.
            foreach (WeaponConfig weapon in allWeapons)
            {
                if (weapon == null) continue;
                if (string.Equals(weapon.stableId, stableId, System.StringComparison.Ordinal)) return weapon;
            }
            return null;
        }

#if UNITY_EDITOR
        // Editor-only, and O(n^2) deliberately: n is the size of the arsenal, and
        // a nested loop that allocates nothing beats a HashSet that has to be
        // built every time the Inspector redraws.
        private void OnValidate()
        {
            for (int i = 0; i < allWeapons.Length; i++)
            {
                WeaponConfig weapon = allWeapons[i];
                if (weapon == null)
                {
                    // An empty slot is how a deleted asset leaves the registry.
                    // Silently skipping it would drop that weapon out of every
                    // balance test while the list still looks the right length.
                    Debug.LogError($"[{name}] entry {i} is empty — a null here removes a weapon from every gate that walks this list.", this);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(weapon.stableId))
                {
                    Debug.LogError($"[{name}] entry {i} ({weapon.name}) has no stableId — saves reference weapons by that key, not by asset name.", this);
                    continue;
                }

                for (int j = i + 1; j < allWeapons.Length; j++)
                {
                    WeaponConfig other = allWeapons[j];
                    if (other == null) continue;
                    if (!string.Equals(weapon.stableId, other.stableId, System.StringComparison.Ordinal)) continue;

                    Debug.LogError(
                        $"[{name}] '{weapon.stableId}' is on both {weapon.name} and {other.name}. " +
                        "A duplicate stableId aliases two weapons into one for every save that names it, " +
                        "and nothing at runtime will report it.", this);
                }
            }
        }
#endif
    }
}
