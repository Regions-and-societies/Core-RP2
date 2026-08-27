using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The occupation sectors a region's workforce splits across (#16).</summary>
    public enum OccupationSector
    {
        Agriculture = 0,  // farming, foraging, herding — working the land
        Industry = 1,     // mining, crafting, manufacture
        Military = 2,     // garrisons, standing forces
        Trade = 3         // merchants, hauling, services
    }

    /// <summary>
    /// The deterministic employment core (0.2.0, #16). Unlike the age/education axes this is not a
    /// per-tile draw: a region's occupation mix and employment rate follow from region-level facts — its
    /// dominant faction's tech level, the mix of world objects on it (a military base pulls toward the
    /// military sector, extraction outposts toward industry, cities toward trade), and what its terrain
    /// supports (fertile land toward agriculture, mineral-rich toward industry). The caller measures
    /// those signals from the world; this blends them into a normalized sector distribution and an
    /// employment rate.
    ///
    /// <para>Pure by design, like the other *Rules: signal scalars in, shares/rate out, no game state,
    /// testable without a game.</para>
    /// </summary>
    public static class EmploymentRules
    {
        public const int SectorCount = 4;

        /// <summary>
        /// The baseline sector split for a tech level, as normalized [agriculture, industry, military,
        /// trade] shares — a tribe works the land, an industrial polity the factories, a spacer one
        /// splits into industry and trade. <paramref name="techLevel"/> is RimWorld's TechLevel ordinal;
        /// unknown falls back to industrial. First-pass, tunable.
        /// </summary>
        public static float[] BaseSectors(int techLevel)
        {
            switch (techLevel)
            {
                case 1: // Animal
                case 2: // Neolithic
                    return new[] { 0.60f, 0.10f, 0.20f, 0.10f };
                case 3: // Medieval
                    return new[] { 0.50f, 0.20f, 0.20f, 0.10f };
                case 4: // Industrial
                    return new[] { 0.30f, 0.40f, 0.15f, 0.15f };
                case 5: // Spacer
                    return new[] { 0.18f, 0.40f, 0.20f, 0.22f };
                case 6: // Ultra
                case 7: // Archotech
                    return new[] { 0.12f, 0.45f, 0.20f, 0.23f };
                default:
                    return new[] { 0.30f, 0.40f, 0.15f, 0.15f };   // treat unknown as industrial
            }
        }

        /// <summary>
        /// The realized sector distribution: the tech baseline scaled by each sector's region signal
        /// (all >= 0, 0 = no push). A military presence weighs heaviest, since a garrison region really is
        /// dominated by soldiering; the land and trade signals push more gently. Always returns a
        /// normalized four-sector array.
        /// </summary>
        public static float[] SectorShares(int techLevel, float agSignal, float indSignal, float milSignal, float tradeSignal)
        {
            float[] b = BaseSectors(techLevel);
            var w = new float[SectorCount];
            w[(int)OccupationSector.Agriculture] = b[0] * (1f + 0.8f * NonNeg(agSignal));
            w[(int)OccupationSector.Industry] = b[1] * (1f + 0.8f * NonNeg(indSignal));
            w[(int)OccupationSector.Military] = b[2] * (1f + 2.0f * NonNeg(milSignal));
            w[(int)OccupationSector.Trade] = b[3] * (1f + 1.0f * NonNeg(tradeSignal));

            float sum = w[0] + w[1] + w[2] + w[3];
            if (sum <= 0f) return new[] { 0.25f, 0.25f, 0.25f, 0.25f };
            for (int i = 0; i < SectorCount; i++) w[i] /= sum;
            return w;
        }

        /// <summary>
        /// The share of working-age people in formal employment, 0-100. Rises with tech (developed
        /// economies employ more formally) and with <paramref name="developmentSignal"/> (0..1, how
        /// built-up the region is). First-pass, tunable.
        /// </summary>
        public static int EmploymentRate(int techLevel, float developmentSignal)
        {
            int rate = BaseEmployment(techLevel) + (int)Math.Round(Clamp01(developmentSignal) * 15f);
            return rate < 0 ? 0 : (rate > 100 ? 100 : rate);
        }

        private static int BaseEmployment(int techLevel)
        {
            switch (techLevel)
            {
                case 1:
                case 2: return 55;
                case 3: return 58;
                case 4: return 66;
                case 5: return 74;
                case 6:
                case 7: return 80;
                default: return 62;
            }
        }

        private static float NonNeg(float v) => v < 0f ? 0f : v;
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
