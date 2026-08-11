#nullable enable
using System;
using CoD.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CoD.Enemies
{
    /// <summary>
    /// Turns "spawn three Rushers" into pooled, navmesh-placed, initialised
    /// drones. The wave runner drives it later; the cheat console drives it now.
    ///
    /// Two rules it enforces on every spawn, both from DifficultyConfig: never
    /// exceed the alive cap (which protects a 4 GB GPU), and never place a drone
    /// closer than the minimum distance — a drone that materialises inside the
    /// player's personal space is a cheap death, not a challenge.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroneSpawner : MonoBehaviour
    {
        [SerializeField] private ObjectPool? _pool = null;
        [SerializeField] private DroneRegistry? _registry = null;
        [Tooltip("The player. Serialized rather than found: no scene searches, ever.")]
        [SerializeField] private Transform? _target = null;
        [SerializeField] private DifficultyConfig? _difficulty = null;
        [Tooltip("Used by the sandbox console and as the fallback when a wave entry has no config.")]
        [SerializeField] private DroneConfig? _defaultDrone = null;
        [SerializeField] private Transform[] _spawnPoints = Array.Empty<Transform>();

        // Instance field, not static: the wave runner swaps in the real token pool
        // at run start, and the sandbox can swap it back out.
        private IAttackTokenSource _tokens = new UnlimitedAttackTokens();
        private int _aliveCapOverride;
        private bool _warnedMissingDifficulty;

        /// <summary>
        /// Used only when no DifficultyConfig is assigned. Not a tuning value — a
        /// last-resort brake so a wiring mistake cannot uncap spawning on a 4 GB
        /// GPU. The real number lives in DifficultyConfig.maxAliveDrones.
        /// </summary>
        private const int FALLBACK_ALIVE_CAP = 40;

        public DroneConfig? DefaultDrone => _defaultDrone;
        public DroneRegistry? Registry => _registry;
        public int AliveCount => _registry != null ? _registry.AliveCount : 0;

        public void SetTokenSource(IAttackTokenSource source) => _tokens = source;

        /// <summary>Per-wave alive cap from WaveConfig.maxAliveOverride. 0 means "use the DifficultyConfig cap".</summary>
        public void SetAliveCapOverride(int cap) => _aliveCapOverride = Mathf.Max(0, cap);

        /// <summary>
        /// The cap actually in force. Fails CLOSED when there is no DifficultyConfig:
        /// this number exists to protect a 4 GB GPU, and an unassigned reference
        /// used to remove it silently — CanSpawn simply returned true and the queue
        /// drained with no brake at all.
        /// </summary>
        public int AliveCap
        {
            get
            {
                if (_aliveCapOverride > 0) return _aliveCapOverride;
                if (_difficulty != null) return _difficulty.maxAliveDrones;
                return FALLBACK_ALIVE_CAP;
            }
        }

        /// <summary>True when the alive cap still has room.</summary>
        public bool CanSpawn()
        {
            if (_registry == null) return false;
            if (_difficulty == null && !_warnedMissingDifficulty)
            {
                _warnedMissingDifficulty = true;
                GameLog.Error(
                    $"DroneSpawner has no DifficultyConfig — falling back to a hard cap of {FALLBACK_ALIVE_CAP} " +
                    "alive drones. The VRAM cap and the three-attacker rule are both unconfigured.", this);
            }
            return _registry.AliveCount < AliveCap;
        }

        public int SpawnBurst(DroneConfig config, int count) => SpawnBurst(config, count, WaveScaling.None);

        public int SpawnBurst(DroneConfig config, int count, WaveScaling scaling)
        {
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (Spawn(config, scaling) == null) break;
                spawned++;
            }
            return spawned;
        }

        public DroneController? Spawn(DroneConfig config) => Spawn(config, WaveScaling.None);

        public DroneController? Spawn(DroneConfig config, WaveScaling scaling)
        {
            if (_pool == null || _registry == null || _target == null)
            {
                GameLog.Error("DroneSpawner is missing its pool, registry or target.", this);
                return null;
            }
            if (config.prefab == null)
            {
                GameLog.Error($"DroneConfig '{config.name}' has no prefab assigned.", this);
                return null;
            }
            if (!CanSpawn()) return null;
            if (!TryFindSpawnPosition(out Vector3 position)) return null;

            PooledObject instance = _pool.Spawn(config.prefab, position, Quaternion.identity);
            if (!instance.TryGetComponent(out DroneController drone))
            {
                GameLog.Error($"Prefab '{config.prefab.name}' has no DroneController.", this);
                _pool.Despawn(instance);
                return null;
            }

            drone.Initialize(config, _target, _pool, _registry, _tokens, scaling);
            return drone;
        }

        /// <summary>
        /// Picks a spawn point far enough from the player and snaps it to the
        /// navmesh. Starts at a random index so repeated bursts do not all pour
        /// out of the same corner, and falls back to the furthest point rather
        /// than refusing to spawn — a wave that silently stops is worse than one
        /// that spawns slightly close.
        /// </summary>
        private bool TryFindSpawnPosition(out Vector3 position)
        {
            position = Vector3.zero;
            if (_spawnPoints.Length == 0 || _target == null) return false;

            float minDistance = _difficulty != null ? _difficulty.minSpawnDistanceFromPlayer : 12f;
            float sampleRadius = _difficulty != null ? _difficulty.spawnSampleRadius : 4f;
            Vector3 playerPosition = _target.position;

            int start = UnityEngine.Random.Range(0, _spawnPoints.Length);
            Vector3 bestFallback = Vector3.zero;
            float bestDistance = -1f;
            bool haveFallback = false;

            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                Transform point = _spawnPoints[(start + i) % _spawnPoints.Length];
                if (point == null) continue;
                if (!NavMesh.SamplePosition(point.position, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas)) continue;

                float distance = Vector3.Distance(hit.position, playerPosition);
                if (distance >= minDistance)
                {
                    position = hit.position;
                    return true;
                }
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestFallback = hit.position;
                    haveFallback = true;
                }
            }

            if (!haveFallback) return false;
            position = bestFallback;
            return true;
        }
    }
}
