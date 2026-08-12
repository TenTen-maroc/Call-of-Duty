#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Every number the interaction system has. There are three, and all three
    /// are feel: how close counts as "at it", how squarely you must face it, and
    /// how fast a released hold drains.
    ///
    /// How LONG a hold takes is deliberately not here — it belongs to the thing
    /// being held, because planting a charge and reading a data pad are not the
    /// same commitment. An interactable reporting 0 is instant.
    /// </summary>
    [CreateAssetMenu(fileName = "Interaction_", menuName = "CoD/Interaction Config", order = 70)]
    public sealed class InteractionConfig : ScriptableObject
    {
        [Tooltip("Floor-plane metres. Roughly one long stride past arm's reach.")]
        [Range(0.5f, 6f)] public float range = 2.6f;

        [Tooltip("Dot product, not degrees. 0.35 is a wide cone — a prompt you have to aim at is a prompt players miss.")]
        [Range(-1f, 1f)] public float minFacing = 0.35f;

        [Tooltip("Releasing part-way drains the hold at this multiple of the fill rate. Above 1 punishes a slipped finger; below 1 forgives it.")]
        [Range(0.25f, 4f)] public float holdDecayRate = 2f;
    }
}
