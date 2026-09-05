using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Compat;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.Patches
{
    [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
    internal static class Patch_WorldInspectPane_TileInspectString
    {
        // This getter runs every GUI frame. The region/territory lines are cheap and cached per tile on a
        // slow cadence. The placement hint (why a tile is refused) is NOT cheap — it walks every holding
        // and flood-fills the world grid — so it is scheduled through PlacementHintCache: only after the
        // tile has stayed selected for a moment, never while Map Preview is flood-filling on its worker
        // thread, and remembered per tile so revisits are free (#44, #45).
        private const int RefreshIntervalTicks = PlacementHintCache.DefaultRefreshIntervalTicks;

        private static int cachedTileId = -1;
        private static int cachedAtTick = -1;
        private static string cachedText = string.Empty;
        private static bool cachedHasProvince;

        private static readonly PlacementHintCache hints = new PlacementHintCache();
        private static World hintsWorld;

        // Diagnostics for the "R&S: inspect-pane hint stress" debug action: how often the pane actually
        // evaluated, and how many frames it stood down because Map Preview was generating.
        internal static int Evaluations;
        internal static int BusyFrames;
        internal static int HitFrames;

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

            if (tileId != cachedTileId || cachedAtTick < 0 || now - cachedAtTick >= RefreshIntervalTicks)
            {
                var sb = new StringBuilder();

                // The per-tile dwellings compass block is HIDDEN for 0.3.0. It rendered as a text NW/N/NE grid
                // and its underlying values are region-uniform; both are being replaced by a drawn honeycomb
                // over location-based demographics in 0.4.0 (#34 / #33). GetDwellingsDisplay is kept for that
                // rebuild. Territory info below stays — it is per-tile-accurate and useful now.

                cachedHasProvince = AppendTerritoryInfo(sb, tileId);
                cachedTileId = tileId;
                cachedAtTick = now;
                cachedText = sb.ToString().TrimEnd();
            }

            if (!cachedHasProvince) return cachedText;

            string hint = PlacementHint(tileId, now);
            if (string.IsNullOrEmpty(hint)) return cachedText;
            return cachedText.Length == 0 ? hint : cachedText + "\n" + hint;
        }

        /// <summary>
        /// Region and territory lines for the tile. Returns whether the tile belongs to a province at all —
        /// tiles outside every province (open sea) get no lines and no placement hint.
        /// </summary>
        private static bool AppendTerritoryInfo(StringBuilder sb, int tileId)
        {
            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return false;

            GeographicProvince province = regionManager.GetProvinceForTile(tileId);
            if (province == null) return false;

            if (!string.IsNullOrEmpty(province.name))
            {
                sb.AppendLine("Region: " + province.name);
            }

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return false;

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

            return true;
        }

        /// <summary>
        /// 0.7: tell the player why a tile is or is not available before they commit to it.
        ///
        /// The refusal messages on the settle and outpost buttons only appear once a tile has been
        /// chosen. This puts the same answer, from the same evaluator, in the inspect pane — so the
        /// map is readable rather than something you probe by trial and error. Since 0.3.2 the answer
        /// is scheduled, not computed inline (see the class comment); until it is ready the line is
        /// simply absent, and the settle button's own check remains authoritative.
        /// </summary>
        private static string PlacementHint(int tileId, int gameTick)
        {
            if (!WorldObjectIntegrationSettings.PlacementGovernanceActive) return null;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return null;

            World world = Find.World;
            if (!ReferenceEquals(world, hintsWorld))
            {
                hints.Clear();
                hintsWorld = world;
            }

            int worldVersion = Find.WorldObjects?.AllWorldObjects?.Count ?? 0;
            float realtime = Time.realtimeSinceStartup;
            bool busy = MapPreviewCompat.IsGeneratingPreview;
            if (busy) BusyFrames++;

            string hint;
            switch (hints.Lookup(tileId, worldVersion, gameTick, realtime, busy, out hint))
            {
                case HintLookup.Hit:
                    HitFrames++;
                    return hint;

                case HintLookup.Evaluate:
                    Evaluations++;
                    PlacementDecision decision = WorldObjectPlacementUtility.Evaluate(
                        tileId, player, WorldObjectKind.Settlement);
                    hint = !decision.Allowed && !string.IsNullOrEmpty(decision.Reason) ? decision.Reason : null;
                    hints.Store(tileId, worldVersion, gameTick, hint);
                    return hint;

                default:
                    return null;
            }
        }
    }
}
