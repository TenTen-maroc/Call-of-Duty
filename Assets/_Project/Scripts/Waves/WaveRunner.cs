#nullable enable
using System;
using System.Collections.Generic;
using CoD.Core;
using CoD.Enemies;
using CoD.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoD.Waves
{
    public enum RunPhase
    {
        /// <summary>Between the run starting and the first wave. Also the pause after a clear.</summary>
        Countdown,
        /// <summary>Drones are being released and fought.</summary>
        Wave,
        /// <summary>Wave cleared, bonus paid, brief breath before the shop.</summary>
        Cleared,
        /// <summary>Shop is open. The player continues when they are ready.</summary>
        Shop,
        /// <summary>Permadeath. The run is over and recorded.</summary>
        GameOver,
    }

    /// <summary>
    /// The game loop: timed wave, clear it, shop, next one, until you die. Owns
    /// the spawn queue, the attack-token pool and the shop, and is the only place
    /// that decides what phase the run is in.
    ///
    /// Everything the UI needs is exposed as events and properties — the runner
    /// never touches a Text field, and the HUD never drives the loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaveRunner : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RunContext? _run = null;
        [SerializeField] private DroneSpawner? _spawner = null;
        [SerializeField] private DroneRegistry? _registry = null;
        [SerializeField] private DifficultyConfig? _difficulty = null;
        [SerializeField] private ShopConfig? _shopConfig = null;
        [Tooltip("The player's Health. Its death ends the run — that is permadeath.")]
        [SerializeField] private Health? _playerHealth = null;
        [Tooltip("Where shop-bought effect modules are installed.")]
        [SerializeField] private WeaponController? _weapon = null;

        [Header("Waves")]
        [Tooltip("Hand-authored waves, in order. Past the last one the endless ramp takes over.")]
        [SerializeField] private WaveConfig[] _waves = Array.Empty<WaveConfig>();

        [Header("Pacing (seconds)")]
        [Tooltip("Breath before the first wave and between a clear and the shop.")]
        [Range(0f, 15f)] [SerializeField] private float _countdownSeconds = 4f;
        [Range(0f, 10f)] [SerializeField] private float _clearedSeconds = 2.5f;

        private readonly List<SpawnTask> _queue = new(8);
        private AttackTokenPool? _tokens;
        private ShopService? _shop;
        private float _phaseEndsAt;
        private int _wave;
        private int _spawnedThisWave;
        private int _plannedThisWave;

        private struct SpawnTask
        {
            public DroneConfig Drone;
            public int Remaining;
            public float NextAt;
            public float Interval;
        }

        public event Action<RunPhase>? PhaseChanged;
        public event Action<int>? WaveStarted;
        public event Action<int>? WaveCleared;
        public event Action? RunEnded;

        public RunPhase Phase { get; private set; } = RunPhase.Countdown;
        public int WaveNumber => _wave;
        public ShopService? Shop => _shop;
        public AttackTokenPool? Tokens => _tokens;
        public float PhaseTimeRemaining => Mathf.Max(0f, _phaseEndsAt - Time.time);

        /// <summary>Queued plus alive. What the HUD counts down.</summary>
        public int EnemiesRemaining
        {
            get
            {
                int queued = 0;
                for (int i = 0; i < _queue.Count; i++) queued += _queue[i].Remaining;
                return queued + (_registry != null ? _registry.AliveCount : 0);
            }
        }

        private void Awake()
        {
            if (_difficulty != null)
            {
                _tokens = new AttackTokenPool(_difficulty);
                // The drones were built against an interface for exactly this
                // moment: the real cap replaces the always-grant stub without the
                // drone code knowing either exists.
                if (_spawner != null) _spawner.SetTokenSource(_tokens);
            }
            if (_shopConfig != null && _run != null) _shop = new ShopService(_shopConfig, _run, _weapon);
        }

        private void OnEnable()
        {
            if (_registry != null) _registry.Killed += OnDroneKilled;
            if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
        }

        private void OnDisable()
        {
            if (_registry != null) _registry.Killed -= OnDroneKilled;
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
        }

        private void Start()
        {
            if (_run != null && _shopConfig != null)
            {
                // Sandbox is "everything unlocked". In a game whose whole
                // progression is a shop, that IS money — a parallel inventory
                // system would be a second way to own a module and a second thing
                // to keep in sync with the shop.
                _run.BeginRun(_run.Mode == GameMode.Sandbox
                    ? _shopConfig.sandboxStartingMoney
                    : _shopConfig.startingMoney);
            }
            EnterCountdown();
        }

        private void Update()
        {
            float now = Time.time;
            _tokens?.Tick(now);

            switch (Phase)
            {
                case RunPhase.Countdown:
                    if (now >= _phaseEndsAt) StartWave(_wave + 1);
                    break;
                case RunPhase.Wave:
                    TickWave(now);
                    break;
                case RunPhase.Cleared:
                    if (now >= _phaseEndsAt) EnterShop();
                    break;
            }
        }

        // ---------- phases ----------

        private void EnterCountdown()
        {
            _phaseEndsAt = Time.time + _countdownSeconds;
            SetPhase(RunPhase.Countdown);
        }

        private void StartWave(int wave)
        {
            _wave = wave;
            _run?.State.SetWave(wave);
            BuildQueue(wave);
            _spawnedThisWave = 0;
            SetPhase(RunPhase.Wave);
            WaveStarted?.Invoke(wave);
        }

        private void TickWave(float now)
        {
            if (_spawner == null) return;

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                SpawnTask task = _queue[i];
                if (now < task.NextAt) continue;
                if (!_spawner.CanSpawn()) continue;   // at the alive cap: the queue waits for deaths

                WaveScaling scaling = _difficulty != null ? _difficulty.ScalingForWave(_wave) : WaveScaling.None;
                if (_spawner.Spawn(task.Drone, scaling) == null) continue;

                _spawnedThisWave++;
                task.Remaining--;
                task.NextAt = now + task.Interval;
                // Structs in a List are copies — writing back is what makes the
                // decrement stick.
                if (task.Remaining <= 0) _queue.RemoveAt(i);
                else _queue[i] = task;
            }

            if (_queue.Count == 0 && _registry != null && _registry.AliveCount == 0 && _spawnedThisWave > 0)
            {
                ClearWave();
            }
        }

        private void ClearWave()
        {
            WaveConfig? config = ConfigForWave(_wave);
            int bonus = config != null ? config.moneyBonusOnClear : 100 + _wave * 10;
            _run?.AddMoney(bonus);

            _phaseEndsAt = Time.time + _clearedSeconds;
            SetPhase(RunPhase.Cleared);
            WaveCleared?.Invoke(_wave);
        }

        private void EnterShop()
        {
            _shop?.OpenBreak(_wave + 1);
            SetPhase(RunPhase.Shop);
        }

        /// <summary>Called by the shop UI when the player is done buying.</summary>
        public void ContinueFromShop()
        {
            if (Phase != RunPhase.Shop) return;
            EnterCountdown();
        }

        private void OnPlayerDied(Health health, DamageInfo info)
        {
            if (Phase == RunPhase.GameOver) return;

            // Clear the arena first: a game-over screen with drones still chewing
            // on the corpse behind it reads as a crash.
            _registry?.DespawnAll();
            _queue.Clear();
            _tokens?.Clear();

            _run?.RecordRunEnded();
            SetPhase(RunPhase.GameOver);
            RunEnded?.Invoke();
        }

        private void OnDroneKilled(DroneController drone, DamageInfo info)
        {
            if (_run == null) return;
            _run.State.AddKill();
            DroneConfig? config = drone.Config;
            if (config != null) _run.AddMoney(config.moneyReward);
        }

        private void SetPhase(RunPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        // ---------- wave contents ----------

        private WaveConfig? ConfigForWave(int wave)
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                if (_waves[i] != null && _waves[i].waveNumber == wave) return _waves[i];
            }
            return null;
        }

        private void BuildQueue(int wave)
        {
            _queue.Clear();
            _plannedThisWave = 0;

            WaveConfig? config = ConfigForWave(wave);
            if (config != null)
            {
                for (int i = 0; i < config.entries.Length; i++)
                {
                    WaveConfig.Entry entry = config.entries[i];
                    if (entry.drone == null) continue;
                    Enqueue(entry.drone, entry.count, entry.spawnOverSeconds, entry.startDelay);
                }
                return;
            }

            BuildEndlessQueue(wave);
        }

        /// <summary>
        /// Past the last authored wave the curves take over: the last wave's size
        /// scaled by countMultiplierByWave, split across the archetypes by their
        /// mix weights. Health and speed scaling ride along on WaveScaling, which
        /// is applied per spawn and never written into a config.
        /// </summary>
        private void BuildEndlessQueue(int wave)
        {
            if (_difficulty == null) return;

            int baseCount = 8;
            WaveConfig? last = _waves.Length > 0 ? _waves[_waves.Length - 1] : null;
            if (last != null) baseCount = Mathf.Max(1, last.TotalCount);

            float multiplier = Mathf.Max(1f, _difficulty.countMultiplierByWave.Evaluate(wave));
            int total = Mathf.Clamp(Mathf.RoundToInt(baseCount * multiplier), 1, _difficulty.maxAliveDrones * 3);

            float weightSum = 0f;
            for (int i = 0; i < _difficulty.endlessMix.Length; i++)
            {
                DifficultyConfig.MixEntry mix = _difficulty.endlessMix[i];
                if (mix.drone == null || mix.weightByWave == null) continue;
                weightSum += Mathf.Max(0f, mix.weightByWave.Evaluate(wave));
            }

            if (weightSum <= 0f)
            {
                // No mix authored yet: fall back to the spawner's default drone so
                // an endless wave is never an empty one.
                DroneConfig? fallback = _spawner != null ? _spawner.DefaultDrone : null;
                if (fallback != null) Enqueue(fallback, total, 20f, 0f);
                return;
            }

            for (int i = 0; i < _difficulty.endlessMix.Length; i++)
            {
                DifficultyConfig.MixEntry mix = _difficulty.endlessMix[i];
                if (mix.drone == null || mix.weightByWave == null) continue;
                float weight = Mathf.Max(0f, mix.weightByWave.Evaluate(wave));
                if (weight <= 0f) continue;
                int count = Mathf.RoundToInt(total * (weight / weightSum));
                if (count > 0) Enqueue(mix.drone, count, 20f, 0f);
            }
        }

        private void Enqueue(DroneConfig drone, int count, float overSeconds, float startDelay)
        {
            if (count <= 0) return;
            _plannedThisWave += count;
            _queue.Add(new SpawnTask
            {
                Drone = drone,
                Remaining = count,
                NextAt = Time.time + startDelay,
                Interval = count > 0 ? Mathf.Max(0f, overSeconds) / count : 0f,
            });
        }

        // ---------- sandbox ----------

        /// <summary>Cheat: end the current wave immediately and move on.</summary>
        public void SkipWave()
        {
            if (Phase != RunPhase.Wave) return;
            _queue.Clear();
            _registry?.DespawnAll();
            ClearWave();
        }

        /// <summary>Restarts the run by reloading the scene — the cheapest correct reset there is.</summary>
        public void RestartRun() => SceneManager.LoadScene(gameObject.scene.name);
    }
}
