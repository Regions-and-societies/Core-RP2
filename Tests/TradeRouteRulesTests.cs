// Behaviour tests for the trade-route core (new): the traversal cost model and the minimum-cost
// spanning network (Kruskal, forest when disconnected). Pure, so this runs without a game.
using System;
using System.Collections.Generic;
using RegionsAndSocieties.Trade;

namespace TradeRouteRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("step cost model");
            float open = TradeRouteRules.StepCost(false, false, false, false, false, false);
            Check("open land is the base cost", Close(open, TradeRouteRules.BaseStep));
            Check("water is blocked", float.IsInfinity(TradeRouteRules.StepCost(false, false, true, false, false, false)));
            Check("impassable is blocked", float.IsInfinity(TradeRouteRules.StepCost(false, false, false, false, true, false)));
            Check("high ground costs more than open land", TradeRouteRules.StepCost(false, false, false, true, false, false) > open);
            Check("hostile territory costs more than open land", TradeRouteRules.StepCost(false, false, false, false, false, true) > open);
            Check("a river is cheaper than open land", TradeRouteRules.StepCost(false, true, false, false, false, false) < open);
            Check("a road is cheaper than a river", TradeRouteRules.StepCost(true, false, false, false, false, false) < TradeRouteRules.StepCost(false, true, false, false, false, false));
            Check("a road along a river is the cheapest ground", TradeRouteRules.StepCost(true, true, false, false, false, false) < TradeRouteRules.StepCost(true, false, false, false, false, false));

            Section("minimum spanning network");
            // 4 nodes in a line 0-1-2-3 plus a costly shortcut 0-3.
            var edges = new List<TradeEdge>
            {
                new TradeEdge(0, 1, 1f),
                new TradeEdge(1, 2, 1f),
                new TradeEdge(2, 3, 1f),
                new TradeEdge(0, 3, 5f),   // redundant, expensive
                new TradeEdge(0, 2, 3f),   // redundant
            };
            var net = TradeRouteRules.MinimumSpanningNetwork(4, edges);
            Check("connects n nodes with n-1 links", net.Count == 3);
            Check("total cost is minimal (3, not via the shortcuts)", Close(TotalCost(net), 3f));
            Check("the expensive shortcut is not used", !Uses(net, 0, 3) && !Uses(net, 0, 2));
            Check("every node is reachable", Spans(net, 4));

            Section("cheapest links win, ties are deterministic");
            var e2 = new List<TradeEdge> { new TradeEdge(0, 2, 2f), new TradeEdge(0, 1, 1f), new TradeEdge(1, 2, 1f) };
            var n2 = TradeRouteRules.MinimumSpanningNetwork(3, e2);
            Check("picks the two cheap links, drops the dear one", Close(TotalCost(n2), 2f) && !Uses(n2, 0, 2));
            var a = TradeRouteRules.MinimumSpanningNetwork(3, e2);
            var b = TradeRouteRules.MinimumSpanningNetwork(3, e2);
            Check("same input -> same network (deterministic)", SameEdges(a, b));

            Section("disconnected settlements -> a spanning forest");
            // Two clusters {0,1} and {2,3}; no candidate link bridges them (or only a blocked one).
            var split = new List<TradeEdge>
            {
                new TradeEdge(0, 1, 1f),
                new TradeEdge(2, 3, 1f),
                new TradeEdge(1, 2, float.PositiveInfinity),   // across the sea: blocked
            };
            var forest = TradeRouteRules.MinimumSpanningNetwork(4, split);
            Check("blocked links are never taken", !Uses(forest, 1, 2));
            Check("each reachable cluster is still connected (forest, 2 links)", forest.Count == 2);

            Section("degenerate inputs");
            Check("one node -> no links", TradeRouteRules.MinimumSpanningNetwork(1, edges).Count == 0);
            Check("no edges -> no links", TradeRouteRules.MinimumSpanningNetwork(4, new List<TradeEdge>()).Count == 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL TRADE ROUTE TESTS PASSED" : failures + " TRADE ROUTE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static float TotalCost(List<TradeEdge> es) { float t = 0f; foreach (var e in es) t += e.Cost; return t; }
        private static bool Uses(List<TradeEdge> es, int a, int b) { foreach (var e in es) if ((e.A == a && e.B == b) || (e.A == b && e.B == a)) return true; return false; }
        private static bool Spans(List<TradeEdge> es, int n)
        {
            var parent = new int[n]; for (int i = 0; i < n; i++) parent[i] = i;
            foreach (var e in es) { int ra = F(parent, e.A), rb = F(parent, e.B); parent[ra] = rb; }
            int root = F(parent, 0);
            for (int i = 1; i < n; i++) if (F(parent, i) != root) return false;
            return true;
        }
        private static int F(int[] p, int x) { while (p[x] != x) x = p[x]; return x; }
        private static bool SameEdges(List<TradeEdge> a, List<TradeEdge> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].A != b[i].A || a[i].B != b[i].B) return false;
            return true;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0005f;
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
