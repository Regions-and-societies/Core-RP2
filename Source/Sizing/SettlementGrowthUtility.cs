using RimWorld;
using RimWorld.Planet;
using Verse;
using RegionsAndSocieties.Demographics;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// Game-coupled glue for settlement birthrate growth (#6): gather the demographic factors a
    /// settlement's growth depends on from R&amp;T's existing per-region demographics, and seed a fresh
    /// settlement's modeled population. The pure arithmetic lives in <see cref="BirthrateRules"/>; this
    /// only reads the world. A factor whose source is off (Ideology / Biotech not active, no region
    /// data) is left at its neutral default, so the additive model degrades gracefully.
    /// </summary>
    public static class SettlementGrowthUtility
    {
        /// <summary>Of the working-age adults, the share that are fertile-age women — roughly half
        /// female, and ~two-thirds of working age (15-64) is fertile age (15-45).</summary>
        private const float FertileShareOfWorkingAge = 0.5f * 0.66f;

        /// <summary>A newly-modeled settlement starts at this fraction of its capacity (the ⅔-max target).
        /// 1.0 (0.3.0; was ⅓): a freshly generated world is an established one — its towns and cities
        /// already stand at their comfortable size — and growth still shows, crowding from the target up
        /// toward the tier max (150% of target) instead of climbing from a near-empty seed.</summary>
        public const float SeedFractionOfCapacity = 1f;

        /// <summary>
        /// Build the growth-factor inputs for a settlement from its faction (tech → mortality) and its
        /// region's demographics (age structure → fertility, wealth → the transition). Food defaults to
        /// fed until the resource model (#7) feeds it; ideology/xenotype are gated on their DLCs and
        /// left neutral until a precept/xenotype mapping is wired.
        /// </summary>
        public static GrowthInputs BuildInputs(WorldObject settlement)
        {
            var g = new GrowthInputs { FoodBalance = 1f, FertileFraction = BirthrateRules.DefaultFertileFraction };
            if (settlement == null) return g;

            Faction faction = settlement.Faction;
            g.TechLevel = (int)(faction?.def?.techLevel ?? TechLevel.Industrial);

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            var prov = mgr?.GetProvinceForTile(settlement.Tile);
            if (prov != null && prov.provinceType == ProvinceType.Land)
            {
                RegionDemographics demo = RegionDemographicsUtility.ForRegion(prov);
                if (demo != null && demo.settledTiles > 0)
                {
                    float workingAge = (demo.ageShares != null && demo.ageShares.Length > (int)AgeBucket.WorkingAge)
                        ? demo.ageShares[(int)AgeBucket.WorkingAge] : 0.6f;
                    g.FertileFraction = demo.femaleFraction * workingAge * FertileShareOfWorkingAge;
                    g.WealthLevel = demo.sesIndex / 100f;

                    // Additive DLC-gated factors — neutral (0) unless/until a mapping is wired in. Kept
                    // explicit so the graceful-degradation contract is visible at the call site.
                    if (demo.ideologyActive) g.IdeologyBias = 0f;   // TODO: natalist precepts -> +bias
                    if (demo.biotechActive) g.XenotypeBias = 0f;    // TODO: xenotype fertility -> +/-bias
                }
            }
            return g;
        }

        /// <summary>The starting modeled population for a settlement with no stored value yet: a third of
        /// its ⅔-max target (the growth capacity), floored so growth can begin, so a fresh settlement
        /// starts small and visibly climbs toward its target. Zero for an untiered holding.</summary>
        public static float SeedPopulation(WorldObject settlement)
        {
            int capacity = SettlementSizeUtility.TargetPopulationOf(settlement);
            if (capacity <= 0) return 0f;
            float seed = capacity * SeedFractionOfCapacity;
            return seed < BirthrateRules.SeedFloor ? BirthrateRules.SeedFloor : seed;
        }
    }
}
