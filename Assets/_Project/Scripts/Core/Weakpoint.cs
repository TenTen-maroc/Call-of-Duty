#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Marks a child collider as the owner's weakpoint. The weapon looks for
    /// this on whatever collider the ray hit, forwards the damage to the owner's
    /// Health, and flags the DamageInfo as a weakpoint hit — which is where the
    /// headshot multipliers come in. Still a marker: no Update, and the one
    /// piece of state it holds is written once at spawn.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class Weakpoint : MonoBehaviour
    {
        [Tooltip("The Health that takes the damage — the parent body, not this collider.")]
        [SerializeField] private Health? _owner = null;

        /// <summary>
        /// How much this particular body's weakpoint is worth, on top of the
        /// weapon's own bonus.
        ///
        /// It lives on the COMPONENT rather than being read from a config by the
        /// weapon, because the weapon has no idea what it just shot. It hits a
        /// collider; the collider knows what it belongs to. Written at spawn by
        /// DroneController from DroneConfig, so a pooled Tank and a pooled Rusher
        /// reusing the same prefab slot never inherit each other's value.
        ///
        /// Defaults to 1, which is exactly the behaviour before this existed —
        /// so a hand-placed weakpoint that nobody initialises is unchanged.
        /// </summary>
        [Tooltip("Set at spawn from DroneConfig. 1 = the weapon's weakpoint bonus alone.")]
        [SerializeField] private float _multiplier = 1f;

        public Health? Owner => _owner != null ? _owner : (_owner = GetComponentInParent<Health>());

        public float Multiplier => _multiplier;

        /// <summary>Spawn-time only. Never per frame — this is authored data reaching an instance.</summary>
        public void SetMultiplier(float value) => _multiplier = Mathf.Max(1f, value);
    }
}
