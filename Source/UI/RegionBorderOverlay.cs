using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// State for the global region-border overlay (#53): a per-frame world layer draws the division
    /// lines between provinces regardless of the active map mode, so ownership can be read at a glance
    /// without turning on territory shading. Toggled from Map Mode Framework's Draw Settings panel (see
    /// Patch_MapModeUI_DrawSettings). The geometry lives in <see cref="WorldLayer_RegionBorders"/>,
    /// which builds one coloured submesh per owning faction; this class only holds the toggle, a build
    /// version used to force a rebuild, and a small material cache keyed by colour.
    /// </summary>
    public static class RegionBorderOverlay
    {
        public static bool Enabled = true;   // default on so it is visible immediately; the checkbox toggles it

        private static int version;
        public static int Version => version;

        /// <summary>Force the border layer to rebuild its mesh on the next frame (region layout changed).</summary>
        public static void Invalidate() => version++;

        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        public static Material MaterialFor(Color color)
        {
            if (!materials.TryGetValue(color, out Material mat) || mat == null)
            {
                // Occlusion is the shader's depth test, not the render queue. MetaOverlay is ZTest
                // Always — it paints on top of everything, so far-hemisphere borders showed straight
                // through the planet (#59). A WorldOverlay* surface shader is depth-tested against the
                // globe, so the solid planet occludes the back the way it does for vanilla surface
                // overlays and MMF's own WorldOverlayAdditive selection ring. Use the *unlit*
                // Transparent variant (not TransparentLit) so borders stay full-colour on the planet's
                // night side rather than dimming with the sun. Keep queue 3600 so lines still draw over
                // the map-mode fills on the near side; the Lift in WorldLayer_RegionBorders handles
                // z-fighting there while depth hides the far side. Fall back to MetaOverlay only if the
                // shader is somehow unavailable, so a missing field degrades to the old look, not a crash.
                Shader shader = ShaderDatabase.WorldOverlayTransparent ?? ShaderDatabase.MetaOverlay;
                mat = MaterialPool.MatFrom(BaseContent.WhiteTex, shader, color, 3600);
                materials[color] = mat;
            }
            return mat;
        }
    }
}
