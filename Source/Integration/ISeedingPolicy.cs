using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// How many holdings of one <see cref="WorldObjectKind"/> a compatibility mod wants seeded around an
    /// anchor settlement at world generation, and which tiles/archetype suit them (#18). The seeding
    /// counterpart to <see cref="IHoldingCreator"/>: a creator BUILDS a holding; a policy decides HOW MANY
    /// and WHERE. Kept separate from the creator so the numbers stay pure and testable and the imperative
    /// build stays minimal — the same read/write split as <see cref="IWorldObjectAdapter"/> vs
    /// <see cref="IHoldingCreator"/>.
    ///
    /// <para>Core ships one policy (Outpost, delegating to the pure <see cref="OutpostAllowanceRules"/> and
    /// <see cref="OutpostArchetypeRules"/>); any kind a creator can build but no mod supplies a policy for
    /// falls back to a generic default. A CP mod registers its own via
    /// <see cref="SeedingPolicyRegistry.Register"/> from its Mod constructor.</para>
    /// </summary>
    public interface ISeedingPolicy
    {
        /// <summary>The one world-object kind this policy sizes and shapes.</summary>
        WorldObjectKind Kind { get; }

        /// <summary>Lower wins when more than one active policy claims the same kind (a CP policy should
        /// sit below Core's default). Mirrors adapter/creator priorities.</summary>
        int Priority { get; }

        /// <summary>True only when the backing mod is loaded AND the player left its integration on.</summary>
        bool IsActive { get; }

        /// <summary>The base number of this kind to seed around an anchor of the given tier, BEFORE the
        /// world-maturity scale is applied. Zero (or a <see cref="SettlementTier.None"/> anchor) seeds none.</summary>
        int Allowance(SettlementTier anchorTier);

        /// <summary>Optional per-tile suitability gate on top of the terrain/ownership filters the driver
        /// already applies. Return false to skip a tile. Default policies accept every candidate.</summary>
        bool AcceptsTile(TileFeatures features);

        /// <summary>The archetype to shape the holding as. Only Outpost-kind holdings use the archetype;
        /// other kinds return <see cref="OutpostArchetype.Encampment"/> and their creator ignores it.</summary>
        OutpostArchetype SelectArchetype(TileFeatures features);
    }
}
