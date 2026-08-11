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
        private int _placementFailuresThisWave;

        /// <summary>Consecutive failed placements before the runner says so. A hang guard's log threshold, not a tuning value.</summary>
        private const int PLACEMENT_FAILURE_WARN_AT = 30;

        /// <summary>And the point at which it stops trying. Same reasoning: a hang guard, not a knob.</summary>
        private const int PLACEMENT_FAILURE_ABANDON_AT = 120;

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
        /// <summary>The authored wave being fought, or null once the endless ramp has taken over.</summary>
        public WaveConfig? CurrentWave => ConfigForWave(_wave);
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
            if (_shopConfig != null && _run != null) _shop = new ShopService(_shopConfig, _run, _weapon, _playerHealth);
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
            BuildQueue(wave);

            // Every Wave_NN.asset serializes maxAliveOverride and, until now,
            // nothing anywhere read it — a designed per-wave knob that silently
            // did nothing. 0 still means "use DifficultyConfig".
            WaveConfig? shape = ConfigForWave(wave);
            _spawner?.SetAliveCapOverride(shape != null ? shape.maxAliveOverride : 0);
            _spawnedThisWave = 0;
            _placementFailuresThisWave = 0;
            SetPhase(RunPhase.Wave);
            WaveStarted?.Invoke(wave);

            // The round is banked only once the wave is real. SetWave used to run
            // before BuildQueue had said whether there was anything to fight, so
            // the recovery path below — which deliberately refuses to PAY for a
            // fight that never happened — still let that wave count toward the
            // best round written to disk.
            if (_plannedThisWave > 0) _run?.State.SetWave(wave);

            // A wave that planned nothing can never satisfy the clear condition —
            // the queue is already empty and nothing will ever have spawned — so
            // the run hangs in RunPhase.Wave forever, staring at an empty arena
            // with no drones, no shop and no way out but the pause menu. Reachable
            // from a WaveConfig whose entries all lost their drone reference, and
            // from an endless wave with no mix authored and no fallback drone on
            // the spawner. Mis-authored data must cost a log line, never the run.
            if (_plannedThisWave <= 0)
            {
                GameLog.Error(
                    $"Wave {wave} planned no drones — check the WaveConfig entries, the endless mix, " +
                    "and the spawner's default drone. Skipping it so the run can continue.", this);
                // SKIPPED, not cleared. A cleared wave pays moneyBonusOnClear and
                // counts toward the record, and a wave that is mis-authored is
                // mis-authored EVERY time it comes round — so paying for it turns
                // one bad asset into an unbounded money press that also inflates
                // the permanent best-round. Nothing was fought; nothing is owed.
                EndWave(payClearBonus: false);
            }
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
                if (_spawner.Spawn(task.Drone, scaling) == null)
                {
                    // Placement failed rather than the cap being full — no spawn
                    // point sampled onto the navmesh, or the prefab is missing.
                    // Back off to the entry's own interval instead of retrying
                    // every frame: the retry re-samples every spawn point against
                    // the navmesh, so a broken arena would burn real frame time
                    // silently, forever, while the wave never ends.
                    task.NextAt = now + Mathf.Max(0.5f, task.Interval);
                    _queue[i] = task;
                    _placementFailuresThisWave++;

                    if (_placementFailuresThisWave == PLACEMENT_FAILURE_WARN_AT)
                    {
                        GameLog.Warn(
                            $"Wave {_wave}: {_placementFailuresThisWave} spawns in a row could not be placed. " +
                            "Check the spawn points reach the navmesh and the drone prefabs are assigned.", this);
                    }
                    else if (_placementFailuresThisWave >= PLACEMENT_FAILURE_ABANDON_AT)
                    {
                        // Give up on the rest of the queue. Backing off stopped the
                        // busy-spin but not the HANG: an entry that can never be
                        // placed never decrements, so the queue never empties, the
                        // clear condition can never be met, and the run sits in
                        // RunPhase.Wave until the player quits — no timeout, no
                        // death, no way out. Dropping the remainder lets the wave
                        // finish on the drones that did make it, which is a bad
                        // wave rather than a dead session.
                        GameLog.Error(
                            $"Wave {_wave}: giving up on {RemainingQueued()} unplaceable spawns. " +
                            "The wave will end on whatever reached the arena.", this);
                        _queue.Clear();

                        // Ending the wave OUTRIGHT when not one drone was ever
                        // placed. Emptying the queue alone still left the run
                        // hanging: the clear condition below also requires
                        // _spawnedThisWave > 0, so an arena where nothing can be
                        // placed produced an empty queue, an empty arena, and a
                        // phase with no way out — the exact hang this whole branch
                        // exists to prevent. Nothing was fought, so nothing is paid.
                        if (_spawnedThisWave == 0) EndWave(payClearBonus: false);
                        return;
                    }
                    continue;
                }

                _placementFailuresThisWave = 0;
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

        /// <summary>Everything queued but not yet released. Used only for diagnostics.</summary>
        private int RemainingQueued()
        {
            int queued = 0;
            for (int i = 0; i < _queue.Count; i++) queued += _queue[i].Remaining;
            return queued;
        }

        private void ClearWave() => EndWave(payClearBonus: true);

        /// <summary>
        /// Moves the run out of the wave phase. The bonus is a parameter because
        /// the recovery paths — a wave that planned nothing, a wave whose spawns
        /// could not be placed — have to end the wave WITHOUT paying for a fight
        /// that never happened.
        /// </summary>
        private void EndWave(bool payClearBonus)
        {
            if (payClearBonus)
            {
                WaveConfig? config = ConfigForWave(_wave);
                int bonus = config != null
                    ? config.moneyBonusOnClear
                    : EndlessClearBonus(_wave);
                _run?.AddMoney(bonus);
            }

            _phaseEndsAt = Time.time + _clearedSeconds;
            SetPhase(RunPhase.Cleared);
            WaveCleared?.Invoke(_wave);
        }

        /// <summary>
        /// The clear bonus past the last authored wave. Read from DifficultyConfig
        /// rather than a formula in this file: it is the entire late-game economy,
        /// and it used to be `100 + wave * 10` in code — untunable, and a cliff at
        /// wave 11, where the payout dropped below wave 10's authored bonus while
        /// enemy count, health and shop prices all kept climbing.
        /// </summary>
        private int EndlessClearBonus(int wave)
        {
            if (_difficulty == null) return 0;
            return Mathf.Max(0, _difficulty.endlessClearBonusBase
                                + _difficulty.endlessClearBonusPerWave * wave);
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

            int baseCount = Mathf.Max(1, _difficulty.endlessFallbackWaveSize);
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
                if (fallback != null) Enqueue(fallback, total, _difficulty.endlessSpawnOverSeconds, 0f);
                return;
            }

            for (int i = 0; i < _difficulty.endlessMix.Length; i++)
            {
                DifficultyConfig.MixEntry mix = _difficulty.endlessMix[i];
                if (mix.drone == null || mix.weightByWave == null) continue;
                float weight = Mathf.Max(0f, mix.weightByWave.Evaluate(wave));
                if (weight <= 0f) continue;
                int count = Mathf.RoundToInt(total * (weight / weightSum));
                if (count > 0) Enqueue(mix.drone, count, _difficulty.endlessSpawnOverSeconds, 0f);
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
