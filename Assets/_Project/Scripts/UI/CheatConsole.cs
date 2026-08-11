#nullable enable
using CoD.Core;
using CoD.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoD.UI
{
    /// <summary>
    /// The in-game console. This is a FEATURE of Sandbox mode, not scaffolding —
    /// and it also happens to be the fastest way to test everything else in the
    /// game, which is why it is built this early rather than "when there's time".
    ///
    /// Compiled out entirely of a shipping build by the UNITY_EDITOR ||
    /// DEVELOPMENT_BUILD guard, so Run mode cannot be cheated by a player who
    /// found the key binding.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CheatConsole : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private GameConfig? _config = null;
        [SerializeField] private WeaponController? _weapon = null;
        [SerializeField] private Health? _playerHealth = null;
        [SerializeField] private ObjectPool? _pool = null;
        [Tooltip("Spawned by the 'spawn dummy' cheat. Must be registered in the pool.")]
        [SerializeField] private GameObject? _dummyTargetPrefab = null;
        [SerializeField] private Transform? _spawnOrigin = null;
        [SerializeField] private Key _toggleKey = Key.Backquote;

        private bool _open;
        private bool _godMode;
        private bool _slowMo;
        private float _baseFixedDeltaTime = 0.02f;

        private void Awake()
        {
            // The project's real physics step, whatever it is — never assume 0.02.
            _baseFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[_toggleKey].wasPressedThisFrame) Toggle();
            if (!_open) return;

            if (keyboard[Key.Digit1].wasPressedThisFrame) ToggleGodMode();
            if (keyboard[Key.Digit2].wasPressedThisFrame) ToggleInfiniteAmmo();
            if (keyboard[Key.Digit3].wasPressedThisFrame) ToggleSlowMo();
            if (keyboard[Key.Digit4].wasPressedThisFrame) SpawnDummy();
            if (keyboard[Key.Digit5].wasPressedThisFrame) CycleDamageMultiplier();
        }

        private void Toggle()
        {
            _open = !_open;
            GameLog.Info(_open ? "Cheat console OPEN" : "Cheat console closed", this);
        }

        private void ToggleGodMode()
        {
            _godMode = !_godMode;
            if (_playerHealth != null)
            {
                _playerHealth.Invulnerable = _godMode;
                _playerHealth.ResetHealth();
            }
            GameLog.Info("godmode: " + _godMode, this);
        }

        private void ToggleInfiniteAmmo()
        {
            if (_weapon == null) return;
            _weapon.InfiniteAmmo = !_weapon.InfiniteAmmo;
            GameLog.Info("infinite ammo: " + _weapon.InfiniteAmmo, this);
        }

        private void ToggleSlowMo()
        {
            _slowMo = !_slowMo;
            float scale = _slowMo && _config != null ? _config.slowMoTimeScale : 1f;
            Time.timeScale = scale;
            // Physics must follow the clock, or slow motion desyncs collision.
            Time.fixedDeltaTime = _baseFixedDeltaTime * scale;
            GameLog.Info("slow-mo: " + _slowMo, this);
        }

        private void SpawnDummy()
        {
            if (_pool == null || _dummyTargetPrefab == null || _spawnOrigin == null) return;
            Vector3 position = _spawnOrigin.position + _spawnOrigin.forward * 8f;
            _pool.Spawn(_dummyTargetPrefab, position, Quaternion.identity);
            GameLog.Info("spawned dummy target", this);
        }

        private void CycleDamageMultiplier()
        {
            if (_weapon == null) return;
            _weapon.DamageMultiplier = _weapon.DamageMultiplier >= 8f ? 1f : _weapon.DamageMultiplier * 2f;
            GameLog.Info("damage multiplier: " + _weapon.DamageMultiplier, this);
        }

        private void OnGUI()
        {
            if (!_open) return;

            // IMGUI is fine here: this panel only exists in dev builds, and it
            // costs nothing to maintain compared to a uGUI hierarchy.
            const float width = 260f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 190f), GUI.skin.box);
            GUILayout.Label("SANDBOX CONSOLE");
            GUILayout.Label("1  godmode          " + _godMode);
            GUILayout.Label("2  infinite ammo    " + (_weapon != null && _weapon.InfiniteAmmo));
            GUILayout.Label("3  slow-mo          " + _slowMo);
            GUILayout.Label("4  spawn dummy");
            GUILayout.Label("5  damage x         " + (_weapon != null ? _weapon.DamageMultiplier : 1f));
            GUILayout.EndArea();
        }

        /// <summary>Godmode is read by the player's damage path rather than blocking input here.</summary>
        public bool IsGodMode => _godMode;

        private void OnDisable()
        {
            // Never leave the game in slow motion because the console was closed
            // by a scene change.
            if (!_slowMo) return;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _baseFixedDeltaTime;
        }
#endif
    }
}
