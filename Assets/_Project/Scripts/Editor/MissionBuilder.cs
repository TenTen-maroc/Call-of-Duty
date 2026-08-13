#nullable enable
using CoD.Waves;
using CoD.Enemies;
using CoD.Core;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>
    /// Authors the campaign: the objective assets, the two missions built out of
    /// them, and the catalog entry that makes each one reachable from the menu.
    /// Run it from the CoD menu, or headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.MissionBuilder.BuildMissionsHeadless
    ///
    /// A SEPARATE FILE FROM GreyBoxBuilder, deliberately. The grey box builds the
    /// arena — scenes, prefabs, materials, the navmesh, the wave assets — and it
    /// is already three thousand lines. A mission is none of those things: it is
    /// pure data, it references assets the grey box already made, and it is the
    /// part of the project that will grow by a file every time someone designs a
    /// mission. Two builders means the campaign can be re-authored without
    /// re-baking a navmesh, and it means a mistake in here cannot cost the arena.
    ///
    /// RUN ORDER. Grey box FIRST, then this. Every wave this file references and
    /// the catalog it fills are the grey box's assets; the errors below say so by
    /// name rather than leaving a mission quietly pointing at nothing.
    ///
    /// IDEMPOTENT, with the same discipline GreyBoxBuilder.LoadOrCreate has: the
    /// configure callback runs ON CREATE ONLY, so a number a human moved in the
    /// Inspector survives a re-run. What is re-asserted on every run is
    /// REFERENCES — the objective a step points at, the waves a mission fights —
    /// because a broken reference is not a tuning difference, it is a mission
    /// that skips a step or a fight that spawns the endless ramp instead of the
    /// wave someone designed. That is WriteWave's rule, applied to missions.
    /// </summary>
    public static class MissionBuilder
    {
        private const string DataMissions = "Assets/_Project/Data/Missions";
        private const string DataWaves = "Assets/_Project/Data/Waves";
        private const string CatalogPath = DataMissions + "/Missions.asset";

        /// <summary>Scene NAME, not a path — the same string Build Settings knows the arena by.</summary>
        private const string ArenaScene = "10_GreyBox";
        private const string AtlasOutpostScene = "11_AtlasOutpost";

        /// <summary>
        /// Zone ids, and they are IDS rather than tuning numbers — the contract
        /// between an objective asset and whatever registers a marker in the
        /// arena at runtime. An objective cannot hold a scene Transform, so it
        /// holds one of these and <see cref="MissionProgress.RegisterZone"/>
        /// gives it a position; an id nobody registered answers "not inside",
        /// which is why a mismatch here reads as an objective that never
        /// completes rather than one that completes instantly.
        ///
        /// Shared across missions on purpose. The objective assets are shared
        /// too — one "extract" file, not one per mission — so the ids have to
        /// mean the same thing in every arena or the sharing is a lie.
        /// </summary>
        private const int ZONE_CONTROL_POINT = 0;

        /// <summary>The extraction pad. See <see cref="ZONE_CONTROL_POINT"/> for why these are ids and not tuning.</summary>
        private const int ZONE_EXTRACT = 1;

        /// <summary>
        /// Save keys. NEVER renamed once shipped: a mission record is keyed by
        /// this string, so changing one does not rename a record, it orphans it
        /// and silently reports the mission as never played.
        /// </summary>
        private const string Mission01Id = "mission_01_shakedown";

        /// <summary>The second mission's save key. Never renamed — see <see cref="Mission01Id"/>.</summary>
        private const string Mission02Id = "mission_02_hard_contact";
        private const int Mission01HumanizationVersion = 1;
        private const int Mission02OutdoorVersion = 1;

        [MenuItem("CoD/Build Missions", false, 2)]
        public static void Build()
        {
            EnsureFolder(DataMissions);

            // The grey box creates this deliberately empty, so the menu has
            // something to read before any mission exists. Created here too, so
            // this builder is runnable on its own.
            MissionCatalog catalog = LoadOrCreate<MissionCatalog>(CatalogPath, _ => { });

            // ---- the objective assets ------------------------------------
            // One file per objective, shared by every mission that wants it —
            // objectives are stateless by rule, so two missions running the same
            // asset cannot see each other's progress. The title is the HUD line
            // and is read mid-fight in peripheral vision; the description is the
            // briefing line and is read before the shooting starts.

            Obj_ReachZone reachControlPoint = LoadOrCreate<Obj_ReachZone>(
                DataMissions + "/Objective_Reach_ControlPoint.asset", objective =>
                {
                    objective.stableId = "obj_reach_control_point";
                    objective.title = "REACH THE CONTROL POINT";
                    objective.description =
                        "The facility's local control point is marked on your display. Walk to it.";
                    objective.zoneId = ZONE_CONTROL_POINT;
                });

            RadioDialogueConfig missionOneRadio = LoadOrCreate<RadioDialogueConfig>(
                DataMissions + "/Radio_Mission01_MaraVenn.asset", ConfigureMissionOneRadio);

            Obj_SurviveWaves surviveTwo = LoadOrCreate<Obj_SurviveWaves>(
                DataMissions + "/Objective_Survive_Two.asset", objective =>
                {
                    objective.stableId = "obj_survive_two";
                    objective.title = "HOLD OUT";
                    objective.description =
                        "Taking the control point wakes the drones on this floor. Clear two waves.";
                    objective.waves = 2;
                });

            Obj_KillQuota killsTwelve = LoadOrCreate<Obj_KillQuota>(
                DataMissions + "/Objective_Kills_Twelve.asset", objective =>
                {
                    objective.stableId = "obj_kills_twelve";
                    objective.title = "DESTROY DRONES";
                    objective.description =
                        "Thin them out before you commit to the floor. Twelve drones, any type.";
                    objective.quota = 12;
                    // Left null on purpose: every drone counts. A filter here
                    // would make the mission's difficulty depend on the wave mix
                    // rather than on the player.
                    objective.droneFilter = null;
                });

            Obj_HoldZone holdControlPoint = LoadOrCreate<Obj_HoldZone>(
                DataMissions + "/Objective_Hold_ControlPoint.asset", objective =>
                {
                    objective.stableId = "obj_hold_control_point";
                    objective.title = "HOLD THE CONTROL POINT";
                    objective.description =
                        "Stand on the control point while the override runs. Step off and it restarts.";
                    objective.zoneId = ZONE_CONTROL_POINT;
                    objective.holdSeconds = 45f;
                    objective.resetOnLeave = true;
                    // The hold is meant to happen UNDER FIRE — that is the whole
                    // point of putting it on a pad in an arena with three lanes.
                    // Off, the player could bank the whole 45 s during a shop
                    // break, which is a walk rather than a decision.
                    objective.requireWavePhase = true;
                });

            Obj_Extract extractPad = LoadOrCreate<Obj_Extract>(
                DataMissions + "/Objective_Extract_Pad.asset", objective =>
                {
                    objective.stableId = "obj_extract_pad";
                    objective.title = "EXTRACT";
                    objective.description =
                        "The bird is on the pad. Stand on it until it lifts — leaving restarts the count.";
                    objective.zoneId = ZONE_EXTRACT;
                    objective.dwellSeconds = 5f;
                });

            // Mission 2 owns separate objective and radio assets. Mission 1's
            // authored language and C-9 dialogue must remain untouched.
            DroneConfig meridian = RequireAsset<DroneConfig>(
                "Assets/_Project/Data/Drones/Meridian_Rifleman.asset");
            DroneConfig shooter = RequireAsset<DroneConfig>(
                "Assets/_Project/Data/Drones/Drone_Shooter.asset");

            Obj_ReachZone reachOutpost = LoadOrCreate<Obj_ReachZone>(
                DataMissions + "/Objective_M02_Reach_Outpost.asset", objective =>
                {
                    objective.stableId = "obj_m02_reach_outpost";
                    objective.title = "LOCATE THE COMMS HUT";
                    objective.description = "Move through the southern approach and identify the outpost relay.";
                    objective.zoneId = ZONE_CONTROL_POINT;
                });
            Obj_KillQuota firstContact = LoadOrCreate<Obj_KillQuota>(
                DataMissions + "/Objective_M02_FirstContact.asset", objective =>
                {
                    objective.stableId = "obj_m02_first_contact";
                    objective.title = "BREAK MERIDIAN CONTACT";
                    objective.description = "Clear the rifle team holding the outpost lanes.";
                    objective.quota = 4;
                    objective.droneFilter = meridian;
                });
            firstContact.droneFilter = meridian;
            EditorUtility.SetDirty(firstContact);
            Obj_Interact disableRelay = LoadOrCreate<Obj_Interact>(
                DataMissions + "/Objective_M02_DisableRelay.asset", objective =>
                {
                    objective.stableId = "obj_m02_disable_relay";
                    objective.title = "DISABLE THE OUTPOST RELAY";
                    objective.description = "Use the generator-side relay and cut Meridian's uplink.";
                    objective.kind = InteractKind.Terminal;
                    objective.count = 1;
                });
            Obj_SurviveWaves holdOutpost = LoadOrCreate<Obj_SurviveWaves>(
                DataMissions + "/Objective_M02_HoldOutpost.asset", objective =>
                {
                    objective.stableId = "obj_m02_hold_outpost";
                    objective.title = "HOLD AGAINST THE PUSH";
                    objective.description = "Meridian is counterattacking through all three lanes. Clear two pushes.";
                    objective.waves = 2;
                });
            Obj_Extract extractNorth = LoadOrCreate<Obj_Extract>(
                DataMissions + "/Objective_M02_ExtractNorth.asset", objective =>
                {
                    objective.stableId = "obj_m02_extract_north";
                    objective.title = "EXTRACT NORTH";
                    objective.description = "Move through the service gate and hold for pickup.";
                    objective.zoneId = ZONE_EXTRACT;
                    objective.dwellSeconds = 5f;
                });
            RadioDialogueConfig missionTwoRadio = LoadOrCreate<RadioDialogueConfig>(
                DataMissions + "/Radio_Mission02_MaraVenn.asset", ConfigureMissionTwoRadio);
            WaveConfig[] missionTwoWaves = BuildMissionTwoWaves(meridian, shooter);

            // ---- mission 1: SHAKEDOWN ------------------------------------
            // The tutorial, and the first thing anyone will ever play of this
            // campaign. It teaches the three verbs in the order that costs the
            // least: walk somewhere, then fight, then walk somewhere under
            // pressure. Rushers only — both waves it fights are rusher-only
            // assets — so nothing shoots back before the player has fired.
            //
            // Nothing here is timed. A deadline on a step is a good tool and a
            // terrible first impression: a tutorial that can be FAILED teaches
            // the menu, not the game.
            MissionConfig shakedown = LoadOrCreate<MissionConfig>(
                DataMissions + "/Mission_01_Shakedown.asset", mission =>
                {
                    mission.stableId = Mission01Id;
                    mission.displayName = "SHAKEDOWN";
                    mission.briefing =
                        "Facility C-9 went dark eleven hours ago and its drone bay is still answering.\n\n" +
                        "Walk to the local control point, hold the floor while the bay empties itself at you, " +
                        "and take the pad out.\n\n" +
                        "Two waves. Rushers only. Nothing here shoots back.";
                    mission.arenaScene = ArenaScene;
                    mission.startingMoney = 300;
                });

            WriteMission(shakedown,
                new[]
                {
                    new StepPlan(reachControlPoint),
                    new StepPlan(surviveTwo),
                    new StepPlan(extractPad),
                },
                LoadWaves(1, 2));

            // One-time authored upgrade. Future Inspector tuning survives every
            // later builder run; references are still repaired below.
            if (shakedown.humanizationVersion < Mission01HumanizationVersion)
            {
                reachControlPoint.title = "GET THE RELAY ONLINE";
                reachControlPoint.description =
                    "The drone bay is cycling from a local relay. Reach it before the next launch.";
                surviveTwo.title = "BREAK THE DRONE PUSH";
                surviveTwo.description =
                    "The relay woke the bay. Clear both launches so the shutdown can finish.";
                extractPad.title = "FALL BACK TO EXTRACTION";
                extractPad.description =
                    "The bay is quiet. Return to the south pad before the backup circuit recovers.";
                shakedown.briefing =
                    "Facility C-9 went dark eleven hours ago. Its local relay is still cycling the drone bay.\n\n" +
                    "Bring the relay online, survive the bay's remaining launches, then fall back to the south pad.";
                shakedown.steps[1].completionDelaySeconds = 4f;
                shakedown.humanizationVersion = Mission01HumanizationVersion;
                EditorUtility.SetDirty(reachControlPoint);
                EditorUtility.SetDirty(surviveTwo);
                EditorUtility.SetDirty(extractPad);
            }
            shakedown.radioDialogue = missionOneRadio;
            EditorUtility.SetDirty(shakedown);

            // ---- mission 2: HARD CONTACT ---------------------------------
            // The same three verbs with the training wheels off: a quota the
            // player has to go and earn, then the hold that makes a corner with
            // good sightlines the wrong answer, then the way out. More starting
            // money, because by now the shop is part of the answer.
            MissionConfig hardContact = LoadOrCreate<MissionConfig>(
                DataMissions + "/Mission_02_HardContact.asset", mission =>
                {
                    mission.stableId = Mission02Id;
                    mission.displayName = "HARD CONTACT";
                    mission.briefing =
                        "The bay is awake, and it has stopped sending rushers on their own.\n\n" +
                        "Break twelve of them, then run the override from the control point — " +
                        "forty-five seconds standing still, in the open, while the rest of the floor comes to you.\n\n" +
                        "Then take the pad out.";
                    mission.arenaScene = AtlasOutpostScene;
                    mission.startingMoney = 450;
                });

            WriteMission(hardContact,
                new[]
                {
                    new StepPlan(reachOutpost),
                    new StepPlan(firstContact),
                    new StepPlan(disableRelay),
                    new StepPlan(holdOutpost),
                    new StepPlan(extractNorth),
                },
                missionTwoWaves);

            if (hardContact.humanizationVersion < Mission02OutdoorVersion)
            {
                hardContact.briefing =
                    "A Vantage relay is transmitting from Tazir Pass. Meridian reached it first.\n\n" +
                    "Cross the forest approach, break their rifle team, cut the uplink, and hold the outpost " +
                    "until the northern route opens.";
                hardContact.arenaScene = AtlasOutpostScene;
                hardContact.steps[1].completionDelaySeconds = 1.2f;
                hardContact.steps[2].completionDelaySeconds = 1.5f;
                hardContact.steps[3].completionDelaySeconds = 3f;
                hardContact.humanizationVersion = Mission02OutdoorVersion;
            }
            hardContact.radioDialogue = missionTwoRadio;
            EditorUtility.SetDirty(hardContact);

            EnsureInCatalog(catalog, shakedown, hardContact);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Missions built: {catalog.Count} in the catalog, under {DataMissions}.");
        }

        /// <summary>Entry point for -executeMethod. Same work, non-zero exit on failure.</summary>
        public static void BuildMissionsHeadless()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Mission build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- the plan ----------

        /// <summary>
        /// One authored step, before it becomes a <see cref="MissionConfig.Step"/>.
        ///
        /// A readonly struct with a constructor rather than an object
        /// initializer, for the reason GreyBoxBuilder's RunAssets gives: under
        /// `#nullable enable` an initializer leaves the fields provably
        /// unassigned, and the quality gate fails on warnings, not just errors.
        /// </summary>
        private readonly struct StepPlan
        {
            public readonly MissionObjective Objective;

            /// <summary>Runs alongside the step BEFORE it. Never true on step 0 — there is nothing there to join.</summary>
            public readonly bool Parallel;

            /// <summary>Seconds before the step fails. 0 = untimed, which is every step in both missions today.</summary>
            public readonly float TimeLimitSeconds;

            public StepPlan(MissionObjective objective, bool parallel = false, float timeLimitSeconds = 0f)
            {
                Objective = objective;
                Parallel = parallel;
                TimeLimitSeconds = timeLimitSeconds;
            }
        }

        private static void ConfigureMissionOneRadio(RadioDialogueConfig config)
        {
            config.lines = new[]
            {
                Line("m01_entry", RadioTrigger.MissionEntry, 1, 50, 999f, 3.2f,
                    "Venn to ground team. C-9's relay is still alive. Get eyes on it."),
                Line("m01_first_objective", RadioTrigger.FirstObjective, 1, 45, 999f, 3.4f,
                    "Reach the relay north of the bunker. If it stays dark, the bay keeps cycling."),
                Line("m01_first_contact", RadioTrigger.FirstContact, 1, 65, 999f, 3.0f,
                    "Movement. Rushers only. Keep space and shoot the lit cores."),
                Line("m01_badly_hurt", RadioTrigger.PlayerBadlyHurt, 1, 95, 999f, 2.5f,
                    "You're bleeding. Break contact. The relay can wait."),
                Line("m01_wave_one_clear", RadioTrigger.WaveClear, 1, 55, 999f, 3.1f,
                    "First push is down. The bay is winding up again."),
                Line("m01_relay_found", RadioTrigger.ObjectiveComplete, 1, 52, 999f, 2.8f,
                    "Relay found. Bringing the floor map back now."),
                Line("m01_wave_two_clear", RadioTrigger.WaveClear, 2, 58, 999f, 2.8f,
                    "Second push is down. Hold. Let the room go quiet."),
                Line("m01_complete", RadioTrigger.MissionComplete, 1, 100, 999f, 3.0f,
                    "Copy your signal. C-9 is contained. For tonight.", RadioInterruptionPolicy.Finish),
                Line("m01_failed", RadioTrigger.MissionFailed, 1, 100, 999f, 2.7f,
                    "I've lost your signal. Pulling the route.", RadioInterruptionPolicy.Finish),
            };
        }

        private static void ConfigureMissionTwoRadio(RadioDialogueConfig config)
        {
            config.lines = new[]
            {
                Line("m02_entry", RadioTrigger.MissionEntry, 1, 55, 999f, 3.6f,
                    "Tazir Pass. The relay is in the centre hut. Meridian has the ridgelines."),
                Line("m02_objective", RadioTrigger.FirstObjective, 1, 52, 999f, 3.2f,
                    "Use the trees on approach. The south lane keeps you below their watch position."),
                Line("m02_contact", RadioTrigger.FirstContact, 1, 80, 999f, 3.4f,
                    "Human contact. Meridian rifle team. Their rounds are slow, but the lanes are covered."),
                Line("m02_wave_one", RadioTrigger.WaveClear, 1, 62, 999f, 2.8f,
                    "First team is down. Cut the generator-side relay before they regroup."),
                Line("m02_wave_two", RadioTrigger.WaveClear, 2, 60, 999f, 2.7f,
                    "More movement behind the service gate. Hold the cross-lanes."),
                Line("m02_badly_hurt", RadioTrigger.PlayerBadlyHurt, 1, 96, 999f, 2.5f,
                    "Break line of sight. Their rifle team is walking rounds onto you."),
                Line("m02_complete", RadioTrigger.MissionComplete, 1, 100, 999f, 3f,
                    "North route is open. Meridian lost the relay, not the story. Move.",
                    RadioInterruptionPolicy.Finish),
                Line("m02_failed", RadioTrigger.MissionFailed, 1, 100, 999f, 2.7f,
                    "Signal lost at Tazir. Abort the route.", RadioInterruptionPolicy.Finish),
            };
        }

        private static WaveConfig[] BuildMissionTwoWaves(DroneConfig meridian, DroneConfig shooter)
        {
            return new[]
            {
                MissionWave("Wave_M02_01_FirstContact.asset", 1, "FIRST CONTACT", 100, 4,
                    new WaveConfig.Entry { drone = meridian, count = 4, spawnOverSeconds = 3f, startDelay = 0f }),
                MissionWave("Wave_M02_02_Counterattack.asset", 2, "RIDGELINE PUSH", 140, 12,
                    new WaveConfig.Entry { drone = meridian, count = 7, spawnOverSeconds = 9f, startDelay = 0f }),
                MissionWave("Wave_M02_03_MixedPush.asset", 3, "HARD CONTACT", 180, 12,
                    new WaveConfig.Entry { drone = meridian, count = 8, spawnOverSeconds = 12f, startDelay = 0f },
                    new WaveConfig.Entry { drone = shooter, count = 3, spawnOverSeconds = 8f, startDelay = 4f }),
            };
        }

        private static WaveConfig MissionWave(string fileName, int number, string displayName,
            int bonus, int maxAlive, params WaveConfig.Entry[] entries)
        {
            WaveConfig wave = LoadOrCreate<WaveConfig>(DataWaves + "/" + fileName, _ => { });
            if (wave.designVersion < 1)
            {
                wave.waveNumber = number;
                wave.displayName = displayName;
                wave.durationTarget = number == 1 ? 28f : 45f;
                wave.maxAliveOverride = maxAlive;
                wave.moneyBonusOnClear = bonus;
                wave.designVersion = 1;
            }
            wave.entries = entries;
            EditorUtility.SetDirty(wave);
            return wave;
        }

        private static RadioLine Line(string stableId, RadioTrigger trigger, int occurrence, int priority,
            float cooldownSeconds, float subtitleSeconds, string subtitle,
            RadioInterruptionPolicy interruption = RadioInterruptionPolicy.AllowHigherPriority)
            => new()
            {
                stableId = stableId,
                speakerId = "operator_mara_venn",
                speakerName = "MARA VENN",
                subtitle = subtitle,
                trigger = trigger,
                occurrence = occurrence,
                priority = priority,
                cooldownSeconds = cooldownSeconds,
                subtitleSeconds = subtitleSeconds,
                interruptionPolicy = interruption,
                audioClip = null,
            };

        /// <summary>
        /// Writes a mission's steps and wave list: references always, everything
        /// else only when the mission was re-shaped.
        ///
        /// The rule is lifted from GreyBoxBuilder.WriteWave because it is the
        /// same problem. Object references are ALWAYS re-asserted, because a step
        /// whose objective came back null is a step the director skips — and a
        /// skipped step means every step after it happens in the wrong order, or
        /// the mission simply ends early with nothing anywhere saying why.
        /// Everything else — the parallel flag, the deadline — is written only
        /// when the step COUNT has moved, which is the signal that the mission
        /// was re-shaped in this file rather than retuned in the Inspector.
        ///
        /// The wave list is assigned outright, and that is not an oversight: a
        /// wave list is a list of references and NOTHING else, so there is no
        /// authored value in it to protect. Where a mission's fight is designed
        /// is the call site in this file.
        /// </summary>
        private static void WriteMission(MissionConfig mission, StepPlan[] plan, WaveConfig[] waves)
        {
            bool rebuild = mission.steps.Length != plan.Length;
            if (rebuild) mission.steps = new MissionConfig.Step[plan.Length];

            for (int i = 0; i < plan.Length; i++)
            {
                mission.steps[i].objective = plan[i].Objective;

                if (rebuild)
                {
                    mission.steps[i].parallel = plan[i].Parallel;
                    mission.steps[i].timeLimitSeconds = plan[i].TimeLimitSeconds;
                }

                // THE ONE VALUE THAT IS DERIVED RATHER THAN AUTHORED.
                //
                // An objective that reports CompletesWithMission never completes
                // under its own power — Obj_NoAlarm is the archetype — so a step
                // holding one must be parallel, or the director waits at it
                // forever and MissionConfig.OnValidate errors on exactly that.
                // Asking a plan table to remember which objectives are in that
                // family is asking it to hold a second copy of a fact the
                // objective already knows, and the copies drift the first time an
                // objective changes its answer. Read it from the objective and
                // there is only one list.
                //
                // ANNOUNCED, never silent. MissionConfig normalises a negative
                // deadline and logs it in the same breath for this reason: a
                // value that quietly disagrees with what an author typed is how
                // the mission in the repo and the mission being played drift
                // apart with nothing in between to notice.
                if (plan[i].Objective.CompletesWithMission && !mission.steps[i].parallel)
                {
                    mission.steps[i].parallel = true;
                    Debug.LogWarning(
                        $"[{mission.name}] step {i} is '{plan[i].Objective.name}', which by design can only " +
                        "complete when the mission does. Marked parallel — a step like that authored on its own " +
                        "would hold the mission at it forever.", mission);
                }
            }

            mission.waves = waves;
            EditorUtility.SetDirty(mission);
        }

        /// <summary>
        /// The catalog entry: appended if the mission is not already listed, and
        /// left exactly where it is if it is.
        ///
        /// Order in this array IS mission number, so re-sorting on every build
        /// would renumber a campaign a player is halfway through. Appending is
        /// therefore the only structural write; the one thing re-asserted in
        /// place is the reference itself, for a slot whose asset was deleted and
        /// re-created and now points at nothing.
        /// </summary>
        private static void EnsureInCatalog(MissionCatalog catalog, params MissionConfig[] missions)
        {
            bool changed = false;

            for (int i = 0; i < missions.Length; i++)
            {
                MissionConfig mission = missions[i];
                int index = catalog.IndexOf(mission.stableId);

                if (index >= 0)
                {
                    if (catalog.missions[index] == mission) continue;
                    catalog.missions[index] = mission;
                    changed = true;
                    continue;
                }

                var grown = new MissionConfig[catalog.missions.Length + 1];
                System.Array.Copy(catalog.missions, grown, catalog.missions.Length);
                grown[^1] = mission;
                catalog.missions = grown;
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(catalog);
        }

        // ---------- helpers ----------

        /// <summary>
        /// The mission's fight, by wave number.
        ///
        /// THE TRAP THIS COMMENT EXISTS FOR. WaveRunner resolves a wave by
        /// matching <see cref="WaveConfig.waveNumber"/> against its own counter,
        /// and that counter starts at 1 — so a mission whose list does not
        /// CONTAIN a wave numbered 1 fights the endless ramp for its opening
        /// wave instead of the asset someone designed, silently, with no error
        /// anywhere. Both missions therefore start their list at Wave_01. A
        /// mission that wants to open on a harder fight needs a wave asset
        /// NUMBERED 1 that holds that fight, not a later asset in slot zero.
        /// </summary>
        private static WaveConfig[] LoadWaves(int first, int last)
        {
            var waves = new WaveConfig[last - first + 1];
            for (int i = 0; i < waves.Length; i++)
            {
                int number = first + i;
                string path = DataWaves + "/Wave_" + number.ToString("00") + ".asset";
                WaveConfig? wave = AssetDatabase.LoadAssetAtPath<WaveConfig>(path);
                if (wave == null)
                {
                    throw new System.InvalidOperationException(
                        $"Mission authoring needs '{path}' and it is not there. Run CoD -> Build Grey Box first: " +
                        "the waves are its assets, and a mission that references none of them quietly fights the " +
                        "endless ramp instead of a designed wave.");
                }
                waves[i] = wave;
            }
            return waves;
        }

        /// <summary>
        /// Loads an asset, or creates and configures one if it is not there.
        ///
        /// A second copy of GreyBoxBuilder.LoadOrCreate, and the duplication is
        /// the point: that method is private, and this file exists precisely so
        /// that authoring a mission never edits the three-thousand-line builder
        /// that owns the scenes. Widening it there would couple the two builders
        /// for the sake of four lines.
        ///
        /// CONFIGURE RUNS ON CREATE ONLY. An asset that already exists comes back
        /// untouched, which is what lets a human retune a hold time or a quota in
        /// the Inspector and keep it across a re-run — and it is also the trap:
        /// RENAMING a path here does not rename the asset. It creates a fresh
        /// default one, discards every tuned value in the old file, and reports
        /// success.
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

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset ?? throw new System.InvalidOperationException(
                "Mission authoring requires the asset '" + path + "'. Run the owning builder first.");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder[..split], folder[(split + 1)..]);
        }
    }
}
