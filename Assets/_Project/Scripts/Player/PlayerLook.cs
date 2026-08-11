#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Player
{
    /// <summary>
    /// Mouse look. Yaw turns the body, pitch tilts the camera — splitting them
    /// is what keeps movement aligned with where the player is facing.
    ///
    /// Camera work runs in LateUpdate, always: if the camera moved in Update it
    /// could be applied before the motor moved the body that frame, and the
    /// result is a subtle jitter that is very hard to diagnose later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLook : MonoBehaviour
    {
        [SerializeField] private GameConfig? _config = null;
        [SerializeField] private PlayerInput? _input = null;
        [SerializeField] private PlayerMotor? _motor = null;
        [Tooltip("The pitch pivot. The camera itself lives under this.")]
        [SerializeField] private Transform? _cameraPivot = null;
        [SerializeField] private Camera? _camera = null;

        private Transform? _selfTransform;
        private float _yaw;
        private float _pitch;
        private float _sensitivityMultiplier = 1f;
        private float _recoilPitch;
        private float _recoilYaw;
        private float _fovOffset;
        private float _dipDegrees;
        private float _dipVelocity;

        /// <summary>Set by the weapon while aiming, so ADS slows the crosshair.</summary>
        public void SetSensitivityMultiplier(float multiplier) => _sensitivityMultiplier = multiplier;

        /// <summary>Additive FOV from the weapon (ADS zoom, fire kick).</summary>
        public void SetFovOffset(float offset) => _fovOffset = offset;

        /// <summary>Called once per shot. Recoil is a camera rotation, not a crosshair effect.</summary>
        public void AddRecoil(float pitchDegrees, float yawDegrees)
        {
            _recoilPitch += pitchDegrees;
            _recoilYaw += yawDegrees;
        }

        /// <summary>
        /// Where the shot actually goes. Taken from the PIVOT, not the camera: the
        /// camera carries the shake offset, and shake must never move the point of
        /// impact — that reads as the game cheating.
        /// </summary>
        public Ray AimRay => _cameraPivot != null
            ? new Ray(_cameraPivot.position, _cameraPivot.forward)
            : new Ray(transform.position, transform.forward);

        public Camera? ViewCamera => _camera;

        /// <summary>The un-modified vertical FOV, so the weapon can compute its ADS offset from it.</summary>
        public float BaseFov => _config != null ? _config.baseFovVertical : 60f;

        private void Awake()
        {
            _selfTransform = transform;
            _yaw = _selfTransform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void LateUpdate()
        {
            if (_config == null || _input == null || _selfTransform == null || _cameraPivot == null) return;

            Vector2 look = _input.Look * (_config.mouseSensitivity * _sensitivityMultiplier);
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, -_config.pitchClamp, _config.pitchClamp);

            // Recoil decays toward zero but the aim point keeps what the player
            // did not pull back down — see WeaponRecoil for the 85% rule.
            _selfTransform.rotation = Quaternion.Euler(0f, _yaw + _recoilYaw, 0f);
            _cameraPivot.localRotation = Quaternion.Euler(_pitch + _recoilPitch, 0f, 0f);

            UpdateFov();
            UpdateLandingDip();
        }

        private void UpdateFov()
        {
            if (_camera == null || _config == null) return;

            float sprintBonus = _motor != null && _motor.IsSprinting ? _config.sprintFovBonus : 0f;
            float target = _config.baseFovVertical + sprintBonus + _fovOffset;
            float ease = Mathf.Max(0.01f, _config.sprintFovEaseTime);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, target, 1f - Mathf.Exp(-Time.deltaTime / ease));
        }

        private void UpdateLandingDip()
        {
            if (_cameraPivot == null || _config == null) return;

            if (_motor != null && _motor.LandingImpact > 0f)
            {
                _dipDegrees += _config.landingDipDegrees * _motor.LandingImpact;
            }

            _dipDegrees = Mathf.SmoothDamp(_dipDegrees, 0f, ref _dipVelocity,
                Mathf.Max(0.01f, _config.landingDipTime));

            if (_dipDegrees > 0.001f)
            {
                _cameraPivot.localRotation *= Quaternion.Euler(_dipDegrees, 0f, 0f);
            }
        }

        /// <summary>
        /// Recoil recovery is driven by the weapon, which owns the timing curve.
        /// </summary>
        public void RecoverRecoil(float pitchAmount, float yawAmount)
        {
            _recoilPitch = Mathf.MoveTowards(_recoilPitch, 0f, pitchAmount);
            _recoilYaw = Mathf.MoveTowards(_recoilYaw, 0f, yawAmount);
        }

        /// <summary>Bakes leftover recoil into the real aim point, so recovery never returns 100%.</summary>
        public void CommitRecoilToAim(float fraction)
        {
            float keptPitch = _recoilPitch * fraction;
            float keptYaw = _recoilYaw * fraction;
            _pitch = Mathf.Clamp(_pitch + keptPitch, -(_config?.pitchClamp ?? 89f), _config?.pitchClamp ?? 89f);
            _yaw += keptYaw;
            _recoilPitch -= keptPitch;
            _recoilYaw -= keptYaw;
        }
    }
}
