#nullable enable
using CoD.Core;
using CoD.Enemies;
using CoD.Waves;
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
        [Tooltip("Every weapon the game ships. Digit 0 walks it. Without this the arsenal is authored, gated, tested and unreachable — only the rifle you start with and the SMG the shop sells can ever be held.")]
        [SerializeField] private WeaponRegistry? _weaponRegistry = null;
        [Tooltip("Every attachment on disk, scanned by the builder. MINUS fits the next one that suits the held weapon. Same argument as the registry above: four of the five are authored and fitted to nothing, so without this nobody can feel one.")]
        [SerializeField] private AttachmentConfig[] _attachments = System.Array.Empty<AttachmentConfig>();
        [SerializeField] private Health? _playerHealth = null;
        [SerializeField] private ObjectPool? _pool = null;
        [Tooltip("Spawned by the 'spawn dummy' cheat. Must be registered in the pool.")]
        [SerializeField] private GameObject? _dummyTargetPrefab = null;
        [SerializeField] private Transform? _spawnOrigin = null;
        [Tooltip("Sandbox drone spawning. The fastest way to test the horde without waiting for a wave.")]
        [SerializeField] private DroneSpawner? _droneSpawner = null;
        [SerializeField] private DroneRegistry? _droneRegistry = null;
        [Tooltip("How many drones the spawn cheat releases at once.")]
        [SerializeField] private int _droneBurstSize = 3;
        [Tooltip("Wave control and the free-money cheat. Also surfaces the live cap counters.")]
        [SerializeField] private WaveRunner? _waveRunner = null;
        [SerializeField] private RunContext? _run = null;
        [SerializeField] private Key _toggleKey = Key.Backquote;
        [Tooltip("Optional. The console ignores every key while the game is paused; slow-mo and pause both own Time.timeScale.")]
        [SerializeField] private PausePanel? _pause = null;

        private bool _open;
        private bool _godMode;
        private bool _slowMo;
        private float _baseFixedDeltaTime = 0.02f;

        private void Awake()
        {
            // The project's real physics step, whatever it is — never assume 0.02.
            _baseFixedDeltaTime = Time.fixedDeltaTime;

#if !UNITY_EDITOR
            // DEVELOPMENT_BUILD only, since the #if above already removed this
            // whole component from a shipping build. There the console is a
            // SANDBOX feature and a Run must not have it.
            //
            // The editor is deliberately exempt: pressing Play straight into
            // 10_GreyBox is how this project is tuned, and gating the console on
            // whichever mode was last picked in the menu would break that
            // workflow for no safety gain — an editor session can already edit
            // every asset in the game.
            if (_run != null && _run.Mode != GameMode.Sandbox)
            {
                enabled = false;
                GameLog.Info("Cheat console disabled: this is a Run, not Sandbox.", this);
            }
#endif
        }

        private void Update()
        {
            if (_pause != null && _pause.IsPaused) return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[_toggleKey].wasPressedThisFrame) Toggle();
            if (!_open) return;

            if (keyboard[Key.Digit1].wasPressedThisFrame) ToggleGodMode();
            if (keyboard[Key.Digit2].wasPressedThisFrame) ToggleInfiniteAmmo();
            if (keyboard[Key.Digit3].wasPressedThisFrame) ToggleSlowMo();
            if (keyboard[Key.Digit4].wasPressedThisFrame) SpawnDummy();
            if (keyboard[Key.Digit5].wasPressedThisFrame) CycleDamageMultiplier();
            if (keyboard[Key.Digit6].wasPressedThisFrame) SpawnDrones();
            if (keyboard[Key.Digit7].wasPressedThisFrame) DespawnDrones();
            if (keyboard[Key.Digit8].wasPressedThisFrame) SkipWave();
            if (keyboard[Key.Digit9].wasPressedThisFrame) GiveMoney();
            if (keyboard[Key.Digit0].wasPressedThisFrame) CycleWeapon();
            if (keyboard[Key.Minus].wasPressedThisFrame) FitNextAttachment();
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

        private void SpawnDrones()
        {
            if (_droneSpawner == null) return;
            DroneConfig? config = _droneSpawner.DefaultDrone;
            if (config == null)
            {
                GameLog.Warn("No default drone assigned on the spawner.", this);
                return;
            }
            int spawned = _droneSpawner.SpawnBurst(config, _droneBurstSize);
            GameLog.Info($"spawned {spawned} x {config.displayName} (alive {_droneSpawner.AliveCount})", this);
        }

        private void DespawnDrones()
        {
            if (_droneRegistry == null) return;
            int before = _droneRegistry.AliveCount;
            _droneRegistry.DespawnAll();
            GameLog.Info($"despawned {before} drones", this);
        }

        private void SkipWave()
        {
            if (_waveRunner == null) return;
            _waveRunner.SkipWave();
            GameLog.Info("skipped to the shop", this);
        }

        private void GiveMoney()
        {
            if (_run == null) return;
            _run.AddMoney(1000);
            GameLog.Info("money: " + _run.State.Money, this);
        }

        /// <summary>
        /// Hands over the next weapon in the registry, wrapping at the end.
        ///
        /// WHY THIS IS A FEATURE AND NOT A CONVENIENCE. The arsenal is eight
        /// weapons; the game could only ever put TWO of them in the player's
        /// hands — the rifle the loadout starts with, and the SMG the shop sells.
        /// The pistol, the marksman rifle, the LMG, the shotgun and now the
        /// launcher were authored, balance-gated, covered by tests, and
        /// unreachable by any human being. A weapon nobody can hold cannot be
        /// judged, which is the one thing this project still needs from a person.
        ///
        /// It goes through EquipWeapon, the same call the shop makes, so bought
        /// effect modules carry across exactly as they do in a real run — and so
        /// this cheat exercises the shipping path rather than a private one.
        /// </summary>
        private void CycleWeapon()
        {
            if (_weapon == null || _weaponRegistry == null) return;

            WeaponConfig[] all = _weaponRegistry.allWeapons;
            if (all.Length == 0)
            {
                GameLog.Warn("The weapon registry is empty — run CoD -> Build Arsenal.", this);
                return;
            }

            // Where to start looking: one past whatever is held. A weapon that is
            // not in the registry (nothing ships like that, but a test can build
            // one) simply starts the walk at zero.
            WeaponConfig? held = _weapon.Runtime?.Config;
            int start = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != held) continue;
                start = i + 1;
                break;
            }

            // Walks past nulls rather than stopping on one: an empty slot is the
            // residue of a deleted asset, and it must not be able to jam the cycle
            // on the weapon before it.
            for (int step = 0; step < all.Length; step++)
            {
                WeaponConfig next = all[(start + step) % all.Length];
                if (next == null) continue;
                _weapon.EquipWeapon(next);
                return;
            }

            GameLog.Warn("Every entry in the weapon registry is empty.", this);
        }

        /// <summary>
        /// Fits the next attachment that suits the weapon in hand, wrapping.
        ///
        /// Walks from just past the last one fitted rather than from zero, so
        /// pressing MINUS repeatedly works through the set instead of toggling
        /// the same slot forever. `TryFit` refuses an attachment that does not
        /// belong to the weapon's class, so the long scope is silently skipped on
        /// everything but the sniper — which is the class rule doing its job, not
        /// a failure worth logging on a cheat key.
        ///
        /// It goes through the runtime's own `TryFit`, the same call a shop would
        /// make, so this exercises the shipping path.
        /// </summary>
        private void FitNextAttachment()
        {
            if (_weapon?.Runtime == null || _attachments.Length == 0) return;

            for (int step = 1; step <= _attachments.Length; step++)
            {
                AttachmentConfig? next = _attachments[(_lastAttachment + step) % _attachments.Length];
                if (next == null || !_weapon.Runtime.TryFit(next)) continue;

                _lastAttachment = (_lastAttachment + step) % _attachments.Length;
                GameLog.Info($"fitted {next.displayName} ({next.slot})", this);
                return;
            }

            GameLog.Info("nothing left that fits this weapon", this);
        }

        /// <summary>Where the attachment walk resumes. Instance state, never static — Domain Reload is off.</summary>
        private int _lastAttachment = -1;

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
            GUILayout.BeginArea(new Rect(12f, 12f, width, 320f), GUI.skin.box);
            GUILayout.Label("SANDBOX CONSOLE");
            GUILayout.Label("1  godmode          " + _godMode);
            GUILayout.Label("2  infinite ammo    " + (_weapon != null && _weapon.InfiniteAmmo));
            GUILayout.Label("3  slow-mo          " + _slowMo);
            GUILayout.Label("4  spawn dummy");
            GUILayout.Label("5  damage x         " + (_weapon != null ? _weapon.DamageMultiplier : 1f));
            GUILayout.Label("6  spawn " + _droneBurstSize + " drones");
            GUILayout.Label("7  clear drones     " + (_droneRegistry != null ? _droneRegistry.AliveCount : 0) + " alive");
            GUILayout.Label("8  skip wave        " + (_waveRunner != null ? _waveRunner.Phase.ToString() : "-"));
            GUILayout.Label("9  +1000 money      " + (_run != null ? _run.State.Money : 0));
            GUILayout.Label("0  next weapon      " +
                (_weapon?.Runtime != null ? _weapon.Runtime.Config.displayName : "-"));
            GUILayout.Label("-  fit attachment   " +
                (_weapon?.Runtime != null ? _weapon.Runtime.Attachments.Count + " fitted" : "-"));
            // The two caps, live. Both are the kind of rule that silently stops
            // working: this is how you SEE that 40-alive and 3-attackers hold.
            GUILayout.Label("alive / cap        " + (_droneRegistry != null ? _droneRegistry.AliveCount : 0));
            GUILayout.Label("attacking / cap    " +
                (_waveRunner != null && _waveRunner.Tokens != null
                    ? _waveRunner.Tokens.Held + " / " + _waveRunner.Tokens.Capacity
                    : "-"));
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
