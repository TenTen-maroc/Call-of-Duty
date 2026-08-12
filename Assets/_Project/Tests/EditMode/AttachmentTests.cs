#nullable enable
using CoD.Core;
using CoD.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The attachment system, and the two rules it was designed around.
    ///
    /// RULE ONE: AN ATTACHMENT NEVER TOUCHES THE ASSET. Configs are read-only at
    /// runtime because Domain Reload is disabled — a write to a `WeaponConfig`
    /// persists between Play sessions and silently rewrites the shipped balance.
    /// Every delta lands on `WeaponRuntime`'s own sheet, and the config keeps
    /// answering for the AUTHORED weapon, which is what every balance law in the
    /// project is written against.
    ///
    /// RULE TWO: NOTHING HERE TOUCHES FIRE RATE. `WeaponStat` has no entry for
    /// it, deliberately — cadence is scheduled off `Config.SecondsPerShot` and
    /// pinned by `WeaponCadenceRegressionTests`, and rate of fire is one half of
    /// the time-to-kill the whole game is tuned around. The test at the bottom is
    /// what stops that rule being quietly relaxed by an enum edit.
    ///
    /// EditMode, because none of it needs a scene: `WeaponRuntime` is a plain C#
    /// object and `AttachmentConfig` is a ScriptableObject with no MonoBehaviour
    /// anywhere in the path.
    /// </summary>
    public sealed class AttachmentTests
    {
        private const string ATTACHMENT_FOLDER = "Assets/_Project/Data/Attachments";

        private static WeaponConfig BuildRifle()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfig>();
            config.name = "Test_Rifle";
            config.stableId = "wpn_test_attach_rifle";
            config.displayName = "Test Rifle";
            config.weaponClass = WeaponClass.AssaultRifle;
            config.bodyDamage = 25f;
            config.roundsPerMinute = 700f;
            config.magazineSize = 30;
            config.reserveAmmo = 180;
            config.adsTime = 0.25f;
            config.adsFovMultiplier = 0.75f;
            config.adsSensitivityMultiplier = 0.75f;
            config.sprintToFireTime = 0.2f;
            config.falloffRange = new Vector2(25f, 60f);
            config.minDamageMultiplier = 0.6f;
            config.baseSpread = 2.5f;
            config.maxSpread = 6f;
            return config;
        }

        private static AttachmentConfig Build(string id, AttachmentSlot slot,
            params AttachmentConfig.Modifier[] modifiers)
        {
            var config = ScriptableObject.CreateInstance<AttachmentConfig>();
            config.name = id;
            config.stableId = id;
            config.displayName = id;
            config.slot = slot;
            config.modifiers = modifiers;
            return config;
        }

        private static AttachmentConfig.Modifier Mult(WeaponStat stat, float value) =>
            new() { stat = stat, kind = StatModifierKind.Multiplier, value = value };

        private static AttachmentConfig.Modifier Flat(WeaponStat stat, float value) =>
            new() { stat = stat, kind = StatModifierKind.FlatAdd, value = value };

        [Test]
        public void AFittedAttachment_MovesTheRuntimeAndNotTheAsset()
        {
            WeaponConfig config = BuildRifle();
            AttachmentConfig grip = Build("grip", AttachmentSlot.Underbarrel,
                Mult(WeaponStat.Damage, 1.2f), Mult(WeaponStat.MagazineSize, 1.5f));

            var runtime = new WeaponRuntime(config);
            Assert.AreEqual(25f, runtime.Damage, 0.001f, "an unfitted weapon must read its authored damage");

            Assert.IsTrue(runtime.TryFit(grip));
            Assert.AreEqual(30f, runtime.Damage, 0.001f, "the fitted weapon must read the modified damage");
            Assert.AreEqual(45, runtime.MagazineSize);

            // THE RULE. Domain Reload is off, so a write here would persist into
            // the next Play session and rewrite the shipped balance permanently.
            Assert.AreEqual(25f, config.bodyDamage, 0.001f,
                "fitting an attachment must never write to the WeaponConfig asset");
            Assert.AreEqual(30, config.magazineSize,
                "fitting an attachment must never write to the WeaponConfig asset");

            Object.DestroyImmediate(grip);
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// A weapon starts with a FULL magazine, and "full" means the magazine it
        /// actually has.
        ///
        /// The constructor seeds `CurrentAmmo` after the authored attachments are
        /// fitted, precisely so an extended magazine does not begin every run
        /// holding a stock magazine's rounds — a bug that would read as "the gun
        /// is not reloading properly" rather than as an ordering mistake.
        /// </summary>
        [Test]
        public void AnExtendedMagazine_StartsFull_NotStockFull()
        {
            WeaponConfig config = BuildRifle();
            AttachmentConfig extended = Build("mag", AttachmentSlot.Magazine,
                Mult(WeaponStat.MagazineSize, 1.5f));
            config.attachments = new[] { extended };

            var runtime = new WeaponRuntime(config);

            Assert.AreEqual(45, runtime.MagazineSize);
            Assert.AreEqual(45, runtime.CurrentAmmo,
                "a weapon shipping with an extended magazine must start with that magazine full");
            Assert.IsTrue(runtime.IsFull, "and must not think it needs a reload");

            Object.DestroyImmediate(extended);
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// One per slot. Fitting a second optic REPLACES the first rather than
        /// stacking with it — two scopes on one rifle is not a build, and stacking
        /// would let one slot multiply itself without limit.
        /// </summary>
        [Test]
        public void TwoAttachmentsInOneSlot_Replace_RatherThanStack()
        {
            WeaponConfig config = BuildRifle();
            AttachmentConfig first = Build("optic_a", AttachmentSlot.Optic, Mult(WeaponStat.AdsFov, 0.5f));
            AttachmentConfig second = Build("optic_b", AttachmentSlot.Optic, Mult(WeaponStat.AdsFov, 0.8f));

            var runtime = new WeaponRuntime(config);
            Assert.IsTrue(runtime.TryFit(first));
            Assert.AreEqual(0.375f, runtime.AdsFovMultiplier, 0.0001f);

            Assert.IsTrue(runtime.TryFit(second));
            Assert.AreEqual(1, runtime.Attachments.Count, "one slot holds one attachment");
            Assert.AreEqual(second, runtime.InSlot(AttachmentSlot.Optic));
            Assert.AreEqual(0.6f, runtime.AdsFovMultiplier, 0.0001f,
                "the replaced optic's zoom must be gone, not folded in underneath the new one");

            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// Removing an attachment removes its effect completely.
        ///
        /// The sheet is rebuilt from scratch rather than un-applied, which is
        /// `StatSheet`'s rule and exists so there is no inverse operation to get
        /// wrong — a divide that does not exactly undo a multiply is how a stat
        /// drifts a fraction of a percent per shop visit.
        /// </summary>
        [Test]
        public void RemovingAnAttachment_LeavesNothingBehind()
        {
            WeaponConfig config = BuildRifle();
            AttachmentConfig grip = Build("grip", AttachmentSlot.Underbarrel, Mult(WeaponStat.Damage, 1.2f));

            var runtime = new WeaponRuntime(config);
            runtime.TryFit(grip);
            Assert.AreEqual(30f, runtime.Damage, 0.001f);

            Assert.IsTrue(runtime.Remove(grip));
            Assert.AreEqual(25f, runtime.Damage, 0.0001f,
                "removing an attachment must return the weapon to its authored number exactly");

            Object.DestroyImmediate(grip);
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// `allowedClasses` is `weaponClass`'s second real reader, and it REFUSES
        /// rather than silently doing nothing — so a shop can decline the sale
        /// instead of charging for an optic that has no effect.
        /// </summary>
        [Test]
        public void AnAttachmentRefusesAWeaponClassItIsNotFor()
        {
            WeaponConfig rifle = BuildRifle();
            AttachmentConfig sniperOnly = Build("scope", AttachmentSlot.Optic, Mult(WeaponStat.AdsFov, 0.4f));
            sniperOnly.allowedClasses = new[] { WeaponClass.Sniper };

            var runtime = new WeaponRuntime(rifle);
            Assert.IsFalse(runtime.TryFit(sniperOnly),
                "a class-restricted attachment must REFUSE, so the caller can refund rather than charge");
            Assert.AreEqual(0, runtime.Attachments.Count);
            Assert.AreEqual(0.75f, runtime.AdsFovMultiplier, 0.0001f, "and must have changed nothing");

            Object.DestroyImmediate(sniperOnly);
            Object.DestroyImmediate(rifle);
        }

        /// <summary>
        /// The falloff RANGE stat stretches both ends of the window, and the
        /// runtime's `DamageAtDistance` is what gameplay reads — while the
        /// config's own keeps answering for the authored weapon, which is what
        /// every balance law in the project is measured against.
        /// </summary>
        [Test]
        public void ARangeAttachment_MovesTheFalloffWindow_ButNotTheConfigsAnswer()
        {
            WeaponConfig config = BuildRifle();     // falloff 25 -> 60, min 0.6
            AttachmentConfig barrel = Build("barrel", AttachmentSlot.Barrel, Mult(WeaponStat.Range, 1.4f));

            var runtime = new WeaponRuntime(config);
            float stockAt40 = runtime.DamageAtDistance(40f);
            Assert.IsTrue(runtime.TryFit(barrel));

            Assert.Greater(runtime.DamageAtDistance(40f), stockAt40,
                "a longer barrel must hit harder at 40 m, because the falloff starts later");
            Assert.AreEqual(config.DamageAtDistance(40f), stockAt40, 0.0001f,
                "the CONFIG must keep answering for the authored weapon — the balance laws read it");

            Object.DestroyImmediate(barrel);
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// Flats add before multipliers multiply, exactly as the passive sheet
        /// does. Two pipelines with two orders of operations would be two balance
        /// models, and the difference only shows up once somebody stacks.
        /// </summary>
        [Test]
        public void FlatsAddBeforeMultipliersMultiply()
        {
            WeaponConfig config = BuildRifle();
            AttachmentConfig a = Build("a", AttachmentSlot.Ammo, Flat(WeaponStat.Damage, 5f));
            AttachmentConfig b = Build("b", AttachmentSlot.Barrel, Mult(WeaponStat.Damage, 2f));

            var runtime = new WeaponRuntime(config);
            runtime.TryFit(a);
            runtime.TryFit(b);

            // (25 + 5) * 2 = 60, not 25 * 2 + 5 = 55.
            Assert.AreEqual(60f, runtime.Damage, 0.0001f);

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// THE GUARD ON RULE TWO. `WeaponStat` must never grow a fire-rate entry.
        ///
        /// This reads like a strange test until you look at what it protects:
        /// cadence is scheduled from `Config.SecondsPerShot`, the authored number,
        /// and `WeaponCadenceRegressionTests` pins the overshoot arithmetic that
        /// keeps a 700 RPM rifle firing at 700 RPM rather than at whatever the
        /// player's monitor rounds it to. A `FireRate` attachment stat would move
        /// that schedule onto a runtime value the regression test does not touch,
        /// and rate of fire is one half of the time-to-kill this whole game is
        /// tuned around. A weapon that should fire faster is a new weapon.
        ///
        /// Checked by NAME rather than by count, so the test says what it means
        /// and does not fail merely because somebody added a legitimate stat.
        /// </summary>
        [Test]
        public void NoAttachmentStat_TouchesFireRate()
        {
            foreach (string name in System.Enum.GetNames(typeof(WeaponStat)))
            {
                string lowered = name.ToLowerInvariant();
                Assert.IsFalse(
                    lowered.Contains("firerate") || lowered.Contains("rpm") ||
                    lowered.Contains("cadence") || lowered.Contains("roundsper"),
                    $"WeaponStat.{name} looks like a fire-rate entry. Cadence is scheduled off the AUTHORED " +
                    "Config.SecondsPerShot and pinned by WeaponCadenceRegressionTests; moving it onto a runtime " +
                    "value takes one half of the game's time-to-kill outside its only regression gate.");
            }
        }

        /// <summary>
        /// `WeaponStat` and `WeaponStatExtensions.Count` agree.
        ///
        /// The count sizes two arrays that every effective value indexes. Add an
        /// enum member without moving it and the new stat writes past the end of
        /// both, which throws on the first attachment that uses it — at runtime,
        /// in a firefight, rather than here.
        /// </summary>
        [Test]
        public void TheWeaponStatSheet_IsSizedForEveryStat()
        {
            Assert.AreEqual(System.Enum.GetValues(typeof(WeaponStat)).Length, WeaponStatExtensions.Count,
                "WeaponStatExtensions.Count sizes WeaponStatSheet's arrays — it must match the enum exactly");

            // And every value indexes cleanly, which is the failure the count
            // exists to prevent, exercised rather than reasoned about.
            var sheet = new WeaponStatSheet();
            foreach (WeaponStat stat in System.Enum.GetValues(typeof(WeaponStat)))
            {
                sheet.AddMultiplier(stat, 1.5f);
                Assert.AreEqual(15f, sheet.Effective(stat, 10f), 0.0001f);
            }
        }

        /// <summary>
        /// Every attachment on disk is authored, distinct and does something.
        ///
        /// Scanned rather than listed, for the reason `WeaponDataTests` scans the
        /// weapons folder: a hardcoded list makes attachment number six a test
        /// edit, and an attachment nobody remembered to add is an attachment with
        /// no gate on it at all. A duplicated `stableId` is the specific failure —
        /// an arsenal is authored by copying the nearest thing to it.
        /// </summary>
        [Test]
        public void EveryShippedAttachment_IsAuthored()
        {
            string[] guids = AssetDatabase.FindAssets("t:AttachmentConfig", new[] { ATTACHMENT_FOLDER });
            if (guids.Length == 0) Assert.Ignore("no attachments on disk yet — run CoD -> Build Arsenal");

            var seen = new System.Collections.Generic.Dictionary<string, string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var attachment = AssetDatabase.LoadAssetAtPath<AttachmentConfig>(path);
                Assert.IsNotNull(attachment, $"unloadable attachment at {path}");

                Assert.IsFalse(string.IsNullOrWhiteSpace(attachment!.stableId),
                    $"{path} has no stableId — saves and unlocks reference attachments by that key");
                Assert.IsFalse(string.IsNullOrWhiteSpace(attachment.displayName),
                    $"{path} has no displayName — the player cannot identify it in a shop row");
                Assert.Greater(attachment.modifiers.Length, 0,
                    $"{attachment.displayName} changes nothing at all, which is indistinguishable from broken");

                Assert.IsFalse(seen.TryGetValue(attachment.stableId, out string? other),
                    $"'{attachment.stableId}' is on both {other} and {path} — a duplicated key aliases two " +
                    "attachments into one for every save that names it, and nothing at runtime reports it");
                seen[attachment.stableId] = path;

                foreach (AttachmentConfig.Modifier modifier in attachment.modifiers)
                {
                    if (modifier.kind != StatModifierKind.Multiplier) continue;
                    Assert.Greater(modifier.value, 0f,
                        $"{attachment.displayName} multiplies {modifier.stat} by {modifier.value} — a multiplier " +
                        "at or below zero collapses the value rather than reducing it");
                }
            }
        }
    }
}
