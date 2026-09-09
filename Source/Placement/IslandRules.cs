namespace RegionsAndSocieties.Placement
{
    /// <summary>What becomes of a chain of small islands (#49). A chain is one or more small
    /// (water-surrounded, sub-<see cref="IslandRules.SmallIslandMaxTiles"/>) islands grouped by mutual
    /// proximity; a lone small island is a chain of one.</summary>
    public enum IslandChainResolution
    {
        /// <summary>Each island in the chain joins its nearest mainland land region.</summary>
        JoinMainland,
        /// <summary>The islands merge into one island-chain region of their own (a real archipelago).</summary>
        FormChainRegion,
        /// <summary>No mainland within reach and the chain is too small to stand on its own — leave the
        /// island(s) as they are (a mid-ocean speck; #51 may later drop a &le;6-tile one).</summary>
        KeepSeparate,
    }

    /// <summary>
    /// Small-island handling (#49). Islands (land fully surrounded by water) below the size cap should not
    /// stand as their own region: a lone one joins the nearest mainland, but a cluster that together
    /// reaches archipelago size becomes a region of its own instead of being scattered onto whatever coast
    /// each speck is nearest. Pure by design like the rest of the Placement layer — sizes and hop counts
    /// in, a decision out, no game state — so worldgen and any migration path share one rule and it is
    /// testable without a game.
    /// </summary>
    public static class IslandRules
    {
        /// <summary>A land region strictly smaller than this (in tiles), and water-surrounded, is a "small
        /// island" eligible to be absorbed or chained. Replaces the old literal 5.</summary>
        public const int SmallIslandMaxTiles = 10;

        /// <summary>Reach, in water hops, for both mainland absorption and island-to-island chaining. The
        /// old 2 missed anything past a one-tile channel.</summary>
        public const int IslandAbsorbHops = 8;

        /// <summary>A chain whose islands total this many tiles or more becomes its own region rather than
        /// joining the mainland — 30 tiles of archipelago is a place, not a coastal footnote.</summary>
        public const int IslandChainMinTiles = 30;

        /// <summary>True when a lone small island should be absorbed into a mainland: small enough, and land
        /// within <see cref="IslandAbsorbHops"/> water hops (a negative hop count means none in reach).</summary>
        public static bool ShouldJoin(int tileCount, int hopsToNearestLand)
        {
            if (tileCount <= 0 || tileCount >= SmallIslandMaxTiles) return false;
            return hopsToNearestLand >= 0 && hopsToNearestLand <= IslandAbsorbHops;
        }

        /// <summary>
        /// The fate of a chain totalling <paramref name="chainTiles"/> tiles whose nearest mainland is
        /// <paramref name="hopsToMainland"/> water hops away (negative = no mainland in reach). A chain at
        /// or above <see cref="IslandChainMinTiles"/> forms its own region regardless of the coast; a
        /// smaller chain (including a lone island) joins the mainland when one is in reach, and is otherwise
        /// left alone.
        /// </summary>
        public static IslandChainResolution ResolveChain(int chainTiles, int hopsToMainland)
        {
            if (chainTiles >= IslandChainMinTiles) return IslandChainResolution.FormChainRegion;
            if (hopsToMainland >= 0 && hopsToMainland <= IslandAbsorbHops) return IslandChainResolution.JoinMainland;
            return IslandChainResolution.KeepSeparate;
        }
    }
}
