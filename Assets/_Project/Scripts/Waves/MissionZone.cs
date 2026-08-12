#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// A named place a mission can send the player.
    ///
    /// Objectives address zones by a small integer id rather than by a scene
    /// reference, for the reason every objective rule exists: an objective is a
    /// ScriptableObject shared by every mission that uses it, so it cannot hold
    /// a Transform. The id is the indirection that lets one authored
    /// "hold the control point" asset mean a different pad in every arena.
    ///
    /// The marker is a plain Transform, not a trigger volume. This project has
    /// no trigger colliders anywhere and the one player-in-zone test that
    /// already works — the repair beacon — is a floor-plane distance check
    /// against a serialized transform. A second spatial model would be a second
    /// thing to keep consistent with the first.
    /// </summary>
    [System.Serializable]
    public struct MissionZone
    {
        [Tooltip("What objectives call this place. Small and stable; missions reference it by number.")]
        [Min(0)] public int id;

        [Tooltip("Where it is. Usually an empty child of the arena, or the beacon anchors.")]
        public Transform? marker;

        [Tooltip("Floor-plane metres. Measured on the floor because the player's origin is at their feet and a zone is a pad.")]
        [Min(0.5f)] public float radius;
    }
}
