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
        [Tooltip("Optional. The core collider, so this archetype's weakpointMultiplier reaches the instance at spawn.")]
        [SerializeField] private Weakpoint? _weakpoint = null;
        [SerializeField] private HumanEnemyPresentation? _human = null;

        private DroneConfig? _config;
        private Transform? _target;
        private ObjectPool? _pool;
        private DroneRegistry? _registry;
        private IAttackTokenSource? _tokens;
        private Transform? _transform;
        private Collider? _bodyCollider;
        private Collider? _targetCollider;
        private MaterialPropertyBlock? _propertyBlock;
        private EnemyReactionConfig? _reactions;

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
        private readonly float[] _reactionLastPlayedAt = new float[(int)EnemyReactionKind.LowHealth + 1];
        private readonly RaycastHit[] _sightHits = new RaycastHit[8];
        private float _nextSightSampleAt;
        private float _lostSightAt;
        private float _reactionPulseEndsAt;
        private float _reactionPulse;
        private float _telegraphAmount;
        private int _reactionSerial;
        private int _reactionSeed;
        private bool _hadSight;
        private bool _lowHealthReacted;
        private bool _awaitingDeathPresentation;

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
            if (_human == null) TryGetComponent(out _human);
            TryGetComponent(out _bodyCollider);
            _propertyBlock = new MaterialPropertyBlock();
            if (_agent != null) _agent.enabled = false;
        }

        private void OnEnable()
        {
            if (_health != null)
            {
                _health.Damaged += OnHealthDamaged;
                _health.Died += OnHealthDied;
            }
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.Damaged -= OnHealthDamaged;
                _health.Died -= OnHealthDied;
            }
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
            if (_registry != null) _registry.Killed -= OnNearbyDroneKilled;
            _config = config;
            _target = target;
            target.TryGetComponent(out _targetCollider);
            _pool = pool;
            _registry = registry;
            _tokens = tokens;
            _attack = default;
            _awaitingDeathPresentation = false;
            _speedMultiplier = 1f;
            _waveSpeedMultiplier = 1f;
            _nextRepathAt = 0f;
            ResetReactions(config.reactions);
            SetTelegraph(0f);
            // Pooled objects are REUSED. A soldier respawning part-way through
            // its own death animation is the same class of bug the pool's
            // generation counter exists to prevent elsewhere.
            _animator?.ResetForReuse();
            _human?.ResetForReuse();

            // HP comes from the drone's own config, not a shared HealthConfig —
            // one source of truth per archetype.
            if (_health != null) _health.ConfigureMax(config.maxHealth * scaling.HealthMultiplier);

            // Written on EVERY spawn, not once in the prefab, because the pool
            // reuses instances: a Tank retired into the pool and reissued as a
            // Rusher would otherwise keep the Tank's core bonus and a Rusher
            // would die to one shot for reasons nothing in the scene explains.
            _weakpoint?.SetMultiplier(config.weakpointMultiplier);
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
            if (_reactions != null) registry.Killed += OnNearbyDroneKilled;
        }

        private void Update()
        {
            if (!_active || _config == null) return;

            float now = Time.time;
            Steer(now);
            TickReactions(now);

            AttackModule? attack = _config.attack;
            if (attack != null) attack.Tick(this, ref _attack, now, Time.deltaTime);
        }

        private void Steer(float now)
        {
            if (_agent == null || _target == null || _config == null) return;
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            _agent.speed = _config.moveSpeed * _speedMultiplier * _waveSpeedMultiplier;

            if (_human != null && _human.TrySteer(now,
                _config.moveSpeed * _speedMultiplier, _waveSpeedMultiplier)) return;

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
            _telegraphAmount = Mathf.Clamp01(amount);
            _animator?.SetTelegraph(_telegraphAmount);

            ApplyCoreVisual();
        }

        private void ApplyCoreVisual()
        {
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
            float t = Mathf.Max(_telegraphAmount, _reactionPulse);
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
        public void PlayAttackAnimation()
        {
            _animator?.PlayAttack();
            TryReaction(EnemyReactionKind.AttackCommit, Time.time);
        }

        public void SetFiringPosture(bool firing) => _human?.SetFiring(firing);

        public void PlayReloadAnimation() => _animator?.PlayReload();

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
        public void DespawnNow()
        {
            if (_awaitingDeathPresentation) FinishDeathPresentation();
            else Retire(raiseDied: false, default);
        }

        private void ResetReactions(EnemyReactionConfig? config)
        {
            _reactions = config;
            _nextSightSampleAt = 0f;
            _lostSightAt = 0f;
            _reactionPulseEndsAt = 0f;
            _reactionPulse = 0f;
            _telegraphAmount = 0f;
            _reactionSerial = 0;
            _reactionSeed = GetInstanceID();
            _hadSight = false;
            _lowHealthReacted = false;
            for (int i = 0; i < _reactionLastPlayedAt.Length; i++)
                _reactionLastPlayedAt[i] = float.NegativeInfinity;
        }

        private void TickReactions(float now)
        {
            if (_reactionPulse > 0f && now >= _reactionPulseEndsAt)
            {
                _reactionPulse = 0f;
                ApplyCoreVisual();
            }

            if (_reactions == null || _target == null || now < _nextSightSampleAt) return;

            // Instance-derived phase offset keeps a fresh wave from sampling and
            // reacting in lockstep on the same frame.
            float jitter = Hash01(_reactionSeed, _reactionSerial++, (int)EnemyReactionKind.DetectPlayer);
            _nextSightSampleAt = now + _reactions.sightSampleInterval * Mathf.Lerp(0.8f, 1.2f, jitter);

            Vector3 sensorOrigin = _bodyCollider != null ? _bodyCollider.bounds.center : Position;
            Vector3 targetPoint = _targetCollider != null ? _targetCollider.bounds.center : _target.position;
            Vector3 delta = targetPoint - sensorOrigin;
            bool inRange = delta.sqrMagnitude <= _reactions.detectionRange * _reactions.detectionRange;
            bool visible = inRange && HasLineOfSight(sensorOrigin, delta);

            if (visible)
            {
                if (!_hadSight) TryReaction(EnemyReactionKind.DetectPlayer, now);
                _hadSight = true;
                _lostSightAt = 0f;
                return;
            }

            if (!_hadSight) return;
            if (_lostSightAt <= 0f)
            {
                _lostSightAt = now;
                return;
            }
            if (now - _lostSightAt < _reactions.lostSightSeconds) return;

            _hadSight = false;
            _lostSightAt = 0f;
            TryReaction(EnemyReactionKind.LostSight, now);
        }

        private bool HasLineOfSight(Vector3 origin, Vector3 delta)
        {
            if (_reactions == null || _target == null) return false;
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon) return true;

            int count = Physics.RaycastNonAlloc(origin, delta / distance, _sightHits, distance,
                _reactions.sightMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return true;

            float nearestDistance = float.MaxValue;
            Transform? nearest = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _sightHits[i];
                if (hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                nearest = hit.transform;
            }
            return nearest != null && nearest.root == _target.root;
        }

        private void OnHealthDamaged(Health health, DamageInfo info)
        {
            if (!_active) return;
            _human?.React(in info);
            if (_reactions == null) return;
            float now = Time.time;
            if (info.Amount >= health.Max * _reactions.heavyDamageFraction)
                TryReaction(EnemyReactionKind.HeavyDamage, now);

            if (!_lowHealthReacted && health.Normalized <= _reactions.lowHealthFraction)
            {
                _lowHealthReacted = true;
                TryReaction(EnemyReactionKind.LowHealth, now);
            }
        }

        private void OnNearbyDroneKilled(DroneController drone, DamageInfo info)
        {
            if (!_active || _reactions == null || drone == this) return;
            Vector3 delta = drone.Position - Position;
            if (delta.sqrMagnitude > _reactions.allyDeathRadius * _reactions.allyDeathRadius) return;
            TryReaction(EnemyReactionKind.NearbyAllyDeath, Time.time);
        }

        private bool TryReaction(EnemyReactionKind kind, float now)
        {
            if (_reactions == null) return false;
            EnemyReactionResponse? response = _reactions.ResponseFor(kind);
            if (response == null) return false;

            int index = (int)kind;
            if (now - _reactionLastPlayedAt[index] < response.cooldownSeconds) return false;
            float roll = Hash01(_reactionSeed, _reactionSerial++, index);
            if (roll > response.probability) return false;

            _reactionLastPlayedAt[index] = now;
            _reactionPulse = Mathf.Max(_reactionPulse, response.corePulse);
            _reactionPulseEndsAt = Mathf.Max(_reactionPulseEndsAt, now + response.pulseSeconds);
            ApplyCoreVisual();
            PlayCue(response.cue);
            return true;
        }

        private static float Hash01(int seed, int serial, int kind)
        {
            unchecked
            {
                uint value = (uint)(seed * 73856093) ^ (uint)(serial * 19349663) ^ (uint)(kind * 83492791);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }

        private void OnHealthDied(Health health, DamageInfo info)
        {
            if (!_active) return;
            if (_human != null && _human.BeginDeath(in info))
            {
                ReleaseCombat(raiseDied: true, in info);
                _awaitingDeathPresentation = true;
                return;
            }
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
            ReleaseCombat(raiseDied, in info);
            ReturnToPool();
        }

        private void ReleaseCombat(bool raiseDied, in DamageInfo info)
        {
            if (!_active) return;
            _active = false;

            if (_config != null && _config.attack != null) _config.attack.Cancel(this, ref _attack);
            ReleaseAttackToken(ref _attack);
            if (_registry != null) _registry.Killed -= OnNearbyDroneKilled;
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
            ResetReactions(null);

            if (_agent != null && _agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.ResetPath();
                _agent.enabled = false;
            }

        }

        public void FinishDeathPresentation()
        {
            if (!_awaitingDeathPresentation) return;
            _awaitingDeathPresentation = false;
            ReturnToPool();
        }

        private void ReturnToPool()
        {
            if (_pool != null && _pooled != null) _pool.Despawn(_pooled);
            else gameObject.SetActive(false);
        }
    }
}
