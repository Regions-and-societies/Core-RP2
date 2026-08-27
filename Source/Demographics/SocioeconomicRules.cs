using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The four socioeconomic classes a region's people are bucketed into (#14), by wealth.</summary>
    public enum SesTier
    {
        Subsistence = 0,  // hand-to-mouth
        Modest = 1,       // getting by
        Prosperous = 2,   // comfortable
        Affluent = 3      // wealthy
    }

    /// <summary>
    /// The deterministic socioeconomic-tiering core (0.2.0, #14). Unlike the age and education axes this
    /// one does not invent a distribution — the demographics engine already draws a per-tile wealth
    /// value (from faction tech level and each settlement's size/pressure). This just classifies those
    /// wealth values into SES tiers and collapses the mix to a 0-100 index, so the two remaining #14
    /// signals — a region's resource richness and its trade-road access — are applied by the caller as a
    /// wealth multiplier before classification (they lift or lower the whole region's standing).
    ///
    /// <para>Pure by design, like the other *Rules: wealth thresholds in and tier/index out, no game
    /// state, testable without a game and identical on every machine.</para>
    /// </summary>
    public static class SocioeconomicRules
    {
        public const int TierCount = 4;

        // Wealth (silver-ish) boundaries between the tiers. Chosen against the engine's per-tech base
        // wealth (neolithic ~120, medieval ~250, industrial ~500, spacer ~1000, ultra+ ~2000) so a
        // tribe reads subsistence/modest, an industrial polity modest/prosperous, a spacer one
        // prosperous, and an ultra/archotech one affluent. First-pass, tunable.
        private const int SubsistenceMax = 200;   // below this: Subsistence
        private const int ModestMax = 600;        // below this: Modest
        private const int ProsperousMax = 1500;   // below this: Prosperous; at/above: Affluent

        // A 0-100 standing score per tier, used to collapse a distribution to one index for shading.
        private static readonly float[] TierScore = { 0f, 33f, 67f, 100f };

        /// <summary>Classify a wealth value into its socioeconomic tier.</summary>
        public static SesTier TierFor(int wealth)
        {
            if (wealth < SubsistenceMax) return SesTier.Subsistence;
            if (wealth < ModestMax) return SesTier.Modest;
            if (wealth < ProsperousMax) return SesTier.Prosperous;
            return SesTier.Affluent;
        }

        /// <summary>
        /// A single 0-100 socioeconomic index for a tier distribution: the share-weighted mean of the
        /// per-tier standing scores. 0 = wholly subsistence, 100 = wholly affluent. Returns 0 for a
        /// null/empty distribution. This is what the wealth-heatmap overlay shades by.
        /// </summary>
        public static int Index(float[] shares)
        {
            if (shares == null || shares.Length < TierCount) return 0;
            float total = 0f, acc = 0f;
            for (int i = 0; i < TierCount; i++) { total += shares[i]; acc += shares[i] * TierScore[i]; }
            if (total <= 0f) return 0;
            return (int)Math.Round(acc / total);
        }
    }
}
