#nullable enable
using UnityEngine;

namespace CoD.Player
{
    /// <summary>
    /// Trauma-based camera shake, applied as a local offset on the camera itself
    /// so it never contaminates the aim ray — shots come from the pivot, shake
    /// is cosmetic. Trauma decays linearly and displacement uses trauma squared,
    /// which is what makes a big hit read as violent and a small one as a tap.
    ///
    /// Deliberately not Cinemachine Impulse for now: an impulse listener needs a
    /// CinemachineCamera driven by a Brain, and that Brain would fight PlayerLook
    /// for FOV and rotation. Revisit when the camera work gets richer (explosions,
    /// hit reactions) — the swap is this one file. Deviation recorded in CLAUDE.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraShake : MonoBehaviour
    {
        [Tooltip("How fast trauma bleeds off, in units per second. 1 = a full-strength shake lasts one second.")]
        [Range(0.2f, 5f)][SerializeField] private float _decayPerSecond = 1.8f;
        [Tooltip("Maximum positional offset in metres at full trauma.")]
        [SerializeField] private float _maxOffset = 0.06f;
        [Tooltip("Maximum rotational offset in degrees at full trauma.")]
        [SerializeField] private float _maxRoll = 1.4f;
        [SerializeField] private float _frequency = 24f;

        private Transform? _selfTransform;
        private float _trauma;
        private float _seed;

        private void Awake()
        {
            _selfTransform = transform;
            // Per-instance seed so two shakes never move in lockstep. Instance
            // state, not static — nothing survives between Play sessions.
            _seed = Random.value * 100f;
        }

        /// <summary>Adds trauma. Values around 0.3 read as a rifle shot, 1.0 as an explosion.</summary>
        public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        private void LateUpdate()
        {
            if (_selfTransform == null) return;

            if (_trauma <= 0f)
            {
                _selfTransform.localPosition = Vector3.zero;
                _selfTransform.localRotation = Quaternion.identity;
                return;
            }

            float shake = _trauma * _trauma;
            float t = Time.time * _frequency;

            float x = (Mathf.PerlinNoise(_seed, t) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(_seed + 17f, t) - 0.5f) * 2f;
            float roll = (Mathf.PerlinNoise(_seed + 43f, t) - 0.5f) * 2f;

            _selfTransform.localPosition = new Vector3(x, y, 0f) * (_maxOffset * shake);
            _selfTransform.localRotation = Quaternion.Euler(0f, 0f, roll * _maxRoll * shake);

            _trauma = Mathf.Max(0f, _trauma - _decayPerSecond * Time.deltaTime);
        }
    }
}
