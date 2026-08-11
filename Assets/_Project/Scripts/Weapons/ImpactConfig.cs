#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// What the world does back when a bullet lands. Half of whether a gun feels
    /// good is impact feedback, not the weapon itself — a shot into a wall that
    /// produces nothing reads as a miss.
    /// One surface type is enough for the grey box; this becomes a per-surface
    /// lookup when the arena gets real materials.
    /// </summary>
    [CreateAssetMenu(fileName = "Impact_", menuName = "CoD/Impact Config", order = 10)]
    public sealed class ImpactConfig : ScriptableObject
    {
        public GameObject? decalPrefab;
        public GameObject? particlePrefab;
        public AudioClip? impactSound;
        [Range(0.5f, 60f)] public float decalLifetime = 20f;
        [Range(0.1f, 10f)] public float particleLifetime = 2f;
        [Tooltip("Lifts the decal off the surface so it does not z-fight with the wall it is on.")]
        [Range(0f, 0.05f)] public float surfaceOffset = 0.008f;
    }
}
