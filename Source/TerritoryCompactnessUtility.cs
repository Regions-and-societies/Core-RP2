using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The game-side surface of the territory-shape metric (#19). The maths lives in the pure
    /// <see cref="Placement.CompactnessRules"/>; this reads the world — which provinces border which,
    /// which of those borders could ever be claimed (Land-type neighbours only; oceans and impassable
    /// ranges are geography's free wall), and which a faction already holds.
    ///
    /// <para>Public on purpose: worldgen placement ranks its candidates through it, and expansion mods
    /// (the Factions spread layer, the coming demographic-pressure spread) are meant to rank theirs
    /// through the same metric, so every grower in the ecosystem squares territories the same way.</para>
    /// </summary>
    public static class TerritoryCompactnessUtility
    {
        /// <summary>
        /// Count a candidate province's distinct claimable neighbours (Land-type provinces) and how many
        /// of those the claimant already holds, given the claimant's held province ids. The two counts
        /// feed <see cref="Placement.CompactnessRules.Embeddedness"/>.
        /// </summary>
        public static void CountBorders(GeographicProvince candidate, HashSet<int> heldProvinceIds,
            SynapseRegionManager manager, WorldGrid grid, out int owned, out int claimable)
        {
            owned = 0;
            claimable = 0;
            if (candidate?.tiles == null || manager == null || grid == null) return;

            var seen = new HashSet<int>();
            var neighbors = new List<PlanetTile>();
            foreach (int tile in candidate.tiles)
            {
                neighbors.Clear();
                grid.GetTileNeighbors(tile, neighbors);
                foreach (PlanetTile n in neighbors)
                {
                    int pid = manager.GetProvinceId(n.tileId);
                    if (pid == -1 || pid == candidate.id || !seen.Add(pid)) continue;

                    GeographicProvince p = manager.GetProvince(pid);
                    if (p == null || p.provinceType != ProvinceType.Land) continue;   // free wall
                    claimable++;
                    if (heldProvinceIds != null && heldProvinceIds.Contains(pid)) owned++;
                }
            }
        }

        /// <summary>A candidate's embeddedness in a faction's territory, 0..1 — the fraction of its
        /// claimable borders the faction already holds. For expansion mods ranking candidates.</summary>
        public static float Embeddedness(GeographicProvince candidate, Faction faction)
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            WorldGrid grid = Find.WorldGrid;
            if (mgr?.Provinces == null || grid == null || candidate == null || faction == null) return 0f;

            var held = new HashSet<int>();
            foreach (GeographicProvince p in mgr.Provinces)
                if (p != null && p.provinceType == ProvinceType.Land && RegionalOwnershipUtility.HoldsTerritory(p, faction))
                    held.Add(p.id);

            CountBorders(candidate, held, mgr, grid, out int owned, out int claimable);
            return Placement.CompactnessRules.Embeddedness(owned, claimable);
        }

        /// <summary>
        /// A faction's whole-domain compactness, 0..1: of every claimable border its held provinces
        /// have, the share facing another held province. 1 = a closed blob, 0 = pure spider. For
        /// readouts and expansion mods deciding where consolidation is needed.
        /// </summary>
        public static float DomainCompactness(Faction faction)
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            WorldGrid grid = Find.WorldGrid;
            if (mgr?.Provinces == null || grid == null || faction == null) return 1f;

            var held = new HashSet<int>();
            var heldProvinces = new List<GeographicProvince>();
            foreach (GeographicProvince p in mgr.Provinces)
            {
                if (p == null || p.provinceType != ProvinceType.Land) continue;
                if (!RegionalOwnershipUtility.HoldsTerritory(p, faction)) continue;
                held.Add(p.id);
                heldProvinces.Add(p);
            }
            if (heldProvinces.Count == 0) return 1f;

            int internalEdges = 0, frontierEdges = 0;
            foreach (GeographicProvince p in heldProvinces)
            {
                CountBorders(p, held, mgr, grid, out int owned, out int claimable);
                // Each internal edge is seen from both of its held endpoints; counting it once from the
                // lower-id side keeps the ratio honest.
                internalEdges += CountOwnedBelow(p, held, mgr, grid);
                frontierEdges += claimable - owned;
            }
            return Placement.CompactnessRules.DomainCompactness(internalEdges, frontierEdges);
        }

        // Distinct held Land neighbours of p with a lower id — the once-per-pair internal edge count.
        private static int CountOwnedBelow(GeographicProvince p, HashSet<int> held, SynapseRegionManager manager, WorldGrid grid)
        {
            var seen = new HashSet<int>();
            var neighbors = new List<PlanetTile>();
            int count = 0;
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                grid.GetTileNeighbors(tile, neighbors);
                foreach (PlanetTile n in neighbors)
                {
                    int pid = manager.GetProvinceId(n.tileId);
                    if (pid == -1 || pid == p.id || pid >= p.id || !seen.Add(pid)) continue;
                    if (held.Contains(pid)) count++;
                }
            }
            return count;
        }
    }
}
