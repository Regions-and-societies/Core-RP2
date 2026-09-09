// Behaviour tests for small-island handling (#49): the join threshold and the chain resolution at the
// 30-tile archipelago cut. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Placement;

namespace IslandRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("ShouldJoin: small + land within reach");
            Check("4-tile island 2 hops off a coast -> join", IslandRules.ShouldJoin(4, 2));
            Check("9-tile island 8 hops off (at the reach) -> join", IslandRules.ShouldJoin(9, 8));
            Check("small island 5 hops off (within the wider reach) -> join", IslandRules.ShouldJoin(4, 5));
            Check("10-tile island is not 'small' -> no", !IslandRules.ShouldJoin(10, 1));
            Check("small island but 9 hops (past reach) -> no", !IslandRules.ShouldJoin(4, 9));
            Check("small island with no land in reach (-1) -> no", !IslandRules.ShouldJoin(4, -1));
            Check("empty region (0 tiles) -> no", !IslandRules.ShouldJoin(0, 1));

            Section("ResolveChain: 30-tile archipelago cut");
            Check("two 20-tile islands (chain 40) -> form region",
                IslandRules.ResolveChain(40, 1) == IslandChainResolution.FormChainRegion);
            Check("chain of exactly 30 -> form region",
                IslandRules.ResolveChain(30, 2) == IslandChainResolution.FormChainRegion);
            Check("big chain forms a region even with no mainland in reach",
                IslandRules.ResolveChain(50, -1) == IslandChainResolution.FormChainRegion);
            Check("lone 4-tile island, mainland 3 hops -> join mainland",
                IslandRules.ResolveChain(4, 3) == IslandChainResolution.JoinMainland);
            Check("small chain (28) near a coast -> join mainland",
                IslandRules.ResolveChain(28, 2) == IslandChainResolution.JoinMainland);
            Check("small chain, no mainland in reach -> keep separate",
                IslandRules.ResolveChain(12, -1) == IslandChainResolution.KeepSeparate);
            Check("small chain, mainland past reach -> keep separate",
                IslandRules.ResolveChain(12, 9) == IslandChainResolution.KeepSeparate);

            Section("constants");
            Check("small-island cap is 10", IslandRules.SmallIslandMaxTiles == 10);
            Check("absorb reach is 8 hops", IslandRules.IslandAbsorbHops == 8);
            Check("chain cut is 30", IslandRules.IslandChainMinTiles == 30);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL ISLAND TESTS PASSED" : failures + " ISLAND TEST(S) FAILED");
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
