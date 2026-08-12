#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Every interactable currently in the scene.
    ///
    /// A scene component, not a static — Domain Reload is disabled, so a static
    /// list would still hold the previous Play session's destroyed objects and
    /// the first prompt of the next session would point at a dead transform.
    /// Same reasoning as DroneRegistry, and deliberately the same shape.
    ///
    /// It also exists so the interactor never runs a scene search or a
    /// GetComponent per frame: things register when they turn on, and the
    /// per-frame cost is one walk over a short list of already-typed references.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractableRegistry : MonoBehaviour
    {
        private readonly List<IInteractable> _all = new(32);

        /// <summary>
        /// Raised after a completed interaction, by whoever completed it.
        ///
        /// It lives HERE rather than on PlayerInteractor because of the assembly
        /// graph: the player RAISES interactions and the mission layer COUNTS
        /// them, CoD.Player references only CoD.Core, and CoD.Waves does not
        /// reference CoD.Player at all. This registry is the one object both
        /// sides already hold a reference to, so routing the event through it
        /// costs no new dependency, no bridge component and no serialized field.
        ///
        /// Without it PlayerInteractor's event had no subscriber anywhere and
        /// MissionDirector.RecordInteraction had no caller, so every Interact
        /// objective was uncompletable -- the same silent, total failure as a
        /// zone nobody registered.
        /// </summary>
        public event System.Action<InteractKind>? Interacted;

        /// <summary>Called by the interactor once a use completes. Never by the interactable itself.</summary>
        public void RaiseInteracted(InteractKind kind) => Interacted?.Invoke(kind);

        public int Count => _all.Count;

        public void Register(IInteractable interactable)
        {
            if (!_all.Contains(interactable)) _all.Add(interactable);
        }

        public void Unregister(IInteractable interactable) => _all.Remove(interactable);

        /// <summary>
        /// The one the player most plausibly means, or null.
        ///
        /// Scored on FACING rather than distance once both are in range: with two
        /// terminals side by side, the one you are looking at is the one you
        /// want, and the one six inches closer is not.
        ///
        /// Allocation-free — an indexed for over a List, never a foreach over an
        /// interface, because iterating boxes the enumerator and this runs every
        /// frame the player is alive.
        /// </summary>
        public IInteractable? Best(Vector3 from, Vector3 forward, float range, float minFacing)
        {
            IInteractable? best = null;
            float bestFacing = minFacing;

            for (int i = 0; i < _all.Count; i++)
            {
                IInteractable candidate = _all[i];
                if (!candidate.CanInteract) continue;
                if (!Interaction.WithinFloorRadius(from, candidate.Position, range)) continue;

                float facing = Interaction.Facing(from, forward, candidate.Position);
                if (facing < bestFacing) continue;

                bestFacing = facing;
                best = candidate;
            }

            return best;
        }
    }
}
