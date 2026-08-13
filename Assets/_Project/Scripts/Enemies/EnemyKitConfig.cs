#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// Optional visuals for the three drone archetypes. The generated root box
    /// remains the hit hull and NavMeshAgent owner; imported prefabs are visual
    /// children with every collider removed. The generated Core remains the
    /// weakpoint and telegraph, so swapping art cannot change combat geometry.
    /// </summary>
    [CreateAssetMenu(fileName = "Kit_Enemy_", menuName = "CoD/Art/Enemy Kit", order = 72)]
    public sealed class EnemyKitConfig : ScriptableObject
    {
        public GameObject? rusherPrefab;
        public GameObject? shooterPrefab;
        public GameObject? tankPrefab;

        [Tooltip("Explicit shared hull material; the generated emissive Core keeps its own material.")]
        public Material? hullMaterial;

        public bool HasNoAssignments =>
            rusherPrefab == null && shooterPrefab == null &&
            tankPrefab == null && hullMaterial == null;

        public bool HasCompleteAssignments =>
            rusherPrefab != null && shooterPrefab != null &&
            tankPrefab != null && hullMaterial != null;

        public bool IsValid => HasNoAssignments || HasCompleteAssignments;
    }
}
