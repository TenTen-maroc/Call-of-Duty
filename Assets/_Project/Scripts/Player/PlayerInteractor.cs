#nullable enable
using System;
using CoD.Core;
using UnityEngine;

namespace CoD.Player
{
    /// <summary>
    /// Turns "the player is near a thing and pressed F" into one Interact call.
    ///
    /// Everything it needs is serialized in. Nothing is looked up per frame,
    /// there is no static state, and it allocates nothing while running — the
    /// registry hands back an already-typed reference and the prompt string is
    /// pre-built by the interactable.
    ///
    /// The hold is the interesting part. Planting a charge or holding an extract
    /// has to be interruptible, because the whole point of a hold is that it
    /// costs you the seconds during which you cannot shoot back. So a released
    /// hold DRAINS rather than resetting to zero: a slipped finger loses ground
    /// instead of the whole attempt, and walking away still cancels it, because
    /// leaving range drops the target entirely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private InteractionConfig? _config = null;
        [SerializeField] private InteractableRegistry? _registry = null;
        [SerializeField] private PlayerInput? _input = null;
        [Tooltip("Where the player is looking. Facing decides which of two nearby things is meant.")]
        [SerializeField] private PlayerLook? _look = null;
        [Tooltip("Interaction stops mattering once you are dead.")]
        [SerializeField] private Health? _health = null;

        private IInteractable? _current;
        private float _held;

        /// <summary>What the HUD should be prompting for, or null. Read every frame; never allocates.</summary>
        public IInteractable? Current => _current;

        /// <summary>0..1 for the hold ring. Always 0 for an instant interactable.</summary>
        public float HoldProgress01 { get; private set; }

        /// <summary>
        /// Raised once per completed interaction. The mission layer counts these
        /// by kind; it never learns that this component exists, and this
        /// component never learns there is a mission.
        /// </summary>
        public event Action<InteractKind>? Interacted;

        private void Update()
        {
            if (_config == null || _registry == null || _input == null || _look == null) return;
            if (_health != null && !_health.IsAlive)
            {
                Drop();
                return;
            }

            IInteractable? best = _registry.Best(
                transform.position, _look.AimRay.direction, _config.range, _config.minFacing);

            // Changing target abandons the hold outright rather than carrying it
            // over. Progress earned on one charge must not finish a different one.
            if (!ReferenceEquals(best, _current))
            {
                _current = best;
                _held = 0f;
            }

            if (_current == null)
            {
                HoldProgress01 = 0f;
                return;
            }

            float required = _current.HoldSeconds;

            // Instant: a tap, not a hold. Read the press edge rather than the
            // held state or walking through a door with the key already down
            // would re-trigger it every frame.
            if (required <= 0f)
            {
                if (_input.InteractPressed) Complete();
                return;
            }

            if (_input.InteractHeld)
            {
                _held += Time.deltaTime;
                if (_held >= required)
                {
                    Complete();
                    return;
                }
            }
            else
            {
                _held = Mathf.Max(0f, _held - Time.deltaTime * _config.holdDecayRate);
            }

            HoldProgress01 = required > 0f ? Mathf.Clamp01(_held / required) : 0f;
        }

        private void Complete()
        {
            IInteractable? target = _current;
            // Cleared BEFORE the callback: Interact may despawn the object,
            // disable itself, or open a menu, and a stale reference held across
            // that is how a prompt ends up pointing at something that is gone.
            Drop();
            if (target == null) return;

            target.Interact();
            Interacted?.Invoke(target.Kind);
        }

        private void Drop()
        {
            _current = null;
            _held = 0f;
            HoldProgress01 = 0f;
        }
    }
}
