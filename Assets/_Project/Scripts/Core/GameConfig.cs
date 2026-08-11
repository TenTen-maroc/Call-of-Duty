#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// The global constants of the game. One asset, in Assets/_Project/Data/Game/.
    /// Nothing here may be duplicated as a literal in a script.
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "CoD/Game Config", order = 0)]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Player")]
        [Range(1f, 500f)] public float playerMaxHealth = 100f;

        [Header("Movement (metres, metres/second)")]
        public float walkSpeed = 5.2f;
        public float sprintSpeed = 8.0f;
        public float crouchSpeed = 2.6f;
        [Tooltip("Negative. Heavier than real gravity — arcade shooters fall fast.")]
        public float gravity = -20f;
        public float jumpHeight = 1.1f;
        public float standingHeight = 1.8f;
        public float crouchedHeight = 1.1f;
        [Tooltip("How quickly the capsule resizes when crouching, in seconds.")]
        [Range(0.02f, 0.5f)] public float crouchTransition = 0.12f;
        [Tooltip("Ground acceleration. Lower feels floatier.")]
        public float acceleration = 60f;
        public float airAcceleration = 12f;

        [Header("Look")]
        [Tooltip("Degrees per mouse-count. Tune with the in-game slider, not here.")]
        [Range(0.01f, 1f)] public float mouseSensitivity = 0.12f;
        [Range(60f, 89.9f)] public float pitchClamp = 89f;

        [Header("View")]
        [Tooltip("Unity's FOV field is VERTICAL. 62 vertical is roughly 95 horizontal at 16:9 — " +
                 "typing 95 here gives a ~120 horizontal fisheye, the classic first-FPS mistake.")]
        [Range(40f, 90f)] public float baseFovVertical = 62f;
        [Tooltip("Added to the base FOV while sprinting, eased in.")]
        public float sprintFovBonus = 8f;
        [Range(0.05f, 1f)] public float sprintFovEaseTime = 0.2f;
        [Tooltip("Camera dip on landing, in degrees, and how long it takes to recover.")]
        public float landingDipDegrees = 2f;
        [Range(0.02f, 0.5f)] public float landingDipTime = 0.12f;

        [Header("Sandbox")]
        [Range(0.05f, 1f)] public float slowMoTimeScale = 0.35f;

        /// <summary>Derived from jumpHeight and gravity — never stored twice.</summary>
        public float JumpVelocity => Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(gravity));

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (gravity > 0f) gravity = -Mathf.Abs(gravity);
            if (crouchedHeight >= standingHeight) crouchedHeight = standingHeight - 0.5f;
            if (baseFovVertical > 80f)
            {
                GameLog.Warn(
                    $"[{name}] baseFovVertical is {baseFovVertical}. Unity's field is VERTICAL — " +
                    "this is roughly a 120 degree horizontal fisheye. For a 95 horizontal feel, use ~62.",
                    this);
            }
        }
#endif
    }
}
