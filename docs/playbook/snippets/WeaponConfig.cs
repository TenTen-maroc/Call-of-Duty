// The canonical shape for tuning data. Every number a human will tune lives in
// an asset like this — never as a literal in a script, never as a public field
// on a MonoBehaviour.
//
// Why this matters more than it looks: gun feel gets tuned hundreds of times.
// Values in a ScriptableObject can be edited *during Play Mode and they persist*
// when you stop. Values on a scene MonoBehaviour are discarded. That difference
// alone changes tuning from a chore into a fast feedback loop.
//
// Starting numbers below are the arcade-shooter defaults from
// references/gunfeel.md. Tune from them; do not treat them as correct.

#nullable enable
// ^ Required. Unity asmdefs have no nullable switch, so the directive lives
//   at the top of every first-party file. Without it, the `GameObject?` fields
//   below emit CS8632 warnings.

using UnityEngine;

namespace TenTen.Weapons
{
    [CreateAssetMenu(fileName = "Weapon_", menuName = "TenTen/Weapon Config", order = 0)]
    public sealed class WeaponConfig : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Assault Rifle";
        public WeaponClass weaponClass = WeaponClass.AssaultRifle;

        [Header("Damage")]
        [Tooltip("Against a 100 HP target. 25 = 4 shots to kill.")]
        [Range(1f, 100f)] public float bodyDamage = 25f;
        [Tooltip("1.5x for automatics, 2.0x for marksman rifles.")]
        [Range(1f, 3f)] public float headshotMultiplier = 1.5f;
        [Tooltip("Damage falloff start and end distance, in metres.")]
        public Vector2 falloffRange = new(25f, 60f);
        [Range(0.1f, 1f)] public float minDamageMultiplier = 0.6f;

        [Header("Fire")]
        [Tooltip("Rounds per minute. 700 RPM = 0.086s between shots.")]
        [Range(30f, 1200f)] public float roundsPerMinute = 700f;
        public FireMode fireMode = FireMode.FullAuto;
        [Tooltip("Only used when fireMode is Burst.")]
        [Range(2, 5)] public int burstCount = 3;
        public int magazineSize = 30;
        public int reserveAmmo = 180;
        public int pelletsPerShot = 1; // shotgun: 12

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

        [Header("Feedback")]
        public GameObject? muzzleFlashPrefab;
        public GameObject? impactDefaultPrefab;
        public GameObject? shellCasingPrefab;
        public AudioClip? fireCloseLayer;   // mechanical crack
        public AudioClip? fireTailLayer;    // distance / reverb tail
        public AudioClip? dryFireClip;
        public AudioClip? reloadClip;
        [Range(0f, 2f)] public float cameraShakeAmplitude = 0.6f;

        // Derived — never stored, never duplicated in a MonoBehaviour.
        public float SecondsPerShot => 60f / Mathf.Max(1f, roundsPerMinute);
        public int ShotsToKill(float targetHealth = 100f) =>
            Mathf.CeilToInt(targetHealth / Mathf.Max(0.01f, bodyDamage));
        public float TimeToKill(float targetHealth = 100f) =>
            (ShotsToKill(targetHealth) - 1) * SecondsPerShot;

#if UNITY_EDITOR
        // Surfaces the number that actually defines the game, right in the Inspector.
        private void OnValidate()
        {
            if (roundsPerMinute <= 0f) roundsPerMinute = 1f;
            if (reloadEmptyTime < reloadTime) reloadEmptyTime = reloadTime;

            float ttk = TimeToKill() * 1000f;
            if (ttk > 0f && (ttk < 150f || ttk > 500f))
            {
                Debug.LogWarning(
                    $"[{name}] TTK is {ttk:F0} ms — outside the 200–400 ms arcade target. " +
                    "Intentional? TTK is the defining choice of the whole game; change it deliberately, not by accident.",
                    this);
            }
        }
#endif
    }

    public enum WeaponClass { AssaultRifle, SMG, Shotgun, Marksman, Sniper, Pistol, LMG }
    public enum FireMode { Single, Burst, FullAuto }
}
