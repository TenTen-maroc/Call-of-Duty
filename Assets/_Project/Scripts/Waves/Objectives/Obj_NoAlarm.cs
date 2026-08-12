#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Do not be detected. The only objective in the family that can never
    /// complete under its own power — it exists to FAIL, and it is always
    /// authored as a parallel step running alongside whatever the player is
    /// actually doing.
    ///
    /// That is what <see cref="MissionObjective.CompletesWithMission"/> is for.
    /// Without it a stealth mission would sit forever on a rule the player was
    /// obeying perfectly: every other step complete, this one still Active,
    /// nothing left in the arena to change it. The director marks it Complete
    /// when the last completing objective finishes.
    ///
    /// It carries no numbers at all. The alarm is raised by the world — a guard
    /// seeing the player, a camera, a broken window — and the director records it
    /// once into <see cref="MissionProgress.RaiseAlarm"/>. This asset only reads
    /// the flag, which is rule 3 in its purest form: the tempting implementation
    /// here is an event subscription, and it is exactly the one that would survive
    /// into the next Play session.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_NoAlarm", menuName = "CoD/Objectives/No Alarm", order = 6)]
    public sealed class Obj_NoAlarm : MissionObjective
    {
        public override bool CompletesWithMission => true;

        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            // Starts full, unlike every counting objective: the player has not
            // been detected yet, so the readout is 100% and can only fall.
            state.SetProgress(1f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            if (!context.Progress.AlarmRaised) return;
            state.SetProgress(0f);
            state.MarkFailed();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            // Literals, not an interpolated string: this line is rebuilt every
            // frame the objective panel is on screen.
            into.Append(state.Status == ObjectiveStatus.Failed ? " — DETECTED" : " — UNDETECTED");
        }
    }
}
