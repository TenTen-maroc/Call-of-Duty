#nullable enable
using System;
using System.Text;
using CoD.Core;
using CoD.Waves;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// Pick a mission.
    ///
    /// Driven by whichever menu opened it, never from its own Update — two
    /// components polling the same key in the same frame is a race, because
    /// MonoBehaviour execution order is undefined. Same contract as
    /// SettingsPanel, deliberately.
    ///
    /// Every authored mission is selectable from a fresh save. Completion
    /// records remain visible as history, but they are not access gates: a
    /// player reviewing a new slice should not have to clear older prototype
    /// content before reaching it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionSelectPanel : MonoBehaviour
    {
        [SerializeField] private SettingsHub? _settings = null;
        [SerializeField] private MissionCatalog? _catalog = null;
        [SerializeField] private GameObject? _root = null;
        [SerializeField] private Text? _titleLabel = null;
        [SerializeField] private Text? _bodyLabel = null;
        [SerializeField] private Text? _footerLabel = null;
        [Tooltip("Fallback when a mission does not name its own arena.")]
        [SerializeField] private string _defaultSceneName = "10_GreyBox";

        private readonly StringBuilder _builder = new(512);
        private readonly MenuCursor _cursor = new(1);

        /// <summary>Raised when the player backs out. The host re-takes input.</summary>
        public event Action? Closed;

        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake() => Show(false);

        public void Open()
        {
            // Count can change between visits only if the catalog changes, but
            // resetting both keeps the cursor from pointing past the end after
            // any edit.
            _cursor.Count = Mathf.Max(1, _catalog != null ? _catalog.Count : 1);
            _cursor.Reset();
            Show(true);
            Redraw();
        }

        public void Close()
        {
            if (!IsOpen) return;
            Show(false);
            Closed?.Invoke();
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>Called by the host while this page owns the screen.</summary>
        public void Tick()
        {
            if (!IsOpen) return;

            if (MenuInput.BackPressed() || MenuInput.EscapePressed())
            {
                Close();
                return;
            }

            int step = MenuInput.VerticalStep();
            if (step != 0)
            {
                _cursor.Move(step);
                Redraw();
            }

            if (MenuInput.ConfirmPressed()) Launch(_cursor.Index);
        }

        private void Launch(int index)
        {
            if (_catalog == null || _settings == null) return;
            MissionConfig? mission = _catalog.At(index);
            if (mission == null) return;

            // Both halves of the channel, in one write: campaign is a save AXIS,
            // and the mission id is what the director resolves on the other side.
            // GameMode stays Run, because a campaign mission still plays by Run
            // rules -- Sandbox is the orthogonal choice and this does not touch it.
            // BOTH axes, every launch. Campaign says WHICH CONTENT; the mode
            // says which RULES, and a mission plays by Run rules. Writing only
            // the content axis meant a mission launched straight after a Sandbox
            // session inherited lastMode: Sandbox -- infinite money and the cheat
            // console, in the campaign, silently.
            _settings.SetLastMode(GameMode.Run);
            _settings.SetCampaign(true, mission.stableId);

            string scene = string.IsNullOrEmpty(mission.arenaScene) ? _defaultSceneName : mission.arenaScene;
            GameLog.Info("Starting mission " + mission.stableId + " in " + scene, this);
            SceneManager.LoadScene(scene);
        }

        private void Redraw()
        {
            if (_titleLabel != null) _titleLabel.text = "CAMPAIGN";

            if (_bodyLabel != null)
            {
                _builder.Clear();

                if (_catalog == null || _catalog.Count == 0)
                {
                    _builder.Append("   NO MISSIONS AUTHORED YET");
                }
                else
                {
                    for (int i = 0; i < _catalog.Count; i++)
                    {
                        AppendMission(i);
                    }
                }

                _bodyLabel.text = _builder.ToString();
            }

            if (_footerLabel != null) _footerLabel.text = "W/S) move    ENTER) start    ESC) back";
        }

        private void AppendMission(int index)
        {
            MissionConfig? mission = _catalog != null ? _catalog.At(index) : null;
            if (mission == null) return;

            _builder.Append(_cursor.Index == index ? ">  " : "   ");
            _builder.Append(index + 1).Append(". ");

            _builder.Append(mission.displayName);

            MissionRecord? record = _settings != null ? _settings.FindRecord(mission.stableId) : null;
            if (record != null && record.completed)
            {
                _builder.Append("   COMPLETE");
                if (record.bestRating > 0) _builder.Append("  ").Append(record.bestRating).Append('*');
                if (record.deaths > 0) _builder.Append("   DEATHS ").Append(record.deaths);
            }

            _builder.Append('\n');
        }
    }
}
