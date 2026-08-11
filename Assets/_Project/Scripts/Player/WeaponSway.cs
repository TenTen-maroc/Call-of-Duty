#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Player
{
    /// <summary>
    /// Moves the weapon the camera does NOT: sway that lags the look direction,
    /// bob tied to movement, and a lowered/tucked pose while sprinting.
    ///
    /// The weapon following the camera slightly late is what sells physical
    /// weight — a viewmodel welded rigidly to the camera reads as a decal on the
    /// screen. Keep the numbers small: overdone bob is the most common first-
    /// project mistake and it makes people nauseous.
    ///
    /// Runs in LateUpdate, after PlayerLook has aimed the camera, so the sway is
    /// computed against the final rotation rather than last frame's.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponSway : MonoBehaviour
    {
        [SerializeField] private PlayerInput? _input = null;
        [SerializeField] private PlayerMotor? _motor = null;

        [Header("Sway (degrees)")]
        [Tooltip("How far the weapon lags behind the look input.")]
        [Range(0f, 12f)][SerializeField] private float _swayAmount = 3.5f;
        [Tooltip("Seconds of lag. 60-80 ms is the weight sweet spot.")]
        [Range(0.01f, 0.3f)][SerializeField] private float _swaySmoothing = 0.07f;

        [Header("Bob")]
        [Tooltip("Vertical bob amplitude in metres at walk speed. Small on purpose.")]
        [Range(0f, 0.08f)][SerializeField] private float _bobAmount = 0.018f;
        [Range(1f, 20f)][SerializeField] private float _bobFrequency = 8.5f;

        [Header("Poses (local space)")]
        [SerializeField] private Vector3 _hipPosition = new(0.145f, -0.125f, 0.28f);
        [SerializeField] private Vector3 _adsPosition = new(0f, -0.055f, 0.24f);
        [SerializeField] private Vector3 _sprintPosition = new(0.19f, -0.19f, 0.2f);
        [SerializeField] private Vector3 _sprintRotation = new(12f, -18f, 0f);
        [Range(0.02f, 0.5f)][SerializeField] private float _poseSmoothing = 0.09f;

        private Vector3 _positionVelocity;
        private float _bobPhase;
        private Vector2 _swayCurrent;
        private Vector2 _swayVelocity;
        private Transform? _selfTransform;
        private float _adsProgress;

        /// <summary>Set by the weapon each frame so the pose can follow the ADS blend.</summary>
        public void SetAdsProgress(float progress) => _adsProgress = Mathf.Clamp01(progress);

        private void Awake() => _selfTransform = transform;

        private void LateUpdate()
        {
            if (_selfTransform == null) return;

            float deltaTime = Time.deltaTime;
            bool sprinting = _motor != null && _motor.IsSprinting && _adsProgress < 0.2f;

            // --- sway: the weapon trails the look input, then springs back
            Vector2 look = _input != null ? _input.Look : Vector2.zero;
            Vector2 targetSway = new(
                Mathf.Clamp(-look.x, -1f, 1f) * _swayAmount,
                Mathf.Clamp(look.y, -1f, 1f) * _swayAmount);
            // Aiming tightens everything: a swaying sight is unusable.
            targetSway *= Mathf.Lerp(1f, 0.25f, _adsProgress);
            _swayCurrent = Vector2.SmoothDamp(_swayCurrent, targetSway, ref _swayVelocity, _swaySmoothing);

            // --- bob: driven by actual ground speed, so it stops when you do
            float speed = _motor != null && _motor.IsGrounded ? _motor.HorizontalSpeed : 0f;
            float bobScale = Mathf.Clamp01(speed / 6f) * Mathf.Lerp(1f, 0.35f, _adsProgress);
            _bobPhase += deltaTime * _bobFrequency * Mathf.Max(0.2f, bobScale);
            Vector3 bob = new(
                Mathf.Cos(_bobPhase) * _bobAmount * 0.5f * bobScale,
                Mathf.Abs(Mathf.Sin(_bobPhase)) * _bobAmount * bobScale,
                0f);

            // --- pose
            Vector3 targetPosition = sprinting
                ? _sprintPosition
                : Vector3.Lerp(_hipPosition, _adsPosition, _adsProgress);

            _selfTransform.localPosition = Vector3.SmoothDamp(
                _selfTransform.localPosition, targetPosition + bob, ref _positionVelocity, _poseSmoothing);

            Vector3 targetEuler = sprinting
                ? _sprintRotation
                : new Vector3(_swayCurrent.y, _swayCurrent.x, _swayCurrent.x * 0.5f);

            _selfTransform.localRotation = Quaternion.Slerp(
                _selfTransform.localRotation,
                Quaternion.Euler(targetEuler),
                1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, _poseSmoothing)));
        }
    }
}
