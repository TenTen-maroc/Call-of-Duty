#nullable enable
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoD.Core
{
    /// <summary>
    /// Proves a BUILT player actually runs. Dormant unless the executable is
    /// launched with <c>-codSmokeTest</c>, so a real player never notices it.
    ///
    /// WHY THIS EXISTS
    /// Every other gate in this project runs inside the editor. A build can pass
    /// all of them and still fail on its own: a scene missing from Build
    /// Settings, an editor-only API reached at runtime, a stripped assembly, a
    /// `#if UNITY_EDITOR` block that was holding something up. The cheat console
    /// in particular is gated on UNITY_EDITOR || DEVELOPMENT_BUILD and that gate
    /// had never been exercised in a real player.
    ///
    /// What it asserts: the exe starts, boot reaches the menu, the menu loads the
    /// arena, and NOTHING logged an error or an exception on the way. It then
    /// quits with an exit code the build script can read.
    ///
    /// The timings below are `const`, not ScriptableObject fields. They are test
    /// harness timeouts, not game tuning — no balance decision reads them, and
    /// putting a CI timeout in the same asset as drone health would make that
    /// asset harder to reason about, not easier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildSmokeTest : MonoBehaviour
    {
        /// <summary>The flag that turns this on. Absent in normal play.</summary>
        public const string EnableArgument = "-codSmokeTest";

        /// <summary>Grepped by the build script. Changing it breaks the gate, so it is a const, once.</summary>
        public const string PassMarker = "COD_SMOKE_OK";
        public const string FailMarker = "COD_SMOKE_FAIL";

        private const float SceneTimeoutSeconds = 60f;
        private const float PlaySeconds = 6f;
        private const string MenuScene = "20_MainMenu";
        private const string GameScene = "10_GreyBox";

        [SerializeField] private bool _alsoLoadTheArena = true;

        private int _errorCount;

        private void Awake()
        {
            if (!HasEnableArgument())
            {
                // Not a smoke run. Remove the component entirely rather than
                // leaving a disabled MonoBehaviour ticking nothing.
                Destroy(this);
                return;
            }

            // Survive the scene loads it is about to watch. Not a static, so the
            // no-mutable-statics rule is untouched; this is one object marked
            // persistent, and only ever in a smoke run.
            DontDestroyOnLoad(gameObject);
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy() => Application.logMessageReceived -= OnLog;

        private void Start()
        {
            if (!HasEnableArgument()) return;
            StartCoroutine(RunSmokeTest());
        }

        private static bool HasEnableArgument()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], EnableArgument, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            // An Assert is a failed assumption and an Exception is a bug. Both
            // count. Warnings do not — a player build legitimately warns about
            // things the editor does not.
            if (type is LogType.Error or LogType.Exception or LogType.Assert) _errorCount++;
        }

        /// <summary>
        /// A coroutine: a method returning IEnumerator whose `yield return null`
        /// hands control back to Unity until the next frame. It is how you write
        /// "wait, then continue" without blocking the game loop — there is no
        /// thread here, and no async/await.
        /// </summary>
        private IEnumerator RunSmokeTest()
        {
            Debug.Log("Smoke test: waiting for " + MenuScene);
            yield return WaitForScene(MenuScene);

            if (_alsoLoadTheArena)
            {
                Debug.Log("Smoke test: loading " + GameScene);
                SceneManager.LoadScene(GameScene);
                yield return WaitForScene(GameScene);

                // Let a wave actually start. Real time, not scaled: this must not
                // stall forever if something leaves timeScale at zero.
                float until = Time.realtimeSinceStartup + PlaySeconds;
                while (Time.realtimeSinceStartup < until) yield return null;
            }

            Finish();
        }

        private IEnumerator WaitForScene(string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (SceneManager.GetActiveScene().name != sceneName)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError($"Smoke test: '{sceneName}' never became the active scene.");
                    Finish();
                    yield break;
                }
                yield return null;
            }
        }

        private void Finish()
        {
            if (_errorCount == 0)
            {
                Debug.Log(PassMarker + ": the built player booted, reached the menu and loaded the arena.");
                Application.Quit(0);
            }
            else
            {
                Debug.LogError($"{FailMarker}: {_errorCount} error(s) or exception(s) during the run.");
                Application.Quit(1);
            }
        }
    }
}
