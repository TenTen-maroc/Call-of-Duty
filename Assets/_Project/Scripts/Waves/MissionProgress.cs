#nullable enable
using CoD.Core;
using CoD.Enemies;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// The kinds of thing a player can walk up to and use. An enum rather than a
    /// string id because it indexes an array — a string key would mean a hash and
    /// a dictionary lookup per interaction, and there is no version of this game
    /// with fifty kinds.
    ///
    /// Order is not serialized anywhere today. If it ever is, adding a member is
    /// safe and reordering one is a silent corruption — the same trap
    /// <see cref="RunOutcome"/> documents.
    /// </summary>
    public enum InteractionKind
    {
        /// <summary>A console the player holds to hack. The default kind.</summary>
        Terminal = 0,

        /// <summary>A demolition charge placed on something.</summary>
        Charge = 1,

        /// <summary>A pickup that carries story rather than power.</summary>
        Intel = 2,

        /// <summary>A door, hatch or lift the player opens.</summary>
        Door = 3,
    }

    public static class InteractionKindExtensions
    {
        /// <summary>How many slots the counter array needs. One place to change when a kind is added.</summary>
        public const int Count = 4;
    }

    /// <summary>
    /// Everything that has happened in this mission so far, as plain counters.
    ///
    /// WHY THIS EXISTS AT ALL
    /// It is the answer to rule 3 of <see cref="MissionObjective"/>. Objectives
    /// must never subscribe to an event — a ScriptableObject that subscribes
    /// keeps the subscription into the next Play session with Domain Reload off,
    /// which is the mutable-static bug class in a shape the guard cannot see. So
    /// the director subscribes ONCE, accumulates everything into this object, and
    /// objectives poll it. The side effect is the property this whole layer is
    /// built around: a test can construct one of these by hand and drive a real
    /// objective to completion with no scene, no runner and no frame.
    ///
    /// A plain C# class, like <see cref="StatSheet"/> and for the same reason: it
    /// is pure runtime state, and runtime state written into a ScriptableObject
    /// would persist into the repo.
    ///
    /// ALLOCATION
    /// Nothing here allocates after construction. Per-type kills use two parallel
    /// arrays scanned linearly instead of a Dictionary: a mission fields a handful
    /// of drone types, a linear scan of four entries beats a hash, and — the part
    /// that actually matters — a Dictionary grows, rehashes and produces garbage
    /// on the frame a whole wave dies at once, which is the worst frame in the
    /// game to spend a collection on.
    /// </summary>
    public sealed class MissionProgress
    {
        /// <summary>Buffer size, not a tuning number: the most drone types one mission can count separately.</summary>
        public const int KILL_TYPE_CAPACITY = 8;

        /// <summary>Buffer size, not a tuning number: the most zones one mission can register.</summary>
        public const int ZONE_CAPACITY = 16;

        private readonly DroneConfig?[] _killTypes = new DroneConfig?[KILL_TYPE_CAPACITY];
        private readonly int[] _killCounts = new int[KILL_TYPE_CAPACITY];
        private int _killTypeCount;
        private bool _killTypeOverflowReported;

        private readonly int[] _interactions = new int[InteractionKindExtensions.Count];

        private readonly int[] _zoneIds = new int[ZONE_CAPACITY];
        private readonly Vector3[] _zoneCenters = new Vector3[ZONE_CAPACITY];
        private readonly float[] _zoneRadii = new float[ZONE_CAPACITY];
        private int _zoneCount;
        private bool _zoneOverflowReported;

        /// <summary>Waves the runner has finished during this mission. What SurviveWaves counts.</summary>
        public int WavesCleared { get; private set; }

        /// <summary>Every drone killed, of any type.</summary>
        public int Kills { get; private set; }

        /// <summary>Destructible mission objects downed — generators, dishes, crates.</summary>
        public int TargetsDestroyed { get; private set; }

        /// <summary>Interactions of every kind, so an objective can ask "did anything get used".</summary>
        public int Interactions { get; private set; }

        /// <summary>
        /// The stealth flag. One way only — an alarm that could be un-raised would
        /// let a player fail NoAlarm, wait, and pass it again.
        /// </summary>
        public bool AlarmRaised { get; private set; }

        public void RecordWaveCleared() => WavesCleared++;

        /// <summary>
        /// One drone died. <paramref name="drone"/> may be null (a spawn with no
        /// config); the total still counts it, because a quota ignoring a kill the
        /// player definitely made is the more confusing of the two failures.
        /// </summary>
        public void RecordKill(DroneConfig? drone)
        {
            Kills++;
            if (drone == null) return;

            for (int i = 0; i < _killTypeCount; i++)
            {
                if (_killTypes[i] != drone) continue;
                _killCounts[i]++;
                return;
            }

            if (_killTypeCount >= _killTypes.Length)
            {
                // Once, not per kill: a horde game would otherwise print this a
                // few hundred times a wave and bury whatever came after it.
                if (!_killTypeOverflowReported)
                {
                    _killTypeOverflowReported = true;
                    GameLog.Warn($"MissionProgress is already tracking {KILL_TYPE_CAPACITY} drone types — " +
                        "kills of further types count towards the total only. Raise KILL_TYPE_CAPACITY.");
                }
                return;
            }

            _killTypes[_killTypeCount] = drone;
            _killCounts[_killTypeCount] = 1;
            _killTypeCount++;
        }

        /// <summary>Kills of one drone type, or the total when <paramref name="drone"/> is null.</summary>
        public int KillsOf(DroneConfig? drone)
        {
            if (drone == null) return Kills;
            for (int i = 0; i < _killTypeCount; i++)
            {
                if (_killTypes[i] == drone) return _killCounts[i];
            }
            return 0;
        }

        public void RecordTargetDestroyed() => TargetsDestroyed++;

        public void RecordInteraction(InteractionKind kind)
        {
            Interactions++;
            int index = (int)kind;
            // Casting an out-of-range int to an enum is legal C#, so a mis-authored
            // asset can hand us a value that is not a member at all.
            if (index >= 0 && index < _interactions.Length) _interactions[index]++;
        }

        public int InteractionsOf(InteractionKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < _interactions.Length ? _interactions[index] : 0;
        }

        /// <summary>
        /// How many times the player has died and rewound to a checkpoint.
        ///
        /// Deliberately survives the rewind while everything else is rebuilt, so
        /// it is a count of attempts rather than a count since the last one. It
        /// is the only progress value the mission RECORD keeps, and a rating that
        /// ignored deaths would rate a mission finished on the twelfth attempt
        /// the same as one finished clean.
        /// </summary>
        public int Deaths { get; private set; }

        public void RecordDeath() => Deaths++;

        public void RaiseAlarm() => AlarmRaised = true;

        /// <summary>
        /// Teach the record where a zone is. The director calls this once per zone
        /// at mission start, and again if a zone moves; objectives only ever hold
        /// the id.
        ///
        /// That indirection is what keeps objectives authorable at all: a
        /// ScriptableObject cannot hold a scene Transform, so a zone an asset
        /// pointed at directly would be a reference that resolves to null in every
        /// scene but the one it was authored in.
        /// </summary>
        public void RegisterZone(int id, Vector3 center, float radius)
        {
            for (int i = 0; i < _zoneCount; i++)
            {
                if (_zoneIds[i] != id) continue;
                _zoneCenters[i] = center;
                _zoneRadii[i] = radius;
                return;
            }

            if (_zoneCount >= _zoneIds.Length)
            {
                if (!_zoneOverflowReported)
                {
                    _zoneOverflowReported = true;
                    GameLog.Warn($"MissionProgress already holds {ZONE_CAPACITY} zones — zone {id} was dropped, " +
                        "so every objective pointing at it can never complete. Raise ZONE_CAPACITY.");
                }
                return;
            }

            _zoneIds[_zoneCount] = id;
            _zoneCenters[_zoneCount] = center;
            _zoneRadii[_zoneCount] = radius;
            _zoneCount++;
        }

        public bool TryGetZone(int id, out Vector3 center, out float radius)
        {
            for (int i = 0; i < _zoneCount; i++)
            {
                if (_zoneIds[i] != id) continue;
                center = _zoneCenters[i];
                radius = _zoneRadii[i];
                return true;
            }
            center = Vector3.zero;
            radius = 0f;
            return false;
        }

        /// <summary>
        /// Occupancy, resolved on demand rather than stored. An unregistered zone
        /// answers false: an objective pointing at a zone this arena does not have
        /// then stays visibly incomplete, instead of completing on frame one
        /// because "not outside anything" got read as "inside".
        /// </summary>
        public bool IsInsideZone(int id, Vector3 position) =>
            TryGetZone(id, out Vector3 center, out float radius) &&
            ObjectiveMath.WithinFloorRadius(position, center, radius);

        /// <summary>
        /// Back to mission start. The checkpoint rewind needs it, and re-registers
        /// zones afterwards because a rewind keeps the arena. Cleared in place: a
        /// fresh MissionProgress per retry would allocate on the one frame the
        /// game is already reloading everything else.
        /// </summary>
        public void Reset()
        {
            WavesCleared = 0;
            Kills = 0;
            TargetsDestroyed = 0;
            Interactions = 0;
            AlarmRaised = false;
            Deaths = 0;

            for (int i = 0; i < _killTypes.Length; i++)
            {
                _killTypes[i] = null;
                _killCounts[i] = 0;
            }
            _killTypeCount = 0;
            _killTypeOverflowReported = false;

            for (int i = 0; i < _interactions.Length; i++) _interactions[i] = 0;

            _zoneCount = 0;
            _zoneOverflowReported = false;
        }
    }
}
