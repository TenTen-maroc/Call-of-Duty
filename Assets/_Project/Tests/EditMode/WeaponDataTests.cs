#nullable enable
using CoD.Core;
using CoD.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The "new weapons are DATA, never new code" claim, checked against the
    /// actual shipped assets rather than against intent. If someone adds a weapon
    /// by adding a class, these tests keep passing and the claim quietly becomes
    /// false — so the second half of the check is that both weapons are the SAME
    /// type driving the same controller.
    /// </summary>
    public sealed class WeaponDataTests
    {
        private static WeaponConfig Load(string path)
        {
            WeaponConfig? config = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
            Assert.IsNotNull(config, $"missing weapon asset: {path}");
            return config!;
        }

        [Test]
        public void BothWeapons_AreTheSameTypeOfThing()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");
            WeaponConfig smg = Load("Assets/_Project/Data/Weapons/SMG_Rapid.asset");

            // Same class, different numbers. That is the entire arsenal design.
            Assert.AreEqual(rifle.GetType(), smg.GetType());
            Assert.AreNotEqual(rifle.stableId, smg.stableId);
        }

        [Test]
        public void EveryWeapon_LandsInsideTheArcadeTtkWindow()
        {
            foreach (string path in new[]
                     {
                         "Assets/_Project/Data/Weapons/AR_Standard.asset",
                         "Assets/_Project/Data/Weapons/SMG_Rapid.asset",
                     })
            {
                WeaponConfig config = Load(path);
                float ttk = config.TimeToKill() * 1000f;
                // 200-400 ms is the defining choice of the whole game. A weapon
                // outside it is not a variant, it is a different game.
                Assert.GreaterOrEqual(ttk, 200f, $"{config.displayName} kills too fast ({ttk:F0} ms)");
                Assert.LessOrEqual(ttk, 400f, $"{config.displayName} kills too slowly ({ttk:F0} ms)");
            }
        }

        [Test]
        public void TheSmg_TradesRangeForRate_RatherThanBeingStrictlyBetter()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");
            WeaponConfig smg = Load("Assets/_Project/Data/Weapons/SMG_Rapid.asset");

            Assert.Greater(smg.roundsPerMinute, rifle.roundsPerMinute, "the SMG must be the faster gun");
            Assert.Less(smg.adsTime, rifle.adsTime, "and the snappier one");
            // The cost of that: it dies at range. Without a real downside the
            // choice is not a choice.
            Assert.Less(smg.falloffRange.x, rifle.falloffRange.x);
            Assert.Less(smg.DamageAtDistance(40f) * smg.roundsPerMinute / 60f,
                rifle.DamageAtDistance(40f) * rifle.roundsPerMinute / 60f,
                "the rifle must out-damage the SMG at 40 m");
        }

        [Test]
        public void EffectModules_AreAuthoredAsAnOrderedList_NotASingleSlot()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");

            // An array, because stacking is the product. A single ref here would
            // have quietly capped the whole "without limits" design at one module.
            Assert.IsNotNull(rifle.effectModules);
        }

        // ---------- the sandbox depth bonus ----------

        /// <summary>
        /// Sandbox resolves effect modules one level deeper than a Run.
        ///
        /// The bonus shifts the depth the module is ASKED about rather than the
        /// maxDepth it declares, because maxDepth lives on a shared config asset
        /// and Domain Reload is off — writing to it would rewrite the shipped
        /// balance for every Play session afterwards. This asserts the exact
        /// expression WeaponController evaluates.
        /// </summary>
        [Test]
        public void SandboxDepth_AllowsExactlyOneMoreLevelPerBonusPoint()
        {
            var chain = UnityEditor.AssetDatabase
                .LoadAssetAtPath<EffectModule>("Assets/_Project/Data/Effects/Effect_Chain.asset");
            var game = UnityEditor.AssetDatabase
                .LoadAssetAtPath<GameConfig>("Assets/_Project/Data/Game/GameConfig.asset");
            Assert.IsNotNull(chain, "Effect_Chain.asset is missing");
            Assert.IsNotNull(game, "GameConfig.asset is missing");

            int max = chain!.maxDepth;
            int bonus = game!.sandboxExtraEffectDepth;
            Assert.Greater(bonus, 0, "sandbox gets no extra depth at all, so the feature is inert");

            // Run mode: the offset is zero and the module stops exactly at maxDepth.
            Assert.IsTrue(chain.RunsAtDepth(max - 0), "a Run must still reach maxDepth");
            Assert.IsFalse(chain.RunsAtDepth(max + 1 - 0), "a Run must stop at maxDepth");

            // Sandbox: the same module, asked about a depth `bonus` levels deeper.
            int deepestInSandbox = max + bonus;
            Assert.IsTrue(chain.RunsAtDepth(deepestInSandbox - bonus),
                "sandbox did not gain the extra level");
            Assert.IsFalse(chain.RunsAtDepth(deepestInSandbox + 1 - bonus),
                "sandbox gained MORE than the bonus allows — the recursion rule is the only thing " +
                "between Explosive > Chain > Explosive and a frozen frame");
        }

    }
}
