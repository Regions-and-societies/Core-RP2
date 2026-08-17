namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The kind of outpost a tile is suited to, chosen so a seeded outpost reads as belonging where
    /// it stands: a mine in the mountains, a logging camp in the forest, a farm on fertile flats.
    ///
    /// This is R&amp;T's own vocabulary, deliberately not a VOE type — the mapping from an archetype
    /// onto a concrete Vanilla Outposts Expanded def lives in <c>VoeOutpostCreator</c>, the one place
    /// allowed to name a foreign mod's types. Keeping the choice abstract here is what lets the rule
    /// stay pure and testable, and lets a second creator (Empire, vanilla) reuse the same judgement.
    /// </summary>
    public enum OutpostArchetype
    {
        /// <summary>No specialisation fits — a plain habitation. The constraint-free fallback.</summary>
        Encampment,
        Mining,
        Logging,
        Farming,
        Hunting
    }

    /// <summary>
    /// The tile facts the archetype choice reads, as plain numbers so the rule needs no <c>Find</c>,
    /// no <c>WorldGrid</c>, no Unity. The seeding facade fills this from the world; the rule decides.
    /// </summary>
    public struct TileFeatures
    {
        /// <summary>0 flat, 1 small hills, 2 large hills, 3 mountainous.</summary>
        public int hilliness;
        /// <summary>Biome plant density, ~0..1.</summary>
        public float plantDensity;
        /// <summary>Biome tree density, ~0..1.</summary>
        public float treeDensity;
        /// <summary>Biome animal density, ~0..1 (wildlife abundance).</summary>
        public float animalDensity;
        /// <summary>How mineable the tile reads, 0..1 (from hilliness / mineral resource pool).</summary>
        public float mineralsFraction;
        /// <summary>Whether the tile touches water.</summary>
        public bool coastal;
    }

    /// <summary>
    /// Chooses an outpost archetype from a tile's terrain. Pure: same inputs, same answer, always.
    ///
    /// First-pass thresholds — deliberately simple and tuned in-game against the "settlement tiers &amp;
    /// outpost allowance" / seeding debug reports before they are trusted. The order is a priority
    /// chain, strongest terrain signal first, ending at the constraint-free Encampment so every tile
    /// always resolves to something a VOE def exists for.
    /// </summary>
    public static class OutpostArchetypeRules
    {
        // Terrain thresholds. Named so a tuning pass changes a number, not a buried literal.
        public const int MiningHilliness = 2;          // large hills or mountainous
        public const float MiningMineralsFraction = 0.60f;
        public const float LoggingTreeDensity = 0.40f;
        public const float FarmingPlantDensity = 0.50f;
        public const int FarmingMaxHilliness = 1;      // farms want flat-to-rolling ground
        public const float HuntingAnimalDensity = 0.50f;

        public static OutpostArchetype Choose(TileFeatures f)
        {
            // Rock and ore first: steep or mineral-rich ground is a mine before it is anything else.
            if (f.hilliness >= MiningHilliness || f.mineralsFraction >= MiningMineralsFraction)
                return OutpostArchetype.Mining;

            // Timber: a forested tile is a logging camp.
            if (f.treeDensity >= LoggingTreeDensity)
                return OutpostArchetype.Logging;

            // Fertile and workable: a farm wants growth and ground it can plough.
            if (f.plantDensity >= FarmingPlantDensity && f.hilliness <= FarmingMaxHilliness)
                return OutpostArchetype.Farming;

            // Game: open land thick with wildlife is a hunting camp.
            if (f.animalDensity >= HuntingAnimalDensity)
                return OutpostArchetype.Hunting;

            // Nothing dominates — a plain habitation, which is also the def with no terrain gate.
            return OutpostArchetype.Encampment;
        }
    }
}
