#nullable enable
using CoD.Core;
using CoD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// Wave number, enemies left, money, and the banner that announces what is
    /// about to happen. Reads the WaveRunner; never drives it.
    ///
    /// Text is rebuilt only when a number actually changes. Assigning `Text.text`
    /// every frame allocates a string every frame and dirties the canvas — the
    /// same quiet leak the ammo readout already avoids.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaveHud : MonoBehaviour
    {
        [SerializeField] private WaveRunner? _runner = null;
        [SerializeField] private RunContext? _run = null;
        [SerializeField] private Text? _waveLabel = null;
        [SerializeField] private Text? _enemiesLabel = null;
        [SerializeField] private Text? _moneyLabel = null;
        [Tooltip("Big centre-screen announcements: countdown, wave cleared, shop open.")]
        [SerializeField] private Text? _bannerLabel = null;

        private int _lastWave = -1;
        private int _lastEnemies = -1;
        private int _lastMoney = -1;
        private int _lastCountdown = -1;
        private bool _lastHeld;
        private RunPhase _lastPhase = (RunPhase)(-1);

        private void OnEnable()
        {
            if (_run != null) _run.MoneyChanged += OnMoneyChanged;
            if (_runner != null) _runner.PhaseChanged += OnPhaseChanged;
        }

        private void OnDisable()
        {
            if (_run != null) _run.MoneyChanged -= OnMoneyChanged;
            if (_runner != null) _runner.PhaseChanged -= OnPhaseChanged;
        }

        private void OnMoneyChanged(int money)
        {
            if (_moneyLabel == null || money == _lastMoney) return;
            _lastMoney = money;
            _moneyLabel.text = "$ " + money;
        }

        private void OnPhaseChanged(RunPhase phase) => _lastPhase = (RunPhase)(-1);   // force a banner rebuild

        private void Update()
        {
            if (_runner == null) return;

            if (_waveLabel != null && _runner.WaveNumber != _lastWave)
            {
                _lastWave = _runner.WaveNumber;
                // Concatenation is fine here: this runs when the wave NUMBER
                // changes, which is once every forty-five seconds, not per frame.
                WaveConfig? config = _runner.CurrentWave;
                string name = config != null ? config.displayName : string.Empty;
                _waveLabel.text = _lastWave <= 0
                    ? "GET READY"
                    : string.IsNullOrEmpty(name) ? "WAVE " + _lastWave : "WAVE " + _lastWave + " — " + name;
            }

            if (_enemiesLabel != null)
            {
                int remaining = _runner.Phase == RunPhase.Wave ? _runner.EnemiesRemaining : 0;
                if (remaining != _lastEnemies)
                {
                    _lastEnemies = remaining;
                    _enemiesLabel.text = remaining > 0 ? "ENEMIES " + remaining : string.Empty;
                }
            }

            UpdateBanner();
        }

        private void UpdateBanner()
        {
            if (_bannerLabel == null || _runner == null) return;

            RunPhase phase = _runner.Phase;
            int seconds = Mathf.CeilToInt(_runner.PhaseTimeRemaining);

            // A SUSPENDED runner is not counting down to anything, and saying so
            // is the whole point.
            //
            // A mission director holds the wave loop during any step that does
            // not want enemies -- walking to a control point, hacking a
            // terminal, extracting. While it is held the runner never left
            // RunPhase.Countdown, and _phaseEndsAt is still zero because Start
            // early-returned before EnterCountdown ever ran. So PhaseTimeRemaining
            // is 0, and Mathf.Max(1, ...) below floors the display at one --
            // which printed "WAVE 1 IN 1", permanently, on the first screen of
            // the first mission. The player is promised a wave in one second and
            // it never arrives, which reads as a frozen game rather than as a
            // quiet objective.
            bool held = _runner.Suspended;

            // Three keys now: the countdown changes text while the phase stays
            // the same, and the hold changes it while BOTH stay the same.
            if (phase == _lastPhase && seconds == _lastCountdown && held == _lastHeld) return;
            _lastPhase = phase;
            _lastCountdown = seconds;
            _lastHeld = held;

            if (held)
            {
                // Silent, not "PAUSED". The objective HUD is already telling the
                // player what to do; a second banner competing with it is noise.
                _bannerLabel.text = string.Empty;
                return;
            }

            _bannerLabel.text = phase switch
            {
                RunPhase.Countdown => "WAVE " + (_runner.WaveNumber + 1) + " IN " + Mathf.Max(1, seconds),
                RunPhase.Cleared => "WAVE " + _runner.WaveNumber + " CLEARED",
                RunPhase.Shop => string.Empty,      // the shop panel speaks for itself
                RunPhase.GameOver => string.Empty,  // so does the game-over panel
                _ => string.Empty,
            };
        }
    }
}
