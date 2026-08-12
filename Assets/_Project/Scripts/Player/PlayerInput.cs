#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoD.Player
{
    /// <summary>
    /// Reads the .inputactions asset and exposes this frame's intent as plain
    /// values. Everything else in the player and weapon code asks this component
    /// instead of touching the Input System — so rebinding, gamepad support, or
    /// swapping the asset never reaches into gameplay code.
    ///
    /// Actions are looked up by name once in Awake. The New Input System only;
    /// Input.GetKey is the legacy API and is never used in this project.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInput : MonoBehaviour
    {
        [SerializeField] private InputActionAsset? _actions = null;
        [SerializeField] private string _actionMapName = "Player";

        private InputActionMap? _map;
        private InputAction? _move;
        private InputAction? _look;
        private InputAction? _fire;
        private InputAction? _aim;
        private InputAction? _reload;
        private InputAction? _jump;
        private InputAction? _sprint;
        private InputAction? _crouch;
        private InputAction? _interact;

        public Vector2 Move => _move?.ReadValue<Vector2>() ?? Vector2.zero;
        public Vector2 Look => _look?.ReadValue<Vector2>() ?? Vector2.zero;
        public bool FireHeld => _fire?.IsPressed() ?? false;
        public bool FirePressedThisFrame => _fire?.WasPressedThisFrame() ?? false;
        public bool AimHeld => _aim?.IsPressed() ?? false;
        public bool ReloadPressed => _reload?.WasPressedThisFrame() ?? false;
        public bool JumpPressed => _jump?.WasPressedThisFrame() ?? false;
        public bool SprintHeld => _sprint?.IsPressed() ?? false;
        public bool CrouchHeld => _crouch?.IsPressed() ?? false;

        /// <summary>The frame the key went down. Instant interactions only.</summary>
        public bool InteractPressed => _interact?.WasPressedThisFrame() ?? false;

        /// <summary>Still down. What a hold-to-plant reads, and why both exist.</summary>
        public bool InteractHeld => _interact?.IsPressed() ?? false;

        private void Awake()
        {
            if (_actions == null)
            {
                GameLogMissingAsset();
                return;
            }

            _map = _actions.FindActionMap(_actionMapName, throwIfNotFound: false);
            if (_map == null)
            {
                CoD.Core.GameLog.Error($"Input action map '{_actionMapName}' not found on '{_actions.name}'.", this);
                return;
            }

            _move = _map.FindAction("Move", throwIfNotFound: false);
            _look = _map.FindAction("Look", throwIfNotFound: false);
            _fire = _map.FindAction("Fire", throwIfNotFound: false);
            _aim = _map.FindAction("Aim", throwIfNotFound: false);
            _reload = _map.FindAction("Reload", throwIfNotFound: false);
            _jump = _map.FindAction("Jump", throwIfNotFound: false);
            _sprint = _map.FindAction("Sprint", throwIfNotFound: false);
            _crouch = _map.FindAction("Crouch", throwIfNotFound: false);
            _interact = _map.FindAction("Interact", throwIfNotFound: false);
        }

        private bool _blocked;

        /// <summary>True while a menu owns the keyboard. Read by tests and by the HUD.</summary>
        public bool IsBlocked => _blocked;

        /// <summary>
        /// Turn the whole action map off — what pause and the menus use. Every
        /// property above then reports "no input", so movement, look, firing and
        /// reloading all stop at their single source instead of each component
        /// growing its own `if (paused)`.
        /// </summary>
        public void SetBlocked(bool blocked)
        {
            _blocked = blocked;
            if (blocked) _map?.Disable();
            else if (isActiveAndEnabled) _map?.Enable();
        }

        // The blocked flag has to survive a disable/enable cycle: a component
        // re-enabled while a menu is open must not hand control back to a player
        // who is looking at a pause screen.
        private void OnEnable()
        {
            if (!_blocked) _map?.Enable();
        }

        private void OnDisable() => _map?.Disable();

        private void GameLogMissingAsset()
            => CoD.Core.GameLog.Error($"PlayerInput on '{name}' has no InputActionAsset assigned.", this);
    }
}
