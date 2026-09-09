// Behaviour tests for the drop-tiny-regions terminal rule (#51): the 6-tile cap, the settlement
// never-orphan guard, and the drop-anyway rule for a holdingless speck. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Placement;

namespace TinyRegionRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("cap: only 1-6 tile regions are candidates");
            Check("0 tiles (empty) -> Keep", TinyRegionRules.Resolve(0, false, true, false) == TinyRegionAction.Keep);
            Check("7 tiles -> Keep (above the cap)", TinyRegionRules.Resolve(7, false, false, false) == TinyRegionAction.Keep);
            Check("a 200-tile region is never touched", TinyRegionRules.Resolve(200, false, false, false) == TinyRegionAction.Keep);

            Section("a land neighbour always wins: join it (fold)");
            Check("6-tile sliver against land -> Fold", TinyRegionRules.Resolve(6, false, true, false) == TinyRegionAction.Fold);
            Check("3-tile crag with a land neighbour -> Fold", TinyRegionRules.Resolve(3, false, true, false) == TinyRegionAction.Fold);
            Check("settlement sliver with a land neighbour -> Fold (goes along)", TinyRegionRules.Resolve(2, true, true, false) == TinyRegionAction.Fold);
            Check("land neighbour folds even with the keep option on", TinyRegionRules.Resolve(3, false, true, true) == TinyRegionAction.Fold);

            Section("isolated speck (no land neighbour)");
            Check("1-tile island, no land in reach, empty -> Drop", TinyRegionRules.Resolve(1, false, false, false) == TinyRegionAction.Drop);
            Check("isolated speck with a settlement -> Keep (never orphaned)", TinyRegionRules.Resolve(2, true, false, false) == TinyRegionAction.Keep);

            Section("keep-small-regions option: keep ONLY the isolated specks");
            Check("isolated holdingless tiny -> Keep when option on", TinyRegionRules.Resolve(3, false, false, true) == TinyRegionAction.Keep);
            Check("isolated settlement tiny -> Keep when option on", TinyRegionRules.Resolve(3, true, false, true) == TinyRegionAction.Keep);
            Check("option never drops an isolated tiny region", !TinyRegionRules.ShouldDrop(6, false, false, true));
            Check("above cap still ignored with option on", TinyRegionRules.Resolve(40, false, false, true) == TinyRegionAction.Keep);

            Section("ShouldDrop wrapper mirrors Resolve==Drop");
            Check("isolated holdingless tiny -> ShouldDrop true", TinyRegionRules.ShouldDrop(5, false, false, false));
            Check("tiny with a land neighbour -> ShouldDrop false (it folds)", !TinyRegionRules.ShouldDrop(5, false, true, false));
            Check("isolated settlement tiny -> ShouldDrop false", !TinyRegionRules.ShouldDrop(5, true, false, false));
            Check("above cap -> ShouldDrop false", !TinyRegionRules.ShouldDrop(9, false, false, false));
            Check("cap constant is 6", TinyRegionRules.TinyRegionMaxTiles == 6);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL TINY-REGION TESTS PASSED" : failures + " TINY-REGION TEST(S) FAILED");
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
