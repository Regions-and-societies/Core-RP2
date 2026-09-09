namespace RegionsAndSocieties.Placement
{
    /// <summary>How each faction's stored placement number is read (#47 follow-up). <b>Percent</b> (default):
    /// the number is a share of the total land regions that self-scales — so a small vanilla world with few
    /// factions still fills, where a fixed count would leave it sparse. <b>Count</b>: the number is a literal
    /// target region count (exact control). The value mode also flips what the per-faction CLUSTER field
    /// means — see <see cref="ClusteringRules.EffectiveBodyCap"/>.</summary>
    public enum PlacementValueMode { Percent, Count }

    /// <summary>Retained for the general <see cref="PlacementShareRules.DistributeRegions"/> signature. The
    /// dialog only ever uses <see cref="SettledNormalized"/> (percent = a normalised share of the settled
    /// land); the whole-planet-absolute basis was dropped from the UI. Kept so the pure distributor stays
    /// general and its tests cover both apportionment paths.</summary>
    public enum PlacementPercentBasis { SettledNormalized, PlanetAbsolute }
}
