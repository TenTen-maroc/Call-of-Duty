#nullable enable
using CoD.Core;
using CoD.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoD.Tests
{
    public sealed class HumanCombatDataTests
    {
        private const string HumanPrefab = "Assets/_Project/Prefabs/Humans/Meridian_Rifleman.prefab";
        private const string HitZones = "Assets/_Project/Data/Humans/HitZones_Human.asset";
        private const string Gore = "Assets/_Project/Data/Humans/Gore_Human.asset";

        [Test]
        public void HumanHitZoneTable_OwnsEveryRequiredMultiplier()
        {
            HitZoneConfig config = Require<HitZoneConfig>(HitZones);
            Assert.AreEqual(1f, config.Factor(HitRegion.Head), 0.001f);
            Assert.AreEqual(1f, config.Factor(HitRegion.Torso), 0.001f);
            Assert.AreEqual(0.75f, config.Factor(HitRegion.LeftArm), 0.001f);
            Assert.AreEqual(0.75f, config.Factor(HitRegion.RightArm), 0.001f);
            Assert.AreEqual(0.70f, config.Factor(HitRegion.LeftLeg), 0.001f);
            Assert.AreEqual(0.70f, config.Factor(HitRegion.RightLeg), 0.001f);
            Assert.AreEqual(0.45f, config.Factor(HitRegion.Armor), 0.001f);
            Assert.IsFalse(config.IsFlesh(HitRegion.Armor));
        }

        [Test]
        public void GorePolicy_SeparatesOffReducedAndExtreme()
        {
            Assert.IsFalse(GoreRules.AllowsBlood(GoreLevel.Off, HitRegion.Torso));
            Assert.IsFalse(GoreRules.AllowsBlood(GoreLevel.Extreme, HitRegion.Armor));
            Assert.IsTrue(GoreRules.AllowsBlood(GoreLevel.Reduced, HitRegion.LeftArm));

            Assert.IsFalse(GoreRules.ShouldDismember(GoreLevel.Reduced, DamageKind.Explosive,
                HitRegion.Head, 999f, 55f, 65f));
            Assert.IsTrue(GoreRules.ShouldDismember(GoreLevel.Extreme, DamageKind.Direct,
                HitRegion.Head, 55f, 55f, 65f));
            Assert.IsTrue(GoreRules.ShouldDismember(GoreLevel.Extreme, DamageKind.Direct,
                HitRegion.LeftLeg, 65f, 55f, 65f));
            Assert.IsTrue(GoreRules.ShouldDismember(GoreLevel.Extreme, DamageKind.Explosive,
                HitRegion.Torso, 1f, 55f, 65f));
            Assert.IsFalse(GoreRules.ShouldDismember(GoreLevel.Extreme, DamageKind.Explosive,
                HitRegion.Armor, 999f, 55f, 65f));
        }

        [Test]
        public void GoreProfile_UsesTheFixedBudgets()
        {
            GoreProfile profile = Require<GoreProfile>(Gore);
            Assert.AreEqual(96, profile.bloodDecalCap);
            Assert.AreEqual(24, profile.woundCap);
            Assert.AreEqual(12, profile.bloodPoolCap);
            Assert.AreEqual(8, profile.corpseCap);
            Assert.AreEqual(4, profile.ragdollCap);
            Assert.AreEqual(24, profile.severedPartCap);
        }

        [Test]
        public void MeridianPrefab_HasOneSharedHumanoidAnimatorAndAllRegionalHitZones()
        {
            GameObject prefab = Require<GameObject>(HumanPrefab);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.avatar, "the imported Humanoid avatar is missing");
            Assert.IsTrue(animator.avatar.isHuman, "the shared avatar is not Humanoid");
            Assert.IsNotNull(animator.runtimeAnimatorController, "the shared Animator Controller is missing");
            Assert.AreEqual(AnimatorCullingMode.CullUpdateTransforms, animator.cullingMode);
            Assert.IsFalse(animator.applyRootMotion);

            HitZone[] zones = prefab.GetComponentsInChildren<HitZone>(true);
            foreach (HitRegion region in new[] { HitRegion.Head, HitRegion.Torso, HitRegion.LeftArm,
                         HitRegion.RightArm, HitRegion.LeftLeg, HitRegion.RightLeg, HitRegion.Armor })
            {
                bool found = false;
                for (int i = 0; i < zones.Length; i++)
                {
                    if (zones[i].Region == region) found = true;
                }
                Assert.IsTrue(found, "missing hit zone " + region);
            }
        }

        private static T Require<T>(string path) where T : Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, "missing generated asset: " + path);
            return asset!;
        }
    }
}
