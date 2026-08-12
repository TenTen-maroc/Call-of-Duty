#nullable enable
using System.Text;
using CoD.Core;
using CoD.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// The front door. Title, the record the whole game is played for, the two
    /// modes CLAUDE.md locked in, settings, and a way out.
    ///
    /// Like PausePanel, this component is the single input owner for its screen
    /// and drives the shared settings page itself rather than letting it poll the
    /// keyboard independently.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuPanel : MonoBehaviour
    {
        // Campaign first: it is the headline mode, and row order is the order
        // of the pitch.
        private const int RowCampaign = 0;
        private const int RowRun = 1;
        private const int RowSandbox = 2;
        private const int RowSettings = 3;
        private const int RowQuit = 4;
        private const int RowCount = 5;

        [SerializeField] private SettingsHub? _settings = null;
        [SerializeField] private GameObject? _root = null;
        [SerializeField] private Text? _titleLabel = null;
        [SerializeField] private Text? _recordLabel = null;
        [SerializeField] private Text? _bodyLabel = null;
        [SerializeField] private Text? _footerLabel = null;
        [SerializeField] private SettingsPanel? _settingsPanel = null;
        [SerializeField] private MissionSelectPanel? _missionPanel = null;
        [Tooltip("The scene both modes load. Same scene, different starting money and rules.")]
        [SerializeField] private string _gameSceneName = "10_GreyBox";

        private readonly StringBuilder _builder = new(384);
        private readonly MenuCursor _cursor = new(RowCount);

        private void OnEnable()
        {
            if (_settingsPanel != null) _settingsPanel.Closed += OnSettingsClosed;
            if (_missionPanel != null) _missionPanel.Closed += OnSettingsClosed;
        }

        private void OnDisable()
        {
            if (_settingsPanel != null) _settingsPanel.Closed -= OnSettingsClosed;
            if (_missionPanel != null) _missionPanel.Closed -= OnSettingsClosed;
        }

        private void OnSettingsClosed()
        {
            Show(true);
            Redraw();
        }

        private void Start()
        {
            // The menu is the one screen that must never be time-frozen. Quitting
            // to the menu from a pause restores the clock first, but a crash or a
            // future code path that forgets would leave a menu that ignores every
            // key and looks like a hang.
            Time.timeScale = 1f;
            PlayerLook.SetCursorLocked(false);

            // Start on whatever was played last. One less keypress on the path
            // people take ninety-nine times out of a hundred.
            if (_settings != null)
            {
                // Campaign wins when it was the last thing played, because the
                // campaign is the mode you come back TO. It is read from the
                // content axis, not from lastMode -- a campaign mission still
                // plays by Run rules, so lastMode says Run for both.
                SaveData save = _settings.Save;
                _cursor.SetIndex(save.campaignSelected
                    ? RowCampaign
                    : save.lastMode == GameMode.Sandbox ? RowSandbox : RowRun);
            }

            Show(true);
            Redraw();
        }

        private void Update()
        {
            if (_settingsPanel != null && _settingsPanel.IsOpen)
            {
                _settingsPanel.HandleInput();
                return;
            }

            if (_missionPanel != null && _missionPanel.IsOpen)
            {
                _missionPanel.Tick();
                return;
            }

            int vertical = MenuInput.VerticalStep();
            if (vertical != 0)
            {
                _cursor.Move(vertical);
                Redraw();
            }

            if (MenuInput.ConfirmPressed()) Activate(_cursor.Index);
        }

        private void Activate(int row)
        {
            switch (row)
            {
                case RowCampaign:
                    Show(false);
                    _missionPanel?.Open();
                    break;
                case RowRun: StartGame(GameMode.Run); break;
                case RowSandbox: StartGame(GameMode.Sandbox); break;
                case RowSettings:
                    Show(false);
                    _settingsPanel?.Open();
                    break;
                case RowQuit:
                    GameLog.Info("Quit from the main menu.", this);
                    Application.Quit();
                    break;
            }
        }

        private void StartGame(GameMode mode)
        {
            // The mode is written to the save and read back by RunContext in the
            // next scene. A static field would be the obvious carrier and is
            // exactly what this project bans: Domain Reload is off, so it would
            // survive into the following Play session and start a Run in Sandbox.
            _settings?.SetLastMode(mode);
            // CLEARS the campaign axis, and this line is load-bearing. Without
            // it, playing a mission and then choosing START RUN would leave
            // campaignSelected true in the save, the director would resolve the
            // same mission again, and the player would get a mission they did
            // not ask for with nothing on screen explaining why.
            _settings?.SetCampaign(false, string.Empty);
            GameLog.Info("Starting a " + mode + " in " + _gameSceneName, this);
            SceneManager.LoadScene(_gameSceneName);
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        private void Redraw()
        {
            if (_titleLabel != null) _titleLabel.text = "CALL OF DUTY";

            if (_recordLabel != null && _settings != null)
            {
                SaveData save = _settings.Save;
                _builder.Clear();
                _builder.Append(save.bestRound > 0 ? "BEST ROUND  " + save.bestRound : "NO RUN RECORDED YET");
                _builder.Append("        RUNS  ").Append(save.totalRuns);
                _builder.Append("        KILLS  ").Append(save.totalKills);
                _recordLabel.text = _builder.ToString();
            }

            if (_bodyLabel != null)
            {
                _builder.Clear();
                AppendRow(RowCampaign, "CAMPAIGN", "missions, checkpoints, the story");
                AppendRow(RowRun, "START RUN", "earned power, permadeath, sets the record");
                AppendRow(RowSandbox, "SANDBOX", "everything affordable, cheat console, no record");
                AppendRow(RowSettings, "SETTINGS", "sensitivity, field of view, volume");
                AppendRow(RowQuit, "QUIT", string.Empty);
                _bodyLabel.text = _builder.ToString();
            }

            if (_footerLabel != null) _footerLabel.text = "W/S) move    ENTER) select";
        }

        private void AppendRow(int row, string label, string note)
        {
            _builder.Append(_cursor.Index == row ? ">  " : "   ").Append(label);
            if (note.Length > 0)
            {
                int padding = 16 - label.Length;
                if (padding > 0) _builder.Append(' ', padding);
                _builder.Append(note);
            }
            _builder.Append('\n');
        }
    }
}
