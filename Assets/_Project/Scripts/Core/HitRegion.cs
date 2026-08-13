#nullable enable

namespace CoD.Core
{
    /// <summary>
    /// Append-only anatomical regions. Serialized HitZone assets store these
    /// numeric values, so existing entries must never be reordered.
    /// </summary>
    public enum HitRegion
    {
        Torso = 0,
        Head = 1,
        LeftArm = 2,
        RightArm = 3,
        LeftLeg = 4,
        RightLeg = 5,
        Armor = 6,
    }

    /// <summary>How the damage arrived; presentation uses this to choose ragdoll and gore.</summary>
    public enum DamageKind
    {
        Direct = 0,
        Explosive = 1,
    }
}
