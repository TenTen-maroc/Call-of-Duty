#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Every spawn in this game goes through here — bullets, casings, impact VFX,
    /// damage numbers, drones. In a horde game with 40 enemies and hundreds of
    /// projectiles, Instantiate/Destroy per frame is the GC-hitch factory: the
    /// collector runs mid-firefight and the frame time spikes exactly when the
    /// player is under pressure.
    ///
    /// Scene object, not a singleton — Domain Reload is disabled, so a static
    /// instance would survive between Play sessions pointing at a destroyed
    /// object. Consumers serialize a reference to this instead.
    /// </summary>
    public sealed class ObjectPool : MonoBehaviour
    {
        [System.Serializable]
        public struct PrewarmEntry
        {
            public GameObject? prefab;
            [Min(0)] public int count;
        }

        [Tooltip("Instances created during Awake, so the first shot of the game does not allocate.")]
        [SerializeField] private PrewarmEntry[] _prewarm = System.Array.Empty<PrewarmEntry>();

        [Tooltip("Safety net: a pool that grows past this has a leak — something is spawning and never despawning.")]
        [SerializeField] private int _leakWarningThreshold = 512;

        private readonly Dictionary<GameObject, Stack<PooledObject>> _available = new();
        private readonly Dictionary<GameObject, int> _liveCount = new();
        private readonly List<TimedDespawn> _timed = new(64);
        private Transform? _root;

        private struct TimedDespawn
        {
            public PooledObject Instance;
            public float DespawnAt;
            public uint Generation;
        }

        private void Awake()
        {
            _root = transform;
            for (int i = 0; i < _prewarm.Length; i++)
            {
                GameObject? prefab = _prewarm[i].prefab;
                if (prefab == null) continue;
                for (int n = 0; n < _prewarm[i].count; n++) Stock(prefab);
            }
        }

        private void Update()
        {
            // Iterates backwards so a removal does not skip the next entry.
            float now = Time.time;
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (now < _timed[i].DespawnAt) continue;
                PooledObject instance = _timed[i].Instance;
                uint generation = _timed[i].Generation;
                _timed.RemoveAt(i);
                // Generation check: if something manually despawned (or despawned
                // and re-spawned) this instance, the timer is stale and must not
                // fire — otherwise it kills an unrelated later use of the object.
                if (instance != null && instance.IsSpawned && instance.SpawnGeneration == generation)
                {
                    Despawn(instance);
                }
            }
        }

        /// <summary>Takes an instance from the pool, creating one if the stack is empty.</summary>
        public PooledObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!_available.TryGetValue(prefab, out Stack<PooledObject> stack))
            {
                stack = new Stack<PooledObject>();
                _available[prefab] = stack;
            }

            // Pop past instances something external destroyed (a scene change, a
            // stray Destroy) rather than crashing on the first dead entry.
            PooledObject? instance = null;
            while (stack.Count > 0)
            {
                instance = stack.Pop();
                if (instance != null) break;
                instance = null;
            }

            if (instance == null)
            {
                instance = Create(prefab);
                int live = _liveCount.TryGetValue(prefab, out int existing) ? existing + 1 : 1;
                _liveCount[prefab] = live;
                if (live == _leakWarningThreshold)
                {
                    GameLog.Warn(
                        $"ObjectPool grew to {live} instances of '{prefab.name}' — that is a leak, " +
                        "something spawns it and never despawns it.", this);
                }
            }

            Transform t = instance.CachedTransform;
            t.SetPositionAndRotation(position, rotation);
            instance.MarkSpawned();
            instance.gameObject.SetActive(true);
            return instance;
        }

        /// <summary>Spawns, then returns it automatically after `lifetime` seconds.</summary>
        public PooledObject SpawnForSeconds(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            PooledObject instance = Spawn(prefab, position, rotation);
            _timed.Add(new TimedDespawn
            {
                Instance = instance,
                DespawnAt = Time.time + lifetime,
                Generation = instance.SpawnGeneration,
            });
            return instance;
        }

        public void Despawn(PooledObject instance)
        {
            GameObject? prefab = instance.SourcePrefab;
            if (prefab == null)
            {
                GameLog.Error($"'{instance.name}' was despawned but never came from the pool.", instance);
                return;
            }

            // A double despawn would push the same instance onto the stack twice,
            // and the pool would later hand one object to two callers at once.
            if (!instance.IsSpawned) return;
            instance.MarkDespawned();

            instance.gameObject.SetActive(false);
            instance.CachedTransform.SetParent(_root, false);

            if (!_available.TryGetValue(prefab, out Stack<PooledObject> stack))
            {
                stack = new Stack<PooledObject>();
                _available[prefab] = stack;
            }
            stack.Push(instance);
        }

        private void Stock(GameObject prefab)
        {
            if (!_available.TryGetValue(prefab, out Stack<PooledObject> stack))
            {
                stack = new Stack<PooledObject>();
                _available[prefab] = stack;
            }
            PooledObject instance = Create(prefab);
            instance.gameObject.SetActive(false);
            stack.Push(instance);
            _liveCount[prefab] = _liveCount.TryGetValue(prefab, out int existing) ? existing + 1 : 1;
        }

        private PooledObject Create(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, _root);
            PooledObject instance = go.GetComponent<PooledObject>();
            if (instance == null) instance = go.AddComponent<PooledObject>();
            instance.BindTo(prefab);
            return instance;
        }
    }
}
