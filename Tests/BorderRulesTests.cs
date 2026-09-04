// Behaviour tests for the border-first partition core (#20): boundary strength from tile-definition
// shifts, wall thresholding, anchor sizing/spacing, undersized-merge, and weakest-border merge target.
// Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Partition;

namespace BorderRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            var w = BoundaryWeights.Default;

            Section("boundary strength — hard walls");
            Check("land vs water -> infinite", float.IsPositiveInfinity(BorderRules.BoundaryStrength(Land(), Water(), w)));
            Check("land vs impassable -> infinite", float.IsPositiveInfinity(BorderRules.BoundaryStrength(Land(), Impassable(), w)));

            Section("boundary strength — weighted signals");
            Check("identical tiles -> 0", BorderRules.BoundaryStrength(Land(), Land(), w) == 0f);
            float biome = BorderRules.BoundaryStrength(Land(), WithBiome(Land(), 7), w);
            Check("biome change contributes", Close(biome, w.BiomeChange));
            float forest = BorderRules.BoundaryStrength(Forest(0), Forest(2), w);
            Check("thick-forest edge contributes per bucket", Close(forest, 2 * w.ForestStep));
            float swamp = BorderRules.BoundaryStrength(Land(), WithSwamp(Land()), w);
            Check("swamp edge contributes once", Close(swamp, w.SwampEdge));
            Check("boundary strength never negative", BorderRules.BoundaryStrength(Hill(3), Flat(), w) >= 0f);

            Section("hilliness is not a border by default (#20)");
            // A hill or mountain range is too narrow to bound; the default weights ignore it so a
            // region flows across high ground rather than stopping at the mountain-foot.
            Check("default HillStep off", BoundaryWeights.Default.HillStep == 0f);
            Check("default HighGround off", BoundaryWeights.Default.HighGround == 0f);
            Check("flat vs mountain is not a boundary under defaults", BorderRules.BoundaryStrength(Flat(), Hill(3), w) == 0f);
            // The hilliness mechanic itself still works when weighted (kept for reversibility): a step
            // into high ground beats a gentle rise.
            var wHill = BoundaryWeights.Default; wHill.HillStep = 0.5f; wHill.HighGround = 0.5f;
            float gentle = BorderRules.BoundaryStrength(Flat(), Hill(1), wHill);
            float ridge = BorderRules.BoundaryStrength(Flat(), Hill(2), wHill);
            Check("weighted: gentle rise = one hill step", Close(gentle, wHill.HillStep));
            Check("weighted: ridge into high ground is stronger", ridge > gentle);
            Check("weighted: ridge = 2 steps + high-ground bonus", Close(ridge, 2 * wHill.HillStep + wHill.HighGround));

            Section("rivers are not a boundary");
            // There is no river field on TileSignal by design: a river between two otherwise-identical
            // tiles yields zero boundary, because rivers seed basin centres, not borders (#20).
            Check("no river signal -> identical tiles stay 0", BorderRules.BoundaryStrength(Land(), Land(), w) == 0f);

            Section("optional climate gradients");
            var wTemp = BoundaryWeights.Default; wTemp.TemperaturePerDegree = 0.1f;
            var cold = Land(); var hot = Land(); hot.Temperature = 20f;
            Check("temperature off by default -> 0", BorderRules.BoundaryStrength(cold, hot, w) == 0f);
            Check("temperature on -> gradient contributes", Close(BorderRules.BoundaryStrength(cold, hot, wTemp), 2f));

            Section("wall thresholding");
            Check("at threshold is a wall", BorderRules.IsWall(1f, 1f));
            Check("below threshold is not", !BorderRules.IsWall(0.9f, 1f));
            Check("infinite is always a wall", BorderRules.IsWall(float.PositiveInfinity, 1f));

            Section("anchor count");
            Check("round to nearest", BorderRules.AnchorCount(1000f, 300f) == 3);   // 3.33 -> 3
            Check("rounds up at .5+", BorderRules.AnchorCount(1050f, 300f) == 4);    // 3.5 -> 4
            Check("never below 1", BorderRules.AnchorCount(10f, 300f) == 1);
            Check("degenerate cap -> 1", BorderRules.AnchorCount(1000f, 0f) == 1);

            Section("separation radius");
            Check("sqrt(area/3)", Close(BorderRules.SeparationRadius(300f), (float)Math.Sqrt(100.0)));
            Check("floored", BorderRules.SeparationRadius(0f) == BorderRules.MinSeparationFloor);

            Section("undersized merge predicate");
            Check("small non-island merges", BorderRules.ShouldMerge(3, 20, false));
            Check("big enough stays", !BorderRules.ShouldMerge(40, 20, false));
            Check("island exempt", !BorderRules.ShouldMerge(3, 20, true));

            Section("weakest-border merge target");
            Check("folds across the weakest border",
                BorderRules.WeakestBorderNeighbor(new[] { 3f, 0.5f, 2f }, new[] { 10, 11, 12 }) == 11);
            Check("tie -> smaller id wins",
                BorderRules.WeakestBorderNeighbor(new[] { 1f, 1f }, new[] { 20, 7 }) == 7);
            Check("no candidates -> -1",
                BorderRules.WeakestBorderNeighbor(new float[0], new int[0]) == -1);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL BORDER TESTS PASSED" : failures + " BORDER TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // --- signal builders: a baseline "land" tile and focused variations of it ---
        private static TileSignal Land() => new TileSignal { BiomeId = 1, HillClass = 0, ForestBucket = 1, Swamp = false, Water = false, Impassable = false, Temperature = 0f, Rainfall = 0f };
        private static TileSignal Water() { var t = Land(); t.Water = true; return t; }
        private static TileSignal Impassable() { var t = Land(); t.Impassable = true; t.HillClass = 4; return t; }
        private static TileSignal Flat() { var t = Land(); t.HillClass = 0; return t; }
        private static TileSignal Hill(int cls) { var t = Land(); t.HillClass = cls; return t; }
        private static TileSignal Forest(int bucket) { var t = Land(); t.ForestBucket = bucket; return t; }
        private static TileSignal WithBiome(TileSignal t, int id) { t.BiomeId = id; return t; }
        private static TileSignal WithSwamp(TileSignal t) { t.Swamp = true; return t; }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0005f;

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
