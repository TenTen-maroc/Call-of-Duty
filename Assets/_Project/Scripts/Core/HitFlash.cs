#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Flashes a renderer when its Health takes damage. Half of "does this gun
    /// feel good" is what the world does back, and a target that does not react
    /// reads as a miss even when the hitmarker fired.
    ///
    /// Uses a MaterialPropertyBlock rather than touching renderer.material —
    /// the latter instantiates a fresh material per object at runtime, which
    /// leaks memory and silently breaks batching.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer? _renderer;
        [SerializeField] private Color _flashColor = new(1f, 0.28f, 0.2f, 1f);
        [Range(0.02f, 0.5f)][SerializeField] private float _flashDuration = 0.08f;

        private Health? _health;
        private MaterialPropertyBlock? _block;
        private Color _baseColor = Color.white;
        private float _flashUntil;

        private void Awake()
        {
            _health = GetComponent<Health>();
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
            if (_renderer != null && _renderer.sharedMaterial != null &&
                _renderer.sharedMaterial.HasProperty(BaseColorId))
            {
                _baseColor = _renderer.sharedMaterial.GetColor(BaseColorId);
            }
        }

        private void OnEnable()
        {
            if (_health != null) _health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
            _flashUntil = 0f;
            Apply(_baseColor);
        }

        private void OnDamaged(Health health, DamageInfo info) 
        {
            _flashUntil = Time.time + _flashDuration;
            Apply(_flashColor);
        }

        private void Update()
        {
            if (_flashUntil <= 0f || Time.time < _flashUntil) return;
            _flashUntil = 0f;
            Apply(_baseColor);
        }

        private void Apply(Color color)
        {
            if (_renderer == null || _block == null) return;
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
