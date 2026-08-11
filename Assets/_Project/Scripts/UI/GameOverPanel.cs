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
                bool newBest = state.RoundReached >= save.bestRound && state.RoundReached > 0;
                _detailLabel.text =
                    "ROUND " + state.RoundReached + "        KILLS " + state.Kills + "\n" +
                    (newBest ? "NEW BEST" : "BEST  " + save.bestRound) + "\n\n" +
                    "R)  run it again";
            }
        }

        private void Update()
        {
            if (_runner == null || _runner.Phase != RunPhase.GameOver) return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[Key.R].wasPressedThisFrame) _runner.RestartRun();
        }
    }
}
