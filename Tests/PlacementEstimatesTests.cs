// Behaviour tests for the shared region-count estimate (#54): expected count from land tiles and target
// size, the ± band, and the pre-gen land-tile fallback. Pure, no game.
using System;
using RegionsAndSocieties.Placement;

namespace PlacementEstimatesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("expected region count = landTiles / target × fill");
            // biomemix ground truth: 67149 land, target 150 -> ~448, fill 1.0.
            Check("biomemix ~448 at fill 1.0", Near(PlacementEstimates.ExpectedRegionCount(67149, 150), 448, 2));
            Check("scales ~1/target: half the target -> ~double", Near(PlacementEstimates.ExpectedRegionCount(67149, 75), 895, 3));
            Check("fill 0.5 halves the count", Near(PlacementEstimates.ExpectedRegionCount(67149, 150, 0.5f), 224, 2));
            Check("zero land -> 0", PlacementEstimates.ExpectedRegionCount(0, 150) == 0);
            Check("zero/negative target -> 0", PlacementEstimates.ExpectedRegionCount(1000, 0) == 0);
            Check("fill<=0 falls back to default", PlacementEstimates.ExpectedRegionCount(67149, 150, 0f) == PlacementEstimates.ExpectedRegionCount(67149, 150));

            Section("± band brackets the estimate");
            int mid = PlacementEstimates.ExpectedRegionCount(67149, 150);
            int lo = PlacementEstimates.ExpectedRegionCountLow(67149, 150);
            int hi = PlacementEstimates.ExpectedRegionCountHigh(67149, 150);
            Check("low < mid < high", lo < mid && mid < hi);
            Check("band is ~±15%", Near(lo, (int)(mid * 0.85), 3) && Near(hi, (int)(mid * 1.15), 3));

            Section("pre-gen land-tile fallback");
            Check("120000 tiles × 0.56 -> ~67200", Near(PlacementEstimates.EstimateLandTiles(120000, 0.56f), 67200, 2));
            Check("fraction<=0 uses the typical constant", PlacementEstimates.EstimateLandTiles(100000, 0f) == PlacementEstimates.EstimateLandTiles(100000, PlacementEstimates.TypicalLandFraction));
            Check("fraction clamped to 1", PlacementEstimates.EstimateLandTiles(1000, 2f) == 1000);
            Check("zero total -> 0", PlacementEstimates.EstimateLandTiles(0, 0.5f) == 0);

            Section("total tiles from coverage — measured anchors, interpolated (2026-09-08 campaign)");
            Check("5% anchor", PlacementEstimates.EstimateTotalTiles(0.05f) == 3787);
            Check("30% anchor", PlacementEstimates.EstimateTotalTiles(0.30f) == 119904);
            Check("100% anchor", PlacementEstimates.EstimateTotalTiles(1.00f) == 590492);
            Check("interpolates between 30% and 50%", Near(PlacementEstimates.EstimateTotalTiles(0.40f), (119904 + 295732) / 2, 2));
            Check("above 100% holds the top anchor", PlacementEstimates.EstimateTotalTiles(1.5f) == 590492);
            Check("below 5% scales down toward zero", PlacementEstimates.EstimateTotalTiles(0.025f) < 3787 && PlacementEstimates.EstimateTotalTiles(0.025f) > 0);
            Check("zero coverage -> 0", PlacementEstimates.EstimateTotalTiles(0f) == 0);
            Check("far above the old linear model at 30% (was 30000)", PlacementEstimates.EstimateTotalTiles(0.30f) > 30000 * 3);

            Section("RP2 planet-scale tiles — ×3.0 per subcount step (measured 2026-09-08)");
            Check("subcount 10 = the vanilla curve", PlacementEstimates.EstimateTotalTilesRP2(10, 0.30f) == PlacementEstimates.EstimateTotalTiles(0.30f));
            Check("subcount 8 = curve / 9 (3^-2) -> ~13323", Near(PlacementEstimates.EstimateTotalTilesRP2(8, 0.30f), 13323, 2));
            Check("subcount 11 = curve × 3 -> ~359712", Near(PlacementEstimates.EstimateTotalTilesRP2(11, 0.30f), 359712, 2));
            Check("subcount 5 = curve / 243 (3^-5) -> ~493", Near(PlacementEstimates.EstimateTotalTilesRP2(5, 0.30f), 493, 2));
            Check("bigger Planet Scale -> more tiles", PlacementEstimates.EstimateTotalTilesRP2(11, 0.30f) > PlacementEstimates.EstimateTotalTilesRP2(10, 0.30f));
            Check("subcount<=0 falls back to the plain curve", PlacementEstimates.EstimateTotalTilesRP2(0, 0.30f) == PlacementEstimates.EstimateTotalTiles(0.30f));

            Section("RP2 sea level -> land fraction (calibrated, monotone)");
            Check("Normal (2) = the cross-seed mean 0.50", Math.Abs(PlacementEstimates.LandFractionForSeaLevel(2) - 0.50f) < 0.001f);
            Check("Low (0) has the most land", PlacementEstimates.LandFractionForSeaLevel(0) > PlacementEstimates.LandFractionForSeaLevel(2));
            Check("High (4) has the least land", PlacementEstimates.LandFractionForSeaLevel(4) < PlacementEstimates.LandFractionForSeaLevel(2));
            Check("monotone decreasing Low..High",
                PlacementEstimates.LandFractionForSeaLevel(0) > PlacementEstimates.LandFractionForSeaLevel(1) &&
                PlacementEstimates.LandFractionForSeaLevel(1) > PlacementEstimates.LandFractionForSeaLevel(2) &&
                PlacementEstimates.LandFractionForSeaLevel(2) > PlacementEstimates.LandFractionForSeaLevel(3) &&
                PlacementEstimates.LandFractionForSeaLevel(3) > PlacementEstimates.LandFractionForSeaLevel(4));
            Check("out-of-range / -1 falls back to the typical fraction", PlacementEstimates.LandFractionForSeaLevel(-1) == PlacementEstimates.TypicalLandFraction && PlacementEstimates.LandFractionForSeaLevel(9) == PlacementEstimates.TypicalLandFraction);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PLACEMENT-ESTIMATE TESTS PASSED" : failures + " PLACEMENT-ESTIMATE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Near(int a, int b, int tol) => Math.Abs(a - b) <= tol;

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
