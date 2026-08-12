#nullable enable
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// What an interaction IS, for anything that wants to count them.
    ///
    /// A mission objective asks "have three charges been planted", not "has
    /// GameObject 47 been used" — so the kind is the thing that travels, and it
    /// lives in Core because the player raises interactions, the mission layer
    /// counts them, and neither assembly may reference the other.
    ///
    /// APPEND ONLY once a mission asset references one of these. It is
    /// serialized into MissionConfig as an int, so reordering silently re-points
    /// every authored objective at a different kind.
    /// </summary>
    public enum InteractKind
    {
        Generic = 0,
        Terminal = 1,
        Charge = 2,
        Intel = 3,
        Extract = 4,
        Door = 5,
    }

    /// <summary>
    /// Something the player can walk up to and use.
    ///
    /// Deliberately NOT a physics trigger. This project has no trigger colliders
    /// anywhere, and the one player-in-zone test that already works — the repair
    /// beacon — is a floor-plane distance check against a serialized transform.
    /// A trigger layer would be a second spatial model to keep consistent with
    /// the first, plus colliders on the arena floor, which ArenaObjective's pad
    /// has to explicitly destroy because a floor collider either blocks movement
    /// or eats the aim ray.
    /// </summary>
    public interface IInteractable
    {
        InteractKind Kind { get; }

        /// <summary>False while spent, locked, or mid-use. A refused prompt still SHOWS — silence reads as a bug.</summary>
        bool CanInteract { get; }

        /// <summary>Pre-built. Never composed per frame: the HUD reads this every frame the player is in range.</summary>
        string Prompt { get; }

        /// <summary>0 = instant. Anything above holds the key, which is what makes planting a charge a commitment.</summary>
        float HoldSeconds { get; }

        Vector3 Position { get; }

        /// <summary>Called ONCE, when the hold completes. Never called while CanInteract is false.</summary>
        void Interact();
    }

    /// <summary>Geometry shared by the player, the mission layer and the arena.</summary>
    public static class Interaction
    {
        /// <summary>
        /// Distance on the FLOOR PLANE, squared.
        ///
        /// Lifted from ArenaObjective, reasoning and all, because it is not
        /// obvious and it is the same everywhere: the player's origin sits at
        /// their feet and every zone in this game is a pad, so a spherical test
        /// is just a slightly smaller circle that also punishes standing on a
        /// crate. Squared because nothing here needs the root.
        /// </summary>
        public static float FloorSqrDistance(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        public static bool WithinFloorRadius(Vector3 a, Vector3 b, float radius)
            => FloorSqrDistance(a, b) <= radius * radius;

        /// <summary>
        /// How squarely the player faces a point, as a dot product in -1..1.
        ///
        /// Facing rather than a raycast, on purpose. A raycast picks whatever the
        /// crosshair is exactly on, which means an interactable at your feet — an
        /// intel pickup, an extract pad — could never be selected without looking
        /// down at it. A cone picks what you walked up to, which is what the
        /// player means.
        /// </summary>
        public static float Facing(Vector3 from, Vector3 forward, Vector3 target)
        {
            Vector3 toTarget = target - from;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return 1f;

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) return 0f;

            return Vector3.Dot(flatForward.normalized, toTarget.normalized);
        }
    }
}
