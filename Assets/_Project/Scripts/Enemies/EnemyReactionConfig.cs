#nullable enable
using System;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>Append only: response arrays are indexed by these serialized values.</summary>
    public enum EnemyReactionKind
    {
        DetectPlayer,
        NearbyAllyDeath,
        HeavyDamage,
        LostSight,
        AttackCommit,
        LowHealth,
    }

    [Serializable]
    public sealed class EnemyReactionResponse
    {
        public EnemyReactionKind kind;
        [Range(0f, 1f)] public float probability = 0.5f;
        [Min(0f)] public float cooldownSeconds = 3f;
        [Range(0f, 1f)] public float corePulse = 0.55f;
        [Min(0.05f)] public float pulseSeconds = 0.2f;
        [Tooltip("Optional non-verbal cue. Null is silence, not a wiring warning.")]
        public AudioClip? cue;
    }

    /// <summary>All event-reaction tuning for an enemy archetype.</summary>
    [CreateAssetMenu(fileName = "Reactions_", menuName = "CoD/Enemy Reaction Config", order = 13)]
    public sealed class EnemyReactionConfig : ScriptableObject
    {
        private const int KIND_COUNT = (int)EnemyReactionKind.LowHealth + 1;

        [Header("Sensing")]
        [Min(0.05f)] public float sightSampleInterval = 0.25f;
        [Min(1f)] public float detectionRange = 24f;
        [Min(0.1f)] public float lostSightSeconds = 1.2f;
        public LayerMask sightMask = Physics.DefaultRaycastLayers;

        [Header("Damage")]
        [Range(0.05f, 1f)] public float heavyDamageFraction = 0.28f;
        [Range(0.05f, 0.9f)] public float lowHealthFraction = 0.25f;
        [Min(0.5f)] public float allyDeathRadius = 9f;

        [Header("Responses")]
        public EnemyReactionResponse[] responses = Array.Empty<EnemyReactionResponse>();

        public EnemyReactionResponse? ResponseFor(EnemyReactionKind kind)
        {
            for (int i = 0; i < responses.Length; i++)
            {
                EnemyReactionResponse? response = responses[i];
                if (response != null && response.kind == kind) return response;
            }
            return null;
        }

        public bool IsComplete
        {
            get
            {
                if (responses.Length != KIND_COUNT) return false;
                int seen = 0;
                for (int i = 0; i < responses.Length; i++)
                {
                    EnemyReactionResponse? response = responses[i];
                    if (response == null) return false;
                    int bit = 1 << (int)response.kind;
                    if ((seen & bit) != 0) return false;
                    if (response.probability < 0f || response.probability > 1f ||
                        response.cooldownSeconds < 0f || response.pulseSeconds <= 0f) return false;
                    seen |= bit;
                }
                return seen == (1 << KIND_COUNT) - 1;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!IsComplete)
            {
                Debug.LogError(
                    $"[{name}] must contain exactly one valid response for every EnemyReactionKind.", this);
            }
        }
#endif
    }
}
