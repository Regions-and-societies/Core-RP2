using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// The generic fallback policy the <see cref="SeedingPolicyRegistry"/> hands back for a kind that has an
    /// active creator but no registered <see cref="ISeedingPolicy"/> (#18). It reuses the tier→count ladder
    /// (<see cref="OutpostAllowanceRules"/>) as a sensible default density, accepts every candidate tile,
    /// and carries no archetype (Encampment) — so a mod that registers only a creator still gets its
    /// holdings seeded, at the same per-tier density outposts use, until it supplies its own policy.
    /// </summary>
    public sealed class DefaultSeedingPolicy : ISeedingPolicy
    {
        public DefaultSeedingPolicy(WorldObjectKind kind) { Kind = kind; }

        public WorldObjectKind Kind { get; }

        public int Priority => int.MaxValue;   // always the last resort

        public bool IsActive => true;

        public int Allowance(SettlementTier anchorTier) => OutpostAllowanceRules.OutpostAllowance(anchorTier);

        public bool AcceptsTile(TileFeatures features) => true;

        public OutpostArchetype SelectArchetype(TileFeatures features) => OutpostArchetype.Encampment;
    }
}
