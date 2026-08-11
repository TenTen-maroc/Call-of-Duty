#nullable enable
using System.Collections.Generic;
using CoD.Core;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Draws the offers for a shop break and applies a purchase. Plain C#, owned
    /// by the WaveRunner — no scene presence, no Update.
    ///
    /// The draw is weighted, wave-gated and capped per run, and it never offers
    /// the same item twice in one break: a break showing "Max HP" four times is a
    /// break with no decision in it.
    /// </summary>
    public sealed class ShopService
    {
        private readonly ShopConfig _config;
        private readonly RunContext _run;
        private readonly List<ShopItemConfig> _offers = new(8);
        private readonly List<int> _prices = new(8);
        private readonly List<ShopConfig.PoolEntry> _eligible = new(32);

        private int _rerollsThisBreak;

        public ShopService(ShopConfig config, RunContext run)
        {
            _config = config;
            _run = run;
        }

        /// <summary>Current offers. Index-aligned with <see cref="Prices"/>.</summary>
        public List<ShopItemConfig> Offers => _offers;
        public List<int> Prices => _prices;
        public int RerollCost => Mathf.RoundToInt(_config.rerollBaseCost *
            Mathf.Pow(_config.rerollCostGrowth, _rerollsThisBreak));

        /// <summary>New break: reroll cost resets, then a fresh draw.</summary>
        public void OpenBreak(int wave)
        {
            _rerollsThisBreak = 0;
            Draw(wave);
        }

        public bool TryReroll(int wave)
        {
            int cost = RerollCost;
            if (!_run.TrySpend(cost)) return false;
            _rerollsThisBreak++;
            Draw(wave);
            return true;
        }

        /// <summary>
        /// Buy offer `index`. Returns false when it is unaffordable or already
        /// gone, so the UI can play a refusal instead of silently doing nothing.
        /// </summary>
        public bool TryBuy(int index, int wave)
        {
            if (index < 0 || index >= _offers.Count) return false;
            ShopItemConfig item = _offers[index];
            if (item == null) return false;

            int price = _prices[index];
            if (!_run.TrySpend(price)) return false;

            switch (item.kind)
            {
                case ShopItemKind.Passive:
                    if (item.passive != null) _run.BuyPassive(item.passive);
                    break;
                case ShopItemKind.Weapon:
                case ShopItemKind.EffectModule:
                    // Weapons and modules land with the arsenal milestone. Refuse
                    // rather than take the money: a purchase that does nothing is
                    // worse than an item that cannot be bought yet.
                    GameLog.Warn($"Shop item '{item.displayName}' has no handler yet; refunding.");
                    _run.AddMoney(price);
                    return false;
            }

            // Sold out of this break. Removing rather than greying out keeps the
            // list short enough to read at a glance.
            _offers.RemoveAt(index);
            _prices.RemoveAt(index);
            return true;
        }

        private void Draw(int wave)
        {
            _offers.Clear();
            _prices.Clear();
            BuildEligible(wave);

            int wanted = Mathf.Min(_config.offersPerBreak, _eligible.Count);
            for (int i = 0; i < wanted; i++)
            {
                int picked = PickWeighted();
                if (picked < 0) break;
                ShopItemConfig? item = _eligible[picked].item;
                _eligible.RemoveAt(picked);   // no duplicates within one break
                if (item == null) continue;

                _offers.Add(item);
                _prices.Add(_config.PriceAtWave(item, wave));
            }
        }

        private void BuildEligible(int wave)
        {
            _eligible.Clear();
            for (int i = 0; i < _config.pool.Length; i++)
            {
                ShopConfig.PoolEntry entry = _config.pool[i];
                if (entry.item == null || !entry.item.IsValid) continue;
                if (wave < entry.minWave) continue;
                if (entry.maxOwned > 0 && OwnedCount(entry.item) >= entry.maxOwned) continue;
                _eligible.Add(entry);
            }
        }

        private int OwnedCount(ShopItemConfig item) =>
            item.kind == ShopItemKind.Passive && item.passive != null ? _run.State.StacksOf(item.passive) : 0;

        private int PickWeighted()
        {
            float total = 0f;
            for (int i = 0; i < _eligible.Count; i++) total += Mathf.Max(0.01f, _eligible[i].weight);
            if (total <= 0f) return -1;

            float roll = Random.value * total;
            for (int i = 0; i < _eligible.Count; i++)
            {
                roll -= Mathf.Max(0.01f, _eligible[i].weight);
                if (roll <= 0f) return i;
            }
            return _eligible.Count - 1;
        }
    }
}
