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

        /// <summary>
        /// Whether the player's death is the end of the run.
        ///
        /// True everywhere except campaign, where a death is a rewind to the last
        /// checkpoint and a director wants to hear about it rather than have the
        /// loop close itself. Run state, not a setting: it is flipped at runtime by
        /// SetDeathEndsRun and must never become a serialized field — GreyBoxVerify
        /// can only Check an object reference, so a serialized bool is a scene knob
        /// nothing proves. The ABSENCE of a director is the endless configuration.
        /// </summary>
        private bool _deathEndsRun = true;

        /// <summary>When Suspend() stopped the clock, so Resume() can give back exactly what it took.</summary>
        private float _suspendedAt;

        /// <summary>
        /// Multiplies the NEXT clear bonus. Set by walking out of a shop break
        /// without buying, consumed the moment a clear actually pays.
        ///
        /// Run state, not config: it changes while the game is running, so it must
        /// never live on a ScriptableObject. Domain Reload is off, and a runtime
        /// write to a config survives into the next Play session.
        /// </summary>
        private float _pendingClearMultiplier = 1f;

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

        /// <summary>
        /// The run is over, and how. It carried no payload while dying was the only
        /// way a run could end; a mission that was completed, failed by its own
        /// rules, or walked away from wants a different screen than a corpse does.
        /// </summary>
        public event Action<RunOutcome>? RunEnded;

        /// <summary>
        /// The player died and the run did NOT end. Raised only while
        /// SetDeathEndsRun(false) is in force, so a director can rewind to a
        /// checkpoint. Unreachable in endless mode, where death is the ending.
        /// </summary>
        public event Action? PlayerDown;

        public RunPhase Phase { get; private set; } = RunPhase.Countdown;

        /// <summary>
        /// How the run ended. Meaningful once Phase is GameOver, and Died by
        /// default because that is the only ending endless mode has — so every
        /// reader is already correct with no director in the scene.
        /// </summary>
        public RunOutcome Outcome { get; private set; } = RunOutcome.Died;

        /// <summary>
        /// The loop is held. Update does nothing while this is true, but nothing is
        /// discarded either: the spawn queue, the drones already in the arena and
        /// the attack tokens are all exactly where Suspend() left them.
        /// </summary>
        public bool Suspended { get; private set; }

        /// <summary>
        /// True once anything has ever suspended this runner. One-way: it marks
        /// that a director is driving, which outlives any particular suspend.
        /// </summary>
        private bool _directorOwned;
        public int WaveNumber => _wave;

        /// <summary>
        /// The wave the loop will fight NEXT, which is not always WaveNumber + 1.
        ///
        /// WaveNumber means "the last wave that started". Mid-fight that is the
        /// wave in progress; in Countdown, Cleared and Shop it is the one already
        /// behind you. So "which wave comes next" is the current one during a
        /// wave and the following one in every other phase, and that distinction
        /// is invisible until something tries to write it down.
        ///
        /// Something does. A campaign checkpoint records a wave number and
        /// StartFrom replays it, and StartFrom's contract is stated in terms of
        /// the wave FOUGHT — so a checkpoint taken with WaveNumber sends the
        /// player back one wave every time it is taken between waves. Having the
        /// runner answer the question itself is what stops each caller deriving
        /// it, differently, from a phase it has to remember to check.
        ///
        /// GameOver answers WaveNumber + 1 like any other non-wave phase. There
        /// is no next wave from a finished run, and no caller asks.
        /// </summary>
        public int NextWaveNumber => Phase == RunPhase.Wave ? _wave : _wave + 1;

        public ShopService? Shop => _shop;
        /// <summary>What the next clear will pay, as a multiplier. 1 unless the player skipped a break.</summary>
        public float PendingClearMultiplier => _pendingClearMultiplier;
        /// <summary>What skipping WOULD pay, for the shop to offer. 1 means skipping is not worth showing.</summary>
        public float SkipBonusMultiplier => _shopConfig != null
            ? Mathf.Max(1f, _shopConfig.skipBonusMultiplier)
            : 1f;
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
            // THE ORDERING GUARANTEE THIS WHOLE SEAM HANGS ON.
            //
            // Unity does not promise Awake ORDER between components, but it does
            // promise that every Awake has completed before any Start runs, for
            // objects present when the scene loads. A MissionDirector calls
            // Suspend() from its Awake; by the time this Start executes the flag is
            // therefore already set, with no race and no execution-order attribute
            // to keep in sync.
            //
            // Returning here means that in campaign the runner does not begin a
            // run, does not open a countdown and does not spawn a thing — the
            // director does all three later, on its own schedule, after the
            // briefing. With no director in the scene Suspended is false and this
            // line is unreachable, which is what keeps endless mode identical.
            // Guarded on "a director owns me", NOT on the transient Suspended
            // flag. Awake order between components is undefined, so a director
            // that suspends in Awake and RESUMES in its own Start can leave
            // Suspended false by the time this runs -- and then the runner would
            // begin its own run and open its own countdown underneath a mission
            // that had already started one. The ownership flag is set once, in
            // Suspend, and never cleared.
            if (_directorOwned || Suspended) return;

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
            // Held by a director: a briefing, a cutscene, a checkpoint fade. The
            // token pool stops ticking with everything else so a hold cannot expire
            // the tokens of drones that are standing perfectly still.
            if (Suspended) return;

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

                // Consumed only when a clear actually PAYS. The recovery paths end
                // a wave that never happened, and eating the player's gamble for a
                // fight they were never given would be the worst possible moment
                // to take it.
                if (_pendingClearMultiplier > 1f)
                {
                    bonus = ApplySkipBonus(bonus, _pendingClearMultiplier);
                    _pendingClearMultiplier = 1f;
                }
                _run?.AddMoney(bonus);
            }

            _phaseEndsAt = Time.time + _clearedSeconds;
            SetPhase(RunPhase.Cleared);
            WaveCleared?.Invoke(_wave);
        }

        /// <summary>
        /// The skipped-shop payout. A pure static so the arithmetic can be tested
        /// exactly, rather than inferred from a money total that kill rewards have
        /// already been added to. No mutable state, so the no-statics rule is happy.
        /// </summary>
        public static int ApplySkipBonus(int bonus, float multiplier) =>
            Mathf.RoundToInt(bonus * Mathf.Max(1f, multiplier));

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

        /// <summary>
        /// Walk out of the break without buying, and the next clear pays more.
        ///
        /// This is the decision the shop was missing. Buying is always correct
        /// when the offers are good, so a good break was never a choice — it was
        /// a menu. Skipping trades the upgrade you could have had for money you
        /// only collect if you survive the next wave without it, and dying in that
        /// wave means you get neither.
        /// </summary>
        public void SkipShopForBonus()
        {
            if (Phase != RunPhase.Shop) return;
            _pendingClearMultiplier = _shopConfig != null
                ? Mathf.Max(1f, _shopConfig.skipBonusMultiplier)
                : 1f;
            EnterCountdown();
        }

        /// <summary>
        /// Drop everything the arena is holding: queued spawns, live drones, and
        /// the attack tokens they were carrying.
        ///
        /// A game-over screen with drones still chewing on the corpse behind it
        /// reads as a crash. So does a mission-complete screen, and so does a
        /// checkpoint fade — same three calls, same reason, three callers.
        /// </summary>
        private void ClearTheArena()
        {
            _registry?.DespawnAll();
            _queue.Clear();
            _tokens?.Clear();
        }

        private void OnPlayerDied(Health health, DamageInfo info)
        {
            if (Phase == RunPhase.GameOver) return;

            ClearTheArena();

            // Campaign death is a rewind to the last checkpoint, not a game over.
            // The runner reports it and stops there: what a death costs is the
            // director's decision, and the phase is left untouched so the arena can
            // simply be refilled without a scene reload.
            if (!_deathEndsRun)
            {
                PlayerDown?.Invoke();
                return;
            }

            Outcome = RunOutcome.Died;
            _run?.RecordRunEnded();
            SetPhase(RunPhase.GameOver);
            RunEnded?.Invoke(RunOutcome.Died);
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

        // ---------- the mission-director seam ----------
        //
        // Seven additions through which a MissionDirector drives THIS loop instead
        // of forking it. Every one generalises something the runner already does
        // and hides: it already picks a starting wave, already carries a wave list,
        // already has a do-nothing state, already ends a run, already clears the
        // arena. A second implementation would have to duplicate — and keep in sync
        // forever — the spawn queue's struct-copy writeback, both placement-failure
        // hang guards, and the wave-that-planned-nothing recovery with its refusal
        // to pay a clear bonus. That is the entire hard-won part of this file.
        //
        // With no director in the scene NONE of this is reachable: Suspended stays
        // false, _deathEndsRun stays true, Outcome stays Died, and _waves stays
        // whatever the scene serialized. Endless mode behaves exactly as it did
        // before the seam existed, and that inertness is the acceptance criterion.

        /// <summary>
        /// Replace the authored wave list, because a mission ships its own. `_waves`
        /// is private-serialized and written by the grey-box builder only.
        ///
        /// Refused mid-wave, loudly. ConfigForWave is read by the live spawn queue
        /// AND by the payout: swapping the list under a running wave changes what
        /// the queue is draining and pays that wave's moneyBonusOnClear out of a
        /// different asset than the one the player actually fought.
        /// </summary>
        internal void SetWaves(WaveConfig[] waves)
        {
            if (Phase == RunPhase.Wave)
            {
                GameLog.Error(
                    $"SetWaves refused during wave {_wave}: the spawn queue and the clear bonus both read the " +
                    "wave list, so swapping it now pays for a fight nobody had. End or abort the wave first.",
                    this);
                return;
            }
            _waves = waves;
        }

        /// <summary>
        /// Hold the loop — a briefing, a cutscene, a checkpoint fade.
        ///
        /// Non-destructive on purpose: the spawn queue, the drones already in the
        /// arena and the held attack tokens all survive, so Resume() puts the player
        /// back into the fight they left rather than a fresh one. AbortWave is the
        /// destructive companion.
        /// </summary>
        internal void Suspend()
        {
            _directorOwned = true;
            if (Suspended) return;
            Suspended = true;
            _suspendedAt = Time.time;
        }

        /// <summary>Put the loop back, along with the clock the hold was keeping.</summary>
        internal void Resume()
        {
            if (!Suspended) return;
            Suspended = false;

            // Phase deadlines are absolute Time.time stamps, so seconds the player
            // spent reading a briefing would otherwise be spent by the countdown
            // too: _phaseEndsAt lands in the past and the wave arrives on the first
            // frame back, with no warning at all. Give back exactly what was taken.
            _phaseEndsAt += Time.time - _suspendedAt;
        }

        /// <summary>
        /// Aim the loop at a wave and count down to it — a checkpoint restore.
        ///
        /// The countdown starts wave `_wave + 1`, so this backs up one:
        /// StartFrom(5) means the next wave fought is 5. It does NOT clear the
        /// arena; pair it with AbortWave when the abandoned wave's drones are still
        /// walking around.
        /// </summary>
        internal void StartFrom(int wave)
        {
            _wave = Mathf.Max(0, wave - 1);
            EnterCountdown();
        }

        /// <summary>
        /// Throw the current wave away: queued spawns, live drones, held tokens.
        ///
        /// The caller owns what happens next, and must call one of Suspend() or
        /// StartFrom() with it. Left alone in RunPhase.Wave with an empty arena the
        /// very next Update finds the clear condition satisfied and pays the bonus
        /// for a fight that was cancelled.
        /// </summary>
        internal void AbortWave() => ClearTheArena();

        /// <summary>
        /// Whether the player's death ends the run. False in campaign, where the
        /// runner raises PlayerDown and leaves the phase alone so a director can
        /// rewind to the last checkpoint.
        /// </summary>
        internal void SetDeathEndsRun(bool value) => _deathEndsRun = value;

        /// <summary>
        /// End the run without a corpse: a mission completed, a mission failed by
        /// its own rules, a run walked away from.
        ///
        /// Deliberately does NOT call RunContext.RecordRunEnded. That writes the
        /// permadeath record, and a mission's wave number must never land in
        /// bestRound — the endless record would be polluted by content that does
        /// not share its difficulty curve. The caller decides what an ending is
        /// worth recording, exactly as PausePanel already does when it quits a run.
        /// </summary>
        internal void FinishRun(RunOutcome outcome)
        {
            if (Phase == RunPhase.GameOver) return;

            ClearTheArena();
            Outcome = outcome;
            SetPhase(RunPhase.GameOver);
            RunEnded?.Invoke(outcome);
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
