// Behaviour tests for the deterministic demographics core (0.8, #36): the per-tile seed, the RNG,
// weighted picking, ranges and the median. All pure, so this runs without a game — and it is what
// guarantees a tile's people regenerate identically on every machine and every load.
using System;
using RegionsAndSocieties.Demographics;

namespace DemographicsRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("tile seed is deterministic and well-separated");
            Check("same inputs -> same seed", DemographicsRules.TileSeed(42, 100) == DemographicsRules.TileSeed(42, 100));
            Check("different tile -> different seed", DemographicsRules.TileSeed(42, 100) != DemographicsRules.TileSeed(42, 101));
            Check("different world -> different seed", DemographicsRules.TileSeed(42, 100) != DemographicsRules.TileSeed(43, 100));
            Check("different salt -> different seed", DemographicsRules.TileSeed(42, 100, 1) != DemographicsRules.TileSeed(42, 100, 2));
            Check("never zero (even for seed 0, tile 0)", DemographicsRules.TileSeed(0, 0) != 0u);

            Section("weighted pick respects the weights");
            Check("all weight on index 0", AlwaysPicks(new float[] { 1f, 0f, 0f }, 0));
            Check("all weight on last index", AlwaysPicks(new float[] { 0f, 0f, 1f }, 2));
            uint s1 = DemographicsRules.TileSeed(1, 1);
            Check("all-zero weights -> -1", DemographicsRules.WeightedPick(ref s1, new float[] { 0f, 0f }) == -1);
            Check("null weights -> -1", DemographicsRules.WeightedPick(ref s1, null) == -1);

            // Distribution: index 0 has half the weight of {2,1,1}; over many draws it should land ~50%.
            int[] counts = new int[3];
            uint s2 = DemographicsRules.TileSeed(99, 99);
            for (int i = 0; i < 4000; i++)
            {
                int idx = DemographicsRules.WeightedPick(ref s2, new float[] { 2f, 1f, 1f });
                counts[idx]++;
            }
            float frac0 = counts[0] / 4000f;
            Check($"index 0 is ~50% of draws (got {frac0:0.00})", frac0 > 0.44f && frac0 < 0.56f);
            Check("every index gets some draws", counts[0] > 0 && counts[1] > 0 && counts[2] > 0);

            Section("range int stays in bounds");
            bool inBounds = true;
            uint s3 = DemographicsRules.TileSeed(7, 7);
            for (int i = 0; i < 2000; i++)
            {
                int v = DemographicsRules.RangeInt(ref s3, 5, 10);
                if (v < 5 || v > 10) { inBounds = false; break; }
            }
            Check("RangeInt(5,10) always in [5,10]", inBounds);
            uint s4 = DemographicsRules.TileSeed(8, 8);
            Check("degenerate range returns min", DemographicsRules.RangeInt(ref s4, 12, 12) == 12);

            Section("median");
            Check("odd sample", DemographicsRules.Median(new int[] { 3, 1, 2 }, 3) == 2);
            Check("even sample averages the middle two", DemographicsRules.Median(new int[] { 4, 1, 3, 2 }, 4) == 2);
            Check("empty sample -> 0", DemographicsRules.Median(new int[] { }, 0) == 0);
            Check("single value", DemographicsRules.Median(new int[] { 77 }, 1) == 77);

            Section("cosine similarity (belief-mix comparison)");
            Check("identical vectors -> 1", Approx(DemographicsRules.Cosine(new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }), 1f));
            Check("proportional vectors -> 1", Approx(DemographicsRules.Cosine(new[] { 1f, 2f }, new[] { 2f, 4f }), 1f));
            Check("orthogonal vectors -> 0", Approx(DemographicsRules.Cosine(new[] { 1f, 0f }, new[] { 0f, 1f }), 0f));
            float partial = DemographicsRules.Cosine(new[] { 1f, 1f, 0f }, new[] { 1f, 0f, 0f });
            Check($"partial overlap is between 0 and 1 (got {partial:0.00})", partial > 0.4f && partial < 0.9f);
            Check("all-zero -> 0", DemographicsRules.Cosine(new[] { 0f, 0f }, new[] { 0f, 0f }) == 0f);
            Check("mismatched length -> 0", DemographicsRules.Cosine(new[] { 1f }, new[] { 1f, 1f }) == 0f);
            Check("null -> 0", DemographicsRules.Cosine(null, new[] { 1f }) == 0f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL DEMOGRAPHICS-RULE TESTS PASSED" : failures + " DEMOGRAPHICS-RULE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // A pick is deterministic when only one index has positive weight, whatever the RNG state.
        private static bool AlwaysPicks(float[] weights, int expected)
        {
            uint state = DemographicsRules.TileSeed(123, 456);
            for (int i = 0; i < 50; i++)
                if (DemographicsRules.WeightedPick(ref state, weights) != expected) return false;
            return true;
        }

        private static bool Approx(float a, float b) => Math.Abs(a - b) < 0.0005f;

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
