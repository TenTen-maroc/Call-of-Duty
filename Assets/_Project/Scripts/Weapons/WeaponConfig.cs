#nullable enable
using UnityEngine;

namespace CoD.Weapons
{
    /// <summary>
    /// The canonical tuning asset. Gun feel gets tuned hundreds of times, and
    /// ScriptableObject values edited during Play Mode PERSIST when you stop —
    /// values on a scene MonoBehaviour are discarded. That difference alone is
    /// what turns tuning from a chore into a fast loop.
    ///
    /// Read-only at runtime. Nothing may write to a field here while playing:
    /// Domain Reload is disabled, so a runtime write would silently edit your
    /// authored balance data and persist between sessions.
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon_", menuName = "CoD/Weapon Config", order = 0)]
    public sealed class WeaponConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Save/registry key. Never renamed once shipped — saves reference content by this, not by asset name.")]
        public string stableId = "wpn_ar_standard";
        public string displayName = "Assault Rifle";
        public WeaponClass weaponClass = WeaponClass.AssaultRifle;

        [Tooltip("Ordered. Stacking IS the product: a railgun with Pierce and Chain is this list with two entries, not a new class. Purchases append to the RUNTIME copy, never here.")]
        public EffectModule[] effectModules = System.Array.Empty<EffectModule>();

        [Header("Damage")]
        [Tooltip("Against a 100 HP target. 25 = 4 shots to kill.")]
        [Range(1f, 100f)] public float bodyDamage = 25f;
        [Tooltip("1.5x for automatics, 2.0x for marksman rifles.")]
        [Range(1f, 3f)] public float headshotMultiplier = 1.5f;
        [Tooltip("Damage falloff start and end distance, in metres.")]
        public Vector2 falloffRange = new(25f, 60f);
        [Range(0.1f, 1f)] public float minDamageMultiplier = 0.6f;
        [Tooltip("How far the hitscan reaches at all.")]
        public float maxRange = 200f;

        [Header("Fire")]
        [Tooltip("Rounds per minute. 700 RPM = 0.086s between shots.")]
        [Range(30f, 1200f)] public float roundsPerMinute = 700f;
        public FireMode fireMode = FireMode.FullAuto;
        [Tooltip("Only used when fireMode is Burst.")]
        [Range(2, 5)] public int burstCount = 3;
        [Range(0.02f, 0.4f)] public float burstPause = 0.12f;
        [Min(1)] public int magazineSize = 30;
        [Min(0)] public int reserveAmmo = 180;
        [Min(1)] public int pelletsPerShot = 1; // shotgun: 12

        [Header("Handling (seconds)")]
        [Tooltip("Hip to fully aimed. AR 0.25, SMG 0.20, sniper 0.40.")]
        [Range(0.05f, 1f)] public float adsTime = 0.25f;
        [Tooltip("Delay after releasing sprint before firing is allowed. The most underrated number in the config.")]
        [Range(0f, 0.6f)] public float sprintToFireTime = 0.20f;
        [Range(0.3f, 6f)] public float reloadTime = 2.0f;
        [Tooltip("Reload from a fully empty magazine — includes the bolt/charge action.")]
        [Range(0.3f, 6f)] public float reloadEmptyTime = 2.6f;
        [Range(0.1f, 2f)] public float swapTime = 0.6f;
        [Tooltip("Fraction of the reload after which cancelling still keeps the ammo.")]
        [Range(0f, 1f)] public float reloadCommitPoint = 0.75f;
        [Tooltip("Delay after a click on an empty magazine before the gun will try again. Stops a held trigger machine-gunning the dry-fire sound.")]
        [Range(0.05f, 1f)] public float dryFireCooldown = 0.25f;

        [Header("Recoil (degrees)")]
        public float verticalKickFirstShot = 0.6f;
        public float verticalKickAtShotEight = 1.2f;
        public float horizontalKickMax = 0.35f;
        [Tooltip("Deterministic pattern seed. The same seed always produces the same climb, which is what makes recoil learnable.")]
        public int recoilSeed = 1337;
        [Range(0f, 0.5f)] public float recoveryDelay = 0.09f;
        [Range(0.05f, 1f)] public float recoveryDuration = 0.25f;
        [Tooltip("Never 1.0 — full recovery makes sustained fire free and the gun feels weightless.")]
        [Range(0.5f, 1f)] public float recoveryCompleteness = 0.85f;
        [Range(0f, 1f)] public float adsRecoilMultiplier = 0.6f;
        [Range(0f, 1f)] public float crouchRecoilMultiplier = 0.8f;

        [Header("Spread (degrees, hipfire only — ADS spread is always zero)")]
        public float baseSpread = 2.5f;
        public float spreadPerShot = 0.35f;
        public float maxSpread = 6f;
        [Tooltip("Degrees per second, after 0.1s of not firing.")]
        public float spreadDecayRate = 4f;
        public float movingMultiplier = 1.4f;
        public float crouchedMultiplier = 0.7f;
        public float airborneMultiplier = 2.0f;

        [Header("View")]
        [Tooltip("Multiplied against the base FOV while aiming.")]
        [Range(0.2f, 1f)] public float adsFovMultiplier = 0.75f;
        [Range(0.1f, 1f)] public float adsSensitivityMultiplier = 0.75f;
        public float fovKickOnFire = 1.5f;
        [Range(0.02f, 0.3f)] public float fovKickDuration = 0.06f;

        [Header("Feedback")]
        public GameObject? muzzleFlashPrefab;
        public GameObject? shellCasingPrefab;
        [Tooltip("Seconds a spent casing survives before returning to the pool.")]
        [Range(0.5f, 10f)] public float casingLifetime = 3f;
        [Tooltip("Casing ejection: sideways speed along the eject point's right, in m/s.")]
        [Range(0f, 8f)] public float casingEjectSpeed = 2.4f;
        [Tooltip("Casing ejection: upward pop, in m/s.")]
        [Range(0f, 5f)] public float casingEjectUpKick = 1.2f;
        [Tooltip("Random tumble on an ejected casing, in radians/second.")]
        [Range(0f, 60f)] public float casingSpinMax = 25f;
        public AudioClip? fireCloseLayer;   // mechanical crack
        public AudioClip? fireTailLayer;    // distance / reverb tail
        public AudioClip? dryFireClip;
        public AudioClip? reloadClip;
        [Tooltip("How long a pooled muzzle flash stays up. Was a literal in WeaponController while every other lifetime on that path already lived here.")]
        [Range(0.01f, 0.5f)] public float muzzleFlashLifetime = 0.08f;
        [Range(0f, 2f)] public float cameraShakeAmplitude = 0.6f;
        [Tooltip("A real point light for a couple of frames is what sells a muzzle flash.")]
        [Range(0f, 0.2f)] public float muzzleLightDuration = 0.03f;
        public float muzzleLightIntensity = 12f;

        // Derived — never stored, never duplicated in a MonoBehaviour.
        public float SecondsPerShot => 60f / Mathf.Max(1f, roundsPerMinute);
        public int ShotsToKill(float targetHealth = 100f) =>
            Mathf.CeilToInt(targetHealth / Mathf.Max(0.01f, bodyDamage));
        public float TimeToKill(float targetHealth = 100f) =>
            (ShotsToKill(targetHealth) - 1) * SecondsPerShot;

        /// <summary>Damage at a distance, after falloff. Used by the controller and by tests.</summary>
        public float DamageAtDistance(float distance)
        {
            float start = falloffRange.x;
            float end = Mathf.Max(start + 0.01f, falloffRange.y);
            float t = Mathf.Clamp01((distance - start) / (end - start));
            return bodyDamage * Mathf.Lerp(1f, minDamageMultiplier, t);
        }

#if UNITY_EDITOR
        // Surfaces the number that actually defines the game, right in the Inspector.
        private void OnValidate()
        {
            if (roundsPerMinute <= 0f) roundsPerMinute = 1f;
            if (reloadEmptyTime < reloadTime) reloadEmptyTime = reloadTime;
            if (falloffRange.y <= falloffRange.x) falloffRange.y = falloffRange.x + 1f;
            if (magazineSize < 1) magazineSize = 1;
            if (reserveAmmo < 0) reserveAmmo = 0;
            if (pelletsPerShot < 1) pelletsPerShot = 1;

            float ttk = TimeToKill() * 1000f;
            if (ttk > 0f && (ttk < 150f || ttk > 500f))
            {
                Debug.LogWarning(
                    $"[{name}] TTK is {ttk:F0} ms — outside the 200-400 ms arcade target. " +
                    "Intentional? TTK is the defining choice of the whole game; change it deliberately, not by accident.",
                    this);
            }
        }
#endif
    }

    public enum WeaponClass { AssaultRifle, SMG, Shotgun, Marksman, Sniper, Pistol, LMG }
    public enum FireMode { Single, Burst, FullAuto }
}
