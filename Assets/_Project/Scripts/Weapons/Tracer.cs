#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// One visible round in flight, muzzle to impact point.
    ///
    /// WHAT A TRACER IS FOR, AND WHY THERE IS ONE EVERY THIRD ROUND
    /// A hitscan weapon resolves its damage on the frame the trigger is pulled,
    /// so without a tracer the only evidence a round ever existed is the flash
    /// at one end and the spark at the other — and at forty metres through fog
    /// the spark is a few pixels. The tracer is the line that connects them, and
    /// it is what makes a miss legible as a miss rather than as a bug. Every
    /// round carrying one is the opposite mistake: a continuous ribbon of light
    /// out of the barrel reads as a laser show, and it flattens the muzzle flash
    /// it is competing with. The cadence lives on WeaponConfig.
    ///
    /// A TrailRenderer, deliberately, and not a LineRenderer or a particle:
    /// it lays its own points as the transform moves, so the whole effect costs
    /// one transform write per frame and allocates nothing on the managed heap.
    ///
    /// THE POOLED-TRAIL TRAP
    /// A TrailRenderer keeps the points it laid down last time. Take one out of
    /// the pool, move it to the muzzle and enable it, and it draws a line from
    /// wherever it last died straight to the barrel — a bright streak across the
    /// arena on the first frame of every reused tracer. Clear() on Launch, and
    /// again on despawn, is the whole fix, and there is no way to notice it
    /// missing from a headless test run.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class Tracer : MonoBehaviour
    {
        /// <summary>
        /// Absolute ceiling on how long one tracer may live, in seconds.
        ///
        /// NOT A TUNING VALUE — a stuck-object guard, the same kind of number as
        /// WeaponController.MAX_FOLLOW_UPS_PER_PULL. Launch computes the real
        /// lifetime from the distance it was actually given; this is what
        /// catches the mis-authored case (a tracerSpeed near zero, a hit point
        /// resolved to something absurd) before a pooled instance is stranded
        /// alive forever and the pool grows an instance per shot for the rest of
        /// the run. A stranded tracer is a leak, not a glitch.
        /// </summary>
        private const float MAX_LIFETIME_SECONDS = 8f;

        /// <summary>
        /// Slack on top of the trail's own fade, so the tail is never cut off
        /// mid-dissolve. Presentation slack, not tuning.
        /// </summary>
        private const float TAIL_GRACE_SECONDS = 0.05f;

        private TrailRenderer? _trail;
        private Transform? _transform;
        private PooledObject? _pooled;
        private ObjectPool? _pool;

        private Vector3 _direction = Vector3.forward;
        private float _speed = 1f;
        private float _remaining;
        private float _despawnAt;
        private bool _flying;

        /// <summary>True between Launch and the moment the tail has finished fading.</summary>
        public bool InFlight => _flying;

        /// <summary>
        /// The absolute Time.time this tracer will retire at, fixed at Launch.
        /// Public so a test can prove a round aimed at a wall two hundred metres
        /// away cannot outlive its own flight — which is not something a headless
        /// run can observe by watching it.
        /// </summary>
        public float DespawnAt => _despawnAt;

        private void Awake()
        {
            _transform = transform;
            TryGetComponent(out _trail);
            TryGetComponent(out _pooled);
        }

        /// <summary>
        /// Sends this tracer from the muzzle to the point the round actually
        /// stopped at. Everything it needs is passed in: the component reads no
        /// config of its own, so one prefab serves every weapon and the numbers
        /// stay on the WeaponConfig that fired it.
        /// </summary>
        public void Launch(ObjectPool pool, Vector3 from, Vector3 to, float speed, float width)
        {
            _pool = pool;
            _speed = Mathf.Max(1f, speed);

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            // A zero-length shot would divide by nothing. It cannot happen from
            // the fire path (the fallback end point is maxRange down the aim
            // ray), which is exactly why it is worth one line here rather than
            // a NaN direction that renders as an invisible tracer forever.
            _direction = distance > 0.0001f ? delta / distance : Vector3.forward;
            _remaining = distance;

            if (_transform != null)
            {
                _transform.SetPositionAndRotation(from, Quaternion.LookRotation(_direction));
            }

            if (_trail != null)
            {
                // See the class comment: a pooled trail still holds the points it
                // laid on its last flight, and enabling it here without this
                // draws a line from that grave to this muzzle.
                _trail.Clear();
                _trail.widthMultiplier = Mathf.Max(0.001f, width);
                _trail.emitting = true;
            }

            float fade = _trail != null ? _trail.time : 0f;
            _despawnAt = Time.time + Mathf.Min(distance / _speed + fade + TAIL_GRACE_SECONDS,
                                               MAX_LIFETIME_SECONDS);
            _flying = true;
        }

        private void Update()
        {
            if (!_flying) return;

            if (_remaining > 0f)
            {
                float step = Mathf.Min(_speed * Time.deltaTime, _remaining);
                _remaining -= step;
                if (_transform != null) _transform.position += _direction * step;

                // DELIBERATELY NOT stopping emission here, and the reason is the
                // whole reason close-range tracers exist.
                //
                // A TrailRenderer records one position per internal update while
                // emitting, and needs TWO recorded points before it draws a single
                // segment. Killing emission the instant the round arrived meant
                // point one was recorded on the launch frame and point two never
                // was -- so any hit reached within one frame drew NOTHING. At 250
                // m/s that is every target inside 4.2 m at 60 fps and 8.3 m at 30,
                // which in a game built around rushers closing to melee is most of
                // the shots that matter. Deterministic, not intermittent, and
                // invisible to a headless test because the tests read despawn
                // timing and never geometry.
                //
                // The trail stops growing by itself once the transform stops
                // moving (minVertexDistance), and Retire/OnDisable already clear
                // emitting before the object goes back in the pool.
            }

            if (Time.time < _despawnAt) return;
            Retire();
        }

        /// <summary>
        /// Back to the pool, never Destroy. Called from Update once the clock set
        /// at Launch runs out, which is the only path that ends a tracer.
        /// </summary>
        private void Retire()
        {
            _flying = false;
            if (_trail != null) _trail.emitting = false;
            if (_pool != null && _pooled != null) _pool.Despawn(_pooled);
        }

        /// <summary>
        /// The second half of the pooled-trail fix, and the one that covers the
        /// paths Retire does not: a scene change, a manual Despawn, the pool's
        /// own timed sweep. Whatever put this instance away, the next Launch must
        /// start from an empty trail.
        /// </summary>
        private void OnDisable()
        {
            _flying = false;
            if (_trail == null) return;
            _trail.emitting = false;
            _trail.Clear();
        }
    }
}
