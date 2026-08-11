#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// The Shooter's bullet. Pooled, raycast-swept, and deliberately SLOW — about
    /// 18 m/s, which is fast enough to punish standing still and slow enough to
    /// sidestep once you have seen it leave the barrel. A hitscan enemy weapon
    /// would make incoming fire unavoidable and unreadable at the same time.
    ///
    /// It sweeps a ray between last frame's position and this frame's rather than
    /// relying on a collider, because a small fast object with a trigger tunnels
    /// straight through a wall at any reasonable physics step.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroneProjectile : MonoBehaviour
    {
        [SerializeField] private PooledObject? _pooled = null;

        private Transform? _transform;
        private ObjectPool? _pool;
        private Vector3 _velocity;
        private float _damage;
        private float _expiresAt;
        private LayerMask _hitMask;
        private bool _live;

        // Pre-sized: a wave of shooters puts a lot of these in the air. Sized for
        // a step segment crossing several drones AND the wall behind them — an
        // overflow that drops the wall lets the round leave the arena.
        private readonly RaycastHit[] _buffer = new RaycastHit[8];

        private void Awake()
        {
            _transform = transform;
            if (_pooled == null) TryGetComponent(out _pooled);
        }

        public void Launch(ObjectPool pool, Vector3 velocity, float damage, float lifetime, LayerMask hitMask)
        {
            _pool = pool;
            _velocity = velocity;
            _damage = damage;
            _hitMask = hitMask;
            _expiresAt = Time.time + lifetime;
            _live = true;

            // Face travel so the tracer reads as a direction, not a floating cube.
            if (_transform != null && velocity.sqrMagnitude > 0.0001f)
            {
                _transform.rotation = Quaternion.LookRotation(velocity);
            }
        }

        private void Update()
        {
            if (!_live || _transform == null) return;

            float deltaTime = Time.deltaTime;
            Vector3 origin = _transform.position;
            Vector3 step = _velocity * deltaTime;
            float distance = step.magnitude;

            if (distance > 0.0001f && TrySweep(origin, step / distance, distance)) return;

            _transform.position = origin + step;
            if (Time.time >= _expiresAt) Despawn();
        }

        /// <summary>
        /// Finds the nearest thing along this frame's step that actually STOPS the
        /// round, and resolves it. Returns false when nothing does, which is the
        /// caller's cue to advance and age the projectile.
        ///
        /// Drones are skipped inside the search rather than treated as a hit. A
        /// shooter's round passing through its own kind is the design — drones
        /// killing each other would hand the player free money and make a crowd
        /// fight itself — but "hit a drone" must never mean "stop": the sweep
        /// restarts from the same point every frame, so a round that neither
        /// moved nor despawned would hang in the air forever, holding its pooled
        /// instance for the rest of the run. In a wave of shooters firing across
        /// a crowd that leaks the projectile pool until it hits the leak warning.
        /// </summary>
        private bool TrySweep(Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _buffer, distance,
                _hitMask, QueryTriggerInteraction.Ignore);

            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                if (BelongsToADrone(_buffer[i].collider)) continue;
                if (nearest < 0 || _buffer[i].distance < _buffer[nearest].distance) nearest = i;
            }

            if (nearest < 0) return false;
            Resolve(_buffer[nearest]);
            return true;
        }

        /// <summary>
        /// True for EITHER of a drone's two colliders. The hull carries the
        /// DroneController and the Health; the small `Core` child carries only a
        /// Weakpoint that relays to it. Testing the collider alone recognised the
        /// hull and not the Core, so a round that clipped another drone's core hit
        /// something with no Health, found nothing to damage, and vanished — the
        /// shooter's own kind acting as cover for the player.
        /// </summary>
        private static bool BelongsToADrone(Collider collider)
        {
            Health? owner = HealthBehind(collider);
            return owner != null && owner.TryGetComponent(out DroneController _);
        }

        /// <summary>The Health a collider speaks for: its own, or the one its Weakpoint points at.</summary>
        private static Health? HealthBehind(Collider collider)
        {
            if (collider.TryGetComponent(out Weakpoint weakpoint)) return weakpoint.Owner;
            return collider.TryGetComponent(out Health direct) ? direct : null;
        }

        private void Resolve(in RaycastHit hit)
        {
            Health? health = HealthBehind(hit.collider);
            if (health != null && health.IsAlive)
            {
                Vector3 direction = _velocity.sqrMagnitude > 0.0001f ? _velocity.normalized : Vector3.forward;
                var info = new DamageInfo(_damage, hit.point, hit.normal, direction, false);
                health.ApplyDamage(in info);
            }

            Despawn();
        }

        private void Despawn()
        {
            _live = false;
            if (_pool != null && _pooled != null) _pool.Despawn(_pooled);
            else gameObject.SetActive(false);
        }

        private void OnDisable() => _live = false;
    }
}
