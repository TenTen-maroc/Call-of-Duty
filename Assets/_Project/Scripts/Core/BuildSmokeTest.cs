#nullable enable
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
#endif

namespace CoD.Core
{
    /// <summary>
    /// Proves a BUILT player actually runs, and — in a development build —
    /// photographs it while it does. Dormant unless the executable is launched
    /// with <c>-codSmokeTest</c> or <c>-codScreenshots</c>, so a real player
    /// never notices it.
    ///
    /// WHY THIS EXISTS
    /// Every other gate in this project runs inside the editor. A build can pass
    /// all of them and still fail on its own: a scene missing from Build
    /// Settings, an editor-only API reached at runtime, a stripped assembly, a
    /// `#if UNITY_EDITOR` block that was holding something up. The cheat console
    /// in particular is gated on UNITY_EDITOR || DEVELOPMENT_BUILD and that gate
    /// had never been exercised in a real player.
    ///
    /// THE TWO ROUTES
    /// <c>-codSmokeTest</c> is the GATE. It runs headlessly under
    /// verify-build.mjs, asserts that the exe starts, that boot reaches the menu,
    /// that the menu loads the arena and that NOTHING logged an error or an
    /// exception on the way, then quits with an exit code the build script reads.
    /// It is deliberately NOT compiled out of release builds — gating it behind
    /// DEVELOPMENT_BUILD would mean the binary that actually ships could never be
    /// verified.
    ///
    /// <c>-codScreenshots</c> is the EYE. Everything -nographics cannot answer.
    /// A headless run does almost no GPU work and renders nothing, so until this
    /// existed the only way anyone found out what the game LOOKED like was to
    /// open Unity and press Play — which means nobody automated could review a
    /// HUD, a palette or a clipping bug. This route walks the same itinerary with
    /// a window open and writes a PNG at each beat. It IS compiled out of release
    /// builds, exactly like the cheat console: a shipped game has no business
    /// carrying a capture harness, and build.md promises the release binary has
    /// none of it.
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
        private const string OutdoorScene = "11_AtlasOutpost";
        private const string MissionTwoId = "mission_02_hard_contact";

        [SerializeField] private bool _alsoLoadTheArena = true;

        private int _errorCount;
        private bool _sceneTimedOut;
        private Route _route;

        /// <summary>
        /// Which harness, if any, this launch asked for.
        ///
        /// The screenshot member sits inside the same #if as the code that uses
        /// it. Leaving it visible in release would be harmless in bytes and
        /// dishonest in documentation: build.md claims the shipped binary
        /// contains no capture harness, and "no harness except the enum that
        /// names it" is the kind of claim that stops being true quietly.
        /// </summary>
        private enum Route
        {
            None,
            Smoke,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Screenshots,
#endif
        }

        private void Awake()
        {
            _route = ResolveRoute();
            if (_route == Route.None)
            {
                // Not a harness run. Remove the component entirely rather than
                // leaving a disabled MonoBehaviour ticking nothing.
                Destroy(this);
                return;
            }

            // Survive the scene loads it is about to watch. Not a static, so the
            // no-mutable-statics rule is untouched; this is one object marked
            // persistent, and only ever in a harness run.
            DontDestroyOnLoad(gameObject);
            Application.logMessageReceived += OnLog;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Before 00_Boot hands off to the menu: the menu reads the save on
            // its first frame, so anything this route wants the save to say has
            // to be on disk already.
            if (_route == Route.Screenshots) PrepareScreenshotRun();
#endif
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;

            // ALSO here, not only in FinishScreenshots.
            //
            // This harness rewrites the developer's REAL save — a development
            // build shares persistentDataPath with the release one — and used to
            // put it back only when the route ran to completion. Every other exit
            // left campaignSelected true in it: the caller's timeout (on Windows
            // that is TerminateProcess, so nothing else fires either), a crash, or
            // the window simply being closed. The developer's next real launch
            // would then open on a mission they never chose, and the next endless
            // screenshot pass would photograph the campaign.
            //
            // RestoreSave is idempotent through _saveRewritten, so running it
            // twice on the happy path costs nothing.
            //
            // GUARDED, because RestoreSave and the whole screenshot route live
            // inside this same directive and OnDestroy does not. Without the
            // guard the editor and the development build compile perfectly and
            // the RELEASE build does not -- which is the one configuration the
            // player ships in, and the reason verify-build exists at all.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RestoreSave();
#endif
        }

        private void Start()
        {
            if (_route == Route.Smoke) StartCoroutine(RunSmokeTest());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else if (_route == Route.Screenshots) StartCoroutine(RunScreenshotRoute());
#endif
        }

        /// <summary>
        /// Screenshots win when both flags are given: that route walks the same
        /// itinerary and counts the same errors, and it also renders. Resolved by
        /// scanning the whole command line rather than returning on the first hit,
        /// so the answer does not depend on argument ORDER — a caller that put
        /// -codSmokeTest first would otherwise get a blind run and four missing
        /// files, with nothing anywhere saying why.
        /// </summary>
        private static Route ResolveRoute()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (HasArgument(ScreenshotArgument)) return Route.Screenshots;
#endif
            return HasArgument(EnableArgument) ? Route.Smoke : Route.None;
        }

        private static bool HasArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase)) return true;
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
            if (_sceneTimedOut)
            {
                Finish();
                yield break;
            }

            if (_alsoLoadTheArena)
            {
                Debug.Log("Smoke test: loading " + GameScene);
                SceneManager.LoadScene(GameScene);
                yield return WaitForScene(GameScene);
                if (_sceneTimedOut)
                {
                    Finish();
                    yield break;
                }

                // Let a wave actually start. Real time, not scaled: this must not
                // stall forever if something leaves timeScale at zero.
                yield return WaitRealSeconds(PlaySeconds);

                Debug.Log("Smoke test: loading " + OutdoorScene);
                SceneManager.LoadScene(OutdoorScene);
                yield return WaitForScene(OutdoorScene);
                if (_sceneTimedOut)
                {
                    Finish();
                    yield break;
                }
                yield return WaitRealSeconds(PlaySeconds);
            }

            Finish();
        }

        /// <summary>
        /// Sets <see cref="_sceneTimedOut"/> rather than finishing the run itself.
        /// A coroutine cannot return a value and it cannot abort its caller, so
        /// the old form — LogError, Finish, yield break — quit the app and then
        /// let the caller carry on issuing scene loads into a process that was
        /// already ending, calling Finish a second time on the way out. The flag
        /// makes each caller decide, once.
        /// </summary>
        private IEnumerator WaitForScene(string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (SceneManager.GetActiveScene().name != sceneName)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError($"Harness: '{sceneName}' never became the active scene.");
                    _sceneTimedOut = true;
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Real time, never scaled. A wait built on Time.time stalls forever the
        /// moment anything leaves timeScale at zero — a pause menu, a slow-mo
        /// cheat, a death screen — and a harness that hangs looks exactly like a
        /// harness that is being thorough.
        /// </summary>
        private static IEnumerator WaitRealSeconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ------------------------------------------------------------------
        // The screenshot route. Development builds only, by the same rule that
        // strips the cheat console — see the class comment.
        // ------------------------------------------------------------------

        /// <summary>Turns the capture route on. Absent in normal play, and absent from release builds entirely.</summary>
        public const string ScreenshotArgument = "-codScreenshots";

        /// <summary>Where the PNGs go. Made absolute on arrival; see <see cref="ResolveShotDirectory"/>.</summary>
        public const string ScreenshotDirectoryArgument = "-codShotDirectory";

        /// <summary>Optional. A MissionConfig.stableId to photograph instead of the endless loop.</summary>
        public const string MissionArgument = "-codMission";

        /// <summary>One per frame written, scraped by screenshot.mjs. Format: marker, byte count, absolute path.</summary>
        public const string ShotMarker = "COD_SHOT";
        public const string ScreenshotPassMarker = "COD_SHOTS_OK";
        public const string ScreenshotFailMarker = "COD_SHOTS_FAIL";

        /// <summary>
        /// How many frames the route below takes. A mismatch fails the run rather
        /// than passing quietly: a capture that silently dropped a frame is the
        /// exact failure this whole file exists to make impossible, and the last
        /// place it should be tolerated is in the capture code itself. Add a
        /// Capture call, move this number.
        /// </summary>
        private const int DefaultExpectedShots = 4;
        private const int MissionTwoExpectedShots = 9;

        /// <summary>
        /// Frames to let a freshly activated scene draw before photographing it.
        /// Not a tuning value — a settling budget. A scene becoming ACTIVE is not
        /// the same as a scene having been DRAWN: capture on the activation frame
        /// and you photograph the previous scene, or a canvas whose layout pass
        /// has not run, which reads as a broken tool rather than a fast one.
        /// </summary>
        private const int SettleFrames = 5;

        /// <summary>Seconds after the arena appears before the HUD frame. Long enough for the objective list to populate.</summary>
        private const float HudSeconds = 2.5f;

        /// <summary>Seconds after THAT before the wave frame. The runner's countdown is ~4s, so this lands with drones in the air.</summary>
        private const float WaveSeconds = 7f;

        /// <summary>Sub-folder used when the caller names no directory. Absolute, because persistentDataPath is.</summary>
        private const string DefaultShotFolder = "Screenshots";

        // One instance, reused across every capture. WaitForEndOfFrame is a
        // yield instruction with no state; allocating a fresh one per frame is
        // the classic coroutine litter.
        private readonly WaitForEndOfFrame _endOfFrame = new();

        private string _shotDirectory = string.Empty;
        private int _shotCount;
        private string _screenshotMissionId = string.Empty;

        private bool _saveRewritten;
        private bool _savedCampaign;
        private string _savedMissionId = string.Empty;
        private GameMode _savedMode;

        private void PrepareScreenshotRun()
        {
            // PlayerSettings.runInBackground is OFF, deliberately — a game has no
            // business burning a laptop battery behind another window. It is also
            // fatal here: alt-tab away from a windowed player and Unity stops
            // ticking, the coroutine freezes mid-route, and the run dies on the
            // caller's timeout having written two frames and no explanation. The
            // harness turns it on for itself and for nobody else.
            Application.runInBackground = true;

            _shotDirectory = ResolveShotDirectory();
            try
            {
                Directory.CreateDirectory(_shotDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Screenshot run: cannot create '{_shotDirectory}': {exception.Message}");
            }

            // ALWAYS written, never inherited — including for the endless pass,
            // which passes no mission and used to leave the save untouched.
            //
            // The route loads the arena directly rather than through
            // MainMenuPanel.StartGame, and StartGame is the only thing that
            // normally writes the two axes. So on any machine whose save was not
            // pristine, the folder labelled "endless" would quietly contain
            // photographs of the CAMPAIGN (campaignSelected still true), or of a
            // Sandbox run with infinite money and the cheat console live
            // (lastMode still Sandbox). Nothing in the log, the console or the
            // filenames would say so, which makes it the worst class of bug a
            // LOOKING tool can have: it lies about what you are looking at.
            _screenshotMissionId = ArgumentValue(MissionArgument);
            PrepareSaveAxes(_screenshotMissionId);

            // The resolution the player ACTUALLY got, not the one that was asked
            // for. Windows clamps a window to the desktop, and a silently clamped
            // window changes the aspect ratio every FOV number here is tuned for.
            Debug.Log($"Screenshot run: {Screen.width}x{Screen.height} -> {_shotDirectory}");
        }

        /// <summary>
        /// Always absolute. Both ScreenCapture and File resolve a relative path
        /// against the PROCESS working directory, which for a launched player is
        /// wherever the caller happened to be standing — so the caller looks in
        /// the folder it named, finds nothing, and concludes the capture silently
        /// failed. It did not; the files are somewhere else.
        /// </summary>
        private static string ResolveShotDirectory()
        {
            string requested = ArgumentValue(ScreenshotDirectoryArgument);
            if (requested.Length > 0) return Path.GetFullPath(requested);
            return Path.Combine(Application.persistentDataPath, DefaultShotFolder);
        }

        /// <summary>The value after a flag, or empty. Missing trailing values return empty rather than throwing.</summary>
        private static string ArgumentValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
            }
            return string.Empty;
        }

        /// <summary>
        /// Points the run at a campaign mission by writing the two fields the
        /// mission-select screen writes.
        ///
        /// WHY NOT DRIVE THE MENU. There is no way to from here. CoD.Core
        /// references nothing — not CoD.UI, so MissionSelectPanel is not a type
        /// this file can name, and not the Input System, so there is no synthetic
        /// ENTER either. The save file is the only sanctioned channel between a
        /// menu and the scene it loads (SettingsHub.SetCampaign says why at
        /// length), and writing the same two axes is a substitute MissionDirector
        /// genuinely cannot tell apart — which is the whole point.
        ///
        /// Both axes, exactly as MissionSelectPanel.Launch does. Campaign says
        /// WHICH CONTENT; the mode says which RULES, and a mission plays by Run
        /// rules. A pass that inherited lastMode: Sandbox would photograph a HUD
        /// with infinite money in it and nothing would look wrong.
        /// </summary>
        private void PrepareSaveAxes(string missionId)
        {
            SaveData save = SaveSystem.Load();
            _savedCampaign = save.campaignSelected;
            _savedMissionId = save.selectedMissionId;
            _savedMode = save.lastMode;
            _saveRewritten = true;

            bool campaign = missionId.Length > 0;
            save.campaignSelected = campaign;
            save.selectedMissionId = missionId;
            // Run rules for both passes. A campaign mission plays by Run rules
            // too, so this is the correct value either way.
            save.lastMode = GameMode.Run;
            SaveSystem.Save(save);
            Debug.Log(campaign
                ? "Screenshot run: campaign mission '" + missionId + "'."
                : "Screenshot run: endless, campaign axis explicitly cleared.");
        }

        /// <summary>
        /// Puts the save back the way it was found. A tool for LOOKING at the
        /// game does not get to change it: leaving campaignSelected true means
        /// the next real launch opens on a mission the player never chose, with
        /// nothing on screen explaining why.
        ///
        /// Re-loaded rather than re-saved from the boot-time copy, because
        /// anything in the run may legitimately have written the file since, and
        /// stamping a ten-second-old snapshot over it would undo a real change.
        /// Only the three fields this harness moved are put back.
        /// </summary>
        private void RestoreSave()
        {
            if (!_saveRewritten) return;
            _saveRewritten = false;

            SaveData save = SaveSystem.Load();
            save.campaignSelected = _savedCampaign;
            save.selectedMissionId = _savedMissionId;
            save.lastMode = _savedMode;
            SaveSystem.Save(save);
        }

        /// <summary>
        /// The itinerary. Four beats: the front door, the arena the instant it
        /// exists, the HUD once it has something to say, and the arena with a
        /// wave in it.
        ///
        /// The arena is loaded unconditionally rather than through
        /// <see cref="_alsoLoadTheArena"/>. That field is the SMOKE TEST's
        /// switch; three of the four frames here are the arena, so honouring it
        /// would mean a scene-authoring change silently reducing this route to a
        /// single photograph of a menu.
        /// </summary>
        private IEnumerator RunScreenshotRoute()
        {
            Debug.Log("Screenshot run: waiting for " + MenuScene);
            yield return WaitForScene(MenuScene);
            if (_sceneTimedOut)
            {
                FinishScreenshots();
                yield break;
            }

            if (IsMissionTwoCapture) ShowMissionSelectionForCapture();
            yield return Settle();
            yield return Capture(IsMissionTwoCapture ? "01-mission-selection" : "01-main-menu");

            string targetScene = IsMissionTwoCapture ? OutdoorScene : GameScene;
            Debug.Log("Screenshot run: loading " + targetScene);
            SceneManager.LoadScene(targetScene);
            yield return WaitForScene(targetScene);
            if (_sceneTimedOut)
            {
                FinishScreenshots();
                yield break;
            }

            yield return Settle();
            yield return Capture(IsMissionTwoCapture ? "02-outdoor-establishing" : "02-arena-loaded");

            if (IsMissionTwoCapture)
            {
                yield return WaitRealSeconds(4f);
                yield return Capture("03-first-meridian-contact");
                yield return WaitRealSeconds(6f);
                yield return Capture("04-soldier-firing-from-cover");
                yield return WaitRealSeconds(3f);
                yield return Capture("05-regional-impact-readability");
                yield return WaitRealSeconds(4f);
                yield return Capture("06-extreme-aftermath");
                yield return WaitRealSeconds(4f);
                yield return Capture("07-reduced-gore");
                yield return WaitRealSeconds(4f);
                yield return Capture("08-gore-off");
                yield return WaitRealSeconds(1f);
                yield return Capture("09-outdoor-combat-hud");
                FinishScreenshots();
                yield break;
            }

            yield return WaitRealSeconds(HudSeconds);
            yield return Capture("03-arena-hud");

            yield return WaitRealSeconds(WaveSeconds);
            yield return Capture("04-arena-wave");

            FinishScreenshots();
        }

        private bool IsMissionTwoCapture =>
            string.Equals(_screenshotMissionId, MissionTwoId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Opens the real mission-select panel for the first Mission 2 frame.
        /// CoD.Core deliberately has no reference to CoD.UI, so the development
        /// harness locates that one component by name and invokes its public
        /// Open method reflectively. It does not use this to launch the mission;
        /// the save-axis route below remains the authoritative scene channel.
        /// </summary>
        private static void ShowMissionSelectionForCapture()
        {
            GameObject canvas = GameObject.Find("MenuCanvas");
            if (canvas == null)
            {
                Debug.LogError("Screenshot run: MenuCanvas was not found for the mission-selection frame.");
                return;
            }

            MonoBehaviour[] components = canvas.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component == null || component.GetType().FullName != "CoD.UI.MissionSelectPanel") continue;
                Type panelType = component.GetType();
                panelType.GetMethod("Open")?.Invoke(component, null);
                object? cursor = panelType.GetField("_cursor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(component);
                cursor?.GetType().GetMethod("SetIndex")?.Invoke(cursor, new object[] { 1 });
                panelType.GetMethod("Redraw",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(component, null);
                return;
            }

            Debug.LogError("Screenshot run: MissionSelectPanel was not found on MenuCanvas.");
        }

        private IEnumerator Settle()
        {
            for (int i = 0; i < SettleFrames; i++) yield return null;
        }

        /// <summary>
        /// END of frame, not any old frame. The capture reads the back buffer,
        /// and part-way through a frame that buffer holds a half-drawn image or
        /// the previous frame entirely. Screen Space Overlay canvases — every HUD
        /// in this game — are drawn last of all, so a capture taken any earlier
        /// is a screenshot with no UI in it.
        /// </summary>
        private IEnumerator Capture(string label)
        {
            yield return _endOfFrame;
            WriteFrame(label);
        }

        /// <summary>
        /// CaptureScreenshotAsTexture plus an explicit write, NOT
        /// ScreenCapture.CaptureScreenshot(path).
        ///
        /// The convenient one is fire-and-forget: it hands the encode and the
        /// write to the end of the frame and returns immediately, so a Quit on
        /// the next line leaves a zero-byte file, or no file at all, and nothing
        /// anywhere reports it. This form hands back the pixels, which makes the
        /// encode and the write ours and makes the byte count logged below PROOF
        /// that a frame exists rather than a hope that one will.
        /// </summary>
        private void WriteFrame(string label)
        {
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                byte[] png = frame.EncodeToPNG();
                string path = Path.Combine(_shotDirectory, label + ".png");
                File.WriteAllBytes(path, png);
                _shotCount++;
                // Absolute path AND size, on one line, so a caller scraping the
                // log can open the file and tell an empty frame from a real one
                // without a second round trip.
                Debug.Log($"{ShotMarker} {png.Length} {path}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"{ShotMarker} failed for '{label}': {exception.Message}");
            }
            finally
            {
                // A native allocation, not a managed one. Four leaked textures
                // would cost nothing, and a harness is still the wrong place to
                // teach the habit.
                Destroy(frame);
            }
        }

        private void FinishScreenshots()
        {
            RestoreSave();

            int expected = IsMissionTwoCapture ? MissionTwoExpectedShots : DefaultExpectedShots;
            if (_errorCount == 0 && _shotCount == expected)
            {
                Debug.Log($"{ScreenshotPassMarker}: {_shotCount} frame(s) written to {_shotDirectory}");
                Application.Quit(0);
            }
            else
            {
                Debug.LogError($"{ScreenshotFailMarker}: {_shotCount}/{expected} frame(s), " +
                               $"{_errorCount} error(s) or exception(s) during the run.");
                Application.Quit(1);
            }
        }
#endif
    }
}
