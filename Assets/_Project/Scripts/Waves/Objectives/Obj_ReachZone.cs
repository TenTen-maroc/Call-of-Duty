#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Get to a marker. The cheapest objective in the family and the one that
    /// does the most work: chained with a time limit on the step it is a run
    /// under fire, and it is how every mission that is not a fight gets its shape.
    ///
    /// Deliberately has no dwell time. That objective already exists — it is
    /// <see cref="Obj_HoldZone"/> with a small holdSeconds — and two objectives
    /// that overlap by a field is how a family of ten becomes a family of thirty.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Reach", menuName = "CoD/Objectives/Reach Zone", order = 3)]
    public sealed class Obj_ReachZone : MissionObjective
    {
        [Tooltip("Which registered zone. The director maps ids to markers when the mission starts.")]
        [Min(0)] public int zoneId = 0;

        public override void Begin(in ObjectiveContext context, ref ObjectiveState state) => state.SetProgress(0f);

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            // Checked rather than assumed: a player who is already standing on the
            // marker when the step begins has reached it, and making them step off
            // and back on would be a puzzle nobody wrote.
            if (context.IsInsideZone(zoneId)) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state) => into.Append(title);
    }
}
