#nullable enable
using CoD.Enemies;
using CoD.Waves;
using NUnit.Framework;
using UnityEditor;

namespace CoD.Tests
{
    /// <summary>
    /// The authored waves, as they actually sit on disk.
    ///
    /// These assert the ASSETS rather than the builder's plan, because the gap
    /// between the two is where the bug was: WriteWave only rewrote counts when
    /// the number of entries changed, so a redesign that kept the same entry
    /// count landed nowhere and looked applied. WaveConfig.designVersion closed
    /// that, and this is what proves it stayed closed.
    /// </summary>
    public sealed class WaveDesignTests
    {
        private const int AuthoredWaves = 10;

        private static WaveConfig Load(int number)
        {
            string path = $"Assets/_Project/Data/Waves/Wave_{number:00}.asset";
            var wave = AssetDatabase.LoadAssetAtPath<WaveConfig>(path);
            Assert.IsNotNull(wave, $"{path} is missing — run CoD > Build Grey Box");
            return wave!;
        }

        [Test]
        public void EveryAuthoredWave_Exists_AndSpawnsSomething()
        {
            for (int number = 1; number <= AuthoredWaves; number++)
            {
                WaveConfig wave = Load(number);
                Assert.AreEqual(number, wave.waveNumber, $"Wave_{number:00} has the wrong waveNumber");
                Assert.Greater(wave.entries.Length, 0, $"Wave_{number:00} has no entries and would clear instantly");
                Assert.Greater(wave.TotalCount, 0, $"Wave_{number:00} spawns nothing");

                foreach (WaveConfig.Entry entry in wave.entries)
                {
                    Assert.IsNotNull(entry.drone,
                        $"Wave_{number:00} has an entry with no drone — it would silently spawn nothing");
                }
            }
        }

        [Test]
        public void EveryAuthoredWave_CameFromTheSamePlan()
        {
            int expected = Load(1).designVersion;
            Assert.Greater(expected, 0,
                "designVersion is still 0, so the builder never rewrote these from the authored plan");

            for (int number = 2; number <= AuthoredWaves; number++)
            {
                Assert.AreEqual(expected, Load(number).designVersion,
                    $"Wave_{number:00} was written from a different plan revision than Wave_01");
            }
        }

        [Test]
        public void EveryAuthoredWave_HasAName()
        {
            for (int number = 1; number <= AuthoredWaves; number++)
            {
                WaveConfig wave = Load(number);
                Assert.IsNotEmpty(wave.displayName,
                    $"Wave_{number:00} has no name — identity the player cannot see is not identity");
            }
        }

        [Test]
        public void ClearingALaterWave_PaysMore()
        {
            for (int number = 2; number <= AuthoredWaves; number++)
            {
                Assert.Greater(Load(number).moneyBonusOnClear, Load(number - 1).moneyBonusOnClear,
                    $"Wave_{number:00} pays no more than the wave before it, so surviving longer buys less");
            }
        }

        /// <summary>
        /// The identities are actually distinct, not a ramp wearing names.
        ///
        /// A swarm has to be mostly bodies and a siege mostly guns; if both are
        /// "a few more of everything" then naming them changed nothing, which is
        /// exactly the failure this redesign was meant to fix.
        /// </summary>
        [Test]
        public void SwarmAndSiege_AreShapedDifferently()
        {
            WaveConfig swarm = Load(4);
            WaveConfig siege = Load(5);

            Assert.AreEqual("SWARM", swarm.displayName);
            Assert.AreEqual("SIEGE", siege.displayName);

            Assert.AreEqual(1, swarm.entries.Length, "a swarm with a ranged entry is not a swarm");
            Assert.Greater(swarm.TotalCount, siege.TotalCount,
                "the swarm must out-number the siege, or the names are decoration");

            int siegeRanged = 0;
            int siegeMelee = 0;
            foreach (WaveConfig.Entry entry in siege.entries)
            {
                DroneConfig? drone = entry.drone;
                if (drone == null) continue;
                if (drone.name.Contains("Shooter")) siegeRanged += entry.count;
                else siegeMelee += entry.count;
            }
            Assert.Greater(siegeRanged, siegeMelee,
                "the siege must be mostly ranged, or there is no reason to fight it from cover");
        }
    }
}
