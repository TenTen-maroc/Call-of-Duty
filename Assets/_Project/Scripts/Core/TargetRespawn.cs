#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Grey-box shooting range behaviour: when the Health on this object dies,
    /// hide it, wait, reset, show it again. Without this a dead dummy stands in
    /// the room forever soaking bullets, and a tuning session runs out of
    /// targets after five magazines.
    ///
    /// Only hides renderers and colliders — the GameObject stays active so this
    /// component's own timer keeps running. Drones will NOT use this; they
    /// despawn through the pool.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public sealed class TargetRespawn : MonoBehaviour
    {
        [SerializeField] private HealthConfig? _config = null;
        [SerializeField] private Renderer[] _renderers = System.Array.Empty<Renderer>();
        [SerializeField] private Collider[] _colliders = System.Array.Empty<Collider>();

        private Health? _health;
        private float _respawnAt;
        private bool _down;

        private void Awake()
        {
            _health = GetComponent<Health>();
            // The builder wires these; the fallback keeps a hand-made target working.
            if (_renderers.Length == 0) _renderers = GetComponentsInChildren<Renderer>(true);
            if (_colliders.Length == 0) _colliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            if (_health != null) _health.Died += OnDied;
            // Pooled reuse: a target that despawned while down must come back up.
            if (_down) Show(true);
        }

        private void OnDisable()
        {
            if (_health != null) _health.Died -= OnDied;
        }

        private void OnDied(Health health, DamageInfo info)
        {
            _down = true;
            _respawnAt = Time.time + (_config != null ? _config.targetRespawnSeconds : 2.5f);
            Show(false);
        }

        private void Update()
        {
            if (!_down || Time.time < _respawnAt) return;
            Show(true);
        }

        private void Show(bool visible)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = visible;
            }
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null) _colliders[i].enabled = visible;
            }
            if (visible)
            {
                _down = false;
                if (_health != null) _health.ResetHealth();
            }
        }
    }
}
