#nullable enable
using CoD.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// A small centre-screen X plus a click. This single element does more for
    /// gun feel than any amount of weapon polish — it is the game confirming the
    /// player's aim, and without it every shot feels like a maybe.
    ///
    /// The kill variant is deliberately different: lower pitched, longer, and a
    /// different colour. Players learn it in seconds and it makes clearing a
    /// wave legible without a single UI number.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hitmarker : MonoBehaviour
    {
        [SerializeField] private WeaponController? _weapon = null;
        [Tooltip("The bars that form the X. Driven as one - built from primitives so the project ships no sprite binary.")]
        [SerializeField] private Graphic[] _markerParts = System.Array.Empty<Graphic>();
        [SerializeField] private AudioSource? _audio = null;

        [Header("Hit")]
        [SerializeField] private Color _hitColor = new(1f, 1f, 1f, 0.9f);
        [SerializeField] private AudioClip? _hitClip = null;
        [Range(0.02f, 0.5f)][SerializeField] private float _hitDuration = 0.09f;

        [Header("Kill")]
        [SerializeField] private Color _killColor = new(1f, 0.35f, 0.25f, 1f);
        [SerializeField] private AudioClip? _killClip = null;
        [Range(0.05f, 0.8f)][SerializeField] private float _killDuration = 0.22f;

        [Tooltip("Scale punch at the moment of the hit, easing back to 1.")]
        [SerializeField] private float _punchScale = 1.35f;

        private Transform? _markerTransform;
        [SerializeField] private Transform? _markerRoot = null;
        private float _visibleUntil;
        private float _duration = 0.1f;
        private bool _showingKill;

        private void Awake()
        {
            _markerTransform = _markerRoot;
            SetAlpha(0f);
        }

        private void OnEnable()
        {
            if (_weapon != null) _weapon.Hit += OnHit;
        }

        private void OnDisable()
        {
            if (_weapon != null) _weapon.Hit -= OnHit;
        }

        private void OnHit(bool killed)
        {
            if (_markerParts.Length == 0) return;

            // A shotgun resolves several pellets in one frame: never let a plain
            // hit pellet overwrite the kill confirmation from a sibling pellet.
            if (!killed && _showingKill && Time.time < _visibleUntil) return;
            _showingKill = killed;

            _duration = killed ? _killDuration : _hitDuration;
            _visibleUntil = Time.time + _duration;
            SetColor(killed ? _killColor : _hitColor);

            AudioClip? clip = killed ? _killClip : _hitClip;
            if (_audio != null && clip != null) _audio.PlayOneShot(clip);
        }

        private void Update()
        {
            if (_markerParts.Length == 0 || _markerTransform == null) return;

            float remaining = _visibleUntil - Time.time;
            if (remaining <= 0f)
            {
                // Guarded like SetAlpha and SetColor are. An unassigned element 0
                // — a hand-built canvas, a part deleted from the prefab — threw a
                // NullReferenceException here EVERY frame, while the two writers
                // beside it degraded quietly.
                Graphic first = _markerParts[0];
                if (first != null && first.color.a > 0f) SetAlpha(0f);
                return;
            }

            float t = Mathf.Clamp01(remaining / Mathf.Max(0.01f, _duration));
            SetAlpha(t);
            // Punch out on the first frame and settle back - the movement is what
            // the eye actually catches at these durations.
            _markerTransform.localScale = Vector3.one * Mathf.Lerp(1f, _punchScale, t * t);
        }

        private void SetAlpha(float alpha)
        {
            for (int i = 0; i < _markerParts.Length; i++)
            {
                Graphic part = _markerParts[i];
                if (part == null) continue;
                Color c = part.color;
                c.a = alpha;
                part.color = c;
            }
            if (alpha <= 0f && _markerTransform != null) _markerTransform.localScale = Vector3.one;
        }

        private void SetColor(Color color)
        {
            for (int i = 0; i < _markerParts.Length; i++)
            {
                if (_markerParts[i] != null) _markerParts[i].color = color;
            }
        }
    }
}
