#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// Who is alive right now. A scene object rather than a static registry —
    /// Domain Reload is disabled, so a static list would still hold the previous
    /// Play session's (destroyed) drones and the wave would never end.
    ///
    /// Everything that needs to ask "how many are left?" — the wave runner, the
    /// spawn throttle, the HUD counter, the chain-lightning module later — asks
    /// here instead of running a scene search.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroneRegistry : MonoBehaviour
    {
        private readonly List<DroneController> _alive = new(64);

        /// <summary>Raised after a drone leaves the list, whatever removed it.</summary>
        public event Action<DroneController>? Removed;

        public int AliveCount => _alive.Count;

        /// <summary>
        /// Read-only by convention, exposed as the concrete List on purpose:
        /// iterating an IReadOnlyList boxes the struct enumerator, and this gets
        /// walked every frame once the token pool exists. Index into it; never
        /// add or remove from outside.
        /// </summary>
        public List<DroneController> Alive => _alive;

        public void Register(DroneController drone)
        {
            if (_alive.Contains(drone)) return;
            _alive.Add(drone);
        }

        public void Unregister(DroneController drone)
        {
            if (!_alive.Remove(drone)) return;
            Removed?.Invoke(drone);
        }

        /// <summary>Wave cleanup and the sandbox cheat. Backwards, because each despawn removes an entry.</summary>
        public void DespawnAll()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                DroneController drone = _alive[i];
                if (drone != null) drone.DespawnNow();
            }
            _alive.Clear();
        }

        private void OnDisable() => _alive.Clear();
    }
}
