#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Collider-to-body relay for a humanoid bone or armor plate. The weapon
    /// reads the factor here; Health still receives one allocation-free value.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitZone : MonoBehaviour
    {
        [SerializeField] private Health? _owner = null;
        [SerializeField] private HitZoneConfig? _config = null;
        [SerializeField] private HitRegion _region = HitRegion.Torso;

        public Health? Owner => _owner;
        public HitRegion Region => _region;
        public float DamageFactor => _config != null ? _config.Factor(_region) : 1f;
        public bool IsFlesh => _config == null ? _region != HitRegion.Armor : _config.IsFlesh(_region);

#if UNITY_EDITOR
        public void Configure(Health owner, HitZoneConfig config, HitRegion region)
        {
            _owner = owner;
            _config = config;
            _region = region;
        }
#endif
    }
}
