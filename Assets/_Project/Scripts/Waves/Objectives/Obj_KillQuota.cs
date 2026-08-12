#nullable enable
using System.Text;
using CoD.Enemies;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Kill N drones, optionally of one type. The filtered form is what makes a
    /// mission read as a mission rather than a wave — "destroy the three Tanks"
    /// is a different instruction from "kill twelve things", and both are this
    /// one asset with a reference set or left empty.
    ///
    /// The filter is a DroneConfig reference rather than a name or an id: it is
    /// the same asset the wave list points at, so a typo is impossible and the
    /// inspector shows what it means.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Kills", menuName = "CoD/Objectives/Kill Quota", order = 1)]
    public sealed class Obj_KillQuota : MissionObjective
    {
        [Tooltip("Kills required FROM HERE. Kills made before this step began do not count.")]
        [Range(1, 200)] public int quota = 12;

        [Tooltip("Leave empty to count every drone. Set it and only this archetype counts.")]
        public DroneConfig? droneFilter;


        /// <summary>

        /// Yes: a kill quota with no wave loop is a locked empty room.

        ///

        /// MissionObjective.RequiresWaves defaults to FALSE and only

        /// Obj_SurviveWaves overrode it, so MissionDirector's wave gate left the

        /// runner suspended for a mission whose only steps were a quota and a

        /// hold. Nothing spawned, the quota could never fill, and the asset

        /// validated clean — so the mission shipped in the catalog as a room

        /// with nothing in it and no error anywhere saying why.

        /// </summary>

        public override bool RequiresWaves => true;


        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            // KillsOf(null) is the grand total on purpose, so the filtered and
            // unfiltered forms are the same two lines of code and cannot drift.
            state.Baseline = context.Progress.KillsOf(droneFilter);
            state.Counter = 0;
            state.SetProgress(0f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            state.Counter = Mathf.Max(0, context.Progress.KillsOf(droneFilter) - state.Baseline);
            state.SetProgress(ObjectiveMath.Progress01(state.Counter, quota));
            if (state.Counter >= quota) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            into.Append(' ');
            ObjectiveMath.AppendInt(into, Mathf.Min(state.Counter, quota));
            into.Append('/');
            ObjectiveMath.AppendInt(into, quota);
        }
    }
}
