#nullable enable
using System.Text;
using CoD.Core;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Use N things of one kind: hack the terminal, plant the charges, grab the
    /// intel. The kind is an enum indexing a counter array rather than a string
    /// tag — a typo in a string id is a mission that can never be completed and
    /// nothing anywhere says why.
    ///
    /// It is Core's <see cref="InteractKind"/>, the one the player raises and
    /// <c>InteractPoint</c> is authored with. There was briefly a second enum
    /// here for the mission layer's own use, translated at the director; two
    /// enums for one concept is a mapping that has to be maintained by hand and
    /// silently miscounts the day it is not.
    ///
    /// The interaction itself — the hold, the prompt, the distance-and-facing
    /// test — belongs to the interaction component and the director. This
    /// objective only counts, which is why it needs no scene to be tested.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_Interact", menuName = "CoD/Objectives/Interact", order = 7)]
    public sealed class Obj_Interact : MissionObjective
    {
        // APPEND ONLY to InteractKind. THIS is the field that makes that rule
        // load-bearing: Unity serializes the enum into the .asset as a raw int,
        // so reordering its members re-points every authored objective in the
        // repo at a different kind, with no error and no diff to notice.
        [Tooltip("Which kind of thing has to be used.")]
        public InteractKind kind = InteractKind.Terminal;

        [Tooltip("How many, FROM HERE. Anything used in an earlier step does not count.")]
        [Range(1, 20)] public int count = 1;

        public override void Begin(in ObjectiveContext context, ref ObjectiveState state)
        {
            state.Baseline = context.Progress.InteractionsOf(kind);
            state.Counter = 0;
            state.SetProgress(0f);
        }

        public override void Tick(in ObjectiveContext context, ref ObjectiveState state, float now, float deltaTime)
        {
            state.Counter = Mathf.Max(0, context.Progress.InteractionsOf(kind) - state.Baseline);
            state.SetProgress(ObjectiveMath.Progress01(state.Counter, count));
            if (state.Counter >= count) state.MarkComplete();
        }

        public override void Describe(StringBuilder into, in ObjectiveState state)
        {
            into.Append(title);
            // A one-of-one interaction has no interesting count, and "1/1" on the
            // HUD is noise the player has to read past.
            if (count <= 1) return;
            into.Append(' ');
            ObjectiveMath.AppendInt(into, Mathf.Min(state.Counter, count));
            into.Append('/');
            ObjectiveMath.AppendInt(into, count);
        }
    }
}
