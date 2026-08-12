#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// Every mission in the game, in order.
    ///
    /// The menu writes a mission's stableId into the save and the arena reads it
    /// back — the save file being the only sanctioned channel between a menu and
    /// the scene it launches, because Domain Reload is off and a static carrier
    /// would survive into the next Play session. Something has to turn that
    /// string back into an asset, and a serialized list is that something.
    ///
    /// Ordered, because the campaign is ordered: index is mission number, and
    /// "the next one" is the one after the highest completed.
    /// </summary>
    [CreateAssetMenu(fileName = "Missions", menuName = "CoD/Mission Catalog", order = 41)]
    public sealed class MissionCatalog : ScriptableObject
    {
        [Tooltip("In campaign order. Mission 1 first.")]
        public MissionConfig[] missions = System.Array.Empty<MissionConfig>();

        public int Count => missions.Length;

        /// <summary>Null for an unknown id, which is what an old save pointing at a deleted mission looks like.</summary>
        public MissionConfig? Find(string stableId)
        {
            if (string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < missions.Length; i++)
            {
                MissionConfig? mission = missions[i];
                if (mission != null && mission.stableId == stableId) return mission;
            }
            return null;
        }

        public MissionConfig? At(int index)
            => index >= 0 && index < missions.Length ? missions[index] : null;

        public int IndexOf(string stableId)
        {
            for (int i = 0; i < missions.Length; i++)
            {
                MissionConfig? mission = missions[i];
                if (mission != null && mission.stableId == stableId) return i;
            }
            return -1;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // A duplicate id makes Find return whichever came first, and the
            // OTHER mission becomes unreachable from the menu with nothing
            // anywhere reporting it. A null hole does the same to every mission
            // after it, because index IS mission number.
            for (int i = 0; i < missions.Length; i++)
            {
                if (missions[i] == null)
                {
                    Debug.LogError($"[{name}] mission slot {i} is empty — index is mission number, so every mission after it is misnumbered.", this);
                    continue;
                }
                for (int j = i + 1; j < missions.Length; j++)
                {
                    if (missions[j] != null && missions[j].stableId == missions[i].stableId)
                    {
                        Debug.LogError($"[{name}] missions {i} and {j} share stableId '{missions[i].stableId}' — one of them can never be selected.", this);
                    }
                }
            }
        }
#endif
    }
}
