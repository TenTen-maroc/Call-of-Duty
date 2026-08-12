#nullable enable
using CoD.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoD.Player
{
    /// <summary>
    /// Applies the player's graphics choices to the camera.
    ///
    /// WHY THE CAMERA AND NOT THE PIPELINE ASSET
    /// MSAA and render scale live on the UniversalRenderPipelineAsset, which is a
    /// ScriptableObject. Domain Reload is off here, so a runtime write to one
    /// survives into the next Play session and permanently rewrites the shipped
    /// default — the same trap that produced WaveScaling, StatSheet and
    /// GameSettings. UniversalAdditionalCameraData is SCENE state and dies with
    /// the scene, which is what makes post-processing and post AA the two knobs
    /// that can be player-facing without paying that cost.
    ///
    /// WHY IT LIVES IN CoD.Player
    /// It needs the render pipeline assembly and CoD.Core must not have one.
    /// PlayerLook and CameraShake already own camera concerns, so it sits beside
    /// them. The main menu hosts one too: that scene has a camera and a
    /// SettingsHub, which is everything this needs.
    ///
    /// No Update. It runs when a setting changes, and never again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraGraphics : MonoBehaviour
    {
        [Tooltip("Where the player's choices come from. Without this the settings silently do nothing.")]
        [SerializeField] private SettingsHub? _settings = null;
        [SerializeField] private Camera? _camera = null;

        [Tooltip("The overlay camera that draws the gun, if this rig has one. Post-processing only — " +
                 "the menu has no viewmodel and leaves this null.")]
        [SerializeField] private Camera? _viewmodelCamera = null;

        private UniversalAdditionalCameraData? _data;
        private UniversalAdditionalCameraData? _viewmodelData;

        private void Awake()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            // Resolved once here. GetUniversalAdditionalCameraData adds the
            // component if it is missing, so this also repairs a camera that was
            // built without one — which is exactly how the whole project shipped
            // with post-processing off.
            if (_camera != null) _data = _camera.GetUniversalAdditionalCameraData();
            if (_viewmodelCamera != null) _viewmodelData = _viewmodelCamera.GetUniversalAdditionalCameraData();
        }

        private void OnEnable()
        {
            if (_settings == null) return;
            _settings.Changed += Apply;
            // Apply once on entry too: Changed only fires on a change, and the
            // first frame must already be at the player's chosen setting.
            Apply(_settings.Current);
        }

        private void OnDisable()
        {
            if (_settings != null) _settings.Changed -= Apply;
        }

        /// <summary>
        /// WHY THE OVERLAY GETS THE POST FLAG AND NOT THE AA ONE
        /// URP resolves a camera stack's post-processing at the LAST camera in the
        /// stack that has renderPostProcessing enabled. The viewmodel camera is
        /// that last camera, so clearing the flag on the base alone leaves post
        /// running on the whole frame and the player's "Post-processing: Off" row
        /// does nothing at all — the setting reads as broken, and the base camera
        /// (which is all RenderingTests used to inspect) says it worked.
        ///
        /// Anti-aliasing is deliberately NOT mirrored. URP takes a stack's post AA
        /// from the BASE camera and ignores the overlays', so writing it to the
        /// overlay would either be dead code or, if URP ever changed, a second
        /// full-screen SMAA pass on a 4 GB laptop GPU.
        ///
        /// Each half is guarded separately: a rig with a viewmodel camera and no
        /// base camera is nonsense, but it must not silently skip the half that
        /// does exist.
        /// </summary>
        private void Apply(GameSettings settings)
        {
            if (_data != null)
            {
                _data.renderPostProcessing = settings.PostProcessing;
                _data.antialiasing = Map(settings.AntiAliasing);
            }
            if (_viewmodelData != null) _viewmodelData.renderPostProcessing = settings.PostProcessing;
        }

        /// <summary>Our enum to URP's, so that CoD.Core never has to see the render pipeline.</summary>
        private static AntialiasingMode Map(AntiAliasingMode mode) => mode switch
        {
            AntiAliasingMode.Fxaa => AntialiasingMode.FastApproximateAntialiasing,
            AntiAliasingMode.Smaa => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            _ => AntialiasingMode.None,
        };
    }
}
