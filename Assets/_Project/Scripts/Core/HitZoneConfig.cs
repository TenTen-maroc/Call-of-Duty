#nullable enable
using System;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>One data-owned table for anatomical damage factors.</summary>
    [CreateAssetMenu(fileName = "HitZones", menuName = "CoD/Hit Zone Config", order = 4)]
    public sealed class HitZoneConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public HitRegion region;
            [Min(0f)] public float damageFactor;
            public bool fleshImpact;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public float Factor(HitRegion region)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].region == region) return Mathf.Max(0f, entries[i].damageFactor);
            }
            return 1f;
        }

        public bool IsFlesh(HitRegion region)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].region == region) return entries[i].fleshImpact;
            }
            return region != HitRegion.Armor;
        }
    }
}
