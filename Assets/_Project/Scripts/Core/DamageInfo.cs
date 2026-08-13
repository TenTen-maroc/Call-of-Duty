#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// One damage event. A readonly struct passed by `in` so it never allocates
    /// and never mutates behind the caller's back — hundreds of these fly per
    /// second once the horde arrives.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Vector3 Direction;
        public readonly bool IsWeakpoint;
        public readonly HitRegion Region;
        public readonly DamageKind Kind;

        public DamageInfo(float amount, Vector3 point, Vector3 normal, Vector3 direction, bool isWeakpoint)
            : this(amount, point, normal, direction, isWeakpoint,
                isWeakpoint ? HitRegion.Head : HitRegion.Torso, DamageKind.Direct)
        {
        }

        public DamageInfo(float amount, Vector3 point, Vector3 normal, Vector3 direction, bool isWeakpoint,
            HitRegion region, DamageKind kind)
        {
            Amount = amount;
            Point = point;
            Normal = normal;
            Direction = direction;
            IsWeakpoint = isWeakpoint;
            Region = region;
            Kind = kind;
        }
    }
}
