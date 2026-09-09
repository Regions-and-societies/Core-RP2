// Behaviour tests for the placement-share model (#47): range→share migration, normalised fractions,
// the per-faction estimate, and largest-remainder apportionment. Pure, no game.
using System;
using System.Collections.Generic;
using RegionsAndSocieties.Placement;

namespace PlacementShareRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("baseCountRange → share weight migration = midpoint");
            Check("civil 5..15 -> 10", Eq(PlacementShareRules.MigrateRangeToShareWeight(5, 15), 10f));
            Check("hostile 3..8 -> 5.5", Eq(PlacementShareRules.MigrateRangeToShareWeight(3, 8), 5.5f));
            Check("proportions preserved (civil > hostile)",
                PlacementShareRules.MigrateRangeToShareWeight(5, 15) > PlacementShareRules.MigrateRangeToShareWeight(3, 8));
            Check("degenerate negative -> 0", Eq(PlacementShareRules.MigrateRangeToShareWeight(-4, -2), 0f));

            Section("normalised fraction = weight / total");
            Check("10 of 40 -> 0.25", Eq(PlacementShareRules.NormalizedFraction(10f, 40f), 0.25f));
            Check("zero total -> 0", Eq(PlacementShareRules.NormalizedFraction(10f, 0f), 0f));
            Check("zero weight -> 0", Eq(PlacementShareRules.NormalizedFraction(0f, 40f), 0f));

            Section("placed total = expected regions × claimed-land fraction");
            Check("448 × 0.5 -> 224", PlacementShareRules.PlacedTotal(448, 0.5f) == 224);
            Check("fraction clamped to 1", PlacementShareRules.PlacedTotal(100, 2f) == 100);
            Check("negative fraction -> 0", PlacementShareRules.PlacedTotal(100, -1f) == 0);
            Check("zero regions -> 0", PlacementShareRules.PlacedTotal(0, 0.5f) == 0);

            Section("per-faction estimate = fraction × placed total");
            // share 40 of 100 total, 200 placed -> 80.
            Check("40% of 200 placed -> 80", PlacementShareRules.EstimatedFactionCount(40f, 100f, 200) == 80);
            Check("zero placed -> 0", PlacementShareRules.EstimatedFactionCount(40f, 100f, 0) == 0);
            Check("zero total -> 0", PlacementShareRules.EstimatedFactionCount(40f, 0f, 200) == 0);

            Section("largest-remainder apportionment sums exactly and honours share");
            int[] a = PlacementShareRules.Apportion(new float[] { 40f, 40f, 20f }, 100);
            Check("40/40/20 of 100 -> 40,40,20", a[0] == 40 && a[1] == 40 && a[2] == 20);
            Check("apportionment sums to total", Sum(a) == 100);

            int[] b = PlacementShareRules.Apportion(new float[] { 1f, 1f, 1f }, 10);
            Check("three equal shares of 10 sum to 10", Sum(b) == 10);
            Check("equal shares within one of each other", Max(b) - Min(b) <= 1);

            int[] c = PlacementShareRules.Apportion(new float[] { 40f, 0f, 60f }, 50);
            Check("zero-weight faction gets nothing", c[1] == 0);
            Check("nonzero split sums to total", Sum(c) == 50 && c[0] > 0 && c[2] > 0);

            int[] d = PlacementShareRules.Apportion(new float[] { 1f, 1f }, 0);
            Check("zero total -> all zero", Sum(d) == 0);
            int[] e = PlacementShareRules.Apportion(new float[] { 0f, 0f }, 10);
            Check("all-zero weights -> all zero (no false placement)", Sum(e) == 0);

            // The acceptance shape: share 40% receives ≈ 40% of placed, ±1.
            int[] f = PlacementShareRules.Apportion(new float[] { 40f, 35f, 25f }, 137);
            Check("share 40% -> ~40% of 137 (55) ±1", Math.Abs(f[0] - 55) <= 1);
            Check("acceptance apportionment sums exactly", Sum(f) == 137);

            Section("five size categories map to the 1/2/4/8/16 multiplier scale and back");
            Check("Tiny -> 1", PlacementShareRules.CategoryToRegionCap(ShareCategory.Tiny) == 1);
            Check("Small -> 2", PlacementShareRules.CategoryToRegionCap(ShareCategory.Small) == 2);
            Check("Medium -> 4", PlacementShareRules.CategoryToRegionCap(ShareCategory.Medium) == 4);
            Check("Large -> 8", PlacementShareRules.CategoryToRegionCap(ShareCategory.Large) == 8);
            Check("Very large -> 16", PlacementShareRules.CategoryToRegionCap(ShareCategory.VeryLarge) == 16);
            Check("1 reads Tiny", PlacementShareRules.CategoryForShare(1f) == ShareCategory.Tiny);
            Check("2 reads Small", PlacementShareRules.CategoryForShare(2f) == ShareCategory.Small);
            Check("4 reads Medium", PlacementShareRules.CategoryForShare(4f) == ShareCategory.Medium);
            Check("8 reads Large", PlacementShareRules.CategoryForShare(8f) == ShareCategory.Large);
            Check("16 reads Very large", PlacementShareRules.CategoryForShare(16f) == ShareCategory.VeryLarge);
            Check("a low between-value (1.4) reads Tiny (nearest band)", PlacementShareRules.CategoryForShare(1.4f) == ShareCategory.Tiny);
            Check("a between-value (5) reads Medium", PlacementShareRules.CategoryForShare(5f) == ShareCategory.Medium);
            Check("11 reads Large (just under the Large/VeryLarge midpoint 12)", PlacementShareRules.CategoryForShare(11f) == ShareCategory.Large);
            Check("a huge value still reads Very large", PlacementShareRules.CategoryForShare(100f) == ShareCategory.VeryLarge);
            Check("round-trips: category -> weight -> category", PlacementShareRules.CategoryForShare(PlacementShareRules.CategoryToRegionCap(ShareCategory.Large)) == ShareCategory.Large);
            Check("labels present", PlacementShareRules.CategoryLabel(ShareCategory.Tiny) == "Tiny" && PlacementShareRules.CategoryLabel(ShareCategory.VeryLarge) == "Very large");

            Section("region-count capacity gate");
            Check("half capacity is neither warn nor over", !PlacementShareRules.IsCrowdingWarning(50, 100) && !PlacementShareRules.IsOverCapacity(50, 100));
            Check("80% triggers the crowding warning", PlacementShareRules.IsCrowdingWarning(80, 100));
            Check("just under 80% does not warn", !PlacementShareRules.IsCrowdingWarning(79, 100));
            Check("exactly full is a warning, not over", PlacementShareRules.IsCrowdingWarning(100, 100) && !PlacementShareRules.IsOverCapacity(100, 100));
            Check("over capacity is the hard gate", PlacementShareRules.IsOverCapacity(101, 100) && !PlacementShareRules.IsCrowdingWarning(101, 100));
            Check("capacity fraction", Eq(PlacementShareRules.CapacityFraction(40, 100), 0.4f));
            Check("zero demand is 0", Eq(PlacementShareRules.CapacityFraction(0, 100), 0f));
            Check("unknown capacity reads fully used", Eq(PlacementShareRules.CapacityFraction(10, 0), 1f) && !PlacementShareRules.IsOverCapacity(10, 0));

            Section("value mode: COUNT — the numbers are literal region targets");
            var counts = new List<float> { 5f, 3f, 7f };   // demand 15
            Check("demand is the plain sum", PlacementShareRules.DemandRegions(PlacementValueMode.Count, PlacementPercentBasis.SettledNormalized, counts, 100, 0.5f) == 15);
            Check("distribute returns the counts unchanged when they fit", Same(PlacementShareRules.DistributeRegions(PlacementValueMode.Count, PlacementPercentBasis.SettledNormalized, counts, 100, 0.5f), new[] { 5, 3, 7 }));
            Check("over capacity scales down to fit, summing exactly", Sum(PlacementShareRules.DistributeRegions(PlacementValueMode.Count, PlacementPercentBasis.SettledNormalized, counts, 10, 0.5f)) == 10);
            Check("count ignores the density knob", Same(PlacementShareRules.DistributeRegions(PlacementValueMode.Count, PlacementPercentBasis.SettledNormalized, counts, 100, 0.2f), new[] { 5, 3, 7 }));

            Section("value mode: PERCENT / SettledNormalized — relative shares fill the claimed area");
            var weights = new List<float> { 1f, 1f, 2f };   // 25% / 25% / 50%
            // Claimed total = 40% of 100 = 40 regions; split 10/10/20.
            Check("normalized demand = density-scaled total, never over", PlacementShareRules.DemandRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, weights, 100, 0.4f) == 40);
            Check("normalized split is proportional to weights", Same(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, weights, 100, 0.4f), new[] { 10, 10, 20 }));
            Check("self-scales: same weights fill a SMALL planet fully too", Sum(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, weights, 20, 0.5f)) == 10);
            Check("normalized never exceeds the planet (no over-capacity)", !PlacementShareRules.IsOverCapacity(PlacementShareRules.DemandRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, weights, 100, 0.9f), 100));

            Section("minimum-one guarantee: a tiny faction beside giants still lands >= 1 (the 1/2/4/8/16 clamp)");
            var giants = new List<float> { 16f, 1f, 16f };   // very large / tiny / very large — the owner's example
            var gdist = PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, giants, 100, 1.0f);
            Check("every faction gets at least 1", gdist[0] >= 1 && gdist[1] >= 1 && gdist[2] >= 1);
            Check("the tiny faction is not starved to 0", gdist[1] >= 1);
            Check("the giants dwarf the tiny one but all placed", gdist[0] > gdist[1] && gdist[2] > gdist[1] && Sum(gdist) == 100);
            // A brutal disparity where the exact share would round below one.
            var starve = new List<float> { 100f, 1f };
            var sdist = PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, starve, 10, 1.0f);
            Check("weight-1 faction among a 100-weight one still gets >= 1", sdist[1] >= 1 && Sum(sdist) == 10);
            Check("more factions than regions -> each still 1 (demand flags over-capacity)", Min(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, new List<float> { 1f, 1f, 1f, 1f, 1f }, 3, 1.0f)) == 1
                && PlacementShareRules.IsOverCapacity(PlacementShareRules.DemandRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, new List<float> { 1f, 1f, 1f, 1f, 1f }, 3, 1.0f), 3));

            Section("value mode: PERCENT / PlanetAbsolute — shares are absolute planet percent");
            var pct = new List<float> { 20f, 30f };   // 50% of the planet claimed, 50% wilderness
            Check("absolute demand = sum of percent × planet", PlacementShareRules.DemandRegions(PlacementValueMode.Percent, PlacementPercentBasis.PlanetAbsolute, pct, 100, 0.5f) == 50);
            Check("absolute split maps each percent to regions", Same(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.PlanetAbsolute, pct, 100, 0.5f), new[] { 20, 30 }));
            Check("absolute over 100% is over capacity and caps at the planet", PlacementShareRules.IsOverCapacity(PlacementShareRules.DemandRegions(PlacementValueMode.Percent, PlacementPercentBasis.PlanetAbsolute, new List<float> { 70f, 60f }, 100, 0.5f), 100)
                && Sum(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.PlanetAbsolute, new List<float> { 70f, 60f }, 100, 0.5f)) == 100);
            Check("absolute ignores the density knob", Same(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.PlanetAbsolute, pct, 100, 0.1f), new[] { 20, 30 }));

            Section("wilderness = planet minus everything placed");
            Check("wilderness is the remainder", PlacementShareRules.WildernessRegions(100, new[] { 20, 30 }) == 50);
            Check("full claim leaves no wilderness", PlacementShareRules.WildernessRegions(40, new[] { 10, 10, 20 }) == 0);
            Check("wilderness never negative", PlacementShareRules.WildernessRegions(10, new[] { 8, 8 }) == 0);
            Check("degenerate zero planet places nothing", PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, weights, 0, 0.5f).Length == 3 && Sum(PlacementShareRules.DistributeRegions(PlacementValueMode.Percent, PlacementPercentBasis.SettledNormalized, weights, 0, 0.5f)) == 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PLACEMENT-SHARE TESTS PASSED" : failures + " PLACEMENT-SHARE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Eq(float a, float b) => Math.Abs(a - b) < 0.0001f;
        private static bool Same(int[] a, int[] b) { if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static int Sum(int[] a) { int s = 0; foreach (int x in a) s += x; return s; }
        private static int Max(int[] a) { int m = int.MinValue; foreach (int x in a) if (x > m) m = x; return m; }
        private static int Min(int[] a) { int m = int.MaxValue; foreach (int x in a) if (x < m) m = x; return m; }

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
