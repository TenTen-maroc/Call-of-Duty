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

        [Header("Viewmodel")]
        [Tooltip("The gun's OWN vertical FOV, rendered by the overlay camera. Deliberately independent " +
                 "of baseFovVertical: the world camera lerps its FOV for the sprint bonus and the ADS/kick " +
                 "offset, and while the gun was a child of that camera it stretched on every sprint and " +
                 "every aim. Shipped equal to the default baseFovVertical so the framing the viewmodel " +
                 "was posed against does not move — but the player's FOV slider no longer warps it.")]
        [Range(30f, 90f)] public float viewmodelFovVertical = 62f;
        [Tooltip("Added to viewmodelFovVertical at full ADS, and nothing else — no sprint bonus, no fire " +
                 "kick. Negative pulls the gun in slightly as the sights come up. Keep it SMALL: the " +
                 "whole point of the separate camera is that the gun stops changing shape.")]
        [Range(-15f, 5f)] public float viewmodelAdsFovDelta = -4f;

        [Header("Damage feedback")]
        [Tooltip("How long the red hit flash stays up, in seconds.")]
        [Range(0.05f, 1f)] public float damageFlashDuration = 0.18f;
        [Range(0f, 1f)] public float damageFlashAlpha = 0.32f;
        [Tooltip("Seconds the directional indicator points at whatever hit you. This is what turns 'died from nowhere' into 'got caught out'.")]
        [Range(0.2f, 3f)] public float damageDirectionDuration = 1.1f;
        [Tooltip("Health fraction below which the screen edges tint and pulse.")]
        [Range(0f, 1f)] public float lowHealthThreshold = 0.35f;
        [Range(0.5f, 6f)] public float lowHealthPulseSpeed = 2.2f;
        [Range(0f, 1f)] public float lowHealthMaxAlpha = 0.4f;

        [Header("Sandbox")]
        [Range(0.05f, 1f)] public float slowMoTimeScale = 0.35f;

        [Tooltip("Extra effect-module resolution depth in SANDBOX only. Run mode always gets 0. " +
                 "This is where the 'without limits' promise is allowed to be felt, because a frame " +
                 "spike in a sandbox costs nothing and a frame spike in a run costs the run.")]
        [Range(0, 3)] public int sandboxExtraEffectDepth = 1;

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
