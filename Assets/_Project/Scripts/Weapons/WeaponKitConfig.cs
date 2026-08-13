#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// Optional visual replacement for the generated first-person weapon.
    /// Muzzle, casing-eject, sway and firing behavior stay on the generated rig;
    /// this prefab is instantiated beneath Viewmodel as a collider-free child.
    /// </summary>
    [CreateAssetMenu(fileName = "Kit_Weapon_", menuName = "CoD/Art/Weapon Kit", order = 71)]
    public sealed class WeaponKitConfig : ScriptableObject
    {
        [Tooltip("Model only. Author at the generated Viewmodel origin; colliders are stripped on build.")]
        public GameObject? viewmodelPrefab;

        [Tooltip("Explicit material override for every renderer in the imported viewmodel.")]
        public Material? viewmodelMaterial;

        public bool HasNoAssignments => viewmodelPrefab == null && viewmodelMaterial == null;
        public bool HasCompleteAssignments => viewmodelPrefab != null && viewmodelMaterial != null;
        public bool IsValid => HasNoAssignments || HasCompleteAssignments;
    }
}
