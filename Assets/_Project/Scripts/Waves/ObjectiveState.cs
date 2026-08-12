#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Where one objective has got to. Four values, and the two terminal ones are
    /// deliberately separate: a mission that ended because a critical objective
    /// FAILED wants a different screen, a different line of comms and a different
    /// save write than one that simply has an objective it has not reached yet.
    ///
    /// <see cref="Pending"/> is first, so the default value of an
    /// <see cref="ObjectiveState"/> is "not started" rather than something a
    /// director could mistake for progress.
    /// </summary>
    public enum ObjectiveStatus
    {
        /// <summary>Authored, not yet begun. The value of a zeroed state.</summary>
        Pending,

        /// <summary>Begun and ticking. The only status <see cref="MissionObjective.Tick"/> expects to see.</summary>
        Active,

        /// <summary>Satisfied. Nothing can un-satisfy it — an objective never goes backwards.</summary>
        Complete,

        /// <summary>Broken. Ends the mission if the objective is <see cref="MissionObjective.Critical"/>.</summary>
        Failed,
    }

    /// <summary>
    /// Every per-instance value an objective is allowed to keep.
    ///
    /// It is a struct, and it is passed by `ref`, because that is the whole
    /// mechanism behind rule 1 of <see cref="MissionObjective"/>: the asset holds
    /// numbers and text, the state holds the count, and one asset can therefore
    /// be shared by twelve missions without any of them seeing another's
    /// progress. A class here would work exactly as well right up until two
    /// missions ran the same objective asset, which is the case the whole design
    /// exists to make free.
    ///
    /// The fields are deliberately generic — a counter, an accumulator, a
    /// deadline — rather than one struct per objective type. Objectives are DATA;
    /// a per-type state struct would mean a per-type director branch, and the
    /// point of the family is that the director never learns what kind of
    /// objective it is holding.
    ///
    /// Members that do not mutate are marked `readonly` on purpose. Without it,
    /// every read through an `in` parameter silently copies the whole struct
    /// first (the compiler cannot know the member is safe), so the cheap thing
    /// quietly becomes the expensive thing in a per-frame path.
    /// </summary>
    public struct ObjectiveState
    {
        /// <summary>Sentinel for "this step is not timed". Not a tuning number — the absence of one.</summary>
        public const float NO_DEADLINE = 0f;

        public ObjectiveStatus Status;

        /// <summary>
        /// The counter's value at the instant the step BEGAN. Every counting
        /// objective measures against this rather than against zero, because a
        /// mission's third step asking for two more kills must not be satisfied
        /// by the forty the player got during step one.
        /// </summary>
        public int Baseline;

        /// <summary>Progress in whole things: waves survived, kills taken, targets down.</summary>
        public int Counter;

        /// <summary>Progress in seconds: time held on a pad, time spent extracting.</summary>
        public float Accumulator;

        /// <summary>
        /// Absolute time this step expires, or <see cref="NO_DEADLINE"/>. Written
        /// once by <see cref="MissionObjective.BeginStep"/> from the STEP's time
        /// limit and checked uniformly by the director — see MissionConfig.Step
        /// for why timing is not an objective type.
        /// </summary>
        public float Deadline;

        /// <summary>0..1 readout for the HUD. Objectives keep it current so the UI never has to know what it is watching.</summary>
        public float Progress;

        public readonly bool IsActive => Status == ObjectiveStatus.Active;

        /// <summary>Complete or Failed. The director stops ticking a resolved objective.</summary>
        public readonly bool IsResolved => Status == ObjectiveStatus.Complete || Status == ObjectiveStatus.Failed;

        /// <summary>
        /// A fresh Active state carrying the step's deadline.
        ///
        /// Assigning rather than mutating is the point: a step that begins reuses
        /// whatever state slot the director has, and a leftover Accumulator from
        /// the previous step would read as progress nobody made.
        /// </summary>
        public static ObjectiveState Begun(float now, float timeLimitSeconds) => new()
        {
            Status = ObjectiveStatus.Active,
            // A zero limit means untimed, so `now + 0` would be a deadline in the
            // past and would fail the step on its first tick.
            Deadline = timeLimitSeconds > 0f ? now + timeLimitSeconds : NO_DEADLINE,
        };

        public readonly bool IsPastDeadline(float now) => Deadline > NO_DEADLINE && now >= Deadline;

        /// <summary>Seconds left on the step, or 0 when it is untimed. For the HUD; never a gameplay test.</summary>
        public readonly float SecondsRemaining(float now) =>
            Deadline > NO_DEADLINE ? Mathf.Max(0f, Deadline - now) : 0f;

        /// <summary>Progress is forced to 1 as well: a complete objective showing a 7/8 bar reads as a bug to the player.</summary>
        public void MarkComplete()
        {
            Status = ObjectiveStatus.Complete;
            Progress = 1f;
        }

        /// <summary>Progress is left where it stood — how far the player got before it broke is worth showing.</summary>
        public void MarkFailed() => Status = ObjectiveStatus.Failed;

        /// <summary>Clamped here rather than at eight call sites, and NaN-proof: a zero target must not produce a bar of NaN.</summary>
        public void SetProgress(float value) => Progress = Mathf.Clamp01(float.IsNaN(value) ? 0f : value);
    }
}
