#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>
    /// Produces the Windows executable. Menu: CoD → Build Windows Player, or
    /// headlessly with
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoD.EditorTools.GameBuilder.BuildWindowsHeadless
    ///
    /// Modelled on GreyBoxBuilder for the same reason: a build produced by
    /// clicking through a dialog cannot be reproduced, reviewed or explained, and
    /// "it worked on my machine last Tuesday" is not a release process.
    ///
    /// The scene list comes from EditorBuildSettings, which GreyBoxBuilder.
    /// RegisterScenes owns. There is exactly one place scenes are declared.
    /// </summary>
    public static class GameBuilder
    {
        /// <summary>Gitignored and covered by guard-no-build-artifacts. Nothing here is ever committed.</summary>
        private const string BuildRoot = "Build";
        private const string ExecutableName = "CallOfDuty.exe";

        [MenuItem("CoD/Build Windows Player", false, 20)]
        public static void BuildWindows() => Build(development: false);

        [MenuItem("CoD/Build Windows Player (Development)", false, 21)]
        public static void BuildWindowsDevelopment() => Build(development: true);

        /// <summary>-executeMethod entry point. Non-zero exit on any failure.</summary>
        public static void BuildWindowsHeadless() => RunHeadless(development: false);

        /// <summary>
        /// The development build. This is the one that exercises the
        /// UNITY_EDITOR || DEVELOPMENT_BUILD gate on the cheat console in a real
        /// player, which no editor test can do.
        /// </summary>
        public static void BuildWindowsDevelopmentHeadless() => RunHeadless(development: true);

        private static void RunHeadless(bool development)
        {
            try
            {
                bool ok = Build(development);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Player build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static string OutputDirectory(bool development)
            => Path.Combine(BuildRoot, development ? "Windows-Dev" : "Windows");

        public static string ExecutablePath(bool development)
            => Path.Combine(OutputDirectory(development), ExecutableName);

        private static bool Build(bool development)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("No scenes are enabled in Build Settings. Run CoD → Build Grey Box first.");
                return false;
            }

            // Index 0 is what the player loads on launch. Getting this wrong ships
            // a game that opens mid-run with no menu, and nothing in the editor
            // would ever show it.
            if (!scenes[0].EndsWith("00_Boot.unity"))
            {
                Debug.LogError($"Build Settings scene 0 is '{scenes[0]}', not 00_Boot. A player would start there.");
                return false;
            }

            ApplyPlayerSettings();

            string directory = OutputDirectory(development);
            Directory.CreateDirectory(directory);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ExecutablePath(development),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Player build {summary.result}: {summary.totalErrors} error(s). " +
                               $"Output: {options.locationPathName}");
                return false;
            }

            Debug.Log($"Player build succeeded ({(development ? "development" : "release")}): " +
                      $"{options.locationPathName}, {summary.totalSize / (1024 * 1024)} MB, " +
                      $"{summary.totalTime.TotalSeconds:0} s, {scenes.Length} scenes.");
            return true;
        }

        private static string[] EnabledScenes()
        {
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled) count++;
            }

            string[] paths = new string[count];
            int next = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled) paths[next++] = all[i].path;
            }
            return paths;
        }

        /// <summary>
        /// The handful of player settings that would otherwise be wrong by
        /// default. Set here rather than clicked once in the Inspector, so a fresh
        /// clone builds the same executable.
        ///
        /// Scripting backend is deliberately left alone (Mono). IL2CPP is the
        /// better shipping answer — faster, harder to decompile — but it needs the
        /// Windows IL2CPP module installed and turns a 30-second build into
        /// several minutes. Reversible in one line when there is something to
        /// ship rather than something to test.
        /// </summary>
        private static void ApplyPlayerSettings()
        {
            // 1024x768 is Unity's default and is a 4:3 resolution in 2026. The FOV
            // note in GameConfig assumes 16:9, and a 4:3 default would make every
            // tuned FOV number wrong on first launch.
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            // Borderless fullscreen: alt-tabs cleanly, which exclusive fullscreen
            // does not, and this game has a pause menu people will alt-tab out of.
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = false;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "ma.tenten.callofduty");
        }
    }
}
