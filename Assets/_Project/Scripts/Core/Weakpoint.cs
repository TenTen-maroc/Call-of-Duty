#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Marks a child collider as the owner's weakpoint. The weapon looks for
    /// this on whatever collider the ray hit, forwards the damage to the owner's
    /// Health, and flags the DamageInfo as a weakpoint hit — which is where the
    /// headshot multipliers come in. Purely a marker: no state, no Update.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class Weakpoint : MonoBehaviour
    {
        [Tooltip("The Health that takes the damage — the parent body, not this collider.")]
        [SerializeField] private Health? _owner = null;

        public Health? Owner => _owner != null ? _owner : (_owner = GetComponentInParent<Health>());
    }
}
