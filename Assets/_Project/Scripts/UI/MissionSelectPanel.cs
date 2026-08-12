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
    /// Missions unlock in order: the first unfinished one is playable and
    /// everything past it is not. Ordered content wants an ordered gate, and it
    /// also means a save with no records has exactly one legal choice, which is
    /// the least confusing first screen a campaign can have.
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

        /// <summary>
        /// The highest mission index the player may start: the first one without
        /// a completed record.
        /// </summary>
        private int HighestUnlocked()
        {
            if (_catalog == null || _settings == null) return 0;
            for (int i = 0; i < _catalog.Count; i++)
            {
                MissionConfig? mission = _catalog.At(i);
                if (mission == null) return i;
                MissionRecord? record = _settings.FindRecord(mission.stableId);
                if (record == null || !record.completed) return i;
            }
            // Everything is finished; the last one stays replayable.
            return Mathf.Max(0, _catalog.Count - 1);
        }

        private void Launch(int index)
        {
            if (_catalog == null || _settings == null) return;
            MissionConfig? mission = _catalog.At(index);
            if (mission == null) return;
            if (index > HighestUnlocked()) return;

            // Both halves of the channel, in one write: campaign is a save AXIS,
            // and the mission id is what the director resolves on the other side.
            // GameMode stays Run, because a campaign mission still plays by Run
            // rules -- Sandbox is the orthogonal choice and this does not touch it.
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
                    int unlocked = HighestUnlocked();
                    for (int i = 0; i < _catalog.Count; i++)
                    {
                        AppendMission(i, unlocked);
                    }
                }

                _bodyLabel.text = _builder.ToString();
            }

            if (_footerLabel != null) _footerLabel.text = "W/S) move    ENTER) start    ESC) back";
        }

        private void AppendMission(int index, int unlocked)
        {
            MissionConfig? mission = _catalog != null ? _catalog.At(index) : null;
            if (mission == null) return;

            _builder.Append(_cursor.Index == index ? ">  " : "   ");
            _builder.Append(index + 1).Append(". ");

            if (index > unlocked)
            {
                // Named but not described: knowing a mission exists is part of
                // the pull, and knowing what happens in it is not.
                _builder.Append("[LOCKED]").Append('\n');
                return;
            }

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
