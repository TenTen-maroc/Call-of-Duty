#nullable enable
using System;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>Append only: these values are serialized into mission dialogue assets.</summary>
    public enum RadioTrigger
    {
        MissionEntry,
        FirstObjective,
        FirstContact,
        PlayerBadlyHurt,
        WaveClear,
        ObjectiveComplete,
        MissionComplete,
        MissionFailed,
    }

    /// <summary>Whether a line already on air may yield to a more important one.</summary>
    public enum RadioInterruptionPolicy
    {
        Finish,
        AllowHigherPriority,
    }

    [Flags]
    public enum RadioValidationIssue
    {
        None = 0,
        MissingStableId = 1 << 0,
        DuplicateStableId = 1 << 1,
        MissingSpeaker = 1 << 2,
        MissingSubtitle = 1 << 3,
        InvalidTiming = 1 << 4,
        InvalidPriority = 1 << 5,
    }

    /// <summary>
    /// One authored radio beat. Audio is optional by contract: subtitles are the
    /// guaranteed delivery channel, not an error fallback.
    /// </summary>
    [Serializable]
    public sealed class RadioLine
    {
        [Tooltip("Stable content key. Never rename once shipped.")]
        public string stableId = "radio_";
        [Tooltip("Stable character key, separate from the displayed callsign.")]
        public string speakerId = "operator";
        public string speakerName = "OPERATOR";
        [TextArea(2, 4)] public string subtitle = "";
        public RadioTrigger trigger;
        [Tooltip("0 = every occurrence; otherwise only this 1-based occurrence of the trigger.")]
        [Min(0)] public int occurrence;
        [Range(0, 100)] public int priority = 50;
        [Min(0f)] public float cooldownSeconds = 5f;
        [Tooltip("Minimum subtitle time. Audio, when present, may hold the line longer.")]
        [Min(0.5f)] public float subtitleSeconds = 2.5f;
        public RadioInterruptionPolicy interruptionPolicy = RadioInterruptionPolicy.AllowHigherPriority;
        [Tooltip("Optional. Null is valid and plays the timed subtitle in silence.")]
        public AudioClip? audioClip;
    }

    /// <summary>Mission-owned narrative data. No scene branch contains dialogue copy.</summary>
    [CreateAssetMenu(fileName = "Radio_", menuName = "CoD/Radio Dialogue Config", order = 72)]
    public sealed class RadioDialogueConfig : ScriptableObject
    {
        public RadioLine[] lines = Array.Empty<RadioLine>();

        public RadioValidationIssue Validate(out int invalidIndex)
        {
            RadioValidationIssue issues = RadioValidationIssue.None;
            invalidIndex = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                RadioLine? line = lines[i];
                if (line == null)
                {
                    issues |= RadioValidationIssue.MissingStableId |
                              RadioValidationIssue.MissingSpeaker |
                              RadioValidationIssue.MissingSubtitle |
                              RadioValidationIssue.InvalidTiming;
                    if (invalidIndex < 0) invalidIndex = i;
                    continue;
                }

                RadioValidationIssue lineIssues = RadioValidationIssue.None;
                if (string.IsNullOrWhiteSpace(line.stableId)) lineIssues |= RadioValidationIssue.MissingStableId;
                if (string.IsNullOrWhiteSpace(line.speakerId) || string.IsNullOrWhiteSpace(line.speakerName))
                    lineIssues |= RadioValidationIssue.MissingSpeaker;
                if (string.IsNullOrWhiteSpace(line.subtitle)) lineIssues |= RadioValidationIssue.MissingSubtitle;
                if (line.subtitleSeconds < 0.5f || line.cooldownSeconds < 0f || line.occurrence < 0)
                    lineIssues |= RadioValidationIssue.InvalidTiming;
                if (line.priority < 0 || line.priority > 100) lineIssues |= RadioValidationIssue.InvalidPriority;

                if (!string.IsNullOrWhiteSpace(line.stableId))
                {
                    for (int other = 0; other < i; other++)
                    {
                        RadioLine? earlier = lines[other];
                        if (earlier != null && string.Equals(earlier.stableId, line.stableId, StringComparison.Ordinal))
                        {
                            lineIssues |= RadioValidationIssue.DuplicateStableId;
                            break;
                        }
                    }
                }

                if (lineIssues != RadioValidationIssue.None && invalidIndex < 0) invalidIndex = i;
                issues |= lineIssues;
            }

            return issues;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RadioValidationIssue issues = Validate(out int index);
            if (issues == RadioValidationIssue.None) return;
            Debug.LogError($"[{name}] radio dialogue is invalid at line {index}: {issues}.", this);
        }
#endif
    }
}
