// Behaviour tests for the deterministic socioeconomic-tiering core (0.2.0, #14): the wealth
// thresholds and the 0-100 index. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Demographics;

namespace SocioeconomicRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("wealth classifies into the right tier");
            Check("near-zero -> Subsistence", SocioeconomicRules.TierFor(50) == SesTier.Subsistence);
            Check("just under 200 -> Subsistence", SocioeconomicRules.TierFor(199) == SesTier.Subsistence);
            Check("200 -> Modest", SocioeconomicRules.TierFor(200) == SesTier.Modest);
            Check("500 -> Modest", SocioeconomicRules.TierFor(500) == SesTier.Modest);
            Check("600 -> Prosperous", SocioeconomicRules.TierFor(600) == SesTier.Prosperous);
            Check("1499 -> Prosperous", SocioeconomicRules.TierFor(1499) == SesTier.Prosperous);
            Check("1500 -> Affluent", SocioeconomicRules.TierFor(1500) == SesTier.Affluent);
            Check("huge -> Affluent", SocioeconomicRules.TierFor(9000) == SesTier.Affluent);
            Check("tiers are monotonic in wealth",
                (int)SocioeconomicRules.TierFor(100) <= (int)SocioeconomicRules.TierFor(400)
                && (int)SocioeconomicRules.TierFor(400) <= (int)SocioeconomicRules.TierFor(1000)
                && (int)SocioeconomicRules.TierFor(1000) <= (int)SocioeconomicRules.TierFor(3000));

            Section("index collapses a distribution");
            Check("all subsistence -> 0", SocioeconomicRules.Index(new[] { 1f, 0f, 0f, 0f }) == 0);
            Check("all affluent -> 100", SocioeconomicRules.Index(new[] { 0f, 0f, 0f, 1f }) == 100);
            Check("even split is mid-scale", SocioeconomicRules.Index(new[] { 0.25f, 0.25f, 0.25f, 0.25f }) == 50);
            int poor = SocioeconomicRules.Index(new[] { 0.6f, 0.3f, 0.1f, 0f });
            int rich = SocioeconomicRules.Index(new[] { 0f, 0.1f, 0.3f, 0.6f });
            Check($"a poor mix ({poor}) scores below a rich mix ({rich})", poor < rich);
            Check("index stays in 0..100", poor >= 0 && rich <= 100);
            Check("null -> 0", SocioeconomicRules.Index(null) == 0);
            Check("empty -> 0", SocioeconomicRules.Index(new[] { 0f, 0f, 0f, 0f }) == 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SOCIOECONOMIC TESTS PASSED" : failures + " SOCIOECONOMIC TEST(S) FAILED");
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
