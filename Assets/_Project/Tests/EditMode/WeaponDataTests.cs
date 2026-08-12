#nullable enable
using System.Collections.Generic;
using CoD.Core;
using CoD.Waves;
using CoD.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoD.Tests
{
    /// <summary>
    /// The "new weapons are DATA, never new code" claim, checked against the
    /// actual shipped assets rather than against intent. If someone adds a weapon
    /// by adding a class, these tests keep passing and the claim quietly becomes
    /// false — so the second half of the check is that both weapons are the SAME
    /// type driving the same controller.
    ///
    /// The balance gate here USED to be a hardcoded array of two asset paths
    /// asserting a single universal 200-400 ms TTK window. It was wrong twice
    /// over: the list made weapon number three a test edit, and the window is not
    /// a universal law. A sniper and a launcher are one-shot BY DESIGN, report a
    /// TTK of zero, and are structurally incapable of passing a 200 ms floor —
    /// so the only way to make one "pass" is to author a 99-damage rifle that
    /// does not one-shot, which is worse than either honest answer and breaks
    /// anyway once DifficultyConfig.healthMultiplierByWave (3.5x) means nothing
    /// one-shots. The window is now the law for the classes it describes, and the
    /// one-shot classes are held to their re-engagement cost instead. The laws
    /// themselves live on WeaponConfig so this file and OnValidate cannot drift.
    ///
    /// That split then shipped with two holes of its own, both fixed 2026-08-12
    /// and both of the same shape — a gate that trusts a claim instead of checking
    /// it:
    ///
    /// - The one-shot exemption asserted the PRICE of one-shotting and never that
    ///   the weapon one-shots, so `weaponClass = Sniper` was a blanket exemption
    ///   from every TTK bound in the project. It is now earned by the asset.
    /// - AllWeapons preferred the registry over the folder scan, so the moment the
    ///   builder emits Weapons.asset a weapon in the folder but missing from the
    ///   list would drop out of every law here in silence. The two sources are now
    ///   asserted to AGREE rather than one being chosen over the other.
    ///
    /// Both new checks are watched failing by their own tests. A gate nobody has
    /// seen bite is a gate nobody knows is connected.
    ///
    /// The arsenal then grew from two weapons to six (ArsenalBuilder), which
    /// exposed four more gaps of the same family — a property nothing checked,
    /// because with two weapons it was true by luck:
    ///
    /// - The blank-id and aliased-id checks only ran when a registry EXISTED.
    ///   They are now unconditional, so a copy-pasted stableId fails on the scan.
    /// - `displayName` had no gate at all: a blank one is a shop row the player
    ///   cannot identify.
    /// - `maxSpread` below `baseSpread` clamps the first shot below the cone the
    ///   asset claims, and `reserveAmmo` below `magazineSize` is a gun that can
    ///   never finish a reload. Both are copy-a-weapon-and-narrow-one-number
    ///   mistakes; neither warns anywhere.
    /// - The starting weapon and every Weapon-kind shop offer hold DIRECT
    ///   references, so both work perfectly while pointing at a weapon no
    ///   `stableId` resolves — until a save round-trips it and gets null back.
    ///
    /// And one gap that is recorded here rather than closed, because closing it
    /// needs a field this file cannot add: a multi-pellet weapon has no way to
    /// declare a fixed pattern, so an AIMED shotgun fires every pellet down one
    /// ray. See EveryMultiPelletWeapon_ThrowsAConeRatherThanAPoint.
    /// </summary>
    public sealed class WeaponDataTests
    {
        /// <summary>Where ArsenalBuilder writes the registry. See AllWeapons for the absent case.</summary>
        private const string REGISTRY_PATH = "Assets/_Project/Data/Weapons/Weapons.asset";
        private const string WEAPON_FOLDER = "Assets/_Project/Data/Weapons";

        /// <summary>
        /// The shop's assets. Scanned rather than listed for the same reason the
        /// weapons folder is: a hardcoded list makes shop entry number twelve a
        /// test edit, and an offer nobody remembered to add is an offer with no
        /// gate on it at all.
        /// </summary>
        private const string SHOP_FOLDER = "Assets/_Project/Data/Shop";

        /// <summary>What the player starts a run holding. Its weapon has to be a weapon a save can name.</summary>
        private const string LOADOUT_PATH = "Assets/_Project/Data/Weapons/Loadout_Default.asset";

        private static WeaponConfig Load(string path)
        {
            WeaponConfig? config = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
            Assert.IsNotNull(config, $"missing weapon asset: {path}");
            return config!;
        }

        /// <summary>
        /// The whole arsenal — scanned from disk EVERY time, and cross-checked
        /// against the registry whenever one exists.
        ///
        /// This used to prefer the registry and fall back to the folder scan only
        /// when the registry was missing or empty, which quietly re-opened the
        /// exact hole the registry exists to close. The moment the builder writes
        /// Weapons.asset, weapon number three added to the folder but FORGOTTEN in
        /// the registry drops straight out of EveryWeapon_ObeysTheLawOfItsClass —
        /// and TheArsenalGate_ActuallyFindsTheShippedWeapons cannot see it either,
        /// because `Length >= 2` and the two known stableIds all stay true. That
        /// is "weapon seven quietly escapes the balance gate", which is the
        /// failure WeaponRegistry's own header cites as its reason to exist.
        ///
        /// So neither source is trusted over the other. The SCAN is the coverage —
        /// a weapon is inside the gate the moment its asset exists, whether or not
        /// anyone remembered to list it. The REGISTRY is the ordering and the
        /// save-key lookup. And the two are asserted to describe the same arsenal,
        /// so a weapon can only escape the balance laws by not existing.
        /// </summary>
        private static WeaponConfig[] AllWeapons()
        {
            WeaponConfig[] onDisk = ScanWeaponFolder();

            // UNCONDITIONAL, and it did not used to be. The blank-id and
            // aliased-id checks lived inside the registry cross-check, which only
            // runs when a registry exists — so before ArsenalBuilder emits
            // Weapons.asset, two weapons sharing one stableId were caught by
            // nothing at all. That is not a hypothetical: an arsenal is authored
            // by copying the nearest weapon, and a copied stableId is the single
            // easiest mistake to make. Two weapons that are one weapon for every
            // save that names either is worth failing on whether or not anyone
            // has run the builder yet.
            AssertNoAliasedIdsOnDisk(onDisk);

            WeaponRegistry? registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(REGISTRY_PATH);
            // No registry yet: the scan alone is the gate — a graceful absence,
            // never a PREFERENCE for the registry over the scan.
            if (registry == null) return onDisk;

            AssertRegistryAndFolderDescribeTheSameArsenal(registry, onDisk);

            // Registry order, because order is presentation and the registry owns
            // it. Anything the scan found that the registry does not list is
            // appended rather than dropped: coverage must never depend on the
            // assertion above having been reached.
            var arsenal = new List<WeaponConfig>(onDisk.Length + registry.allWeapons.Length);
            foreach (WeaponConfig listed in registry.allWeapons)
            {
                if (listed != null) arsenal.Add(listed);
            }
            foreach (WeaponConfig found in onDisk)
            {
                if (!arsenal.Contains(found)) arsenal.Add(found);
            }
            return arsenal.ToArray();
        }

        /// <summary>
        /// Every WeaponConfig asset that actually exists. This is the coverage
        /// half: an asset on disk is inside every balance law below the moment it
        /// is created, with nothing to remember and nothing to opt into.
        /// </summary>
        private static WeaponConfig[] ScanWeaponFolder()
        {
            string[] guids = AssetDatabase.FindAssets("t:WeaponConfig", new[] { WEAPON_FOLDER });
            var found = new List<WeaponConfig>(guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                WeaponConfig? config = AssetDatabase.LoadAssetAtPath<WeaponConfig>(path);
                // Skipping an unloadable match silently is how a corrupt asset
                // leaves the gate: it is a weapon in the folder that no law sees.
                Assert.IsTrue(config != null, $"{path} matched t:WeaponConfig but would not load");
                found.Add(config!);
            }
            return found.ToArray();
        }

        /// <summary>
        /// The registry and the weapons folder must describe the SAME arsenal, in
        /// both directions, because each direction breaks the game silently.
        ///
        /// On disk but not in the registry: that weapon is outside every balance
        /// law in this file the moment the registry becomes the source of truth,
        /// and nothing at runtime reports it. In the registry but not on disk: a
        /// save key that resolves to nothing, while the list still looks the right
        /// length — the same shape of failure as a null slot.
        ///
        /// Written as a static helper rather than inline so a test can prove it
        /// bites (see TheRegistryAndTheFolder_MustDescribeTheSameArsenal): a
        /// cross-check nobody has watched fail is not a cross-check.
        /// </summary>
        private static void AssertRegistryAndFolderDescribeTheSameArsenal(
            WeaponRegistry registry, WeaponConfig[] onDisk)
        {
            Dictionary<string, WeaponConfig> byId = AssertNoAliasedIdsOnDisk(onDisk);

            var listedIds = new HashSet<string>();
            for (int i = 0; i < registry.allWeapons.Length; i++)
            {
                WeaponConfig? listed = registry.allWeapons[i];
                Assert.IsTrue(listed != null,
                    $"{REGISTRY_PATH} entry {i} is empty — a slot pointing at an asset that no longer exists " +
                    "still counts toward Length, so the list looks complete while a weapon has vanished from it");
                Assert.IsTrue(byId.ContainsKey(listed!.stableId),
                    $"{REGISTRY_PATH} lists '{listed.stableId}' ({listed.name}) but no asset with that stableId " +
                    $"is in {WEAPON_FOLDER} — the registry names a weapon the arsenal does not contain, and every " +
                    "save that resolves that key gets nothing back");
                listedIds.Add(listed.stableId);
            }

            foreach (KeyValuePair<string, WeaponConfig> entry in byId)
            {
                Assert.IsTrue(listedIds.Contains(entry.Key),
                    $"{entry.Value.name} ('{entry.Key}') is in {WEAPON_FOLDER} but missing from {REGISTRY_PATH}. " +
                    "The moment the registry is the source of truth that asset sits outside every balance law in " +
                    "this file, with nothing at runtime to report it — the exact failure the registry exists to " +
                    "prevent. Add it to the registry, or delete the asset.");
            }
        }

        /// <summary>
        /// Every weapon on disk has an id, and no two share one. Returns the
        /// arsenal keyed by that id, because the caller that needs the check also
        /// needs the map.
        ///
        /// EXTRACTED SO IT CAN RUN WITHOUT A REGISTRY. These two assertions used
        /// to be the opening of the registry cross-check, which meant they only
        /// ever fired when Weapons.asset existed — and it did not exist at all
        /// until ArsenalBuilder shipped. Two assets carrying one stableId is the
        /// easiest mistake in the whole authoring loop (an arsenal is written by
        /// copying the nearest weapon), it makes two weapons one weapon for every
        /// save that names either, and nothing at runtime reports it. It is not a
        /// failure that should wait for a registry to be built first.
        ///
        /// It is also load-bearing for the check that follows it: two assets
        /// sharing an id collapse to one dictionary entry, which would make the
        /// set comparison agree about an arsenal that does not exist.
        /// </summary>
        private static Dictionary<string, WeaponConfig> AssertNoAliasedIdsOnDisk(WeaponConfig[] onDisk)
        {
            var byId = new Dictionary<string, WeaponConfig>();
            foreach (WeaponConfig config in onDisk)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(config.stableId),
                    $"{config.name} in {WEAPON_FOLDER} has no stableId — it can match no registry entry, " +
                    "and a save references weapons by that key rather than by asset name");

                if (byId.TryGetValue(config.stableId, out WeaponConfig first))
                {
                    Assert.Fail(
                        $"'{config.stableId}' is on both {first.name} and {config.name} in {WEAPON_FOLDER} — " +
                        "two weapons are one weapon for every save that names it, and the registry can only list one");
                }
                byId.Add(config.stableId, config);
            }
            return byId;
        }

        [Test]
        public void BothWeapons_AreTheSameTypeOfThing()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");
            WeaponConfig smg = Load("Assets/_Project/Data/Weapons/SMG_Rapid.asset");

            // Same class, different numbers. That is the entire arsenal design.
            Assert.AreEqual(rifle.GetType(), smg.GetType());
            Assert.AreNotEqual(rifle.stableId, smg.stableId);
        }

        /// <summary>
        /// A gate that enumerates itself can also enumerate NOTHING and stay
        /// green, which is a worse failure than the hardcoded list it replaced —
        /// the list at least named what it was checking. So the enumeration is
        /// itself asserted against the weapons we know ship.
        /// </summary>
        [Test]
        public void TheArsenalGate_ActuallyFindsTheShippedWeapons()
        {
            WeaponConfig[] arsenal = AllWeapons();
            Assert.GreaterOrEqual(arsenal.Length, 2,
                "the balance gate found fewer than the two shipped weapons — it is checking nothing");

            bool foundRifle = false;
            bool foundSmg = false;
            foreach (WeaponConfig config in arsenal)
            {
                Assert.IsNotNull(config, "a null entry in the arsenal — that weapon is outside every gate below");
                if (config.stableId == "wpn_ar_standard") foundRifle = true;
                if (config.stableId == "wpn_smg_rapid") foundSmg = true;
            }

            Assert.IsTrue(foundRifle, "AR_Standard is not in the enumerated arsenal");
            Assert.IsTrue(foundSmg, "SMG_Rapid is not in the enumerated arsenal");
        }

        /// <summary>
        /// The aliased-id check, watched failing WITHOUT a registry.
        ///
        /// It used to be the opening of AssertRegistryAndFolderDescribeTheSameArsenal
        /// and therefore ran only when Weapons.asset existed — which, until
        /// ArsenalBuilder, was never. AllWeapons now calls it on every scan, so
        /// this is the test that proves the unconditional path is connected rather
        /// than merely written.
        /// </summary>
        [Test]
        public void TheFolderScan_RejectsAnAliasedId_EvenWithNoRegistry()
        {
            WeaponConfig first = ScriptableObject.CreateInstance<WeaponConfig>();
            first.stableId = "wpn_alpha";
            WeaponConfig second = ScriptableObject.CreateInstance<WeaponConfig>();
            second.stableId = "wpn_beta";

            Assert.DoesNotThrow(() => AssertNoAliasedIdsOnDisk(new[] { first, second }),
                "two weapons with two ids must pass, or the check is simply always red");

            // The copy-paste failure: an arsenal is authored by copying the
            // nearest weapon, and the id is the field people forget.
            WeaponConfig twin = ScriptableObject.CreateInstance<WeaponConfig>();
            twin.stableId = "wpn_alpha";
            Assert.Throws<AssertionException>(
                () => AssertNoAliasedIdsOnDisk(new[] { first, twin }),
                "two assets sharing one stableId escaped the scan — they are one weapon for every save that names either");

            WeaponConfig nameless = ScriptableObject.CreateInstance<WeaponConfig>();
            nameless.stableId = "   ";
            Assert.Throws<AssertionException>(
                () => AssertNoAliasedIdsOnDisk(new[] { nameless }),
                "a weapon with no stableId escaped the scan — no save and no registry entry can ever name it");
        }

        /// <summary>
        /// Every weapon can be named to a player and named to a save.
        ///
        /// `displayName` had no gate on it anywhere: it is the shop row, the HUD
        /// label and the loadout line, and a blank one ships as an empty button
        /// the player cannot identify. The failure is silent in exactly the way
        /// the blank `stableId` is — nothing throws, the arsenal is the right
        /// length, and the gun simply has no name.
        /// </summary>
        [Test]
        public void EveryWeapon_IsNameableInAShopRowAndInASave()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(config.stableId),
                    $"{config.name} has no stableId — a save references weapons by that key, not by asset name");
                Assert.IsFalse(string.IsNullOrWhiteSpace(config.displayName),
                    $"{config.name} has no displayName — it is the shop row, the HUD label and the loadout line, " +
                    "so a blank one ships as a button the player cannot identify");
            }
        }

        /// <summary>
        /// Bloom can never be authored below its own floor.
        ///
        /// `WeaponRuntime` starts `CurrentSpread` at `baseSpread`, grows it by
        /// `spreadPerShot` clamped to `maxSpread`, and decays it back down to
        /// `baseSpread`. Author `maxSpread` under `baseSpread` and the clamp fires
        /// on the very first shot: the gun is permanently TIGHTER than the cone
        /// its own `baseSpread` claims, and the authored number is a lie that no
        /// warning anywhere reports. It is the kind of mistake that only appears
        /// when a weapon is written by copying another and then narrowing one of
        /// the two numbers.
        /// </summary>
        [Test]
        public void EveryWeapon_BloomCanNeverShrinkBelowItsOwnBaseline()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                Assert.GreaterOrEqual(config.maxSpread, config.baseSpread,
                    $"{config.displayName} caps bloom at {config.maxSpread:F2}° but starts it at {config.baseSpread:F2}° — " +
                    "the clamp fires on the first shot, so the authored baseSpread never happens");
                Assert.GreaterOrEqual(config.spreadPerShot, 0f,
                    $"{config.displayName} has negative spreadPerShot — firing would TIGHTEN the cone, " +
                    "which is the opposite of every other gun in the game");
            }
        }

        /// <summary>
        /// A multi-pellet weapon must throw a cone rather than a point — and the
        /// only cone this data model owns is the hipfire one.
        ///
        /// THIS TEST IS ALSO THE RECORD OF WHAT THE MODEL CANNOT SAY. A shotgun
        /// pattern is GEOMETRY: a fixed cone, identical on the first pull and the
        /// fiftieth, hip or aimed. Bloom is not that — it starts at `baseSpread`,
        /// grows as you fire, and `WeaponController.CurrentSpreadDegrees` returns
        /// exactly ZERO while aiming, by a deliberate design rule ("a random cone
        /// while aiming reads as the game cheating"). `FireOneShot` then casts
        /// every pellet through that one number, so an AIMED twelve-pellet weapon
        /// puts all twelve on one point: 120 damage in a single ray at any range
        /// inside the falloff, which is a sniper wearing a shotgun's name.
        ///
        /// So the strongest law authorable against today's fields is this one:
        /// hipfire must at least be a cone. Closing the rest needs
        /// `pelletSpreadDegrees` on `WeaponConfig` and a `CastOneRay` that uses
        /// `max(pattern, bloom)` instead of bloom alone — a field and a line, both
        /// outside the file list this arsenal was authored under. When they land,
        /// this test should grow the assertion that the pattern is non-zero
        /// regardless of aim, and the ContactBurst law should be read alongside
        /// it: `ShotsToKillAtRange` charges for DISTANCE and knows nothing about
        /// whether the pellets that carried the damage landed on one body.
        /// </summary>
        [Test]
        public void EveryMultiPelletWeapon_ThrowsAConeRatherThanAPoint()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                if (config.pelletsPerShot <= 1) continue;

                Assert.Greater(config.baseSpread, 0f,
                    $"{config.displayName} throws {config.pelletsPerShot} pellets through a zero-degree cone — " +
                    "every pellet is the same ray, so it is a single bullet dealing " +
                    $"{config.DamagePerShot:F0} damage rather than a spread");
                Assert.Greater(config.maxSpread, 0f,
                    $"{config.displayName} caps its cone at zero, which cancels the baseSpread above");
            }
        }

        /// <summary>
        /// Every weapon can complete one reload out of its own reserve, and
        /// running dry is never the fast way to reload.
        ///
        /// `reserveAmmo` below `magazineSize` is a weapon that can never refill a
        /// magazine, and it poisons the shop besides: `RefillReserve` hands back a
        /// FRACTION of the config reserve, so Resupply on such a gun sells the
        /// player a handful of rounds. `reloadEmptyTime` below `reloadTime` is
        /// worse than a wasted number — it makes emptying the magazine the optimal
        /// play, which inverts the one habit the reload timings exist to teach.
        /// `OnValidate` normalises the second of these, but a builder that writes
        /// an asset in batch mode is not an Inspector edit, so the gate belongs
        /// here too.
        /// </summary>
        [Test]
        public void EveryWeapon_CanCompleteOneReloadFromItsOwnReserve()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                Assert.GreaterOrEqual(config.reserveAmmo, config.magazineSize,
                    $"{config.displayName} carries {config.reserveAmmo} spare rounds for a {config.magazineSize}-round " +
                    "magazine — it cannot complete a single reload, and Resupply sells a fraction of that");
                Assert.GreaterOrEqual(config.reloadEmptyTime, config.reloadTime,
                    $"{config.displayName} reloads FASTER from empty ({config.reloadEmptyTime:F2}s) than with a round " +
                    $"chambered ({config.reloadTime:F2}s) — running dry becomes the optimal play");
            }
        }

        /// <summary>
        /// A weapon that says it fires a projectile has one to fire, and the
        /// prefab it points at is actually a projectile.
        ///
        /// THIS IS THE ONE FAILURE ON THE FIRING PATH WITH NO SYMPTOM. Every other
        /// way a weapon can be mis-authored shows itself: a missing muzzle flash
        /// is a dark gun, a missing clip is a silent one, a bad falloff is a
        /// weapon that will not kill. A launcher with a null `projectilePrefab`
        /// consumes a round, kicks the camera, lights the muzzle, plays both fire
        /// layers, starts its cadence — and puts nothing whatsoever in the air. It
        /// looks exactly like a working gun aimed at nothing.
        ///
        /// The prefab is loaded and its COMPONENT checked, not just its
        /// non-nullness, because the pool would otherwise hand out an instance
        /// that never moves and never returns itself: one leaked instance per
        /// trigger pull for the rest of the run.
        /// </summary>
        [Test]
        public void EveryProjectileWeapon_HasARoundToFire()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                if (config.delivery != DeliveryMode.Projectile) continue;

                Assert.IsNotNull(config.projectilePrefab,
                    $"{config.displayName} delivers by projectile and has no projectilePrefab — it fires, kicks, " +
                    "flashes and produces no round at all, and nothing on the firing path can notice");

                Assert.IsNotNull(config.projectilePrefab!.GetComponent<Projectile>(),
                    $"{config.displayName}'s round '{config.projectilePrefab.name}' carries no Projectile " +
                    "component, so it would never move and never return itself to the pool");
                Assert.IsNotNull(config.projectilePrefab.GetComponent<PooledObject>(),
                    $"{config.displayName}'s round '{config.projectilePrefab.name}' carries no PooledObject — " +
                    "everything that spawns in this game goes through the pool");

                Assert.Greater(config.projectileSpeed, 0f,
                    $"{config.displayName}'s round has no speed and would hang at the muzzle");
            }
        }

        /// <summary>
        /// A round can reach the range its own weapon claims.
        ///
        /// `maxRange` is the weapon's stated reach and `projectileSpeed x
        /// projectileLifetime` is the reach it actually has. When the second is
        /// smaller the config lies, and it lies quietly: the round simply
        /// evaporates in mid-air at the distance nobody wrote down, which reads as
        /// a shot that was never fired rather than as a range limit.
        ///
        /// A hitscan weapon is exempt because `maxRange` IS its reach — the cast
        /// is bounded by it directly.
        /// </summary>
        [Test]
        public void EveryProjectileWeapon_CanReachItsOwnStatedRange()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                if (config.delivery != DeliveryMode.Projectile) continue;

                float reach = config.projectileSpeed * config.projectileLifetime;
                Assert.GreaterOrEqual(reach, config.maxRange,
                    $"{config.displayName} claims {config.maxRange:F0} m of range but its round only flies " +
                    $"{reach:F0} m before it expires ({config.projectileSpeed:F0} m/s for " +
                    $"{config.projectileLifetime:F1} s) — past that the shot vanishes with no impact and no sound");
            }
        }

        /// <summary>
        /// A HITSCAN weapon carries no projectile prefab.
        ///
        /// Not tidiness. `projectilePrefab` is the field
        /// <c>WeaponConfig.OnValidate</c> reads to decide whether a projectile
        /// weapon is finished being authored, so a rifle carrying one for no
        /// reason is how that warning becomes unreachable for the weapon that
        /// needs it — and a builder that stamps the round onto every weapon on
        /// disk is exactly the shortcut somebody reaches for. VfxBuilder assigns
        /// it only where `delivery` asks for it; this is what keeps that true.
        /// </summary>
        [Test]
        public void AHitscanWeapon_CarriesNoProjectilePrefab()
        {
            foreach (WeaponConfig config in AllWeapons())
            {
                if (config.delivery == DeliveryMode.Projectile) continue;
                // Checked and returned rather than asserted with a message, because
                // NUnit builds the message BEFORE it evaluates the condition — and
                // reading `.name` off the unassigned reference this test exists to
                // find throws before the assertion it belongs to can fail.
                if (config.projectilePrefab == null) continue;

                Assert.Fail(
                    $"{config.displayName} resolves as hitscan but carries '{config.projectilePrefab.name}' as a " +
                    "round it will never fire — and while it does, OnValidate can never warn about a real " +
                    "launcher that is missing one");
            }
        }

        /// <summary>
        /// The weapon the player starts holding is a weapon the arsenal contains.
        ///
        /// `PlayerLoadoutConfig.startingWeapon` is a direct object reference, so
        /// it works perfectly while pointing at an asset that no registry lists —
        /// right up until a save round-trips it by `stableId` and gets nothing
        /// back. That is the same class of failure as a registry entry with no
        /// asset behind it, arriving from the other end, and nothing else in the
        /// project checks it.
        /// </summary>
        [Test]
        public void TheStartingWeapon_IsPartOfTheArsenal()
        {
            PlayerLoadoutConfig? loadout = AssetDatabase.LoadAssetAtPath<PlayerLoadoutConfig>(LOADOUT_PATH);
            Assert.IsNotNull(loadout, $"missing {LOADOUT_PATH} — run CoD -> Build Grey Box");

            WeaponConfig? starting = loadout!.startingWeapon;
            Assert.IsNotNull(starting,
                $"{LOADOUT_PATH} has no starting weapon — a run would begin holding nothing at all");

            Assert.IsTrue(ArsenalIds().Contains(starting!.stableId),
                $"the starting weapon '{starting.stableId}' ({starting.name}) is not in the arsenal enumerated by " +
                "this file. A direct reference works until a save round-trips it by id and resolves to nothing.");
        }

        /// <summary>
        /// Every weapon the shop sells is a weapon the arsenal contains.
        ///
        /// A `ShopItemConfig` of kind Weapon holds a direct reference too, so the
        /// shop happily sells a gun that no `stableId` resolves: the player buys
        /// it, carries it for the rest of the run, and the next save writes a key
        /// that comes back null. The arsenal is about to grow by four, and each
        /// one needs a shop row — this is the gate that catches a row added
        /// without its weapon reaching the registry.
        /// </summary>
        [Test]
        public void EveryWeaponTheShopSells_IsPartOfTheArsenal()
        {
            Assert.IsTrue(AssetDatabase.IsValidFolder(SHOP_FOLDER),
                $"{SHOP_FOLDER} is missing — run CoD -> Build Grey Box");

            HashSet<string> arsenal = ArsenalIds();
            string[] guids = AssetDatabase.FindAssets("t:ShopItemConfig", new[] { SHOP_FOLDER });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ShopItemConfig? item = AssetDatabase.LoadAssetAtPath<ShopItemConfig>(path);
                Assert.IsTrue(item != null, $"{path} matched t:ShopItemConfig but would not load");
                if (item!.kind != ShopItemKind.Weapon) continue;

                Assert.IsNotNull(item.weapon,
                    $"{item.name} is a Weapon-kind offer with no weapon behind it — the player pays and receives nothing");
                Assert.IsTrue(arsenal.Contains(item.weapon!.stableId),
                    $"{item.name} sells '{item.weapon.stableId}' ({item.weapon.name}), which is not in the arsenal " +
                    "enumerated by this file — the purchase survives the run and dies at the next save");
            }
        }

        /// <summary>Every stableId the arsenal answers to. Built per call; this is a test, not a frame.</summary>
        private static HashSet<string> ArsenalIds()
        {
            WeaponConfig[] arsenal = AllWeapons();
            var ids = new HashSet<string>();
            foreach (WeaponConfig config in arsenal) ids.Add(config.stableId);
            return ids;
        }

        /// <summary>
        /// The balance law, split by class. Every weapon answers to exactly one,
        /// and no class is exempt — WeaponConfig.LawFor defaults an unlisted class
        /// to the arcade window, so a new class cannot escape by being new.
        /// </summary>
        [Test]
        public void EveryWeapon_ObeysTheLawOfItsClass()
        {
            foreach (WeaponConfig config in AllWeapons()) AssertObeysTheLawOfItsClass(config);
        }

        /// <summary>
        /// One weapon against its own law. Split out of the loop above so that
        /// TheOneShotExemption_IsEarnedByTheAsset_NotGrantedByTheEnum can watch it
        /// FAIL on a mis-authored asset — a gate nobody has seen bite is a gate
        /// nobody knows is connected.
        /// </summary>
        private static void AssertObeysTheLawOfItsClass(WeaponConfig config)
        {
            string who = $"{config.displayName} ({config.weaponClass})";

            switch (config.Law)
            {
                case BalanceLaw.ArcadeTtkWindow:
                {
                    // 200-400 ms is the defining choice of the whole game. A
                    // weapon outside it is not a variant, it is a different game.
                    float ttk = config.TimeToKill() * 1000f;
                    Assert.GreaterOrEqual(ttk, WeaponConfig.ARCADE_TTK_MIN_MS,
                        $"{who} kills too fast ({ttk:F0} ms)");
                    Assert.LessOrEqual(ttk, WeaponConfig.ARCADE_TTK_MAX_MS,
                        $"{who} kills too slowly ({ttk:F0} ms)");
                    break;
                }

                case BalanceLaw.ContactBurst:
                {
                    // A shotgun is the GAP between contact and ten metres. Both
                    // halves are load-bearing: one pull everywhere is a sniper
                    // without a scope, two pulls at contact is a bad rifle.
                    Assert.LessOrEqual(config.ShotsToKill(), 1,
                        $"{who} does not one-pull at contact ({config.DamagePerShot:F0} damage per pull)");
                    Assert.AreEqual(0f, config.TimeToKillAtRange(100f, 0f), 0.0001f,
                        $"{who} needs a second pull at contact");
                    Assert.GreaterOrEqual(
                        config.ShotsToKillAtRange(100f, WeaponConfig.SHOTGUN_TWO_PULL_METRES), 2,
                        $"{who} still one-pulls at {WeaponConfig.SHOTGUN_TWO_PULL_METRES:F0} m — it is the best rifle in the game");
                    Assert.Greater(
                        config.TimeToKillAtRange(100f, WeaponConfig.SHOTGUN_TWO_PULL_METRES), 0f,
                        $"{who} costs no time at all at {WeaponConfig.SHOTGUN_TWO_PULL_METRES:F0} m");
                    break;
                }

                case BalanceLaw.ReEngagementCost:
                {
                    // THE PREMISE FIRST. This law exempts the weapon from the
                    // 200-400 ms window on the stated grounds that it one-shots,
                    // and until this assertion existed nothing anywhere checked
                    // that it does. `weaponClass = Sniper` was therefore a blanket
                    // exemption from every TTK bound in the project: 25 damage at
                    // 60 RPM is four pulls and three full seconds against a 100 HP
                    // drone, and it passed both floors below in silence. The
                    // ContactBurst case above has always asserted its own premise;
                    // this one merely forgot to.
                    Assert.AreEqual(1, config.ShotsToKill(),
                        $"{who} needs {config.ShotsToKill()} pulls to kill ({config.DamagePerShot:F0} damage per pull, " +
                        $"{config.TimeToKill() * 1000f:F0} ms) — it is exempt from the " +
                        $"{WeaponConfig.ARCADE_TTK_MIN_MS:F0}-{WeaponConfig.ARCADE_TTK_MAX_MS:F0} ms window on a one-shot " +
                        "premise it does not meet, so it answers to no time-to-kill bound at all");

                    // One shot, one kill. TTK is not the axis; the cost of
                    // lining up the NEXT shot is the only thing that stops
                    // this being strictly better than the rifle everywhere.
                    Assert.GreaterOrEqual(config.adsTime, WeaponConfig.ONE_SHOT_MIN_ADS_SECONDS,
                        $"{who} aims in {config.adsTime:F2}s — a one-shot weapon that snaps to target has no downside");
                    Assert.GreaterOrEqual(config.SecondsPerShot, WeaponConfig.ONE_SHOT_MIN_CYCLE_SECONDS,
                        $"{who} cycles in {config.SecondsPerShot:F2}s — that is an assault rifle that one-shots");
                    break;
                }

                default:
                    Assert.Fail($"{who} answers to no balance law at all");
                    break;
            }
        }

        /// <summary>
        /// The registry is ArsenalBuilder's asset and may not be on disk in a
        /// fresh clone. When it is, these are the two ways it silently breaks the
        /// game: a null entry
        /// drops a weapon out of every gate above while the list still looks the
        /// right length, and a duplicate stableId aliases two weapons into one for
        /// every save that names either.
        /// </summary>
        [Test]
        public void TheRegistry_IfItExists_HasNoHolesAndNoAliasedIds()
        {
            WeaponRegistry? registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(REGISTRY_PATH);
            if (registry == null) Assert.Ignore($"no registry asset at {REGISTRY_PATH} yet — the folder scan is the gate");

            var seen = new HashSet<string>();
            foreach (WeaponConfig config in registry!.allWeapons)
            {
                Assert.IsNotNull(config, "the registry has an empty slot");
                Assert.IsFalse(string.IsNullOrWhiteSpace(config.stableId),
                    $"{config.name} has no stableId — saves reference weapons by that key, not by asset name");
                Assert.IsTrue(seen.Add(config.stableId),
                    $"'{config.stableId}' appears twice — two weapons are now one weapon for every save");
                Assert.AreSame(config, registry.ByStableId(config.stableId),
                    $"ByStableId did not return {config.name} for its own id");
            }

            Assert.AreEqual(registry.allWeapons.Length, registry.Count);
        }

        /// <summary>
        /// The cross-check itself, watched failing.
        ///
        /// AllWeapons used to PREFER the registry over the folder scan, so weapon
        /// number three added to Assets/_Project/Data/Weapons and forgotten in
        /// Weapons.asset would have dropped out of every balance law above without
        /// a single test going red — TheArsenalGate_ActuallyFindsTheShippedWeapons
        /// keeps passing, because `Length >= 2` and both known stableIds are still
        /// there. The registry may not be on disk at all, so the only way to know
        /// the replacement cross-check is connected is to hand it a disagreement
        /// and watch it throw.
        /// </summary>
        [Test]
        public void TheRegistryAndTheFolder_MustDescribeTheSameArsenal()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");
            WeaponConfig smg = Load("Assets/_Project/Data/Weapons/SMG_Rapid.asset");
            WeaponConfig[] folder = { rifle, smg };

            var complete = ScriptableObject.CreateInstance<WeaponRegistry>();
            complete.allWeapons = new[] { rifle, smg };
            Assert.DoesNotThrow(() => AssertRegistryAndFolderDescribeTheSameArsenal(complete, folder),
                "an agreeing registry and folder must pass, or the check is simply always red");

            // The failure the registry exists to prevent: an asset on disk that
            // nobody added to the list, and therefore outside every law above.
            var forgotten = ScriptableObject.CreateInstance<WeaponRegistry>();
            forgotten.allWeapons = new[] { rifle };
            Assert.Throws<AssertionException>(
                () => AssertRegistryAndFolderDescribeTheSameArsenal(forgotten, folder),
                "a weapon in the folder but missing from the registry escaped the cross-check");

            // And the reverse: the registry names a weapon the arsenal no longer
            // contains, so every save resolving that key gets nothing back.
            var stale = ScriptableObject.CreateInstance<WeaponRegistry>();
            stale.allWeapons = new[] { rifle, smg };
            Assert.Throws<AssertionException>(
                () => AssertRegistryAndFolderDescribeTheSameArsenal(stale, new[] { rifle }),
                "a registry entry with no asset behind it escaped the cross-check");

            // A hole where a deleted asset used to be still counts toward Length.
            var holed = ScriptableObject.CreateInstance<WeaponRegistry>();
            // Explicit element type: `new[] { rifle, null! }` infers a nullable
            // array and assigning it to WeaponConfig[] is a CS8619 warning, and
            // this project's gate is zero warnings, not zero errors.
            holed.allWeapons = new WeaponConfig[] { rifle, null! };
            Assert.Throws<AssertionException>(
                () => AssertRegistryAndFolderDescribeTheSameArsenal(holed, new[] { rifle }),
                "an empty registry slot escaped the cross-check");
        }

        /// <summary>
        /// The one-shot exemption is EARNED BY THE ASSET, not granted by the enum.
        ///
        /// ReEngagementCost exempts a weapon from the 200-400 ms window on the
        /// stated premise that it one-shots — and nothing checked the premise. So
        /// `weaponClass = Sniper` was a blanket exemption from every TTK bound in
        /// the project: the impostor below takes four pulls and three full seconds
        /// to kill a 100 HP drone, clears both re-engagement floors, and used to
        /// pass the gate in silence. That is the "99-damage sniper that does not
        /// one-shot" the split was written to prevent, arriving through the door
        /// the split opened.
        /// </summary>
        [Test]
        public void TheOneShotExemption_IsEarnedByTheAsset_NotGrantedByTheEnum()
        {
            WeaponConfig impostor = ScriptableObject.CreateInstance<WeaponConfig>();
            impostor.weaponClass = WeaponClass.Sniper;
            impostor.bodyDamage = 25f;
            impostor.roundsPerMinute = 60f;   // 1.00 s between rounds
            impostor.adsTime = 0.40f;

            // Everything the old gate looked at, and it passes all of it.
            Assert.AreEqual(BalanceLaw.ReEngagementCost, impostor.Law);
            Assert.GreaterOrEqual(impostor.adsTime, WeaponConfig.ONE_SHOT_MIN_ADS_SECONDS);
            Assert.GreaterOrEqual(impostor.SecondsPerShot, WeaponConfig.ONE_SHOT_MIN_CYCLE_SECONDS);

            // What nobody was looking at: it is not a one-shot weapon at all, and
            // 3000 ms is seven times the slowest TTK the game permits anywhere.
            Assert.AreEqual(4, impostor.ShotsToKill(100f));
            Assert.AreEqual(3.0f, impostor.TimeToKill(100f), 0.002f);
            Assert.Greater(impostor.TimeToKill(100f) * 1000f, WeaponConfig.ARCADE_TTK_MAX_MS);

            Assert.Throws<AssertionException>(
                () => AssertObeysTheLawOfItsClass(impostor),
                "weaponClass = Sniper is still a blanket exemption from every TTK bound in the project");

            // The positive control, so the assertion above cannot be passing
            // because the gate rejects every sniper ever authored: 100 damage in
            // one pull, and the same two floors, and it goes through.
            WeaponConfig honest = ScriptableObject.CreateInstance<WeaponConfig>();
            honest.weaponClass = WeaponClass.Sniper;
            honest.bodyDamage = 100f;
            honest.roundsPerMinute = 60f;
            honest.adsTime = 0.40f;

            Assert.AreEqual(1, honest.ShotsToKill(100f));
            Assert.DoesNotThrow(() => AssertObeysTheLawOfItsClass(honest),
                "a sniper that genuinely one-shots must still clear its own law");
        }

        // ---------- the model itself ----------
        //
        // Synthetic configs, not shipped assets: the point is the arithmetic, and
        // authoring a real sniper to prove the sniper law would be exactly the
        // backwards move this whole change exists to stop.

        /// <summary>
        /// The failure that forced the split. A one-shot weapon reports a TTK of
        /// zero, so under a universal 200 ms floor it is not merely unbalanced —
        /// it cannot be authored at all.
        /// </summary>
        [Test]
        public void AOneShotWeapon_ReportsOneShot_AndIsJudgedOnReEngagementInstead()
        {
            WeaponConfig sniper = ScriptableObject.CreateInstance<WeaponConfig>();
            sniper.weaponClass = WeaponClass.Sniper;
            sniper.bodyDamage = 100f;
            sniper.roundsPerMinute = 60f;   // 1.00 s between rounds
            sniper.adsTime = 0.40f;

            // One shot, not zero. The old model floored nothing and a one-shot
            // weapon's "shots to kill" was still 1, but its TTK was 0 — and 0 is
            // below every floor, forever.
            Assert.AreEqual(1, sniper.ShotsToKill(100f));
            Assert.AreEqual(0f, sniper.TimeToKill(100f), 0.0001f);
            Assert.Less(sniper.TimeToKill(100f) * 1000f, WeaponConfig.ARCADE_TTK_MIN_MS,
                "if this ever passes the arcade floor, the split is no longer needed");

            // So it answers to a different law, and it passes that one.
            Assert.AreEqual(BalanceLaw.ReEngagementCost, sniper.Law);
            Assert.AreEqual(BalanceLaw.ReEngagementCost, WeaponConfig.LawFor(WeaponClass.Launcher));
            Assert.GreaterOrEqual(sniper.adsTime, WeaponConfig.ONE_SHOT_MIN_ADS_SECONDS);
            Assert.GreaterOrEqual(sniper.SecondsPerShot, WeaponConfig.ONE_SHOT_MIN_CYCLE_SECONDS);

            // And that law is worth more than the window it replaces: 1.4 s to
            // re-engage against the AR's 0.257 s is the actual trade being sold.
            Assert.Greater(sniper.adsTime + sniper.SecondsPerShot, 1.0f);
        }

        /// <summary>The core automatics keep the window, unchanged. This is the control.</summary>
        [Test]
        public void TheCoreAutomatics_StillAnswerToTheArcadeWindow()
        {
            Assert.AreEqual(BalanceLaw.ArcadeTtkWindow, WeaponConfig.LawFor(WeaponClass.AssaultRifle));
            Assert.AreEqual(BalanceLaw.ArcadeTtkWindow, WeaponConfig.LawFor(WeaponClass.SMG));
            Assert.AreEqual(BalanceLaw.ArcadeTtkWindow, WeaponConfig.LawFor(WeaponClass.LMG));
            Assert.AreEqual(BalanceLaw.ArcadeTtkWindow, WeaponConfig.LawFor(WeaponClass.Pistol));
            Assert.AreEqual(BalanceLaw.ArcadeTtkWindow, WeaponConfig.LawFor(WeaponClass.Marksman));
            Assert.AreEqual(BalanceLaw.ContactBurst, WeaponConfig.LawFor(WeaponClass.Shotgun));
        }

        /// <summary>
        /// One trigger pull is every pellet it throws. Reading bodyDamage alone
        /// scored a 12x11 shotgun as an 11-damage gun needing NINE pulls to kill a
        /// 100 HP drone, when it in fact kills in one.
        /// </summary>
        [Test]
        public void AShotgunPull_IsAllOfItsPellets()
        {
            WeaponConfig shotgun = ScriptableObject.CreateInstance<WeaponConfig>();
            shotgun.weaponClass = WeaponClass.Shotgun;
            shotgun.bodyDamage = 11f;
            shotgun.pelletsPerShot = 12;
            shotgun.roundsPerMinute = 70f;
            shotgun.falloffRange = new Vector2(2f, 14f);
            shotgun.minDamageMultiplier = 0.35f;

            Assert.AreEqual(132f, shotgun.DamagePerShot, 0.001f);
            Assert.AreEqual(1, shotgun.ShotsToKill(100f), "a 132-damage pull is one pull, not nine");
            Assert.AreEqual(0f, shotgun.TimeToKill(100f), 0.0001f);

            // And the other half of the law: it must stop being that gun at range.
            Assert.GreaterOrEqual(shotgun.ShotsToKillAtRange(100f, WeaponConfig.SHOTGUN_TWO_PULL_METRES), 2);
            Assert.Greater(shotgun.TimeToKillAtRange(100f, WeaponConfig.SHOTGUN_TWO_PULL_METRES), 0f);
        }

        /// <summary>
        /// A burst weapon does not fire its bursts for free. WeaponController adds
        /// burstPause on top of the cadence after the last round of a burst, so a
        /// kill that crosses a burst boundary pays it — 257 ms became 377 ms, and
        /// the difference is 30% of the whole window.
        /// </summary>
        [Test]
        public void BurstPause_IsChargedToTimeToKill()
        {
            WeaponConfig burst = ScriptableObject.CreateInstance<WeaponConfig>();
            burst.bodyDamage = 25f;
            burst.roundsPerMinute = 700f;
            burst.fireMode = FireMode.Burst;
            burst.burstCount = 3;
            burst.burstPause = 0.12f;

            WeaponConfig auto = ScriptableObject.CreateInstance<WeaponConfig>();
            auto.bodyDamage = 25f;
            auto.roundsPerMinute = 700f;
            auto.fireMode = FireMode.FullAuto;

            // Four rounds, three cadence gaps, and exactly ONE burst boundary
            // crossed (after round three) — not two, and not zero.
            Assert.AreEqual(4, burst.ShotsToKill(100f));
            Assert.AreEqual(0.377f, burst.TimeToKill(100f), 0.002f);
            Assert.AreEqual(0.257f, auto.TimeToKill(100f), 0.002f);
            Assert.Greater(burst.TimeToKill(100f), auto.TimeToKill(100f));

            // Three rounds never cross a boundary; seven cross two.
            Assert.AreEqual(auto.TimeForShots(3), burst.TimeForShots(3), 0.0001f);
            Assert.AreEqual(auto.TimeForShots(7) + 2f * burst.burstPause, burst.TimeForShots(7), 0.0001f);

            // Still inside the window, which is the point: the model got harsher
            // and the shipped law did not have to move.
            Assert.LessOrEqual(burst.TimeToKill(100f) * 1000f, WeaponConfig.ARCADE_TTK_MAX_MS);
        }

        /// <summary>
        /// TTK is a point-blank number. The same rifle at the end of its falloff
        /// needs seven rounds instead of four, and no gate saw that before.
        /// </summary>
        [Test]
        public void TimeToKillAtRange_ChargesForFalloff()
        {
            WeaponConfig rifle = ScriptableObject.CreateInstance<WeaponConfig>();
            rifle.bodyDamage = 25f;
            rifle.roundsPerMinute = 700f;
            rifle.falloffRange = new Vector2(25f, 60f);
            rifle.minDamageMultiplier = 0.6f;

            // Inside the falloff start, range costs nothing.
            Assert.AreEqual(rifle.TimeToKill(100f), rifle.TimeToKillAtRange(100f, 10f), 0.0001f);

            // Past the end: 15 damage a round, so seven rounds and six gaps.
            Assert.AreEqual(7, rifle.ShotsToKillAtRange(100f, 60f));
            Assert.AreEqual(0.514f, rifle.TimeToKillAtRange(100f, 60f), 0.002f);
            Assert.Greater(rifle.TimeToKillAtRange(100f, 60f), rifle.TimeToKillAtRange(100f, 10f));
        }

        [Test]
        public void TheSmg_TradesRangeForRate_RatherThanBeingStrictlyBetter()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");
            WeaponConfig smg = Load("Assets/_Project/Data/Weapons/SMG_Rapid.asset");

            Assert.Greater(smg.roundsPerMinute, rifle.roundsPerMinute, "the SMG must be the faster gun");
            Assert.Less(smg.adsTime, rifle.adsTime, "and the snappier one");
            // The cost of that: it dies at range. Without a real downside the
            // choice is not a choice.
            Assert.Less(smg.falloffRange.x, rifle.falloffRange.x);
            Assert.Less(smg.DamageAtDistance(40f) * smg.roundsPerMinute / 60f,
                rifle.DamageAtDistance(40f) * rifle.roundsPerMinute / 60f,
                "the rifle must out-damage the SMG at 40 m");
            // The same trade, now readable straight off the model.
            Assert.Greater(smg.TimeToKillAtRange(100f, 40f), rifle.TimeToKillAtRange(100f, 40f),
                "the SMG must take longer to kill at 40 m than the rifle does");
        }

        [Test]
        public void EffectModules_AreAuthoredAsAnOrderedList_NotASingleSlot()
        {
            WeaponConfig rifle = Load("Assets/_Project/Data/Weapons/AR_Standard.asset");

            // An array, because stacking is the product. A single ref here would
            // have quietly capped the whole "without limits" design at one module.
            Assert.IsNotNull(rifle.effectModules);
        }

        // ---------- the sandbox depth bonus ----------

        /// <summary>
        /// Sandbox resolves effect modules one level deeper than a Run.
        ///
        /// The bonus shifts the depth the module is ASKED about rather than the
        /// maxDepth it declares, because maxDepth lives on a shared config asset
        /// and Domain Reload is off — writing to it would rewrite the shipped
        /// balance for every Play session afterwards. This asserts the exact
        /// expression WeaponController evaluates.
        /// </summary>
        [Test]
        public void SandboxDepth_AllowsExactlyOneMoreLevelPerBonusPoint()
        {
            var chain = UnityEditor.AssetDatabase
                .LoadAssetAtPath<EffectModule>("Assets/_Project/Data/Effects/Effect_Chain.asset");
            var game = UnityEditor.AssetDatabase
                .LoadAssetAtPath<GameConfig>("Assets/_Project/Data/Game/GameConfig.asset");
            Assert.IsNotNull(chain, "Effect_Chain.asset is missing");
            Assert.IsNotNull(game, "GameConfig.asset is missing");

            int max = chain!.maxDepth;
            int bonus = game!.sandboxExtraEffectDepth;
            Assert.Greater(bonus, 0, "sandbox gets no extra depth at all, so the feature is inert");

            // Run mode: the offset is zero and the module stops exactly at maxDepth.
            Assert.IsTrue(chain.RunsAtDepth(max - 0), "a Run must still reach maxDepth");
            Assert.IsFalse(chain.RunsAtDepth(max + 1 - 0), "a Run must stop at maxDepth");

            // Sandbox: the same module, asked about a depth `bonus` levels deeper.
            int deepestInSandbox = max + bonus;
            Assert.IsTrue(chain.RunsAtDepth(deepestInSandbox - bonus),
                "sandbox did not gain the extra level");
            Assert.IsFalse(chain.RunsAtDepth(deepestInSandbox + 1 - bonus),
                "sandbox gained MORE than the bonus allows — the recursion rule is the only thing " +
                "between Explosive > Chain > Explosive and a frozen frame");
        }

    }
}
