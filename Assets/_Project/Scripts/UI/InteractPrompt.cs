#nullable enable
using CoD.Core;
using CoD.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// The "HOLD F" line and its fill bar.
    ///
    /// Reads PlayerInteractor and never drives it. The prompt string is built by
    /// the interactable and stored once, so showing it costs a reference
    /// comparison per frame and a text assignment only when the TARGET changes —
    /// not when the hold advances, which is what the bar is for.
    ///
    /// The bar is a filled Image rather than text: a number counting up reads as
    /// data, and a bar filling reads as a thing you are doing. It is also the
    /// only feedback that a released hold is DRAINING rather than cancelled,
    /// which is the one behaviour of the interaction system a player has to
    /// learn by seeing it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractPrompt : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor? _interactor = null;
        [SerializeField] private Text? _promptLabel = null;
        [Tooltip("Image Type must be Filled, or the hold will be invisible.")]
        [SerializeField] private Image? _holdBar = null;

        // The last interactable we printed for. Compared by reference: the
        // prompt string cannot change without the target changing.
        private IInteractable? _lastTarget;
        private float _lastFill = -1f;

        private void LateUpdate()
        {
            if (_interactor == null) return;

            IInteractable? target = _interactor.Current;
            if (!ReferenceEquals(target, _lastTarget))
            {
                _lastTarget = target;
                if (_promptLabel != null) _promptLabel.text = target != null ? target.Prompt : string.Empty;
            }

            if (_holdBar == null) return;

            float fill = target != null ? _interactor.HoldProgress01 : 0f;
            // Mathf.Approximately rather than ==: the fill is a lerped float and
            // an exact compare would assign the Image every frame forever, which
            // dirties the canvas for no visible change.
            if (Mathf.Approximately(fill, _lastFill)) return;

            _lastFill = fill;
            _holdBar.fillAmount = fill;
            // Hidden at zero rather than drawn empty. An empty bar under every
            // prompt reads as "this is broken", and an instant interactable has
            // no hold at all.
            if (_holdBar.enabled != fill > 0f) _holdBar.enabled = fill > 0f;
        }
    }
}
