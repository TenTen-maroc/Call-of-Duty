#nullable enable
using CoD.Core;
using CoD.Waves;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// Permadeath, on screen. Shows the round reached against the best round on
    /// record — the one number the whole game is played for — and restarts.
    ///
    /// The record is read from the save AFTER the runner has written it, so a new
    /// best shows the run that just set it rather than the previous one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverPanel : MonoBehaviour
    {
        [SerializeField] private WaveRunner? _runner = null;
        [SerializeField] private RunContext? _run = null;
        [SerializeField] private GameObject? _root = null;
        [SerializeField] private Text? _titleLabel = null;
        [SerializeField] private Text? _detailLabel = null;
        [Tooltip("Optional. Restart is ignored while paused.")]
        [SerializeField] private PausePanel? _pause = null;

        private void OnEnable()
        {
            if (_runner != null) _runner.PhaseChanged += OnPhaseChanged;
            Show(false);
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.PhaseChanged -= OnPhaseChanged;
        }

        private void OnPhaseChanged(RunPhase phase)
        {
            bool over = phase == RunPhase.GameOver;
            Show(over);
            if (over) Redraw();
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        private void Redraw()
        {
            if (_run == null) return;
            RunState state = _run.State;
            SaveData save = _run.Save;

            if (_titleLabel != null) _titleLabel.text = "YOU DIED";
            if (_detailLabel != null)
            {
                // Asked, not re-derived. Comparing RoundReached against bestRound
                // here read a value RecordRunEnded had ALREADY raised, so `>=` was
                // true for every run that merely tied the record — and true in
                // Sandbox, where RecordRunEnded writes nothing at all and there was
                // no record to have beaten. RunContext knows which of those
                // actually happened; this panel does not need to guess.
                string record = _run.Mode == GameMode.Sandbox
                    ? "SANDBOX  —  NOT RECORDED"
                    : _run.SetANewRecord ? "NEW BEST" : "BEST  " + save.bestRound;

                _detailLabel.text =
                    "ROUND " + state.RoundReached + "        KILLS " + state.Kills + "\n" +
                    record + "\n\n" +
                    "R)  run it again";
            }
        }

        private void Update()
        {
            if (_runner == null || _runner.Phase != RunPhase.GameOver) return;
            // Same one-frame rule the shop uses — see PausePanel.OwnsInputThisFrame.
            if (_pause != null && _pause.OwnsInputThisFrame) return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[Key.R].wasPressedThisFrame) _runner.RestartRun();
        }
    }
}
