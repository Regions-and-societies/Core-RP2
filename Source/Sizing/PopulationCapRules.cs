using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// How large a settlement may grow, and the size it drifts toward, as a function of its tier
    /// (0.8). The cap reuses the pyramid directly: a tier's cap is the number of territories that tier
    /// requires (its triangular number, <see cref="TierPyramidRules.TerritoriesForTier"/>) times a
    /// player-set multiplier, times a tech-level factor. So at the default ×10 with an industrial
    /// faction (factor 1): T1 caps at 10, T5 (metropolis) at 150.
    ///
    /// <para>The desired size is two-thirds of the cap, and the modeled population steps toward it with
    /// a dead-band so it settles rather than oscillates (hysteresis). Pure: it works on plain numbers,
    /// so the tech factor and the current population arrive as arguments and the whole thing is
    /// testable without a game. For the player this only informs R&amp;T's model — it never adds or
    /// removes real colonists.</para>
    /// </summary>
    public static class PopulationCapRules
    {
        /// <summary>
        /// Default cap multiplier: territories-for-tier × this. Player-tunable via a mod-menu slider.
        /// 30 (0.3.0; was 10): at industrial tech a village caps at 30, a town 90, a city 180, a major
        /// city 300 and a metropolis 450, so a settled region reads in the hundreds rather than tens
        /// and the density heatmap actually reaches its upper bands.
        /// </summary>
        public const float DefaultMultiplier = 30f;

        /// <summary>
        /// The population a settlement starts modelling from.
        ///
        /// <para><b>A non-positive <paramref name="targetCapacity"/> means "no tier-imposed cap", not
        /// "room for nobody"</b> — the contract <see cref="MaxPopulation"/> documents and every other
        /// caller honours. An untiered settlement (which is every settlement when the settlement-tier
        /// feature is off, its default) therefore seeds from <paramref name="fallbackEstimate"/>. Reading
        /// that zero as an empty settlement is what left every NPC settlement with no population at all,
        /// and with it no demographics anywhere on the planet (#71).</para>
        /// </summary>
        public static float SeedPopulation(int targetCapacity, int fallbackEstimate, float seedFraction, float seedFloor)
        {
            float floor = seedFloor < 0f ? 0f : seedFloor;
            if (targetCapacity <= 0)
            {
                float f = fallbackEstimate > 0 ? fallbackEstimate : floor;
                return f < floor ? floor : f;
            }
            float fraction = seedFraction > 0f ? seedFraction : TargetFraction;
            float seed = targetCapacity * fraction;
            return seed < floor ? floor : seed;
        }

        /// <summary>The desired size is this fraction of the cap; the population drifts toward it.</summary>
        public const float TargetFraction = 2f / 3f;

        /// <summary>
        /// Maximum population a settlement of this tier may hold:
        /// <c>territoriesForTier(tier) × multiplier × techFactor</c>, rounded, never negative.
        /// A tierless holding caps at 0.
        /// </summary>
        public static int MaxPopulation(SettlementTier tier, float multiplier, float techFactor)
        {
            int territories = TierPyramidRules.TerritoriesForTier((int)tier);
            if (territories <= 0 || multiplier <= 0f || techFactor <= 0f) return 0;
            return Mathf_RoundToInt(territories * multiplier * techFactor);
        }

        /// <summary>The size a settlement of this tier drifts toward: two-thirds of its cap.</summary>
        public static int TargetPopulation(SettlementTier tier, float multiplier, float techFactor)
        {
            return Mathf_RoundToInt(MaxPopulation(tier, multiplier, techFactor) * TargetFraction);
        }

        /// <summary>
        /// One hysteresis step of the modeled population toward <paramref name="target"/>: no change
        /// while within <paramref name="deadBand"/> of the target (so it settles instead of jittering),
        /// otherwise move toward it by at most <paramref name="maxStep"/>. Works in both directions —
        /// a settlement over its target shrinks toward it, one under it grows.
        /// </summary>
        public static int StepToward(int current, int target, int maxStep, int deadBand)
        {
            int diff = target - current;
            if (Math.Abs(diff) <= Math.Max(0, deadBand)) return current;
            int step = Math.Min(Math.Max(1, maxStep), Math.Abs(diff));
            return current + Math.Sign(diff) * step;
        }

        // Local rounding so this file stays free of UnityEngine and compiles in the pure sandbox.
        private static int Mathf_RoundToInt(float v)
        {
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }
    }
}
