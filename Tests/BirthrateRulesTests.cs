// Behaviour tests for the settlement birthrate-growth core (#6): additive fertility/mortality factors
// at real scale, the transition shape, birth crowding (full to 100% of cap, stagnant at 150%), and the
// GrowStep that runs population toward/past the cap with mortality balancing. Pure, no game.
using System;
using RegionsAndSocieties.Sizing;

namespace BirthrateRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("factor terms");
            Check("fertility rises with the fertile-age share", BirthrateRules.FertilityRate(0.30f) > BirthrateRules.FertilityRate(0.15f));
            Check("no fertile women -> no births", BirthrateRules.FertilityRate(0f) == 0f);
            Check("medicine lowers mortality (neolithic dies faster than industrial)", BirthrateRules.MortalityRate(2) > BirthrateRules.MortalityRate(4));
            Check("wealth suppresses fertility", BirthrateRules.WealthFertilityPenalty(0f) == 0f && BirthrateRules.WealthFertilityPenalty(1f) > 0f);
            Check("famine only when food is short", BirthrateRules.FamineMortality(1f) == 0f && BirthrateRules.FamineMortality(0.2f) > 0f);

            Section("transition shape (real scale; the slider sets pace)");
            float neo = BirthrateRules.NetAnnualRate(Inputs(2, 0.14f, 0f));            // pre-industrial, poor, young
            float industrializing = BirthrateRules.NetAnnualRate(Inputs(4, 0.12f, 0.3f)); // industrial, developing
            float richSpacer = BirthrateRules.NetAnnualRate(Inputs(5, 0.09f, 1.0f));   // wealthy post-industrial, aging
            Check("industrializing grows fastest (the hump)", industrializing > neo && industrializing > richSpacer);
            Check("pre-industrial still grows", neo > 0f);
            Check("wealthy post-industrial stays positive but lower", richSpacer > 0f && richSpacer < industrializing);
            Check("real-scale rate is small (~1-2%/yr before the multiplier)", industrializing > 0.005f && industrializing < 0.03f);

            Section("factors are additive and degrade gracefully");
            var baseIn = Inputs(4, 0.12f, 0.3f);
            float baseNet = BirthrateRules.NetAnnualRate(baseIn);
            var withNeutral = baseIn; withNeutral.IdeologyBias = 0f; withNeutral.XenotypeBias = 0f;
            Check("neutral (absent) ideology/xenotype leave the rate unchanged", BirthrateRules.NetAnnualRate(withNeutral) == baseNet);
            var natalist = baseIn; natalist.IdeologyBias = 0.005f;
            Check("a natalist ideology raises the rate", BirthrateRules.NetAnnualRate(natalist) > baseNet);
            var barren = baseIn; barren.XenotypeBias = -0.01f;
            Check("a non-breeding xenotype lowers the rate", BirthrateRules.NetAnnualRate(barren) < baseNet);
            var starving = baseIn; starving.FoodBalance = 0.3f;
            Check("a food shortfall lowers the rate (famine mortality)", BirthrateRules.NetAnnualRate(starving) < baseNet);
            var atWar = baseIn; atWar.WarLossRate = 0.01f;
            Check("war/insecurity lowers the rate", BirthrateRules.NetAnnualRate(atWar) < baseNet);
            Check("food/war can push net below zero", BirthrateRules.NetAnnualRate(Inputs(5, 0.09f, 1.0f, 0.2f, 0.02f)) < 0f);

            Section("birth crowding: full to 100% of cap, stagnant at 150%");
            Check("below cap -> full births", BirthrateRules.BirthCrowdingFactor(0.5f) == 1f);
            Check("at cap -> still full births", BirthrateRules.BirthCrowdingFactor(1.0f) == 1f);
            Check("midway (125%) -> half births", Close(BirthrateRules.BirthCrowdingFactor(1.25f), 0.5f, 0.001f));
            Check("at 150% -> births stagnate", BirthrateRules.BirthCrowdingFactor(1.5f) == 0f);
            Check("beyond 150% -> still zero", BirthrateRules.BirthCrowdingFactor(2.0f) == 0f);

            Section("grow step runs toward and past the cap");
            float s = BirthrateRules.GrowStep(2f, 10, 0.15f, 0.03f, 1f);
            Check("a settlement below cap grows", s > 2f);
            Check("zero elapsed time -> no change", BirthrateRules.GrowStep(2f, 10, 0.15f, 0.03f, 0f) == 2f);
            Check("a near-zero settlement seeds and grows", BirthrateRules.GrowStep(0f, 10, 0.15f, 0.03f, 1f) > 0f);
            Check("never exceeds the 150% ceiling", BirthrateRules.GrowStep(20f, 10, 0.15f, 0.03f, 1f) <= 15f);
            Check("never goes negative", BirthrateRules.GrowStep(-5f, 10, 0.15f, 0.03f, 1f) >= 0f);
            Check("untiered (cap 0) -> no growth", BirthrateRules.GrowStep(3f, 0, 0.15f, 0.03f, 1f) == 3f);

            // Well-fed: births far exceed deaths, so it overshoots the cap toward equilibrium ~140%.
            float p = 2f;
            for (int i = 0; i < 400; i++) p = BirthrateRules.GrowStep(p, 10, 0.15f, 0.03f, 1f);
            Check($"well-fed settlement overshoots its cap (got {p:0.0} vs cap 10)", p > 10f);
            Check("...and settles below the 150% ceiling", p <= 15f && p > 12f);

            // Starved: deaths exceed births -> declines below the cap.
            float d = 9f;
            for (int i = 0; i < 40; i++) d = BirthrateRules.GrowStep(d, 10, 0.15f, 0.35f, 1f);
            Check($"a starving settlement declines below its cap (got {d:0.0})", d < 9f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL BIRTHRATE TESTS PASSED" : failures + " BIRTHRATE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static GrowthInputs Inputs(int tech, float fertile, float wealth, float food = 1f, float war = 0f)
        {
            return new GrowthInputs { TechLevel = tech, FertileFraction = fertile, WealthLevel = wealth, FoodBalance = food, WarLossRate = war };
        }

        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
