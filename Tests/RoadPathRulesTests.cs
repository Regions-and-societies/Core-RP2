// Behaviour tests for the road-linking search core (#38): the hop budget, that a reachable pair gets
// the same road bounded as unbounded, and that a water-separated pair costs a small local scan instead
// of flooding the whole landmass. Pure, no game — runs on a synthetic square grid with 4-neighbours.
using System;
using System.Collections.Generic;
using System.Linq;
using RegionsAndSocieties.Roads;

namespace RoadPathRulesTests
{
    public static class Program
    {
        private static int failures;

        // A W x H grid, tile id = y * W + x, 4-neighbour adjacency, blocked tiles impassable.
        private const int W = 120, H = 120;
        private static readonly HashSet<int> blocked = new HashSet<int>();

        private static int Id(int x, int y) => y * W + x;
        private static bool Passable(int t) => !blocked.Contains(t);
        private static void Neighbors(int t, List<int> into)
        {
            int x = t % W, y = t / W;
            if (x > 0) into.Add(Id(x - 1, y));
            if (x < W - 1) into.Add(Id(x + 1, y));
            if (y > 0) into.Add(Id(x, y - 1));
            if (y < H - 1) into.Add(Id(x, y + 1));
        }

        public static int Main()
        {
            Section("hop budget");
            Check("16-tile pair gets a 48-hop budget (3x the straight line)", RoadPathRules.DepthBudget(16f) == 48);
            Check("adjacent pair still gets the floor budget", RoadPathRules.DepthBudget(1f) == RoadPathRules.MinDepthBudget);
            Check("zero distance gets the floor budget", RoadPathRules.DepthBudget(0f) == RoadPathRules.MinDepthBudget);
            Check("fractional distance rounds the budget up", RoadPathRules.DepthBudget(5.1f) == 16);

            Section("open ground");
            blocked.Clear();
            var path = RoadPathRules.FindPath(Id(10, 10), Id(22, 10), RoadPathRules.DepthBudget(12f), Passable, Neighbors, out var s);
            Check("straight road found", path != null && s.Found);
            Check("road is the shortest one (13 tiles for 12 hops)", path != null && path.Count == 13 && s.PathLength == 13);
            Check("road starts and ends at the bases", path != null && path[0] == Id(10, 10) && path[path.Count - 1] == Id(22, 10));
            Check("every step is one hop", path != null && Enumerable.Range(1, path.Count - 1).All(i => IsAdjacent(path[i - 1], path[i])));
            Check("visited set is local (well under the 14,400-tile grid)", s.TilesVisited < 600);
            Check("budget not exhausted on a found road", !s.BudgetExhausted);

            var same = RoadPathRules.FindPath(Id(5, 5), Id(5, 5), 8, Passable, Neighbors, out var s0);
            Check("start == end is a one-tile road", same != null && same.Count == 1 && s0.Found);

            Section("a lake in the way: same road bounded as unbounded");
            blocked.Clear();
            // Vertical wall x=16, y in [4,16] with the only gap at y=17 — the road has to detour south.
            for (int y = 4; y <= 16; y++) blocked.Add(Id(16, y));
            var unbounded = RoadPathRules.FindPath(Id(10, 10), Id(22, 10), int.MaxValue, Passable, Neighbors, out var su);
            var bounded = RoadPathRules.FindPath(Id(10, 10), Id(22, 10), RoadPathRules.DepthBudget(12f), Passable, Neighbors, out var sb);
            Check("detour found both ways", unbounded != null && bounded != null);
            Check("bounded road is tile-for-tile the road the unbounded search found", unbounded != null && bounded != null && unbounded.SequenceEqual(bounded));
            Check("detour is longer than the straight line but inside the budget", bounded != null && bounded.Count - 1 > 12 && bounded.Count - 1 <= 36);
            Check("bounded search visits no more tiles than the unbounded one", sb.TilesVisited <= su.TilesVisited);

            Section("water-separated pair: local scan, not a landmass flood");
            blocked.Clear();
            for (int y = 0; y < H; y++) blocked.Add(Id(16, y)); // a full-height channel: no crossing at all
            int openTiles = W * H - H;
            var flood = RoadPathRules.FindPath(Id(10, 10), Id(22, 10), int.MaxValue, Passable, Neighbors, out var sf);
            var capped = RoadPathRules.FindPath(Id(10, 10), Id(22, 10), RoadPathRules.DepthBudget(12f), Passable, Neighbors, out var sc);
            Check("no road across the channel either way", flood == null && capped == null);
            Check($"unbounded search floods the whole western landmass ({sf.TilesVisited} tiles)", sf.TilesVisited == 16 * H);
            Check($"bounded search stays local ({sc.TilesVisited} tiles, budget 36 hops)", sc.TilesVisited < sf.TilesVisited / 2 && sc.TilesVisited <= 2 * 36 * 36 + 2 * 36 + 1);
            Check("bounded search reports the budget as the reason it stopped", sc.BudgetExhausted && !sc.Found);
            Check("unbounded search reports true isolation, not a budget stop", !sf.BudgetExhausted && !sf.Found);
            Check("visited count never exceeds the open tiles", sf.TilesVisited <= openTiles);

            Section("a small island: isolated, not budget-limited");
            blocked.Clear();
            for (int x = 0; x < W; x++) { blocked.Add(Id(x, 3)); }
            for (int y = 0; y < 3; y++) { blocked.Add(Id(3, y)); }
            var island = RoadPathRules.FindPath(Id(0, 0), Id(22, 10), RoadPathRules.DepthBudget(24f), Passable, Neighbors, out var si);
            Check("no road off a 3x3 island", island == null);
            Check("the island is fully explored (9 tiles) and reported isolated", si.TilesVisited == 9 && !si.BudgetExhausted);

            Section("budget semantics");
            blocked.Clear();
            var exact = RoadPathRules.FindPath(Id(0, 0), Id(8, 0), 8, Passable, Neighbors, out var se);
            var short1 = RoadPathRules.FindPath(Id(0, 0), Id(9, 0), 8, Passable, Neighbors, out var ss);
            Check("a road exactly at the budget is found", exact != null && exact.Count == 9);
            Check("one hop past the budget is not", short1 == null && ss.BudgetExhausted);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL ROAD PATH TESTS PASSED" : failures + " ROAD PATH TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool IsAdjacent(int a, int b)
        {
            int ax = a % W, ay = a / W, bx = b % W, by = b / W;
            return Math.Abs(ax - bx) + Math.Abs(ay - by) == 1;
        }

        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
