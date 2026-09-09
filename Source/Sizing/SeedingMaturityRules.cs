using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// World maturity for holding seeding (#18): a single 0..1 knob for how built-up a freshly generated
    /// world starts. It scales the per-anchor allowance every seeding policy reports, so 1.0 seeds the full
    /// allowance (a ready-to-play, fully-settled world — e.g. a World Domination start) and 0 seeds nothing.
    ///
    /// Pure, in the manner of <see cref="OutpostAllowanceRules"/>: the scaling is arithmetic on the base
    /// allowance and the maturity, so the seeding pass can be reasoned about and unit-tested without a game.
    /// </summary>
    public static class SeedingMaturityRules
    {
        /// <summary>Default world maturity — a partly-developed world, between a bare frontier and a
        /// fully-settled one.</summary>
        public const float DefaultMaturity = 0.5f;

        /// <summary>
        /// Scale a policy's base per-anchor allowance by <paramref name="maturity"/> (clamped to 0..1),
        /// rounded to a whole count and never negative. A positive base allowance never rounds down to zero
        /// while any maturity remains, so even a low-maturity world still seeds a token holding where one is
        /// allowed — the "Off" end of the slider (maturity 0) is the only value that yields nothing.
        /// </summary>
        public static int ScaleAllowance(int baseAllowance, float maturity)
        {
            if (baseAllowance <= 0) return 0;
            if (maturity <= 0f) return 0;
            if (maturity >= 1f) return baseAllowance;
            int scaled = (int)Math.Round(baseAllowance * (double)maturity, MidpointRounding.AwayFromZero);
            return scaled < 1 ? 1 : scaled;
        }

        /// <summary>A short player-facing label for a maturity value, for the settings slider readout.</summary>
        public static string Label(float maturity)
        {
            if (maturity <= 0f) return "Off";
            if (maturity < 0.375f) return "Frontier";
            if (maturity < 0.625f) return "Developing";
            if (maturity < 0.875f) return "Established";
            return "Fully settled";
        }
    }
}
