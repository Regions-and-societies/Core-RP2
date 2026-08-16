using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.Patches
{
    // #81: every patch in this file targets a Map Mode Framework type. If the target is baked into the
    // [HarmonyPatch(typeof(...))] attribute, PatchAll force-loads that type while enumerating patch
    // classes — which throws a TypeLoadException when NEITHER framework is installed, aborting the whole
    // mod. So each one instead declares [HarmonyPatch] with no type, resolves its target dynamically in
    // TargetMethod(), and gates on MapFrameworkGate.Present in Prepare(). Prepare() runs before Harmony
    // touches TargetMethod() or the patch-method signatures, so when neither framework is present these
    // classes are skipped cleanly and no MapModeFramework type is ever resolved.

    [StaticConstructorOnStartup]
    [HarmonyPatch]
    public static class Patch_MapModeDef_Icon
    {
        private static bool Prepare() => MapFrameworkGate.Present;
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(MapModeDef), "Icon");

        private static Texture2D processedIcon = null;

        [HarmonyPrefix]
        public static bool Prefix(MapModeDef __instance, ref Texture2D __result)
        {
            if (__instance.defName == "SynapseMapModeGroup")
            {
                if (processedIcon != null)
                {
                    __result = processedIcon;
                    return false;
                }

                if (!string.IsNullOrEmpty(__instance.iconPath))
                {
                    Texture2D rawIcon = ContentFinder<Texture2D>.Get(__instance.iconPath, false);
                    if (rawIcon != null && rawIcon != BaseContent.BadTex)
                    {
                        processedIcon = TextureUtility.MakeTextureReadableAndTransparent(rawIcon);
                        __result = processedIcon;
                        return false;
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class Patch_MapModeUI_MapModes
    {
        private static bool Prepare() => MapFrameworkGate.Present;
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(MapModeUI), "MapModes");

        [HarmonyPostfix]
        public static void Postfix(ref List<MapMode> __result)
        {
            if (__result != null)
            {
                __result = __result.Where(m => m.def.defName != "SynapsePopulationDensity" && m.def.defName != "SynapseFactionTerritory" && m.def.defName != "SynapseGeographicProvinces").ToList();
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_MapModeComponent_Reset
    {
        private static bool Prepare() => MapFrameworkGate.Present;
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MapModeComponent), "Reset");

        [HarmonyPostfix]
        public static void Postfix(MapModeComponent __instance)
        {
            if (__instance.mapModes != null)
            {
                var regionsMode = __instance.mapModes.FirstOrDefault(m => m.def?.defName == "SynapseGeographicProvinces");
                if (regionsMode != null)
                {
                    __instance.currentMapMode = regionsMode;
                }
            }
        }
    }
}
