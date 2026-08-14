#nullable enable
using System;
using System.Text;
using CoD.Core;
using CoD.Enemies;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Runs a mission on top of the wave engine, without forking it.
    ///
    /// It owns four things and nothing else: which step is active, what has
    /// happened so far (MissionProgress), when to tell WaveRunner to do
    /// something, and what a death costs. Objectives never touch the world and
    /// never subscribe to anything — this subscribes once, accumulates, and the
    /// objectives poll. That rule is what keeps every objective testable with no
    /// scene at all, and it is why all the event wiring lives here.
    ///
    /// THE ORDERING GUARANTEE, restated because everything hangs on it.
    /// Unity does not promise Awake ORDER between components, but it does
    /// promise every Awake completes before any Start. This suspends the runner
    /// in Awake; WaveRunner.Start early-returns while suspended. So in campaign
    /// the runner does not begin a run, open a countdown or spawn anything — the
    /// director does all three later, on its own schedule, with no race and no
    /// execution-order attribute to keep in sync.
    ///
    /// AND THE INVERSE, which is the more important half: if this component is
    /// absent, or the save does not say campaign, it does NOTHING. Endless mode
    /// is byte-identical to a build with no mission layer at all. There is
    /// deliberately no serialized "is campaign" flag — a bool cannot be Checked
    /// by GreyBoxVerify, which tests objectReferenceValue, so the ABSENCE of a
    /// director is the endless configuration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionDirector : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RunContext? _run = null;
        [SerializeField] private WaveRunner? _runner = null;
        [SerializeField] private MissionCatalog? _catalog = null;
        [Tooltip("Where the mission RESULT is written. Never the permadeath record.")]
        [SerializeField] private SettingsHub? _settings = null;
        [SerializeField] private DroneRegistry? _registry = null;
        [Tooltip("The scene's interactables. The director subscribes for the counting; it never touches one.")]
        [SerializeField] private CoD.Core.InteractableRegistry? _interactables = null;
        [Tooltip("Where the player is. Zone objectives measure from here.")]
        [SerializeField] private Transform? _player = null;
        [Tooltip("The player's Health. A campaign death is a rewind, and a rewind has to put them back on their feet.")]
        [SerializeField] private Health? _playerHealth = null;
        [Tooltip("Priority/cooldown scheduler. Optional: missions remain fully playable without radio.")]
        [SerializeField] private RadioDialogueScheduler? _radio = null;
        [Tooltip("Named places a mission can send the player. Registered on mission start; objectives address them by id.")]
        [SerializeField] private MissionZone[] _zones = System.Array.Empty<MissionZone>();

        private readonly MissionProgress _progress = new();

        private MissionConfig? _mission;
        private ObjectiveState[] _states = Array.Empty<ObjectiveState>();
        private int _activeStep;
        private bool _running;
        private bool _finished;
        private bool _transitionPending;
        private float _transitionAt;
        private int _pendingNextStep;

        private float _startedAt;
        private int _checkpointStep;
        private int _checkpointWave;

        public MissionConfig? Mission => _mission;
        public MissionProgress Progress => _progress;
        public int ActiveStep => _activeStep;
        public bool IsRunning => _running;

        /// <summary>
        /// The wave a death rewinds to — the wave the checkpoint's step group
        /// STARTS at, not the one before it. Exposed because it is the one piece
        /// of checkpoint state with no other observable consequence until
        /// somebody dies, which is exactly how it was wrong for two milestones.
        /// See <see cref="ActivateFrom"/> for where it is captured and why there.
        /// </summary>
        public int CheckpointWave => _checkpointWave;

        /// <summary>Raised when a step resolves, so the HUD redraws on change instead of every frame.</summary>
        public event Action? ObjectivesChanged;

        /// <summary>Raised once, with the outcome the mission ended on.</summary>
        public event Action<RunOutcome>? MissionEnded;

        private void Awake()
        {
            // The single decision that makes this inert for endless mode, taken
            // in Awake specifically so it lands before WaveRunner.Start.
            if (_run == null || _runner == null || !_run.Save.campaignSelected)
            {
                enabled = false;
                return;
            }

            _mission = _catalog != null ? _catalog.Find(_run.Save.selectedMissionId) : null;
            if (_mission == null)
            {
                // A save pointing at a deleted mission, or a catalog nobody
                // filled in. Falling through to the endless loop is strictly
                // better than a suspended runner and an empty arena, which is
                // indistinguishable from a hang.
                GameLog.Error(
                    "Campaign was selected but no matching mission is in the catalog. " +
                    "Falling back to the endless loop.", this);
                enabled = false;
                return;
            }

            _runner.Suspend();
            _runner.SetDeathEndsRun(false);
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.WaveCleared += OnWaveCleared;
                _runner.WaveStarted += OnWaveStarted;
                _runner.PlayerDown += OnPlayerDown;
            }
            if (_registry != null) _registry.Killed += OnDroneKilled;
            if (_interactables != null) _interactables.Interacted += RecordInteraction;
            if (_playerHealth != null) _playerHealth.Damaged += OnPlayerDamaged;
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.WaveCleared -= OnWaveCleared;
                _runner.WaveStarted -= OnWaveStarted;
                _runner.PlayerDown -= OnPlayerDown;
            }
            if (_registry != null) _registry.Killed -= OnDroneKilled;
            if (_interactables != null) _interactables.Interacted -= RecordInteraction;
            if (_playerHealth != null) _playerHealth.Damaged -= OnPlayerDamaged;
        }

        private void Start()
        {
            if (_mission == null || _runner == null || _run == null) return;
            BeginMission();
        }

        private void BeginMission()
        {
            if (_mission == null || _runner == null || _run == null) return;

            _progress.Reset();
            _states = new ObjectiveState[_mission.StepCount];
            _activeStep = 0;
            _checkpointStep = 0;
            // _checkpointWave is deliberately not set here. ActivateFrom owns it
            // and sets it below, from the runner, once the wave gate has decided
            // what the first step group actually fights.
            _finished = false;
            _transitionPending = false;

            _run.BeginRun(_mission.startingMoney);
            _runner.SetWaves(_mission.waves);
            _running = true;
            _startedAt = Time.time;
            _radio?.Configure(_mission.radioDialogue);
            TriggerRadio(RadioTrigger.MissionEntry);

            RegisterZones();

            // Deliberately NOT resumed here. The runner stays suspended until a
            // step actually asks for enemies — see ApplyWaveGate.
            ActivateFrom(0);
        }

        /// <summary>
        /// Turns on the step at the given index and every parallel step that
        /// runs alongside it.
        ///
        /// Parallel is authored on the step that JOINS the one before it, so a
        /// run of parallel steps is contiguous — which is why this walks forward
        /// while the next step says parallel rather than scanning the whole list.
        /// </summary>
        private void ActivateFrom(int index)
        {
            if (_mission == null) return;

            float now = Time.time;
            ObjectiveContext context = Context();

            for (int i = index; i < _mission.StepCount; i++)
            {
                MissionConfig.Step step = _mission.steps[i];
                if (i > index && !step.parallel) break;
                if (step.objective == null) continue;

                _states[i] = default;
                step.objective.BeginStep(in context, ref _states[i], now, step.timeLimitSeconds);
            }

            ApplyWaveGate();

            // THE CHECKPOINT'S WAVE, CAPTURED HERE AND NOWHERE ELSE.
            //
            // AFTER the gate, because the gate is the only thing that decides
            // which wave a step group fights, and it does not simply continue
            // the count: resuming a suspended runner it calls
            // StartFrom(WaveNumber + 1), deliberately, so a group that opens on
            // a briefing or a walk gets a fresh countdown rather than the
            // remains of the wave that was running when the last group ended.
            //
            // Captured BEFORE it — which is what the callers used to do — the
            // number recorded is the wave the group is about to skip past, and
            // every checkpoint from the second wave group onward is one wave
            // short. The player dies, respawns into a wave they have already
            // cleared, and fights that wave's authored composition instead of
            // the one their objective is standing in. Mission 1 never showed it:
            // it has a single wave group, which starts at wave 1, and one short
            // of wave 1 clamps back to wave 1 in StartFrom. Mission 2 has two.
            //
            // NextWaveNumber rather than WaveNumber + 1 because a group can also
            // begin in the middle of a live wave, where the gate does nothing at
            // all and the wave to come back to is the one already being fought.
            _checkpointWave = _runner != null ? _runner.NextWaveNumber : 0;

            ObjectivesChanged?.Invoke();
            if (index == 0) TriggerRadio(RadioTrigger.FirstObjective);
        }

        /// <summary>
        /// Waves run only while a step actually wants them.
        ///
        /// Two bugs live here if it is skipped. The obvious one: a stealth step,
        /// a walk to a control room or a terminal hack would happen under a live
        /// wave, which turns "reach the server room unseen" into a firefight and
        /// makes NoAlarm unwinnable by construction.
        ///
        /// The subtle one: WaveRunner.Start early-returns while suspended, so it
        /// never called EnterCountdown and _phaseEndsAt is still 0. Resuming
        /// would therefore find "now >= _phaseEndsAt" true on the very first
        /// frame and start wave 1 INSTANTLY, with no countdown at all — the
        /// breath before the first wave, silently gone, only in campaign.
        /// StartFrom is what puts a real countdown back.
        /// </summary>
        private void ApplyWaveGate()
        {
            if (_mission == null || _runner == null) return;

            bool wanted = GroupWantsWaves();
            if (wanted == !_runner.Suspended) return;

            if (wanted)
            {
                // ORDER MATTERS, and it is the opposite of the obvious one.
                // Resume gives back the time the runner spent suspended by
                // adding it to _phaseEndsAt — correct on its own. StartFrom then
                // overwrites _phaseEndsAt outright with a fresh countdown, which
                // is also correct on its own. Do StartFrom first and Resume adds
                // the whole suspended interval ON TOP of the new countdown: a
                // player who took 25 s to walk to the first objective would then
                // stare at an empty arena for 29 seconds instead of 4.
                _runner.Resume();
                _runner.StartFrom(_runner.WaveNumber + 1);
            }
            else
            {
                // Suspend, not abort: a step that stops wanting waves should not
                // delete the drones already in the arena and standing between the
                // player and the next objective.
                _runner.Suspend();
            }
        }

        /// <summary>
        /// Hands the mission layer the real positions of the places a mission
        /// can send the player.
        ///
        /// Without this, MissionProgress.RegisterZone has no caller anywhere in
        /// the game, IsInsideZone answers false forever — correctly, by its own
        /// design — and every ReachZone, HoldZone and Extract objective is
        /// uncompletable. The failure is silent and total: the mission simply
        /// sits on its first step with the arena empty, which is the state this
        /// file's own comments call indistinguishable from a hang.
        ///
        /// Re-run after a checkpoint rewind because Reset() clears them.
        /// </summary>
        private void RegisterZones()
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                MissionZone zone = _zones[i];
                if (zone.marker == null) continue;
                _progress.RegisterZone(zone.id, zone.marker.position, zone.radius);
            }
        }

        private bool GroupWantsWaves()
        {
            if (_mission == null) return false;
            for (int i = _activeStep; i < _mission.StepCount; i++)
            {
                if (i > _activeStep && !_mission.steps[i].parallel) break;
                MissionObjective? objective = _mission.steps[i].objective;
                if (objective != null && objective.RequiresWaves) return true;
            }
            return false;
        }

        private ObjectiveContext Context()
            => new(_progress, _runner, _player != null ? _player.position : Vector3.zero);

        private void Update()
        {
            if (!_running || _finished || _mission == null) return;

            float now = Time.time;
            if (_transitionPending)
            {
                if (now < _transitionAt) return;
                _transitionPending = false;
                _activeStep = _pendingNextStep;
                _checkpointStep = _pendingNextStep;
                ActivateFrom(_pendingNextStep);
                return;
            }

            float deltaTime = Time.deltaTime;
            ObjectiveContext context = Context();

            bool anyActive = false;
            bool changed = false;

            for (int i = _activeStep; i < _mission.StepCount; i++)
            {
                if (i > _activeStep && !_mission.steps[i].parallel) break;

                MissionObjective? objective = _mission.steps[i].objective;
                if (objective == null) continue;
                if (_states[i].IsResolved) continue;

                objective.Tick(in context, ref _states[i], now, deltaTime);

                // The deadline is checked HERE, uniformly, rather than inside
                // each objective. That is what lets any objective be timed
                // without a wrapper ScriptableObject composing another one.
                if (!_states[i].IsResolved && _states[i].IsPastDeadline(now))
                {
                    _states[i].MarkFailed();
                }

                if (_states[i].IsResolved)
                {
                    objective.End(in context, ref _states[i]);
                    changed = true;

                    if (_states[i].Status == ObjectiveStatus.Failed && objective.Critical)
                    {
                        FinishMission(RunOutcome.MissionFailed);
                        return;
                    }
                }
                else
                {
                    anyActive = true;
                }
            }

            if (changed) ObjectivesChanged?.Invoke();
            if (anyActive) return;

            // Every step in this group resolved. An objective that only completes
            // WITH the mission — an extraction — ends it here rather than
            // advancing to a step that does not exist.
            if (GroupCompletesTheMission())
            {
                FinishMission(RunOutcome.MissionComplete);
                return;
            }

            int next = NextGroupStart();
            if (next >= _mission.StepCount)
            {
                FinishMission(RunOutcome.MissionComplete);
                return;
            }


            TriggerRadio(RadioTrigger.ObjectiveComplete);
            float completionDelay = GroupCompletionDelay();
            if (completionDelay > 0f)
            {
                _runner?.Suspend();
                _transitionPending = true;
                _transitionAt = now + completionDelay;
                _pendingNextStep = next;
                ObjectivesChanged?.Invoke();
                return;
            }

            _activeStep = next;
            _checkpointStep = next;
            ActivateFrom(next);
        }

        private bool GroupCompletesTheMission()
        {
            if (_mission == null) return false;
            for (int i = _activeStep; i < _mission.StepCount; i++)
            {
                if (i > _activeStep && !_mission.steps[i].parallel) break;
                MissionObjective? objective = _mission.steps[i].objective;
                if (objective != null && objective.CompletesWithMission) return true;
            }
            return false;
        }

        private int NextGroupStart()
        {
            if (_mission == null) return int.MaxValue;
            int i = _activeStep + 1;
            while (i < _mission.StepCount && _mission.steps[i].parallel) i++;
            return i;
        }

        private float GroupCompletionDelay()
        {
            if (_mission == null) return 0f;
            float delay = 0f;
            for (int i = _activeStep; i < _mission.StepCount; i++)
            {
                if (i > _activeStep && !_mission.steps[i].parallel) break;
                delay = Mathf.Max(delay, _mission.steps[i].completionDelaySeconds);
            }
            return delay;
        }

        private void FinishMission(RunOutcome outcome)
        {
            if (_finished) return;
            _finished = true;
            _running = false;

            TriggerRadio(outcome == RunOutcome.MissionComplete
                ? RadioTrigger.MissionComplete
                : RadioTrigger.MissionFailed);

            _runner?.FinishRun(outcome);

            // Write the record. Without this SettingsHub.RecordMissionResult has
            // no caller at all, no mission is ever marked complete, and mission
            // select therefore never unlocks anything past mission one — the
            // campaign would have no progression whatsoever.
            //
            // Deliberately NOT RecordRunEnded: that writes bestRound, and a
            // campaign mission must never touch the permadeath record.
            if (_mission != null && _settings != null)
            {
                _settings.RecordMissionResult(
                    _mission.stableId,
                    completed: outcome == RunOutcome.MissionComplete,
                    rating: 0,
                    timeSeconds: Time.time - _startedAt,
                    deaths: _progress.Deaths);
            }

            MissionEnded?.Invoke(outcome);
        }

        /// <summary>
        /// A campaign death rewinds to the last checkpoint, IN MEMORY.
        ///
        /// Never on disk, deliberately: a mission retried after a death must not
        /// write anything a crash could leave half-applied, and the only thing
        /// worth persisting about a mission is whether it was finished. The
        /// record is written once, at the end.
        /// </summary>
        private void OnPlayerDown()
        {
            if (_mission == null || _runner == null || _run == null || _finished) return;

            _progress.RecordDeath();
            _transitionPending = false;
            _runner.AbortWave();

            // PUT THE PLAYER BACK ON THEIR FEET. Everything else here is
            // bookkeeping; this is the line that makes a rewind a rewind.
            //
            // RunContext.BeginRun ends in ApplyStats, which uses AdjustMax and
            // NOT ConfigureMax — deliberately, because it runs on every shop
            // purchase and a player at 8 HP must not be healed by buying a
            // reload-speed passive. With an unchanged max the delta is zero, so
            // current health stays at zero and the player stays dead. Health
            // then refuses all damage (IsAlive is false so ApplyDamage returns 0), Died can
            // never fire again, and WeaponController refuses to fire at all --
            // so waves respawn around an invincible corpse that cannot shoot,
            // no quota or clear can ever complete, and the mission wedges
            // forever with no error anywhere.
            _playerHealth?.ResetHealth();

            _activeStep = _checkpointStep;
            _run.BeginRun(_mission.startingMoney);
            // Suspend first so ActivateFrom's gate sees a consistent state and
            // decides for itself whether this step wants waves back.
            _runner.Suspend();
            // StartFrom's contract is stated in terms of the wave FOUGHT, and
            // _checkpointWave is the wave the checkpoint's group starts at, so
            // these two line up with no arithmetic in between. The gate inside
            // the ActivateFrom below then re-derives the same number and rewrites
            // _checkpointWave with it, which is why a second death in the same
            // group rewinds to the same place rather than walking backwards a
            // wave at a time.
            _runner.StartFrom(_checkpointWave);
            // NOT Progress.Reset(). It wipes Deaths, which is the one counter
            // that must survive a rewind -- it counts what the mission has cost
            // you across every attempt, and a rating that forgot it would score
            // a mission finished on the twelfth try like one finished clean.
            //
            // Nothing else needs clearing either. Every counting objective
            // re-baselines against the current total in its own Begin, so a
            // quota re-activated by this rewind asks for N MORE from here --
            // and the zones survive precisely BECAUSE Reset is not called,
            // since Reset is the only thing that drops them.
            ActivateFrom(_activeStep);
        }

        private void OnWaveStarted(int wave) => TriggerRadio(RadioTrigger.FirstContact);

        private void OnWaveCleared(int wave)
        {
            _progress.RecordWaveCleared();
            TriggerRadio(RadioTrigger.WaveClear);
        }

        private void OnPlayerDamaged(Health health, DamageInfo info)
        {
            if (health.Normalized <= 0.25f) TriggerRadio(RadioTrigger.PlayerBadlyHurt);
        }

        private void TriggerRadio(RadioTrigger trigger) => _radio?.Trigger(trigger);

        private void OnDroneKilled(DroneController drone, DamageInfo info)
            => _progress.RecordKill(drone.Config);

        /// <summary>
        /// Wired from PlayerInteractor, which speaks Core's InteractKind because
        /// the player RAISES interactions, the mission layer COUNTS them, and
        /// neither assembly may reference the other. Core is the only assembly
        /// both can see, so Core is where the enum lives.
        ///
        /// There used to be a hand-written switch here translating that enum
        /// into a second one owned by CoD.Waves. It was a seam, not a feature:
        /// one concept described twice, with a mapping that had to be kept
        /// correct by hand. It is gone — the kind now travels from the thing the
        /// player used all the way to the counter without being translated, so
        /// there is no longer a place for the two halves to disagree.
        ///
        /// One behaviour changed with it, deliberately: Generic and Extract had
        /// no case, so they fell out of the switch and were not counted AT ALL —
        /// not even in the running total, which documents itself as
        /// "interactions of every kind". They count now. Nothing reads them
        /// today (an extraction is an objective watching a zone, not a tally),
        /// but an authored Generic interaction being invisible to the record was
        /// the mapping's bug, not its design.
        /// </summary>
        public void RecordInteraction(InteractKind kind) => _progress.RecordInteraction(kind);

        public void RaiseAlarm() => _progress.RaiseAlarm();

        public void RecordTargetDestroyed() => _progress.RecordTargetDestroyed();

        /// <summary>
        /// Writes the active objective lines into a CALLER-OWNED builder.
        ///
        /// Never returns a string. The HUD redraws this on change, and the one
        /// thing every UI component in this project already does is avoid
        /// building a string it is about to throw away.
        /// </summary>
        public void DescribeActive(StringBuilder into)
        {
            if (_mission == null) return;
            if (_transitionPending) return;

            // Bounded by _states, NOT by StepCount, and the difference is a
            // crash on scene load.
            //
            // Awake resolves _mission; BeginMission sizes _states, and it runs
            // in Start. Every OnEnable in the scene lands in between --
            // including ObjectiveHud, which redraws immediately so the first
            // frame is not blank. So there is a real window where the mission
            // has three steps and this array has none, and indexing StepCount
            // into it throws before the mission has begun.
            //
            // Bounding on the array is the fix rather than an IsRunning guard,
            // because the honest answer to "what are the objectives" before the
            // mission starts is "nothing yet", not an exception.
            int steps = Mathf.Min(_mission.StepCount, _states.Length);
            for (int i = _activeStep; i < steps; i++)
            {
                if (i > _activeStep && !_mission.steps[i].parallel) break;
                MissionObjective? objective = _mission.steps[i].objective;
                if (objective == null) continue;

                if (into.Length > 0) into.Append('\n');
                objective.Describe(into, in _states[i]);
            }
        }

        /// <summary>The state of a step, for the HUD and for the tests.</summary>
        public ObjectiveState StateOf(int index)
            => index >= 0 && index < _states.Length ? _states[index] : default;
    }
}
