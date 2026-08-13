#nullable enable
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// One mission: an arena, a wave list, and an ordered list of steps. Nothing
    /// else. Twelve of these and the campaign exists — no mission is allowed to
    /// need code, which is the same bet the weapon system already won.
    ///
    /// The wave list is the mission's, not the runner's. WaveRunner keeps owning
    /// spawning, the alive cap, the shop and the endless ramp; the director hands
    /// it these waves through SetWaves and otherwise leaves it alone. A mission
    /// with an empty wave list is legal and means a mission with no fighting.
    /// </summary>
    [CreateAssetMenu(fileName = "Mission_", menuName = "CoD/Mission Config", order = 70)]
    public sealed class MissionConfig : ScriptableObject
    {
        /// <summary>
        /// One entry in the mission's ordered list.
        ///
        /// WHY "TIMED" IS NOT AN OBJECTIVE TYPE. The time limit lives HERE, on the
        /// step, and is stamped onto the state by
        /// <see cref="MissionObjective.BeginStep"/> and checked uniformly by the
        /// director. The alternative — an Obj_Timed that wraps another objective —
        /// makes objectives compose objectives, and a ScriptableObject tree that
        /// references ScriptableObjects of its own type is where systems like this
        /// rot: it can nest, it can cycle, and the inspector shows you none of it.
        /// This way any objective can be timed, timing is one number, and there is
        /// exactly one countdown implementation in the game.
        /// </summary>
        [System.Serializable]
        public struct Step
        {
            public MissionObjective? objective;

            [Tooltip("Runs alongside the step BEFORE it instead of after. A constraint like NoAlarm is always parallel.")]
            public bool parallel;

            [Tooltip("Seconds before this step fails. 0 = untimed. Any objective can be timed; there is no timed objective type.")]
            [Min(0f)] public float timeLimitSeconds;

            [Tooltip("Authored silence after this group resolves before the next instruction. Independent of dialogue audio.")]
            [Min(0f)] public float completionDelaySeconds;
        }

        [Header("Identity")]
        [Tooltip("Save key for this mission's record. Never renamed once shipped.")]
        public string stableId = "mission_";

        public string displayName = "MISSION";

        [Tooltip("Shown on the briefing screen before the drop.")]
        [TextArea(3, 10)] public string briefing = "";

        [Header("Where")]
        [Tooltip("Scene name, not a path — the same string Build Settings knows the arena by.")]
        public string arenaScene = "10_GreyBox";

        [Tooltip("What the player drops in with. A mission's economy is authored, not inherited from the endless shop.")]
        [Min(0)] public int startingMoney = 300;

        [Tooltip("One-time builder content revision. It protects later human tuning from being overwritten.")]
        [Min(0)] public int humanizationVersion;

        [Header("The mission")]
        [Tooltip("In order. A step is complete before the next begins, unless the next one is marked parallel.")]
        public Step[] steps = System.Array.Empty<Step>();

        [Tooltip("Handed to WaveRunner when the mission starts. Empty means a mission with no fighting.")]
        public WaveConfig[] waves = System.Array.Empty<WaveConfig>();

        [Tooltip("Optional mission-owned radio arc. Null means the mission plays without radio or subtitles.")]
        public RadioDialogueConfig? radioDialogue;

        public int StepCount => steps.Length;

        /// <summary>Waves that actually exist. An array slot left empty in the inspector is not a wave.</summary>
        public int UsableWaveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < waves.Length; i++)
                {
                    if (waves[i] != null) count++;
                }
                return count;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Catches the authoring mistakes that do not throw. Every one of these
        /// produces a mission that loads, plays, and is simply impossible or
        /// instantly over — the failure a player would report as "the game is
        /// broken" and a developer would spend an hour bisecting.
        ///
        /// Errors only, never exceptions, and never a mutation NOBODY IS TOLD
        /// ABOUT: a mis-authored asset must cost a log line, never the run. A
        /// value that is meaningless out of range is normalised here — WaveConfig
        /// does the same to a spawn count below one — but the normalisation is
        /// always announced in the same breath, because a silent clamp is how the
        /// number an author typed and the number the game plays drift apart with
        /// nothing in between to notice.
        /// </summary>
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                Debug.LogError(
                    $"[{name}] has no stableId. Mission records are keyed by it, so this mission's " +
                    "completion could never be saved or read back.", this);
            }

            if (steps.Length == 0)
            {
                Debug.LogError(
                    $"[{name}] has no steps. A mission with nothing to do completes on the frame it starts, " +
                    "which reads as the briefing screen flashing and the mission ending.", this);
            }

            bool wantsWaves = false;
            for (int i = 0; i < steps.Length; i++)
            {
                MissionObjective? objective = steps[i].objective;
                if (objective == null)
                {
                    Debug.LogError(
                        $"[{name}] step {i} has no objective. The director would skip it, so every step after " +
                        "it happens in the wrong order — or the mission ends early with no explanation.", this);
                    continue;
                }

                if (objective.RequiresWaves) wantsWaves = true;

                // A step cannot run alongside a step that does not exist. Harmless
                // at runtime — the director has nothing to pair it with — but it
                // always means the author believed something else was happening.
                if (i == 0 && steps[i].parallel)
                {
                    Debug.LogWarning(
                        $"[{name}] step 0 is marked parallel, but there is no step before it to run alongside.",
                        this);
                }

                // An objective that only completes when the MISSION does cannot
                // be a step the mission waits ON. The director holds the list at
                // that step, and the single thing that could advance it — the
                // mission finishing — is downstream of the step it is stuck at.
                // Obj_NoAlarm is the archetype and is always authored parallel;
                // the mistake is forgetting the tick box, which produces a
                // mission that plays perfectly and then simply never ends.
                if (objective.CompletesWithMission && !steps[i].parallel)
                {
                    Debug.LogError(
                        $"[{name}] step {i} is '{objective.name}', which by design never completes on its own " +
                        "(CompletesWithMission), but it is not marked parallel. The mission would wait at that " +
                        "step forever — nothing left in the arena can finish it. Mark it parallel.", this);
                }

                // Normalised, not silently: BeginStep already reads any
                // non-positive limit as untimed, so this changes no behaviour —
                // it stops the stored number disagreeing with the played one. The
                // error is the part that matters. An author who typed -30 meant
                // 30, and a step that quietly becomes untimed is a deadline that
                // never fires and a mission nobody can explain.
                if (steps[i].timeLimitSeconds < 0f)
                {
                    Debug.LogError(
                        $"[{name}] step {i} had a negative time limit ({steps[i].timeLimitSeconds}s), which reads " +
                        "as untimed. Clamped to 0 — set the seconds you meant.", this);
                    steps[i].timeLimitSeconds = 0f;
                }

                if (steps[i].completionDelaySeconds < 0f)
                {
                    Debug.LogError(
                        $"[{name}] step {i} had a negative completion delay. Clamped to 0.", this);
                    steps[i].completionDelaySeconds = 0f;
                }
            }

            if (radioDialogue != null)
            {
                RadioValidationIssue issues = radioDialogue.Validate(out int invalidLine);
                if (issues != RadioValidationIssue.None)
                {
                    Debug.LogError(
                        $"[{name}] radio dialogue has invalid line {invalidLine}: {issues}.", this);
                }
            }

            if (wantsWaves && UsableWaveCount == 0)
            {
                Debug.LogError(
                    $"[{name}] has a step that counts waves but no waves to run. That objective can never " +
                    "complete, so the mission can never end.", this);
            }
        }
#endif
    }
}
