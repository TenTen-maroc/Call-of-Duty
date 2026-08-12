#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Player
{
    /// <summary>
    /// The player's feet: a step every <see cref="FootstepConfig.strideLength"/>
    /// metres of ground actually covered, plus a thump on landing.
    ///
    /// A DISTANCE ACCUMULATOR, NOT A TIMER, and the difference is the whole
    /// component. A timer fires every N seconds, so walking and sprinting produce
    /// the same number of steps per second and the player hears legs that move at
    /// one rate while the world slides past at three — the classic "running on the
    /// spot" defect. Accumulating DISTANCE makes step spacing a property of the
    /// ground rather than of the clock: the same legs, taking the same size stride,
    /// simply arrive more often when the body is moving faster. It also means
    /// crouch-walking, walking and sprinting need no separate cadence numbers,
    /// which is one fewer table to keep in agreement.
    ///
    /// ONE RAYCAST PER STEP. The surface probe fires at the instant a step plays —
    /// roughly twice a second — and never in the general per-frame path. Probing
    /// every frame would be sixty casts a second to answer a question that is
    /// asked twice, and in a game that budgets sixteen kilobytes of allocation per
    /// frame with forty drones alive, "it is only a raycast" is exactly how that
    /// budget goes.
    ///
    /// NOTHING HERE ALLOCATES. The AudioSource is cached, the probe uses the
    /// single-hit Raycast overload with an `out` struct, clips come out of arrays
    /// the config already owns, and volumes are floats. This component runs for
    /// every frame of every run; it has to cost nothing.
    ///
    /// SILENCE IS THE SHIPPED STATE. The project has no footstep WAVs yet, so an
    /// unauthored clip array plays nothing and logs nothing. See FootstepConfig.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Footsteps : MonoBehaviour
    {
        [Tooltip("Stride, gaits, jitter, surfaces. Nothing is played without one.")]
        [SerializeField] private FootstepConfig? _config = null;

        [Tooltip("Where speed, gait, grounding and the landing impulse come from. On the same GameObject.")]
        [SerializeField] private PlayerMotor? _motor = null;

        [Tooltip("PlayOneShot only, so a step never cuts off the one before it. 2D — these are the player's own feet.")]
        [SerializeField] private AudioSource? _audio = null;

        private Transform? _selfTransform;
        private Vector3 _lastPosition;
        private float _distanceSinceStep;

        /// <summary>
        /// Stops one landing playing twice.
        ///
        /// PlayerMotor.LandingImpact is a per-frame value: it is zeroed at the top
        /// of the motor's Update and set non-zero only on the frame the controller
        /// regains the ground. That makes it order-dependent — read before the
        /// motor runs, this component sees the PREVIOUS frame's value; read after,
        /// the current one — and Unity does not promise which way round two
        /// components on the same object run. The latch makes the answer the same
        /// either way: fire on the first frame the value is non-zero, and re-arm
        /// only once it is back to zero.
        /// </summary>
        private bool _landingLatched;

        /// <summary>Which clip played last, so the random pick never repeats a sample back to back.</summary>
        private int _lastClipIndex = -1;

        private void Awake()
        {
            _selfTransform = transform;
            _lastPosition = _selfTransform.position;

            // one-time: a serialized reference is the intended wiring, and this is
            // the fallback for a rig that put the source on the player itself.
            if (_audio == null) _audio = GetComponent<AudioSource>();

            // A MISSING REFERENCE AND A MISSING CLIP ARE DIFFERENT FAILURES, and
            // this component treats them differently on purpose. An unauthored
            // clip array is the shipped state and says nothing. An unwired
            // reference is a scene that was built wrong, and the only symptom
            // would otherwise be silence — indistinguishable from the state above,
            // which is exactly how a wiring bug survives to the build. Warn rather
            // than Error because footsteps are cosmetic: this must be loud in the
            // editor and in a development build, and must not spam a shipped
            // player's log.
            if (_config == null || _motor == null || _audio == null)
            {
                GameLog.Warn(
                    "Footsteps is missing a reference (config, motor or AudioSource) — the player will " +
                    "walk in silence, which looks exactly like having no clips authored.", this);
                enabled = false;
                return;
            }

            // PlayOneShot ignores clip/loop but not these, and a footstep source
            // that inherited playOnAwake from a prefab would blare on frame one.
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;
            _audio.dopplerLevel = 0f;
            // Null today. The whole AudioMixer migration is this one line already
            // being here — see docs/systems/audio.md.
            _audio.outputAudioMixerGroup = _config.outputGroup;
        }

        private void Update()
        {
            if (_config == null || _motor == null || _audio == null || _selfTransform == null) return;

            Vector3 position = _selfTransform.position;
            Vector3 moved = position - _lastPosition;
            _lastPosition = position;

            HandleLanding(position);

            // Airborne feet do not touch anything. Accumulating while falling
            // would bank a stride's worth of distance and fire a step the instant
            // the player lands, on top of the landing thump.
            if (!_motor.IsGrounded) return;

            float speed = _motor.HorizontalSpeed;
            if (speed < _config.minSpeed)
            {
                // Standing still. Bank most of a stride so the first step after a
                // standstill arrives promptly — at zero the player skates for a
                // full stride before their feet engage, which reads as the audio
                // being late rather than as a deliberate wind-up.
                float banked = _config.strideLength * _config.firstStepFraction;
                if (_distanceSinceStep < banked) _distanceSinceStep = banked;
                return;
            }

            // THE SMALLER OF INTENT AND REALITY, and it fixes two bugs at once.
            //
            // PlayerMotor.HorizontalSpeed is the velocity the motor WANTS: it is
            // integrated from input and never learns that CharacterController.Move
            // was blocked. A player holding W against a wall therefore reports full
            // walking speed forever, and a naive `speed * deltaTime` accumulator
            // plays footsteps for as long as they lean on it. Measuring the
            // transform instead fixes that — and introduces the opposite failure,
            // because a respawn or a teleport moves the transform tens of metres in
            // one frame and would machine-gun a dozen steps out of it.
            //
            // Taking the minimum keeps each one's answer where it is right: the
            // wall drives ACTUAL to zero, the teleport is clipped by INTENT, and
            // ordinary walking has the two within a rounding error of each other.
            moved.y = 0f;
            _distanceSinceStep += Mathf.Min(speed * Time.deltaTime, moved.magnitude);

            if (_distanceSinceStep < _config.strideLength) return;

            // Carry the remainder rather than zeroing, or a stride is quietly
            // lengthened by however much of a frame it overshot by — at sprint
            // speed that is centimetres per step, and it compounds.
            _distanceSinceStep -= _config.strideLength;

            // A single frame long enough to owe two steps is a hitch or a
            // teleport, not a sprint. Pay one and drop the debt; the alternative
            // is a burst of footsteps every time the game stutters.
            if (_distanceSinceStep > _config.strideLength) _distanceSinceStep = 0f;

            PlayStep(position);
        }

        /// <summary>
        /// The landing thump, exactly once per landing, scaled by how hard it was.
        /// A hop off a kerb and a drop off the centre bunker are different events
        /// and the ear is the only thing that reports the difference.
        /// </summary>
        private void HandleLanding(Vector3 position)
        {
            if (_config == null || _motor == null || _audio == null) return;

            float impact = _motor.LandingImpact;
            if (impact <= 0f)
            {
                _landingLatched = false;
                return;
            }

            if (_landingLatched) return;
            _landingLatched = true;

            // Feet are back under the player. Restart the stride so the first step
            // after a landing is a step and not the tail of the jump.
            _distanceSinceStep = 0f;

            if (impact < _config.landMinImpact) return;

            FootstepConfig.SurfaceSet? set = _config.Surface(ProbeSurface(position));
            if (set == null) return;

            // A surface only needs its own landing clips if it lands differently
            // from how it walks. Falling back to the step clips means a new floor
            // is one array, not two.
            AudioClip? clip = PickClip(set.landClips);
            if (clip == null) clip = PickClip(set.stepClips);
            if (clip == null) return;

            _audio.pitch = Jitter(_config.landPitch * set.pitchScale, _config.pitchJitter);
            _audio.PlayOneShot(clip,
                Jitter(_config.landVolume * set.volumeScale * impact, _config.volumeJitter));
        }

        private void PlayStep(Vector3 position)
        {
            if (_config == null || _motor == null || _audio == null) return;

            FootstepConfig.SurfaceSet? set = _config.Surface(ProbeSurface(position));
            if (set == null) return;

            AudioClip? clip = PickClip(set.stepClips);
            if (clip == null) return;

            // Gait changes volume and pitch and NOTHING ELSE. Spacing is the
            // accumulator's job — see the class header for why putting a cadence
            // number here would undo the whole design.
            float volume;
            float pitch;
            if (_motor.IsCrouched)
            {
                volume = _config.crouchVolume;
                pitch = _config.crouchPitch;
            }
            else if (_motor.IsSprinting)
            {
                volume = _config.sprintVolume;
                pitch = _config.sprintPitch;
            }
            else
            {
                volume = _config.walkVolume;
                pitch = _config.walkPitch;
            }

            _audio.pitch = Jitter(pitch * set.pitchScale, _config.pitchJitter);
            _audio.PlayOneShot(clip, Jitter(volume * set.volumeScale, _config.volumeJitter));
        }

        /// <summary>
        /// One downward ray, fired only from a step or a landing. Returns an index
        /// into the config's surface list, or its default when the probe finds
        /// nothing — which happens on the frame the player walks off a ledge, and
        /// must not be an error.
        /// </summary>
        private int ProbeSurface(Vector3 footPosition)
        {
            if (_config == null) return -1;

            Vector3 origin = footPosition + Vector3.up * _config.probeStartHeight;
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _config.probeDistance,
                _config.groundMask, QueryTriggerInteraction.Ignore)
                ? _config.ResolveSurface(hit.collider)
                : _config.DefaultSurfaceIndex;
        }

        /// <summary>
        /// A random clip that is never the one before it.
        ///
        /// Plain Random.Range on a four-clip array repeats a quarter of the time,
        /// and a repeated sample is the single loudest tell that footsteps are a
        /// file being played rather than a person walking. Rejecting the last one
        /// costs an int and turns four clips into four genuinely different steps.
        /// </summary>
        private AudioClip? PickClip(AudioClip?[]? clips)
        {
            if (clips == null || clips.Length == 0) return null;

            if (clips.Length == 1)
            {
                _lastClipIndex = 0;
                return clips[0];
            }

            int index = Random.Range(0, clips.Length);
            if (index == _lastClipIndex) index = (index + 1) % clips.Length;
            _lastClipIndex = index;
            return clips[index];
        }

        /// <summary>
        /// Symmetric multiplicative wobble. Static and pure — a static METHOD is
        /// fine under the no-mutable-statics rule; static STATE is what Domain
        /// Reload being off makes fatal.
        /// </summary>
        private static float Jitter(float value, float amount)
            => amount <= 0f ? value : value * (1f + Random.Range(-amount, amount));
    }
}
