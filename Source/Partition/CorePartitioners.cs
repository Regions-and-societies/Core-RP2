using System.Collections.Generic;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// Core's default algorithm (0.4.0): draw regions INSIDE the terrain's natural sections — each flooded
    /// into one biome- and barrier-bounded container — then cut each container into evenly-sized,
    /// biome-weighted cells, and finally share inland lakes across their shores, absorb/chain islands and
    /// fold or drop tiny slivers. Borders sit on mountains, coasts and biome edges. Wraps
    /// <see cref="BorderPartitioner.PartitionContainSubdivide"/> plus the 0.4.0 post-passes.
    /// </summary>
    public class ContainSubdividePartitioner : IRegionPartitioner
    {
        public string AlgorithmId => RegionPartitionerRegistry.DefaultAlgorithmId;
        public string Label => "v0.4.0 — Contain then subdivide (default)";
        public string Description => "Draws regions inside natural sections (biome + barriers), cuts each into even honeycomb cells, then shares inland lakes and cleans up islands and slivers. The v0.4.0 algorithm.";
        public int Order => 0;

        public List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
            => BorderPartitioner.PartitionContainSubdivide(tileToProvinceId, minRegionTiles, maxRegionTiles, honeycomb: true);
    }

    /// <summary>
    /// Core's previous algorithm (0.3.0): the same contain-then-subdivide, but with its original balanced
    /// Chebyshev-ish CELL subdivision instead of the 0.4.0 honeycomb. Packaged so a world built under it
    /// regenerates faithfully and for players who prefer that region shape. Wraps
    /// <see cref="BorderPartitioner.PartitionContainSubdivide"/> with honeycomb off.
    /// </summary>
    public class ContainSubdivide030Partitioner : IRegionPartitioner
    {
        public string AlgorithmId => RegionPartitionerRegistry.Legacy030AlgorithmId;
        public string Label => "v0.3.0 — Contain then subdivide (cells)";
        public string Description => "The v0.3.0 algorithm: the same natural-section containers, cut into balanced box-ish cells rather than the 0.4.0 honeycomb. Kept for old-save fidelity and for preference.";
        public int Order => 5;

        public List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
            => BorderPartitioner.PartitionContainSubdivide(tileToProvinceId, minRegionTiles, maxRegionTiles, honeycomb: false);
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
        public string Label => "v0.2.x — Anchor-Voronoi boxes (legacy)";
        public string Description => "The 0.2.x algorithm: spaced anchors with a Chebyshev box fill. Kept for old-save fidelity and for preference.";
        public int Order => 10;

        public List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
            => BorderPartitioner.PartitionLand(tileToProvinceId, minRegionTiles, maxRegionTiles);
    }
}
