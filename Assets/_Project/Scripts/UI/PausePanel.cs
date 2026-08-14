#nullable enable
using System.Text;
using CoD.Core;
using CoD.Player;
using CoD.Waves;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// Pause. Escape opens it, the world stops, the cursor comes back, and there
    /// is finally a way out of a run that is not alt-F4.
    ///
    /// This component is the single input owner while it is open: it drives the
    /// settings page itself rather than letting that page poll the keyboard, so
    /// one Escape press cannot both close the page and unpause the game.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PausePanel : MonoBehaviour
    {
        private const int RowResume = 0;
        private const int RowSettings = 1;
        private const int RowQuitToMenu = 2;
        private const int RowQuitToDesktop = 3;
        private const int RowCount = 4;

        [SerializeField] private GameObject? _root = null;
        [SerializeField] private Text? _titleLabel = null;
        [SerializeField] private Text? _bodyLabel = null;
        [SerializeField] private Text? _footerLabel = null;
        [SerializeField] private SettingsPanel? _settingsPanel = null;
        [Tooltip("Blocked while paused, so the camera does not turn under the panel.")]
        [SerializeField] private PlayerInput? _input = null;
        [Tooltip("Optional. Pausing is refused once the run is over — the death screen owns that moment.")]
        [SerializeField] private WaveRunner? _runner = null;
        [Tooltip("Abandoning a run still records the round it reached. See Activate().")]
        [SerializeField] private RunContext? _run = null;
        [Tooltip("Optional. Released before the clock is captured — see Pause().")]
        [SerializeField] private Hitstop? _hitstop = null;
        [SerializeField] private string _menuSceneName = "20_MainMenu";

        private readonly StringBuilder _builder = new(256);
        private readonly MenuCursor _cursor = new(RowCount);

        private bool _paused;
        private float _timeScaleBeforePause = 1f;
        private int _resumedOnFrame = -1;

        public bool IsPaused => _paused;

        /// <summary>
        /// True while paused AND for the remainder of the frame that unpaused.
        /// The other keyboard panels test THIS, not IsPaused.
        ///
        /// Resume() clears the flag from inside Update, so any panel whose Update
        /// happened to run later in the same frame saw IsPaused already false and
        /// consumed the very keypress that resumed the game. SPACE is this menu's
        /// confirm key and the shop's "next wave" key, so pressing RESUME during a
        /// shop break started the next wave as well — and MonoBehaviour order is
        /// undefined, so it did that on some machines and not others.
        /// </summary>
        public bool OwnsInputThisFrame => _paused || Time.frameCount == _resumedOnFrame;

        /// <summary>
        /// Hand the player's controls to a full-screen panel that is not this one.
        /// The shop uses it: with the shop open the player used to keep walking,
        /// jumping and firing behind it, and R and SPACE were live in the Player
        /// action map and the shop at the same time.
        ///
        /// It lives here because this component is already the single answer to
        /// "who is holding the keyboard" — a second component calling SetBlocked
        /// is how the two of them start disagreeing.
        /// </summary>
        public void SetPlayerControlsBlocked(bool blocked)
        {
            // Pause outranks every other panel while it is open.
            if (_paused) return;
            // And a request to GIVE CONTROL BACK is refused once the run is over,
            // because the shop closing and the run ending arrive as the same phase
            // change and the order between the two listeners is undefined.
            if (!blocked && _runner != null && _runner.Phase == RunPhase.GameOver) return;
            _input?.SetBlocked(blocked);
        }

        private void Awake() => Show(false);

        private void OnEnable()
        {
            if (_settingsPanel != null) _settingsPanel.Closed += OnSettingsClosed;
            if (_runner != null) _runner.PhaseChanged += OnPhaseChanged;
        }

        private void OnDisable()
        {
            if (_settingsPanel != null) _settingsPanel.Closed -= OnSettingsClosed;
            if (_runner != null) _runner.PhaseChanged -= OnPhaseChanged;

            // A scene load while paused would otherwise leave the next scene
            // frozen at timeScale 0 with no panel to unfreeze it — a hang that
            // looks exactly like a crash.
            if (_paused) RestoreTime();
        }

        /// <summary>
        /// Death takes the keyboard away, here, because this component is already
        /// the one thing that owns "who is holding the controls".
        ///
        /// Without it the run ends and NOTHING stops the player: the death screen
        /// draws over an arena the corpse is still walking and shooting around,
        /// the mouse stays captured, and pausing is refused (CanPause), so the
        /// only key that does anything is R. The game over is the moment the
        /// player is meant to read a number, not keep playing.
        /// </summary>
        private void OnPhaseChanged(RunPhase phase)
        {
            if (phase != RunPhase.GameOver) return;
            _input?.SetBlocked(true);
            PlayerLook.SetCursorLocked(false);
        }

        private void Update()
        {
            if (_settingsPanel != null && _settingsPanel.IsOpen)
            {
                _settingsPanel.HandleInput();
                return;
            }

            if (!_paused)
            {
                if (MenuInput.EscapePressed() && CanPause()) Pause();
                return;
            }

            int vertical = MenuInput.VerticalStep();
            if (vertical != 0)
            {
                _cursor.Move(vertical);
                Redraw();
            }

            if (MenuInput.ConfirmPressed()) Activate(_cursor.Index);
            else if (MenuInput.EscapePressed()) Resume();
        }

        private bool CanPause()
        {
            // Pausing the game-over screen would hide the one thing the player is
            // there to read, behind a menu whose Resume button resumes nothing.
            return _runner == null || _runner.Phase != RunPhase.GameOver;
        }

        public void Pause()
        {
            if (_paused) return;
            _paused = true;

            // Let go of a hitstop BEFORE capturing, and the order is the whole
            // point. A kill freezes the clock for a few dozen milliseconds; pause
            // during that window and the capture below records the FROZEN scale
            // as the thing to go back to, so the player resumes into permanent
            // near-stopped time with no way to fix it short of dying. Hitstop
            // declines to stomp a clock somebody else took, but it cannot stop
            // this line from reading the wrong number — only releasing first can.
            _hitstop?.Cancel();

            // Capture rather than assume 1: the sandbox console's slow-mo may
            // already own the clock, and resuming to 1 would silently cancel it.
            _timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            // fixedDeltaTime is deliberately NOT scaled here. Slow-mo scales it to
            // keep physics in step with a slower clock; at timeScale 0 no
            // FixedUpdate runs at all, and a fixedDeltaTime of 0 is an infinite
            // loop waiting to happen.

            _input?.SetBlocked(true);
            PlayerLook.SetCursorLocked(false);

            _cursor.Reset();
            Show(true);
            Redraw();
        }

        public void Resume()
        {
            if (!_paused) return;
            RestoreTime();
            Show(false);

            // The controls go back to whoever should hold them, which is not
            // always the player. Pausing during a shop break and resuming used to
            // hand the arena straight back while the shop was still covering the
            // screen — walking and firing under a full-screen menu, which is the
            // very thing SetPlayerControlsBlocked exists to stop.
            bool aPanelStillOwnsThem = _runner != null && _runner.Phase == RunPhase.Shop;
            _input?.SetBlocked(aPanelStillOwnsThem);
            PlayerLook.SetCursorLocked(true);
        }

        private void RestoreTime()
        {
            _paused = false;
            _resumedOnFrame = Time.frameCount;
            Time.timeScale = _timeScaleBeforePause;
        }

        private void Activate(int row)
        {
            switch (row)
            {
                case RowResume:
                    Resume();
                    break;
                case RowSettings:
                    // Hand the screen over. Two full-screen panels drawn at once
                    // is unreadable, and the settings page is full-screen.
                    Show(false);
                    _settingsPanel?.Open();
                    break;
                case RowQuitToMenu:
                    // Abandoning counts. You reached that round either way, and a
                    // record that only lands on death rewards suiciding to bank
                    // it — the opposite of what a permadeath score is for.
                    // RecordRunEnded itself refuses to write in Sandbox.
                    _run?.RecordRunEnded();
                    // Restore the clock BEFORE the load: the next scene inherits
                    // Time.timeScale, and a main menu at 0 accepts no input.
                    RestoreTime();
                    PlayerLook.SetCursorLocked(false);
                    SceneManager.LoadScene(_menuSceneName);
                    break;
                case RowQuitToDesktop:
                    // Records too, for the same reason QUIT TO MENU does. Losing
                    // the round you reached because you left by the other door is
                    // an inconsistency the player experiences as the game eating
                    // a run. Alt-F4 cannot be caught; our own button can.
                    _run?.RecordRunEnded();
                    RestoreTime();
                    GameLog.Info("Quit from the pause menu.", this);
                    Application.Quit();
                    break;
            }
        }

        private void OnSettingsClosed()
        {
            if (!_paused) return;
            Show(true);
            Redraw();
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        private void Redraw()
        {
            if (_titleLabel != null) _titleLabel.text = "PAUSED";

            if (_bodyLabel != null)
            {
                _builder.Clear();
                AppendRow(RowResume, "RESUME");
                AppendRow(RowSettings, "SETTINGS");
                AppendRow(RowQuitToMenu, "QUIT TO MENU");
                AppendRow(RowQuitToDesktop, "QUIT TO DESKTOP");
                _bodyLabel.text = _builder.ToString();
            }

            if (_footerLabel != null)
            {
                _footerLabel.text = "W/S) move    ENTER) select    ESC) resume\n"
                                    + "Quitting to the menu ENDS this run. The round you reached is kept.";
            }
        }

        private void AppendRow(int row, string label)
            => _builder.Append(_cursor.Index == row ? ">  " : "   ").Append(label).Append('\n');
    }
}
