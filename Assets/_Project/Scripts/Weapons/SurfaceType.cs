#nullable enable
namespace CoD.Weapons
{
    /// <summary>
    /// What a bullet landed ON, and therefore what the world does back.
    ///
    /// WHY THIS IS AN ENUM AND NOT A COMPONENT
    /// The surface is resolved on the hit path, which runs once per pellet per
    /// trigger pull and is reached from Update. A `SurfaceTag` MonoBehaviour
    /// would mean a GetComponent per impact — the exact call
    /// guard-no-find-in-update exists to stop, and the one that is invisible
    /// with a single target and fatal with forty. The lookup is keyed on
    /// `Collider.gameObject.layer` instead: an int field read, allocation-free,
    /// and something the physics engine already had to know.
    ///
    /// The layer that carries each of these is authored in ImpactConfig rather
    /// than hard-coded, because a layer INDEX is not a stable handle — anyone
    /// reordering ProjectSettings/TagManager.asset would silently repoint every
    /// surface in the game with nothing failing.
    ///
    /// APPEND ONLY. Unity serialises an enum as its integer value, so inserting
    /// a member in the middle silently re-labels every authored entry after it:
    /// a metal grate quietly becomes flesh, and no import error is produced.
    /// </summary>
    public enum SurfaceType
    {
        /// <summary>The arena itself — floor, walls, cover. Dust, and a bullet hole.</summary>
        Concrete,

        /// <summary>Hull plate and machinery. Sparks, and a brighter, shorter crack.</summary>
        Metal,

        /// <summary>Walkway mesh and vents. Sparks with no hole: you cannot stamp a decal on something you can see through.</summary>
        Grate,

        /// <summary>
        /// Nothing in this game is flesh yet, and the entry exists anyway.
        ///
        /// Every enemy here is a drone precisely so a solo developer never
        /// touches humanoid animation — but the day a human-shaped target does
        /// arrive, "blood or no blood" has to be a DATA question. Authored now,
        /// a gore level is one prefab reference swapped in one asset; authored
        /// later, it is a branch in the fire path, and a branch in the fire path
        /// is a thing that gets tested in one configuration and shipped in the
        /// other.
        /// </summary>
        Flesh,

        /// <summary>Loose earth and dusty paths.</summary>
        Soil,

        /// <summary>Ochre stone, cliffs, and boulders.</summary>
        Rock,

        /// <summary>Logs, timber barriers, and wooden structures.</summary>
        Wood,

        /// <summary>Leaves and decorative vegetation. Never navigation geometry.</summary>
        Foliage,
    }
}
