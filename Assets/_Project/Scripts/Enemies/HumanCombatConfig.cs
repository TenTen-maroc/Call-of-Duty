#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>All rifleman movement, reaction, corpse, and cover timing.</summary>
    [CreateAssetMenu(fileName = "HumanCombat_", menuName = "CoD/Human Combat Config", order = 12)]
    public sealed class HumanCombatConfig : ScriptableObject
    {
        [Header("Animation")]
        [Min(0.1f)] public float speedAtFullBlend = 4.5f;
        [Range(0f, 0.5f)] public float speedDamping = 0.12f;

        [Header("Meridian variants")]
        public Color variantA = new(0.22f, 0.25f, 0.18f);
        public Color variantB = new(0.34f, 0.28f, 0.18f);

        [Header("Cover")]
        [Range(0.1f, 3f)] public float decisionInterval = 0.65f;
        [Range(1, 32)] public int coverChecksPerDecision = 8;
        [Range(0.1f, 2f)] public float coverArrivalDistance = 0.7f;
        [Range(2f, 40f)] public float coverSearchRadius = 20f;
        [Range(0f, 8f)] public float flankLaneBonus = 3f;

        [Header("Firing posture")]
        [Range(0f, 1f)] public float firingSpeedMultiplier = 0f;
        [Range(30f, 720f)] public float facingDegreesPerSecond = 300f;
        [Range(0.1f, 3f)] public float betweenBurstStrafeSeconds = 0.8f;
        [Range(0.5f, 5f)] public float strafeDistance = 2f;

        [Header("Damage reactions")]
        [Range(0.05f, 2f)] public float suppressionSeconds = 0.8f;
        [Range(0.05f, 2f)] public float aimDisruptionSeconds = 0.35f;
        [Range(0.05f, 2f)] public float legStumbleSeconds = 0.5f;
        [Range(0.1f, 1f)] public float legStumbleSpeedMultiplier = 0.45f;

        [Header("Death presentation")]
        [Range(0.5f, 30f)] public float corpseLifetime = 12f;
        [Range(0.1f, 10f)] public float ragdollLifetime = 6f;
        [Range(0f, 5f)] public float bloodPoolDelay = 1.2f;
    }
}
