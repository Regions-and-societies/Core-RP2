// Behaviour tests for the population-cap core, and in particular the seeding contract that #71 broke:
// a non-positive capacity means "no tier-imposed cap", never "room for nobody". Pure, no game.
using System;
using RegionsAndSocieties.Sizing;

namespace PopulationCapRulesTests
{
    public static class Program
    {
        private static int failures;
        private const float Fraction = 1f;
        private const float Floor = 1f;

        public static int Main()
        {
            Section("caps and targets by tier");
            Check("a tierless holding has no cap", PopulationCapRules.MaxPopulation(SettlementTier.None, 30f, 1f) == 0);
            Check("a village caps above zero", PopulationCapRules.MaxPopulation(SettlementTier.Village, 30f, 1f) > 0);
            Check("caps rise with tier", PopulationCapRules.MaxPopulation(SettlementTier.Metropolis, 30f, 1f)
                > PopulationCapRules.MaxPopulation(SettlementTier.Village, 30f, 1f));
            Check("the target is two-thirds of the cap",
                Near(PopulationCapRules.TargetPopulation(SettlementTier.City, 30f, 1f),
                     (int)Math.Round(PopulationCapRules.MaxPopulation(SettlementTier.City, 30f, 1f) * 2f / 3f), 1));
            Check("a nonsense multiplier caps at nothing", PopulationCapRules.MaxPopulation(SettlementTier.City, 0f, 1f) == 0);

            Section("seeding — the #71 contract");
            // The bug: an untiered settlement has capacity 0, and seeding read that as an empty settlement.
            // Settlement tiers are OFF by default, so this emptied every NPC settlement on the planet and
            // with it the whole demographic pressure field.
            Check("an untiered settlement seeds from the fallback, not from zero",
                Near(PopulationCapRules.SeedPopulation(0, 90, Fraction, Floor), 90f, 0.01f));
            Check("a negative capacity is treated the same way",
                Near(PopulationCapRules.SeedPopulation(-5, 90, Fraction, Floor), 90f, 0.01f));
            Check("an untiered settlement is never empty",
                PopulationCapRules.SeedPopulation(0, 90, Fraction, Floor) > 0f);
            Check("with no fallback either, it still holds at least the floor",
                Near(PopulationCapRules.SeedPopulation(0, 0, Fraction, Floor), Floor, 0.01f));
            Check("a nonsense fallback cannot push it below the floor",
                PopulationCapRules.SeedPopulation(0, -50, Fraction, Floor) >= Floor);

            Section("seeding — the tiered path is unchanged");
            Check("a tiered settlement seeds from its capacity, not the fallback",
                Near(PopulationCapRules.SeedPopulation(120, 90, Fraction, Floor), 120f, 0.01f));
            Check("the seed fraction applies", Near(PopulationCapRules.SeedPopulation(120, 90, 0.5f, Floor), 60f, 0.01f));
            Check("the floor still applies to a tiny capacity",
                Near(PopulationCapRules.SeedPopulation(1, 0, 0.1f, Floor), Floor, 0.01f));
            Check("a nonsense fraction falls back to the target fraction rather than collapsing",
                Near(PopulationCapRules.SeedPopulation(120, 90, 0f, Floor), 120f * PopulationCapRules.TargetFraction, 0.5f));
            Check("a bigger capacity seeds bigger",
                PopulationCapRules.SeedPopulation(300, 90, Fraction, Floor) > PopulationCapRules.SeedPopulation(120, 90, Fraction, Floor));
            Check("a negative floor is treated as no floor",
                PopulationCapRules.SeedPopulation(0, 0, Fraction, -3f) >= 0f);

            Console.WriteLine();
            if (failures == 0) { Console.WriteLine("ALL POPULATION CAP TESTS PASSED"); return 0; }
            Console.WriteLine(failures + " POPULATION CAP TEST(S) FAILED");
            return 1;
        }

        private static bool Near(float a, float b, float tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static bool Near(int a, int b, int tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string what, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        }
    }
}
