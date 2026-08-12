#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Stand on a pad for N seconds. The one objective that takes the arena's
    /// three lanes and makes a corner with good sightlines the wrong answer —
    /// which is the tuning problem the repair beacon was built for, stated as a
    /// mission rule instead of a reward.
    ///
    /// The zone is an ID, not a reference. A ScriptableObject cannot hold a scene
    /// Transform, so the director registers the pad's position with
    /// <see cref="MissionProgress.RegisterZone"/> and this asset only ever names
    /// it — which is also why the same asset can be reused in four arenas.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Hold", menuName = "CoD/Objectives/Hold Zone", order = 2)]
    public sealed class Obj_HoldZone : MissionObjective
    {
        [Tooltip("Which registered zone. The director maps ids to pads when the mission starts.")]
        [Min(0)] public int zoneId = 0;

        [Tooltip("Seconds of occupancy required.")]
        [Range(1f, 300f)] public float holdSeconds = 45f;

        [Tooltip("Stepping off restarts the clock. Off = the hold is cumulative, so the player can leave and return.")]
        public bool resetOnLeave = true;

        [Tooltip("Only counts while a wave is actually running. Off would let the player bank the hold during the shop break, for free. This PAUSES the clock between waves; only stepping off the pad can reset it.")]
        public bool requireWavePhase = true;

        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            state.Accumulator = 0f;
            state.SetProgress(0f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            bool inside = context.IsInsideZone(zoneId);
            bool phaseAllows = !requireWavePhase || context.Phase == RunPhase.Wave;

            // TWO CONDITIONS, NEVER ONE. These were fused once — a single
            // `phaseAllows && inside` chose the branch — and that `&&` deleted
            // holds the player had legitimately earned: standing perfectly still
            // on the pad when the wave ended fell straight into the reset branch
            // and zeroed. With the shipped defaults (45 s, requireWavePhase,
            // resetOnLeave) the whole hold then had to fit inside one
            // uninterrupted Wave phase — and a wave ends when the last drone
            // dies, not on a clock the player controls — so the default-authored
            // objective was plausibly impossible to complete.
            //
            // The phase gate PAUSES. Only leaving RESETS. They are different
            // rules about different things and must read as different branches.
            if (inside && phaseAllows)
            {
                state.Accumulator += deltaTime;
            }
            else if (!inside && resetOnLeave)
            {
                // Zeroed on the frame they leave, not decayed: a hold that drains
                // slowly is a hold the player cannot read, and every attempt to
                // show it on a bar looks like a bug.
                state.Accumulator = 0f;
            }

            state.SetProgress(ObjectiveMath.Progress01(state.Accumulator, holdSeconds));
            if (state.Accumulator >= holdSeconds) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            into.Append(' ');
            ObjectiveMath.AppendSeconds(into, Mathf.Max(0f, holdSeconds - state.Accumulator));
        }
    }
}
