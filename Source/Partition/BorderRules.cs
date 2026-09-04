using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// The classified per-tile signals a border decision reads (#20). A plain value struct so the
    /// border rules stay pure: the game side fills one of these from <c>Find.WorldGrid</c>, the rules
    /// compare two of them without ever touching the grid. Everything is pre-reduced to a number or a
    /// flag — a biome is an <see cref="int"/> identity, hilliness is a 0..4 class — so the boundary
    /// combiner is testable with hand-written values.
    /// </summary>
    public struct TileSignal
    {
        /// <summary>Stable per-biome identity (any injective int is fine; only equality is read).</summary>
        public int BiomeId;

        /// <summary>Hilliness class: 0 Flat, 1 SmallHills, 2 LargeHills, 3 Mountainous, 4 Impassable.</summary>
        public int HillClass;

        /// <summary>Forest-density bucket from tree density (0 open, 1 wooded, 2 thick forest).</summary>
        public int ForestBucket;

        /// <summary>Swamp / marsh tile.</summary>
        public bool Swamp;

        /// <summary>Open water (ocean, lake, sea ice) — a hard wall for land partitioning.</summary>
        public bool Water;

        /// <summary>Impassable terrain or impassable biome — a hard wall.</summary>
        public bool Impassable;

        /// <summary>Tile temperature (Celsius); only the neighbour delta is read.</summary>
        public float Temperature;

        /// <summary>Tile rainfall; only the neighbour delta is read.</summary>
        public float Rainfall;
    }

    /// <summary>
    /// Tunable weights for the boundary combiner (#20). Plain data so a caller (and the future audit
    /// tuner) can hold and vary them; <see cref="Default"/> is the first-pass tuning. The
    /// forest/swamp/temperature/rainfall signals are weighted contributions; water and impassable are
    /// hard walls handled outside these weights.
    /// </summary>
    public struct BoundaryWeights
    {
        /// <summary>Per hilliness-class step between the two tiles (watershed ridge).</summary>
        public float HillStep;

        /// <summary>Bonus when either tile is LargeHills/Mountainous — a ridge binds harder than a gentle rise.</summary>
        public float HighGround;

        /// <summary>Flat contribution when the two tiles are different biomes.</summary>
        public float BiomeChange;

        /// <summary>Per forest-bucket step (a thick-forest edge against open land).</summary>
        public float ForestStep;

        /// <summary>Contribution when exactly one of the two tiles is swamp/marsh.</summary>
        public float SwampEdge;

        /// <summary>Per degree-Celsius of temperature difference (weak; off by default).</summary>
        public float TemperaturePerDegree;

        /// <summary>Per unit of rainfall difference (weak; off by default).</summary>
        public float RainfallPerUnit;

        /// <summary>
        /// Default weights (#20). Hilliness is deliberately <b>off</b> (HillStep = HighGround = 0): a
        /// hill or mountain range is too narrow a feature to be a border, and weighting it made regions
        /// stop at the mountain-foot and snake in thin strips along the range instead of flowing across
        /// the high ground into the next valley. Passable high ground is interior terrain the region
        /// spans; only <b>impassable</b> peaks remain hard walls (handled outside these weights). Real
        /// borders come from biome changes, thick-forest edges, and coasts. Temperature/rainfall stay
        /// off until the audit calls for them.
        /// </summary>
        public static BoundaryWeights Default => new BoundaryWeights
        {
            HillStep = 0f,
            HighGround = 0f,
            BiomeChange = 1f,
            ForestStep = 0.5f,
            SwampEdge = 0.25f,
            TemperaturePerDegree = 0f,
            RainfallPerUnit = 0f,
        };
    }

    /// <summary>
    /// The border-first partition core (#20). Provinces are river basins whose borders fall on the
    /// natural boundaries between them — the shifts in tile definition (ridgelines, biome edges, forest
    /// bands, coasts) that read as real frontiers. This class answers, from classified numbers only:
    /// how strong is the boundary across one edge, is it a wall, how many anchors a cell wants, how far
    /// apart they sit, and whether a cell is too small to keep.
    ///
    /// <para>Pure by design, like <see cref="Placement.CompactnessRules"/>: signals and scalars in,
    /// decisions out, no game state. The game-coupled flooding lives in the partitioner and is covered
    /// by the in-game audit; everything here is unit-tested without a game.</para>
    /// </summary>
    public static class BorderRules
    {
        /// <summary>Edges at or above this combined strength are walls. First-pass value, tunable.</summary>
        public const float DefaultWallThreshold = 1f;

        /// <summary>A cell must have at least this many tiles to stand as its own domain (non-island).</summary>
        public const int DefaultMinTiles = 20;

        /// <summary>Floor for <see cref="SeparationRadius"/> so anchors never coincide.</summary>
        public const float MinSeparationFloor = 1f;

        /// <summary>
        /// Boundary strength across the edge between two adjacent land tiles: the summed discontinuity
        /// in their definitions, weighted by <paramref name="w"/>. Water and impassable crossings are
        /// <b>hard walls</b> and return <see cref="float.PositiveInfinity"/> regardless of weights — a
        /// coast or an impassable range always bounds. Rivers are deliberately absent: they seed basin
        /// centres, they are not a boundary signal (#20). Never negative.
        /// </summary>
        public static float BoundaryStrength(TileSignal a, TileSignal b, BoundaryWeights w)
        {
            // Hard walls: a land partition never spans open water or impassable terrain.
            if (a.Water || b.Water || a.Impassable || b.Impassable)
            {
                return float.PositiveInfinity;
            }

            float strength = 0f;

            // Hilliness step (watershed ridge). The class delta is the rise; crossing into high ground
            // (LargeHills/Mountainous on either side) adds the ridge bonus so a mountain divide binds
            // harder than a gentle slope.
            int hillStep = Math.Abs(a.HillClass - b.HillClass);
            strength += hillStep * w.HillStep;
            if (a.HillClass >= 2 || b.HillClass >= 2)
            {
                strength += w.HighGround;
            }

            // Biome change: a different biome on the far side is a natural frontier.
            if (a.BiomeId != b.BiomeId)
            {
                strength += w.BiomeChange;
            }

            // Forest-density band: the edge of a thick forest against open land.
            strength += Math.Abs(a.ForestBucket - b.ForestBucket) * w.ForestStep;

            // Swamp / marsh edge: a contribution only where exactly one side is wetland.
            if (a.Swamp != b.Swamp)
            {
                strength += w.SwampEdge;
            }

            // Weak climate gradients, off by default until the audit calls for them.
            if (w.TemperaturePerDegree > 0f)
            {
                strength += Math.Abs(a.Temperature - b.Temperature) * w.TemperaturePerDegree;
            }
            if (w.RainfallPerUnit > 0f)
            {
                strength += Math.Abs(a.Rainfall - b.Rainfall) * w.RainfallPerUnit;
            }

            return strength;
        }

        /// <summary>Whether an edge of the given strength is a wall (border) at the given threshold.
        /// Infinite strength (a hard wall) is always a wall, including at a degenerate threshold.</summary>
        public static bool IsWall(float strength, float threshold)
        {
            if (float.IsPositiveInfinity(strength)) return true;
            return strength >= threshold;
        }

        /// <summary>
        /// How many anchors a cell wants when it is large enough to subdivide: <c>max(1, round(sum /
        /// cap))</c> over the cell's value points. One minimum guarantees every cell (and so every land
        /// component / island) keeps a province. A non-positive cap collapses to a single anchor.
        /// </summary>
        public static int AnchorCount(float sumPoints, float pointCap)
        {
            if (pointCap <= 0f) return 1;
            int n = (int)Math.Round(sumPoints / pointCap, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// Minimum spacing between anchors seeded into a featureless cell: <c>sqrt(targetArea / 3)</c>,
        /// floored at <see cref="MinSeparationFloor"/>. The /3 packs roughly one anchor per hex-ish
        /// cell of the target area, so a subdivided open basin gets evenly spread centres rather than
        /// clustered ones.
        /// </summary>
        public static float SeparationRadius(float targetArea)
        {
            if (targetArea <= 0f) return MinSeparationFloor;
            float r = (float)Math.Sqrt(targetArea / 3.0);
            return r < MinSeparationFloor ? MinSeparationFloor : r;
        }

        /// <summary>
        /// Whether a cell is too small to stand on its own and should merge into a neighbour: fewer
        /// than <paramref name="minTiles"/> and not an island. Islands are exempt because geography,
        /// not the sizer, set their size — the same rule the ownership layer follows ("geography is a
        /// free wall", <see cref="Placement.CompactnessRules"/>).
        /// </summary>
        public static bool ShouldMerge(int tileCount, int minTiles, bool isIsland)
        {
            if (isIsland) return false;
            return tileCount < minTiles;
        }

        /// <summary>
        /// Of a cell's candidate merge targets, the neighbour to fold into: the one across the
        /// <b>weakest</b> boundary — the least-natural border, the one it costs least to erase. Ties
        /// break to the smallest neighbour id for regenerate-identical determinism. Returns -1 when
        /// there are no candidates. <paramref name="borderStrengths"/> and
        /// <paramref name="neighborIds"/> are parallel.
        /// </summary>
        public static int WeakestBorderNeighbor(IReadOnlyList<float> borderStrengths, IReadOnlyList<int> neighborIds)
        {
            if (borderStrengths == null || neighborIds == null) return -1;
            int count = Math.Min(borderStrengths.Count, neighborIds.Count);
            int best = -1;
            float bestStrength = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float s = borderStrengths[i];
                int id = neighborIds[i];
                if (best == -1 || s < bestStrength || (s == bestStrength && id < neighborIds[best]))
                {
                    best = i;
                    bestStrength = s;
                }
            }
            return best == -1 ? -1 : neighborIds[best];
        }
    }
}
