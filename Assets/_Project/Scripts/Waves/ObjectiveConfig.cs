#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// The repair beacon's numbers.
    ///
    /// The beacon exists to answer tuning-card item 8 — "does breaking line of
    /// sight actually change a fight, or do drones still arrive as one mass" —
    /// from the other direction. The arena has three lanes and, until now,
    /// nothing that rewarded being in any particular one. A heal that moves every
    /// wave makes standing still cost something without adding a single rule the
    /// player has to be taught.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_", menuName = "CoD/Objective Config", order = 63)]
    public sealed class ObjectiveConfig : ScriptableObject
    {
        [Tooltip("How close the player must stand, in metres, measured on the floor plane rather than as a sphere.")]
        [Range(1f, 8f)] public float radius = 2.5f;

        [Tooltip("Health per second while standing inside it.")]
        [Range(1f, 40f)] public float healPerSecond = 6f;

        [Tooltip("Total health it can give in ONE wave. This is the number that stops it being a free reset " +
                 "and turns it into a decision about when to go.")]
        [Range(0f, 200f)] public float healBudgetPerWave = 35f;
    }
}
