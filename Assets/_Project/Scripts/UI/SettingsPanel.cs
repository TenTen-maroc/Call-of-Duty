#nullable enable
using System;
using System.Text;
using CoD.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>
    /// The settings screen, shared by the main menu and the pause menu. One
    /// component, two hosts — a second copy of this list is a second place for
    /// the FOV row to go out of date.
    ///
    /// Changes apply LIVE and are written to disk only when the page closes. That
    /// split is the whole point of SettingsHub.Apply() vs Persist(): dragging the
    /// FOV should show you the FOV, and it should not cost a file write per
    /// keypress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : MonoBehaviour
    {
        private const int RowSensitivity = 0;
        private const int RowFov = 1;
        private const int RowInvert = 2;
        private const int RowVolume = 3;
        private const int RowBack = 4;
        private const int RowCount = 5;

        /// <summary>Width of the text bars, in characters. A layout constant, not a tuning value.</summary>
        private const int BarCells = 14;

        [SerializeField] private SettingsHub? _settings = null;
        [Tooltip("Root object toggled with the page.")]
        [SerializeField] private GameObject? _root = null;
        [SerializeField] private Text? _bodyLabel = null;
        [SerializeField] private Text? _footerLabel = null;

        private readonly StringBuilder _builder = new(512);
        private readonly MenuCursor _cursor = new(RowCount);

        /// <summary>Raised when the player leaves the page. The host re-takes input.</summary>
        public event Action? Closed;

        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake() => Show(false);

        public void Open()
        {
            _cursor.Reset();
            Show(true);
            Redraw();
        }

        public void Close()
        {
            if (!IsOpen) return;
            Show(false);
            // One disk write per visit to this page, not one per keypress.
            _settings?.Persist();
            Closed?.Invoke();
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>
        /// Driven by whichever menu opened this page — never from its own
        /// Update. Two components polling the same Escape key in the same frame
        /// is a race: MonoBehaviour execution order is undefined, so closing this
        /// page could also unpause the game, or not, depending on the order Unity
        /// happened to pick. One input owner per screen removes the question.
        /// </summary>
        public void HandleInput()
        {
            if (!IsOpen) return;

            int vertical = MenuInput.VerticalStep();
            if (vertical != 0)
            {
                _cursor.Move(vertical);
                Redraw();
            }

            int horizontal = MenuInput.HorizontalStep();
            if (horizontal != 0 && Adjust(horizontal)) Redraw();

            if (MenuInput.ConfirmPressed())
            {
                if (_cursor.Index == RowBack) { Close(); return; }
                // Enter on a slider row toggles or nudges right, so the page is
                // usable without ever learning that left/right exist.
                if (Adjust(1)) Redraw();
            }

            if (MenuInput.BackPressed()) Close();
        }

        private bool Adjust(int direction)
        {
            if (_settings == null) return false;
            GameSettings settings = _settings.Current;

            switch (_cursor.Index)
            {
                case RowSensitivity: settings.StepMouseSensitivity(direction); break;
                case RowFov: settings.StepFovVertical(direction); break;
                case RowInvert: settings.SetInvertLook(!settings.InvertLook); break;
                case RowVolume: settings.StepMasterVolume(direction); break;
                default: return false;
            }

            // Apply, not Persist: the camera and the mixer follow immediately,
            // the disk write waits for Close().
            _settings.Apply();
            return true;
        }

        private void Redraw()
        {
            if (_bodyLabel == null || _settings == null) return;
            GameSettings settings = _settings.Current;

            _builder.Clear();
            AppendRow(RowSensitivity, "MOUSE SENSITIVITY");
            AppendBar(settings.SensitivityFraction);
            _builder.Append("  ").Append(settings.MouseSensitivity.ToString("0.00")).Append('\n');

            AppendRow(RowFov, "FIELD OF VIEW");
            AppendBar(settings.FovFraction);
            // Unity's field is VERTICAL. Showing both numbers is what stops the
            // classic "I typed 95 and everything went fisheye" report.
            _builder.Append("  ").Append(Mathf.RoundToInt(settings.FovVertical))
                    .Append("v  (~").Append(HorizontalFov(settings.FovVertical)).Append("h)\n");

            AppendRow(RowInvert, "INVERT LOOK");
            _builder.Append(settings.InvertLook ? "ON" : "OFF").Append('\n');

            AppendRow(RowVolume, "MASTER VOLUME");
            AppendBar(settings.VolumeFraction);
            _builder.Append("  ").Append(Mathf.RoundToInt(settings.MasterVolume * 100f)).Append("%\n");

            _builder.Append('\n');
            AppendRow(RowBack, "BACK");

            _bodyLabel.text = _builder.ToString();

            if (_footerLabel != null)
            {
                _footerLabel.text = "W/S) move    A/D) change    ENTER) select    ESC) back";
            }
        }

        private void AppendRow(int row, string label)
        {
            _builder.Append(_cursor.Index == row ? ">  " : "   ");
            _builder.Append(label);
            // Pad to a fixed column so the bars line up. Append(char, count)
            // never allocates an intermediate string the way PadRight does.
            int padding = 20 - label.Length;
            if (padding > 0) _builder.Append(' ', padding);
        }

        private void AppendBar(float fraction)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(fraction * BarCells), 0, BarCells);
            _builder.Append('[').Append('#', filled).Append('-', BarCells - filled).Append(']');
        }

        /// <summary>
        /// The horizontal FOV that a vertical one produces at 16:9. Shown, never
        /// stored — Unity's field is vertical and this project has already paid
        /// for that confusion once.
        /// </summary>
        private static int HorizontalFov(float vertical)
        {
            const float aspect = 16f / 9f;
            float radians = 2f * Mathf.Atan(Mathf.Tan(vertical * 0.5f * Mathf.Deg2Rad) * aspect);
            return Mathf.RoundToInt(radians * Mathf.Rad2Deg);
        }
    }
}
