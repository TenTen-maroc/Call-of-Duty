#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Enemies;
using CoD.Player;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The campaign, actually running in the real arena — and, first of all, the
    /// promise that it is not there when nobody asked for it.
    ///
    /// WHAT THIS SUITE IS FOR. The mission layer was added on top of a wave loop
    /// that already worked, through a seam whose entire acceptance criterion is
    /// invisible in a diff: with no mission selected, the game must behave
    /// exactly as it did before any of it existed. A regression there does not
    /// throw, does not log and does not fail a build — it quietly changes the
    /// game every existing player is playing. <see cref="Endless_IsUntouched"/>
    /// is therefore the most important test in the file, and it is deliberately
    /// first.
    ///
    /// The other half is the record. Permadeath's best round is the one number
    /// the endless game is played for, and a mission's wave number must never
    /// reach it: mission content does not share the endless difficulty curve, so
    /// a mission that wrote to <c>bestRound</c> would inflate a record nobody
    /// earned, permanently, on the player's own disk. WaveRunner.FinishRun
    /// deliberately does not call RecordRunEnded and MissionDirector deliberately
    /// does not either, which means the guarantee is currently held by TWO
    /// omissions — and an omission is exactly the kind of thing a later change
    /// adds back without noticing.
    ///
    /// WHAT IT CANNOT PROVE. Nothing in the arena registers a mission zone yet
    /// (see the header on <see cref="StandOnZone"/>), so the zone objectives are
    /// driven by registering the marker where the player already is. That proves
    /// the director, the objective and the step machine; it does not prove an
    /// arena that hands the director a real pad, because no such arena exists.
    /// </summary>
    public sealed class CampaignTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/10_GreyBox.unity";
        private const string ArenaScene = "10_GreyBox";

        /// <summary>The stableId MissionBuilder authors mission 1 with. A save key, not a tuning value.</summary>
        private const string Mission01Id = "mission_01_shakedown";

        /// <summary>The mission that shipped as an empty room. See MissionTwo_ActuallyRunsItsWaves.</summary>
        private const string Mission02Id = "mission_02_hard_contact";

        /// <summary>Zone ids, matching the ones MissionBuilder authors the objective assets with.</summary>
        private const int ZoneControlPoint = 0;
        private const int ZoneExtract = 1;

        /// <summary>
        /// Real-second budgets, not tuning numbers: they exist so a hang fails
        /// one test instead of wedging the run. Generous on purpose —
        /// docs/systems/performance.md records that 20 s is already marginal in
        /// batchmode, where there is no GPU and every frame is whatever the CPU
        /// manages.
        /// </summary>
        private const float StepSeconds = 60f;

        /// <summary>The budget for driving a whole mission: two waves, two countdowns, a shop break and a 5 s extract.</summary>
        private const float MissionSeconds = 240f;

        /// <summary>
        /// How long "nothing spawned" is watched for. Must comfortably exceed
        /// WaveRunner's countdown (4 s), or the test passes simply by finishing
        /// before the first wave would have started anyway.
        /// </summary>
        private const float QuietSeconds = 10f;

        /// <summary>
        /// The radius the test registers a zone with. Not a tunable — nothing in
        /// the game reads it. It stands in for an arena that would register a
        /// real pad, and it matches the radius the repair beacon uses so the
        /// test is not asserting on a target smaller than any the game has.
        /// </summary>
        private const float ZoneRadiusMeters = 2.5f;

        // A campaign mission ends runs, kills the player and finishes missions —
        // every one of those is a moment something could write the player's
        // record. The suite once inflated a human's totalRuns from 2 to 5.
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator ResetTheSave()
        {
            // Capture and reset ONLY. The scene is loaded per test, because the
            // save has to say which mission — or that there is none — BEFORE
            // MissionDirector.Awake reads it, and Awake runs during the load.
            _save.CaptureAndReset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreTheSave()
        {
            _save.Restore();
            yield return null;
        }

        // ---------- fixture plumbing ----------

        /// <summary>
        /// Points the save at a campaign mission. Must be called BEFORE the arena
        /// loads: the save file is the only sanctioned channel between a menu and
        /// the scene it launches (Domain Reload is off, so a static carrier would
        /// survive into the next Play session), and the director reads it in
        /// Awake.
        /// </summary>
        private static void SelectMission(string stableId)
        {
            SaveData save = SaveSystem.Load();
            save.campaignSelected = true;
            save.selectedMissionId = stableId;
            SaveSystem.Save(save);
        }

        private static IEnumerator LoadArena()
        {
            AsyncOperation? load = SceneManager.LoadSceneAsync(ArenaScene, LoadSceneMode.Single);
            Assert.IsNotNull(load, $"'{ScenePath}' must be in the build settings — the builder registers it");
            while (load != null && !load.isDone) yield return null;
            // One frame past the load so every Awake and Start has run. The whole
            // campaign seam depends on that ordering: the director suspends in
            // Awake and WaveRunner.Start reads the flag.
            yield return null;
        }

        /// <summary>Polls a condition with a real-time budget, so a hang fails the test instead of the run.</summary>
        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds, string what,
            System.Action? eachFrame = null)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail($"timed out waiting for {what}");
                eachFrame?.Invoke();
                yield return null;
            }
        }

        private static MissionDirector FindDirector()
        {
            // Include, not the default Exclude: this component switches itself
            // off in Awake for endless mode, and "is it off" is the single thing
            // the first test in this file exists to assert. A search that could
            // not see a disabled component would report the endless case as a
            // missing director and pass for the wrong reason.
            var director = Object.FindFirstObjectByType<MissionDirector>(FindObjectsInactive.Include);
            Assert.IsNotNull(director,
                "no MissionDirector in the arena. The scenes are GENERATED — run CoD -> Build Grey Box " +
                "(GreyBoxBuilder.BuildHeadless) and the campaign layer lands in 10_GreyBox.");
            return director!;
        }

        private static T Find<T>(string missing) where T : Object
        {
            var found = Object.FindFirstObjectByType<T>();
            Assert.IsNotNull(found, missing);
            return found!;
        }

        /// <summary>
        /// Puts a mission zone under the player's feet.
        ///
        /// WHY A TEST HAS TO DO THIS. An objective holds a zone ID, never a
        /// Transform — a ScriptableObject cannot reference a scene object, so
        /// MissionProgress.RegisterZone is how a marker gets a position. Nothing
        /// in the arena calls it today: 10_GreyBox has no mission markers, so
        /// every zone objective in the game currently answers "not inside"
        /// forever. Registering it here drives the director and the objective
        /// honestly; it does NOT prove an arena that has pads in it, and until
        /// something in the scene registers a zone, neither mission is
        /// completable by a human.
        /// </summary>
        private static void StandOnZone(MissionDirector director, int zoneId, Transform player)
            => director.Progress.RegisterZone(zoneId, player.position, ZoneRadiusMeters);

        /// <summary>
        /// Waits until the mission has actually begun.
        ///
        /// Not paranoia about Start ordering — insurance against the ONE write
        /// that would make this suite lie. MissionDirector.BeginMission calls
        /// MissionProgress.Reset, and Reset drops every registered zone. A zone
        /// registered a frame too early is therefore silently erased, every zone
        /// objective then answers "not inside" forever, and the test fails on a
        /// timeout that says nothing about the cause.
        /// </summary>
        private static IEnumerator WaitForMissionStart(MissionDirector director)
        {
            yield return WaitUntil(() => director.IsRunning, StepSeconds,
                "the mission to begin. If this times out, the catalog has no mission with the selected id — " +
                "run CoD -> Build Missions (MissionBuilder.BuildMissionsHeadless).");
        }

        /// <summary>
        /// Runs the wave loop forward without fighting it.
        ///
        /// Both calls no-op in the wrong phase, so this is safe every frame. The
        /// combat is covered by GreyBoxLoopTests against real drones; what is
        /// under test here is the STEP MACHINE and the save file, and a mission
        /// driven by real fighting is a coin flip on whether a Rusher reaches the
        /// player first — which in campaign is a checkpoint rewind, not a
        /// failure, so it would not fail the test, it would slow it past its
        /// budget for reasons the test never mentions.
        /// </summary>
        private static void SkipTheFighting(WaveRunner runner)
        {
            runner.SkipWave();
            runner.ContinueFromShop();
        }

        // ---------- the acceptance criterion ----------

        /// <summary>
        /// No mission selected, no mission layer. The director switches itself
        /// off, the runner is never held, and the first wave arrives exactly as
        /// it does in a build with no campaign code in it at all.
        ///
        /// This is the one that matters most. Everything else in the campaign can
        /// be broken and only campaign players notice; break this and the endless
        /// game — the whole shipped game today — stops starting.
        /// </summary>
        [UnityTest]
        public IEnumerator Endless_IsUntouched()
        {
            // The save written by SaveFileGuard.CaptureAndReset is endless:
            // campaignSelected false, no mission id. Nothing selects one here.
            yield return LoadArena();

            MissionDirector director = FindDirector();
            Assert.IsFalse(director.enabled,
                "the director must disable itself in Awake when no campaign is selected — enabled, it subscribes " +
                "to the runner and starts driving a game nobody asked for");
            Assert.IsNull(director.Mission, "a director with no campaign must resolve no mission");
            Assert.IsFalse(director.IsRunning, "a director with no campaign must not be running a mission");

            var runner = Find<WaveRunner>("no WaveRunner");
            Assert.IsFalse(runner.Suspended,
                "the runner must NOT be held in endless mode — a suspended runner with no director to resume it " +
                "is an empty arena that never ends, which is indistinguishable from a hang");

            var registry = Find<DroneRegistry>("no DroneRegistry");
            yield return WaitUntil(() => runner.Phase == RunPhase.Wave, StepSeconds,
                "the first wave to start exactly as it does without the mission layer");
            yield return WaitUntil(() => registry.AliveCount > 0, StepSeconds, "the first drone to spawn");

            Assert.AreEqual(RunOutcome.Died, runner.Outcome,
                "Died is the only ending endless mode has, and must stay the default with no director driving");
        }

        // ---------- the campaign boot ----------

        /// <summary>
        /// A campaign boot holds the loop. The runner is suspended before its own
        /// Start can open a countdown, and nothing spawns until a step asks for
        /// enemies — mission 1 opens on a walk, so the arena must stay empty
        /// well past the moment wave 1 would otherwise have arrived.
        /// </summary>
        [UnityTest]
        public IEnumerator CampaignBoot_SuspendsTheRunner_AndSpawnsNothing()
        {
            SelectMission(Mission01Id);
            yield return LoadArena();

            MissionDirector director = FindDirector();
            Assert.IsTrue(director.enabled,
                "the director disabled itself with a campaign selected — either the save did not reach it or the " +
                "catalog has no mission with this id. Run CoD -> Build Missions.");
            Assert.IsNotNull(director.Mission, "the catalog did not resolve the selected mission");
            Assert.AreEqual(Mission01Id, director.Mission!.stableId);
            Assert.IsTrue(director.IsRunning, "the mission never began");
            Assert.AreEqual(0, director.ActiveStep, "a mission starts at its first step");
            Assert.AreEqual(ObjectiveStatus.Active, director.StateOf(0).Status,
                "the first step must be ticking the moment the mission starts");

            var runner = Find<WaveRunner>("no WaveRunner");
            var registry = Find<DroneRegistry>("no DroneRegistry");
            Assert.IsTrue(runner.Suspended,
                "the runner must be held: mission 1 opens on a walk, and a live wave during it is a firefight " +
                "nobody authored");

            // Watched for longer than the countdown, not sampled once. The bug
            // this guards is a runner that was suspended and then resumed itself
            // a few seconds later — a single assertion on frame two would pass
            // for a build that starts wave 1 at second four.
            float deadline = Time.realtimeSinceStartup + QuietSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.AreNotEqual(RunPhase.Wave, runner.Phase,
                    "a wave started during a step that never asked for enemies");
                Assert.AreEqual(0, registry.AliveCount,
                    "a drone spawned during a step that never asked for enemies");
                yield return null;
            }
        }

        /// <summary>
        /// The step machine: the first objective is live, completing it resolves
        /// that step and activates the next — and the wave gate opens exactly
        /// then, because step 2 is the one that needs enemies.
        /// </summary>
        [UnityTest]
        public IEnumerator FirstObjective_Activates_AndCompletingItAdvancesToTheSecond()
        {
            SelectMission(Mission01Id);
            yield return LoadArena();

            MissionDirector director = FindDirector();
            yield return WaitForMissionStart(director);

            var runner = Find<WaveRunner>("no WaveRunner");
            var motor = Find<PlayerMotor>("no player in the arena");

            Assert.IsNotNull(director.Mission, "the catalog did not resolve the selected mission");
            Assert.GreaterOrEqual(director.Mission!.StepCount, 2,
                "this test needs a mission with a second step to advance to");
            Assert.AreEqual(0, director.ActiveStep);
            Assert.AreEqual(ObjectiveStatus.Active, director.StateOf(0).Status);
            Assert.AreEqual(ObjectiveStatus.Pending, director.StateOf(1).Status,
                "a step that has not begun must read Pending, never anything a director could mistake for progress");
            Assert.IsTrue(runner.Suspended);

            StandOnZone(director, ZoneControlPoint, motor.transform);

            yield return WaitUntil(() => director.ActiveStep == 1, StepSeconds,
                "the reach step to complete and hand over to the next one");

            Assert.AreEqual(ObjectiveStatus.Complete, director.StateOf(0).Status,
                "the step that advanced the mission must be recorded Complete, not merely left behind");
            Assert.AreEqual(ObjectiveStatus.Active, director.StateOf(1).Status,
                "the next step must be ticking, or the mission is stalled at a step nobody is running");

            // The gate, which is the half of the advance that is easy to lose:
            // step 2 is Obj_SurviveWaves, so waves have to come back on.
            yield return WaitUntil(() => !runner.Suspended, StepSeconds,
                "the wave gate to open for a step that requires waves");
            yield return WaitUntil(() => runner.Phase == RunPhase.Wave, StepSeconds,
                "the first wave of the mission");
        }

        // ---------- permadeath integrity ----------

        /// <summary>
        /// FINISHING A MISSION MUST NOT TOUCH THE PERMADEATH RECORD.
        ///
        /// bestRound is the endless game's whole scoreboard, and a mission does
        /// not share its difficulty curve — a mission that wrote its wave number
        /// there would inflate a record the player never earned, on their own
        /// disk, permanently. The guarantee is held by two OMISSIONS today
        /// (WaveRunner.FinishRun does not call RecordRunEnded; nor does
        /// MissionDirector), and an omission is the easiest thing in a codebase
        /// for a later change to put back.
        ///
        /// totalRuns and totalKills are asserted alongside it because they are
        /// written by the same method: whatever adds RecordRunEnded to a mission
        /// ending moves all three at once.
        /// </summary>
        [UnityTest]
        public IEnumerator CompletingAMission_NeverWritesThePermadeathRecord()
        {
            SelectMission(Mission01Id);
            yield return LoadArena();

            MissionDirector director = FindDirector();
            yield return WaitForMissionStart(director);

            var runner = Find<WaveRunner>("no WaveRunner");
            var motor = Find<PlayerMotor>("no player in the arena");
            var health = motor.GetComponent<Health>();
            Assert.IsNotNull(health, "the player has no Health");

            // Determinism, not the behaviour under test. The player stands still
            // for the whole mission while real waves are driven past them; a
            // Rusher reaching them is a checkpoint rewind, which would not fail
            // this test, it would silently double its length. Invulnerable blocks
            // ApplyDamage and nothing else — the death path is asserted on its
            // own in ACampaignDeath_RewindsAndWritesNothing.
            health!.Invulnerable = true;

            // Step 1: walk to the control point.
            StandOnZone(director, ZoneControlPoint, motor.transform);
            yield return WaitUntil(() => director.ActiveStep >= 1, StepSeconds, "the reach step");

            // Step 2: two waves, driven rather than fought.
            yield return WaitUntil(() => director.ActiveStep >= 2, MissionSeconds,
                "two waves to clear and the mission to reach its last step",
                () => SkipTheFighting(runner));
            Assert.GreaterOrEqual(director.Progress.WavesCleared, 2,
                "the survive step advanced without the waves it counts actually clearing");

            // Step 3: the extraction pad, wherever the player ended up standing.
            StandOnZone(director, ZoneExtract, motor.transform);
            yield return WaitUntil(() => runner.Phase == RunPhase.GameOver, MissionSeconds,
                "the mission to finish on the extract", () => SkipTheFighting(runner));

            Assert.AreEqual(RunOutcome.MissionComplete, runner.Outcome,
                "the mission ended, but not as a completion — a mission that finishes as Died would take the " +
                "permadeath path with it");
            Assert.IsFalse(director.IsRunning, "a finished mission must stop running");

            health.Invulnerable = false;

            SaveData after = SaveSystem.Load();
            // Read back first: this proves the file being asserted on is the one
            // the run actually used. Without it, every zero below could be a zero
            // from reading some other file, and the test would pass on a build
            // that wrote the record perfectly.
            Assert.IsTrue(after.campaignSelected, "this is not the save the mission ran against");
            Assert.AreEqual(Mission01Id, after.selectedMissionId, "this is not the save the mission ran against");

            Assert.AreEqual(0, after.bestRound,
                "a completed mission wrote bestRound. Mission waves do not share the endless difficulty curve, " +
                "so this permanently inflates the one number the endless game is played for.");
            Assert.AreEqual(0, after.totalRuns,
                "a completed mission counted as an endless run");
            Assert.AreEqual(0, after.totalKills,
                "a completed mission folded its kills into the endless lifetime total");
        }

        /// <summary>
        /// And the other ending: dying inside a mission. A campaign death is a
        /// rewind to the last checkpoint, not a game over — so it must not reach
        /// the phase that writes the record, and it must not write it another
        /// way either.
        ///
        /// This is the likelier of the two leaks. Death is the ONE path that
        /// legitimately writes the record in endless mode, and the only thing
        /// separating the two is a single runtime flag the director sets in Awake.
        /// </summary>
        [UnityTest]
        public IEnumerator ACampaignDeath_RewindsAndWritesNothing()
        {
            SelectMission(Mission01Id);
            yield return LoadArena();

            MissionDirector director = FindDirector();
            yield return WaitForMissionStart(director);

            var runner = Find<WaveRunner>("no WaveRunner");
            var motor = Find<PlayerMotor>("no player in the arena");
            var health = motor.GetComponent<Health>();
            Assert.IsNotNull(health, "the player has no Health");

            // Get the mission as far as the step that runs waves, so the death
            // happens in the middle of a real fight rather than during a walk.
            StandOnZone(director, ZoneControlPoint, motor.transform);
            yield return WaitUntil(() => director.ActiveStep >= 1, StepSeconds, "the wave step");
            yield return WaitUntil(() => runner.Phase == RunPhase.Wave, MissionSeconds, "the first wave");

            int deathsBefore = director.Progress.Deaths;

            // Watch for the death rather than looking for it afterwards.
            //
            // The whole rewind is SYNCHRONOUS: ApplyDamage raises Died, Health
            // calls into WaveRunner, WaveRunner raises PlayerDown, and the
            // director revives the player -- all inside this one call. So by the
            // line after it the player is alive again, and an "is the player
            // dead" assertion here would fail against a working rewind while
            // passing against a broken one that left them dead. That is exactly
            // backwards, so the event is what gets asserted.
            bool died = false;
            void OnDied(Health h, DamageInfo info) => died = true;
            health!.Died += OnDied;
            try
            {
                var killingBlow = new DamageInfo(9999f, motor.transform.position, Vector3.up, Vector3.forward, false);
                health.ApplyDamage(in killingBlow);
            }
            finally
            {
                health.Died -= OnDied;
            }
            Assert.IsTrue(died, "the player did not actually die, so nothing below is testing a rewind");

            // Two frames: one for Health to raise Died into WaveRunner, one for
            // the director to have finished rewinding.
            yield return null;
            yield return null;

            Assert.AreNotEqual(RunPhase.GameOver, runner.Phase,
                "a campaign death ended the run. Death in a mission is a checkpoint rewind — GameOver is the " +
                "endless path, and it is the path that writes the record.");
            Assert.AreEqual(deathsBefore + 1, director.Progress.Deaths,
                "the death was not recorded against the mission");
            Assert.IsTrue(director.IsRunning, "the mission stopped running on a death that should have rewound it");

            // THE ASSERTION THIS TEST WAS MISSING, and the reason it certified a
            // wedged mission as correct.
            //
            // The rewind rebuilt the step machine and never touched Health.
            // RunContext.BeginRun ends in ApplyStats, which uses AdjustMax and
            // NOT ConfigureMax -- deliberately, so buying a passive at 8 HP does
            // not heal you -- so with an unchanged max the delta is zero and
            // current health stays at zero. A dead player takes no damage, can
            // never raise Died again, and WeaponController refuses to fire at
            // all. Waves respawn around an invincible corpse that cannot shoot,
            // no quota or clear can ever complete, and the mission is wedged
            // forever with no error anywhere.
            //
            // Every assertion above passed in that state.
            Assert.IsTrue(health.IsAlive,
                "a checkpoint rewind left the player dead. They are now immune to damage, cannot fire, and " +
                "the mission can never be completed or failed — it just never ends.");
            Assert.Greater(health.Current, 0f, "the player was revived to zero health");

            // And the mission must actually resume, not merely be marked running.
            yield return WaitUntil(() => runner.Phase == RunPhase.Countdown || runner.Phase == RunPhase.Wave,
                StepSeconds, "the wave loop to resume after the rewind");

            SaveData after = SaveSystem.Load();
            Assert.IsTrue(after.campaignSelected, "this is not the save the mission ran against");
            Assert.AreEqual(0, after.bestRound,
                "a campaign death wrote bestRound — the mission took the permadeath path");
            Assert.AreEqual(0, after.totalRuns, "a campaign death counted as an endless run");
            Assert.AreEqual(0, after.totalKills, "a campaign death folded its kills into the endless lifetime total");
        }

        /// <summary>
        /// MISSION TWO SPAWNS ENEMIES.
        ///
        /// This is the test whose absence let a completely uncompletable mission
        /// ship. `MissionObjective.RequiresWaves` defaults to false and only
        /// `Obj_SurviveWaves` overrode it; mission 2's steps are a kill quota, a
        /// hold and an extract, so `MissionDirector`'s wave gate left the runner
        /// suspended from Awake and not one drone ever spawned. The quota could
        /// never fill, the hold's phase gate was never satisfied, and
        /// `MissionConfig.OnValidate` passed the asset clean — because it derives
        /// "does this mission want waves" from the same flag that was wrong.
        ///
        /// Every other test in this file loads mission 1, which survives on
        /// `Obj_SurviveWaves` alone. One test that loads mission 2 and waits for
        /// a single spawn is the whole difference.
        /// </summary>
        [UnityTest]
        public IEnumerator MissionTwo_ActuallyRunsItsWaves_SoItsQuotaCanFill()
        {
            SelectMission(Mission02Id);
            yield return LoadArena();

            MissionDirector director = FindDirector();
            yield return WaitForMissionStart(director);

            var runner = Find<WaveRunner>("no WaveRunner");
            var registry = Find<DroneRegistry>("no DroneRegistry");

            Assert.AreEqual(0, director.ActiveStep, "mission 2 should open on its kill quota");
            Assert.IsFalse(runner.Suspended,
                "the runner is suspended on a step that needs enemies. Obj_KillQuota must report " +
                "RequiresWaves, or this mission is a locked empty room.");

            yield return WaitUntil(() => runner.Phase == RunPhase.Wave, StepSeconds,
                "mission 2's first wave to start");
            yield return WaitUntil(() => registry.AliveCount > 0, StepSeconds,
                "mission 2 to spawn a single drone");

            // And the quota counts what dies. Driven rather than fought, for the
            // same reason SkipTheFighting exists.
            int before = director.Progress.Kills;
            KillEverythingAlive(registry);
            yield return null;
            Assert.Greater(director.Progress.Kills, before,
                "drones died and the mission counted none of them");
        }

        /// <summary>Applies a fatal blow to everything alive, so a quota can be driven without a firefight.</summary>
        private static void KillEverythingAlive(DroneRegistry registry)
        {
            for (int i = registry.Alive.Count - 1; i >= 0; i--)
            {
                DroneController drone = registry.Alive[i];
                Health? health = drone.GetComponent<Health>();
                if (health == null || !health.IsAlive) continue;
                var blow = new DamageInfo(9999f, drone.Position, Vector3.up, Vector3.forward, false);
                health.ApplyDamage(in blow);
            }
        }
    }
}
