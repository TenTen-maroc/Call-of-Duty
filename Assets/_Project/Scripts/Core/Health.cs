#nullable enable
using System;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Current health — state, not settings. The maximum comes from a config
    /// asset; this component only ever holds what is true right now.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private HealthConfig? _config;

        private float _current;

        /// <summary>Instance event, not static — a static one would keep last Play session's subscribers.</summary>
        public event Action<Health, DamageInfo>? Damaged;
        public event Action<Health, DamageInfo>? Died;

        public bool IsAlive => _current > 0f;
        public float Current => _current;
        public float Max => _config != null ? _config.maxHealth : 100f;
        public float Normalized => Max <= 0f ? 0f : Mathf.Clamp01(_current / Max);

        private void Awake() => ResetHealth();

        private void OnEnable() => ResetHealth();

        public void ResetHealth() => _current = Max;

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive) return 0f;

            float multiplier = info.IsWeakpoint && _config != null ? _config.weakpointMultiplier : 1f;
            float requested = info.Amount * multiplier;
            float applied = Mathf.Min(requested, _current);
            _current -= applied;

            Damaged?.Invoke(this, info);
            if (!IsAlive) Died?.Invoke(this, info);
            return applied;
        }
    }
}
