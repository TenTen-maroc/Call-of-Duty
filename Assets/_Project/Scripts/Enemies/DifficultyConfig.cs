#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// The caps that protect the frame rate and the feel. Lives in CoD.Enemies
    /// rather than with the wave code because the spawner needs it and the
    /// dependency only runs one way (waves reference enemies, never the reverse).
    ///
    /// Both caps are load-bearing:
    /// - maxAliveDrones protects a 4 GB GPU. Past it the spawn queue waits for
    ///   deaths instead of adding bodies.
    /// - maxSimultaneousAttackers is why twenty enemies feels fair rather than
    ///   instantly lethal. See IAttackTokenSource.
    /// </summary>
    [CreateAssetMenu(fileName = "Difficulty", menuName = "CoD/Difficulty Config", order = 30)]
    public sealed class DifficultyConfig : ScriptableObject
    {
        [Header("Hard caps — not tuning knobs")]
        [Range(1, 200)] public int maxAliveDrones = 40;
        [Range(1, 20)] public int maxSimultaneousAttackers = 3;

        [Header("Spawning")]
        [Tooltip("A drone that appears inside the player's personal space is a cheap death, not a challenge.")]
        [Range(0f, 40f)] public float minSpawnDistanceFromPlayer = 12f;
        [Tooltip("How far from a spawn point the navmesh may be sampled before the point is skipped.")]
        [Range(0.5f, 20f)] public float spawnSampleRadius = 4f;
        [Tooltip("Seconds a drone may hold an attack token before it is reclaimed. Stops one stuck drone from starving the pack.")]
        [Range(0.5f, 30f)] public float attackTokenTimeout = 6f;
    }
}
