#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// What the world does back when a bullet lands. Half of whether a gun feels
    /// good is impact feedback, not the weapon itself — a shot into a wall that
    /// produces nothing reads as a miss.
    ///
    /// A TABLE, KEYED ON PHYSICS LAYER. This asset used to describe exactly one
    /// surface, and its own comment called that a placeholder until the arena
    /// got real materials. The cost of the placeholder was that every impact in
    /// the game — concrete wall, drone hull, walkway mesh — produced the same
    /// spark, and that `impactSound` sat here from the day the file was written
    /// and was read by NOTHING. A gun that sounds identical hitting a wall and
    /// hitting an enemy is a gun with no feedback at all.
    ///
    /// WHY LAYERS RATHER THAN A COMPONENT ON THE SURFACE
    /// See <see cref="SurfaceType"/>. Short version: the lookup happens on the
    /// hit path, a component lookup there is the defect guard-no-find-in-update
    /// exists to catch, and `Collider.gameObject.layer` is an int the physics
    /// engine already had to know.
    ///
    /// WHY THE LAYER LIVES HERE AND NOT IN CODE
    /// A layer index is not a stable handle — reordering TagManager.asset would
    /// repoint every surface in the game and nothing would fail. The mapping is
    /// authored, in one asset, and a layer no entry claims falls through to the
    /// fallback block below rather than producing nothing.
    /// </summary>
    [CreateAssetMenu(fileName = "Impact_", menuName = "CoD/Impact Config", order = 10)]
    public sealed class ImpactConfig : ScriptableObject
    {
        /// <summary>
        /// One surface's whole response: what it leaves behind, what it throws
        /// off, and what it sounds like.
        ///
        /// A class rather than a struct so the lookup can return a REFERENCE —
        /// the fire path resolves one of these per pellet per pull, and handing
        /// back a copy of the struct would put a dozen field copies on the stack
        /// for every trigger pull of a shotgun to no purpose. A null return then
        /// means "no entry claims this layer", which is a state a struct cannot
        /// express without a second flag.
        /// </summary>
        [System.Serializable]
        public sealed class SurfaceResponse
        {
            [Tooltip("Which surface this row describes. Labelling only — the LAYERS field below is what the lookup matches on.")]
            public SurfaceType surface = SurfaceType.Concrete;

            [Tooltip("Physics layers that ARE this surface. A mask rather than one layer, so a surface can span several without duplicating the row. A layer claimed by two rows belongs to the first.")]
            public LayerMask layers;

            [Tooltip("The bullet hole. Leave empty for a surface that cannot hold one — you cannot stamp a hole on a grate, and a decal on a drone is spawned into the WORLD, so it hangs in mid-air after the drone dies.")]
            public GameObject? decalPrefab;

            [Tooltip("The spray. Dust off concrete, sparks off plate, mist off flesh — this is most of what makes two surfaces read differently.")]
            public GameObject? particlePrefab;

            [Tooltip("The crack this surface makes. The whole reason this table exists: a wall and a hull must not sound the same.")]
            public AudioClip? impactSound;

            [Range(0f, 1f)] public float volume = 0.55f;
        }

        [Header("Per-surface response")]
        [Tooltip("Scanned in order; the first row whose layer mask contains the hit layer wins. Everything else falls through to the block below.")]
        public SurfaceResponse[] surfaces = System.Array.Empty<SurfaceResponse>();

        [Header("Fallback — the response for a layer no row claims")]
        [Tooltip("Deliberately the same as Concrete. An unmapped layer must still spark: a silent, invisible impact reads as a missed shot, which is a worse bug than the wrong dust colour.")]
        public GameObject? decalPrefab;
        public GameObject? particlePrefab;
        public AudioClip? impactSound;
        [Range(0f, 1f)] public float impactVolume = 0.55f;

        [Header("Budget — shared by every surface, on purpose")]
        [Tooltip("A decal lives this long, and a rifle fires twelve rounds a second: this number IS the live-decal footprint on a 4 GB card, so it is one budget rather than a per-surface knob.")]
        [Range(0.5f, 60f)] public float decalLifetime = 20f;
        [Range(0.1f, 10f)] public float particleLifetime = 2f;
        [Tooltip("Lifts the decal off the surface so it does not z-fight with the wall it is on. Geometry, not a surface trait.")]
        [Range(0f, 0.05f)] public float surfaceOffset = 0.008f;

        /// <summary>
        /// The response for one impact, or null when nothing claims that layer
        /// and the caller should use the fallback block.
        ///
        /// Allocation-free: an int mask test over an array of at most a handful
        /// of rows, no LINQ, no enumerator, nothing new. It runs once per pellet
        /// per trigger pull, which for a twelve-pellet weapon at 700 RPM is 140
        /// calls a second, and the horde budget is 16 KB of managed allocation
        /// per FRAME with forty drones alive.
        /// </summary>
        /// <param name="onBody">
        /// True when the bullet hit something damageable, and the ONE thing the
        /// layer does not get to decide on its own.
        ///
        /// A body is never architecture. Everything in this arena — walls, cover,
        /// drone hulls — currently shares the Default layer, so a plain scan
        /// would answer "concrete" for a drone and put a puff of masonry dust
        /// off a machine. Rather than special-case drones, the rule is stated
        /// once and holds for whatever arrives next: a body that no row claims
        /// is METAL, because every body in this game is a machine. The day a
        /// human-shaped target exists it gets a flesh layer of its own, is
        /// claimed by the scan below, and this floor never fires for it — which
        /// is what makes gore level a data swap rather than a branch in the fire
        /// path.
        /// </param>
        public SurfaceResponse? ResponseFor(int layer, bool onBody)
        {
            // Layers are 0-31 by Unity's own definition, so the shift is always
            // in range; a hit reports the collider's layer, never an index we
            // invented.
            int bit = 1 << layer;
            for (int i = 0; i < surfaces.Length; i++)
            {
                SurfaceResponse? entry = surfaces[i];
                if (entry == null) continue;
                if ((entry.layers.value & bit) == 0) continue;
                // See the onBody note: the layer wins for everything except a
                // body that landed on the architecture row, which is what an
                // arena with one layer produces for every drone in the game.
                if (onBody && entry.surface == SurfaceType.Concrete) break;
                return entry;
            }

            return onBody ? Find(SurfaceType.Metal) : null;
        }

        /// <summary>The first row describing that surface, whatever layer it is keyed on. Null when the table has no such row.</summary>
        public SurfaceResponse? Find(SurfaceType surface)
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                SurfaceResponse? entry = surfaces[i];
                if (entry != null && entry.surface == surface) return entry;
            }
            return null;
        }
    }
}
