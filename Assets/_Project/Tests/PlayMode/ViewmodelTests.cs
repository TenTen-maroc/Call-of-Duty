#nullable enable
using System.Collections;
using CoD.Core;
using CoD.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoD.Tests
{
    /// <summary>
    /// The gun is drawn by its OWN camera, and the world camera cannot see it.
    ///
    /// WHY THIS SUITE EXISTS
    /// For the whole life of the project there was exactly one camera, and the
    /// WeaponRig hung off it. That produced the two most obvious "amateur" tells
    /// a first-person game can have, and neither of them fails a compile, a
    /// guard, a headless build or any other test in this repo:
    ///
    ///   1. The barrel tip sits 0.53 m in front of the lens and the near clip is
    ///      0.05 m, so walking into a wall put the gun inside it.
    ///   2. PlayerLook lerped the camera's FOV for the sprint bonus and the
    ///      ADS/fire-kick offset, and a child of that camera is re-projected with
    ///      it — the model stretched on every sprint and every shot.
    ///
    /// The fix is a URP overlay camera on a dedicated layer. Every assertion here
    /// is a piece of that arrangement that is silent when it breaks: a culling
    /// mask bit, a stack entry, a layer on eight cubes. The one that bites
    /// hardest is the audio listener — the natural way to build a second camera
    /// is to duplicate the first, and a duplicated AudioListener is a permanent
    /// console warning and undefined 3D audio.
    ///
    /// EVERY TEST IN THIS FILE FAILS AGAINST THE PRE-SPLIT SCENE, and that is the
    /// bar each one is written to: a test that would pass either way is not a
    /// gate, it is a comment with a green tick. Checked against the committed
    /// 10_GreyBox.unity, which still has a camera culling mask of 0xFFFFFFFF, an
    /// empty cameraStack, a WeaponRig on Default, and an Fx_MuzzleFlash.prefab
    /// with no Light in it at all. Which also means this suite is RED until
    /// `CoD → Build Grey Box` regenerates the scenes and the prefabs — being red
    /// for that reason is the suite working, not the suite broken.
    /// </summary>
    public sealed class ViewmodelTests
    {
        private readonly SaveFileGuard _save = new();

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            // The FOV test drives the player's saved settings, so it starts from
            // the known save rather than from whatever the last human left behind.
            _save.CaptureAndReset();
            AsyncOperation? load = SceneManager.LoadSceneAsync("10_GreyBox", LoadSceneMode.Single);
            Assert.IsNotNull(load, "'10_GreyBox' must be in the build settings — RegisterScenes puts it there");
            while (load != null && !load.isDone) yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreTheSave()
        {
            _save.Restore();
            yield return null;
        }

        private static int ViewmodelLayer()
        {
            int layer = LayerMask.NameToLayer("Viewmodel");
            // NameToLayer returns -1 for a layer that does not exist, and Unity
            // assigns -1 without complaining. Everything below would then be
            // comparing against a mask of all ones, so this has to be the first
            // thing asserted rather than the cause of six confusing failures.
            Assert.GreaterOrEqual(layer, 0,
                "no 'Viewmodel' layer in ProjectSettings/TagManager.asset — the whole split is inert");
            return layer;
        }

        private static Camera BaseCamera()
        {
            Camera? camera = Camera.main;
            Assert.IsNotNull(camera, "no camera tagged MainCamera in the arena");
            return camera!;
        }

        private static Camera OverlayCamera()
        {
            var data = BaseCamera().GetComponent<UniversalAdditionalCameraData>();
            Assert.IsNotNull(data, "the world camera has no UniversalAdditionalCameraData, so it has no stack at all");
            Assert.AreEqual(1, data!.cameraStack.Count,
                "the world camera's stack must hold exactly one overlay — the viewmodel camera");
            Camera? overlay = data.cameraStack[0];
            Assert.IsNotNull(overlay, "the stack entry is null; the overlay camera was destroyed or never wired");
            return overlay!;
        }

        [UnityTest]
        public IEnumerator WorldCamera_CannotSeeTheViewmodelLayer()
        {
            int layer = ViewmodelLayer();
            Camera world = BaseCamera();

            Assert.AreEqual(0, world.cullingMask & (1 << layer),
                "the world camera still renders the Viewmodel layer — the gun would be drawn twice, " +
                "and the copy the world draws is the one that clips through walls");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OverlayCamera_IsAnOverlay_AndOwnsNothingItShouldNot()
        {
            int layer = ViewmodelLayer();
            Camera world = BaseCamera();
            Camera overlay = OverlayCamera();

            var data = overlay.GetComponent<UniversalAdditionalCameraData>();
            Assert.IsNotNull(data, "the viewmodel camera has no UniversalAdditionalCameraData");
            Assert.AreEqual(CameraRenderType.Overlay, data!.renderType,
                "the viewmodel camera is a Base camera sitting in a stack — URP rejects that and logs an error");

            Assert.AreEqual(1 << layer, overlay.cullingMask,
                "the viewmodel camera draws something other than the Viewmodel layer, so it redraws the world");

            // Camera.main is how RenderingTests finds the graded camera and how
            // anything else in Unity finds "the" camera. Two MainCamera tags is a
            // coin toss that changes between runs.
            Assert.IsFalse(overlay.CompareTag("MainCamera"),
                "the viewmodel camera is tagged MainCamera — Camera.main becomes undefined");

            Assert.IsNull(overlay.GetComponent<AudioListener>(),
                "the viewmodel camera has an AudioListener; two listeners warn every frame and break 3D audio");

            // The anti-clipping half of the fix. The world's near plane stays
            // where it is for the world; the gun gets one in front of its muzzle.
            Assert.Less(overlay.nearClipPlane, world.nearClipPlane,
                "the viewmodel camera's near plane is no closer than the world's, so the barrel still clips");
            yield return null;
        }

        /// <summary>
        /// One listener, and it is on the WORLD camera.
        ///
        /// The count alone was true before the split too, so on its own it gated
        /// nothing: it passed identically against the one-camera scene this suite
        /// exists to replace. Resolving the overlay first makes it a real gate,
        /// and naming which camera carries the listener is what the count was
        /// really trying to say — 3D audio is positioned by the listener's
        /// transform, and the overlay is the camera whose projection is a lie.
        /// </summary>
        [UnityTest]
        public IEnumerator TheArena_HasExactlyOneAudioListener()
        {
            Camera world = BaseCamera();
            OverlayCamera();

            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            Assert.AreEqual(1, listeners.Length,
                "the arena must have exactly one active AudioListener; a second camera is the easiest way to gain one");
            Assert.AreSame(world.gameObject, listeners[0].gameObject,
                "the arena's one AudioListener is not on the world camera, so every 3D sound in the game is " +
                "positioned by whatever object ended up with it");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryPartOfTheGun_IsOnTheViewmodelLayer()
        {
            int layer = ViewmodelLayer();
            Camera overlay = OverlayCamera();

            // Found by component, not by name: WeaponSway is what makes this
            // object the weapon rig, and a rename would otherwise pass silently.
            var sway = Object.FindFirstObjectByType<WeaponSway>();
            Assert.IsNotNull(sway, "no WeaponSway — there is no weapon rig in the scene at all");

            Transform rig = sway!.transform;
            Assert.IsTrue(rig.IsChildOf(overlay.transform),
                "the WeaponRig is not parented under the viewmodel camera, so it moves with the world projection");

            int renderers = 0;
            foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>(true))
            {
                renderers++;
                Assert.AreEqual(layer, renderer.gameObject.layer,
                    $"'{renderer.name}' is on layer {renderer.gameObject.layer}, not Viewmodel — " +
                    "layers do not inherit, so one missed child is invisible until you look for it");
            }
            Assert.Greater(renderers, 0, "the weapon rig has no renderers; there is no gun on screen");
            yield return null;
        }

        /// <summary>
        /// The muzzle flash lights the ROOM and the GUN, which takes two lights.
        ///
        /// A camera's culling mask culls LIGHTS by their GameObject's layer, not
        /// just renderers, and these two cameras have disjoint masks — so no
        /// single light can reach both. Splitting the rig onto its own layer
        /// therefore silently cost the gun its muzzle flash: MuzzleLight stayed on
        /// Default, which is right (the room and the drones are the whole reason
        /// it exists) and which makes it invisible to the only camera that draws
        /// the weapon. On the most repeated visual event in the game.
        ///
        /// The gun's half is a point light on the Fx_MuzzleFlash prefab, spawned
        /// and despawned by the pool alongside the flash sprite.
        ///
        /// DELIBERATELY NOT A COUNT. The first version of this pinned "exactly one
        /// light under the rig", which does not test the arrangement — it forbids
        /// the fix. What matters is that both halves exist and that neither sits
        /// on a layer the camera that needs it cannot see.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMuzzleFlash_LightsTheRoom_AndTheGun()
        {
            Camera world = BaseCamera();
            Camera overlay = OverlayCamera();

            var sway = Object.FindFirstObjectByType<WeaponSway>();
            Assert.IsNotNull(sway, "no WeaponSway — there is no weapon rig in the scene at all");

            // TWO lights, and they must be two, because a camera culls lights by
            // the light's LAYER. The world light cannot reach a gun drawn by the
            // overlay camera and a viewmodel light cannot reach the room, so
            // neither one alone can do both halves of a muzzle flash.
            //
            // Expressed as "a camera can see it" rather than "is on layer N":
            // the failure is being culled, and any culled layer causes it.
            Light? roomLight = null;
            Light? gunLight = null;
            foreach (Light light in sway!.transform.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Point) continue;
                if ((world.cullingMask & (1 << light.gameObject.layer)) != 0) roomLight ??= light;
                if ((overlay.cullingMask & (1 << light.gameObject.layer)) != 0) gunLight ??= light;
            }

            Assert.IsNotNull(roomLight,
                "no light under the weapon rig is visible to the world camera, so firing stopped lighting the room");
            Assert.IsNotNull(gunLight,
                "no light under the weapon rig is visible to the overlay camera, so the gun no longer lights up " +
                "when it fires — the world light is culled by the camera that draws it");
            Assert.AreNotSame(roomLight, gunLight,
                "one light cannot be both: a camera culls lights by layer, so whichever layer it is on, one half " +
                "of the flash is missing");

            Assert.IsFalse(roomLight!.isActiveAndEnabled, "the room flash is lit before a shot was fired");
            Assert.IsFalse(gunLight!.isActiveAndEnabled, "the gun flash is lit before a shot was fired");

            // AND THE REGRESSION THAT COST A REWRITE: no Light on the pooled
            // muzzle-flash prefab.
            //
            // A pooled object's lifetime is muzzleFlashLifetime — the SPRITE's
            // number, which is allowed to be long because overlapping sprites
            // look fine. A light inheriting it does not: at the SMG's 900 rpm
            // there are 0.0667 s between shots, an 0.08 s light never goes out,
            // and sustained fire puts the viewmodel under a continuous glow while
            // the room strobes correctly. The gun light lives on the RIG instead,
            // on WeaponController's own muzzleLightDuration clock.
            // Scoped to the MUZZLE FLASH prefab specifically. A pooled light is
            // not wrong in general — Fx_Explosion carries one deliberately, and
            // its pooled lifetime IS the explosion's own duration, which is the
            // correct number for it. The muzzle flash is the case where the
            // pooled lifetime belongs to the sprite and the light needs a
            // different, shorter one.
            foreach (PooledObject pooled in Object.FindObjectsByType<PooledObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!pooled.name.StartsWith("Fx_MuzzleFlash")) continue;
                Light[] onFlash = pooled.GetComponentsInChildren<Light>(true);
                Assert.AreEqual(0, onFlash.Length,
                    $"'{pooled.name}' carries a Light. Its pooled lifetime is muzzleFlashLifetime — the " +
                    "SPRITE's number — so at the SMG's 900 rpm the light outlives the gap between shots and " +
                    "the viewmodel sits under a continuous glow. The gun's flash light belongs on the rig, " +
                    "on WeaponController's muzzleLightDuration clock.");
            }
            yield return null;
        }

        /// <summary>
        /// The defect the whole change exists to kill: the player's FOV slider
        /// moving the world view and NOT reshaping the gun.
        /// </summary>
        [UnityTest]
        public IEnumerator ChangingTheWorldFov_DoesNotTouchTheGun()
        {
            Camera world = BaseCamera();
            Camera overlay = OverlayCamera();

            SettingsHub? hub = Object.FindFirstObjectByType<SettingsHub>();
            Assert.IsNotNull(hub, "no SettingsHub, so the FOV setting reaches nothing");

            float startWorld = world.fieldOfView;
            float startViewmodel = overlay.fieldOfView;

            hub!.Current.SetFovVertical(hub.Current.FovVertical + 20f);
            hub.Apply();

            // The world FOV is EASED (sprintFovEaseTime), so it needs frames, not
            // one tick. Sixty is far more than the ease needs at any frame rate
            // this suite runs at, and this is a hang guard, not a tuning number.
            for (int frame = 0; frame < 60; frame++) yield return null;

            Assert.Greater(world.fieldOfView, startWorld + 1f,
                "the world camera ignored the FOV setting entirely");
            Assert.AreEqual(startViewmodel, overlay.fieldOfView, 1e-3f,
                "the viewmodel camera followed the world FOV — the gun still stretches, which is the " +
                "exact defect the second camera was added to remove");
        }
    }
}
