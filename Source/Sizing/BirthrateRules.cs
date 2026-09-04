using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The factor inputs a settlement's net growth is built from (#6). Plain data with neutral
    /// defaults, so the game side fills in only what it can measure and a factor whose source is absent
    /// (a DLC not installed, a system not present) simply contributes nothing. Every field is
    /// independent and <b>additive</b>: fertility and mortality are each a sum of terms.
    /// </summary>
    public struct GrowthInputs
    {
        /// <summary>Share of the population that is fertile-age women (from the region age structure),
        /// 0..1. Drives the fertility term.</summary>
        public float FertileFraction;

        /// <summary>Faction tech level as an int (RimWorld <c>TechLevel</c>: 2 Neolithic .. 5 Spacer).
        /// Drives the mortality term — medicine lowers the death rate, which powers the transition.</summary>
        public int TechLevel;

        /// <summary>Development / wealth of the region, 0..1 (from the socioeconomic tier). Higher wealth
        /// suppresses fertility — the late-transition decline. 0 if unknown.</summary>
        public float WealthLevel;

        /// <summary>Ideology natalist bias as a direct additive rate (+ pro-fertility, − anti). 0 when
        /// Ideology is absent or the ideoligion is fertility-neutral.</summary>
        public float IdeologyBias;

        /// <summary>Xenotype reproduction bias as a direct additive rate. 0 without Biotech / baseliner.</summary>
        public float XenotypeBias;

        /// <summary>Food adequacy, 1 = fully fed, &lt;1 = shortfall (from the region resource model, #7).
        /// A shortfall adds famine mortality. Defaults to fed (1).</summary>
        public float FoodBalance;

        /// <summary>Recent annual fractional loss from war/raids and insecurity, added as mortality.
        /// 0 when nothing threatens the settlement.</summary>
        public float WarLossRate;
    }

    /// <summary>
    /// Birthrate-informed growth of a settlement's modeled population (#6), as an additive factor model
    /// split into a <b>fertility</b> rate and a <b>mortality</b> rate. Births grow the population at full
    /// rate up to 100% of the passed <b>capacity</b> — the comfortable/target size — taper between 100%
    /// and 150% (crowding), and stagnate past 150%; the balance point is set by mortality, which food and
    /// security drive (famine, war). So a well-fed, secure settlement crowds above its target toward the
    /// 150% ceiling and a starved or besieged one falls. The caller decides what capacity means: the game
    /// passes the ⅔-max target, so 150% of it lands exactly on the tier max (the hard ceiling).
    ///
    /// <para>Rates here are at REAL-WORLD scale; the game applies a player-set multiplier (default 10×)
    /// for pacing, scaling births and deaths together so the balance point is unchanged and only the
    /// speed moves. Pure — plain numbers in and out, no game state, no Unity — so it is unit-tested
    /// without a game, and the same call drives both the live tick and the debug fast-forward. For the
    /// player it only informs R&amp;T's model; it never adds or removes real colonists.</para>
    /// </summary>
    public static class BirthrateRules
    {
        /// <summary>A modeled settlement holds at least this population, so growth can start from a
        /// fresh (near-zero) settlement.</summary>
        public const float SeedFloor = 1f;

        /// <summary>Typical fertile-age-women share when the age structure is unknown — roughly
        /// working-age ⅔ × female ½ × fertile ⅔ ≈ a tenth of the population.</summary>
        public const float DefaultFertileFraction = 0.12f;

        /// <summary>Births run at full rate up to this multiple of capacity, then taper.</summary>
        public const float FullBirthCapacityRatio = 1.0f;
        /// <summary>Births reach zero (stagnate) at this multiple of capacity; also the hard ceiling.</summary>
        public const float BirthStagnationRatio = 1.5f;

        // Real-world-scale annual rates (the game multiplies by the pacing slider). The fertility /
        // mortality SPLIT gives the demographic-transition shape; food and war feed mortality.
        private const float BirthsPerFertileWomanYear = 0.16f;   // fertility scale
        private const float WealthFertilityPenaltyMax = 0.006f;  // full-wealth fertility suppression
        private const float FamineMortalityMax = 0.018f;         // total starvation death rate
        private const float MaxNetPerYear = 0.8f;                // per-step stability rail on the net rate

        // --- individual additive factors ---

        /// <summary>Fertility term: births per capita per year, proportional to the fertile-age-women
        /// share.</summary>
        public static float FertilityRate(float fertileFraction)
        {
            if (fertileFraction < 0f) fertileFraction = 0f;
            return fertileFraction * BirthsPerFertileWomanYear;
        }

        /// <summary>Mortality term: deaths per capita per year by tech level. Medicine lowers it.</summary>
        public static float MortalityRate(int techLevel)
        {
            switch (techLevel)
            {
                case 2: return 0.0090f;   // Neolithic
                case 3: return 0.0060f;   // Medieval
                case 4: return 0.0030f;   // Industrial
                case 5: return 0.0025f;   // Spacer
                case 6:
                case 7: return 0.0020f;   // Ultra / Archotech
                default: return 0.0045f;
            }
        }

        /// <summary>Wealth-driven fertility decline (the late-transition fall). 0 at subsistence.</summary>
        public static float WealthFertilityPenalty(float wealthLevel)
        {
            if (wealthLevel <= 0f) return 0f;
            if (wealthLevel > 1f) wealthLevel = 1f;
            return wealthLevel * WealthFertilityPenaltyMax;
        }

        /// <summary>Famine mortality from a food shortfall: 0 when fed, rising as food runs out.</summary>
        public static float FamineMortality(float foodBalance)
        {
            if (foodBalance >= 1f) return 0f;
            if (foodBalance < 0f) foodBalance = 0f;
            return (1f - foodBalance) * FamineMortalityMax;
        }

        /// <summary>The births rate (per capita, per year, at real scale) before crowding: fertility
        /// minus the wealth penalty, plus ideology/xenotype biases, never below zero.</summary>
        public static float Fertility(GrowthInputs g)
        {
            float f = FertilityRate(g.FertileFraction) - WealthFertilityPenalty(g.WealthLevel) + g.IdeologyBias + g.XenotypeBias;
            return f < 0f ? 0f : f;
        }

        /// <summary>The deaths rate (per capita, per year, at real scale): base mortality plus famine
        /// and war/insecurity. Food and security enter here.</summary>
        public static float Mortality(GrowthInputs g)
        {
            return MortalityRate(g.TechLevel) + FamineMortality(g.FoodBalance) + (g.WarLossRate > 0f ? g.WarLossRate : 0f);
        }

        /// <summary>Headline net annual rate below capacity (density 1): fertility − mortality, at real
        /// scale. For readouts; growth itself uses <see cref="GrowStep"/> with crowding applied.</summary>
        public static float NetAnnualRate(GrowthInputs g)
        {
            return Fertility(g) - Mortality(g);
        }

        /// <summary>Convenience net rate from a tech level alone (default fertile share, subsistence,
        /// fed, no DLC factors) — the simplest caller / a fallback.</summary>
        public static float AnnualGrowthRate(int techLevel)
        {
            return NetAnnualRate(new GrowthInputs { FertileFraction = DefaultFertileFraction, TechLevel = techLevel, FoodBalance = 1f });
        }

        /// <summary>The crowding factor on births at a given population/capacity ratio: full (1) up to
        /// 100% of capacity, a linear taper to 0 across 100%→150%, and 0 (stagnant) beyond.</summary>
        public static float BirthCrowdingFactor(float populationOverCapacity)
        {
            if (populationOverCapacity <= FullBirthCapacityRatio) return 1f;
            if (populationOverCapacity >= BirthStagnationRatio) return 0f;
            return (BirthStagnationRatio - populationOverCapacity) / (BirthStagnationRatio - FullBirthCapacityRatio);
        }

        /// <summary>
        /// Advance a modeled population one step over <paramref name="yearsElapsed"/> years. Births
        /// (<paramref name="fertility"/>, already pace-scaled) run at full rate to 100% of
        /// <paramref name="capacity"/>, taper to zero by 150%, and stagnate beyond; deaths
        /// (<paramref name="mortality"/>) apply always. The net drives exponential change, so a well-fed
        /// settlement climbs past its cap toward ~150% and a starved/besieged one declines. Clamped to
        /// <c>[0, 1.5 × capacity]</c>; a near-zero settlement seeds only when it is actually growing.
        /// </summary>
        public static float GrowStep(float current, int capacity, float fertility, float mortality, float yearsElapsed)
        {
            float p = current < 0f ? 0f : current;
            if (capacity <= 0 || yearsElapsed <= 0f) return p <= 0f ? 0f : p;

            float ceil = capacity * BirthStagnationRatio;
            float ratio = p / capacity;
            float births = (fertility < 0f ? 0f : fertility) * BirthCrowdingFactor(ratio);
            float net = births - (mortality < 0f ? 0f : mortality);
            if (net > MaxNetPerYear) net = MaxNetPerYear;
            else if (net < -MaxNetPerYear) net = -MaxNetPerYear;

            if (net > 0f && p < SeedFloor) p = SeedFloor;   // seed a fresh settlement so growth can start
            float next = p + net * p * yearsElapsed;

            if (next < 0f) next = 0f;
            if (next > ceil) next = ceil;
            return next;
        }
    }
}
