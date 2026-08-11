#nullable enable
using CoD.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// What being hurt looks and sounds like. Until the first drone existed the
    /// player could not take damage at all, so the only feedback was a number in
    /// the corner — and a number changing is not feedback, it is bookkeeping.
    ///
    /// Three layers, in the order the player notices them:
    ///   1. a red flash, so damage registers even mid-firefight
    ///   2. a directional wedge, so the player learns WHERE it came from — the
    ///      same principle as the Shooter's deliberate first miss
    ///   3. a pulsing edge tint under the low-health threshold, so dying is never
    ///      a surprise
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDamageFeedback : MonoBehaviour
    {
        [SerializeField] private GameConfig? _config = null;
        [SerializeField] private Health? _health = null;
        [Tooltip("Full-screen image, alpha driven. Colour comes from the prefab, timing from GameConfig.")]
        [SerializeField] private Image? _flash = null;
        [Tooltip("Full-screen tint that pulses when health is low.")]
        [SerializeField] private Image? _lowHealthTint = null;
        [Tooltip("Four screen-edge wedges in Crosshair order: up, down, left, right.")]
        [SerializeField] private Image[] _directionBars = System.Array.Empty<Image>();
        [Tooltip("Damage direction is relative to where the player is LOOKING, so it must read the camera, not the body.")]
        [SerializeField] private Transform? _cameraTransform = null;
        [SerializeField] private AudioSource? _audio = null;
        [SerializeField] private AudioClip? _hurtClip = null;

        private float _flashUntil;
        private float _flashStartedAt;
        private readonly float[] _barUntil = new float[4];
        private float _barDuration = 1.1f;

        private void OnEnable()
        {
            if (_health != null) _health.Damaged += OnDamaged;
            HideAll();
        }

        private void OnDisable()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
        }

        private void OnDamaged(Health health, DamageInfo info)
        {
            float now = Time.time;
            float duration = _config != null ? _config.damageFlashDuration : 0.18f;
            _barDuration = _config != null ? _config.damageDirectionDuration : 1.1f;

            _flashStartedAt = now;
            _flashUntil = now + duration;

            int bar = ResolveDirectionBar(info.Direction);
            if (bar >= 0 && bar < _barUntil.Length) _barUntil[bar] = now + _barDuration;

            if (_audio != null && _hurtClip != null) _audio.PlayOneShot(_hurtClip);
        }

        /// <summary>
        /// Which edge lights up. info.Direction is the direction the damage was
        /// TRAVELLING, so the source sits the other way; project that onto the
        /// camera's own axes and take the dominant one.
        /// </summary>
        private int ResolveDirectionBar(Vector3 travelDirection)
        {
            if (_cameraTransform == null) return 0;

            Vector3 toSource = -travelDirection;
            toSource.y = 0f;
            if (toSource.sqrMagnitude < 0.0001f) return 0;
            toSource.Normalize();

            Vector3 forward = _cameraTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return 0;
            forward.Normalize();

            Vector3 right = _cameraTransform.right;
            right.y = 0f;
            right.Normalize();

            float forwardDot = Vector3.Dot(forward, toSource);
            float rightDot = Vector3.Dot(right, toSource);

            if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot)) return forwardDot >= 0f ? 0 : 1;
            return rightDot >= 0f ? 3 : 2;
        }

        private void Update()
        {
            float now = Time.time;
            UpdateFlash(now);
            UpdateDirectionBars(now);
            UpdateLowHealth(now);
        }

        private void UpdateFlash(float now)
        {
            if (_flash == null) return;

            float alpha = 0f;
            if (now < _flashUntil)
            {
                float duration = Mathf.Max(0.01f, _flashUntil - _flashStartedAt);
                float remaining = 1f - (now - _flashStartedAt) / duration;
                alpha = remaining * (_config != null ? _config.damageFlashAlpha : 0.32f);
            }
            SetAlpha(_flash, alpha);
        }

        private void UpdateDirectionBars(float now)
        {
            for (int i = 0; i < _directionBars.Length && i < _barUntil.Length; i++)
            {
                Image? bar = _directionBars[i];
                if (bar == null) continue;
                float remaining = _barUntil[i] - now;
                SetAlpha(bar, remaining <= 0f ? 0f : Mathf.Clamp01(remaining / Mathf.Max(0.01f, _barDuration)) * 0.85f);
            }
        }

        private void UpdateLowHealth(float now)
        {
            if (_lowHealthTint == null || _health == null) return;

            float threshold = _config != null ? _config.lowHealthThreshold : 0.35f;
            float normalized = _health.Normalized;
            if (normalized > threshold || !_health.IsAlive)
            {
                SetAlpha(_lowHealthTint, 0f);
                return;
            }

            // Deeper into the red = stronger tint, and a slow pulse on top so it
            // reads as an alarm rather than a static overlay the eye filters out.
            float severity = threshold <= 0f ? 1f : 1f - normalized / threshold;
            float speed = _config != null ? _config.lowHealthPulseSpeed : 2.2f;
            float maxAlpha = _config != null ? _config.lowHealthMaxAlpha : 0.4f;
            float pulse = 0.65f + 0.35f * Mathf.Sin(now * speed * Mathf.PI);
            SetAlpha(_lowHealthTint, severity * maxAlpha * pulse);
        }

        private void HideAll()
        {
            SetAlpha(_flash, 0f);
            SetAlpha(_lowHealthTint, 0f);
            for (int i = 0; i < _directionBars.Length; i++)
            {
                SetAlpha(_directionBars[i], 0f);
                if (i < _barUntil.Length) _barUntil[i] = 0f;
            }
        }

        private static void SetAlpha(Image? image, float alpha)
        {
            if (image == null) return;
            Color color = image.color;
            color.a = alpha;
            image.color = color;
            // Toggling enabled keeps a fully transparent overlay out of the
            // transparent queue entirely — four idle full-screen quads cost real
            // fill rate on a laptop GPU.
            bool visible = alpha > 0.001f;
            if (image.enabled != visible) image.enabled = visible;
        }
    }
}
