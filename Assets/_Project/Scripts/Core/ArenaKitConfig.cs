#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Optional presentation for the generated arena. Gameplay geometry never
    /// comes from these references: GreyBoxBuilder always authors the same box
    /// colliders and uses these unit modules only as collider-free children.
    ///
    /// Empty is a supported, shippable state. Partially assigned is not: a
    /// mixed kit creates an arena that looks half imported while every gameplay
    /// test continues to pass, so GreyBoxVerify rejects it.
    /// </summary>
    [CreateAssetMenu(fileName = "Kit_Arena_", menuName = "CoD/Art/Arena Kit", order = 70)]
    public sealed class ArenaKitConfig : ScriptableObject
    {
        [Header("Floor — unit cube authored around the origin")]
        public GameObject? floorModule;
        public Material? floorMaterial;

        [Header("Walls, dividers, bunker, cover and pillars — unit cube")]
        public GameObject? wallModule;
        public Material? wallMaterial;

        [Header("Interior reflection — procedural sky remains the visible background")]
        public Cubemap? reflectionCubemap;
        [Range(0f, 1f)] public float reflectionIntensity = 0.35f;

        public bool HasNoAssignments =>
            floorModule == null && floorMaterial == null &&
            wallModule == null && wallMaterial == null &&
            reflectionCubemap == null;

        public bool HasCompleteAssignments =>
            floorModule != null && floorMaterial != null &&
            wallModule != null && wallMaterial != null &&
            reflectionCubemap != null;

        public bool IsValid => HasNoAssignments || HasCompleteAssignments;
    }
}
