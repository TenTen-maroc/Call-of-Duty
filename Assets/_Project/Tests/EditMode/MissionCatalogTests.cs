#nullable enable
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The catalog is the whole menu-to-arena resolution path.
    ///
    /// The menu writes a mission's stableId into the save and the arena reads it
    /// back — the save file being the only sanctioned channel, because Domain
    /// Reload is off and a static carrier would survive into the next Play
    /// session. The catalog is what turns that string back into an asset, so a
    /// silent failure here does not look like a broken catalog. It looks like
    /// "the campaign does nothing", which is the hardest kind of bug to trace
    /// back to its cause.
    /// </summary>
    public sealed class MissionCatalogTests
    {
        private static MissionConfig Mission(string id)
        {
            MissionConfig mission = ScriptableObject.CreateInstance<MissionConfig>();
            mission.stableId = id;
            mission.displayName = id.ToUpperInvariant();
            return mission;
        }

        private static MissionCatalog Catalog(params MissionConfig[] missions)
        {
            MissionCatalog catalog = ScriptableObject.CreateInstance<MissionCatalog>();
            catalog.missions = missions;
            return catalog;
        }

        [Test]
        public void FindsAMissionByTheIdTheSaveStores()
        {
            MissionConfig second = Mission("mission_02");
            MissionCatalog catalog = Catalog(Mission("mission_01"), second, Mission("mission_03"));

            Assert.AreSame(second, catalog.Find("mission_02"));
            Assert.AreEqual(1, catalog.IndexOf("mission_02"));
        }

        /// <summary>
        /// The exact shape of an old save pointing at a mission that has since
        /// been renamed or deleted. It must come back null so the director can
        /// fall back to the endless loop, rather than throw or hand back the
        /// wrong mission.
        /// </summary>
        [Test]
        public void AnUnknownIdIsNull_RatherThanTheWrongMission()
        {
            MissionCatalog catalog = Catalog(Mission("mission_01"), Mission("mission_02"));

            Assert.IsNull(catalog.Find("mission_99"));
            Assert.AreEqual(-1, catalog.IndexOf("mission_99"));
        }

        /// <summary>
        /// An empty id is what a save written by the ENDLESS path carries, and
        /// the director asks the catalog before it knows which it is looking at.
        /// </summary>
        [Test]
        public void AnEmptyIdIsNull_BecauseThatIsWhatEndlessLooksLike()
        {
            MissionCatalog catalog = Catalog(Mission("mission_01"));

            Assert.IsNull(catalog.Find(string.Empty));
            Assert.IsNull(catalog.Find(null!));
        }

        /// <summary>
        /// Index IS mission number, so out-of-range has to be null rather than
        /// an exception — the select screen asks about slots it is drawing.
        /// </summary>
        [Test]
        public void AtIsBoundsSafeInBothDirections()
        {
            MissionCatalog catalog = Catalog(Mission("mission_01"));

            Assert.IsNotNull(catalog.At(0));
            Assert.IsNull(catalog.At(1));
            Assert.IsNull(catalog.At(-1));
            Assert.AreEqual(1, catalog.Count);
        }

        /// <summary>
        /// A null slot must not stop the lookup: index is mission number, so a
        /// hole misnumbers everything after it, and Find still has to reach the
        /// missions past the hole rather than throwing on the way.
        /// </summary>
        [Test]
        public void AHoleInTheListDoesNotHideTheMissionsAfterIt()
        {
            MissionCatalog catalog = Catalog(Mission("mission_01"), null!, Mission("mission_03"));

            Assert.IsNotNull(catalog.Find("mission_03"));
            Assert.AreEqual(2, catalog.IndexOf("mission_03"));
            Assert.IsNull(catalog.At(1));
        }

        /// <summary>
        /// An empty catalog is the shipped state until the first mission is
        /// authored, and the menu opens on it. It must answer, not throw.
        /// </summary>
        [Test]
        public void AnEmptyCatalogAnswersEverything()
        {
            MissionCatalog catalog = Catalog();

            Assert.AreEqual(0, catalog.Count);
            Assert.IsNull(catalog.Find("anything"));
            Assert.IsNull(catalog.At(0));
            Assert.AreEqual(-1, catalog.IndexOf("anything"));
        }
    }
}
