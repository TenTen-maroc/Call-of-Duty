#nullable enable
using System;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// The scene's handle on the current run: the RunState, the persistent record,
    /// and the one event everything else listens to when the stat sheet changes.
    ///
    /// A scene component rather than a singleton — Domain Reload is off, so a
    /// static instance would survive into the next Play session pointing at a
    /// destroyed object, and a run would start already half-finished.
    ///
    /// It applies MaxHealth itself (Health lives in this assembly) and PUBLISHES
    /// the rest. The player's movement and the weapon read the sheet through
    /// StatsChanged, which is what keeps the dependency pointing one way: Player
    /// and Weapons know about Core, and Core knows about neither.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunContext : MonoBehaviour
    {
        [SerializeField] private GameConfig? _config = null;
        [Tooltip("The player's Health. MaxHealth passives are applied here directly.")]
        [SerializeField] private Health? _playerHealth = null;
        [Tooltip("Money the run starts with. Also on ShopConfig; this is the fallback when no shop exists yet.")]
        [SerializeField] private int _startingMoney = 300;

        private SaveData? _save;

        public RunState State { get; } = new();
        public StatSheet Stats => State.Stats;

        /// <summary>Raised after every rebuild — on run start and on every purchase.</summary>
        public event Action<StatSheet>? StatsChanged;
        public event Action<int>? MoneyChanged;

        /// <summary>The persistent record. Loaded once, saved when a run ends.</summary>
        public SaveData Save => _save ??= SaveSystem.Load();

        private void Awake()
        {
            _save = SaveSystem.Load();
            BeginRun(_startingMoney);
        }

        public void BeginRun(int startingMoney)
        {
            State.BeginRun(startingMoney);
            ApplyStats();
            MoneyChanged?.Invoke(State.Money);
        }

        public void AddMoney(int amount)
        {
            if (amount == 0) return;
            State.AddMoney(amount);
            MoneyChanged?.Invoke(State.Money);
        }

        public bool TrySpend(int cost)
        {
            if (!State.TrySpend(cost)) return false;
            MoneyChanged?.Invoke(State.Money);
            return true;
        }

        public void BuyPassive(PassiveConfig passive)
        {
            State.AddPassive(passive);
            ApplyStats();
        }

        /// <summary>
        /// Push the rebuilt sheet everywhere. Raising max health tops the player
        /// up as a side effect — deliberate: a health upgrade that leaves you at
        /// 12/125 reads as broken, and the shop only opens between waves anyway.
        /// </summary>
        public void ApplyStats()
        {
            if (_playerHealth != null && _config != null)
            {
                _playerHealth.ConfigureMax(Stats.Effective(Stat.MaxHealth, _config.playerMaxHealth));
            }
            StatsChanged?.Invoke(Stats);
        }

        /// <summary>Called when a run ends. The only moment anything is written to disk.</summary>
        public void RecordRunEnded()
        {
            SaveData save = Save;
            save.totalRuns++;
            save.totalKills += State.Kills;
            if (State.RoundReached > save.bestRound) save.bestRound = State.RoundReached;
            SaveSystem.Save(save);
        }
    }
}
