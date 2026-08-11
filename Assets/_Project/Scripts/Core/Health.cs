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
        [SerializeField] private HealthConfig? _config = null;
        [Tooltip("Player only. When set, max health comes from GameConfig.playerMaxHealth — the one asset that owns global player numbers.")]
        [SerializeField] private GameConfig? _playerConfig = null;

        private float _current;
        /// <summary>Set from DroneConfig at spawn. Negative means "no override".</summary>
        private float _maxOverride = -1f;

        /// <summary>Instance event, not static — a static one would keep last Play session's subscribers.</summary>
        public event Action<Health, DamageInfo>? Damaged;
        public event Action<Health, DamageInfo>? Died;

        public bool IsAlive => _current > 0f;
        public float Current => _current;
        public float Max => _maxOverride > 0f ? _maxOverride
            : _playerConfig != null ? _playerConfig.playerMaxHealth
            : _config != null ? _config.maxHealth : 100f;
        public float Normalized => Max <= 0f ? 0f : Mathf.Clamp01(_current / Max);

        /// <summary>Godmode. State flipped by the sandbox console, never saved.</summary>
        public bool Invulnerable { get; set; }

        private void Awake() => ResetHealth();

        private void OnEnable() => ResetHealth();

        public void ResetHealth() => _current = Max;

        /// <summary>
        /// Drones carry their max HP on their own DroneConfig, not on a shared
        /// HealthConfig asset — one archetype, one source of truth. The spawner
        /// calls this right after the pool hands the instance over, which also
        /// re-fills the bar for the reused instance.
        /// </summary>
        public void ConfigureMax(float max)
        {
            _maxOverride = Mathf.Max(1f, max);
            ResetHealth();
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || Invulnerable) return 0f;

            // info.Amount already includes the weakpoint bonus: the WEAPON owns
            // the headshot multiplier (WeaponConfig.headshotMultiplier), so the
            // same number is never applied twice. IsWeakpoint stays on the info
            // purely for feedback — distinct hit sounds and markers later.
            float applied = Mathf.Min(info.Amount, _current);
            _current -= applied;

            Damaged?.Invoke(this, info);
            if (!IsAlive) Died?.Invoke(this, info);
            return applied;
        }
    }
}
