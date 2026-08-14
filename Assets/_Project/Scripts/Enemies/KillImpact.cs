#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// Turns a drone death into a hitstop punch.
    ///
    /// WHY THIS IS A SEPARATE COMPONENT FROM Hitstop. Hitstop lives in CoD.Core
    /// and knows nothing about drones — it is a clock service, and Core is the
    /// assembly with no references at all, so it CANNOT know about them. The
    /// enemy-shaped half of the question ("how big was that thing, and did the
    /// player earn it") belongs here in CoD.Enemies, which already references
    /// Core. That split is not tidiness: it is what lets an explosion, a player
    /// death or a boss stagger punch the same clock later without any of them
    /// having to know a drone exists.
    ///
    /// It subscribes ONCE to the registry rather than per spawn, the same
    /// pattern and for the same reason as the wave runner: forty drones a wave
    /// is forty subscribe/unsubscribe pairs, each of which is a leak the day
    /// somebody forgets one.
    ///
    /// Inert without wiring. A null registry or a null hitstop makes this
    /// component do nothing, which is what keeps a scene built before this
    /// existed from throwing on load.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KillImpact : MonoBehaviour
    {
        [Tooltip("Who died. Subscribed once, never per spawn.")]
        [SerializeField] private DroneRegistry? _registry = null;

        [Tooltip("The clock. Without it this component is inert.")]
        [SerializeField] private Hitstop? _hitstop = null;

        [Tooltip("Where hitstopHealthForMax lives. Without it every kill weighs the same.")]
        [SerializeField] private GameConfig? _config = null;

        private void OnEnable()
        {
            if (_registry != null) _registry.Killed += OnKilled;
        }

        private void OnDisable()
        {
            if (_registry != null) _registry.Killed -= OnKilled;
        }

        /// <summary>
        /// Weight comes from the dead enemy's authored maxHealth rather than
        /// from a hitstop field on DroneConfig.
        ///
        /// The health IS the size, in every sense the player can feel: it is
        /// what makes the Tank take twenty-four rounds and the Rusher four. A
        /// separate per-enemy freeze value would be a second number saying the
        /// same thing, free to drift away from the first, and it would have to
        /// be authored again for every enemy added from here on. Deriving it
        /// means a new enemy gets a correctly-weighted kill for free.
        /// </summary>
        private void OnKilled(DroneController drone, DamageInfo info)
        {
            if (_hitstop == null) return;

            float weight = 0f;
            if (_config != null && drone.Config != null && _config.hitstopHealthForMax > 0f)
            {
                weight = Mathf.Clamp01(drone.Config.maxHealth / _config.hitstopHealthForMax);
            }

            _hitstop.Punch(weight, info.IsWeakpoint);
        }
    }
}
