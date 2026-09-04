using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MapModeFramework;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Collapses every Regions and Societies overlay into a single "Regions and Societies" button in Map
    /// Mode Framework's mode bar. All the individual R&S overlays (territory, population, the demographic
    /// axes, biomes &amp; walls) are reachable through that one button's menu (<see cref="MapMode_SynapseGroup"/>),
    /// so listing each of them in the bar as well just crowds it. This filters the bar's <c>MapModes</c>
    /// view to hide the R&S individual modes — they stay REGISTERED (the group menu still finds them, and a
    /// save that had one selected still resolves), only the bar's presentation changes.
    ///
    /// <para>Resolved by string so the patch is skipped cleanly when the framework is absent (same
    /// approach as <see cref="Patch_MapModeUI_DrawSettings"/>). Drag-to-sort of the bar operates on this
    /// filtered view, so reordering is not persisted — an accepted trade for the tidier menu.</para>
    /// </summary>
    [HarmonyPatch]
    public static class Patch_MapModeUI_HideGroupedModes
    {
        private static MethodBase Getter() =>
            AccessTools.PropertyGetter(AccessTools.TypeByName("MapModeFramework.MapModeUI"), "MapModes");

        private static bool Prepare() => Getter() != null;
        private static MethodBase TargetMethod() => Getter();

        public static void Postfix(ref List<MapMode> __result)
        {
            if (__result == null || __result.Count == 0) return;
            bool anyHidden = false;
            for (int i = 0; i < __result.Count; i++)
            {
                if (IsHiddenRSMode(__result[i])) { anyHidden = true; break; }
            }
            if (!anyHidden) return;
            __result = __result.Where(m => !IsHiddenRSMode(m)).ToList();
        }

        // Every R&S map mode except the group launcher: all defNames are "Synapse…"; the group keeps its
        // own button and its menu carries the rest.
        private static bool IsHiddenRSMode(MapMode m)
        {
            string name = m?.def?.defName;
            return name != null && name.StartsWith("Synapse") && name != "SynapseMapModeGroup";
        }
    }
}
