#nullable enable
using System.Collections.Generic;
using CoD.Enemies;
using CoD.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The headshot breakpoint law, checked against the shipped enemy assets.
    ///
    /// THE DEFECT THIS EXISTS FOR. The starter rifle does 25 body and 1.5x on a
    /// weakpoint, so a head hit was 37.5 for everything in the game. The three
    /// drones were tuned against that number and it showed — the Shooter at 75
    /// was exactly 2 headshots and the Tank at 600 was exactly 16. The Meridian
    /// Rifleman shipped at 115, which is 2.5 HP above THREE headshots. A player
    /// who landed three consecutive head hits, the hardest thing this game asks
    /// of them, was told to fire a fourth bullet to remove 2.5 HP.
    ///
    /// (The Tank has since gained a weakpointMultiplier of its own, so its core
    /// is no longer worth 37.5 and it no longer dies to sixteen. HeadshotDamage
    /// below folds both multipliers together for exactly that reason.)
    ///
    /// That is the worst feeling a shooter can produce: the skill was executed
    /// and the game refused to pay it. Nothing caught it, because every gate in
    /// this project checks a weapon against a WINDOW (the 200-400 ms TTK law) and
    /// no gate anywhere compared an enemy's health against the damage that
    /// actually lands on it.
    ///
    /// The law below is deliberately about the LAST shot rather than about round
    /// numbers. Requiring maxHealth to be an exact multiple of a headshot would
    /// be a straitjacket — it would forbid the Rusher's 100, which is fine — so
    /// what is asserted instead is that the final headshot does real work. An
    /// enemy sitting a sliver above a breakpoint charges a whole extra bullet for
    /// a rounding error, and that is the shape of the bug, not the exact value.
    /// </summary>
    public sealed class EnemyBreakpointTests
    {
        private const string DRONE_FOLDER = "Assets/_Project/Data/Drones";
        private const string STARTER_RIFLE_PATH = "Assets/_Project/Data/Weapons/AR_Standard.asset";

        /// <summary>
        /// How much of its damage the killing headshot must actually spend.
        ///
        /// At 0.25 an enemy may sit anywhere in the upper three quarters of a
        /// breakpoint and still pass; only the sliver just above one fails. The
        /// Rifleman's old 115 scored 0.067 against the starter rifle. Every
        /// shipped enemy scores 0.667 or better.
        /// </summary>
        private const float MIN_FINAL_HEADSHOT_FRACTION = 0.25f;

        /// <summary>
        /// Guards the divide, not the design. maxHealth / headshotDamage on a
        /// value that divides exactly can land a hair above the integer in
        /// binary floating point, and CeilToInt would then charge a whole extra
        /// shot for a rounding error — which is the very bug this file is about,
        /// reproduced inside its own gate.
        /// </summary>
        private const float BREAKPOINT_EPSILON = 0.0001f;

        private static WeaponConfig StarterRifle()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponConfig>(STARTER_RIFLE_PATH);
            Assert.IsNotNull(rifle, $"{STARTER_RIFLE_PATH} is missing — run CoD > Build Grey Box");
            return rifle!;
        }

        private static List<DroneConfig> AllEnemies()
        {
            var found = new List<DroneConfig>();
            string[] guids = AssetDatabase.FindAssets("t:DroneConfig", new[] { DRONE_FOLDER });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<DroneConfig>(path);
                if (config != null) found.Add(config);
            }

            Assert.Greater(found.Count, 0, $"No DroneConfig assets under {DRONE_FOLDER}");
            return found;
        }

        /// <summary>
        /// What one head hit actually removes from THIS enemy.
        ///
        /// Two multipliers, and leaving either out models a gun nobody fires.
        /// The weapon's headshotMultiplier says how good the gun is at
        /// exploiting a weak point; the enemy's weakpointMultiplier says how
        /// soft that particular body is there. The Tank's core is worth 2.5x on
        /// top of the rifle's 1.5x, which is the whole reason it stopped being a
        /// twenty-four-round sponge — a law that ignored it would be checking
        /// breakpoints the player never encounters.
        /// </summary>
        private static float HeadshotDamage(WeaponConfig rifle, DroneConfig enemy)
            => rifle.bodyDamage * rifle.headshotMultiplier * enemy.weakpointMultiplier;

        /// <summary>
        /// How much of its damage the last headshot spends, in 0..1. A value of
        /// 1 means maxHealth lands exactly on a breakpoint; a value near 0 means
        /// the enemy is a sliver above one and the final shot is nearly wasted.
        /// </summary>
        private static float FinalHeadshotFraction(float maxHealth, float headshotDamage)
        {
            int headshots = Mathf.CeilToInt(maxHealth / headshotDamage - BREAKPOINT_EPSILON);
            float carriedByEarlierShots = (headshots - 1) * headshotDamage;
            return (maxHealth - carriedByEarlierShots) / headshotDamage;
        }

        [Test]
        public void EveryEnemy_SitsOnAHeadshotBreakpoint_WithTheStarterRifle()
        {
            WeaponConfig rifle = StarterRifle();
            Assert.Greater(rifle.bodyDamage * rifle.headshotMultiplier, 0f,
                "The starter rifle does no weakpoint damage at all");

            foreach (DroneConfig enemy in AllEnemies())
            {
                float headshotDamage = HeadshotDamage(rifle, enemy);
                float fraction = FinalHeadshotFraction(enemy.maxHealth, headshotDamage);
                int headshots = Mathf.CeilToInt(enemy.maxHealth / headshotDamage - BREAKPOINT_EPSILON);

                Assert.GreaterOrEqual(fraction, MIN_FINAL_HEADSHOT_FRACTION,
                    $"{enemy.name} has {enemy.maxHealth} HP, which sits {enemy.maxHealth - (headshots - 1) * headshotDamage:F1} " +
                    $"HP above {headshots - 1} headshots ({headshotDamage} each). The player lands {headshots - 1} " +
                    $"head hits and is charged a {headshots}th bullet for a sliver of health. Drop maxHealth to " +
                    $"{(headshots - 1) * headshotDamage:F0} or below, or raise it well clear of the breakpoint.");
            }
        }

        /// <summary>
        /// Headshots must be worth aiming for. An enemy where head and body cost
        /// the same number of bullets is one where the weakpoint is decoration.
        /// </summary>
        [Test]
        public void EveryEnemy_CostsFewerHeadshotsThanBodyShots()
        {
            WeaponConfig rifle = StarterRifle();

            foreach (DroneConfig enemy in AllEnemies())
            {
                float headshotDamage = HeadshotDamage(rifle, enemy);
                int bodyShots = Mathf.CeilToInt(enemy.maxHealth / rifle.bodyDamage - BREAKPOINT_EPSILON);
                int headshots = Mathf.CeilToInt(enemy.maxHealth / headshotDamage - BREAKPOINT_EPSILON);

                Assert.Less(headshots, bodyShots,
                    $"{enemy.name} takes {headshots} headshots and {bodyShots} body shots — aiming for the " +
                    "weakpoint buys the player nothing, so the weakpoint is decoration.");
            }
        }

        /// <summary>
        /// The Rifleman's actual regression, pinned to the number.
        ///
        /// The general law above would pass again the moment someone nudged the
        /// value back to something merely legal, so this holds the specific asset
        /// to the specific breakpoint the fix was about.
        /// </summary>
        [Test]
        public void TheRifleman_DiesToThreeHeadshots()
        {
            var rifleman = AssetDatabase.LoadAssetAtPath<DroneConfig>(
                $"{DRONE_FOLDER}/Meridian_Rifleman.asset");
            Assert.IsNotNull(rifleman, "Meridian_Rifleman.asset is missing");

            WeaponConfig rifle = StarterRifle();
            float headshotDamage = HeadshotDamage(rifle, rifleman!);

            Assert.LessOrEqual(rifleman!.maxHealth, headshotDamage * 3f,
                $"Three starter-rifle headshots deal {headshotDamage * 3f} and the Rifleman has " +
                $"{rifleman.maxHealth} HP. It shipped at 115 for exactly this reason — three perfect head " +
                "hits did not kill, and the fourth bullet removed 2.5 HP.");

            Assert.Greater(rifleman.maxHealth, headshotDamage * 2f,
                "The Rifleman now dies to TWO headshots, which makes him softer than a Rusher " +
                "and undoes the reason he is the tougher enemy.");
        }

        /// <summary>
        /// The gate, watched biting.
        ///
        /// A law nobody has seen reject anything is a law nobody knows is
        /// connected — the same reasoning WeaponDataTests applies to its own
        /// exemptions. This rebuilds the exact defect in memory and asserts the
        /// check refuses it.
        /// </summary>
        [Test]
        public void TheLaw_RejectsAnEnemyASliverAboveABreakpoint()
        {
            WeaponConfig rifle = StarterRifle();
            float headshotDamage = rifle.bodyDamage * rifle.headshotMultiplier;

            // 115 against a 37.5 headshot: the shipped defect, exactly.
            float shipped = FinalHeadshotFraction(115f, headshotDamage);
            Assert.Less(shipped, MIN_FINAL_HEADSHOT_FRACTION,
                "The Rifleman's original 115 HP no longer trips this law, so the law has stopped " +
                "describing the bug it was written for.");

            // And the fix passes it.
            float fixedValue = FinalHeadshotFraction(110f, headshotDamage);
            Assert.GreaterOrEqual(fixedValue, MIN_FINAL_HEADSHOT_FRACTION,
                "110 HP should sit comfortably inside the law");
        }

        /// <summary>
        /// The epsilon, watched doing its job. An enemy authored exactly on a
        /// breakpoint is the CORRECT case (the Shooter and the Tank both are),
        /// and it is the one floating point is most likely to get wrong.
        /// </summary>
        [Test]
        public void AnEnemyExactlyOnABreakpoint_CountsAsWholeShots()
        {
            WeaponConfig rifle = StarterRifle();
            float headshotDamage = rifle.bodyDamage * rifle.headshotMultiplier;

            Assert.AreEqual(1f, FinalHeadshotFraction(headshotDamage * 2f, headshotDamage), 0.001f,
                "An enemy at exactly two headshots should spend the whole second one");
            Assert.AreEqual(1f, FinalHeadshotFraction(headshotDamage * 16f, headshotDamage), 0.001f,
                "A value sixteen breakpoints up must not read as seventeen");
        }

        /// <summary>
        /// The Tank stopped being a sponge, pinned to the number.
        ///
        /// The point of its weakpointMultiplier is that circling for the core is
        /// worth the risk of a 34-damage slam. If the core ever stops paying for
        /// that trip the Tank quietly reverts to two seconds of holding the
        /// trigger, and nothing else in the suite would notice.
        /// </summary>
        [Test]
        public void TheTank_RewardsGoingForTheCore()
        {
            var tank = AssetDatabase.LoadAssetAtPath<DroneConfig>($"{DRONE_FOLDER}/Drone_Tank.asset");
            Assert.IsNotNull(tank, "Drone_Tank.asset is missing");

            WeaponConfig rifle = StarterRifle();
            int bodyShots = Mathf.CeilToInt(tank!.maxHealth / rifle.bodyDamage - BREAKPOINT_EPSILON);
            int coreShots = Mathf.CeilToInt(tank.maxHealth / HeadshotDamage(rifle, tank) - BREAKPOINT_EPSILON);

            Assert.GreaterOrEqual(bodyShots - coreShots, 12,
                $"The Tank takes {bodyShots} body shots and {coreShots} core shots. Going for the core has " +
                "to save enough rounds to be worth closing the angle on something that slams for 34 in a " +
                "4.5 m radius — otherwise the correct play is to stand back and grind, which is the " +
                "encounter this multiplier exists to remove.");

            Assert.Greater(coreShots, 3,
                $"The Tank now dies to {coreShots} core shots, which makes the slowest, heaviest enemy in " +
                "the game meltable before it closes. The core is meant to be an answer, not a delete key.");
        }
    }
}
