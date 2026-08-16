using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Which provinces border which, for the whole map, derived once and held.
    ///
    /// <para>Two subsystems now ask this question on paths a player can feel: placement asks it for
    /// every candidate holding when checking sequential expansion, and military reach asks it for
    /// every step of every supply search. <c>SynapseRegionManager.AreProvincesAdjacent</c> answers
    /// it by comparing one province's tiles against another's, which is fine once and quadratic
    /// when it is the inner loop of a graph search.</para>
    ///
    /// <para>Walking each tile's own neighbours and recording which province each one lands in
    /// answers it for every pair at once, in time proportional to the number of tiles. The result
    /// cannot change without worldgen running again, so it is cached — keyed on the world instance
    /// rather than on a bool, because the failure this guards against is a second save loading into
    /// the previous game's geography, which would present as territory rules obeying a map nobody
    /// is looking at.</para>
    ///
    /// <para>Impure by necessity (it reads <c>Find</c>), and deliberately the only new impure thing
    /// either subsystem gained: both evaluators still receive adjacency as a delegate and still know
    /// nothing about RimWorld.</para>
    /// </summary>
    public static class ProvinceAdjacency
    {
        private static object cachedWorld;
        private static Dictionary<int, List<int>> cachedMap;

        /// <summary>Neighbours of every province, by province id. Never null; may be empty.</summary>
        public static Dictionary<int, List<int>> Map(SynapseRegionManager regionManager)
        {
            if (regionManager == null) return new Dictionary<int, List<int>>();

            object world = Find.World;
            if (cachedMap != null && ReferenceEquals(cachedWorld, world)) return cachedMap;

            var adjacency = new Dictionary<int, List<int>>();

            foreach (GeographicProvince province in regionManager.Provinces)
            {
                if (province == null) continue;
                if (!adjacency.ContainsKey(province.id)) adjacency[province.id] = new List<int>();
            }

            WorldGrid grid = Find.WorldGrid;
            if (grid != null)
            {
                var neighbourTiles = new List<PlanetTile>();

                foreach (GeographicProvince province in regionManager.Provinces)
                {
                    if (province == null || province.tiles == null) continue;

                    List<int> edges = adjacency[province.id];

                    foreach (int tile in province.tiles)
                    {
                        neighbourTiles.Clear();
                        grid.GetTileNeighbors(tile, neighbourTiles);

                        foreach (PlanetTile neighbour in neighbourTiles)
                        {
                            int other = regionManager.GetProvinceId(neighbour);
                            if (other < 0 || other == province.id) continue;
                            if (!edges.Contains(other)) edges.Add(other);
                        }
                    }
                }
            }

            cachedWorld = world;
            cachedMap = adjacency;
            return adjacency;
        }

        public static IEnumerable<int> NeighboursOf(SynapseRegionManager regionManager, int provinceId)
        {
            if (provinceId < 0) return new int[0];

            Dictionary<int, List<int>> map = Map(regionManager);
            List<int> neighbours;
            return map.TryGetValue(provinceId, out neighbours) ? (IEnumerable<int>)neighbours : new int[0];
        }

        /// <summary>
        /// Whether two provinces share a border.
        ///
        /// Falls back to the region manager's own tile-by-tile comparison when the derived map has
        /// nothing to say about the province — the map is empty when there is no world grid to walk,
        /// and answering "not adjacent" from an empty map would quietly refuse every expansion.
        /// </summary>
        public static bool AreAdjacent(SynapseRegionManager regionManager, int a, int b)
        {
            if (regionManager == null || a < 0 || b < 0) return false;
            if (a == b) return true;

            Dictionary<int, List<int>> map = Map(regionManager);

            List<int> neighbours;
            if (map.TryGetValue(a, out neighbours) && neighbours.Count > 0) return neighbours.Contains(b);
            if (map.TryGetValue(b, out neighbours) && neighbours.Count > 0) return neighbours.Contains(a);

            GeographicProvince pa = regionManager.GetProvince(a);
            GeographicProvince pb = regionManager.GetProvince(b);
            if (pa == null || pb == null) return false;

            return regionManager.AreProvincesAdjacent(pa, pb);
        }

        /// <summary>Drop the cached map. Called when a world is discarded or provinces regenerate.</summary>
        public static void ClearCache()
        {
            cachedWorld = null;
            cachedMap = null;
        }
    }
}
