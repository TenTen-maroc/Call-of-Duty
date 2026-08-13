#nullable enable
using System;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>Value sent to subtitle UI. Visible=false clears the current line.</summary>
    public readonly struct RadioSubtitle
    {
        public readonly bool Visible;
        public readonly string Speaker;
        public readonly string Text;

        public RadioSubtitle(bool visible, string speaker, string text)
        {
            Visible = visible;
            Speaker = speaker;
            Text = text;
        }

        public static RadioSubtitle Hidden => new(false, string.Empty, string.Empty);
    }

    /// <summary>
    /// Pure scheduling state. It knows priority, interruption, cooldown and
    /// duplicate suppression, but no Time, AudioSource or scene.
    /// </summary>
    public sealed class RadioDialogueArbiter
    {
        private const int PENDING_CAPACITY = 8;
        private const int TRIGGER_COUNT = (int)RadioTrigger.MissionFailed + 1;

        private readonly RadioLine?[] _pending = new RadioLine?[PENDING_CAPACITY];
        private readonly int[] _triggerOccurrences = new int[TRIGGER_COUNT];
        private RadioLine[] _lines = Array.Empty<RadioLine>();
        private float[] _lastPlayedAt = Array.Empty<float>();
        private RadioLine? _current;

        public RadioLine? Current => _current;
        public int PendingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _pending.Length; i++) if (_pending[i] != null) count++;
                return count;
            }
        }

        public void Configure(RadioDialogueConfig? config)
        {
            _lines = config != null ? config.lines : Array.Empty<RadioLine>();
            _lastPlayedAt = new float[_lines.Length];
            for (int i = 0; i < _lastPlayedAt.Length; i++) _lastPlayedAt[i] = float.NegativeInfinity;
            ResetRuntime();
        }

        public void ResetRuntime()
        {
            _current = null;
            Array.Clear(_pending, 0, _pending.Length);
            Array.Clear(_triggerOccurrences, 0, _triggerOccurrences.Length);
        }

        public bool Request(RadioTrigger trigger, float now, out RadioLine? started, out bool interrupted)
        {
            started = null;
            interrupted = false;
            int triggerIndex = (int)trigger;
            if (triggerIndex < 0 || triggerIndex >= _triggerOccurrences.Length) return false;
            int occurrence = ++_triggerOccurrences[triggerIndex];

            int selectedIndex = -1;
            RadioLine? selected = null;
            for (int i = 0; i < _lines.Length; i++)
            {
                RadioLine? line = _lines[i];
                if (line == null || line.trigger != trigger) continue;
                if (line.occurrence > 0 && line.occurrence != occurrence) continue;
                if (now - _lastPlayedAt[i] < line.cooldownSeconds) continue;
                if (IsDuplicate(line.stableId)) continue;
                if (selected == null || line.priority > selected.priority)
                {
                    selected = line;
                    selectedIndex = i;
                }
            }

            if (selected == null) return false;

            if (_current == null)
            {
                Start(selected, selectedIndex, now);
                started = selected;
                return true;
            }

            if (selected.priority > _current.priority &&
                _current.interruptionPolicy == RadioInterruptionPolicy.AllowHigherPriority)
            {
                Start(selected, selectedIndex, now);
                started = selected;
                interrupted = true;
                return true;
            }

            for (int i = 0; i < _pending.Length; i++)
            {
                if (_pending[i] != null) continue;
                _pending[i] = selected;
                return true;
            }
            return false;
        }

        public RadioLine? CompleteCurrent(float now)
        {
            _current = null;
            int best = -1;
            for (int i = 0; i < _pending.Length; i++)
            {
                RadioLine? candidate = _pending[i];
                if (candidate == null) continue;
                if (best < 0 || candidate.priority > _pending[best]!.priority) best = i;
            }
            if (best < 0) return null;

            RadioLine next = _pending[best]!;
            _pending[best] = null;
            int lineIndex = IndexOf(next);
            Start(next, lineIndex, now);
            return next;
        }

        private bool IsDuplicate(string stableId)
        {
            if (_current != null && string.Equals(_current.stableId, stableId, StringComparison.Ordinal)) return true;
            for (int i = 0; i < _pending.Length; i++)
            {
                RadioLine? line = _pending[i];
                if (line != null && string.Equals(line.stableId, stableId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private int IndexOf(RadioLine line)
        {
            for (int i = 0; i < _lines.Length; i++) if (ReferenceEquals(_lines[i], line)) return i;
            return -1;
        }

        private void Start(RadioLine line, int lineIndex, float now)
        {
            _current = line;
            if (lineIndex >= 0 && lineIndex < _lastPlayedAt.Length) _lastPlayedAt[lineIndex] = now;
        }
    }

    /// <summary>Scene bridge for the pure arbiter, optional voice audio, and subtitle events.</summary>
    [DisallowMultipleComponent]
    public sealed class RadioDialogueScheduler : MonoBehaviour
    {
        [SerializeField] private AudioSource? _audio = null;

        private readonly RadioDialogueArbiter _arbiter = new();
        private float _endsAt;

        public event Action<RadioSubtitle>? SubtitleChanged;
        public RadioLine? Current => _arbiter.Current;

        public void Configure(RadioDialogueConfig? config)
        {
            _audio?.Stop();
            _arbiter.Configure(config);
            _endsAt = 0f;
            SubtitleChanged?.Invoke(RadioSubtitle.Hidden);
        }

        public bool Trigger(RadioTrigger trigger)
        {
            float now = Time.unscaledTime;
            bool accepted = _arbiter.Request(trigger, now, out RadioLine? started, out bool interrupted);
            if (started == null) return accepted;
            if (interrupted) _audio?.Stop();
            Present(started, now);
            return true;
        }

        private void Update()
        {
            if (_arbiter.Current == null || Time.unscaledTime < _endsAt) return;
            SubtitleChanged?.Invoke(RadioSubtitle.Hidden);
            RadioLine? next = _arbiter.CompleteCurrent(Time.unscaledTime);
            if (next != null) Present(next, Time.unscaledTime);
        }

        private void Present(RadioLine line, float now)
        {
            float duration = line.subtitleSeconds;
            if (line.audioClip != null)
            {
                if (_audio != null) _audio.PlayOneShot(line.audioClip);
                duration = Mathf.Max(duration, line.audioClip.length);
            }
            _endsAt = now + Mathf.Max(0.5f, duration);
            SubtitleChanged?.Invoke(new RadioSubtitle(true, line.speakerName, line.subtitle));
        }

        private void OnDisable()
        {
            _audio?.Stop();
            _arbiter.ResetRuntime();
            SubtitleChanged?.Invoke(RadioSubtitle.Hidden);
        }
    }
}
