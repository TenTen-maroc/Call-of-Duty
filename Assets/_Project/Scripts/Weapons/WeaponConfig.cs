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

        [Tooltip("What this weapon SHIPS with bolted on. A different thing from effectModules: a module is a behaviour hook, an attachment is a stat delta — see AttachmentConfig for why they are not the same pattern. Fitted through the same TryFit a shop would use, so a config authored with two optics behaves like a player trying to fit two.")]
        public AttachmentConfig[] attachments = System.Array.Empty<AttachmentConfig>();

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

        [Tooltip("Shotgun PATTERN, in degrees — the fixed spread of the shell itself, which is not bloom. Bloom (baseSpread and friends) grows as you fire and collapses to ZERO while aiming; a pattern never does, because a shell does not tighten because you aimed. 0 for anything with one pellet.")]
        [Range(0f, 15f)] public float pelletSpreadDegrees;

        [Header("Delivery")]
        [Tooltip("Hitscan resolves on the frame the trigger is pulled. Projectile puts a real object in the air, which is the only honest way to build a launcher — a rocket the player cannot see coming is a rocket that reads as a random explosion.")]
        public DeliveryMode delivery = DeliveryMode.Hitscan;

        [Tooltip("Pooled prefab carrying CoD.Core.Projectile. REQUIRED when delivery is Projectile — without it the gun consumes ammo, kicks, flashes and fires nothing at all.")]
        public GameObject? projectilePrefab;

        [Tooltip("Metres per second. Slow enough to be seen leaving the tube and fast enough to lead a rusher: 34 crosses the arena's longest lane in about a second.")]
        [Range(5f, 200f)] public float projectileSpeed = 34f;

        [Tooltip("Seconds before an unspent round retires. Only reached by a shot fired at open sky — anything aimed into the arena meets a wall first.")]
        [Range(0.5f, 30f)] public float projectileLifetime = 6f;

        [Tooltip("How far down the AIM RAY the round is born, in metres. Far enough clear of the player's own capsule to be obvious, close enough that a muzzle pressed against a wall still detonates on that wall rather than behind it.")]
        [Range(0f, 3f)] public float projectileSpawnOffset = 0.9f;

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

        [Tooltip("The gun's own flash light, which sits centimetres from the barrel instead of metres from a wall. Same duration, far lower intensity — the room number would blow the viewmodel out completely.")]
        [Range(0f, 20f)] public float viewmodelMuzzleLightIntensity = 2.2f;

        [Header("Muzzle flash — three parts, because one quad is not a flash")]
        [Tooltip("The second, STRETCHED quad. Spawned on the same shot and the same lifetime as the flash, with its own random roll: two overlapping shapes at different aspect ratios is what stops a repeating sprite reading as a repeating sprite, and it costs no new texture.")]
        public GameObject? muzzleFlashWidePrefab;
        [Tooltip("Random scale spread applied to BOTH flash quads, every spawn. Written every time rather than only when it changes — a pooled instance keeps the scale its last use left it at.")]
        [Range(0f, 0.6f)] public float muzzleFlashScaleJitter = 0.25f;
        [Tooltip("The puff that says the barrel has been working. Deliberately not every shot: on the last round of a burst, and every muzzleSmokeEveryNRounds under sustained fire.")]
        public GameObject? muzzleSmokePrefab;
        [Tooltip("0 = only at the end of a burst. Smoke on every round is fog in front of the sight, which costs the player the thing they are aiming at.")]
        [Range(0, 30)] public int muzzleSmokeEveryNRounds = 6;
        [Tooltip("A puff lingers far longer than a flash — it is the one part of the muzzle that is allowed to be seen.")]
        [Range(0.05f, 3f)] public float muzzleSmokeLifetime = 0.9f;

        [Header("Tracers")]
        [Tooltip("The pooled Fx_Tracer prefab. Null = this weapon fires no tracers at all, which is a legitimate choice for a suppressed weapon.")]
        public GameObject? tracerPrefab;
        [Tooltip("ONE IN N ROUNDS, never every round. A tracer on every round is a continuous ribbon of light out of the barrel: it reads as a laser show rather than as gunfire, and it flattens the muzzle flash it is drawn on top of. Every third round is the real-world belt convention and it is the convention for the same reason — you get the line that tells you where the rounds went without turning the gun into a light source.")]
        [Range(1, 10)] public int tracerEveryNRounds = 3;
        [Tooltip("Metres per second. Real tracers travel at the bullet's speed; a hitscan game wants them SLOW enough to be seen crossing the room — 250 crosses a 30 m arena in 0.12 s, which is a streak rather than a teleport.")]
        [Range(50f, 2000f)] public float tracerSpeed = 250f;
        [Tooltip("Trail width in metres. Wider than a real round by an order of magnitude, because a physically correct tracer is invisible at 1080p.")]
        [Range(0.005f, 0.15f)] public float tracerWidth = 0.02f;

        // ---------- The time-to-kill model ----------
        // Derived, never stored, never duplicated in a MonoBehaviour. The first
        // version of this got four things wrong, and every one of them reads as a
        // balance bug rather than as an arithmetic one: a one-shot weapon reported
        // a TTK of 0 and so could never satisfy a 200 ms floor, a shotgun's pull
        // was scored as a single pellet, a burst weapon was scored as if the pause
        // between bursts were free, and range did not exist at all.

        public float SecondsPerShot => 60f / Mathf.Max(1f, roundsPerMinute);

        /// <summary>
        /// What ONE TRIGGER PULL puts on a body at point blank — every pellet it
        /// throws, because one pull is one shot however many rays it casts.
        /// Scoring a 12x11 shotgun as 11 damage said it needed nine pulls to kill
        /// a 100 HP drone, when it in fact needs one.
        /// </summary>
        public float DamagePerShot => Mathf.Max(0.01f, bodyDamage) * Mathf.Max(1, pelletsPerShot);

        public int ShotsToKill(float targetHealth = 100f) =>
            ShotsFor(targetHealth, DamagePerShot);

        /// <summary>Pulls to kill at a distance, charged for falloff.</summary>
        public int ShotsToKillAtRange(float targetHealth, float metres) =>
            ShotsFor(targetHealth, DamageAtDistance(metres) * Mathf.Max(1, pelletsPerShot));

        public float TimeToKill(float targetHealth = 100f) =>
            TimeForShots(ShotsToKill(targetHealth));

        /// <summary>
        /// The same model, charged for the falloff at that range. The arcade
        /// window is a point-blank law; this is what the shotgun law is written
        /// against, because a shotgun's identity IS the gap between the two.
        /// </summary>
        public float TimeToKillAtRange(float targetHealth, float metres) =>
            TimeForShots(ShotsToKillAtRange(targetHealth, metres));

        /// <summary>
        /// Wall-clock seconds from the first round leaving the barrel to the Nth:
        /// N-1 cadence gaps, plus one burstPause for every burst boundary crossed.
        /// WeaponController adds burstPause ON TOP of the cadence after the last
        /// round of a burst (see FireOneShot), so this mirrors the gun that ships
        /// rather than an idealised one that fires its bursts for free.
        /// </summary>
        public float TimeForShots(int shots)
        {
            int gaps = Mathf.Max(0, shots - 1);
            float time = gaps * SecondsPerShot;
            if (fireMode == FireMode.Burst) time += (gaps / Mathf.Max(1, burstCount)) * burstPause;
            return time;
        }

        /// <summary>
        /// Floored at one. A weapon that kills in a single pull needs ONE shot,
        /// not zero, and that difference is the entire reason a sniper could never
        /// pass a window written for rifles.
        /// </summary>
        private static int ShotsFor(float targetHealth, float damagePerShot) =>
            Mathf.Max(1, Mathf.CeilToInt(targetHealth / Mathf.Max(0.01f, damagePerShot)));

        /// <summary>Damage at a distance, after falloff. Used by the controller and by tests.</summary>
        public float DamageAtDistance(float distance)
        {
            float start = falloffRange.x;
            float end = Mathf.Max(start + 0.01f, falloffRange.y);
            float t = Mathf.Clamp01((distance - start) / (end - start));
            return bodyDamage * Mathf.Lerp(1f, minDamageMultiplier, t);
        }

        // ---------- The balance laws ----------
        // Boundaries, not dials. These are what an authored asset is checked
        // AGAINST — the same kind of number as MAX_FOLLOW_UPS_PER_PULL, a ceiling
        // rather than a knob — so they are const rather than a ScriptableObject
        // field. They live here, in one place, because OnValidate and
        // WeaponDataTests both read them: a law with two copies is a law that gets
        // edited on one side to make a test go green.

        /// <summary>The arcade window, in milliseconds. The defining choice of the whole game.</summary>
        public const float ARCADE_TTK_MIN_MS = 200f;
        public const float ARCADE_TTK_MAX_MS = 400f;

        /// <summary>
        /// The Inspector warns wider than the test fails, so a weapon half-way
        /// through being authored does not scream on every keystroke.
        /// </summary>
        public const float ARCADE_TTK_WARN_MIN_MS = 150f;
        public const float ARCADE_TTK_WARN_MAX_MS = 500f;

        /// <summary>The range by which a shotgun must have stopped being a one-pull weapon.</summary>
        public const float SHOTGUN_TWO_PULL_METRES = 10f;

        /// <summary>
        /// What a one-shot weapon pays for the privilege. Killing instantly is
        /// only a trade if lining up the NEXT one costs more than the rifle's
        /// entire time-to-kill: 0.35 s to aim plus 0.9 s to cycle is ~1.25 s
        /// against the AR's 0.257 s. That gap IS the balance — not TTK, which for
        /// a sniper is zero by design and therefore says nothing at all.
        /// </summary>
        public const float ONE_SHOT_MIN_ADS_SECONDS = 0.35f;
        public const float ONE_SHOT_MIN_CYCLE_SECONDS = 0.9f;

        /// <summary>Which law this weapon answers to. weaponClass's first real reader.</summary>
        public BalanceLaw Law => LawFor(weaponClass);

        public static BalanceLaw LawFor(WeaponClass forClass) => forClass switch
        {
            WeaponClass.Shotgun => BalanceLaw.ContactBurst,
            WeaponClass.Sniper => BalanceLaw.ReEngagementCost,
            WeaponClass.Launcher => BalanceLaw.ReEngagementCost,
            // Anything that has not argued its way out answers to the game's
            // identity. Defaulting the other way would let a new weapon class opt
            // out of the only balance rule this project has, simply by existing.
            _ => BalanceLaw.ArcadeTtkWindow,
        };

#if UNITY_EDITOR
        // Surfaces the number that actually defines the game, right in the
        // Inspector — but the number differs by class. Judging a sniper by TTK
        // warned on every correctly authored sniper asset forever, which is the
        // fastest way to teach a developer to ignore the console.
        private void OnValidate()
        {
            if (roundsPerMinute <= 0f) roundsPerMinute = 1f;
            if (reloadEmptyTime < reloadTime) reloadEmptyTime = reloadTime;
            if (falloffRange.y <= falloffRange.x) falloffRange.y = falloffRange.x + 1f;
            if (magazineSize < 1) magazineSize = 1;
            if (reserveAmmo < 0) reserveAmmo = 0;
            if (pelletsPerShot < 1) pelletsPerShot = 1;

            // A launcher with no round is the quietest failure a weapon can have:
            // it consumes ammo, kicks, flashes, plays its fire layers and puts
            // nothing in the air. Nothing else in the fire path can notice —
            // there is no ray to miss and no impact to be absent.
            if (delivery == DeliveryMode.Projectile && projectilePrefab == null)
            {
                Debug.LogWarning(
                    $"[{name}] delivers by projectile and has no projectilePrefab — it will fire, kick, flash and " +
                    "produce no round at all. Assign the pooled prefab carrying CoD.Core.Projectile.", this);
            }

            switch (Law)
            {
                case BalanceLaw.ArcadeTtkWindow:
                {
                    // No `ttk > 0` escape any more: a rifle-class weapon that
                    // one-shots reports 0 ms, and that is precisely the mistake
                    // worth shouting about rather than the one worth excusing.
                    float ttk = TimeToKill() * 1000f;
                    if (ttk < ARCADE_TTK_WARN_MIN_MS || ttk > ARCADE_TTK_WARN_MAX_MS)
                    {
                        Debug.LogWarning(
                            $"[{name}] TTK is {ttk:F0} ms — outside the {ARCADE_TTK_MIN_MS:F0}-{ARCADE_TTK_MAX_MS:F0} ms arcade target. " +
                            "Intentional? TTK is the defining choice of the whole game; change it deliberately, not by accident.",
                            this);
                    }
                    break;
                }

                case BalanceLaw.ContactBurst:
                {
                    // A shotgun IS the gap between these two numbers. One pull at
                    // every range is a sniper without a scope; two pulls at
                    // contact is just a bad rifle.
                    int contact = ShotsToKill();
                    if (contact > 1)
                    {
                        Debug.LogWarning(
                            $"[{name}] needs {contact} pulls to kill at contact — a shotgun that does not " +
                            $"one-pull point blank has no identity. bodyDamage x pelletsPerShot is {DamagePerShot:F0}.",
                            this);
                    }
                    else if (ShotsToKillAtRange(100f, SHOTGUN_TWO_PULL_METRES) <= 1)
                    {
                        Debug.LogWarning(
                            $"[{name}] still one-pulls at {SHOTGUN_TWO_PULL_METRES:F0} m — falloffRange and " +
                            "minDamageMultiplier are the only things stopping a shotgun from being the best rifle in the game.",
                            this);
                    }
                    break;
                }

                case BalanceLaw.ReEngagementCost:
                {
                    // The exemption from the arcade window is EARNED BY THE ASSET,
                    // never granted by the enum. Without this first check,
                    // `weaponClass = Sniper` is a blanket exemption from every TTK
                    // bound in the project: 25 damage at 60 RPM takes four pulls
                    // and three full seconds to kill a 100 HP drone, answers to no
                    // TTK bound at all, and nothing anywhere says a word. That is
                    // the exact failure the split was written to prevent — a
                    // 99-damage sniper that does not one-shot — with the gate that
                    // caught it removed rather than replaced.
                    //
                    // Binary, so there is no wider warn band to author inside: a
                    // weapon either one-pulls or it is in the wrong class. The
                    // ContactBurst case above warns on the same terms.
                    int pulls = ShotsToKill();
                    if (pulls > 1)
                    {
                        Debug.LogWarning(
                            $"[{name}] needs {pulls} pulls to kill ({DamagePerShot:F0} damage per pull, {TimeToKill() * 1000f:F0} ms) — " +
                            $"it is exempt from the {ARCADE_TTK_MIN_MS:F0}-{ARCADE_TTK_MAX_MS:F0} ms arcade window on a " +
                            "one-shot premise it does not meet, so it currently answers to no time-to-kill bound at all. " +
                            "Raise bodyDamage x pelletsPerShot to 100, or give it a class whose law it can actually pass.",
                            this);
                    }

                    // One shot, one kill is the design, so TTK is not the axis at
                    // all. What keeps it honest is the cost of the SECOND shot.
                    if (adsTime < ONE_SHOT_MIN_ADS_SECONDS || SecondsPerShot < ONE_SHOT_MIN_CYCLE_SECONDS)
                    {
                        Debug.LogWarning(
                            $"[{name}] re-engages in {adsTime + SecondsPerShot:F2}s (ads {adsTime:F2}s + cycle {SecondsPerShot:F2}s) — " +
                            $"below the {ONE_SHOT_MIN_ADS_SECONDS:F2}s / {ONE_SHOT_MIN_CYCLE_SECONDS:F2}s floor a one-shot weapon is " +
                            "strictly better than the rifle at every range.",
                            this);
                    }
                    break;
                }
            }
        }
#endif
    }

    /// <summary>
    /// The shape of a weapon, and the only thing that decides which balance law it
    /// answers to. APPEND ONLY: Unity serialises an enum as its integer value, so
    /// inserting a member silently re-classes every asset authored after it — an
    /// AR that quietly becomes a shotgun changes which law it is held to and
    /// leaves no import error behind.
    /// </summary>
    public enum WeaponClass { AssaultRifle, SMG, Shotgun, Marksman, Sniper, Pistol, LMG, Launcher }

    /// <summary>
    /// Which balance law a weapon class answers to.
    ///
    /// The 200-400 ms TTK window is the game's identity for the core automatics
    /// and it is NOT universal. Enforcing it on a sniper produces a 99-damage
    /// rifle that does not one-shot — worse than either honest answer — and it
    /// breaks anyway the moment DifficultyConfig.healthMultiplierByWave (ramping
    /// to 3.5x) means nothing one-shots regardless of what was authored.
    /// </summary>
    public enum BalanceLaw
    {
        /// <summary>TTK inside the arcade window against a 100 HP body. AR, SMG, LMG, Pistol, Marksman.</summary>
        ArcadeTtkWindow,

        /// <summary>One pull at contact, two or more at ten metres. The shotgun, and only the shotgun.</summary>
        ContactBurst,

        /// <summary>
        /// One shot, one kill — judged on the cost of lining up the next one.
        /// Sniper, Launcher. The one-shot half is ASSERTED, not assumed: both
        /// OnValidate and WeaponDataTests check `ShotsToKill() == 1` before the
        /// re-engagement floors, because otherwise this enum value alone is a
        /// blanket exemption from every TTK bound in the project.
        /// </summary>
        ReEngagementCost,
    }
    public enum FireMode { Single, Burst, FullAuto }

    /// <summary>
    /// How a shot gets from the barrel to the target.
    ///
    /// APPEND ONLY, for <see cref="WeaponClass"/>'s reason: Unity serialises an
    /// enum as its integer value, and inserting a member here would turn every
    /// hitscan weapon authored after it into a launcher with no projectile
    /// prefab — a gun that consumes ammo and fires nothing.
    /// </summary>
    public enum DeliveryMode
    {
        /// <summary>A ray, resolved on the frame the trigger is pulled. Every weapon in the game but the launcher.</summary>
        Hitscan,

        /// <summary>
        /// A real object in the air, resolved when it arrives. Slower to hit and
        /// dodgeable, which is the trade a one-shot weapon pays — and the reason
        /// the rocket carries its own config: it OUTLIVES A WEAPON SWAP. See
        /// <c>CoD.Core.Projectile.Payload</c>.
        /// </summary>
        Projectile,
    }
}
