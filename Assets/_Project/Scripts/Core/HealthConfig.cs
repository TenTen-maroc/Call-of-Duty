#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Max health for anything that is not a drone (grey-box targets, props).
    /// Drones get theirs from DroneConfig once that lands.
    /// </summary>
    [CreateAssetMenu(fileName = "Health_", menuName = "CoD/Health Config", order = 20)]
    public sealed class HealthConfig : ScriptableObject
    {
        [Range(1f, 10000f)] public float maxHealth = 100f;
        // The headshot bonus lives on WeaponConfig.headshotMultiplier — ONE owner.
        // A second multiplier here double-dipped every weakpoint hit.
        [Tooltip("Grey-box dummy targets: seconds a dead target stays down before it resets. Drones despawn instead.")]
        [Range(0.5f, 30f)] public float targetRespawnSeconds = 2.5f;
    }
}
