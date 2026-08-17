using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Road overlays stop at foreign borders: a road segment is not drawn into a region owned by a
    /// faction other than the player's. Lived in the Empire patch file historically, but the target
    /// is vanilla <see cref="WorldGrid"/> — it belongs to core and stays here through the
    /// compatibility inversion (Core-MMF#3). The player-faction resolution goes through
    /// <see cref="RegionOwnershipHelpers"/>, whose override seam Empire's patch fills in.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.OverlayRoad))]
    public static class Patch_WorldGrid_OverlayRoad
    {
        [HarmonyPrefix]
        public static bool Prefix(PlanetTile fromTile, PlanetTile toTile, RoadDef roadDef)
        {
            if (Current.ProgramState != ProgramState.Playing)
                return true;

            try
            {
                Faction playerFaction = RegionOwnershipHelpers.GetPlayerFaction();
                if (playerFaction == null) return true;

                int from = fromTile.tileId;
                int to = toTile.tileId;

                Faction ownerFrom = RegionOwnershipHelpers.GetRegionOwner(from);
                if (ownerFrom != null && ownerFrom != playerFaction)
                {
                    return false; // Block road segment overlay
                }

                Faction ownerTo = RegionOwnershipHelpers.GetRegionOwner(to);
                if (ownerTo != null && ownerTo != playerFaction)
                {
                    return false; // Block road segment overlay
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[RegionsAndSocieties] Error in Patch_WorldGrid_OverlayRoad: {ex}", 991823);
            }
            return true;
        }
    }
}
