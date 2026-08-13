#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>One exclusive, builder-authored position beside gameplay cover.</summary>
    [DisallowMultipleComponent]
    public sealed class CoverPoint : MonoBehaviour
    {
        [SerializeField] private Vector3 _outward = Vector3.forward;
        [SerializeField] private int _lane;
        private DroneController? _claimant;

        public Vector3 Position => transform.position;
        public Vector3 Outward => transform.TransformDirection(_outward).normalized;
        public int Lane => _lane;
        public bool IsClaimed => _claimant != null && _claimant.IsActive;
        public DroneController? Claimant => IsClaimed ? _claimant : null;

        public bool TryClaim(DroneController claimant)
        {
            if (IsClaimed && _claimant != claimant) return false;
            _claimant = claimant;
            return true;
        }

        public void Release(DroneController claimant)
        {
            if (_claimant == claimant) _claimant = null;
        }

#if UNITY_EDITOR
        public void Configure(Vector3 outward, int lane)
        {
            _outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.forward;
            _lane = lane;
        }
#endif
    }
}
