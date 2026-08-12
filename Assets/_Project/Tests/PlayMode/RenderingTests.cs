#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// Proves the image pipeline is actually ON, in both scenes.
    ///
    /// This suite exists because the project ran for its whole life with it OFF
    /// and nothing failed. The camera had no UniversalAdditionalCameraData, so URP
    /// left renderPostProcessing false — and the emissive drone cores, plus the
    /// emission ramp DroneController.SetTelegraph drives through every attack
    /// windup, clipped flat instead of glowing. Everything compiled, every guard
    /// passed, all 84 tests were green, and the game rendered with no tonemapping,
    /// no bloom and no anti-aliasing at all.
    ///
    /// A missing component is invisible to every other gate in this repo. That is
    /// what these assertions are for.
    /// </summary>
    public sealed class RenderingTests
    {
        private static IEnumerator Load(string scene)
        {
            AsyncOperation? load = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            Assert.IsNotNull(load, $"'{scene}' must be in the build settings — RegisterScenes puts it there");
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        private static void AssertPostProcessingIsLive(string scene)
        {
            Camera? camera = Camera.main;
            Assert.IsNotNull(camera, $"{scene}: no camera tagged MainCamera");

            var data = camera!.GetComponent<UniversalAdditionalCameraData>();
            Assert.IsNotNull(data,
                $"{scene}: the camera has no UniversalAdditionalCameraData, so URP renders it with post-processing off");
            Assert.IsTrue(data!.renderPostProcessing,
                $"{scene}: post-processing is off — nothing resolves the emissive cores or the attack telegraph");

            Volume? volume = Object.FindFirstObjectByType<Volume>();
            Assert.IsNotNull(volume, $"{scene}: no Volume, so there is no profile to render");
            Assert.IsTrue(volume!.isGlobal,
                $"{scene}: the Volume is not global, so it would only apply inside a collider");
            Assert.IsNotNull(volume.sharedProfile, $"{scene}: the Volume has no profile assigned");
        }

        [UnityTest]
        public IEnumerator Arena_RendersWithPostProcessing()
        {
            yield return Load("10_GreyBox");
            AssertPostProcessingIsLive("10_GreyBox");
        }

        [UnityTest]
        public IEnumerator Menu_RendersWithPostProcessing()
        {
            yield return Load("20_MainMenu");
            AssertPostProcessingIsLive("20_MainMenu");
        }

        /// <summary>
        /// The arena is lit by more than the sun, and none of the decorative trim
        /// carries a collider.
        ///
        /// The collider half is the one that bites: BakeNavMesh collects from
        /// PhysicsColliders, so a trim strip built with the collider
        /// CreatePrimitive hands out would carve a floating obstacle into the
        /// drone navmesh. That shows up as drones pathing around thin air, which
        /// reads as broken AI rather than as a build mistake.
        /// </summary>
        [UnityTest]
        public IEnumerator Arena_IsLit_AndItsTrimIsNonSolid()
        {
            yield return Load("10_GreyBox");

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            int pointLights = 0;
            foreach (Light light in lights)
            {
                if (!light.enabled || light.type != LightType.Point) continue;
                // The muzzle light ships disabled and lives under the viewmodel.
                if (light.GetComponentInParent<Camera>() != null) continue;
                pointLights++;
            }
            Assert.GreaterOrEqual(pointLights, 4,
                "the arena lane lights are missing — every lane would look identical");

            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional) continue;
                Assert.AreEqual(LightShadows.None, light.shadows,
                    $"'{light.name}' casts shadows; the sun is meant to be the only caster");
            }

            int trims = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("Trim_")) continue;
                trims++;
                Assert.IsNull(t.GetComponent<Collider>(),
                    $"'{t.name}' has a collider and would be baked into the navmesh as an obstacle");
            }
            Assert.Greater(trims, 0, "no edge trim was built");
        }

        /// <summary>
        /// The settings actually REACH the camera.
        ///
        /// CameraGraphics holds a serialized SettingsHub reference, and a null one
        /// is silent: the component sits there, the menu row moves, and nothing
        /// changes on screen. Driving the hub and reading the camera back is the
        /// only way to prove the link survived the scene build.
        /// </summary>
        [UnityTest]
        public IEnumerator GraphicsSettings_ReachTheCamera()
        {
            yield return Load("10_GreyBox");

            var graphics = Object.FindFirstObjectByType<CameraGraphics>();
            Assert.IsNotNull(graphics, "no CameraGraphics — the graphics settings would do nothing");

            SettingsHub? hub = Object.FindFirstObjectByType<SettingsHub>();
            Assert.IsNotNull(hub);
            var data = Camera.main!.GetComponent<UniversalAdditionalCameraData>();
            Assert.IsNotNull(data);

            hub!.Current.SetPostProcessing(false);
            hub.Current.SetAntiAliasing(AntiAliasingMode.Off);
            hub.Apply();
            yield return null;

            Assert.IsFalse(data!.renderPostProcessing, "turning post-processing off did not reach the camera");
            Assert.AreEqual(AntialiasingMode.None, data.antialiasing);

            hub.Current.SetPostProcessing(true);
            hub.Current.SetAntiAliasing(AntiAliasingMode.Smaa);
            hub.Apply();
            yield return null;

            Assert.IsTrue(data.renderPostProcessing, "turning it back on did not reach the camera either");
            Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, data.antialiasing);
        }

        /// <summary>
        /// The profile's overrides survived being written to disk.
        ///
        /// VolumeProfile.Add only puts a component in an in-memory list; persisting
        /// it needs AddObjectToAsset. Miss that and the profile saves referencing
        /// objects that were never written — which looks like an empty profile in
        /// the Inspector and renders as no post-processing at all. TryGet failing
        /// here is exactly that bug.
        /// </summary>
        [UnityTest]
        public IEnumerator ArenaProfile_KeptItsOverrides_ThroughTheSave()
        {
            yield return Load("10_GreyBox");

            Volume? volume = Object.FindFirstObjectByType<Volume>();
            Assert.IsNotNull(volume);
            VolumeProfile? profile = volume!.sharedProfile;
            Assert.IsNotNull(profile);

            Assert.IsTrue(profile!.TryGet(out Bloom bloom),
                "no Bloom in the profile — the emissive cores would not glow");
            Assert.IsTrue(bloom.intensity.overrideState,
                "Bloom intensity is not overridden, so the stack ignores the value entirely");
            Assert.Greater(bloom.intensity.value, 0f, "Bloom is present but contributes nothing");

            Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping),
                "no Tonemapping — every HDR value above 1.0 would clip flat");
            Assert.IsTrue(tonemapping.mode.overrideState, "the tonemapper is not overridden");

            Assert.IsTrue(profile.TryGet(out Vignette _), "no Vignette");
            Assert.IsTrue(profile.TryGet(out ColorAdjustments _), "no ColorAdjustments");

            // The grade. Every one of these folds into the HDR grading LUT and so
            // costs nothing at runtime — which is exactly why a missing one is
            // invisible. AddOverride has to call AssetDatabase.AddObjectToAsset or
            // the profile saves referencing objects that were never written, and
            // the failure mode is an empty profile in the Inspector and no post
            // at all, with every other gate still green.
            Assert.IsTrue(profile.TryGet(out ShadowsMidtonesHighlights split),
                "no ShadowsMidtonesHighlights — the cool-shadow/warm-highlight split is the grade");
            Assert.IsTrue(split.shadows.overrideState, "the shadow tint is not overridden, so it does nothing");

            Assert.IsTrue(profile.TryGet(out WhiteBalance balance), "no WhiteBalance");
            Assert.IsTrue(balance.temperature.overrideState, "white balance is not overridden");

            Assert.IsTrue(profile.TryGet(out ChromaticAberration aberration), "no ChromaticAberration");
            Assert.IsTrue(aberration.intensity.overrideState, "chromatic aberration is not overridden");

            // Motion blur is refused on purpose, not merely absent: URP's is
            // camera-only, so a fast mouse turn smears the entire screen and
            // hides the drone that is about to reach you. It shipped dormant
            // inside the template profile that used to sit under this stack, one
            // Inspector click from live. If this assertion ever fails, somebody
            // added it back — read the comment in ApplyPostFxDefaults first.
            Assert.IsFalse(profile.TryGet(out MotionBlur _),
                "MotionBlur is in the arena profile — it is refused deliberately; see ApplyPostFxDefaults");
        }
    }
}
