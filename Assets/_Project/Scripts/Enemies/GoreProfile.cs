#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>Data-owned thresholds, lifetimes, prefabs, and hard gore budgets.</summary>
    [CreateAssetMenu(fileName = "Gore_", menuName = "CoD/Gore Profile", order = 13)]
    public sealed class GoreProfile : ScriptableObject
    {
        [Header("Pooled presentation")]
        public GameObject? bloodSprayPrefab;
        public GameObject? bloodDecalPrefab;
        public GameObject? woundPrefab;
        public GameObject? bloodPoolPrefab;
        public GameObject? stumpPrefab;
        public GameObject? severedPartPrefab;

        [Header("Lifetime")]
        [Range(0.1f, 10f)] public float sprayLifetime = 1.2f;
        [Range(0.5f, 60f)] public float decalLifetime = 18f;
        [Range(0.5f, 60f)] public float woundLifetime = 14f;
        [Range(0f, 5f)] public float poolDelay = 1.2f;
        [Range(0.5f, 60f)] public float poolLifetime = 20f;
        [Range(0.5f, 30f)] public float severedPartLifetime = 10f;

        [Header("Lethal thresholds")]
        [Min(0f)] public float headDismemberDamage = 55f;
        [Min(0f)] public float limbDismemberDamage = 65f;
        [Range(0f, 8f)] public float explosiveImpulse = 4.5f;

        [Header("Hard caps — oldest recycles first")]
        [Range(1, 128)] public int bloodDecalCap = 96;
        [Range(1, 48)] public int woundCap = 24;
        [Range(1, 24)] public int bloodPoolCap = 12;
        [Range(1, 16)] public int corpseCap = 8;
        [Range(1, 8)] public int ragdollCap = 4;
        [Range(1, 48)] public int severedPartCap = 24;

        [Header("Projection")]
        public LayerMask worldMask = Physics.DefaultRaycastLayers;
        [Range(0.1f, 8f)] public float surfaceProjectionDistance = 3f;
        [Range(0f, 0.05f)] public float surfaceOffset = 0.01f;
    }
}
