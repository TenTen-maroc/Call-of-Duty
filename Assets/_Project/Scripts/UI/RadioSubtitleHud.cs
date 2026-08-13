#nullable enable
using CoD.Core;
using CoD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace CoD.UI
{
    /// <summary>Accessible, high-contrast subtitle presentation for mission radio.</summary>
    [DisallowMultipleComponent]
    public sealed class RadioSubtitleHud : MonoBehaviour
    {
        [SerializeField] private RadioDialogueScheduler? _radio = null;
        [SerializeField] private SettingsHub? _settings = null;
        [SerializeField] private Text? _label = null;
        [SerializeField] private Image? _background = null;

        private RadioSubtitle _current = RadioSubtitle.Hidden;

        private void OnEnable()
        {
            if (_radio != null) _radio.SubtitleChanged += OnSubtitleChanged;
            if (_settings != null) _settings.Changed += OnSettingsChanged;
            Apply();
        }

        private void OnDisable()
        {
            if (_radio != null) _radio.SubtitleChanged -= OnSubtitleChanged;
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
        }

        private void OnSubtitleChanged(RadioSubtitle subtitle)
        {
            _current = subtitle;
            if (_label != null)
            {
                _label.text = subtitle.Visible ? subtitle.Speaker + ": " + subtitle.Text : string.Empty;
            }
            Apply();
        }

        private void OnSettingsChanged(GameSettings settings) => Apply(settings);

        private void Apply()
        {
            if (_settings == null)
            {
                SetVisible(false);
                return;
            }
            Apply(_settings.Current);
        }

        private void Apply(GameSettings settings)
        {
            if (_label != null) _label.fontSize = settings.SubtitleFontSize;
            SetVisible(_current.Visible && settings.SubtitlesEnabled);
        }

        private void SetVisible(bool visible)
        {
            if (_label != null) _label.enabled = visible;
            if (_background != null) _background.enabled = visible;
        }
    }
}
