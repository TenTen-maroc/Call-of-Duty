#nullable enable
using System.Collections.Generic;
using CoD.Enemies;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// The three-attacker rule, implemented. However many drones are alive, only
    /// `maxSimultaneousAttackers` may hold a token and therefore commit to an
    /// attack; everyone else keeps closing, circling and waiting. This single
    /// constraint is why twenty enemies reads as a fight with a shape instead of
    /// as instant death.
    ///
    /// A plain C# object ticked by the WaveRunner, not a MonoBehaviour: it has no
    /// transform, no scene presence, and one owner.
    /// </summary>
    public sealed class AttackTokenPool : IAttackTokenSource
    {
        private readonly List<DroneController> _holders = new(8);
        private readonly List<float> _acquiredAt = new(8);
        private readonly DifficultyConfig _config;

        /// <summary>Sandbox override. -1 means "use the config".</summary>
        private int _capacityOverride = -1;

        public AttackTokenPool(DifficultyConfig config) => _config = config;

        public int Capacity => _capacityOverride >= 0 ? _capacityOverride : _config.maxSimultaneousAttackers;
        public int Held => _holders.Count;

        public void SetCapacityOverride(int capacity) => _capacityOverride = capacity;

        public bool TryAcquire(DroneController drone)
        {
            if (_holders.Contains(drone)) return true;
            if (_holders.Count >= Capacity) return false;
            _holders.Add(drone);
            _acquiredAt.Add(Time.time);
            return true;
        }

        public void Release(DroneController drone)
        {
            int index = _holders.IndexOf(drone);
            if (index < 0) return;
            _holders.RemoveAt(index);
            _acquiredAt.RemoveAt(index);
        }

        /// <summary>
        /// Reclaims tokens held too long. Without this one drone stuck behind
        /// cover mid-windup holds a third of the pack's attacking capacity for the
        /// rest of the wave, and the horde slowly turns into a staring contest —
        /// a bug that looks like "the AI stopped working" and is nearly impossible
        /// to spot in play.
        /// </summary>
        public void Tick(float now)
        {
            float timeout = _config.attackTokenTimeout;
            for (int i = _holders.Count - 1; i >= 0; i--)
            {
                DroneController holder = _holders[i];
                bool expired = now - _acquiredAt[i] > timeout;
                if (holder != null && holder.IsActive && !expired) continue;

                _holders.RemoveAt(i);
                _acquiredAt.RemoveAt(i);
                // Tell the drone too, or it believes it still holds one and never
                // asks again.
                if (holder != null && holder.IsActive) holder.ForceReleaseAttackToken();
            }
        }

        public void Clear()
        {
            _holders.Clear();
            _acquiredAt.Clear();
        }
    }
}
