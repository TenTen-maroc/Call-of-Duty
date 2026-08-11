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
        [SerializeField] private string _menuSceneName = "20_MainMenu";

        private readonly StringBuilder _builder = new(256);
        private readonly MenuCursor _cursor = new(RowCount);

        private bool _paused;
        private float _timeScaleBeforePause = 1f;

        public bool IsPaused => _paused;

        private void Awake() => Show(false);

        private void OnEnable()
        {
            if (_settingsPanel != null) _settingsPanel.Closed += OnSettingsClosed;
        }

        private void OnDisable()
        {
            if (_settingsPanel != null) _settingsPanel.Closed -= OnSettingsClosed;

            // A scene load while paused would otherwise leave the next scene
            // frozen at timeScale 0 with no panel to unfreeze it — a hang that
            // looks exactly like a crash.
            if (_paused) RestoreTime();
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
            _input?.SetBlocked(false);
            PlayerLook.SetCursorLocked(true);
            Show(false);
        }

        private void RestoreTime()
        {
            _paused = false;
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
