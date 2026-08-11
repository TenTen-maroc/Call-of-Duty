#nullable enable
using System.Collections.Generic;

namespace CoD.Core
{
    /// <summary>
    /// One run, in memory. Money, round, kills, and the passives bought so far.
    /// A plain C# object — permadeath means a run is never serialised, which is
    /// what keeps the save system one page long.
    ///
    /// The StatSheet is rebuilt from the owned list on every change. Nothing here
    /// ever writes to a ScriptableObject.
    /// </summary>
    public sealed class RunState
    {
        private readonly List<PassiveConfig> _owned = new(16);

        public StatSheet Stats { get; } = new();

        public int Money { get; private set; }
        public int Wave { get; private set; }
        public int Kills { get; private set; }
        /// <summary>Highest wave actually cleared this run. What the record is measured in.</summary>
        public int RoundReached { get; private set; }

        public List<PassiveConfig> Owned => _owned;

        public void BeginRun(int startingMoney)
        {
            _owned.Clear();
            Money = startingMoney;
            Wave = 0;
            Kills = 0;
            RoundReached = 0;
            Rebuild();
        }

        public void SetWave(int wave)
        {
            Wave = wave;
            if (wave > RoundReached) RoundReached = wave;
        }

        public void AddKill() => Kills++;

        /// <summary>Money in, after the MoneyGainMult passives. Rounded down — the player never sees fractions.</summary>
        public int AddMoney(int amount)
        {
            int scaled = UnityEngine.Mathf.FloorToInt(Stats.Effective(Stat.MoneyGainMult, amount));
            Money += scaled;
            return scaled;
        }

        public bool CanAfford(int cost) => Money >= cost;

        public bool TrySpend(int cost)
        {
            if (!CanAfford(cost)) return false;
            Money -= cost;
            return true;
        }

        public int StacksOf(PassiveConfig passive)
        {
            int count = 0;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] == passive) count++;
            }
            return count;
        }

        public void AddPassive(PassiveConfig passive)
        {
            _owned.Add(passive);
            Rebuild();
        }

        /// <summary>
        /// From scratch, every time. Incremental application looks cheaper right
        /// up until one path forgets to undo itself, and then the player has a
        /// permanent ghost bonus nobody can find.
        /// </summary>
        public void Rebuild()
        {
            Stats.Clear();
            for (int i = 0; i < _owned.Count; i++)
            {
                PassiveConfig passive = _owned[i];
                if (passive != null) passive.ApplyTo(Stats);
            }
        }
    }
}
