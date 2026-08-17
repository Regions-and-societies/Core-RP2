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
        /// <summary>Default cap multiplier: territories-for-tier × this. Player-tunable via a mod-menu slider.</summary>
        public const float DefaultMultiplier = 10f;

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
