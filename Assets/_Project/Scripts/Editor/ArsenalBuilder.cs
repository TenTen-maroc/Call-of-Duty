#nullable enable
using System.Collections.Generic;
using System.Text;
using CoD.Weapons;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>
    /// Authors the arsenal: four new weapons as PURE DATA, plus the
    /// <see cref="WeaponRegistry"/> that lists every weapon the game ships.
    /// Run it from the CoD menu, or headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.ArsenalBuilder.BuildArsenalHeadless
    ///
    /// A SEPARATE FILE FROM GreyBoxBuilder, for MissionBuilder's reason. The grey
    /// box builds the arena — scenes, prefabs, materials, the navmesh — and owns
    /// the two weapons the loop was tuned around. A weapon is none of those
    /// things: it is a row of numbers, and the arsenal is the part of the project
    /// that grows every time someone wants a different gun. Two builders means the
    /// arsenal can be re-authored without re-baking a navmesh, and a mistake in
    /// here cannot cost the arena.
    ///
    /// RUN ORDER. Grey box FIRST, then this. AR_Standard and SMG_Rapid are the
    /// grey box's assets and this builder LOADS them rather than creating them —
    /// creating a second AR here would mean two files, two stableIds and a
    /// registry that disagrees with the loadout. <see cref="LoadShipped"/> says so
    /// by name rather than leaving the registry quietly two weapons short.
    ///
    /// IDEMPOTENT, with GreyBoxBuilder.LoadOrCreate's discipline: the configure
    /// callback runs ON CREATE ONLY, so a number a human moved in the Inspector
    /// survives a re-run. What IS re-asserted on every run is references — the
    /// registry's entries, and the feedback prefabs/clips on a weapon whose slot
    /// is empty — because a broken reference is not a tuning difference. It is a
    /// silent gun, or a weapon that no save can name.
    ///
    /// THE CLAIM THIS FILE TESTS. "A new weapon is a WeaponConfig asset and
    /// nothing else." Three of the four below are exactly that: the pistol, the
    /// marksman and the LMG are numbers in this file and zero lines anywhere else.
    /// The shotgun is where the claim frays, and the comment on
    /// <see cref="ConfigureShotgun"/> says exactly how — the honest answer is
    /// recorded there and in docs/systems/weapons.md rather than papered over.
    /// </summary>
    public static class ArsenalBuilder
    {
        private const string DataWeapons = "Assets/_Project/Data/Weapons";
        private const string Prefabs = "Assets/_Project/Prefabs";
        private const string Audio = "Assets/_Project/Audio";

        /// <summary>The registry WeaponDataTests cross-checks against the folder scan, in both directions.</summary>
        private const string RegistryPath = DataWeapons + "/Weapons.asset";

        /// <summary>The grey box's two. Loaded, never created — see the class header.</summary>
        private const string RiflePath = DataWeapons + "/AR_Standard.asset";

        /// <summary>The second grey-box weapon. See <see cref="RiflePath"/>.</summary>
        private const string SmgPath = DataWeapons + "/SMG_Rapid.asset";

        private const string PistolPath = DataWeapons + "/Pistol_Sidearm.asset";
        private const string MarksmanPath = DataWeapons + "/DMR_Marksman.asset";
        private const string LmgPath = DataWeapons + "/LMG_Support.asset";
        private const string ShotgunPath = DataWeapons + "/SG_Breacher.asset";

        /// <summary>
        /// The health every balance number in this file is written against. It is
        /// the Rusher's maxHealth and the same figure WeaponConfig.ShotsToKill
        /// defaults to — named here so the arithmetic in each Configure comment
        /// can be checked without opening two other files.
        /// </summary>
        private const float REFERENCE_DRONE_HEALTH = 100f;

        [MenuItem("CoD/Build Arsenal", false, 3)]
        public static void Build()
        {
            EnsureFolder(DataWeapons);

            // The grey box's two, loaded. A weapon this builder did not author is
            // still a weapon the registry has to list, or the folder/registry
            // cross-check fails — which is that gate working, not a problem to
            // route around.
            WeaponConfig rifle = LoadShipped(RiflePath);
            WeaponConfig smg = LoadShipped(SmgPath);

            // ---- the four new weapons ------------------------------------
            // Ordered by how much of the "weapons are data" claim each one tests:
            // the pistol is the control, the marksman moves the fire mode and the
            // headshot number, the LMG moves the magazine and the recoil, and the
            // shotgun is the one that needs geometry the config cannot express.

            WeaponConfig pistol = LoadOrCreate<WeaponConfig>(PistolPath, ConfigurePistol);
            WeaponConfig marksman = LoadOrCreate<WeaponConfig>(MarksmanPath, ConfigureMarksman);
            WeaponConfig lmg = LoadOrCreate<WeaponConfig>(LmgPath, ConfigureLmg);
            WeaponConfig shotgun = LoadOrCreate<WeaponConfig>(ShotgunPath, ConfigureShotgun);

            // ---- the house feedback set ----------------------------------
            // Re-asserted every run, but ONLY into an empty slot. A null muzzle
            // flash is a gun that fires with no light and no sound, and nothing
            // reports it — that is a broken reference, not a tuning choice. A slot
            // someone has already filled is the opposite: it is the choice, and
            // stamping over it every build is how a builder eats a day of work.
            //
            // All four adopt the SAME Fx_MuzzleFlash and Fx_ShellCasing prefabs
            // the rifle and SMG use, deliberately: those two are already in the
            // pool's prewarm list, so the arsenal quadruples without a single new
            // pool entry and without the first shot of a new gun allocating.
            GameObject? flash = Load<GameObject>(Prefabs + "/Fx_MuzzleFlash.prefab");
            GameObject? casing = Load<GameObject>(Prefabs + "/Fx_ShellCasing.prefab");
            AudioClip? fireClose = Load<AudioClip>(Audio + "/Fire_AR_Close.wav");
            AudioClip? fireTail = Load<AudioClip>(Audio + "/Fire_AR_Tail.wav");
            AudioClip? dryFire = Load<AudioClip>(Audio + "/DryFire.wav");
            AudioClip? reload = Load<AudioClip>(Audio + "/Reload_AR.wav");

            WeaponConfig[] authored = { pistol, marksman, lmg, shotgun };
            foreach (WeaponConfig weapon in authored)
            {
                AdoptHouseFeedback(weapon, flash, casing, fireClose, fireTail, dryFire, reload);
            }

            // ---- the registry --------------------------------------------
            // Every weapon, including the two this builder did not author.
            WeaponRegistry registry = LoadOrCreate<WeaponRegistry>(RegistryPath, _ => { });
            EnsureListed(registry, rifle, smg, pistol, marksman, lmg, shotgun);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Saved FIRST, then judged. A weapon that breaks its class law is
            // still a weapon someone has to open and fix, and throwing before the
            // write would leave nothing on disk to open.
            AssertEveryWeaponObeysItsLaw(registry);

            Debug.Log($"Arsenal built: {registry.Count} weapons listed in {RegistryPath}.");
        }

        /// <summary>Entry point for -executeMethod. Same work, non-zero exit on failure.</summary>
        public static void BuildArsenalHeadless()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Arsenal build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- the four weapons ----------
        //
        // Every number below is checked against WeaponConfig's own consts by
        // AssertEveryWeaponObeysItsLaw at the end of Build() and by
        // WeaponDataTests.EveryWeapon_ObeysTheLawOfItsClass in EditMode. The
        // arithmetic is spelled out in each header so a retune can be reasoned
        // about without running either.

        /// <summary>
        /// THE CONTROL, and the cleanest test of the claim: a pistol is a row of
        /// numbers and nothing else. Single fire, fastest ADS in the game, twelve
        /// rounds, and a falloff that ends the argument past thirty metres.
        ///
        /// 34 damage x 1 pellet = three pulls on a 100 HP drone, two gaps at
        /// 400 RPM (0.15 s) = 300 ms. Dead centre of the arcade window, and
        /// slower than the AR's 257 ms — which is the point. What it buys is
        /// 0.15 s to aim against the rifle's 0.25 s: the gun you bring up when
        /// something is already inside your guard.
        ///
        /// Three pulls in 300 ms is 6.7 clicks a second, which is fast but is the
        /// honest cost of a semi-auto. Raising the damage to two pulls would put
        /// it in the marksman's seat; lowering it to four would put the window out
        /// of reach of a human finger.
        /// </summary>
        private static void ConfigurePistol(WeaponConfig config)
        {
            config.stableId = "wpn_pistol_sidearm";
            config.displayName = "Sidearm";
            config.weaponClass = WeaponClass.Pistol;
            config.fireMode = FireMode.Single;
            config.roundsPerMinute = 400f;
            config.bodyDamage = 34f;
            config.headshotMultiplier = 1.5f;
            config.magazineSize = 12;
            config.reserveAmmo = 96;

            // The sidearm's whole case: it comes up first and it goes away first.
            config.adsTime = 0.15f;
            config.sprintToFireTime = 0.12f;
            config.reloadTime = 1.4f;
            config.reloadEmptyTime = 1.9f;
            config.swapTime = 0.35f;

            // Dies sooner and harder than the SMG (14-34 at 0.5). Past 30 m it is
            // 15.3 a shot: seven pulls, which is not a fight anyone wins.
            config.falloffRange = new Vector2(12f, 30f);
            config.minDamageMultiplier = 0.45f;
            config.maxRange = 120f;

            // A snappy gun that punishes spam. The kick is large for the calibre
            // and recovers almost fully, so a tapped pistol is accurate and a
            // mashed one is not.
            config.verticalKickFirstShot = 0.9f;
            config.verticalKickAtShotEight = 1.1f;
            config.horizontalKickMax = 0.3f;
            config.recoveryDelay = 0.06f;
            config.recoveryDuration = 0.18f;
            config.recoveryCompleteness = 0.95f;
            config.recoilSeed = 7331;

            config.baseSpread = 2.0f;
            config.spreadPerShot = 0.45f;
            config.maxSpread = 5.5f;

            config.adsFovMultiplier = 0.85f;
            config.cameraShakeAmplitude = 0.45f;
        }

        /// <summary>
        /// The marksman rifle, and it is NOT exempt from anything. A 2.0x
        /// headshot is the fantasy; the class is Marksman rather than Sniper, so
        /// it answers to the same 200-400 ms window the AR does and earns its
        /// place inside it rather than arguing its way out.
        ///
        /// 55 damage = two pulls on a 100 HP drone, one gap at 240 RPM (0.25 s) =
        /// 250 ms. Seven milliseconds off the AR, from half the rounds. The cost
        /// is everywhere else: a MISS costs 250 ms where the rifle's costs 86,
        /// the magazine is ten, and it aims in 0.32 s. RPM 240 sits inside the
        /// 150-300 band a marksman rifle has to live in — above 300 it is a
        /// two-shot AR, below 150 the window is unreachable at any damage that
        /// still needs two pulls.
        ///
        /// 55 x 2.0 = 110 on a weakpoint, so it DOES one-shot a headshot. That is
        /// deliberate and it is not a way around the law: the law is written
        /// against a body, and the head is the reward for the aim the rest of the
        /// numbers charge for.
        /// </summary>
        private static void ConfigureMarksman(WeaponConfig config)
        {
            config.stableId = "wpn_dmr_marksman";
            config.displayName = "Marksman Rifle";
            config.weaponClass = WeaponClass.Marksman;
            config.fireMode = FireMode.Single;
            config.roundsPerMinute = 240f;
            config.bodyDamage = 55f;
            config.headshotMultiplier = 2.0f;
            config.magazineSize = 10;
            config.reserveAmmo = 60;

            config.adsTime = 0.32f;
            config.sprintToFireTime = 0.28f;
            config.reloadTime = 2.4f;
            config.reloadEmptyTime = 3.0f;
            config.swapTime = 0.75f;

            // The only weapon in the arsenal that keeps its damage at the far end
            // of a lane: 46.75 at 90 m, still three pulls. Every other gun has
            // dropped to a fourth or a fifth pull by then.
            config.falloffRange = new Vector2(40f, 90f);
            config.minDamageMultiplier = 0.85f;
            config.maxRange = 200f;

            // Heavy per-shot kick, near-total recovery: each shot is a decision
            // and the sight settles before the next one is available anyway.
            config.verticalKickFirstShot = 1.6f;
            config.verticalKickAtShotEight = 1.9f;
            config.horizontalKickMax = 0.25f;
            config.recoveryDelay = 0.05f;
            config.recoveryDuration = 0.28f;
            config.recoveryCompleteness = 0.95f;
            config.adsRecoilMultiplier = 0.5f;
            config.recoilSeed = 2718;

            // Hipfire is not an option, and the numbers say so out loud.
            config.baseSpread = 5.5f;
            config.spreadPerShot = 1.2f;
            config.maxSpread = 9f;

            // The scope: a real magnification step, and the sensitivity drop that
            // makes it usable. 0.45 vertical FOV against the base is roughly 2.2x.
            config.adsFovMultiplier = 0.45f;
            config.adsSensitivityMultiplier = 0.45f;
            config.fovKickOnFire = 2.2f;
            config.cameraShakeAmplitude = 0.9f;
        }

        /// <summary>
        /// The support gun. A hundred rounds, a five-second empty reload, and the
        /// heaviest sustained climb in the game — the weapon that answers "the
        /// lane is full" rather than "that one drone".
        ///
        /// 20 damage = five pulls, four gaps at 750 RPM (0.08 s) = 320 ms. Slower
        /// than the AR and the SMG on any single drone, which is exactly right:
        /// what it buys is never reloading during a wave. 100 rounds at 750 RPM is
        /// eight seconds of continuous fire; the AR's magazine is 2.6.
        ///
        /// THE COST IS AUTHORED, not implied. 0.42 s to aim, 1.0 s to swap, a
        /// 5.4 s reload from empty, and a recovery that only returns 70% of the
        /// climb — hold the trigger and the gun walks off the target and stays
        /// there. Without those four numbers a big magazine is a free upgrade.
        /// </summary>
        private static void ConfigureLmg(WeaponConfig config)
        {
            config.stableId = "wpn_lmg_support";
            config.displayName = "LMG";
            config.weaponClass = WeaponClass.LMG;
            config.fireMode = FireMode.FullAuto;
            config.roundsPerMinute = 750f;
            config.bodyDamage = 20f;
            config.headshotMultiplier = 1.4f;
            config.magazineSize = 100;
            config.reserveAmmo = 300;

            config.adsTime = 0.42f;
            config.sprintToFireTime = 0.32f;
            config.reloadTime = 4.2f;
            config.reloadEmptyTime = 5.4f;
            config.swapTime = 1.0f;
            // Later than the house 0.75: committing to an LMG reload is meant to
            // be a commitment, so cancelling it keeps the rounds only right at the
            // end.
            config.reloadCommitPoint = 0.85f;

            config.falloffRange = new Vector2(20f, 55f);
            config.minDamageMultiplier = 0.55f;
            config.maxRange = 200f;

            // The signature. Recovery at 0.7 is the lowest in the arsenal:
            // CommitRecoilToAim folds the unrecovered 30% into the real aim point
            // every frame, so a held trigger genuinely climbs off the target.
            config.verticalKickFirstShot = 0.7f;
            config.verticalKickAtShotEight = 1.9f;
            config.horizontalKickMax = 0.7f;
            config.recoveryDelay = 0.14f;
            config.recoveryDuration = 0.45f;
            config.recoveryCompleteness = 0.7f;
            config.recoilSeed = 8128;

            // Hipfiring an LMG is spraying, and the cone says so.
            config.baseSpread = 4.2f;
            config.spreadPerShot = 0.22f;
            config.maxSpread = 9f;
            config.spreadDecayRate = 3f;
            config.movingMultiplier = 1.7f;
            config.crouchedMultiplier = 0.55f;

            config.adsFovMultiplier = 0.8f;
            config.cameraShakeAmplitude = 0.8f;
        }

        /// <summary>
        /// The breaching shotgun — twelve pellets, one pull, and the weapon where
        /// "a new weapon is data and nothing else" stops being true.
        ///
        /// THE BALANCE LAW IT PASSES, and it passes it on data alone. ContactBurst
        /// is a gap between two numbers: 10 damage x 12 pellets = 120 at contact,
        /// so one pull; at ten metres the (6, 16) falloff has it at 0.72 of that
        /// (86.4) so it needs two. Both halves are load-bearing — one pull at
        /// every range is a sniper without a scope, two pulls at contact is a bad
        /// rifle — and both are pure arithmetic over fields that already exist.
        ///
        /// WHAT IT CANNOT EXPRESS, AND THIS IS THE HONEST ANSWER TO THE CLAIM.
        /// A shotgun's pattern is GEOMETRY: twelve pellets arranged in a fixed
        /// cone, the same cone on the first shot and the fiftieth, whether hip or
        /// aimed. Bloom is something else entirely — WeaponRuntime.CurrentSpread
        /// starts at baseSpread, grows by spreadPerShot, decays back, and
        /// WeaponController.CurrentSpreadDegrees returns EXACTLY ZERO while
        /// aiming, because aimed accuracy in this game is governed by recoil
        /// alone. FireOneShot then casts every pellet through that one number.
        ///
        /// So an aimed shotgun today puts all twelve pellets on a single point:
        /// 120 damage in one ray at any range inside the falloff, which is a
        /// sniper. The numbers below cannot fix that, because the only cone the
        /// config owns is the one ADS is defined to zero. Hipfire at baseSpread
        /// 4.0 is a cone, and it is a RANDOM one that grows as you fire — a
        /// pattern that changes shape under sustained fire is not a pattern.
        ///
        /// The fix is one field and one line, and neither is authorable from here:
        /// `pelletSpreadDegrees` on WeaponConfig, and CastOneRay taking a cone
        /// that is `max(pelletSpreadDegrees, bloom)` rather than bloom alone. Both
        /// files are outside this task's remit; the gap is recorded here, in
        /// docs/systems/weapons.md, and in EveryMultiPelletWeapon_ThrowsACone
        /// rather than papered over with a number that only looks like a pattern.
        /// </summary>
        private static void ConfigureShotgun(WeaponConfig config)
        {
            config.stableId = "wpn_sg_breacher";
            config.displayName = "Shotgun";
            config.weaponClass = WeaponClass.Shotgun;
            config.fireMode = FireMode.Single;
            config.roundsPerMinute = 70f;
            config.bodyDamage = 10f;
            config.pelletsPerShot = 12;

            // 1.2x, not 1.5x. Twelve pellets means a "headshot" is however many of
            // them happened to land on a small collider, which is luck rather than
            // aim; paying it at rifle rates makes the luckiest pull in the game
            // the strongest one.
            config.headshotMultiplier = 1.2f;

            config.magazineSize = 6;
            config.reserveAmmo = 48;
            config.adsTime = 0.28f;
            config.sprintToFireTime = 0.18f;
            config.reloadTime = 3.4f;
            config.reloadEmptyTime = 4.0f;
            config.swapTime = 0.7f;

            // The steep one. Full damage to 6 m, 0.3 of it past 16 — the whole
            // identity of the weapon lives in those ten metres.
            config.falloffRange = new Vector2(6f, 16f);
            config.minDamageMultiplier = 0.3f;
            // 40 m rather than the house 200. Past the falloff a pellet is 3
            // damage; a ray that still reaches across the arena only buys the
            // player a hitmarker that means nothing.
            config.maxRange = 40f;

            config.verticalKickFirstShot = 2.2f;
            config.verticalKickAtShotEight = 2.6f;
            config.horizontalKickMax = 0.5f;
            config.recoveryDelay = 0.1f;
            config.recoveryDuration = 0.4f;
            config.recoveryCompleteness = 0.9f;
            config.recoilSeed = 5150;

            // NOT the pattern — see the header. This is hipfire bloom on top of
            // whatever pattern the fire path eventually grows, and it is non-zero
            // so that a hipfired shotgun is at least a cone today.
            config.baseSpread = 4.0f;
            config.spreadPerShot = 0.5f;
            config.maxSpread = 8f;

            config.adsFovMultiplier = 0.9f;
            config.cameraShakeAmplitude = 1.1f;
        }

        // ---------- the registry ----------

        /// <summary>
        /// Every weapon listed exactly once, in build order, with nothing a human
        /// added thrown away.
        ///
        /// Lifted from MissionBuilder.EnsureInCatalog because it is the same
        /// problem: order is presentation, appending is the only structural write,
        /// and the one thing re-asserted in place is the REFERENCE — for a slot
        /// whose asset was deleted and re-created and now points at nothing.
        ///
        /// Nulls are the exception, and they are compacted rather than kept. A
        /// null is what a deleted asset leaves behind; it still counts toward
        /// Length, so the list looks the right size while a weapon has vanished
        /// from every gate that walks it. It is never authored intent, so removing
        /// it is a repair rather than a stamp — announced, because a builder that
        /// silently changes the shape of an authored list is how the file in the
        /// repo and the file being played drift apart.
        /// </summary>
        private static void EnsureListed(WeaponRegistry registry, params WeaponConfig[] weapons)
        {
            var listed = new List<WeaponConfig>(registry.allWeapons.Length + weapons.Length);
            int holes = 0;
            foreach (WeaponConfig existing in registry.allWeapons)
            {
                if (existing == null)
                {
                    holes++;
                    continue;
                }
                listed.Add(existing);
            }

            if (holes > 0)
            {
                Debug.LogWarning(
                    $"[{registry.name}] had {holes} empty slot(s) — the residue of a deleted asset. Removed: an " +
                    "empty slot counts toward Length, so the registry looks complete while a weapon is outside " +
                    "every balance gate that walks it.", registry);
            }

            foreach (WeaponConfig weapon in weapons)
            {
                int index = IndexOfId(listed, weapon.stableId);
                if (index < 0)
                {
                    listed.Add(weapon);
                    continue;
                }
                // Same id, different object: the asset behind that entry was
                // replaced. Re-point it rather than appending a duplicate id,
                // which would alias two weapons into one for every save.
                listed[index] = weapon;
            }

            registry.allWeapons = listed.ToArray();
            EditorUtility.SetDirty(registry);
        }

        /// <summary>Ordinal, because a stableId is an identifier and not display text. See WeaponRegistry.ByStableId.</summary>
        private static int IndexOfId(List<WeaponConfig> weapons, string stableId)
        {
            for (int i = 0; i < weapons.Count; i++)
            {
                if (string.Equals(weapons[i].stableId, stableId, System.StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        // ---------- the gate ----------

        /// <summary>
        /// Every weapon in the registry, against the law of its class, at build
        /// time.
        ///
        /// A second copy of WeaponDataTests.AssertObeysTheLawOfItsClass in spirit
        /// and NOT in substance: both read the same consts off WeaponConfig, so
        /// there is one law and two readers rather than two laws. It exists
        /// because the person most likely to break a weapon is the person editing
        /// this file, and the test suite needs a licence, an editor and a minute —
        /// this reports in the same breath as the build that caused it.
        ///
        /// Throws rather than warns. OnValidate already warns per asset, and a
        /// warning is what a builder run scrolls past; the arsenal being wrong is
        /// worth a red line and a non-zero exit code from the headless path.
        /// </summary>
        private static void AssertEveryWeaponObeysItsLaw(WeaponRegistry registry)
        {
            var broken = new StringBuilder();

            foreach (WeaponConfig weapon in registry.allWeapons)
            {
                if (weapon == null) continue;
                string? complaint = LawComplaint(weapon);
                if (complaint == null) continue;

                Debug.LogError($"[{weapon.name}] {complaint}", weapon);
                broken.Append("\n  - ").Append(weapon.name).Append(": ").Append(complaint);
            }

            if (broken.Length == 0) return;
            throw new System.InvalidOperationException(
                "The arsenal does not obey its own balance laws:" + broken +
                "\nThe laws are consts on WeaponConfig and WeaponDataTests reads the same ones. " +
                "A weapon that cannot satisfy its class is the wrong weapon, not the wrong law.");
        }

        /// <summary>What is wrong with this weapon, or null when nothing is. Mirrors the three BalanceLaw branches.</summary>
        private static string? LawComplaint(WeaponConfig weapon)
        {
            switch (weapon.Law)
            {
                case BalanceLaw.ArcadeTtkWindow:
                {
                    float ttk = weapon.TimeToKill(REFERENCE_DRONE_HEALTH) * 1000f;
                    if (ttk < WeaponConfig.ARCADE_TTK_MIN_MS || ttk > WeaponConfig.ARCADE_TTK_MAX_MS)
                    {
                        return $"TTK is {ttk:F0} ms, outside the " +
                               $"{WeaponConfig.ARCADE_TTK_MIN_MS:F0}-{WeaponConfig.ARCADE_TTK_MAX_MS:F0} ms arcade window " +
                               $"({weapon.ShotsToKill(REFERENCE_DRONE_HEALTH)} pulls at {weapon.roundsPerMinute:F0} RPM)";
                    }
                    return null;
                }

                case BalanceLaw.ContactBurst:
                {
                    if (weapon.ShotsToKill(REFERENCE_DRONE_HEALTH) > 1)
                    {
                        return $"does not one-pull at contact ({weapon.DamagePerShot:F0} damage per pull) — " +
                               "a shotgun that cannot do that has no identity";
                    }
                    if (weapon.ShotsToKillAtRange(REFERENCE_DRONE_HEALTH, WeaponConfig.SHOTGUN_TWO_PULL_METRES) < 2)
                    {
                        return $"still one-pulls at {WeaponConfig.SHOTGUN_TWO_PULL_METRES:F0} m — " +
                               "falloffRange and minDamageMultiplier are the only things stopping it being the best rifle in the game";
                    }
                    return null;
                }

                case BalanceLaw.ReEngagementCost:
                {
                    // The premise before the floors, in that order, for the reason
                    // WeaponDataTests gives: without it the class is a blanket
                    // exemption from every TTK bound in the project.
                    if (weapon.ShotsToKill(REFERENCE_DRONE_HEALTH) != 1)
                    {
                        return $"needs {weapon.ShotsToKill(REFERENCE_DRONE_HEALTH)} pulls to kill — it is exempt from the " +
                               "arcade window on a one-shot premise it does not meet, so it answers to no TTK bound at all";
                    }
                    if (weapon.adsTime < WeaponConfig.ONE_SHOT_MIN_ADS_SECONDS ||
                        weapon.SecondsPerShot < WeaponConfig.ONE_SHOT_MIN_CYCLE_SECONDS)
                    {
                        return $"re-engages in {weapon.adsTime + weapon.SecondsPerShot:F2}s, below the " +
                               $"{WeaponConfig.ONE_SHOT_MIN_ADS_SECONDS:F2}s / {WeaponConfig.ONE_SHOT_MIN_CYCLE_SECONDS:F2}s floor — " +
                               "a one-shot weapon that re-aims for free is strictly better than the rifle everywhere";
                    }
                    return null;
                }

                default:
                    return "answers to no balance law at all";
            }
        }

        // ---------- helpers ----------

        /// <summary>
        /// Fills the feedback slots this project's other weapons use, and ONLY
        /// where the slot is empty. See the call site for why the direction
        /// matters: an empty slot is a silent gun, a filled one is a decision.
        /// </summary>
        private static void AdoptHouseFeedback(WeaponConfig weapon, GameObject? flash, GameObject? casing,
            AudioClip? fireClose, AudioClip? fireTail, AudioClip? dryFire, AudioClip? reload)
        {
            bool changed = false;
            if (weapon.muzzleFlashPrefab == null && flash != null) { weapon.muzzleFlashPrefab = flash; changed = true; }
            if (weapon.shellCasingPrefab == null && casing != null) { weapon.shellCasingPrefab = casing; changed = true; }
            if (weapon.fireCloseLayer == null && fireClose != null) { weapon.fireCloseLayer = fireClose; changed = true; }
            if (weapon.fireTailLayer == null && fireTail != null) { weapon.fireTailLayer = fireTail; changed = true; }
            if (weapon.dryFireClip == null && dryFire != null) { weapon.dryFireClip = dryFire; changed = true; }
            if (weapon.reloadClip == null && reload != null) { weapon.reloadClip = reload; changed = true; }
            if (changed) EditorUtility.SetDirty(weapon);
        }

        /// <summary>
        /// A weapon this builder does not own. Missing is fatal and says which
        /// builder owns it: a registry two weapons short fails the folder/registry
        /// cross-check, and "the arsenal is incomplete" is a far less useful
        /// message than "run the grey box first".
        /// </summary>
        private static WeaponConfig LoadShipped(string path)
        {
            WeaponConfig? weapon = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
            if (weapon == null)
            {
                throw new System.InvalidOperationException(
                    $"The arsenal needs '{path}' and it is not there. Run CoD -> Build Grey Box first: the rifle and " +
                    "the SMG are its assets, and a registry that omits them disagrees with the weapons folder in " +
                    "exactly the way WeaponDataTests exists to catch.");
            }
            return weapon;
        }

        /// <summary>
        /// A shared asset, warned about rather than thrown on. A missing muzzle
        /// flash is a worse gun, not a broken arsenal — and the placeholder audio
        /// is generated by a script that may simply not have been run yet.
        /// </summary>
        private static T? Load<T>(string path) where T : Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"Missing '{path}'. The new weapons will carry an empty slot where it belongs — for audio, run " +
                    "node Tools/make-placeholder-audio.mjs; for a prefab, run CoD -> Build Grey Box.");
            }
            return asset;
        }

        /// <summary>
        /// Loads an asset, or creates and configures one if it is not there.
        ///
        /// A third copy of GreyBoxBuilder.LoadOrCreate, and the duplication is the
        /// point for MissionBuilder's reason: that method is private, and this
        /// file exists precisely so that authoring a weapon never edits the
        /// three-thousand-line builder that owns the scenes.
        ///
        /// CONFIGURE RUNS ON CREATE ONLY. An asset that already exists comes back
        /// untouched, which is what lets a human retune a gun in the Inspector and
        /// keep it across a re-run — and it is also the trap: RENAMING a path here
        /// does not rename the asset. It creates a fresh default one, discards
        /// every tuned value in the old file, and reports success.
        /// </summary>
        private static T LoadOrCreate<T>(string path, System.Action<T> configure) where T : ScriptableObject
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                configure(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
        }
    }
}
