#nullable enable
using UnityEngine;

namespace CoD.Core
{
    public enum StatModifierKind { FlatAdd, Multiplier }

    /// <summary>
    /// A permanent, run-long upgrade bought in the shop. Pure data: a list of
    /// (stat, kind, value) rows the StatSheet folds together. Adding an upgrade is
    /// an asset, never code.
    /// </summary>
    [CreateAssetMenu(fileName = "Passive_", menuName = "CoD/Passive Config", order = 40)]
    public sealed class PassiveConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Modifier
        {
            public Stat stat;
            public StatModifierKind kind;
            [Tooltip("FlatAdd: added to the base. Multiplier: 1.15 = +15%.")]
            public float value;
        }

        [Header("Identity")]
        [Tooltip("Save/registry key. Never renamed once shipped.")]
        public string stableId = "passive_";
        public string displayName = "Passive";
        [TextArea] public string description = "";

        [Header("Effect")]
        public Modifier[] modifiers = System.Array.Empty<Modifier>();

        [Header("Stacking")]
        public bool stackable = true;
        [Tooltip("Ignored when stackable is false.")]
        [Range(1, 20)] public int maxStacks = 5;

        /// <summary>Folds this passive into a sheet. Called once per owned stack during a rebuild.</summary>
        public void ApplyTo(StatSheet sheet)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                Modifier modifier = modifiers[i];
                if (modifier.kind == StatModifierKind.FlatAdd) sheet.AddFlat(modifier.stat, modifier.value);
                else sheet.AddMultiplier(modifier.stat, modifier.value);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                // A multiplier of 0 zeroes the stat forever and reads as "broken
                // item" rather than "bad buy".
                if (modifiers[i].kind == StatModifierKind.Multiplier && modifiers[i].value <= 0f)
                {
                    modifiers[i].value = 1f;
                }
            }
        }
#endif
    }
}
