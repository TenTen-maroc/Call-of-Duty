#nullable enable
using CoD.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// The aiming reticle: four arms around a centre dot, and the gap between
    /// them tracks the weapon's CURRENT spread rather than sitting still.
    ///
    /// That is the point of it. Bloom is invisible otherwise — the player sprays,
    /// accuracy quietly degrades, and the game never says so. A crosshair that
    /// opens as it blooms turns "why did I miss" into "I can see I am spraying",
    /// and it teaches burst-firing without a word of tutorial.
    ///
    /// It fades out while aiming down sights, because ADS spread is always zero
    /// and the sight is the aiming device at that point.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Crosshair : MonoBehaviour
    {
        // static readonly, so nothing survives between Play sessions.
        private static readonly Vector2[] Directions =
        {
            Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        };

        [SerializeField] private WeaponController? _weapon = null;
        [Tooltip("Four arms, in the order up, down, left, right.")]
        [SerializeField] private Graphic[] _arms = System.Array.Empty<Graphic>();
        [Tooltip("Fades the whole reticle, outlines included. Per-Graphic alpha would leave the dark outlines behind.")]
        [SerializeField] private CanvasGroup? _group = null;

        [Header("Shape")]
        [Tooltip("Gap from centre to each arm at zero spread, in reference pixels.")]
        [Range(0f, 40f)][SerializeField] private float _baseGap = 7f;
        [Tooltip("Extra gap per degree of spread. This is what makes bloom visible.")]
        [Range(0f, 60f)][SerializeField] private float _pixelsPerDegree = 13f;
        [Tooltip("How quickly the arms chase the spread value. Instant looks twitchy.")]
        [Range(0.01f, 0.4f)][SerializeField] private float _smoothing = 0.05f;

        [Header("Fade")]
        [Range(0f, 1f)][SerializeField] private float _adsAlpha = 0f;
        [Range(0f, 1f)][SerializeField] private float _restAlpha = 0.85f;

        private float _currentGap;
        private float _gapVelocity;

        private void Awake() => _currentGap = _baseGap;

        private void LateUpdate()
        {
            if (_arms.Length == 0) return;

            float spread = _weapon != null ? _weapon.EffectiveSpreadDegrees : 0f;
            float targetGap = _baseGap + spread * _pixelsPerDegree;
            _currentGap = Mathf.SmoothDamp(_currentGap, targetGap, ref _gapVelocity, _smoothing);

            for (int i = 0; i < _arms.Length && i < Directions.Length; i++)
            {
                Graphic arm = _arms[i];
                if (arm == null) continue;
                arm.rectTransform.anchoredPosition = Directions[i] * _currentGap;
            }

            float ads = _weapon != null ? _weapon.AdsProgress : 0f;
            float alpha = Mathf.Lerp(_restAlpha, _adsAlpha, ads);
            if (_group != null && !Mathf.Approximately(_group.alpha, alpha)) _group.alpha = alpha;
        }
    }
}
