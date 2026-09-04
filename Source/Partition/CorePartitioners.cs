using System.Collections.Generic;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// Core's default algorithm (0.3.0): draw regions INSIDE the terrain's natural sections — each flooded
    /// into one biome- and barrier-bounded container — then cut each container into evenly-sized,
    /// biome-weighted cells. Borders sit on mountains, coasts and biome edges. Wraps
    /// <see cref="BorderPartitioner.PartitionContainSubdivide"/>.
    /// </summary>
    public class ContainSubdividePartitioner : IRegionPartitioner
    {
        public string AlgorithmId => RegionPartitionerRegistry.DefaultAlgorithmId;
        public string Label => "Contain then subdivide (default)";
        public string Description => "Draws regions inside natural sections (biome + barriers), then cuts each into evenly-sized cells. The 0.3.0 algorithm.";
        public int Order => 0;

        public List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
            => BorderPartitioner.PartitionContainSubdivide(tileToProvinceId, minRegionTiles, maxRegionTiles);
    }

    /// <summary>
    /// Core's legacy algorithm (0.2.x): spaced farthest-point anchors claimed by a Chebyshev (L-infinity)
    /// box fill, so regions come out as terrain-clipped boxes. Kept so a world generated under it reproduces
    /// exactly on regenerate, and for players who prefer its blockier look. Wraps
    /// <see cref="BorderPartitioner.PartitionLand"/>.
    /// </summary>
    public class AnchorVoronoiPartitioner : IRegionPartitioner
    {
        public string AlgorithmId => RegionPartitionerRegistry.LegacyAlgorithmId;
        public string Label => "Anchor-Voronoi boxes (legacy)";
        public string Description => "The 0.2.x algorithm: spaced anchors with a Chebyshev box fill. Kept for old-save fidelity and for preference.";
        public int Order => 10;

        public List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
            => BorderPartitioner.PartitionLand(tileToProvinceId, minRegionTiles, maxRegionTiles);
    }
}
