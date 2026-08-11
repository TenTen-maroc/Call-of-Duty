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

        // Pre-sized: a wave of shooters puts a lot of these in the air.
        private readonly RaycastHit[] _buffer = new RaycastHit[4];

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

            if (distance > 0.0001f)
            {
                int count = Physics.RaycastNonAlloc(origin, step / distance, _buffer, distance,
                    _hitMask, QueryTriggerInteraction.Ignore);
                if (count > 0)
                {
                    int nearest = 0;
                    for (int i = 1; i < count; i++)
                    {
                        if (_buffer[i].distance < _buffer[nearest].distance) nearest = i;
                    }
                    Resolve(_buffer[nearest]);
                    return;
                }
            }

            _transform.position = origin + step;
            if (Time.time >= _expiresAt) Despawn();
        }

        private void Resolve(in RaycastHit hit)
        {
            Collider collider = hit.collider;

            // A shooter's round passes through its own kind. Drones killing each
            // other would hand the player free money and make a crowd fight itself.
            if (collider.TryGetComponent(out DroneController _)) return;

            if (collider.TryGetComponent(out Health health) && health.IsAlive)
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
