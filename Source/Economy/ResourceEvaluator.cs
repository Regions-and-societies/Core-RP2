using System;

namespace RegionsAndSocieties.Economy
{
    /// <summary>
    /// The resource model's arithmetic. Pure, exactly as <c>PlacementEvaluator</c> and
    /// <c>SettlementSizeEvaluator</c> are: no <c>Find</c>, no Harmony, no Unity, no
    /// <c>TechLevel</c>. Research progress and pawn competence arrive as plain numbers, so the
    /// façade can source them however the live game allows without the rules caring.
    ///
    /// The shape, from the design note: every resource has a cap, a current stock, and a growth
    /// rate, and a province is never simply "depleting" or "full" — it sits wherever a faction's
    /// growth and extraction balance out.
    /// </summary>
    public static class ResourceEvaluator
    {
        /// <summary>
        /// How useful the best available researcher is at running a scanner, 0 to 1, from
        /// RimWorld's own 0-20 Intellectual scale.
        ///
        /// The *best* pawn, not the average: scanning is one operator at one console, so a
        /// settlement of thousands with one brilliant researcher scans exactly as well as a
        /// settlement of ten with the same researcher. This is what makes a small advanced
        /// population viable, which is the whole point of the low-population branch.
        /// </summary>
        public static float Competence(float bestIntellectualSkill)
        {
            if (bestIntellectualSkill <= ResourceRules.MinCompetenceSkill) return 0f;
            if (bestIntellectualSkill >= ResourceRules.FullCompetenceSkill) return 1f;

            return (bestIntellectualSkill - ResourceRules.MinCompetenceSkill)
                 / (ResourceRules.FullCompetenceSkill - ResourceRules.MinCompetenceSkill);
        }

        /// <summary>
        /// How well a faction can find new stock, 0 to 1. Research buys the equipment; competence
        /// operates it. Both are required — either at zero means no scanning at all, which is how a
        /// tribal faction on rich ground still strips it and a spacer faction with no researchers
        /// does the same.
        /// </summary>
        /// <param name="researchProgress">Fraction of the tech tree completed, 0 to 1.</param>
        /// <param name="competence">Result of <see cref="Competence"/>, 0 to 1.</param>
        public static float ScanCapability(float researchProgress, float competence)
        {
            if (competence <= 0f) return 0f;
            if (researchProgress <= ResourceRules.MinResearchForScanning) return 0f;

            float researchFactor = (researchProgress - ResourceRules.MinResearchForScanning)
                                 / (1f - ResourceRules.MinResearchForScanning);
            if (researchFactor > 1f) researchFactor = 1f;

            float capability = researchFactor * competence;
            return capability > 1f ? 1f : (capability < 0f ? 0f : capability);
        }

        /// <summary>
        /// How much of a resource comes back in a year.
        ///
        /// Biological stock regrows on the land's own terms and owes nothing to its owner — a
        /// forest does not care who holds the province. Geological stock is found rather than
        /// grown, so all of it is bought with scanning capability. Manufactured goods have no
        /// natural pool and return zero.
        /// </summary>
        public static float RenewalPerYear(ResourceKind kind, float cap, float scanCapability)
        {
            if (cap <= 0f) return 0f;

            switch (kind.Renewal())
            {
                case RenewalClass.Biological:
                    return cap * ResourceRules.BiologicalRenewalPerYear;

                case RenewalClass.Geological:
                    if (scanCapability <= 0f) return 0f;
                    if (scanCapability > 1f) scanCapability = 1f;
                    return cap * ResourceRules.GeologicalRenewalPerYear * scanCapability;

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// How much a population draws in a year. Scaled by the settlement tier's production
        /// multiplier, so a major city genuinely consumes its region faster than a village does —
        /// this is where <c>SettlementSizeRules.ProductionScale</c> enters the economy.
        /// </summary>
        public static float ExtractionPerYear(int population, float productionScale)
        {
            if (population <= 0) return 0f;
            if (productionScale <= 0f) productionScale = 1f;

            return population * ResourceRules.ExtractionPerResidentPerYear * productionScale;
        }

        /// <summary>Growth minus draw. Negative means the province is being consumed.</summary>
        public static float NetChangePerYear(
            ResourceKind kind, float cap, float scanCapability, int population, float productionScale)
        {
            return RenewalPerYear(kind, cap, scanCapability)
                 - ExtractionPerYear(population, productionScale);
        }

        /// <summary>
        /// The population at which growth exactly matches extraction — above it the province is
        /// being consumed, below it the stock holds or recovers.
        ///
        /// This is the design note's spacer-population threshold, derived rather than picked. A
        /// flat number would have had to be wrong for somebody; this falls out of the faction's own
        /// research, its own researchers, and the province's own richness, so a tribal faction's
        /// sustainable population on minerals is zero and a fully-scanning spacer faction's is
        /// large. Returns 0 for a resource that cannot recover at all, which is the honest answer:
        /// no population is sustainable on it.
        ///
        /// Read alongside the tension James flagged: a population small enough to be sustainable is
        /// also small enough to produce very little, because output scales with surrounding
        /// population. Under-population buys sustainability with irrelevance, and the two rules are
        /// meant to be tuned together.
        /// </summary>
        public static int SustainablePopulation(
            ResourceKind kind, float cap, float scanCapability, float productionScale)
        {
            float renewal = RenewalPerYear(kind, cap, scanCapability);
            if (renewal <= 0f) return 0;

            if (productionScale <= 0f) productionScale = 1f;
            float perResident = ResourceRules.ExtractionPerResidentPerYear * productionScale;
            if (perResident <= 0f) return 0;

            return (int)(renewal / perResident);
        }

        /// <summary>
        /// Whether this province can carry this population on this resource indefinitely.
        /// </summary>
        public static bool IsSustainable(
            ResourceKind kind, float cap, float scanCapability, int population, float productionScale)
        {
            return NetChangePerYear(kind, cap, scanCapability, population, productionScale) >= 0f;
        }

        /// <summary>
        /// Advance a pool by <paramref name="years"/> and report what was actually extracted.
        ///
        /// Extraction runs before growth, so a province emptied last year yields nothing this year
        /// but still recovers — exhaustion bites immediately and heals slowly, which is the right
        /// way round for a mechanic the player is supposed to see coming.
        ///
        /// The draw is capped at <c>MaxAnnualDrawFraction</c> of the province's ceiling. Without
        /// that, a large enough population empties any province the first time the economy ticks,
        /// and depletion stops being a curve anyone can react to.
        ///
        /// Returns the amount extracted, which is <b>not</b> necessarily the amount demanded — an
        /// over-extended economy asking for more than the ground holds is the normal case, and the
        /// shortfall is the signal Epic 3's production feedback is supposed to act on.
        /// </summary>
        public static float Advance(
            ResourcePool pool,
            ResourceKind kind,
            float scanCapability,
            int population,
            float productionScale,
            float years)
        {
            if (pool == null || years <= 0f) return 0f;

            pool.EnsureSeeded();

            if (!kind.IsExtracted()) return 0f;

            float demand = ExtractionPerYear(population, productionScale) * years;

            float ceiling = pool.cap * ResourceRules.MaxAnnualDrawFraction * years;
            if (ceiling > 0f && demand > ceiling) demand = ceiling;

            float extracted = pool.Draw(demand);

            pool.Grow(RenewalPerYear(kind, pool.cap, scanCapability) * years);

            return extracted;
        }

        /// <summary>
        /// The same step in game ticks, for callers driven by <c>WorldComponent.WorldComponentTick</c>.
        /// </summary>
        public static float AdvanceTicks(
            ResourcePool pool,
            ResourceKind kind,
            float scanCapability,
            int population,
            float productionScale,
            int ticks)
        {
            if (ticks <= 0) return 0f;
            return Advance(pool, kind, scanCapability, population, productionScale,
                ticks / (float)ResourceRules.TicksPerYear);
        }
    }
}
