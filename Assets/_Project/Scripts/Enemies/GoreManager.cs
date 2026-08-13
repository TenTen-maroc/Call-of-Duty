#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// One scene-owned budget for blood, wounds, corpses, ragdolls, and parts.
    /// Fixed rings make every cap oldest-first and allocation-free during play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GoreManager : MonoBehaviour
    {
        [SerializeField] private GoreProfile? _profile = null;
        [SerializeField] private ObjectPool? _pool = null;
        [SerializeField] private SettingsHub? _settings = null;

        private PooledObject?[] _decals = System.Array.Empty<PooledObject?>();
        private PooledObject?[] _wounds = System.Array.Empty<PooledObject?>();
        private PooledObject?[] _pools = System.Array.Empty<PooledObject?>();
        private PooledObject?[] _parts = System.Array.Empty<PooledObject?>();
        private HumanEnemyPresentation?[] _corpses = System.Array.Empty<HumanEnemyPresentation?>();
        private HumanEnemyPresentation?[] _ragdolls = System.Array.Empty<HumanEnemyPresentation?>();
        private PendingPool[] _pendingPools = System.Array.Empty<PendingPool>();
        private int _decalCursor;
        private int _woundCursor;
        private int _poolCursor;
        private int _partCursor;
        private int _corpseCursor;
        private int _ragdollCursor;
        private readonly RaycastHit[] _surfaceHits = new RaycastHit[4];

        private struct PendingPool
        {
            public HumanEnemyPresentation? Owner;
            public Vector3 Position;
            public float SpawnAt;
        }

        public GoreLevel CurrentLevel { get; private set; } = GoreLevel.Extreme;
        public int ActiveBloodEffects => Count(_decals) + Count(_wounds) + Count(_pools) + Count(_parts);

        public void Release(PooledObject effect)
        {
            if (_pool != null && effect.IsSpawned) _pool.Despawn(effect);
        }

        private void Awake()
        {
            int decalCap = _profile != null ? _profile.bloodDecalCap : 96;
            int woundCap = _profile != null ? _profile.woundCap : 24;
            int poolCap = _profile != null ? _profile.bloodPoolCap : 12;
            int partCap = _profile != null ? _profile.severedPartCap : 24;
            int corpseCap = _profile != null ? _profile.corpseCap : 8;
            int ragdollCap = _profile != null ? _profile.ragdollCap : 4;
            _decals = new PooledObject?[Mathf.Max(1, decalCap)];
            _wounds = new PooledObject?[Mathf.Max(1, woundCap)];
            _pools = new PooledObject?[Mathf.Max(1, poolCap)];
            _parts = new PooledObject?[Mathf.Max(1, partCap)];
            _corpses = new HumanEnemyPresentation?[Mathf.Max(1, corpseCap)];
            _ragdolls = new HumanEnemyPresentation?[Mathf.Max(1, ragdollCap)];
            _pendingPools = new PendingPool[Mathf.Max(1, poolCap)];

            if (_settings != null)
            {
                CurrentLevel = _settings.Current.Gore;
                _settings.Changed += OnSettingsChanged;
            }
        }

        private void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
        }

        private void Update()
        {
            if (CurrentLevel != GoreLevel.Extreme || _profile == null || _pool == null) return;
            float now = Time.time;
            for (int i = 0; i < _pendingPools.Length; i++)
            {
                PendingPool pending = _pendingPools[i];
                if (pending.Owner == null || now < pending.SpawnAt) continue;
                if (_profile.bloodPoolPrefab != null)
                {
                    SpawnCapped(_profile.bloodPoolPrefab, pending.Position,
                        Quaternion.Euler(90f, 0f, 0f), _pools, ref _poolCursor, _profile.poolLifetime);
                }
                _pendingPools[i] = default;
            }
        }

        private void OnSettingsChanged(GameSettings settings)
        {
            GoreLevel previous = CurrentLevel;
            CurrentLevel = settings.Gore;
            if (previous == CurrentLevel) return;
            System.Array.Clear(_pendingPools, 0, _pendingPools.Length);
            ClearBloodPresentation();
            for (int i = 0; i < _corpses.Length; i++) _corpses[i]?.RemoveGorePresentation();
        }

        public void PresentHit(HumanEnemyPresentation owner, in DamageInfo info, Transform? regionAnchor)
        {
            if (!GoreRules.AllowsBlood(CurrentLevel, info.Region) || _profile == null || _pool == null)
                return;

            if (_profile.bloodSprayPrefab != null)
            {
                Vector3 direction = info.Direction.sqrMagnitude > 0.001f ? info.Direction.normalized : Vector3.forward;
                _pool.SpawnForSeconds(_profile.bloodSprayPrefab, info.Point,
                    Quaternion.LookRotation(direction), _profile.sprayLifetime);
            }

            TrySpawnSurfaceDecal(in info);
            if (CurrentLevel != GoreLevel.Extreme || _profile.woundPrefab == null) return;

            PooledObject wound = SpawnCapped(_profile.woundPrefab, info.Point,
                Quaternion.LookRotation(info.Normal.sqrMagnitude > 0.001f ? info.Normal : Vector3.up),
                _wounds, ref _woundCursor, _profile.woundLifetime);
            if (regionAnchor != null) wound.CachedTransform.SetParent(regionAnchor, worldPositionStays: true);
            owner.TrackAttachedEffect(wound);
        }

        public void BeginDeath(HumanEnemyPresentation owner, in DamageInfo info)
        {
            bool ragdoll = info.Kind == DamageKind.Explosive;
            RegisterCorpse(owner, ragdoll);

            bool dismember = CurrentLevel == GoreLevel.Extreme && ShouldDismember(in info);
            owner.ApplyDeathPresentation(in info, ragdoll, dismember,
                _profile != null ? _profile.explosiveImpulse : 0f);

            if (CurrentLevel != GoreLevel.Extreme || _profile == null || _pool == null) return;
            QueueBloodPool(owner);

            if (!dismember) return;
            if (info.Kind == DamageKind.Explosive && !GoreRules.IsDismemberable(info.Region))
            {
                PresentDismemberment(owner, HitRegion.LeftArm, in info);
                PresentDismemberment(owner, HitRegion.RightLeg, in info);
                return;
            }
            PresentDismemberment(owner, info.Region, in info);
        }

        private void PresentDismemberment(HumanEnemyPresentation owner, HitRegion region, in DamageInfo info)
        {
            if (_profile == null || _pool == null || !GoreRules.IsDismemberable(region)) return;
            owner.HideRegionForGore(region);
            Transform? anchor = owner.RegionAnchor(region);
            Vector3 position = anchor != null ? anchor.position : info.Point;
            if (_profile.stumpPrefab != null)
            {
                PooledObject stump = SpawnCapped(_profile.stumpPrefab, position, Quaternion.identity,
                    _wounds, ref _woundCursor, _profile.woundLifetime);
                owner.TrackAttachedEffect(stump);
            }
            if (_profile.severedPartPrefab != null)
            {
                SpawnCapped(_profile.severedPartPrefab, position, Quaternion.identity,
                    _parts, ref _partCursor, _profile.severedPartLifetime);
            }
        }

        private void QueueBloodPool(HumanEnemyPresentation owner)
        {
            int slot = -1;
            for (int i = 0; i < _pendingPools.Length; i++)
            {
                if (_pendingPools[i].Owner == null)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) slot = _poolCursor % _pendingPools.Length;
            _pendingPools[slot] = new PendingPool
            {
                Owner = owner,
                Position = owner.Position + Vector3.up * _profile!.surfaceOffset,
                SpawnAt = Time.time + _profile.poolDelay,
            };
        }

        private bool ShouldDismember(in DamageInfo info)
        {
            if (_profile == null) return false;
            return GoreRules.ShouldDismember(CurrentLevel, info.Kind, info.Region, info.Amount,
                _profile.headDismemberDamage, _profile.limbDismemberDamage);
        }

        private void RegisterCorpse(HumanEnemyPresentation owner, bool ragdoll)
        {
            HumanEnemyPresentation? previous = _corpses[_corpseCursor];
            if (previous != null && previous != owner) previous.ForceRecycle();
            _corpses[_corpseCursor] = owner;
            _corpseCursor = (_corpseCursor + 1) % _corpses.Length;

            if (!ragdoll) return;
            HumanEnemyPresentation? oldRagdoll = _ragdolls[_ragdollCursor];
            if (oldRagdoll != null && oldRagdoll != owner) oldRagdoll.EndRagdollEarly();
            _ragdolls[_ragdollCursor] = owner;
            _ragdollCursor = (_ragdollCursor + 1) % _ragdolls.Length;
        }

        private void TrySpawnSurfaceDecal(in DamageInfo info)
        {
            if (_profile == null || _pool == null || _profile.bloodDecalPrefab == null) return;
            Vector3 direction = info.Direction.sqrMagnitude > 0.001f ? info.Direction.normalized : Vector3.down;
            int count = Physics.RaycastNonAlloc(info.Point, direction, _surfaceHits,
                _profile.surfaceProjectionDistance, _profile.worldMask, QueryTriggerInteraction.Ignore);
            if (count == 0)
            {
                count = Physics.RaycastNonAlloc(info.Point, Vector3.down, _surfaceHits,
                    _profile.surfaceProjectionDistance, _profile.worldMask, QueryTriggerInteraction.Ignore);
            }
            if (count == 0) return;

            RaycastHit nearest = _surfaceHits[0];
            for (int i = 1; i < count; i++)
            {
                if (_surfaceHits[i].distance < nearest.distance) nearest = _surfaceHits[i];
            }
            Vector3 point = nearest.point + nearest.normal * _profile.surfaceOffset;
            SpawnCapped(_profile.bloodDecalPrefab, point, Quaternion.LookRotation(nearest.normal),
                _decals, ref _decalCursor, _profile.decalLifetime);
        }

        private PooledObject SpawnCapped(GameObject prefab, Vector3 position, Quaternion rotation,
            PooledObject?[] ring, ref int cursor, float lifetime)
        {
            PooledObject? previous = ring[cursor];
            if (previous != null && previous.IsSpawned) _pool?.Despawn(previous);
            PooledObject spawned = _pool!.SpawnForSeconds(prefab, position, rotation, lifetime);
            ring[cursor] = spawned;
            cursor = (cursor + 1) % ring.Length;
            return spawned;
        }

        private void ClearBloodPresentation()
        {
            Clear(_decals);
            Clear(_wounds);
            Clear(_pools);
            Clear(_parts);
        }

        private void Clear(PooledObject?[] ring)
        {
            if (_pool == null) return;
            for (int i = 0; i < ring.Length; i++)
            {
                PooledObject? item = ring[i];
                if (item != null && item.IsSpawned) _pool.Despawn(item);
                ring[i] = null;
            }
        }

        private static int Count(PooledObject?[] ring)
        {
            int count = 0;
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i] != null && ring[i]!.IsSpawned) count++;
            }
            return count;
        }

#if UNITY_EDITOR
        public void Configure(GoreProfile profile, ObjectPool pool, SettingsHub settings)
        {
            _profile = profile;
            _pool = pool;
            _settings = settings;
        }
#endif
    }
}
