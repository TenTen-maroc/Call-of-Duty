#nullable enable
using System.Diagnostics;
using UnityEngine;

namespace CoD.Core
{
    /// <summary>
    /// Every log in first-party code goes through here. The [Conditional]
    /// attribute makes the compiler DELETE the call sites outside the editor and
    /// development builds — argument expressions included, so a string
    /// interpolation in a hot path costs nothing in a release build. A plain
    /// Debug.Log would still build the string and then throw it away.
    /// </summary>
    public static class GameLog
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message, Object? context = null)
            => UnityEngine.Debug.Log(message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(string message, Object? context = null)
            => UnityEngine.Debug.LogWarning(message, context);

        /// <summary>
        /// Kept in release builds on purpose — an error that only reports itself
        /// in the editor is an error nobody ever hears about.
        /// </summary>
        public static void Error(string message, Object? context = null)
            => UnityEngine.Debug.LogError(message, context);
    }
}
