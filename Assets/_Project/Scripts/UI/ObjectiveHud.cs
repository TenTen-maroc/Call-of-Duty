#nullable enable
using System.Text;
using CoD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// What the player is supposed to be doing, and how far through it they are.
    ///
    /// Redrawn on the director's ObjectivesChanged event plus a slow tick, never
    /// every frame. Objective lines carry live numbers — a hold timer, a kill
    /// count — so they genuinely change between events, but they change at human
    /// speed, and rebuilding a string sixty times a second to show "12 / 20"
    /// allocates sixty strings and dirties the canvas sixty times for two frames
    /// of visible difference. The rest of this HUD already avoids that; so does
    /// this.
    ///
    /// Reads the director; never drives it. Absent in endless mode, where the
    /// director disables itself and this simply has nothing to show.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveHud : MonoBehaviour
    {
        [SerializeField] private MissionDirector? _director = null;
        [Tooltip("The objective list. Empty in endless mode.")]
        [SerializeField] private Text? _objectiveLabel = null;
        [Tooltip("Centre-screen, for MISSION COMPLETE / MISSION FAILED.")]
        [SerializeField] private Text? _bannerLabel = null;

        [Tooltip("Seconds between redraws of the live numbers. Not a tuning value — a refresh budget.")]
        private const float REFRESH_SECONDS = 0.1f;

        // Reused for the life of the component. The whole point of the
        // director's Describe taking a builder is that nobody builds a string
        // and throws it away.
        private readonly StringBuilder _builder = new(160);
        private string _lastText = string.Empty;
        private float _nextRefreshAt;

        private void OnEnable()
        {
            if (_director != null)
            {
                _director.ObjectivesChanged += Redraw;
                _director.MissionEnded += OnMissionEnded;
            }
            ClearBanner();
            Redraw();
        }

        private void OnDisable()
        {
            if (_director != null)
            {
                _director.ObjectivesChanged -= Redraw;
                _director.MissionEnded -= OnMissionEnded;
            }
        }

        private void Update()
        {
            // The event covers "a step resolved"; this covers "the number inside
            // a step moved". Both are needed: without the event a completed
            // objective would linger for up to a tenth of a second, and without
            // the tick a hold timer would never appear to count down.
            if (_director == null || !_director.IsRunning) return;
            if (Time.time < _nextRefreshAt) return;
            _nextRefreshAt = Time.time + REFRESH_SECONDS;
            Redraw();
        }

        private void Redraw()
        {
            if (_objectiveLabel == null || _director == null) return;

            _builder.Clear();
            _director.DescribeActive(_builder);

            // Compare BEFORE assigning, and compare EXPLICITLY.
            //
            // StringBuilder.Equals(string) is a trap: depending on which
            // overload the compiler picks it is either a span comparison or
            // object.Equals, and object.Equals compares references, so it would
            // be false every single time and quietly rebuild the string on every
            // tick — the exact cost this method exists to avoid, with nothing to
            // show that it is happening. A hand-written loop cannot be
            // mis-resolved, allocates nothing, and returns on the first
            // differing character.
            if (Matches(_builder, _lastText)) return;

            _lastText = _builder.ToString();
            _objectiveLabel.text = _lastText;
        }

        private static bool Matches(StringBuilder builder, string text)
        {
            if (builder.Length != text.Length) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (builder[i] != text[i]) return false;
            }
            return true;
        }

        private void OnMissionEnded(RunOutcome outcome)
        {
            if (_bannerLabel == null) return;
            _bannerLabel.text = outcome switch
            {
                RunOutcome.MissionComplete => "MISSION COMPLETE",
                RunOutcome.MissionFailed => "MISSION FAILED",
                RunOutcome.Abandoned => "MISSION ABORTED",
                // Died cannot reach here in campaign: a death is a checkpoint
                // rewind, and the runner raises PlayerDown instead of ending.
                _ => string.Empty,
            };
        }

        private void ClearBanner()
        {
            if (_bannerLabel != null) _bannerLabel.text = string.Empty;
        }
    }
}
