#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// The one place a passive's effect is computed:
    ///
    ///     effective = (base + sum of flatAdds) * product of mults
    ///
    /// A plain C# object, not a ScriptableObject and not a MonoBehaviour, because
    /// it is pure runtime state. The alternative — "applying" a passive by writing
    /// the new value into a config asset — is the single most destructive thing
    /// this codebase could do: Domain Reload is disabled, so those writes persist
    /// between Play sessions and quietly rewrite the authored balance. Three shop
    /// visits and the AR is not the AR any more, in the repo, permanently.
    ///
    /// Rebuilt from scratch on every purchase rather than incremented, so a bad
    /// add can never accumulate and there is no "remove a passive" path to get
    /// wrong.
    /// </summary>
    public sealed class StatSheet
    {
        private readonly float[] _flatAdd = new float[StatExtensions.Count];
        private readonly float[] _multiplier = new float[StatExtensions.Count];

        public StatSheet() => Clear();

        public void Clear()
        {
            for (int i = 0; i < _flatAdd.Length; i++)
            {
                _flatAdd[i] = 0f;
                _multiplier[i] = 1f;
            }
        }

        public void AddFlat(Stat stat, float amount) => _flatAdd[(int)stat] += amount;

        public void AddMultiplier(Stat stat, float multiplier) => _multiplier[(int)stat] *= multiplier;

        public float FlatAdd(Stat stat) => _flatAdd[(int)stat];
        public float Multiplier(Stat stat) => _multiplier[(int)stat];

        /// <summary>
        /// The only way gameplay code should read a tunable that passives can
        /// modify. Clamped at zero: a stacking negative multiplier that flips a
        /// speed or a damage number negative is a bug factory.
        /// </summary>
        public float Effective(Stat stat, float baseValue)
        {
            int index = (int)stat;
            return Mathf.Max(0f, (baseValue + _flatAdd[index]) * _multiplier[index]);
        }
    }
}
