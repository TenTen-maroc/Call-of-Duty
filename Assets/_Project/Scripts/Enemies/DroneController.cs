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
    public sealed class DroneController : MonoBehaviour, IFactionMember
    {
        /// <summary>
        /// Every body this controller drives is hostile — the drones today and
        /// the Meridian soldiers that will share it, which is exactly why the
        /// answer lives here rather than on a per-archetype config.
        ///
        /// What reads it is <see cref="Projectile"/>: an enemy round must pass
        /// THROUGH other enemies rather than stop on them. It used to ask for this
        /// component by type, which stopped being possible when the projectile
        /// moved into CoD.Core — see <see cref="IFactionMember"/>.
        /// </summary>
        public Faction Faction => Faction.Hostile;

        [SerializeField] private NavMeshAgent? _agent = null;
        [Tooltip("Present on a rigged humanoid, null on a drone. Every call site is null-checked, so a cube pays nothing for it.")]
        [SerializeField] private EnemyAnimator? _animator = null;
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

        // Shader property ids, resolved once. Shader.PropertyToID does a string
        // hash every call, and the telegraph is written every frame of a windup
        // by every drone in the wave. static readonly, so the no-mutable-statics
        // rule is untouched.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Only used before Initialize supplies a config. The real values live on DroneConfig.</summary>
        private static readonly Color DefaultIdleCore = new(0.75f, 0.12f, 0.10f);
        private static readonly Color DefaultTelegraphCore = new(1f, 0.95f, 0.75f);

        private DroneAttackState _attack;
        private float _nextRepathAt;
        private float _speedMultiplier = 1f;
        private float _waveSpeedMultiplier = 1f;
        private bool _active;

        /// <summary>
        /// Pre-sized and owned HERE rather than by the attack module: modules are
        /// shared assets, and a buffer living on one would be written by every
        /// drone using it at the same time.
        /// </summary>
        /// <remarks>
        /// Sized well past the alive cap's worth of nearby colliders. OverlapSphere
        /// fills a full buffer with an ARBITRARY subset and reports no overflow, so
        /// a blast in a dense pack could come back holding only drones and miss the
        /// player entirely — the attack silently doing nothing, which reads as the
        /// enemy being broken rather than as a near miss. Each drone contributes
        /// two colliders (hull + Core), so 16 covered barely eight bodies.
        ///
        /// 64 was still not enough, and the horde-load test caught it: the query
        /// is not layer-filtered down to things carrying Health, so a blast near
        /// the ground also collects the floor, the walls and every cover box it
        /// reaches. Under a full arena that overflows, and the truncation warning
        /// fired for real. Raised rather than mask-tightened because the mask
        /// comes from the attack configs and narrowing it would be a silent
        /// behaviour change; a bigger buffer can only make the result MORE
        /// complete. 256 colliders is 2 KB per drone, ~82 KB across the alive cap.
        /// </remarks>
        private readonly Collider[] _overlapBuffer = new Collider[256];

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
            => Initialize(config, target, pool, registry, tokens, WaveScaling.None);

        /// <summary>
        /// Spawn-time setup with the wave's difficulty multipliers folded in. The
        /// multipliers are applied HERE, to the instance, and never written back
        /// to the config — that is the whole reason this parameter exists.
        /// </summary>
        public void Initialize(DroneConfig config, Transform target, ObjectPool pool,
            DroneRegistry registry, IAttackTokenSource tokens, WaveScaling scaling)
        {
            _config = config;
            _target = target;
            _pool = pool;
            _registry = registry;
            _tokens = tokens;
            _attack = default;
            _speedMultiplier = 1f;
            _waveSpeedMultiplier = 1f;
            _nextRepathAt = 0f;
            SetTelegraph(0f);
            // Pooled objects are REUSED. A soldier respawning part-way through
            // its own death animation is the same class of bug the pool's
            // generation counter exists to prevent elsewhere.
            _animator?.ResetForReuse();

            // HP comes from the drone's own config, not a shared HealthConfig —
            // one source of truth per archetype.
            if (_health != null) _health.ConfigureMax(config.maxHealth * scaling.HealthMultiplier);
            _waveSpeedMultiplier = scaling.SpeedMultiplier;

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

            _agent.speed = _config.moveSpeed * _speedMultiplier * _waveSpeedMultiplier;

            // Fed from the agent's REALISED velocity, not from its target speed.
            // A soldier stopped dead against a wall, or zeroed by an attack
            // module for a stop-to-shoot, must read as standing still — driving
            // the blend from config.moveSpeed would leave it running on the spot.
            if (_animator != null)
            {
                Vector3 velocity = _agent.velocity;
                velocity.y = 0f;
                _animator.SetSpeed(velocity.magnitude);
            }

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
            // Cancel FIRST, like Retire does. Resetting the two state fields left
            // whatever the windup had started still running on the drone: the
            // Rusher kept its 1.35x lunge speed and both archetypes kept the
            // bright telegraph tint, permanently. A drone stuck behind cover long
            // enough to lose its token then chased the player for the rest of the
            // wave looking like it was about to detonate, and moving as if it had.
            if (_config != null && _config.attack != null) _config.attack.Cancel(this, ref _attack);
            _attack.HasToken = false;
            _attack.Phase = DroneAttackPhase.Idle;
        }

        /// <summary>0 = normal, 1 = about to go off. Drives the windup tint.</summary>
        public void SetTelegraph(float amount)
        {
            // The pose is the SAME contract on a second channel, and it is the
            // one that survives having no glowing core. Set before the early
            // return below, because a humanoid prefab has no core renderer at
            // all and would otherwise be silently un-telegraphed -- which is the
            // fairness contract failing in the exact case it was extended for.
            _animator?.SetTelegraph(amount);

            if (_coreRenderer == null || _propertyBlock == null) return;
            // MaterialPropertyBlock rather than renderer.material: touching
            // .material clones it per drone, which is forty extra materials and
            // forty broken batches in a full wave.
            //
            // The two ends of the ramp come from the ARCHETYPE, not from this
            // file. They used to be literals here, and Initialize calls this with
            // 0 on every spawn — so the first thing every drone did was overwrite
            // its authored core colour with the Rusher's red, and a Shooter, a
            // Tank and a Rusher were indistinguishable at a glance in the one
            // place the player has to tell them apart instantly.
            float t = Mathf.Clamp01(amount);
            Color idle = _config != null ? _config.idleCoreColor : DefaultIdleCore;
            Color hot = _config != null ? _config.telegraphCoreColor : DefaultTelegraphCore;
            float idleGlow = _config != null ? _config.idleEmission : 0.4f;
            float hotGlow = _config != null ? _config.telegraphEmission : 3.9f;

            Color color = Color.Lerp(idle, hot, t);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(EmissionColorId, color * Mathf.Lerp(idleGlow, hotGlow, t));
            _coreRenderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// The windup is over and the attack is committed. Called by the attack
        /// modules at the moment they act, so the pose and the damage are the
        /// same beat rather than two things that drift apart.
        /// </summary>
        public void PlayAttackAnimation() => _animator?.PlayAttack();

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
            _animator?.PlayDeath();
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

            if (raiseDied)
            {
                Died?.Invoke(this, info);
                // The registry is the hub the wave runner listens to, so score and
                // money are paid for EVERY kill — including drones the sandbox
                // console spawned, which the runner never saw being created.
                _registry?.NotifyKilled(this, info);
            }
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
