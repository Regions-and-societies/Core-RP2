using System;
using RegionsAndSocieties.Integration;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// Turns whatever a settlement will tell us about its size into a tier.
    ///
    /// Pure by design, exactly as <c>PlacementEvaluator</c> is: no <c>Find</c>, no Harmony, no
    /// Unity. Everything arrives as plain numbers, so the rules can be tested rather than assumed.
    /// </summary>
    public static class SettlementSizeEvaluator
    {
        /// <summary>
        /// The highest tier a given kind of holding may reach.
        ///
        /// Outposts are tiered — VOE levels them and the scope calls for mapping those onto R&amp;T
        /// tiers — but they are capped at Town. An outpost is a production holding by definition;
        /// calling a maxed-out mining camp a major city would make the tier meaningless for the
        /// settlements it is supposed to describe. Camps and military installations are not
        /// population centres at all and carry no tier.
        /// </summary>
        public static SettlementTier MaxTierFor(WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.Settlement: return SettlementTier.MajorCity;
                case WorldObjectKind.Outpost: return SettlementTier.Town;
                default: return SettlementTier.None;
            }
        }

        public static SettlementTier FromPopulation(int population)
        {
            if (population >= SettlementSizeRules.MajorCityMinPopulation) return SettlementTier.MajorCity;
            if (population >= SettlementSizeRules.CityMinPopulation) return SettlementTier.City;
            if (population >= SettlementSizeRules.TownMinPopulation) return SettlementTier.Town;
            if (population >= SettlementSizeRules.VillageMinPopulation) return SettlementTier.Village;
            return SettlementTier.None;
        }

        /// <summary>Dwellings standing in for an unknown population.</summary>
        public static SettlementTier FromDwellings(int dwellings)
        {
            if (dwellings <= 0) return SettlementTier.None;
            return FromPopulation(dwellings * SettlementSizeRules.ResidentsPerDwelling);
        }

        /// <summary>
        /// A mod's own upgrade level mapped onto a tier, as a fraction of its maximum.
        ///
        /// Levels are read through <c>WorldObjectAdapterRegistry.TryGetLevel</c>, so this works for
        /// Empire colony levels and VOE outpost levels without either being named here — and for
        /// any future mod that exposes a level at all.
        ///
        /// Major City is reserved for a fully upgraded holding. Reaching the top of a mod's own
        /// progression is the clearest statement it can make about size, so it is the one thing
        /// that earns the top tier outright.
        /// </summary>
        public static SettlementTier FromLevel(int level, int maxLevel)
        {
            if (level <= 0 || maxLevel <= 0) return SettlementTier.None;
            if (level >= maxLevel) return SettlementTier.MajorCity;
            if (maxLevel == 1) return SettlementTier.MajorCity;

            float fraction = (level - 1f) / (maxLevel - 1f);

            if (fraction >= 2f / 3f) return SettlementTier.City;
            if (fraction >= 1f / 3f) return SettlementTier.Town;
            return SettlementTier.Village;
        }

        /// <summary>
        /// The tier for a holding, from every source that has something to say about it.
        ///
        /// Population and level are combined by taking the larger, not by averaging, because they
        /// measure genuinely different things: one is a headcount, the other is the owning mod's
        /// own statement about progression. A fully upgraded Empire colony with a modest garrison
        /// is a major city whatever its pawn count says, and a sprawling settlement is not a
        /// village just because its mod has no level concept. Understating an established holding
        /// is the worse of the two errors.
        ///
        /// Pass 0 or a negative for anything unknown.
        /// </summary>
        public static SettlementTier Classify(WorldObjectKind kind, int population, int dwellings, int level, int maxLevel)
        {
            SettlementTier ceiling = MaxTierFor(kind);
            if (ceiling == SettlementTier.None) return SettlementTier.None;

            SettlementTier byHeadcount = population > 0
                ? FromPopulation(population)
                : FromDwellings(dwellings);

            SettlementTier byLevel = FromLevel(level, maxLevel);

            SettlementTier tier = byHeadcount.Max(byLevel);

            return (int)tier > (int)ceiling ? ceiling : tier;
        }

        /// <summary>Convenience overload for callers with no level information.</summary>
        public static SettlementTier Classify(WorldObjectKind kind, int population)
        {
            return Classify(kind, population, 0, 0, 0);
        }
    }
}
