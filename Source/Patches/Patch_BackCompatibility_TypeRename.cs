using System;
using HarmonyLib;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Save-game continuity across the RimSynapse.RegionsAndTerritories → RegionsAndSocieties
    /// rebrand (Core-MMF#2).
    ///
    /// Only two type names were ever written into save files with their full names — the scribed
    /// world/game components. Everything else the mod scribes (GeographicProvince, SettlementCrisis,
    /// DemographicOverride, FactionPlacementProfile) is saved via Scribe_Deep/Scribe_Collections on
    /// concrete field types, which carry no class attribute in the XML and therefore rename freely.
    ///
    /// RimWorld's own extension point for exactly this is BackCompatibility.GetBackCompatibleType:
    /// every scribed class attribute resolves through it before the loader gives up. Its final
    /// fallback returns null for a type that no longer exists, so a postfix that fills in the mapped
    /// type only when the result is null cannot interfere with any other resolution.
    /// </summary>
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
    public static class Patch_BackCompatibility_TypeRename
    {
        /// <summary>How many old-name resolutions this session — read by the debug report.</summary>
        public static int ResurrectionCount;

        /// <summary>The last old name resolved, for the debug report.</summary>
        public static string LastResurrectedName;

        public static void Postfix(string providedClassName, ref Type __result)
        {
            if (__result != null || providedClassName == null) return;

            Type mapped = MapOldName(providedClassName);
            if (mapped == null) return;

            __result = mapped;
            ResurrectionCount++;
            LastResurrectedName = providedClassName;
        }

        private static Type MapOldName(string providedClassName)
        {
            switch (providedClassName)
            {
                // The WorldComponent holding every province, ownership score and population figure.
                // This mapping is what makes a pre-rebrand world load with its territory data intact.
                case "RimSynapse.RegionsAndTerritories.SynapseRegionManager":
                    return typeof(SynapseRegionManager);

                // The map-mode GameComponent. Holds no data worth keeping, but mapping it keeps an
                // old save loading green instead of logging a could-not-instantiate error.
                case "RimSynapse.RegionsAndTerritories.MapModeTestHelper":
                    return typeof(MapModeTestHelper);

                default:
                    return null;
            }
        }
    }
}
