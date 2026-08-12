#nullable enable
using System.Text;
using UnityEngine;

namespace CoD.Waves
{
    /// <summary>
    /// The handful of pure functions every objective needs, in one place.
    ///
    /// Two of them are lifted verbatim out of <see cref="ArenaObjective"/>, which
    /// solved both problems first and got both right. They are copied rather than
    /// referenced because ArenaObjective is a MonoBehaviour and these are wanted
    /// by ScriptableObjects that never see a scene; the beacon should later
    /// delegate to this class so there is one copy again.
    ///
    /// Everything here is static and pure: no state, no allocation, no Unity
    /// object touched. That is what lets the whole objective layer be tested with
    /// no scene.
    /// </summary>
    public static class ObjectiveMath
    {
        /// <summary>
        /// Is a point inside a pad of the given radius?
        ///
        /// Measured on the FLOOR PLANE, not as a sphere. The player's origin sits
        /// at their feet and a zone is a pad painted on the ground, so a spherical
        /// test is just a slightly smaller circle for no reason — and it shrinks
        /// further the moment anything stands on a crate, which is exactly when a
        /// player would swear they were inside the marker.
        /// </summary>
        public static bool WithinFloorRadius(Vector3 point, Vector3 center, float radius)
        {
            Vector3 delta = point - center;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// A uniform index in [0, count) that is never <paramref name="previous"/>.
        ///
        /// Drawing from a range one shorter and stepping over the excluded index
        /// gives a uniform choice with NO reroll loop. The obvious implementation
        /// — pick, compare, pick again — has an unbounded worst case, which on a
        /// list of one is not a worst case but a hang.
        ///
        /// A negative <paramref name="previous"/> means "nothing to avoid".
        /// </summary>
        public static int PickDifferent(int count, int previous)
        {
            if (count <= 1) return 0;
            if (previous < 0 || previous >= count) return Random.Range(0, count);

            int index = Random.Range(0, count - 1);
            if (index >= previous) index++;
            return index;
        }

        /// <summary>
        /// A 0..1 readout for the HUD. Clamped, and a zero or negative target
        /// answers 1 rather than dividing: an objective asking for nothing is
        /// already satisfied, and NaN on a progress bar renders as a bar of
        /// nothing that never moves.
        /// </summary>
        public static float Progress01(float current, float target) =>
            target <= 0f ? 1f : Mathf.Clamp01(current / target);

        /// <summary>
        /// Append an integer without producing a string.
        ///
        /// <c>StringBuilder.Append(int)</c> looks free and is not: on several
        /// runtimes it formats via <c>value.ToString()</c> and allocates a string
        /// per call. Objective text is rebuilt every frame the HUD shows it, three
        /// or four objectives at a time, so that is a steady drip of garbage for
        /// text nobody changed. Writing the digits by hand costs a handful of
        /// character appends into a buffer the caller already owns.
        /// </summary>
        public static void AppendInt(StringBuilder into, int value)
        {
            if (value < 0)
            {
                into.Append('-');
                // Widened before negating: -int.MinValue does not fit in an int
                // and silently comes back negative, printing "--2147483648".
                AppendDigits(into, (ulong)(-(long)value));
                return;
            }
            AppendDigits(into, (ulong)value);
        }

        /// <summary>
        /// Append seconds as a whole number, rounded UP. A timer reading 0 while
        /// the player still has three tenths of a second is the kind of lie that
        /// gets read as a bug.
        /// </summary>
        public static void AppendSeconds(StringBuilder into, float seconds)
        {
            AppendInt(into, Mathf.CeilToInt(Mathf.Max(0f, seconds)));
            into.Append('s');
        }

        /// <summary>
        /// Digits, most significant first, with no intermediate string and no
        /// buffer of any kind — the leading power of ten is found first so the
        /// digits come out in the order they are wanted.
        /// </summary>
        private static void AppendDigits(StringBuilder into, ulong value)
        {
            ulong divisor = 1UL;
            for (ulong rest = value; rest >= 10UL; rest /= 10UL) divisor *= 10UL;

            while (divisor > 0UL)
            {
                into.Append((char)('0' + (int)(value / divisor % 10UL)));
                divisor /= 10UL;
            }
        }
    }
}
