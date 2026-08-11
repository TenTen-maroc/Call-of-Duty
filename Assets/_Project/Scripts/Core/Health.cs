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

        /// <summary>
        /// Changes the maximum WITHOUT refilling — what a mid-run upgrade needs.
        /// Current health moves by the same amount the maximum did, so a +25 max
        /// upgrade reads as 125/125 rather than the broken-looking 100/125, and a
        /// reduction clamps instead of leaving current above max.
        ///
        /// Split out from ConfigureMax because the player and a pooled drone want
        /// opposite things from one call. RunContext.ApplyStats runs on EVERY
        /// purchase, not just health ones, so routing it through ConfigureMax made
        /// the cheapest passive in the shop a full heal: buy a reload upgrade at
        /// 8 HP and walk into the next wave topped up. Drones still want the
        /// reset — a pooled instance must not inherit the last one's damage.
        /// </summary>
        public void AdjustMax(float max)
        {
            float next = Mathf.Max(1f, max);
            float delta = next - Max;
            _maxOverride = next;
            _current = delta > 0f ? _current + delta : Mathf.Min(_current, next);
        }

        /// <summary>
        /// Restores health, never past the maximum and never to the dead.
        ///
        /// Returns what was ACTUALLY restored, which is the part that matters: a
        /// caller can then refuse to charge for a heal that did nothing, and that
        /// is the whole difference between a shop item and a scam. The HUD polls
        /// Current every frame, so there is no event to raise here.
        /// </summary>
        public float Heal(float amount)
        {
            if (amount <= 0f || !IsAlive) return 0f;
            float before = _current;
            _current = Mathf.Min(Max, _current + amount);
            return _current - before;
        }

        public float ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || Invulnerable) return 0f;

            // info.Amount already includes the weakpoint bonus: the WEAPON owns
            // the headshot multiplier (WeaponConfig.headshotMultiplier), so the
            // same number is never applied twice. IsWeakpoint stays on the info
            // purely for feedback — distinct hit sounds and markers later.
            // Clamped at BOTH ends. The upper clamp stops overkill reporting more
            // damage than the target had; the lower one stops a negative amount —
            // a falloff curve authored backwards, a passive multiplier gone below
            // zero — from silently HEALING the target past its own maximum and
            // making a drone unkillable while every hitmarker still fires.
            float applied = Mathf.Clamp(info.Amount, 0f, _current);
            _current -= applied;

            Damaged?.Invoke(this, info);
            if (!IsAlive) Died?.Invoke(this, info);
            return applied;
        }
    }
}
