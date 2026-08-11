#nullable enable
using System;
using CoD.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CoD.Enemies
{
    /// <summary>
    /// ONE controller for every drone in the game. It reads a DroneConfig for its
    /// numbers and ticks an AttackModule for its behaviour, so a new archetype is
    /// two assets and no new code.
    ///
    /// Pooled, like everything that spawns. The NavMeshAgent is why this class has
    /// an explicit lifecycle instead of just OnEnable: an agent enabled while its
    /// object sits off the navmesh throws "SetDestination can only be called on an
    /// active agent that has been placed on a NavMesh", and a pooled agent that
    /// kept its old path would walk the new drone to the dead one's destination.
    /// The prefab therefore ships with the agent DISABLED, and Initialize enables
    /// it, warps it onto the mesh, and clears the path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroneController : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent? _agent = null;
        [SerializeField] private Health? _health = null;
        [SerializeField] private PooledObject? _pooled = null;
        [Tooltip("Plays the attack telegraph cue. Audible because the drone is still alive during a windup.")]
        [SerializeField] private AudioSource? _audio = null;
        [Tooltip("Tinted through the attack windup. The telegraph is what makes a contact detonation fair instead of a coin flip.")]
        [SerializeField] private Renderer? _coreRenderer = null;

        private DroneConfig? _config;
        private Transform? _target;
        private ObjectPool? _pool;
        private DroneRegistry? _registry;
        private IAttackTokenSource? _tokens;
        private Transform? _transform;
        private MaterialPropertyBlock? _propertyBlock;

        private DroneAttackState _attack;
        private float _nextRepathAt;
        private float _speedMultiplier = 1f;
        private bool _active;

        /// <summary>
        /// Pre-sized and owned HERE rather than by the attack module: modules are
        /// shared assets, and a buffer living on one would be written by every
        /// drone using it at the same time.
        /// </summary>
        private readonly Collider[] _overlapBuffer = new Collider[16];

        // Instance events, never static — Domain Reload is off, and a static event
        // would still be holding the previous Play session's subscribers.
        /// <summary>Shot down. This is the one that pays score and money.</summary>
        public event Action<DroneController, DamageInfo>? Died;
        /// <summary>Left play for any reason at all, including its own detonation.</summary>
        public event Action<DroneController>? Despawned;

        public DroneConfig? Config => _config;
        public Transform? Target => _target;
        public ObjectPool? Pool => _pool;
        public Health? HealthComponent => _health;
        public Collider[] OverlapBuffer => _overlapBuffer;
        public bool IsActive => _active;
        public bool HasAttackToken => _attack.HasToken;
        public Vector3 Position => _transform != null ? _transform.position : transform.position;

        private void Awake()
        {
            _transform = transform;
            if (_agent == null) TryGetComponent(out _agent);
            if (_health == null) TryGetComponent(out _health);
            if (_pooled == null) TryGetComponent(out _pooled);
            _propertyBlock = new MaterialPropertyBlock();
            if (_agent != null) _agent.enabled = false;
        }

        private void OnEnable()
        {
            if (_health != null) _health.Died += OnHealthDied;
        }

        private void OnDisable()
        {
            if (_health != null) _health.Died -= OnHealthDied;
            // Something deactivated us without going through Retire (scene unload,
            // a stray SetActive). Give the token back anyway: a leaked token
            // shrinks the attack pool for the rest of the run.
            if (_active) Retire(raiseDied: false, default);
        }

        /// <summary>Called by the spawner immediately after the pool hands the instance over.</summary>
        public void Initialize(DroneConfig config, Transform target, ObjectPool pool,
            DroneRegistry registry, IAttackTokenSource tokens)
        {
            _config = config;
            _target = target;
            _pool = pool;
            _registry = registry;
            _tokens = tokens;
            _attack = default;
            _speedMultiplier = 1f;
            _nextRepathAt = 0f;
            SetTelegraph(0f);

            // HP comes from the drone's own config, not a shared HealthConfig —
            // one source of truth per archetype.
            if (_health != null) _health.ConfigureMax(config.maxHealth);

            if (_agent != null)
            {
                _agent.speed = config.moveSpeed;
                _agent.acceleration = config.acceleration;
                _agent.angularSpeed = config.turnSpeed;
                _agent.baseOffset = config.hoverHeight;
                _agent.stoppingDistance = config.stopDistance;
                _agent.autoBraking = false;   // a braking horde dithers on approach

                _agent.enabled = true;
                _agent.Warp(Position);
                if (_agent.isOnNavMesh) _agent.ResetPath();
            }

            _active = true;
            registry.Register(this);
        }

        private void Update()
        {
            if (!_active || _config == null) return;

            float now = Time.time;
            Steer(now);

            AttackModule? attack = _config.attack;
            if (attack != null) attack.Tick(this, ref _attack, now, Time.deltaTime);
        }

        private void Steer(float now)
        {
            if (_agent == null || _target == null || _config == null) return;
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            _agent.speed = _config.moveSpeed * _speedMultiplier;
            if (now < _nextRepathAt) return;
            _nextRepathAt = now + _config.repathInterval;

            Vector3 targetPosition = _target.position;
            if (_config.preferredRange > 0.01f)
            {
                // Kiting is DATA. A drone with a preferredRange holds a ring around
                // the player instead of closing; same controller, same module
                // contract, one different number.
                Vector3 away = Position - targetPosition;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                targetPosition += away.normalized * _config.preferredRange;
            }

            _agent.SetDestination(targetPosition);
        }

        /// <summary>Distance to the player, squared — the hot path never calls Sqrt.</summary>
        public float SqrDistanceToTarget()
        {
            if (_target == null) return float.MaxValue;
            return (_target.position - Position).sqrMagnitude;
        }

        /// <summary>Attack modules speed the drone up for a committed lunge. Initialize resets it on the next spawn, so nothing has to undo it.</summary>
        public void SetSpeedMultiplier(float multiplier) => _speedMultiplier = Mathf.Max(0f, multiplier);

        public bool TryAcquireAttackToken(ref DroneAttackState state)
        {
            if (state.HasToken) return true;
            if (_tokens == null) return true;
            if (!_tokens.TryAcquire(this)) return false;
            state.HasToken = true;
            return true;
        }

        public void ReleaseAttackToken(ref DroneAttackState state)
        {
            if (!state.HasToken) return;
            state.HasToken = false;
            _tokens?.Release(this);
        }

        /// <summary>Called by the token pool when a drone has held a token too long — one stuck drone must not starve the pack.</summary>
        public void ForceReleaseAttackToken()
        {
            _attack.HasToken = false;
            _attack.Phase = DroneAttackPhase.Idle;
        }

        /// <summary>0 = normal, 1 = about to go off. Drives the windup tint.</summary>
        public void SetTelegraph(float amount)
        {
            if (_coreRenderer == null || _propertyBlock == null) return;
            // MaterialPropertyBlock rather than renderer.material: touching
            // .material clones it per drone, which is forty extra materials and
            // forty broken batches in a full wave.
            float t = Mathf.Clamp01(amount);
            Color color = Color.Lerp(new Color(0.75f, 0.12f, 0.10f), new Color(1f, 0.95f, 0.75f), t);
            _propertyBlock.SetColor("_BaseColor", color);
            _propertyBlock.SetColor("_EmissionColor", color * (0.4f + 3.5f * t));
            _coreRenderer.SetPropertyBlock(_propertyBlock);
        }

        public void PlayCue(AudioClip? clip)
        {
            if (clip == null || _audio == null) return;
            _audio.PlayOneShot(clip);
        }

        /// <summary>
        /// The drone removed itself (a contact detonation). Deliberately does NOT
        /// raise Died: the player did not kill it, so it must not pay score or
        /// money — otherwise suiciding into a wall is an income stream.
        /// </summary>
        public void SelfDestruct() => Retire(raiseDied: false, default);

        /// <summary>Wave cleanup and the sandbox "kill all" cheat.</summary>
        public void DespawnNow() => Retire(raiseDied: false, default);

        private void OnHealthDied(Health health, DamageInfo info)
        {
            if (!_active) return;
            if (_config != null && _config.deathVfx != null && _pool != null)
            {
                _pool.SpawnForSeconds(_config.deathVfx, Position, Quaternion.identity, _config.deathVfxLifetime);
            }
            Retire(raiseDied: true, info);
        }

        /// <summary>
        /// The single exit path. Every way a drone can leave play funnels through
        /// here, so the token, the registry entry and the pool slot are each
        /// released exactly once.
        /// </summary>
        private void Retire(bool raiseDied, in DamageInfo info)
        {
            if (!_active) return;
            _active = false;

            if (_config != null && _config.attack != null) _config.attack.Cancel(this, ref _attack);
            ReleaseAttackToken(ref _attack);
            _registry?.Unregister(this);

            if (raiseDied) Died?.Invoke(this, info);
            Despawned?.Invoke(this);

            // Clear the subscriber lists. The instance goes back to the pool, and
            // the next user of it must not inherit this wave's listeners.
            Died = null;
            Despawned = null;

            if (_agent != null && _agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.ResetPath();
                _agent.enabled = false;
            }

            if (_pool != null && _pooled != null) _pool.Despawn(_pooled);
            else gameObject.SetActive(false);
        }
    }
}
