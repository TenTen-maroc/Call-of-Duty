#nullable enable
using System;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Owns the player's settings for the lifetime of a scene: loads them from
    /// disk, applies the ones nobody else can (audio), publishes the rest, and
    /// writes them back.
    ///
    /// A scene component in EVERY scene rather than a DontDestroyOnLoad
    /// singleton. A singleton would be a mutable static, which this project bans
    /// outright — Domain Reload is off, so it would survive into the next Play
    /// session pointing at a destroyed object. Re-reading a two-kilobyte JSON
    /// file on each scene load is the cheaper mistake by a wide margin, and it
    /// means a scene opened directly in the editor is fully configured too.
    ///
    /// Nothing here writes to a ScriptableObject. The configs supply the DEFAULTS
    /// and the BOUNDS; the live values live in GameSettings and on disk.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsHub : MonoBehaviour
    {
        [Tooltip("Bounds and step sizes. Never written to.")]
        [SerializeField] private SettingsConfig? _bounds = null;
        [Tooltip("Where the shipped defaults come from the first time the game runs.")]
        [SerializeField] private GameConfig? _defaults = null;

        private GameSettings? _settings;
        private SaveData? _save;

        /// <summary>Raised whenever any value changes, and once on first resolve.</summary>
        public event Action<GameSettings>? Changed;

        /// <summary>
        /// The live settings. Resolved lazily on first access — `??=` assigns only
        /// if the field is still null — so it does not matter whether this
        /// component's Awake ran before or after the components that read it.
        /// Script execution order is exactly the kind of implicit dependency that
        /// breaks silently when a scene is rebuilt.
        /// </summary>
        public GameSettings Current => _settings ??= Resolve();

        /// <summary>The record these settings were read from. Shared with whoever else loaded it.</summary>
        public SaveData Save => _save ??= SaveSystem.Load();

        private void Awake()
        {
            // Touching Current here applies audio immediately, so the first frame
            // is already at the player's chosen volume rather than full blast.
            Apply();
        }

        private GameSettings Resolve()
        {
            SaveData save = Save;

            if (_bounds == null)
            {
                GameLog.Error("SettingsHub has no SettingsConfig — settings cannot be bounded.", this);
                _bounds = ScriptableObject.CreateInstance<SettingsConfig>();
            }

            if (!save.settingsInitialised)
            {
                // First launch, or a v1 save whose settings block was never real.
                // Seed from the configs: they are the only place a default number
                // is allowed to live.
                save.mouseSensitivity = _defaults != null ? _defaults.mouseSensitivity : _bounds.sensitivityMin;
                save.fovVertical = _defaults != null ? _defaults.baseFovVertical : _bounds.fovMin;
                save.masterVolume = _bounds.volumeMax;
                save.invertLook = false;
                save.settingsInitialised = true;
            }

            return new GameSettings(_bounds, save.mouseSensitivity, save.fovVertical,
                save.masterVolume, save.invertLook);
        }

        /// <summary>
        /// Push the current values everywhere and apply the global ones. Call after
        /// any Set/Step; the UI does exactly that.
        /// </summary>
        public void Apply()
        {
            GameSettings settings = Current;

            // The one setting nothing else can honour. AudioListener.volume is a
            // single global multiplier over every AudioSource in the scene, which
            // is the whole requirement while this game has one sound category.
            //
            // Deliberately NOT an AudioMixer: a .mixer is opaque binary-ish YAML
            // that the scene builder cannot generate, and this project's rule is
            // that nothing in a scene is hand-authored. Revisit the moment there
            // is a second bus to balance — music against SFX — because that is
            // the first thing one line of global volume genuinely cannot do.
            AudioListener.volume = settings.MasterVolume;

            Changed?.Invoke(settings);
        }

        /// <summary>Write the settings block back to disk. The run record is untouched.</summary>
        public void Persist()
        {
            SaveData save = Save;
            Current.WriteTo(save);
            SaveSystem.Save(save);
        }

        /// <summary>Apply and persist in one call — what a menu does when it closes.</summary>
        public void ApplyAndPersist()
        {
            Apply();
            Persist();
        }

        /// <summary>Records which mode the player last chose, so the menu can come back to it.</summary>
        public void SetLastMode(GameMode mode)
        {
            SaveData save = Save;
            if (save.lastMode == mode) return;
            save.lastMode = mode;
            SaveSystem.Save(save);
        }
    }
}
