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
        [SerializeField] private DroneRegistry? _registry = null;
        [Tooltip("Where the player is. Zone objectives measure from here.")]
        [SerializeField] private Transform? _player = null;

        private readonly MissionProgress _progress = new();

        private MissionConfig? _mission;
        private ObjectiveState[] _states = Array.Empty<ObjectiveState>();
        private int _activeStep;
        private bool _running;
        private bool _finished;

        private int _checkpointStep;
        private int _checkpointWave;

        public MissionConfig? Mission => _mission;
        public MissionProgress Progress => _progress;
        public int ActiveStep => _activeStep;
        public bool IsRunning => _running;

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
                _runner.PlayerDown += OnPlayerDown;
            }
            if (_registry != null) _registry.Killed += OnDroneKilled;
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.WaveCleared -= OnWaveCleared;
                _runner.PlayerDown -= OnPlayerDown;
            }
            if (_registry != null) _registry.Killed -= OnDroneKilled;
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
            _checkpointWave = 0;
            _finished = false;

            _run.BeginRun(_mission.startingMoney);
            _runner.SetWaves(_mission.waves);
            _running = true;

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
            ObjectivesChanged?.Invoke();
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
                _runner.StartFrom(_runner.WaveNumber + 1);
                _runner.Resume();
            }
            else
            {
                // Suspend, not abort: a step that stops wanting waves should not
                // delete the drones already in the arena and standing between the
                // player and the next objective.
                _runner.Suspend();
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

            _activeStep = next;
            _checkpointStep = next;
            _checkpointWave = _runner != null ? _runner.WaveNumber : 0;
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

        private void FinishMission(RunOutcome outcome)
        {
            if (_finished) return;
            _finished = true;
            _running = false;

            _runner?.FinishRun(outcome);
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
            _runner.AbortWave();

            _activeStep = _checkpointStep;
            _run.BeginRun(_mission.startingMoney);
            // Suspend first so ActivateFrom's gate sees a consistent state and
            // decides for itself whether this step wants waves back.
            _runner.Suspend();
            _runner.StartFrom(_checkpointWave);
            ActivateFrom(_activeStep);
        }

        private void OnWaveCleared(int wave) => _progress.RecordWaveCleared();

        private void OnDroneKilled(DroneController drone, DamageInfo info)
            => _progress.RecordKill(drone.Config);

        /// <summary>
        /// Wired from PlayerInteractor, which speaks Core's InteractKind because
        /// the player RAISES interactions, the mission layer COUNTS them, and
        /// neither assembly may reference the other.
        ///
        /// The mapping below is a seam, not a feature: two enums for one concept
        /// is one too many, and this collapses to a single Core enum in a
        /// follow-up. Written out rather than cast, so the day the two diverge
        /// is a compile error instead of a silently miscounted objective.
        /// </summary>
        public void RecordInteraction(InteractKind kind)
        {
            switch (kind)
            {
                case InteractKind.Terminal: _progress.RecordInteraction(InteractionKind.Terminal); break;
                case InteractKind.Charge: _progress.RecordInteraction(InteractionKind.Charge); break;
                case InteractKind.Intel: _progress.RecordInteraction(InteractionKind.Intel); break;
                case InteractKind.Door: _progress.RecordInteraction(InteractionKind.Door); break;
                // Generic and Extract carry no counter: an extraction is an
                // objective watching a zone, not a tally.
                default: break;
            }
        }

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
            for (int i = _activeStep; i < _mission.StepCount; i++)
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
