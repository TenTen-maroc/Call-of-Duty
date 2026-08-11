#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Player
{
    /// <summary>
    /// First-person movement on a CharacterController: walk, sprint, crouch, jump.
    /// Every number comes from GameConfig — this component holds only what is
    /// true right now (current velocity, whether we are crouched).
    ///
    /// CharacterController.Move is not rigidbody physics, so it belongs in
    /// Update, not FixedUpdate: it is applied immediately and reads best when
    /// tied to the rendered frame.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private GameConfig? _config = null;
        [SerializeField] private PlayerInput? _input = null;
        [Tooltip("Layers the crouch/stand check treats as blocking geometry.")]
        [SerializeField] private LayerMask _headroomMask = ~0;

        private CharacterController? _controller;
        private Transform? _selfTransform;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _currentHeight;
        private bool _wantsCrouch;
        private bool _wasGrounded = true;

        /// <summary>Read by the weapon (spread, sprint-to-fire) and the camera (FOV, bob).</summary>
        public bool IsSprinting { get; private set; }
        public bool IsCrouched { get; private set; }
        public bool IsGrounded { get; private set; }
        public float HorizontalSpeed => _horizontalVelocity.magnitude;
        public float LandingImpact { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _selfTransform = transform;
            if (_config != null)
            {
                _currentHeight = _config.standingHeight;
                _controller.height = _currentHeight;
                _controller.center = new Vector3(0f, _currentHeight * 0.5f, 0f);
            }
        }

        private void Update()
        {
            if (_config == null || _input == null || _controller == null || _selfTransform == null) return;

            float deltaTime = Time.deltaTime;
            IsGrounded = _controller.isGrounded;

            HandleLanding();
            HandleCrouch(deltaTime);
            HandleHorizontal(deltaTime);
            HandleVertical(deltaTime);

            Vector3 motion = _horizontalVelocity;
            motion.y = _verticalVelocity;
            CollisionFlags flags = _controller.Move(motion * deltaTime);

            // Head hit a ceiling mid-jump: kill the upward velocity, or the
            // controller stays glued to the ceiling for the rest of the arc.
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
            {
                _verticalVelocity = 0f;
            }

            _wasGrounded = IsGrounded;
        }

        private void HandleLanding()
        {
            LandingImpact = 0f;
            if (IsGrounded && !_wasGrounded && _config != null)
            {
                // Scaled by how hard we hit, so a hop reads differently from a drop.
                LandingImpact = Mathf.Clamp01(Mathf.Abs(_verticalVelocity) / Mathf.Abs(_config.gravity));
            }
        }

        private void HandleCrouch(float deltaTime)
        {
            if (_config == null || _input == null || _controller == null || _selfTransform == null) return;

            _wantsCrouch = _input.CrouchHeld;

            // Do not stand up into geometry — the classic way a player clips
            // through a ceiling and falls out of the arena.
            if (!_wantsCrouch && IsCrouched)
            {
                float needed = _config.standingHeight - _currentHeight + 0.05f;
                Vector3 origin = _selfTransform.position + Vector3.up * _currentHeight;
                if (Physics.SphereCast(origin, _controller.radius * 0.9f, Vector3.up, out _, needed,
                        _headroomMask, QueryTriggerInteraction.Ignore))
                {
                    _wantsCrouch = true;
                }
            }

            float target = _wantsCrouch ? _config.crouchedHeight : _config.standingHeight;
            float speed = Mathf.Abs(_config.standingHeight - _config.crouchedHeight) /
                          Mathf.Max(0.01f, _config.crouchTransition);
            _currentHeight = Mathf.MoveTowards(_currentHeight, target, speed * deltaTime);
            _controller.height = _currentHeight;
            _controller.center = new Vector3(0f, _currentHeight * 0.5f, 0f);
            IsCrouched = _currentHeight < (_config.standingHeight - 0.05f);
        }

        private void HandleHorizontal(float deltaTime)
        {
            if (_config == null || _input == null || _selfTransform == null) return;

            Vector2 move = Vector2.ClampMagnitude(_input.Move, 1f);
            Vector3 wish = (_selfTransform.right * move.x) + (_selfTransform.forward * move.y);

            // Sprint is forward-only and cancelled by crouching — otherwise it is
            // free speed in every direction and the arena shrinks.
            IsSprinting = _input.SprintHeld && !IsCrouched && move.y > 0.1f;

            float targetSpeed = IsCrouched ? _config.crouchSpeed
                : IsSprinting ? _config.sprintSpeed
                : _config.walkSpeed;

            Vector3 targetVelocity = wish * targetSpeed;
            float accel = IsGrounded ? _config.acceleration : _config.airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, accel * deltaTime);
        }

        private void HandleVertical(float deltaTime)
        {
            if (_config == null || _input == null) return;

            if (IsGrounded && _verticalVelocity < 0f)
            {
                // A small downward bias keeps isGrounded true on slopes and steps.
                _verticalVelocity = -2f;
                if (_input.JumpPressed && !IsCrouched) _verticalVelocity = _config.JumpVelocity;
            }
            else
            {
                _verticalVelocity += _config.gravity * deltaTime;
            }
        }
    }
}
