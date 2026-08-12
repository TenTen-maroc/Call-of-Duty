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
