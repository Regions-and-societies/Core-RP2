// Behaviour tests for the world-maturity scaling behind holding seeding (#18): base allowance × maturity,
// the "never round a positive allowance down to zero unless maturity is 0" rule, and the slider labels.
// Pure, no game.
using System;
using RegionsAndSocieties.Sizing;

namespace SeedingMaturityRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("allowance scales with maturity");
            Check("full maturity keeps the whole allowance", SeedingMaturityRules.ScaleAllowance(6, 1f) == 6);
            Check("half maturity halves (6 -> 3)", SeedingMaturityRules.ScaleAllowance(6, 0.5f) == 3);
            Check("quarter maturity (6 -> 2, rounded)", SeedingMaturityRules.ScaleAllowance(6, 0.25f) == 2);
            Check("above 1 clamps to full", SeedingMaturityRules.ScaleAllowance(4, 2f) == 4);

            Section("zero maturity is the only value that yields nothing");
            Check("maturity 0 -> 0", SeedingMaturityRules.ScaleAllowance(6, 0f) == 0);
            Check("negative maturity -> 0", SeedingMaturityRules.ScaleAllowance(6, -1f) == 0);
            Check("tiny maturity still seeds a token 1 (2 × 0.1)", SeedingMaturityRules.ScaleAllowance(2, 0.1f) == 1);
            Check("tiny maturity on a big allowance still rounds up off zero", SeedingMaturityRules.ScaleAllowance(6, 0.05f) == 1);

            Section("degenerate allowances");
            Check("zero base -> 0 at any maturity", SeedingMaturityRules.ScaleAllowance(0, 1f) == 0);
            Check("negative base -> 0", SeedingMaturityRules.ScaleAllowance(-3, 1f) == 0);

            Section("slider labels");
            Check("0 -> Off", SeedingMaturityRules.Label(0f) == "Off");
            Check(".25 -> Frontier", SeedingMaturityRules.Label(0.25f) == "Frontier");
            Check(".5 -> Developing", SeedingMaturityRules.Label(0.5f) == "Developing");
            Check(".75 -> Established", SeedingMaturityRules.Label(0.75f) == "Established");
            Check("1 -> Fully settled", SeedingMaturityRules.Label(1f) == "Fully settled");
            Check("default constant is Developing", SeedingMaturityRules.Label(SeedingMaturityRules.DefaultMaturity) == "Developing");

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SEEDING-MATURITY TESTS PASSED" : failures + " SEEDING-MATURITY TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
