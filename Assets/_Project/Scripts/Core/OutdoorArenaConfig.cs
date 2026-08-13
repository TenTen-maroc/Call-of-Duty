#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Builder-owned layout data for the fixed Atlas outpost. The scene is a
    /// generated projection of this asset: gameplay collision, cover, spawns,
    /// lighting, and decorative placements all originate here.
    /// </summary>
    [CreateAssetMenu(fileName = "Arena_", menuName = "CoD/Outdoor Arena Config", order = 6)]
    public sealed class OutdoorArenaConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Block
        {
            public string name;
            public Vector3 position;
            public Vector3 size;
            public SurfaceKind surface;
        }

        [System.Serializable]
        public struct Point
        {
            public string name;
            public Vector3 position;
            public Vector3 outward;
            public int lane;
        }

        [System.Serializable]
        public struct Decoration
        {
            public string assetPath;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
            public bool lod;
            public bool castsShadow;
        }

        public enum SurfaceKind
        {
            Soil,
            Rock,
            Wood,
            Metal,
        }

        [Header("Identity")]
        public string displayLocation = "TAZIR PASS OUTPOST";
        [Min(1)] public int contentVersion = 1;

        [Header("Environment")]
        public Color sunColor = new(1f, 0.78f, 0.55f);
        [Range(0f, 4f)] public float sunIntensity = 1.25f;
        public Vector3 sunEuler = new(38f, -28f, 0f);
        public Color fogColor = new(0.36f, 0.43f, 0.48f);
        [Min(0f)] public float fogStart = 32f;
        [Min(1f)] public float fogEnd = 94f;

        [Header("Generated layout")]
        public Block[] blocks = System.Array.Empty<Block>();
        public Point[] spawnPoints = System.Array.Empty<Point>();
        public Point[] coverPoints = System.Array.Empty<Point>();
        public Decoration[] decorations = System.Array.Empty<Decoration>();
    }
}
