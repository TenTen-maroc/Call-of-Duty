#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Added to every pooled instance when the pool first creates it. Holds the
    /// prefab it came from (so Despawn knows which stack to return it to) and a
    /// cached Transform, because `transform` is a property call into native code
    /// and pooled things are touched constantly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        private Transform? _cachedTransform;

        /// <summary>The prefab this instance was cloned from. Set once, by the pool.</summary>
        public GameObject? SourcePrefab { get; private set; }

        public Transform CachedTransform => _cachedTransform ??= transform;

        public void BindTo(GameObject prefab) => SourcePrefab = prefab;
    }
}
