#nullable enable
using System.Collections.Generic;
using CoD.Core;
using CoD.Enemies;
using NUnit.Framework;
using UnityEditor;

namespace CoD.Tests
{
    /// <summary>
    /// The arena has to stay darker than the things trying to kill you.
    ///
    /// THE DEFECT THIS EXISTS FOR. Every enemy carries an emissive core that
    /// ramps from ~0.4 idle to ~4.0 the instant it commits to an attack, and
    /// CLAUDE.md calls that ramp a fairness contract rather than decoration —
    /// it is the channel the player reads danger from. The arena shipped lit at
    /// a sun of 0.85 with lane lights at 1.6 and a key at 2.2, so the room was
    /// brighter than the threat in it and a telegraph resolved as a slightly
    /// lighter dot. The contract was written, wired, tested and invisible.
    ///
    /// No gate could see it. Lighting is the one system in this project where
    /// every automated check passes on a scene that looks wrong: the lights
    /// exist, the references are non-null, the emission ramp fires, the tests
    /// are green. Only a human looking at it can say it reads badly — which is
    /// exactly why the NUMBERS need a law even though the LOOK cannot have one.
    ///
    /// So this file does not assert that the arena looks good. It asserts the
    /// two things that made it look bad, both of which are arithmetic:
    /// the rig stayed dim, and the telegraph stayed a jump rather than a nudge.
    /// </summary>
    public sealed class ArenaLightingTests
    {
        private const string PALETTE_PATH = "Assets/_Project/Data/Game/Palette_GreyBox.asset";
        private const string DRONE_FOLDER = "Assets/_Project/Data/Drones";

        /// <summary>
        /// The ceiling the rig was brought down to, with headroom for tuning.
        ///
        /// These are NOT the authored values — the authored values are 0.35 /
        /// 0.8 / 1.1 and live in the asset, where a human can slide them while
        /// looking at the game. These are the point at which the room starts
        /// drowning the telegraph again, which is the regression worth failing a
        /// build over.
        /// </summary>
        private const float MAX_SUN_INTENSITY = 0.6f;
        private const float MAX_LANE_INTENSITY = 1.2f;
        private const float MAX_KEY_INTENSITY = 1.5f;

        /// <summary>
        /// How much brighter a telegraph must be than the same core at rest.
        ///
        /// A telegraph the player has to A/B against memory is not a telegraph.
        /// Every shipped enemy clears 8x; the floor is set at 4x so the law
        /// catches a core that stopped ramping, not one that was tuned.
        /// </summary>
        private const float MIN_TELEGRAPH_RATIO = 4f;

        private static PaletteConfig Palette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<PaletteConfig>(PALETTE_PATH);
            Assert.IsNotNull(palette, $"{PALETTE_PATH} is missing — run CoD > Build Grey Box");
            return palette!;
        }

        private static List<DroneConfig> AllEnemies()
        {
            var found = new List<DroneConfig>();
            foreach (string guid in AssetDatabase.FindAssets("t:DroneConfig", new[] { DRONE_FOLDER }))
            {
                var config = AssetDatabase.LoadAssetAtPath<DroneConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) found.Add(config);
            }

            Assert.Greater(found.Count, 0, $"No DroneConfig assets under {DRONE_FOLDER}");
            return found;
        }

        [Test]
        public void TheArenaRig_StaysDim()
        {
            PaletteConfig palette = Palette();

            Assert.LessOrEqual(palette.sunIntensity, MAX_SUN_INTENSITY,
                $"The sun is back up to {palette.sunIntensity}. It shipped at 0.85 and washed out every " +
                "drone core in the arena; the sun is here for shape and shadow, not for illumination.");

            Assert.LessOrEqual(palette.laneLightIntensity, MAX_LANE_INTENSITY,
                $"The lane lights are back up to {palette.laneLightIntensity}. They shipped at 1.6, which " +
                "is brighter than an idle drone core and roughly half a telegraph.");

            Assert.LessOrEqual(palette.keyLightIntensity, MAX_KEY_INTENSITY,
                $"The bunker key light is back up to {palette.keyLightIntensity}. It shipped at 2.2 — the " +
                "brightest thing in a room whose whole read depends on the enemies being the brightest thing.");
        }

        /// <summary>
        /// Ambient is the quiet one. Lights can be dropped to nothing and a
        /// bright ambient term still floods every surface evenly, which produces
        /// precisely the flat look the rig drop exists to remove — with no light
        /// in the scene to blame for it.
        /// </summary>
        [Test]
        public void AmbientStaysBelowTheRig()
        {
            PaletteConfig palette = Palette();
            float skyLuminance = palette.ambientSky.grayscale;

            Assert.Less(skyLuminance, 0.25f,
                $"Ambient sky luminance is {skyLuminance:F3}. Ambient does not cast, does not fall off and " +
                "cannot be aimed — a bright one erases contrast everywhere at once and no light in the " +
                "scene will look responsible for it.");

            Assert.Less(palette.ambientGround.grayscale, palette.ambientEquator.grayscale,
                "Ground ambient is brighter than the equator, so the floor reads as lit from below — " +
                "the arena is a sealed interior, not a light table.");
        }

        [Test]
        public void EveryEnemy_TelegraphsWithARealJump()
        {
            foreach (DroneConfig enemy in AllEnemies())
            {
                Assert.Greater(enemy.idleEmission, 0f,
                    $"{enemy.name} has no idle emission, so its core is unlit until it attacks — " +
                    "the player cannot learn to read a channel that is off most of the time.");

                float ratio = enemy.telegraphEmission / enemy.idleEmission;
                Assert.GreaterOrEqual(ratio, MIN_TELEGRAPH_RATIO,
                    $"{enemy.name} ramps {enemy.idleEmission} to {enemy.telegraphEmission} ({ratio:F1}x). " +
                    "Below 4x the player has to compare against memory to notice a windup, and the " +
                    "attack telegraph is a fairness contract rather than decoration.");
            }
        }

        /// <summary>
        /// The trim line carries the arena's silhouette now that the rig is dim.
        /// Half-height cover is only cover if "can I shoot over that" is
        /// answerable from across the room.
        /// </summary>
        [Test]
        public void TheTrimStaysReadable()
        {
            PaletteConfig palette = Palette();

            Assert.GreaterOrEqual(palette.trimEmission, 1.4f,
                $"Trim emission is {palette.trimEmission}. Under the dimmed rig the trim is no longer a " +
                "highlight on a block you can already see — it is the only edge the player can read at " +
                "range, and cover you cannot see the edge of is cover you do not use.");
        }
    }
}
