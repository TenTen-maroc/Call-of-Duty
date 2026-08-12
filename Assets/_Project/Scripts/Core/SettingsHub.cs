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

            if (!save.graphicsInitialised)
            {
                // First launch, or a save written before schema 3 existed. Same
                // rule as the block above: the only place a default may live is a
                // config asset, so the migration writes nothing and this seeds it.
                save.postProcessing = _bounds.postProcessingDefault;
                save.antiAliasing = _bounds.antiAliasingDefault;
                save.graphicsInitialised = true;
            }

            return new GameSettings(_bounds, save.mouseSensitivity, save.fovVertical,
                save.masterVolume, save.invertLook, save.postProcessing, save.antiAliasing);
        }

        /// <summary>
        /// Push the current values everywhere and apply the global ones. Call after
        /// any Set/Step; the UI does exactly that.
        /// </summary>
        public void Apply()
        {
            GameSettings settings = Current;

            // STILL AudioListener.volume, and now that is a decision rather than
            // a limitation — the mixer this comment used to say could not exist
            // now does (Assets/_Project/Audio/Master.mixer, with MasterVolume,
            // SfxVolume, MusicVolume and AmbienceVolume already exposed to script).
            //
            // Moving the master slider onto the mixer TODAY would be a
            // regression, not progress. Only footsteps and ambience are routed
            // through a bus; the weapon layers, the impacts, the hitmarker and
            // every UI cue still play straight to the listener, because nothing
            // has given them an output group. AudioListener.volume is applied to
            // the final mix and therefore covers all of them AND the mixer's
            // output; an exposed MasterVolume parameter would cover only the two
            // that happen to be routed, and the slider would silently stop
            // working for most of the game.
            //
            // The switch is worth making at exactly one moment: when there is a
            // SECOND bus a player needs to balance — music against SFX — which
            // is also the moment every source gets an output group. Until then
            // this one line is complete and the exposed parameters are a seam
            // waiting, not a seam ignored.
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

        /// <summary>
        /// Records WHICH CONTENT the menu is launching: a campaign mission, or
        /// the endless loop.
        ///
        /// The save file is the only sanctioned channel between a menu and the
        /// scene it loads. A static carrier is banned outright — Domain Reload is
        /// off, so it would survive into the next Play session and start a
        /// campaign mission in what the player asked to be an endless run.
        ///
        /// This is a SECOND AXIS, never a third GameMode value: GameMode is
        /// serialised as a raw int and C# enums are not range-checked, so a
        /// shipped build reading an unknown value would treat it as a Run and
        /// write a mission's wave number into bestRound, permanently. Mode means
        /// rules; this means content.
        ///
        /// Written through the shared SaveData, like everything else here. Two
        /// independently loaded copies each rewrite the whole file, so the last
        /// writer silently reverts the other half — the bug that used to zero the
        /// settings block every time the player died.
        /// </summary>
        public void SetCampaign(bool selected, string missionId)
        {
            SaveData save = Save;
            string id = missionId ?? string.Empty;
            if (save.campaignSelected == selected && save.selectedMissionId == id) return;

            save.campaignSelected = selected;
            save.selectedMissionId = id;
            SaveSystem.Save(save);
        }

        /// <summary>
        /// The stored result for one mission, or null if it has never been
        /// finished. A pure lookup: the mission-select screen asks about every
        /// mission in the catalog every time it redraws, and a query that
        /// created a row would rewrite the save file just for being looked at.
        /// </summary>
        public MissionRecord? FindRecord(string missionId)
        {
            SaveData save = Save;
            for (int i = 0; i < save.missionRecords.Length; i++)
            {
                if (save.missionRecords[i].missionId == missionId) return save.missionRecords[i];
            }
            return null;
        }

        /// <summary>
        /// Writes the result of a finished mission, keeping the BEST of each
        /// value rather than the latest.
        ///
        /// Best-not-latest is the whole point of a record: replaying a mission
        /// you already three-starred, and dying twice doing it, must not delete
        /// the three stars. Deaths accumulate for the same reason from the other
        /// direction — they are a count of what the mission has cost you, not of
        /// the last attempt.
        /// </summary>
        public void RecordMissionResult(string missionId, bool completed, int rating, float timeSeconds, int deaths)
        {
            SaveData save = Save;
            MissionRecord? existing = FindRecord(missionId);

            if (existing == null)
            {
                existing = new MissionRecord { missionId = missionId };
                var grown = new MissionRecord[save.missionRecords.Length + 1];
                System.Array.Copy(save.missionRecords, grown, save.missionRecords.Length);
                grown[^1] = existing;
                save.missionRecords = grown;
            }

            existing.completed |= completed;
            existing.deaths += deaths;
            if (rating > existing.bestRating) existing.bestRating = rating;
            // A zero time means "not timed", so it must never win the comparison.
            if (completed && timeSeconds > 0f &&
                (existing.bestTimeSeconds <= 0f || timeSeconds < existing.bestTimeSeconds))
            {
                existing.bestTimeSeconds = timeSeconds;
            }

            SaveSystem.Save(save);
        }
    }
}
