using System.Collections.Generic;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// A pluggable world-partition algorithm — the toolkit's extension point for how the globe is cut into
    /// land provinces. Core ships two (contain-then-subdivide, anchor-Voronoi); a mod contributes its own by
    /// implementing this and registering it in <see cref="RegionPartitionerRegistry"/> from its Mod
    /// constructor, and it then appears in the world-partition dropdown in Regions and Societies' settings.
    ///
    /// <para>The chosen algorithm's <see cref="AlgorithmId"/> is stamped onto every world it generates, so a
    /// later regenerate reproduces that world with the same algorithm and a save records which one rendered
    /// it. Changing the setting therefore only affects NEW worlds — an existing save keeps its algorithm.</para>
    /// </summary>
    public interface IRegionPartitioner
    {
        /// <summary>Stable identifier — persisted in saves and used as the settings value. Treat it as a
        /// permanent contract: never localise it or change it once shipped.</summary>
        string AlgorithmId { get; }

        /// <summary>Human-readable name shown in the settings dropdown.</summary>
        string Label { get; }

        /// <summary>One-line description for the dropdown's tooltip.</summary>
        string Description { get; }

        /// <summary>Ascending sort order in the dropdown; Core's default is 0.</summary>
        int Order { get; }

        /// <summary>
        /// Partition the unclaimed land into province tile-groups. Water and impassable tiles are already
        /// claimed in <paramref name="tileToProvinceId"/> (their entries are &gt;= 0) and must be treated as
        /// hard walls the fill never spans. Returns one tile list per land province, ready for the caller to
        /// wrap in provinces. Must be deterministic from the world for regenerate-fidelity.
        /// </summary>
        List<List<int>> Partition(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles);
    }
}
