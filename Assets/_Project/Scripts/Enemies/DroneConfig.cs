#nullable enable
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// One drone archetype, entirely as data: stats, movement, rewards, prefab,
    /// and the AttackModule it carries. Read-only at runtime.
    ///
    /// There is deliberately NO behaviour enum and no weakpoint multiplier here.
    /// Behaviour comes from the attack module plus preferredRange (a Rusher and a
    /// Shooter differ by DATA, not by class), and the headshot bonus has exactly
    /// one owner project-wide: WeaponConfig.headshotMultiplier. Drones still
    /// reward precision — their prefab carries a small `Core` collider with the
    /// same Weakpoint component the grey-box dummy uses.
    /// </summary>
    [CreateAssetMenu(fileName = "Drone_", menuName = "CoD/Drone Config", order = 10)]
    public sealed class DroneConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Save/registry key. Never renamed once shipped.")]
        public string stableId = "drone_rusher";
        public string displayName = "Rusher";

        [Header("Health")]
        [Tooltip("100 = four AR body shots, the same ~257 ms TTK the whole game is tuned around.")]
        [Range(1f, 5000f)] public float maxHealth = 100f;

        [Header("Movement (metres, metres/second)")]
        [Tooltip("Player walks 5.2 and sprints 8.0. Between the two means backpedalling loses and sprinting wins.")]
        public float moveSpeed = 6f;
        public float acceleration = 24f;
        [Tooltip("NavMeshAgent.angularSpeed, degrees/second.")]
        public float turnSpeed = 720f;
        [Tooltip("NavMeshAgent.baseOffset — how far the drone floats above the navmesh.")]
        public float hoverHeight = 0.9f;
        [Tooltip("0 = closes to contact. Above 0 = holds this distance and kites (the Shooter is this number, not a new class).")]
        public float preferredRange = 0f;
        public float stopDistance = 0.6f;
        [Tooltip("Seconds between destination updates. Repathing 40 agents every frame is the biggest CPU cost in a horde game; 0.15 is invisible to the player.")]
        [Range(0.05f, 1f)] public float repathInterval = 0.15f;

        [Header("Rewards")]
        public int scoreValue = 10;
        public int moneyReward = 12;

        [Header("Wiring")]
        [Tooltip("Pooled prefab. Registered in the ObjectPool's prewarm list in the same commit that created it.")]
        public GameObject? prefab;
        public AttackModule? attack;
        [Tooltip("Optional event-reaction tuning. Null preserves the pre-humanization behaviour.")]
        public EnemyReactionConfig? reactions;

        [Header("Telegraph")]
        [Tooltip("Core colour at rest. Per-archetype, because the drone tints its own core and would otherwise overwrite whatever material the prefab shipped with.")]
        public Color idleCoreColor = new(0.75f, 0.12f, 0.10f);
        [Tooltip("Core colour at the instant the attack lands. The bright end of the windup ramp.")]
        public Color telegraphCoreColor = new(1f, 0.95f, 0.75f);
        [Tooltip("Emission strength at rest, and at full telegraph. What makes a windup readable across the arena.")]
        [Range(0f, 4f)] public float idleEmission = 0.4f;
        [Range(0f, 12f)] public float telegraphEmission = 3.9f;

        [Header("Death")]
        [Tooltip("Pooled VFX spawned when the drone is SHOT DOWN. It carries its own AudioSource — the drone itself deactivates on despawn, so a clip played on the drone would be cut off mid-sound.")]
        public GameObject? deathVfx;
        [Range(0.1f, 4f)] public float deathVfxLifetime = 0.9f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (preferredRange > 0f && attack is ContactDetonate)
            {
                Debug.LogWarning(
                    $"[{name}] preferredRange is {preferredRange} but the attack is ContactDetonate — " +
                    "a drone that holds distance can never touch the player, so it will orbit forever.", this);
            }
            if (stopDistance < 0f) stopDistance = 0f;
            if (moveSpeed <= 0f) moveSpeed = 0.1f;
        }
#endif
    }
}
