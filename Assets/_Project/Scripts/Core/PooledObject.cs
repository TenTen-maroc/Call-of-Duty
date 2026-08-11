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
        private Rigidbody? _cachedRigidbody;
        private bool _rigidbodyLookedUp;

        /// <summary>The prefab this instance was cloned from. Set once, by the pool.</summary>
        public GameObject? SourcePrefab { get; private set; }

        /// <summary>True between Spawn and Despawn. Only the pool writes this.</summary>
        public bool IsSpawned { get; private set; }

        /// <summary>
        /// Incremented on every Spawn. A timed despawn records the generation it
        /// spawned with, so a stale timer can never kill a later reuse of the
        /// same instance after it was manually despawned and re-spawned.
        /// </summary>
        public uint SpawnGeneration { get; private set; }

        public Transform CachedTransform => _cachedTransform ??= transform;

        /// <summary>Cached like the Transform — casings and (later) drones touch this on every spawn.</summary>
        public Rigidbody? CachedRigidbody
        {
            get
            {
                if (!_rigidbodyLookedUp)
                {
                    _rigidbodyLookedUp = true;
                    TryGetComponent(out _cachedRigidbody);
                }
                return _cachedRigidbody;
            }
        }

        public void BindTo(GameObject prefab) => SourcePrefab = prefab;

        public void MarkSpawned()
        {
            IsSpawned = true;
            SpawnGeneration++;
        }

        public void MarkDespawned() => IsSpawned = false;
    }
}
