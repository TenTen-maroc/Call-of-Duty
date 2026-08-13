#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Everything in this game that travels rather than arriving instantly: the
    /// Shooter's bullet, and the player's rocket.
    ///
    /// PROMOTED, NOT REWRITTEN. This was CoD.Enemies.DroneProjectile, and the
    /// launcher needed the identical object — pooled, swept, self-retiring. A
    /// second implementation in CoD.Weapons would have been the same forty lines
    /// with a different set of bugs, and only one of the two would ever get the
    /// next fix. CoD.Core references nothing and both CoD.Enemies and CoD.Weapons
    /// reference Core, so this is the one place both can reach.
    ///
    /// IT SWEEPS A RAY, IT DOES NOT CARRY A COLLIDER. A small fast trigger tunnels
    /// straight through a wall at any reasonable physics step, so each frame casts
    /// from last frame's position to this frame's.
    ///
    /// IT PASSES THROUGH ITS OWN DECLARED SIDE. A drone's round crossing another drone must
    /// keep going rather than stop: drones killing each other would hand the
    /// player free money and make a crowd fight itself. A rocket leaving the
    /// player's barrel must not detonate on the player. Both are one rule — see
    /// <see cref="IFactionMember"/> — and "pass through" must never be confused
    /// with "hit": the sweep restarts from the same point every frame, so a round
    /// that neither moved nor despawned hangs in the air forever holding its
    /// pooled instance, and a wave of shooters leaks the pool for the rest of the
    /// run.
    ///
    /// SPEED IS A DESIGN CHOICE, NOT A LIMITATION. The Shooter's round travels at
    /// ~18 m/s, fast enough to punish standing still and slow enough to sidestep
    /// once you have seen it leave the barrel. A hitscan enemy weapon is
    /// unreadable and unavoidable at the same time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField] private PooledObject? _pooled = null;

        [Tooltip("Optional. The smoke/flame trail on a rocket; the drones' round has none. See Launch for the pooled-trail trap that makes this three lines rather than zero.")]
        [SerializeField] private TrailRenderer? _trail = null;

        private Transform? _transform;
        private ObjectPool? _pool;
        private Vector3 _velocity;
        private Vector3 _direction = Vector3.forward;
        private float _damage;
        private float _expiresAt;
        private LayerMask _hitMask;
        private Faction _firedBy = Faction.Hostile;
        private Health? _owner;
        private IProjectileImpactSink? _sink;
        private bool _live;

        // Pre-sized: a wave of shooters puts a lot of these in the air. Sized for
        // a step segment crossing several drones AND the wall behind them — an
        // overflow that drops the wall lets the round leave the arena.
        private readonly RaycastHit[] _buffer = new RaycastHit[8];

        /// <summary>
        /// The asset this round was fired from — a <c>WeaponConfig</c> for the
        /// player's launcher, null for a drone.
        ///
        /// CARRIED, NEVER LOOKED UP, and that is the whole reason this field
        /// exists rather than the sink reading its own current weapon at impact:
        /// a rocket in flight OUTLIVES A WEAPON SWAP. Fire the launcher, take the
        /// pistol, and an impact that read the controller's current runtime would
        /// resolve the rocket with the pistol's damage, the pistol's falloff and
        /// the pistol's effect modules. Nothing would log; the rocket would simply
        /// be wrong.
        ///
        /// Typed as <see cref="ScriptableObject"/> because CoD.Core must not
        /// reference CoD.Weapons. The sink casts, and refuses the impact if the
        /// cast fails rather than guessing.
        /// </summary>
        public ScriptableObject? Payload { get; private set; }

        /// <summary>
        /// Metres flown since launch. What a weapon charges damage falloff
        /// against — <c>RaycastHit.distance</c> here is the length of ONE
        /// FRAME'S sweep, a few centimetres, so a rocket resolved against it
        /// would be point blank at every range in the arena.
        /// </summary>
        public float DistanceTravelled { get; private set; }

        /// <summary>True between Launch and the impact or expiry that ends it.</summary>
        public bool InFlight => _live;

        private void Awake()
        {
            _transform = transform;
            if (_pooled == null) TryGetComponent(out _pooled);
            if (_trail == null) TryGetComponent(out _trail);
        }

        /// <summary>
        /// Sends this round on its way. Every field it needs arrives in one
        /// struct, in the style of <c>FollowUp</c> and <c>ObjectPool.PrewarmEntry</c>:
        /// a nine-argument method whose call sites are nine bare positional values
        /// is how a damage number lands in a lifetime slot with nothing to notice.
        /// The struct is passed by <c>in</c>, so it never touches the heap.
        /// </summary>
        public void Launch(in ProjectileShot shot)
        {
            if (shot.Pool == null)
            {
                GameLog.Error("A projectile was launched with no pool — it could never be returned.", this);
                gameObject.SetActive(false);
                return;
            }

            _pool = shot.Pool;
            _velocity = shot.Velocity;
            _damage = shot.Damage;
            _hitMask = shot.HitMask;
            _firedBy = shot.FiredBy;
            _owner = shot.Owner;
            _sink = shot.Sink;
            Payload = shot.Payload;
            DistanceTravelled = 0f;
            _expiresAt = Time.time + Mathf.Max(0.05f, shot.Lifetime);
            _live = true;

            // Face travel so the round reads as a direction rather than a
            // floating cube, and so a trail on the prefab lays down straight.
            if (_velocity.sqrMagnitude > 0.0001f) _direction = _velocity.normalized;
            if (_transform != null) _transform.rotation = Quaternion.LookRotation(_direction);

            // THE POOLED-TRAIL TRAP, and it is the same one Tracer documents. A
            // TrailRenderer keeps the points it laid down on its last flight, so
            // a reused rocket draws a line from wherever the last one detonated
            // straight to the muzzle — a bright streak across the arena on the
            // first frame of every shot after the first. Nothing headless can see
            // it, because it is geometry rather than state.
            if (_trail == null) return;
            _trail.Clear();
            _trail.emitting = true;
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
            DistanceTravelled += distance;
            if (Time.time >= _expiresAt) Despawn();
        }

        /// <summary>
        /// Finds the nearest thing along this frame's step that actually STOPS the
        /// round, and resolves it. Returns false when nothing does, which is the
        /// caller's cue to advance and age the projectile.
        /// </summary>
        private bool TrySweep(Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _buffer, distance,
                _hitMask, QueryTriggerInteraction.Ignore);

            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                if (PassesThrough(_buffer[i].collider)) continue;
                if (nearest < 0 || _buffer[i].distance < _buffer[nearest].distance) nearest = i;
            }

            if (nearest < 0) return false;

            // Charged before the impact resolves, so a weapon's falloff sees the
            // distance to the thing it actually hit rather than to the last frame.
            DistanceTravelled += _buffer[nearest].distance;
            Resolve(_buffer[nearest]);
            return true;
        }

        /// <summary>
        /// True for a collider this round is not allowed to stop on: the shooter,
        /// and anything on the shooter's own side.
        ///
        /// Tests the HEALTH BEHIND the collider, not the collider. Every drone puts
        /// two colliders on the line — the hull, which carries the Health, and the
        /// small `Core` child, which carries only a Weakpoint that relays to it.
        /// Testing the collider alone recognised the hull and not the Core, so a
        /// round that clipped another drone's core hit something with no Health,
        /// found nothing to damage, and vanished: the shooter's own kind acting as
        /// cover for the player.
        /// </summary>
        private bool PassesThrough(Collider collider)
        {
            Health? health = HealthBehind(collider);
            if (health == null) return false;
            if (health == _owner) return true;

            // Unaligned NEVER matches, on either side of the comparison. A body
            // that has declared no side is nobody's friend (see IFactionMember),
            // and a ProjectileShot whose FiredBy was left at its default must fail
            // towards stopping on things rather than towards flying through the
            // whole arena — the second is invisible, and a round that hits nothing
            // is indistinguishable from a shot that was never fired.
            if (_firedBy == Faction.Unaligned) return false;
            return health.Faction == _firedBy;
        }

        /// <summary>The Health a collider speaks for: its own, or the one its Weakpoint points at.</summary>
        private static Health? HealthBehind(Collider collider)
        {
            if (collider.TryGetComponent(out Weakpoint weakpoint)) return weakpoint.Owner;
            if (collider.TryGetComponent(out HitZone zone)) return zone.Owner;
            return collider.TryGetComponent(out Health direct) ? direct : null;
        }

        private void Resolve(in RaycastHit hit)
        {
            IProjectileImpactSink? sink = LiveSink();
            if (sink != null)
            {
                // The sink owns damage, VFX, sound, the hitmarker and the effect
                // modules. This round contributes only where it landed and what
                // fired it.
                sink.OnProjectileImpact(this, in hit, _direction);
                Despawn();
                return;
            }

            Health? health = HealthBehind(hit.collider);
            if (health != null && health.IsAlive)
            {
                HitRegion region = HitRegion.Torso;
                float damage = _damage;
                if (hit.collider.TryGetComponent(out HitZone zone))
                {
                    region = zone.Region;
                    damage *= zone.DamageFactor;
                }
                var info = new DamageInfo(damage, hit.point, hit.normal, _direction,
                    region == HitRegion.Head, region, DamageKind.Direct);
                health.ApplyDamage(in info);
            }

            Despawn();
        }

        /// <summary>
        /// The sink, if there still is one.
        ///
        /// An interface reference cannot see Unity's "destroyed but not garbage
        /// collected" state: the reference stays non-null for a WeaponController
        /// whose GameObject a scene change tore down while this round was still
        /// in the air, and calling into it throws on the first serialized field
        /// it touches. Casting back to <see cref="Object"/> is the only way to
        /// ask the engine rather than the runtime.
        /// </summary>
        private IProjectileImpactSink? LiveSink()
        {
            if (_sink == null) return null;
            if (_sink is Object unityObject && unityObject == null) return null;
            return _sink;
        }

        private void Despawn()
        {
            _live = false;

            // Cleared BEFORE the instance goes back on the stack. A pooled object
            // keeps every field it was last used with, so a rocket's sink and
            // config would otherwise ride into the next drone round that reused
            // this instance — and the strong reference would keep a dead
            // WeaponController and its whole scene alive until then.
            _sink = null;
            _owner = null;
            Payload = null;
            StopTrail();

            if (_pool != null && _pooled != null) _pool.Despawn(_pooled);
            else gameObject.SetActive(false);
        }

        /// <summary>
        /// The other half of the trail fix, covering the paths Despawn does not:
        /// a scene change, a manual Despawn, the pool's own timed sweep.
        /// </summary>
        private void OnDisable()
        {
            _live = false;
            _sink = null;
            _owner = null;
            Payload = null;
            StopTrail();
        }

        private void StopTrail()
        {
            if (_trail == null) return;
            _trail.emitting = false;
            _trail.Clear();
        }
    }

    /// <summary>
    /// One launch, as data. A mutable struct with named fields, deliberately, in
    /// the style of <c>FollowUp</c>: the call site reads as a list of decisions
    /// rather than as nine positional values, and it never touches the heap.
    /// </summary>
    public struct ProjectileShot
    {
        /// <summary>Where the round came from and where it goes back to. Required.</summary>
        public ObjectPool? Pool;

        /// <summary>Direction and speed in one. Metres per second.</summary>
        public Vector3 Velocity;

        /// <summary>
        /// What this round applies on impact ALL BY ITSELF. Ignored entirely when
        /// <see cref="Sink"/> is set, because a sink owns the damage model — see
        /// <see cref="IProjectileImpactSink"/>.
        /// </summary>
        public float Damage;

        /// <summary>Seconds before it retires unspent. The backstop against a round that never meets anything.</summary>
        public float Lifetime;

        /// <summary>What the sweep is allowed to find at all.</summary>
        public LayerMask HitMask;

        /// <summary>The side that fired it. Bodies on this side are passed through, never hit.</summary>
        public Faction FiredBy;

        /// <summary>The shooter's own Health, passed through regardless of side. A rocket must not kill the player who fired it.</summary>
        public Health? Owner;

        /// <summary>The config that fired it. See <see cref="Projectile.Payload"/> for why it is carried rather than looked up.</summary>
        public ScriptableObject? Payload;

        /// <summary>Who resolves the impact. Null means this round resolves its own <see cref="Damage"/>.</summary>
        public IProjectileImpactSink? Sink;
    }
}
