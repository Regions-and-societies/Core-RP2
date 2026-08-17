using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// Every threshold and per-tier effect in one table, following the precedent
    /// <c>PlacementRules</c> set in Epic 2: 0.8 is Logic Externalization, so the numbers live in a
    /// single object that can be moved into defs or settings without hunting them down.
    /// </summary>
    public static class SettlementSizeRules
    {
        // -- population thresholds --------------------------------------------
        //
        // Calibrated against what PopulationDensityUtility actually produces rather than invented.
        // NPC settlements are seeded from faction tech level plus random(-10, +20):
        //
        //     medieval / default   base  50   ->   40 - 69
        //     neolithic            base  60   ->   50 - 79
        //     industrial           base  90   ->   80 - 109
        //     spacer               base 150   ->  140 - 169
        //
        // while a player colony reports its live FreeColonistsCount, realistically 1 - 25.
        //
        // The thresholds below therefore land tech level onto tier almost exactly: a tribal
        // settlement is a town, an industrial one a city, a spacer one a major city, and a player
        // colony starts as a village. That correspondence is deliberate — a spacer world genuinely
        // is a major city next to a tribal camp — but it does mean a vanilla player colony cannot
        // out-grow Village on headcount alone. Players running Empire climb through the tiers on
        // their colonies' upgrade levels instead; see SettlementSizeEvaluator.FromLevel.

        public const int VillageMinPopulation = 1;
        public const int TownMinPopulation = 40;
        public const int CityMinPopulation = 80;
        public const int MajorCityMinPopulation = 140;
        public const int MetropolisMinPopulation = 240;

        /// <summary>
        /// Residents per dwelling, so a dwelling count can stand in for an unknown population.
        /// Mirrors <c>GeographicProvince.totalDwellings</c>, which is population / 2.
        /// </summary>
        public const int ResidentsPerDwelling = 2;

        public static int MinPopulationFor(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return VillageMinPopulation;
                case SettlementTier.Town: return TownMinPopulation;
                case SettlementTier.City: return CityMinPopulation;
                case SettlementTier.MajorCity: return MajorCityMinPopulation;
                case SettlementTier.Metropolis: return MetropolisMinPopulation;
                default: return 0;
            }
        }

        // -- tier effects -----------------------------------------------------

        /// <summary>
        /// How many residents this tier can support. A settlement at capacity is the signal for
        /// Epic 3 that further growth has to come from tiering up rather than from more people.
        ///
        /// <c>None</c> returns 0, meaning "no tier-imposed cap". Callers must read a non-positive
        /// capacity as unlimited rather than as room for nobody.
        /// </summary>
        public static int PopulationCapacity(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return TownMinPopulation;
                case SettlementTier.Town: return CityMinPopulation;
                case SettlementTier.City: return MajorCityMinPopulation;
                case SettlementTier.MajorCity: return MetropolisMinPopulation;
                case SettlementTier.Metropolis: return 700;
                default: return 0;
            }
        }

        /// <summary>
        /// Production multiplier for the tier.
        ///
        /// Deliberately sublinear in population: a major city holds roughly seven times a village's
        /// headcount but produces a little over twice as much. Big settlements are better, not
        /// runaway — otherwise the optimal play is one enormous capital and nothing else, which is
        /// the opposite of a mod about regions.
        ///
        /// <c>None</c> returns 1, not 0. A holding with no tier — a camp, or anything at all when
        /// tiers are switched off — must be left alone by this multiplier, and a neutral 1 is the
        /// only value that does that. Returning 0 here would silently zero the economy of every
        /// untiered holding in the world, which is a far worse failure than a missing bonus.
        /// </summary>
        public static float ProductionScale(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return 1.00f;
                case SettlementTier.Town: return 1.35f;
                case SettlementTier.City: return 1.75f;
                case SettlementTier.MajorCity: return 2.25f;
                case SettlementTier.Metropolis: return 2.80f;
                default: return 1f;
            }
        }

        /// <summary>
        /// How far this settlement's claim reaches, in tiles. Feeds territory footprint: larger
        /// tiers claim more of the region around them.
        /// </summary>
        public static int TerritoryFootprint(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return 1;
                case SettlementTier.Town: return 1;
                case SettlementTier.City: return 2;
                case SettlementTier.MajorCity: return 3;
                case SettlementTier.Metropolis: return 4;
                default: return 0;
            }
        }
    }
}
