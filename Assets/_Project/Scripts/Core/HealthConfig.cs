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
        [Tooltip("Damage multiplier for a weakpoint hit. Headshots should pay on drones too.")]
        [Range(1f, 5f)] public float weakpointMultiplier = 2f;
    }
}
