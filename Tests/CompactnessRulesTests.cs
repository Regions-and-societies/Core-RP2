// Behaviour tests for the territory-shape core (#19): embeddedness, the desired-ratio effective
// score, and domain compactness. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Placement;

namespace CompactnessRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("embeddedness");
            Check("tendril tip (1 of 6) is low", Close(CompactnessRules.Embeddedness(1, 6), 1f / 6f));
            Check("pocket fill (4 of 6) is high", Close(CompactnessRules.Embeddedness(4, 6), 4f / 6f));
            Check("no owned neighbours -> 0", CompactnessRules.Embeddedness(0, 6) == 0f);
            Check("fully surrounded -> 1", CompactnessRules.Embeddedness(6, 6) == 1f);
            Check("owned beyond claimable clamps to 1", CompactnessRules.Embeddedness(9, 6) == 1f);
            // Geography is a free wall: an island with no claimable neighbours is fully embedded.
            Check("island (no claimable neighbours) -> 1", CompactnessRules.Embeddedness(0, 0) == 1f);

            Section("effective score bends suitability by shape");
            const float ratio = 0.4f;
            Check("weight 0 -> legacy score untouched", CompactnessRules.EffectiveScore(10f, 0f, ratio, 0f) == 10f);
            Check("at the desired ratio -> full score", Close(CompactnessRules.EffectiveScore(10f, 0.4f, ratio, 1f), 10f));
            Check("above the ratio -> still full score (no bonus)", Close(CompactnessRules.EffectiveScore(10f, 0.9f, ratio, 1f), 10f));
            float tendril = CompactnessRules.EffectiveScore(10f, 1f / 6f, ratio, 1f);
            Check($"tendril at full weight keeps ~emb/ratio of its score (got {tendril:0.00})", Close(tendril, 10f * (1f / 6f) / ratio));
            Check("zero embeddedness at full weight -> 0", CompactnessRules.EffectiveScore(10f, 0f, ratio, 1f) == 0f);
            float half = CompactnessRules.EffectiveScore(10f, 0f, ratio, 0.5f);
            Check($"half weight halves the penalty (got {half:0.00})", Close(half, 5f));
            Check("monotonic in embeddedness",
                CompactnessRules.EffectiveScore(10f, 0.1f, ratio, 1f) < CompactnessRules.EffectiveScore(10f, 0.2f, ratio, 1f)
                && CompactnessRules.EffectiveScore(10f, 0.2f, ratio, 1f) < CompactnessRules.EffectiveScore(10f, 0.4f, ratio, 1f));
            Check("weight above 1 clamps to 1",
                Close(CompactnessRules.EffectiveScore(10f, 0f, ratio, 5f), CompactnessRules.EffectiveScore(10f, 0f, ratio, 1f)));
            Check("degenerate ratio (0) passes suitability through", CompactnessRules.EffectiveScore(10f, 0f, 0f, 1f) == 10f);

            // The decision the metric exists for: a dramatically better tendril still beats a mediocre
            // pocket, but a merely-better one no longer does.
            float goodTendril = CompactnessRules.EffectiveScore(40f, 1f / 6f, ratio, 1f);
            float okPocket = CompactnessRules.EffectiveScore(10f, 0.5f, ratio, 1f);
            Check("dramatically better land still wins a tendril", goodTendril > okPocket);
            float slightlyBetterTendril = CompactnessRules.EffectiveScore(12f, 1f / 6f, ratio, 1f);
            Check("slightly better land no longer wins a tendril", slightlyBetterTendril < okPocket);

            Section("domain compactness");
            Check("closed blob -> 1", CompactnessRules.DomainCompactness(8, 0) == 1f);
            Check("pure spider (all frontier) -> 0", CompactnessRules.DomainCompactness(0, 8) == 0f);
            Check("half internal -> 0.5", Close(CompactnessRules.DomainCompactness(4, 4), 0.5f));
            Check("no claimable borders at all -> 1", CompactnessRules.DomainCompactness(0, 0) == 1f);
            Check("negative counts are floored", CompactnessRules.DomainCompactness(-3, -3) == 1f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL COMPACTNESS TESTS PASSED" : failures + " COMPACTNESS TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

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
