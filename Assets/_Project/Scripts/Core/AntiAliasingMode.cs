#nullable enable
namespace CoD.Core
{
    /// <summary>
    /// The player's anti-aliasing choice.
    ///
    /// OUR enum rather than URP's, deliberately. CoD.Core sits at the bottom of
    /// the dependency graph — every other assembly points at it — and making it
    /// reference the render pipeline just to hold three values would drag URP down
    /// there with it. The mapping to UnityEngine.Rendering.Universal's own enum
    /// happens in CoD.Player.CameraGraphics, the only thing that touches a camera.
    ///
    /// These are written to the save file as numbers, so the ORDER here is a file
    /// format. Append new modes; never reorder the existing ones.
    /// </summary>
    public enum AntiAliasingMode
    {
        Off = 0,
        /// <summary>Cheapest, and softens the image slightly. On 4 GB of VRAM that is the trade.</summary>
        Fxaa = 1,
        /// <summary>Sharper than FXAA, and URP's usual pairing with post-processing.</summary>
        Smaa = 2,
    }
}
