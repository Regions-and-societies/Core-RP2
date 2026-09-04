using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The five education levels a region's people are bucketed into (#15/#26), a real-world
    /// schooling ladder from no schooling to a research elite. A structure, not a per-pawn roll — see
    /// <see cref="EducationRules.Profiles"/> for what each level means for a pawn (skills, passion) and
    /// for the economy (the capability it unlocks).</summary>
    public enum EducationTier
    {
        Illiterate = 0,  // no formal schooling
        Primary = 1,     // basic literacy — primary school
        Secondary = 2,   // skilled labour — secondary / vocational
        Undergrad = 3,   // higher education — undergraduate
        Postgrad = 4     // research elite — postgraduate
    }

    /// <summary>What one education level means for the people in it and for the economy (#26 → #28).
    /// The skill range and passion are the guidance a pawn-generation consumer applies (#28); the
    /// economic role is the capability that level unlocks for the regional economy (0.4.0). Plain data,
    /// so it lives in the pure rules layer and is the single source of truth both consumers read.</summary>
    public struct EducationProfile
    {
        public string label;         // the real-world schooling level
        public int skillLow;         // typical skill points a pawn of this level brings to a specialty
        public int skillHigh;
        public string passion;       // how passion tends to show up
        public string economicRole;  // the economic capability this level unlocks
    }

    /// <summary>
    /// The deterministic education-structure core (0.2.0, #15). A region's education distribution — how
    /// its people split across the five levels — is a pure function of a few signals: the dominant
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
        public const int TierCount = 5;

        // A 0-100 attainment score per tier, used to collapse a distribution to one index for shading.
        private static readonly float[] TierScore = { 0f, 25f, 50f, 75f, 100f };

        /// <summary>The meaning of each <see cref="EducationTier"/>, indexed by tier ordinal. First-pass
        /// values, tunable; the skill ranges and passions feed the pawn-generation hook (#28) and the
        /// economic roles gate what the regional economy can do (industrial growth needs Secondary+,
        /// critical-systems resiliency needs Undergrad+, high-tech needs Postgrad).</summary>
        public static readonly EducationProfile[] Profiles =
        {
            new EducationProfile { label = "Illiterate",  skillLow = 0,  skillHigh = 1,  passion = "none",                       economicRole = "Subsistence labour only — cannot operate machinery" },
            new EducationProfile { label = "Primary",     skillLow = 1,  skillHigh = 2,  passion = "none",                       economicRole = "Manual & agricultural labour" },
            new EducationProfile { label = "Secondary",   skillLow = 5,  skillHigh = 6,  passion = "one specialty passion",      economicRole = "Runs industrial workshops — production output" },
            new EducationProfile { label = "Undergrad",   skillLow = 7,  skillHigh = 10, passion = "a burning passion",          economicRole = "Runs & maintains critical systems (hydroponics, power) — industrial growth & resiliency" },
            new EducationProfile { label = "Postgrad",    skillLow = 11, skillHigh = 15, passion = "multiple burning passions",  economicRole = "R&D — unlocks high-tech production & innovation" },
        };

        /// <summary>
        /// The baseline distribution for a tech level, as normalized [illiterate, basic, skilled,
        /// advanced] shares. <paramref name="techLevel"/> is RimWorld's <c>TechLevel</c> ordinal
        /// (Animal=1 … Archotech=7); anything unrecognised falls back to the industrial shape. First-pass
        /// values, tunable.
        /// </summary>
        public static float[] BasePyramid(int techLevel)
        {
            // Shares over [illiterate, primary, secondary, undergrad, postgrad].
            switch (techLevel)
            {
                case 1: // Animal
                case 2: // Neolithic — oral culture, almost no formal schooling
                    return new[] { 0.50f, 0.38f, 0.10f, 0.02f, 0.00f };
                case 3: // Medieval
                    return new[] { 0.35f, 0.40f, 0.20f, 0.05f, 0.00f };
                case 4: // Industrial
                    return new[] { 0.10f, 0.28f, 0.35f, 0.22f, 0.05f };
                case 5: // Spacer
                    return new[] { 0.03f, 0.15f, 0.32f, 0.35f, 0.15f };
                case 6: // Ultra
                    return new[] { 0.01f, 0.08f, 0.25f, 0.40f, 0.26f };
                case 7: // Archotech — a research civilisation
                    return new[] { 0.00f, 0.04f, 0.16f, 0.40f, 0.40f };
                default:
                    return new[] { 0.10f, 0.28f, 0.35f, 0.22f, 0.05f };   // treat unknown as industrial
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
            if (sum <= 0f) return new[] { 0f, 1f, 0f, 0f, 0f };   // degenerate: call it all primary
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
