using System.Reflection;
using HarmonyLib;
using MapModeFramework;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Adds a "Draw region borders" checkbox to Map Mode Framework's Draw Settings panel (#53), beside
    /// Draw Hills / Rivers / Roads, so the global region-border overlay is toggled from the same place
    /// as the other world overlays and applies on top of any map mode. The panel auto-sizes from a
    /// private height field, so the postfix grows it by one line (reflection) before drawing the row.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_MapModeUI_DrawSettings
    {
        // #81: this target does not always exist. Realistic Planets 2's fork of Map Mode Framework
        // rewrote the Draw-Settings UI and DELETED DoDrawSettingsExpanded, and with NEITHER framework
        // installed MapModeUI itself is absent. Resolve the type by string (never a compile-time typeof,
        // which the JIT would eagerly load) so Prepare() safely returns false in both cases and Harmony
        // skips this patch — instead of PatchAll throwing and aborting the whole mod constructor. The
        // "Draw region borders" toggle stays reachable from R&T's own mod settings when this is skipped.
        private static MethodBase DrawSettingsMethod() =>
            AccessTools.Method(AccessTools.TypeByName("MapModeFramework.MapModeUI"), "DoDrawSettingsExpanded");

        private static bool Prepare() => DrawSettingsMethod() != null;
        private static MethodBase TargetMethod() => DrawSettingsMethod();

        public static void Postfix(MapModeUI __instance, ref Rect inRect)
        {
            try
            {
                Traverse tr = Traverse.Create(__instance);
                Traverse heightField = tr.Field("drawSettingsHeight");
                float height = heightField.GetValue<float>();
                heightField.SetValue(height + Text.LineHeight);
                tr.Method("UpdateWindowSize").GetValue();

                Rect row = new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeight);
                bool enabled = UI.RegionBorderOverlay.Enabled;
                Widgets.CheckboxLabeled(row, "Draw region borders", ref enabled);
                UI.RegionBorderOverlay.Enabled = enabled;
                inRect.y += Text.LineHeight;
            }
            catch
            {
                // If MMF's private layout members ever change, fail quiet rather than break the panel.
            }
        }
    }
}
