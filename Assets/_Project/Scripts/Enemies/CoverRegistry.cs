#nullable enable
using System;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// Scene-owned bounded cover query. Decisions scan a rotating slice instead
    /// of every soldier searching every point every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoverRegistry : MonoBehaviour
    {
        [SerializeField] private CoverPoint[] _points = Array.Empty<CoverPoint>();
        private int _cursor;

        public int Count => _points.Length;

        public bool TryClaimBest(DroneController claimant, Vector3 from, Vector3 threat,
            float searchRadius, int preferredDifferentLane, float laneBonus, int checks,
            out CoverPoint? selected)
        {
            selected = null;
            if (_points.Length == 0) return false;

            float radiusSq = searchRadius * searchRadius;
            float bestScore = float.MaxValue;
            int bounded = Mathf.Clamp(checks, 1, _points.Length);
            int start = _cursor;
            _cursor = (_cursor + bounded) % _points.Length;

            for (int i = 0; i < bounded; i++)
            {
                CoverPoint point = _points[(start + i) % _points.Length];
                if (point == null || (point.IsClaimed && point.Claimant != claimant)) continue;

                Vector3 offset = point.Position - from;
                float distanceSq = offset.sqrMagnitude;
                if (distanceSq > radiusSq) continue;

                Vector3 toThreat = threat - point.Position;
                toThreat.y = 0f;
                if (toThreat.sqrMagnitude < 0.01f) continue;
                float exposure = Mathf.Clamp01(Vector3.Dot(point.Outward, toThreat.normalized));
                float score = distanceSq + exposure * radiusSq;
                if (point.Lane != preferredDifferentLane) score -= laneBonus;
                if (score >= bestScore) continue;
                bestScore = score;
                selected = point;
            }

            return selected != null && selected.TryClaim(claimant);
        }

#if UNITY_EDITOR
        public void Configure(CoverPoint[] points) => _points = points ?? Array.Empty<CoverPoint>();
#endif
    }
}
