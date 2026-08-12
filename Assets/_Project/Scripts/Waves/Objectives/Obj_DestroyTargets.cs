#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Blow up N mission objects — dishes, generators, crates. Counted through
    /// <see cref="MissionProgress.RecordTargetDestroyed"/>, which the director
    /// calls when a destructible dies, so this objective never has to know what a
    /// destructible IS or hold a reference to one.
    ///
    /// There is no per-group filter. "Destroy these three specific dishes" is
    /// authored by only spawning three destructibles in that step, which keeps
    /// the objective to one number and puts the choice of what is destructible
    /// where it belongs — in the arena.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Destroy", menuName = "CoD/Objectives/Destroy Targets", order = 4)]
    public sealed class Obj_DestroyTargets : MissionObjective
    {
        [Tooltip("Targets to destroy FROM HERE. Anything blown up in an earlier step does not count.")]
        [Range(1, 20)] public int count = 3;

        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            state.Baseline = context.Progress.TargetsDestroyed;
            state.Counter = 0;
            state.SetProgress(0f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            state.Counter = Mathf.Max(0, context.Progress.TargetsDestroyed - state.Baseline);
            state.SetProgress(ObjectiveMath.Progress01(state.Counter, count));
            if (state.Counter >= count) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            into.Append(' ');
            ObjectiveMath.AppendInt(into, Mathf.Min(state.Counter, count));
            into.Append('/');
            ObjectiveMath.AppendInt(into, count);
        }
    }
}
