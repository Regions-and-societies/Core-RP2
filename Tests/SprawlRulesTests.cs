// Behaviour tests for the sprawl-spread rule (0.3.0): a settlement's people are split between its own
// tile and the tiles its sprawl reaches, in proportion to the sprawl weights, total conserved. Pure.
using System;
using System.Collections.Generic;
using RegionsAndSocieties.Demographics;

namespace SprawlRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            var shares = new List<float>();

            Section("conservation and shape");
            // Six flat neighbours (0.5), twelve second-ring tiles (0.25), one road tile further out (0.75).
            var w = new List<float>();
            for (int i = 0; i < 6; i++) w.Add(0.5f);
            for (int i = 0; i < 12; i++) w.Add(0.25f);
            w.Add(0.75f);
            float centre = SprawlRules.Spread(200f, w, shares);
            float sum = centre; foreach (float s in shares) sum += s;
            Check($"a 200-person city totals 200 after the spread (got {sum:0.###})", Close(sum, 200f, 0.001f));
            Check("the centre keeps exactly the core share", Close(centre, 100f, 0.001f));
            Check("one share per reached tile", shares.Count == w.Count);
            Check("a road tile further out draws more than a flat neighbour (weight, not distance, decides)", shares[18] > shares[0]);
            Check("a second-ring tile draws half of a flat neighbour", Close(shares[6], shares[0] * 0.5f, 0.001f));
            Check($"suburbs are visible people, not dust (flat neighbour = {shares[0]:0.##})", shares[0] >= 5f);

            Section("blocked ground: what the sprawl cannot reach stays home");
            var few = new List<float> { 0.5f, 0.5f, 0f };
            float c2 = SprawlRules.Spread(200f, few, shares);
            Check("a zero-weight tile (water, impassable) gets nobody", shares[2] == 0f);
            Check("total still 200", Close(c2 + shares[0] + shares[1], 200f, 0.001f));
            Check("the centre still keeps its core share; the budget goes to the reachable two", Close(c2, 100f, 0.001f) && Close(shares[0], 50f, 0.001f));
            float c3 = SprawlRules.Spread(200f, new List<float> { 0f, 0f }, shares);
            Check("nothing reachable at all: everyone stays on the centre", c3 == 200f && shares[0] == 0f && shares[1] == 0f);
            float c4 = SprawlRules.Spread(30f, null, shares);
            Check("no sprawl list (an outpost): the whole count stays on the tile", c4 == 30f && shares.Count == 0);
            float c5 = SprawlRules.Spread(0f, new List<float> { 0.5f }, shares);
            Check("zero population places nothing anywhere", c5 == 0f && shares[0] == 0f);

            Section("cutoff");
            Check("the cutoff is a small fraction of the centre weight", SprawlRules.WeightCutoff > 0f && SprawlRules.WeightCutoff <= 0.05f);
            Check("seven flat steps fall under it, six do not", Math.Pow(0.5, 7) < SprawlRules.WeightCutoff && Math.Pow(0.5, 6) >= SprawlRules.WeightCutoff);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SPRAWL TESTS PASSED" : failures + " SPRAWL TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
