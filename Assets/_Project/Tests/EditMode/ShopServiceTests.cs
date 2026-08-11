#nullable enable
using CoD.Core;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The shop's rules, without a scene: wave gating, per-run caps, no duplicate
    /// offers in one break, and the reroll cost curve. All of them are silent
    /// failures — a mis-gated item does not crash, it just makes the economy
    /// nonsense three waves later.
    /// </summary>
    public sealed class ShopServiceTests
    {
        private GameObject? _host;
        private RunContext? _run;

        [SetUp]
        public void CreateRun()
        {
            _host = new GameObject("TestRun");
            _run = _host.AddComponent<RunContext>();
            // Awake does not fire for components added in edit mode, which suits
            // us: the run is started explicitly, with no save file involved.
            _run.State.BeginRun(1000);
        }

        [TearDown]
        public void DestroyRun()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private static PassiveConfig MakePassive(string id)
        {
            PassiveConfig passive = ScriptableObject.CreateInstance<PassiveConfig>();
            passive.stableId = id;
            passive.displayName = id;
            passive.maxStacks = 2;
            passive.modifiers = new[]
            {
                new PassiveConfig.Modifier
                {
                    stat = Stat.MaxHealth, kind = StatModifierKind.FlatAdd, value = 10f,
                },
            };
            return passive;
        }

        private static ShopItemConfig MakeItem(string id, int cost, PassiveConfig passive)
        {
            ShopItemConfig item = ScriptableObject.CreateInstance<ShopItemConfig>();
            item.stableId = id;
            item.displayName = id;
            item.cost = cost;
            item.kind = ShopItemKind.Passive;
            item.passive = passive;
            return item;
        }

        private static ShopConfig MakeShop(params (ShopItemConfig item, int minWave, int maxOwned)[] entries)
        {
            ShopConfig shop = ScriptableObject.CreateInstance<ShopConfig>();
            shop.startingMoney = 1000;
            shop.offersPerBreak = 4;
            shop.rerollBaseCost = 50;
            shop.rerollCostGrowth = 1.5f;
            shop.priceScalingByWave = AnimationCurve.Constant(1f, 30f, 1f);

            var pool = new ShopConfig.PoolEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                pool[i] = new ShopConfig.PoolEntry
                {
                    item = entries[i].item,
                    weight = 1f,
                    minWave = entries[i].minWave,
                    maxOwned = entries[i].maxOwned,
                };
            }
            shop.pool = pool;
            return shop;
        }

        [Test]
        public void Draw_NeverOffersTheSameItemTwiceInOneBreak()
        {
            ShopItemConfig a = MakeItem("a", 100, MakePassive("a"));
            ShopItemConfig b = MakeItem("b", 100, MakePassive("b"));
            var service = new ShopService(MakeShop((a, 1, 0), (b, 1, 0)), _run!, null);

            service.OpenBreak(1);

            Assert.AreEqual(2, service.Offers.Count);
            Assert.AreNotSame(service.Offers[0], service.Offers[1],
                "four identical offers is a break with no decision in it");
        }

        [Test]
        public void Draw_RespectsMinWave()
        {
            ShopItemConfig early = MakeItem("early", 100, MakePassive("early"));
            ShopItemConfig late = MakeItem("late", 100, MakePassive("late"));
            var service = new ShopService(MakeShop((early, 1, 0), (late, 5, 0)), _run!, null);

            service.OpenBreak(2);

            Assert.AreEqual(1, service.Offers.Count);
            Assert.AreSame(early, service.Offers[0]);
        }

        [Test]
        public void Buy_SpendsMoney_InstallsThePassive_AndRemovesTheOffer()
        {
            PassiveConfig passive = MakePassive("hp");
            ShopItemConfig item = MakeItem("hp", 250, passive);
            var service = new ShopService(MakeShop((item, 1, 0)), _run!, null);
            service.OpenBreak(1);

            Assert.IsTrue(service.TryBuy(0, 1));
            Assert.AreEqual(750, _run!.State.Money);
            Assert.AreEqual(1, _run.State.StacksOf(passive));
            Assert.AreEqual(0, service.Offers.Count, "a sold item leaves the break");
            Assert.AreEqual(110f, _run.State.Stats.Effective(Stat.MaxHealth, 100f), 0.001f);
        }

        [Test]
        public void Buy_RefusesWhenUnaffordable_AndTakesNothing()
        {
            ShopItemConfig item = MakeItem("expensive", 5000, MakePassive("expensive"));
            var service = new ShopService(MakeShop((item, 1, 0)), _run!, null);
            service.OpenBreak(1);

            Assert.IsFalse(service.TryBuy(0, 1));
            Assert.AreEqual(1000, _run!.State.Money);
            Assert.AreEqual(1, service.Offers.Count);
        }

        [Test]
        public void MaxOwned_RetiresAnItemOnceTheCapIsReached()
        {
            PassiveConfig passive = MakePassive("capped");
            ShopItemConfig item = MakeItem("capped", 100, passive);
            var service = new ShopService(MakeShop((item, 1, 1)), _run!, null);

            service.OpenBreak(1);
            Assert.IsTrue(service.TryBuy(0, 1));

            service.OpenBreak(2);
            Assert.AreEqual(0, service.Offers.Count, "a maxed item must stop appearing");
        }

        [Test]
        public void Reroll_ChargesAndGrowsWithinABreak_ThenResets()
        {
            ShopItemConfig item = MakeItem("a", 100, MakePassive("a"));
            var service = new ShopService(MakeShop((item, 1, 0)), _run!, null);

            service.OpenBreak(1);
            Assert.AreEqual(50, service.RerollCost);

            Assert.IsTrue(service.TryReroll(1));
            Assert.AreEqual(950, _run!.State.Money);
            Assert.AreEqual(75, service.RerollCost, "each reroll in a break costs more");

            service.OpenBreak(2);
            Assert.AreEqual(50, service.RerollCost, "and the cost resets every break");
        }

        [Test]
        public void PriceScaling_MakesLateWavesCostMore()
        {
            ShopItemConfig item = MakeItem("a", 100, MakePassive("a"));
            ShopConfig shop = MakeShop((item, 1, 0));
            shop.priceScalingByWave = AnimationCurve.Linear(1f, 1f, 21f, 3f);

            Assert.AreEqual(100, shop.PriceAtWave(item, 1));
            Assert.AreEqual(300, shop.PriceAtWave(item, 21));
        }

        // ---------- consumables and the always-offered rows ----------

        private static ConsumableConfig MakeConsumable(float heal, float ammo)
        {
            ConsumableConfig consumable = ScriptableObject.CreateInstance<ConsumableConfig>();
            consumable.healFraction = heal;
            consumable.ammoReserveFraction = ammo;
            return consumable;
        }

        private static ShopItemConfig MakeConsumableItem(string id, int cost, ConsumableConfig payload)
        {
            ShopItemConfig item = ScriptableObject.CreateInstance<ShopItemConfig>();
            item.stableId = id;
            item.displayName = id;
            item.cost = cost;
            item.kind = ShopItemKind.Consumable;
            item.consumable = payload;
            item.repeatable = true;
            return item;
        }

        [Test]
        public void AlwaysOfferedRows_AppearInEveryBreak_AndSurviveARepeatedReroll()
        {
            PassiveConfig passive = MakePassive("p1");
            ShopConfig shop = MakeShop((MakeItem("i1", 10, passive), 1, 0));
            ShopItemConfig repair = MakeConsumableItem("repair", 50, MakeConsumable(0.5f, 0f));
            shop.alwaysOffered = new[] { repair };

            var service = new ShopService(shop, _run!, null, null);
            service.OpenBreak(1);
            Assert.Contains(repair, service.Offers, "the repair row is missing from a fresh break");

            // A reroll redraws the weighted offers; the floor under a bad roll
            // must not be something the player can accidentally reroll away.
            service.TryReroll(1);
            Assert.Contains(repair, service.Offers, "rerolling removed the always-offered row");
        }

        [Test]
        public void AConsumableThatDoesNothing_IsRefusedAndRefunded()
        {
            // No Health and no weapon, so neither payload can apply.
            ShopConfig shop = MakeShop();
            ShopItemConfig repair = MakeConsumableItem("repair", 50, MakeConsumable(0.5f, 0f));
            shop.alwaysOffered = new[] { repair };

            var service = new ShopService(shop, _run!, null, null);
            service.OpenBreak(1);
            int before = _run!.State.Money;

            Assert.IsFalse(service.TryBuy(0, 1), "a consumable that restores nothing must refuse the sale");
            Assert.AreEqual(before, _run.State.Money, "the money must come back — this is the scam case");
        }

        [Test]
        public void ARepeatableRow_StaysOnTheShelfAfterItSells()
        {
            var host = new GameObject("Player");
            try
            {
                Health health = host.AddComponent<Health>();
                health.ConfigureMax(100f);
                health.ApplyDamage(new DamageInfo(60f, Vector3.zero, Vector3.up, Vector3.forward, false));
                Assert.Less(health.Current, health.Max, "the test needs damage to repair");

                ShopConfig shop = MakeShop();
                ShopItemConfig repair = MakeConsumableItem("repair", 50, MakeConsumable(0.25f, 0f));
                shop.alwaysOffered = new[] { repair };

                var service = new ShopService(shop, _run!, null, health);
                service.OpenBreak(1);

                Assert.IsTrue(service.TryBuy(0, 1));
                Assert.Contains(repair, service.Offers,
                    "a repeatable row must stay buyable — one repair is rarely the whole answer");

                float afterFirst = health.Current;
                Assert.IsTrue(service.TryBuy(0, 1), "the second purchase must also go through");
                Assert.Greater(health.Current, afterFirst);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HealingNeverExceedsMaximum_AndAFullPlayerIsRefused()
        {
            var host = new GameObject("Player");
            try
            {
                Health health = host.AddComponent<Health>();
                health.ConfigureMax(100f);

                ShopConfig shop = MakeShop();
                ShopItemConfig repair = MakeConsumableItem("repair", 50, MakeConsumable(0.5f, 0f));
                shop.alwaysOffered = new[] { repair };

                var service = new ShopService(shop, _run!, null, health);
                service.OpenBreak(1);
                int before = _run!.State.Money;

                Assert.IsFalse(service.TryBuy(0, 1), "a player at full health has nothing to buy");
                Assert.AreEqual(before, _run.State.Money);
                Assert.AreEqual(100f, health.Current, 1e-3f, "healing must never push past the maximum");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

    }
}
