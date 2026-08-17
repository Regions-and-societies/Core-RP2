using HarmonyLib;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using Verse;

namespace RegionsAndSocieties.Patches
{
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.PostAdd))]
    public static class Patch_WorldObject_PostAdd
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            InvalidateCaches.OnWorldObjectChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.PostRemove))]
    public static class Patch_WorldObject_PostRemove
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            InvalidateCaches.OnWorldObjectChanged(__instance);
        }
    }

    internal static class InvalidateCaches
    {
        /// <summary>
        /// A world object was added or removed. Invalidate the caches whose inputs it changed:
        /// - population density, for anything that contributes residents;
        /// - province ownership, for any territorial holding (settlement/outpost/military/camp) —
        ///   including non-population military installations, which the density check alone misses.
        /// Classify once and reuse. Transient/non-territorial objects (caravans, sites) touch nothing.
        /// </summary>
        public static void OnWorldObjectChanged(WorldObject obj)
        {
            WorldObjectKind kind = WorldObjectClassifier.Classify(obj);

            if (kind == WorldObjectKind.Settlement
                || kind == WorldObjectKind.Outpost
                || kind == WorldObjectKind.Military
                || kind == WorldObjectKind.Camp)
            {
                SynapseRegionManager.BumpOwnershipEpoch();
            }

            if (WorldObjectClassifier.HasPopulation(obj))
            {
                PopulationDensityUtility.MarkCacheDirty();
            }
        }
    }
}
