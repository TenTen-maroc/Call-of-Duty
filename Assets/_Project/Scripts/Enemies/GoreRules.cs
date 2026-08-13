#nullable enable
using CoD.Core;

namespace CoD.Enemies
{
    /// <summary>Pure gore policy shared by runtime presentation and EditMode tests.</summary>
    public static class GoreRules
    {
        public static bool AllowsBlood(GoreLevel level, HitRegion region)
            => level != GoreLevel.Off && region != HitRegion.Armor;

        public static bool ShouldDismember(GoreLevel level, DamageKind kind, HitRegion region,
            float damage, float headThreshold, float limbThreshold)
        {
            if (level != GoreLevel.Extreme || region == HitRegion.Armor) return false;
            if (kind == DamageKind.Explosive) return true;
            if (region == HitRegion.Head) return damage >= headThreshold;
            return IsLimb(region) && damage >= limbThreshold;
        }

        public static bool IsLimb(HitRegion region) => region is HitRegion.LeftArm or HitRegion.RightArm
            or HitRegion.LeftLeg or HitRegion.RightLeg;

        public static bool IsDismemberable(HitRegion region) => region == HitRegion.Head || IsLimb(region);
    }
}
