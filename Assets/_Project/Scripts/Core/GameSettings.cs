#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// The player's live settings. A plain C# object, deliberately NOT a
    /// ScriptableObject.
    ///
    /// WHY: Domain Reload is off in this project, so a runtime write to a
    /// ScriptableObject survives into the next Play session and silently rewrites
    /// the shipped defaults. That has already cost this project time once, which
    /// is why WaveScaling and StatSheet exist. Settings are the same shape of
    /// problem — values that change while the game runs — so they get the same
    /// answer: read the config, never write it, and keep the mutable copy here.
    ///
    /// Clamping lives in this class rather than at the call sites so there is
    /// exactly one place a value can go out of range.
    /// </summary>
    public sealed class GameSettings
    {
        private readonly SettingsConfig _bounds;

        public float MouseSensitivity { get; private set; }
        public float FovVertical { get; private set; }
        public float MasterVolume { get; private set; }
        public bool InvertLook { get; private set; }

        public GameSettings(SettingsConfig bounds, float sensitivity, float fov, float volume, bool invert)
        {
            _bounds = bounds;
            MouseSensitivity = Mathf.Clamp(sensitivity, bounds.sensitivityMin, bounds.sensitivityMax);
            FovVertical = Mathf.Clamp(fov, bounds.fovMin, bounds.fovMax);
            MasterVolume = Mathf.Clamp(volume, bounds.volumeMin, bounds.volumeMax);
            InvertLook = invert;
        }

        public void SetMouseSensitivity(float value)
            => MouseSensitivity = Mathf.Clamp(value, _bounds.sensitivityMin, _bounds.sensitivityMax);

        public void SetFovVertical(float value)
            => FovVertical = Mathf.Clamp(value, _bounds.fovMin, _bounds.fovMax);

        public void SetMasterVolume(float value)
            => MasterVolume = Mathf.Clamp(value, _bounds.volumeMin, _bounds.volumeMax);

        public void SetInvertLook(bool value) => InvertLook = value;

        /// <summary>Nudge by one step. Direction is -1 or +1; anything else is ignored.</summary>
        public void StepMouseSensitivity(int direction)
            => SetMouseSensitivity(MouseSensitivity + _bounds.sensitivityStep * Mathf.Sign(direction));

        public void StepFovVertical(int direction)
            => SetFovVertical(FovVertical + _bounds.fovStep * Mathf.Sign(direction));

        public void StepMasterVolume(int direction)
            => SetMasterVolume(MasterVolume + _bounds.volumeStep * Mathf.Sign(direction));

        /// <summary>0..1 across the allowed range, for drawing a bar without the caller knowing the bounds.</summary>
        public float SensitivityFraction => Fraction(MouseSensitivity, _bounds.sensitivityMin, _bounds.sensitivityMax);
        public float FovFraction => Fraction(FovVertical, _bounds.fovMin, _bounds.fovMax);
        public float VolumeFraction => Fraction(MasterVolume, _bounds.volumeMin, _bounds.volumeMax);

        private static float Fraction(float value, float min, float max)
            => max - min <= Mathf.Epsilon ? 0f : Mathf.Clamp01((value - min) / (max - min));

        /// <summary>Copy the live values back onto the record that gets written to disk.</summary>
        public void WriteTo(SaveData save)
        {
            save.mouseSensitivity = MouseSensitivity;
            save.fovVertical = FovVertical;
            save.masterVolume = MasterVolume;
            save.invertLook = InvertLook;
            save.settingsInitialised = true;
        }
    }
}
