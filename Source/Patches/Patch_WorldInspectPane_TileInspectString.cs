using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;
using Verse;

namespace RegionsAndSocieties.Patches
{
    [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
    internal static class Patch_WorldInspectPane_TileInspectString
    {
        // This getter runs every GUI frame. Evaluating placement means walking every world object
        // and every province, so the answer is cached per tile and refreshed on a slow cadence —
        // territory ownership does not change between frames, and a stale line for a second is a
        // far better trade than recomputing the map sixty times a second.
        private const int RefreshIntervalTicks = 120;

        private static int cachedTileId = -1;
        private static int cachedAtTick = -1;
        private static string cachedText = string.Empty;

        [HarmonyPostfix]
        static void Postfix(ref string __result)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;

            PlanetTile selectedTile = Find.WorldSelector.SelectedTile;
            if (selectedTile != PlanetTile.Invalid)
            {
                string extra = GetTileText(selectedTile.tileId);
                if (!string.IsNullOrEmpty(extra))
                {
                    if (!string.IsNullOrEmpty(__result)) __result += "\n";
                    __result += extra;
                }
            }
        }

        private static string GetTileText(int tileId)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            if (tileId == cachedTileId && cachedAtTick >= 0 && now - cachedAtTick < RefreshIntervalTicks)
            {
                return cachedText;
            }

            var sb = new StringBuilder();

            // The per-tile dwellings compass block is HIDDEN for 0.3.0. It rendered as a text NW/N/NE grid
            // and its underlying values are region-uniform; both are being replaced by a drawn honeycomb
            // over location-based demographics in 0.4.0 (#34 / #33). GetDwellingsDisplay is kept for that
            // rebuild. Territory info below stays — it is per-tile-accurate and useful now.

            AppendTerritoryInfo(sb, tileId);

            cachedTileId = tileId;
            cachedAtTick = now;
            cachedText = sb.ToString().TrimEnd();
            return cachedText;
        }

        /// <summary>
        /// 0.7: tell the player why a tile is or is not available before they commit to it.
        ///
        /// The refusal messages on the settle and outpost buttons only appear once a tile has been
        /// chosen. This puts the same answer, from the same evaluator, in the inspect pane — so the
        /// map is readable rather than something you probe by trial and error.
        /// </summary>
        private static void AppendTerritoryInfo(StringBuilder sb, int tileId)
        {
            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return;

            GeographicProvince province = regionManager.GetProvinceForTile(tileId);
            if (province == null) return;

            if (!string.IsNullOrEmpty(province.name))
            {
                sb.AppendLine("Region: " + province.name);
            }

            Faction player = Faction.OfPlayer;
            if (player == null) return;

            ProvinceControl control = RegionalOwnershipUtility.GetControl(province, player);
            switch (control)
            {
                case ProvinceControl.Held:
                    sb.AppendLine("Territory: yours");
                    break;
                case ProvinceControl.Contested:
                    sb.AppendLine("Territory: contested");
                    break;
                case ProvinceControl.Foreign:
                    RegionalOwnershipData data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
                    Faction owner = data != null ? data.PrimaryOwner : null;
                    sb.AppendLine("Territory: " + (owner != null ? owner.Name : "another faction"));
                    break;
                default:
                    sb.AppendLine("Territory: unclaimed");
                    break;
            }

            if (!WorldObjectIntegrationSettings.PlacementGovernanceActive) return;

            PlacementDecision decision = WorldObjectPlacementUtility.Evaluate(
                tileId, player, WorldObjectKind.Settlement);

            if (!decision.Allowed && !string.IsNullOrEmpty(decision.Reason))
            {
                sb.AppendLine(decision.Reason);
            }
        }
    }
}
