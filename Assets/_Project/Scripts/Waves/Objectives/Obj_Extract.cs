#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Stand on the extraction pad until the bird lifts. Almost always the last
    /// step of a mission, which is why it is its own type rather than a
    /// <see cref="Obj_HoldZone"/> with a short timer: the director, the comms and
    /// the end-of-mission screen all need to know that THIS is the way out, and
    /// asking them to infer it from a hold time would be a guess.
    ///
    /// The dwell always resets on leave, with no option. Extraction is the one
    /// hold where stepping off has to mean something — a cumulative extract lets
    /// the player tap the pad between fights and leave the moment the counter
    /// happens to fill, which is not a decision.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Extract", menuName = "CoD/Objectives/Extract", order = 5)]
    public sealed class Obj_Extract : MissionObjective
    {
        [Tooltip("Which registered zone the extraction pad is.")]
        [Min(0)] public int zoneId = 0;

        [Tooltip("Seconds on the pad before the mission ends. 0 = the instant the player steps on.")]
        [Range(0f, 60f)] public float dwellSeconds = 5f;

        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            state.Accumulator = 0f;
            state.SetProgress(0f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            if (context.IsInsideZone(zoneId)) state.Accumulator += deltaTime;
            else state.Accumulator = 0f;

            state.SetProgress(ObjectiveMath.Progress01(state.Accumulator, dwellSeconds));

            // The zone test comes first so a zero dwell still requires the player
            // to actually be standing there — otherwise `0 >= 0` completes the
            // mission on the frame the step begins, from anywhere in the arena.
            if (context.IsInsideZone(zoneId) && state.Accumulator >= dwellSeconds) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            if (state.Accumulator <= 0f) return;
            into.Append(' ');
            ObjectiveMath.AppendSeconds(into, Mathf.Max(0f, dwellSeconds - state.Accumulator));
        }
    }
}
