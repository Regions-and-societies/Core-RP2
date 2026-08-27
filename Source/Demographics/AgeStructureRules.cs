using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The three age classes a region's people are bucketed into (#10). Deliberately coarse —
    /// the model is a structure, not a per-pawn age roll.</summary>
    public enum AgeBucket
    {
        Child = 0,       // pre-working dependents
        WorkingAge = 1,  // the productive majority
        Elder = 2        // past working age
    }

    /// <summary>
    /// The deterministic age-structure core (0.2.0, #10). A region's age pyramid — the split between
    /// children, working-age adults and elders — is a pure function of a few signals: the dominant
    /// faction's tech level (tribal societies run birth-heavy pyramids, spacer societies run flat,
    /// long-lived ones), an ideology natalist skew, and a xenotype longevity skew where Biotech gives
    /// a caste unusual lifespans. No signal is required: with none supplied the base tech pyramid
    /// stands, so the model degrades cleanly to plain humans with no DLC.
    ///
    /// <para>Pure by design, like <see cref="DemographicsRules"/> and <see cref="Sizing.TierPyramidRules"/>:
    /// it works on plain numbers (a tech-level ordinal and two skew scalars), so the whole thing is
    /// testable without a game and identical on every machine. The game-side glue that reads a
    /// faction's tech level, its ideology and a xenotype's longevity lives in
    /// <see cref="RegionDemographicsUtility"/>; only the shaping maths is here.</para>
    /// </summary>
    public static class AgeStructureRules
    {
        public const int BucketCount = 3;

        // Age boundaries between the buckets, in years. A child becomes working-age at 13 (RimWorld's
        // biological adulthood) and an elder at 65. The top of the elder band is not fixed: longevity
        // stretches it (below), which is how a long-lived caste reads as genuinely older rather than
        // merely "more elders".
        public const int ChildMaxAge = 13;
        public const int ElderMinAge = 65;
        private const int BaseElderMaxAge = 90;
        private const int LongevityElderStretch = 55;   // full-longevity elders reach ~145

        /// <summary>
        /// The baseline pyramid for a tech level, as normalized [child, working, elder] shares. The
        /// <paramref name="techLevel"/> is RimWorld's <c>TechLevel</c> ordinal (Animal=1 … Archotech=7);
        /// anything unrecognised falls back to the industrial shape. First-pass values, tunable.
        /// </summary>
        public static float[] BasePyramid(int techLevel)
        {
            switch (techLevel)
            {
                case 1: // Animal
                case 2: // Neolithic — tribal high-birthrate pyramid: many children, few elders
                    return new[] { 0.40f, 0.52f, 0.08f };
                case 3: // Medieval
                    return new[] { 0.34f, 0.55f, 0.11f };
                case 4: // Industrial
                    return new[] { 0.26f, 0.60f, 0.14f };
                case 5: // Spacer — flatter: low birthrate, more elders
                    return new[] { 0.18f, 0.60f, 0.22f };
                case 6: // Ultra
                    return new[] { 0.14f, 0.58f, 0.28f };
                case 7: // Archotech — near-post-mortal: few children, elder-heavy
                    return new[] { 0.12f, 0.56f, 0.32f };
                default:
                    return new[] { 0.26f, 0.60f, 0.14f };   // treat unknown as industrial
            }
        }

        /// <summary>
        /// The realized pyramid for a region: the tech baseline bent by a natalist skew (pro-natalist
        /// ideology pushes weight toward children and away from elders) and a longevity skew (a
        /// long-lived xenotype caste holds more elders). Both skews are clamped to [0,1]; 0 leaves the
        /// baseline untouched. Always returns a normalized [child, working, elder].
        /// </summary>
        public static float[] Pyramid(int techLevel, float natalistSkew, float longevitySkew)
        {
            float[] p = BasePyramid(techLevel);
            float natal = Clamp01(natalistSkew);
            float longev = Clamp01(longevitySkew);

            float child = p[0] * (1f + 0.60f * natal);
            float working = p[1];
            float elder = p[2] * (1f - 0.30f * natal) * (1f + 1.20f * longev);

            if (child < 0f) child = 0f;
            if (working < 0f) working = 0f;
            if (elder < 0f) elder = 0f;

            float sum = child + working + elder;
            if (sum <= 0f) return new[] { 0f, 1f, 0f };   // degenerate: call it all working-age
            return new[] { child / sum, working / sum, elder / sum };
        }

        /// <summary>
        /// The median age implied by a [child, working, elder] pyramid: the age at which the cumulative
        /// share crosses one half, interpolated inside whichever band contains it. <paramref name="longevitySkew"/>
        /// stretches the top of the elder band so a long-lived population reads older. Returns 0 for a
        /// null or empty pyramid.
        /// </summary>
        public static int MedianAge(float[] pyramid, float longevitySkew = 0f)
        {
            if (pyramid == null || pyramid.Length < BucketCount) return 0;
            float child = pyramid[0], working = pyramid[1], elder = pyramid[2];
            float total = child + working + elder;
            if (total <= 0f) return 0;

            float half = total * 0.5f;
            int elderMax = BaseElderMaxAge + (int)(Clamp01(longevitySkew) * LongevityElderStretch);

            if (half <= child)
                return (int)Math.Round((half / child) * ChildMaxAge);

            if (half <= child + working)
            {
                float into = (half - child) / working;
                return (int)Math.Round(ChildMaxAge + into * (ElderMinAge - ChildMaxAge));
            }

            float intoElder = elder > 0f ? (half - child - working) / elder : 0f;
            return (int)Math.Round(ElderMinAge + intoElder * (elderMax - ElderMinAge));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
