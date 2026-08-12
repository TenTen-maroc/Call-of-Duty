#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Hold out for N more waves. The bridge between the campaign and the engine
    /// that already exists — a mission step that is just the endless game, fenced.
    ///
    /// The folder is filing, not architecture: every objective stays in the
    /// CoD.Waves namespace so the director, the HUD and the tests need one using
    /// and never learn which file a type lives in.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Survive", menuName = "CoD/Objectives/Survive Waves", order = 0)]
    public sealed class Obj_SurviveWaves : MissionObjective
    {
        [Tooltip("Waves to survive FROM HERE. Not the wave number to reach — a step is always relative to where the mission got to.")]
        [Range(1, 20)] public int waves = 2;

        public override bool RequiresWaves => true;

        /// <summary>
        /// The baseline is taken here and nowhere else. Take it in Tick and it is
        /// re-taken every frame, so the count never moves; skip it entirely and a
        /// mission whose third step asks for two waves is already satisfied by the
        /// five the player fought in step one.
        /// </summary>
        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            state.Baseline = context.Progress.WavesCleared;
            state.Counter = 0;
            state.SetProgress(0f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            // Clamped at zero because a checkpoint rewind resets the progress
            // record while this state survives, which would otherwise read as a
            // negative count and a progress bar running backwards.
            state.Counter = Mathf.Max(0, context.Progress.WavesCleared - state.Baseline);
            state.SetProgress(ObjectiveMath.Progress01(state.Counter, waves));
            if (state.Counter >= waves) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            into.Append(' ');
            ObjectiveMath.AppendInt(into, Mathf.Min(state.Counter, waves));
            into.Append('/');
            ObjectiveMath.AppendInt(into, waves);
        }
    }
}
