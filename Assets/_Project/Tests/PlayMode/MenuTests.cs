#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Player;
using CoD.UI;
using CoD.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The main menu, actually loading. Before this scene existed, 00_Boot went
    /// straight into a run and there was no way to pick a mode, change a setting
    /// or leave.
    /// </summary>
    public sealed class MainMenuTests
    {
        [UnitySetUp]
        public IEnumerator LoadMenu()
        {
            AsyncOperation? load = SceneManager.LoadSceneAsync("20_MainMenu", LoadSceneMode.Single);
            Assert.IsNotNull(load, "'20_MainMenu' must be in the build settings — RegisterScenes puts it there");
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator MenuScene_HasItsPanelsAndASettingsHub()
        {
            Assert.IsNotNull(Object.FindFirstObjectByType<MainMenuPanel>(), "no MainMenuPanel");
            Assert.IsNotNull(Object.FindFirstObjectByType<SettingsPanel>(), "no SettingsPanel");
            Assert.IsNotNull(Object.FindFirstObjectByType<SettingsHub>(), "no SettingsHub — settings would not load");
            // Without a camera Unity renders nothing and logs every frame; without
            // a listener the same for audio. Both are cheap to forget in a
            // generated scene and loud once you do.
            Assert.IsNotNull(Object.FindFirstObjectByType<Camera>(), "no camera in the menu scene");
            Assert.IsNotNull(Object.FindFirstObjectByType<AudioListener>(), "no audio listener in the menu scene");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MenuScene_RunsAtNormalSpeed()
        {
            // Quitting to the menu from a pause restores the clock first, but if
            // any path ever forgets, the menu is a screen that ignores every key
            // and looks exactly like a hang.
            Assert.AreEqual(1f, Time.timeScale, 1e-5f, "the menu must never be time-frozen");
            // Cursor.lockState is deliberately NOT asserted anywhere in this
            // suite. A -batchmode run has no window, so the platform never
            // honours the request and the field reads None whatever the code
            // did — the assertion would test Unity's headless mode, pass for the
            // wrong reason in one place and fail for the wrong reason in
            // another. Cursor handling is verified by playing the game.
            yield return null;
        }

        [UnityTest]
        public IEnumerator SettingsHub_ResolvesWithinItsBounds()
        {
            var hub = Object.FindFirstObjectByType<SettingsHub>();
            Assert.IsNotNull(hub);

            GameSettings settings = hub!.Current;
            // Resolve must produce something playable even on a machine with no
            // save file at all — the first-launch path.
            Assert.Greater(settings.MouseSensitivity, 0f, "a sensitivity of zero is a camera that cannot turn");
            Assert.Greater(settings.FovVertical, 0f);
            Assert.AreEqual(settings.MasterVolume, AudioListener.volume, 1e-4f,
                "Awake must have applied the volume — this is the value that used to be dead data");
            yield return null;
        }
    }

    /// <summary>
    /// Pause, in the real grey box. The state machine is the risky half — key
    /// polling is three lines and cannot be subtly wrong the way timeScale
    /// bookkeeping can.
    /// </summary>
    public sealed class PauseTests
    {
        [UnitySetUp]
        public IEnumerator LoadGreyBox()
        {
            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load);
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator AlwaysUnpause()
        {
            // A test that leaves timeScale at 0 hangs every test after it.
            var pause = Object.FindFirstObjectByType<PausePanel>();
            if (pause != null) pause.Resume();
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pause_StopsTheClock_AndBlocksInput()
        {
            var pause = Object.FindFirstObjectByType<PausePanel>();
            var input = Object.FindFirstObjectByType<PlayerInput>();
            Assert.IsNotNull(pause, "the grey box has no PausePanel");
            Assert.IsNotNull(input);

            Assert.IsFalse(pause!.IsPaused);

            pause.Pause();
            yield return null;

            Assert.IsTrue(pause.IsPaused);
            Assert.IsTrue(input!.IsBlocked, "the action map must be switched off");
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "pause has to stop the world");
            Assert.AreEqual(Vector2.zero, input.Move,
                "a blocked map must report no input, or the player walks around behind the panel");
            Assert.AreEqual(Vector2.zero, input.Look);
            Assert.IsFalse(input.FireHeld);

            pause.Resume();
            yield return null;

            Assert.IsFalse(pause.IsPaused);
            Assert.AreEqual(1f, Time.timeScale, 1e-6f);
            Assert.IsFalse(input.IsBlocked, "the action map must be back on after a resume");
        }

        [UnityTest]
        public IEnumerator Pause_RestoresWhateverTimeScaleItFound_NotOne()
        {
            var pause = Object.FindFirstObjectByType<PausePanel>();
            Assert.IsNotNull(pause);

            // Stand in for the sandbox console's slow-mo. Resuming to a hard 1
            // would silently cancel a cheat the player turned on.
            Time.timeScale = 0.35f;
            pause!.Pause();
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, 1e-6f);

            pause.Resume();
            yield return null;
            Assert.AreEqual(0.35f, Time.timeScale, 1e-5f, "pause must give the clock back exactly as it found it");

            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Pause_IsRefused_OnceTheRunIsOver()
        {
            var pause = Object.FindFirstObjectByType<PausePanel>();
            var runner = Object.FindFirstObjectByType<WaveRunner>();
            var health = Object.FindFirstObjectByType<PlayerMotor>()!.GetComponent<Health>();
            Assert.IsNotNull(pause);
            Assert.IsNotNull(runner);
            Assert.IsNotNull(health);

            var lethal = new DamageInfo(99999f, health!.transform.position, Vector3.up, Vector3.forward, false);
            health.ApplyDamage(in lethal);
            yield return null;

            Assert.AreEqual(RunPhase.GameOver, runner!.Phase, "the player should be dead");

            // The panel refuses through its own key path; Pause() is the manual
            // door and is deliberately still open, so drive the guard directly.
            Assert.IsFalse(pause!.IsPaused,
                "nothing should have paused itself when the run ended");
            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "the death screen runs at normal speed");
        }

        [UnityTest]
        public IEnumerator SettingsChange_ReachesTheCamera()
        {
            var hub = Object.FindFirstObjectByType<SettingsHub>();
            var look = Object.FindFirstObjectByType<PlayerLook>();
            Assert.IsNotNull(hub, "the grey box has no SettingsHub");
            Assert.IsNotNull(look);

            float original = hub!.Current.FovVertical;
            float wanted = Mathf.Approximately(original, 70f) ? 60f : 70f;

            hub.Current.SetFovVertical(wanted);
            hub.Apply();
            yield return null;

            // The whole point of the milestone: a saved number that changes what
            // you see. BaseFov is what the weapon computes ADS against, so this
            // also proves the aim path picked it up.
            Assert.AreEqual(hub.Current.FovVertical, look!.BaseFov, 1e-4f,
                "PlayerLook must be driven by the settings, not by GameConfig");

            hub.Current.SetFovVertical(original);
            hub.Apply();
            yield return null;
        }
    }
}
