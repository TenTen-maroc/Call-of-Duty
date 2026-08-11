#nullable enable
using CoD.Core;
using UnityEngine;

namespace CoD.Enemies
{
    /// <summary>
    /// Radial damage, shared by every attack that has any: the Rusher's
    /// detonation and the Tank's slam resolve identically and should stay that
    /// way. Two copies of this loop would drift, and the difference would show up
    /// as "the Tank ignores cover" long after the change that caused it.
    ///
    /// A static class of pure methods — allowed under the no-mutable-statics rule
    /// because there is no state here to survive a Play session.
    /// </summary>
    internal static class Blast
    {
        /// <summary>
        /// Damages everything with a root Health inside `radius`, falling off to
        /// `minMultiplier` at the edge. The buffer belongs to the drone, so no
        /// allocation and no shared state between drones.
        /// </summary>
        public static void Apply(DroneController drone, Vector3 origin, float radius, float damage,
            float minMultiplier, LayerMask mask)
        {
            Collider[] buffer = drone.OverlapBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, radius, buffer, mask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = buffer[i];
                if (hit == null) continue;

                // Root colliders carrying Health only. Weakpoint children are
                // skipped on purpose: explosions do not score headshots, and
                // matching both colliders on one target damages it twice.
                if (!hit.TryGetComponent(out Health health)) continue;
                if (health == drone.HealthComponent) continue;

                // Drones never damage each other. A blast that wipes the pack
                // steals the player's kills and the wave's money with them.
                if (health.TryGetComponent(out DroneController _)) continue;

                float distance = Vector3.Distance(origin, hit.ClosestPoint(origin));
                float falloff = Mathf.Lerp(1f, minMultiplier, Mathf.Clamp01(distance / Mathf.Max(0.01f, radius)));

                Vector3 toTarget = health.transform.position - origin;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector3.up;
                Vector3 direction = toTarget.normalized;

                var info = new DamageInfo(damage * falloff, origin, -direction, direction, false);
                health.ApplyDamage(in info);
            }
        }
    }
}
