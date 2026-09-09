using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Core's built-in seeding policy for <see cref="WorldObjectKind.Outpost"/> (#18). Sizes and shapes
    /// outposts by delegating to the pure rules — <see cref="OutpostAllowanceRules"/> for the count,
    /// <see cref="OutpostArchetypeRules"/> for the type — so the numbers stay unit-tested. It is always
    /// active but sits at a high priority so a VOE-CP outpost policy can override it by registering lower.
    /// </summary>
    public sealed class OutpostSeedingPolicy : ISeedingPolicy
    {
        public WorldObjectKind Kind => WorldObjectKind.Outpost;

        /// <summary>Core default: high, so a CP policy registered for Outpost (lower number) wins.</summary>
        public int Priority => 1000;

        public bool IsActive => true;

        public int Allowance(SettlementTier anchorTier) => OutpostAllowanceRules.OutpostAllowance(anchorTier);

        public bool AcceptsTile(TileFeatures features) => true;

        public OutpostArchetype SelectArchetype(TileFeatures features) => OutpostArchetypeRules.Choose(features);
    }
}
