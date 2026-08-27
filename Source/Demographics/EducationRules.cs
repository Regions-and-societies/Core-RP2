using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The four education classes a region's people are bucketed into (#15). Coarse by design —
    /// a structure, not a per-pawn schooling roll.</summary>
    public enum EducationTier
    {
        Illiterate = 0,  // no formal schooling
        Basic = 1,       // literate, basic trades
        Skilled = 2,     // trained specialists
        Advanced = 3     // higher education / research
    }

    /// <summary>
    /// The deterministic education-structure core (0.2.0, #15). A region's education distribution — how
    /// its people split across the four tiers — is a pure function of a few signals: the dominant
    /// faction's tech level (the dominant signal: a tribe is mostly illiterate, a spacer polity mostly
    /// skilled), an ideology research skew (transhumanist/tech memes lift it, primitivist/nature memes
    /// pull it down), and a xenotype aptitude skew where Biotech gives a caste engineered intellect.
    /// No signal is required: with none supplied the tech baseline stands, so it degrades cleanly to
    /// plain humans with no DLC.
    ///
    /// <para>Pure by design, like <see cref="AgeStructureRules"/> and <see cref="DemographicsRules"/>:
    /// it works on plain numbers (a tech ordinal and two skew scalars), testable without a game and
    /// identical on every machine. The game-side glue that reads a faction's tech level, its ideology
    /// memes and a xenotype's aptitudes lives in <see cref="RegionDemographicsUtility"/>.</para>
    /// </summary>
    public static class EducationRules
    {
        public const int TierCount = 4;

        // A 0-100 attainment score per tier, used to collapse a distribution to one index for shading.
        private static readonly float[] TierScore = { 0f, 33f, 67f, 100f };

        /// <summary>
        /// The baseline distribution for a tech level, as normalized [illiterate, basic, skilled,
        /// advanced] shares. <paramref name="techLevel"/> is RimWorld's <c>TechLevel</c> ordinal
        /// (Animal=1 … Archotech=7); anything unrecognised falls back to the industrial shape. First-pass
        /// values, tunable.
        /// </summary>
        public static float[] BasePyramid(int techLevel)
        {
            switch (techLevel)
            {
                case 1: // Animal
                case 2: // Neolithic — oral culture, almost no formal schooling
                    return new[] { 0.55f, 0.35f, 0.09f, 0.01f };
                case 3: // Medieval
                    return new[] { 0.40f, 0.40f, 0.17f, 0.03f };
                case 4: // Industrial
                    return new[] { 0.15f, 0.45f, 0.30f, 0.10f };
                case 5: // Spacer
                    return new[] { 0.05f, 0.30f, 0.40f, 0.25f };
                case 6: // Ultra
                    return new[] { 0.02f, 0.20f, 0.43f, 0.35f };
                case 7: // Archotech — near-universally highly educated
                    return new[] { 0.01f, 0.14f, 0.40f, 0.45f };
                default:
                    return new[] { 0.15f, 0.45f, 0.30f, 0.10f };   // treat unknown as industrial
            }
        }

        /// <summary>
        /// The realized distribution for a region: the tech baseline bent by a research skew (positive =
        /// tech/transhumanist ideology lifting attainment, negative = primitivist pulling it down; clamped
        /// to [-1,1]) and a xenotype aptitude skew (0..1, an engineered-intellect caste raising the top).
        /// Weight shifts toward the higher tiers as the combined push rises and toward the lower tiers as
        /// it falls, hinged around the middle of the ladder. Always returns a normalized four-tier array.
        /// </summary>
        public static float[] Pyramid(int techLevel, float researchSkew, float aptitudeSkew)
        {
            float[] p = BasePyramid(techLevel);
            float shift = 0.40f * Clamp(researchSkew, -1f, 1f) + 0.55f * Clamp01(aptitudeSkew);

            // Tier rank runs 0..3; hinge at 1.5 so the two low tiers scale opposite the two high tiers.
            const float hinge = (TierCount - 1) / 2f;
            float sum = 0f;
            var w = new float[TierCount];
            for (int i = 0; i < TierCount; i++)
            {
                float factor = 1f + shift * ((i - hinge) / hinge);
                w[i] = p[i] * (factor < 0f ? 0f : factor);
                sum += w[i];
            }
            if (sum <= 0f) return new[] { 0f, 1f, 0f, 0f };   // degenerate: call it all basic
            for (int i = 0; i < TierCount; i++) w[i] /= sum;
            return w;
        }

        /// <summary>
        /// A single 0-100 education index for a distribution: the share-weighted mean of the per-tier
        /// attainment scores. 0 = wholly illiterate, 100 = wholly advanced. Returns 0 for a null/empty
        /// distribution. This is what the overlay shades by.
        /// </summary>
        public static int Index(float[] pyramid)
        {
            if (pyramid == null || pyramid.Length < TierCount) return 0;
            float total = 0f, acc = 0f;
            for (int i = 0; i < TierCount; i++) { total += pyramid[i]; acc += pyramid[i] * TierScore[i]; }
            if (total <= 0f) return 0;
            return (int)Math.Round(acc / total);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
