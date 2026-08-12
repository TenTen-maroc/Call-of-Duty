#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// One thing a mission asks of the player, as data. A mission is an ordered
    /// list of these plus a wave list — twelve missions and no new gameplay code
    /// is the whole point, exactly as a weapon is a config plus a list of
    /// EffectModules.
    ///
    /// THREE RULES. They are transcribed from <see cref="CoD.Weapons.EffectModule"/>
    /// because they hold here for the same reasons, and because each one is a bug
    /// that has already been paid for once somewhere in this project:
    ///
    /// 1. Objectives are STATELESS. The asset holds numbers and text, nothing
    ///    else. One asset is shared by every mission that uses it — "kill twelve
    ///    drones" is one file, not one per mission — so a count stored on the
    ///    asset would be shared too. Worse, configs are read-only at runtime for a
    ///    hard reason: Domain Reload is off, so a runtime write to a
    ///    ScriptableObject survives into the next Play session and edits the
    ///    authored design in the repo. Every per-instance value travels in
    ///    <see cref="ObjectiveState"/>, passed by ref.
    ///
    /// 2. Objectives NEVER mutate the world. They read the context and write the
    ///    state, and that is all. Spawning, healing, phase changes, scene loads
    ///    and save writes belong to the director, so each of those has exactly one
    ///    place to go wrong instead of one per objective type. (AttackModule bends
    ///    this — it is handed the DroneController and moves it — because a drone's
    ///    attack IS a world change. An objective has no equivalent need, so the
    ///    stricter line holds here.)
    ///
    /// 3. Objectives NEVER subscribe to anything. No `+=` in an objective, ever.
    ///    With Domain Reload off, a ScriptableObject that subscribes keeps that
    ///    subscription into the next Play session: handlers fire twice, then three
    ///    times, pointing at objects from a session that has ended. It is the
    ///    mutable-static bug class in the one shape the guard cannot see, because
    ///    nothing about the line is static. The director subscribes once and
    ///    accumulates into <see cref="MissionProgress"/>; objectives POLL it. That
    ///    is also precisely what makes every objective testable with no scene, no
    ///    runner and no frame — see MissionObjectiveTests.
    ///
    /// There is deliberately NO "Timed" objective. See MissionConfig.Step.
    /// </summary>
    public abstract class MissionObjective : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Save/record key. Never renamed once shipped — mission records are keyed by it.")]
        public string stableId = "obj_";

        [Tooltip("The HUD line. Short: it is read mid-fight, in peripheral vision.")]
        public string title = "OBJECTIVE";

        [Tooltip("The briefing line. Long form, read before the shooting starts.")]
        [TextArea] public string description = "";

        /// <summary>
        /// Failing this fails the mission. True by default because that is what an
        /// objective normally means; an optional bonus objective sets it false and
        /// the director then records the failure without ending anything.
        /// </summary>
        public virtual bool Critical => true;

        /// <summary>
        /// This objective never completes under its own power — it is a constraint
        /// that runs in parallel and can only fail (NoAlarm is the archetype). The
        /// director marks it Complete when the last completing objective does.
        /// Without this the mission would wait forever on a rule the player was
        /// obeying perfectly.
        /// </summary>
        public virtual bool CompletesWithMission => false;

        /// <summary>
        /// True when this objective is meaningless without a wave list, so
        /// MissionConfig can refuse to ship a mission that can never finish. A
        /// virtual rather than a type test in OnValidate: the next objective that
        /// needs waves is then one override, not another line in a growing
        /// `is Obj_This or Obj_That` chain.
        /// </summary>
        public virtual bool RequiresWaves => false;

        /// <summary>
        /// Start this objective. The director calls THIS, never
        /// <see cref="Begin"/> directly, because it is the one place the step's
        /// time limit is stamped onto the state — an objective cannot forget to do
        /// it, and cannot disagree with another objective about what a deadline
        /// means.
        ///
        /// The state is ASSIGNED, not adjusted: the director reuses state slots
        /// between steps, and a leftover accumulator from the previous step would
        /// read as progress nobody made.
        /// </summary>
        public void BeginStep(in ObjectiveContext context, ref ObjectiveState state, float now, float timeLimitSeconds)
        {
            state = ObjectiveState.Begun(now, timeLimitSeconds);
            Begin(in context, ref state);
        }

        /// <summary>
        /// Take the baseline. Anything counted against a running total — waves,
        /// kills, targets, interactions — must snapshot it HERE and measure the
        /// difference, or step three's "two more kills" is satisfied by the forty
        /// the player already had.
        /// </summary>
        public abstract void Begin(in ObjectiveContext context, ref ObjectiveState state);

        /// <summary>
        /// Called every frame the objective is Active. Read the context, write the
        /// state, return. <paramref name="now"/> and <paramref name="deltaTime"/>
        /// are passed in rather than read from <see cref="Time"/> so the whole
        /// family can be driven by a test at any rate, including instantly.
        /// </summary>
        public abstract void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime);

        /// <summary>
        /// The objective is leaving play — completed, failed, or the mission was
        /// abandoned mid-step. Undo nothing in the world (rule 2); this exists for
        /// objectives that want to finalise their own readout.
        /// </summary>
        public virtual void End(in ObjectiveContext context, ref ObjectiveState state) { }

        /// <summary>
        /// Write the HUD line into a builder the CALLER owns, and never return a
        /// string. The objective panel redraws every frame it is visible, so a
        /// `string Describe()` would allocate three or four strings per frame
        /// forever — the exact drip that shows up as a mystery GC spike an hour
        /// later. Append only; never Clear a builder that is not yours.
        /// </summary>
        public abstract void Describe(StringBuilder into, in ObjectiveState state);
    }

    /// <summary>
    /// Everything an objective is allowed to know. A readonly struct passed by
    /// `in`, built once per director tick and read by every active objective.
    ///
    /// The list is short on purpose, and it is the enforcement mechanism for rule
    /// 2: an objective handed a spawner would eventually spawn something. It gets
    /// the accumulated record, a read-only view of the runner, and where the
    /// player is standing.
    ///
    /// The runner is nullable and every accessor degrades quietly, so a test — or
    /// a mission that runs no waves at all — can build a context with no runner
    /// in existence. That is not a convenience; it is the property that keeps the
    /// objective layer EditMode-testable.
    /// </summary>
    public readonly struct ObjectiveContext
    {
        /// <summary>What the director has accumulated. The only channel objectives have into the world.</summary>
        public readonly MissionProgress Progress;

        /// <summary>The wave loop, read-only by convention. Null in a test, and in any mission with no waves.</summary>
        public readonly WaveRunner? Runner;

        /// <summary>Feet, not eyes — zone tests are on the floor plane. See <see cref="ObjectiveMath.WithinFloorRadius"/>.</summary>
        public readonly Vector3 PlayerPosition;

        public ObjectiveContext(MissionProgress progress, WaveRunner? runner, Vector3 playerPosition)
        {
            Progress = progress;
            Runner = runner;
            PlayerPosition = playerPosition;
        }

        /// <summary>Countdown when there is no runner: the phase before anything has been fought is the honest default.</summary>
        public RunPhase Phase => Runner != null ? Runner.Phase : RunPhase.Countdown;

        public int WaveNumber => Runner != null ? Runner.WaveNumber : 0;

        /// <summary>Queued plus alive. Zero with no runner, which reads as "nothing left to fight".</summary>
        public int EnemiesRemaining => Runner != null ? Runner.EnemiesRemaining : 0;

        /// <summary>Is the player standing on the zone with this id? False for an id this arena never registered.</summary>
        public bool IsInsideZone(int zoneId) => Progress.IsInsideZone(zoneId, PlayerPosition);
    }
}
