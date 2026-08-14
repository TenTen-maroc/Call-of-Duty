#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// The short clock freeze that gives a kill weight.
    ///
    /// WHAT THIS IS FOR. Everything else about firing a gun in this project was
    /// already here — recoil patterns, spread, muzzle flash with a real world
    /// light, tracers, shell casings, camera shake, per-surface impacts. The one
    /// missing piece was the oldest trick in the genre: stop the world for a few
    /// dozen milliseconds when something dies. It is what makes a weapon feel
    /// like it has mass rather than like it is emitting numbers.
    ///
    /// A SCENE COMPONENT, NOT A STATIC. Domain Reload is disabled, so a static
    /// holding "am I currently frozen" would carry the previous Play session's
    /// state into this one and could leave the editor pinned at a timeScale of
    /// 0.02 with nothing left alive to restore it.
    ///
    /// THREE THINGS OWN Time.timeScale IN THIS GAME and they must not fight:
    /// the pause menu (0), the sandbox slow-mo cheat (0.35), and this. The rules
    /// that keep them apart are, in order of how much damage they prevent:
    ///
    /// 1. This scales RELATIVE to whatever it found. Slow-mo at 0.35 and a
    ///    hitstop factor of 0.05 compose to 0.0175, so a kill in slow-mo feels
    ///    like the same punch rather than like a speed-up.
    /// 2. It refuses to engage when the clock is already stopped. A kill landing
    ///    on the frame a pause menu opens must not resume the game.
    /// 3. Cancel only undoes ITS OWN write. If anything took the clock while
    ///    this was holding it, that thing is the owner now and restoring would
    ///    stomp it — the sandbox console toggling slow-mo mid-freeze is the real
    ///    case, and it is an 80 ms window nobody should have to think about.
    ///
    /// PausePanel still calls Cancel() before it captures, because rule 3 makes
    /// this yield gracefully but cannot make the pause menu capture the right
    /// number: it would otherwise record the frozen scale as "what to go back
    /// to" and the player would resume into permanent slow motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hitstop : MonoBehaviour
    {
        [Tooltip("Where the durations live. Without it this component does nothing at all.")]
        [SerializeField] private GameConfig? _config = null;

        /// <summary>Unscaled seconds left to hold. Never scaled — the whole point is that the scaled clock has stopped.</summary>
        private float _remaining;

        /// <summary>The clock to hand back. Captured on the punch that started the hold, never on a later one.</summary>
        private float _restoreTo = 1f;

        /// <summary>What this component actually wrote, so Cancel can tell "still mine" from "someone else's now".</summary>
        private float _appliedScale = 1f;

        /// <summary>Unscaled stamp of the last release, for the cooldown.</summary>
        private float _readyAt;

        private bool _active;

        public bool IsActive => _active;

        /// <summary>
        /// Below this the clock counts as already stopped and this refuses to
        /// touch it. Not Mathf.Epsilon: a pause menu writes exactly 0, but a
        /// slow-mo cheat tuned to something tiny should also be left alone.
        /// </summary>
        private const float STOPPED_CLOCK = 0.001f;

        /// <summary>
        /// Freeze, weighted by how big the thing that died was.
        /// </summary>
        /// <param name="weight01">0 for the smallest enemy in the game, 1 for the largest.</param>
        /// <param name="weakpoint">Whether the killing blow hit a weakpoint. A core kill thuds harder.</param>
        public void Punch(float weight01, bool weakpoint)
        {
            if (_config == null) return;

            float now = Time.unscaledTime;

            // THE HORDE PROBLEM, and why a cooldown is not timidity.
            //
            // Wave 8 sends twenty Rushers in ten seconds. Per-kill hitstop with
            // no floor between two kills turns the best moment in the game into
            // a strobe: the clock stutters continuously, aim feels rubbery, and
            // the effect that exists to make ONE kill feel heavy makes forty
            // kills feel broken. The cooldown is what keeps a freeze meaning
            // "that one landed" instead of meaning "you are still shooting".
            if (!_active && now < _readyAt) return;

            // Rule 2: never restart a stopped clock.
            if (Time.timeScale <= STOPPED_CLOCK) return;

            float seconds = Mathf.Lerp(
                _config.hitstopMinSeconds, _config.hitstopMaxSeconds, Mathf.Clamp01(weight01));
            if (weakpoint) seconds *= _config.hitstopWeakpointBonus;
            if (seconds <= 0f) return;

            if (!_active)
            {
                // Captured ONCE per hold. A second kill inside the freeze must
                // not record the frozen clock as the thing to go back to.
                _restoreTo = Time.timeScale;
                _active = true;
            }

            // Overlapping kills take the longer hold rather than adding up.
            // Adding would let a cluster of deaths compound into a hang.
            _remaining = Mathf.Max(_remaining, seconds);

            // Rule 1: relative, so slow-mo composes instead of being cancelled.
            _appliedScale = _restoreTo * _config.hitstopTimeScale;
            Time.timeScale = _appliedScale;
        }

        /// <summary>
        /// Hand the clock back now. Safe to call when nothing is held, and safe
        /// to call when something else has since taken over.
        /// </summary>
        public void Cancel()
        {
            if (!_active) return;
            _active = false;
            _remaining = 0f;
            _readyAt = Time.unscaledTime + (_config != null ? _config.hitstopCooldownSeconds : 0f);

            // Rule 3. Approximately rather than == because the value made the
            // round trip through a float field.
            if (Mathf.Approximately(Time.timeScale, _appliedScale)) Time.timeScale = _restoreTo;
        }

        private void Update()
        {
            if (!_active) return;

            // Unscaled, necessarily: the scaled clock is the thing being held,
            // so counting down in scaled time would stretch a 60 ms freeze into
            // most of a second and look like a hang.
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining <= 0f) Cancel();
        }

        /// <summary>
        /// Scene teardown, a mode change, a reload. Leaving a frozen clock
        /// behind is the one failure here that a player cannot recover from and
        /// that BuildSmokeTest specifically watches for.
        /// </summary>
        private void OnDisable() => Cancel();

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only injection, so the tests can drive this without a scene.
        /// Same pattern as HitZone.Configure and for the same reason: the field
        /// is serialized and private, which is correct for the game and useless
        /// for a test that wants to build one component and assert on it.
        /// </summary>
        public void Configure(GameConfig config) => _config = config;
#endif
    }
}
