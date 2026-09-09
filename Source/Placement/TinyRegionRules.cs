namespace RegionsAndSocieties.Placement
{
    /// <summary>The terminal fate of a land region too small to serve (#51). A region of 1–6 tiles carries
    /// no useful region benefits and nothing the demographic model can model, so once every merge/absorb
    /// pass has had its chance it is removed rather than kept — with one exception: a region that anchors a
    /// permanent holding (settlement/outpost) is never orphaned.</summary>
    public enum TinyRegionAction
    {
        /// <summary>Leave the region as-is (not tiny, or a settlement region with nowhere to fold).</summary>
        Keep,
        /// <summary>Unassign the tiles (tileToProvinceId = -1) and remove the region — the same state
        /// impassable holes already use.</summary>
        Drop,
        /// <summary>Fold the tiles into the largest land neighbour rather than dropping — used only to keep a
        /// settlement/outpost from being orphaned.</summary>
        Fold,
    }

    /// <summary>
    /// The drop-tiny-regions terminal rule (#51). Pure by design like the rest of the Placement layer:
    /// counts and flags in, an action out, no game state — so worldgen's final pass and any save-migration
    /// path share exactly one decision, and it is testable without a game.
    ///
    /// <para>Order of the merge/split passes still wins: this only decides the fate of what survived them.
    /// With #49 in place a small island within reach of land has already joined the mainland before this
    /// runs, so what reaches here is genuinely unplaceable.</para>
    /// </summary>
    public static class TinyRegionRules
    {
        /// <summary>Land regions of this size or smaller are dropped (or folded when they hold a settlement).
        /// A fixed value, not a setting: below this the demographic model has nothing to model, and a slider
        /// would only let a player mint regions the rest of the mod cannot serve.</summary>
        public const int TinyRegionMaxTiles = 6;

        /// <summary>
        /// What to do with a land region of <paramref name="tileCount"/> tiles. Above the cap → Keep. At or
        /// below it, a region that touches a land mass at all → Fold: a sliver next to real land should JOIN
        /// that land, not stand alone or leave a hole, and a settlement on it goes along. The mod option and
        /// the drop only decide the fate of a tiny region with NO land neighbour — a genuinely isolated
        /// speck (a mid-water island #49 could not reach): when <paramref name="keepSmallRegions"/> is on it
        /// is Kept as a benefits-suppressed region; otherwise it is Kept if it anchors a settlement (never
        /// orphaned) and Dropped if it is empty. Any tiny region the caller Keeps earns no regional benefits.
        /// </summary>
        public static TinyRegionAction Resolve(int tileCount, bool hasSettlement, bool hasLandNeighbour, bool keepSmallRegions)
        {
            if (tileCount <= 0 || tileCount > TinyRegionMaxTiles) return TinyRegionAction.Keep;
            if (hasLandNeighbour) return TinyRegionAction.Fold;   // join into the neighbouring land mass
            if (keepSmallRegions) return TinyRegionAction.Keep;   // isolated speck kept (benefits-suppressed)
            if (hasSettlement) return TinyRegionAction.Keep;      // never orphan a settlement with nowhere to go
            return TinyRegionAction.Drop;
        }

        /// <summary>The acceptance-named predicate: true when the region should become unassigned tiles.
        /// A thin wrapper over <see cref="Resolve"/>.</summary>
        public static bool ShouldDrop(int tileCount, bool hasSettlement, bool hasLandNeighbour, bool keepSmallRegions)
        {
            return Resolve(tileCount, hasSettlement, hasLandNeighbour, keepSmallRegions) == TinyRegionAction.Drop;
        }
    }
}
