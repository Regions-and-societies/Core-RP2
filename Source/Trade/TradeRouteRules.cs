using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Trade
{
    /// <summary>An undirected candidate trade link between two settlement nodes (indices into the node
    /// list) and the least-cost traversal cost of the path between them.</summary>
    public struct TradeEdge
    {
        public int A;
        public int B;
        public float Cost;

        public TradeEdge(int a, int b, float cost) { A = a; B = b; Cost = cost; }
    }

    /// <summary>
    /// The pure core of the ideal-trade-route network (new). Two responsibilities, both game-free and
    /// testable: the traversal <b>cost model</b> for moving goods over one tile — cheap along roads and
    /// rivers, dear over mountains and hostile ground, blocked across water and impassable terrain — and
    /// the construction of the <b>minimum-cost connected network</b> (a spanning tree, or a forest when
    /// the settlements split into unreachable groups) over the candidate links.
    ///
    /// <para>This version determines the network STRUCTURE from cost alone; the VALUE / volume of trade
    /// (which links actually carry the most goods) is deferred until the resource model is fuller, and
    /// will layer on top as a weighting without changing this core.</para>
    /// </summary>
    public static class TradeRouteRules
    {
        /// <summary>Cost to traverse an ordinary open-land tile.</summary>
        public const float BaseStep = 1f;
        /// <summary>Added when a tile is high ground (large hills / mountains) — trade climbs slowly.</summary>
        public const float HighGroundPenalty = 2f;
        /// <summary>Added when a tile lies in territory hostile to the trading parties — routes detour
        /// around it rather than through it.</summary>
        public const float HostilePenalty = 4f;
        /// <summary>Multiplier for a tile a river runs through — a cheap corridor (a trade spine).</summary>
        public const float RiverDiscount = 0.5f;
        /// <summary>Multiplier for a tile a road runs through — the cheapest overland travel.</summary>
        public const float RoadDiscount = 0.3f;

        /// <summary>
        /// Cost to move goods across one tile. Water and impassable terrain are blocked
        /// (<see cref="float.PositiveInfinity"/>) — overland trade never crosses them (naval trade is a
        /// later layer). Otherwise the base cost rises over high ground and hostile territory (added
        /// penalties) and falls along rivers and roads (multiplicative discounts, which compound, so a
        /// road along a river valley is the cheapest ground of all). Never below a small floor.
        /// </summary>
        public static float StepCost(bool road, bool river, bool water, bool highGround, bool impassable, bool hostile)
        {
            if (water || impassable) return float.PositiveInfinity;

            float c = BaseStep;
            if (highGround) c += HighGroundPenalty;
            if (hostile) c += HostilePenalty;
            if (river) c *= RiverDiscount;
            if (road) c *= RoadDiscount;
            return c < 0.01f ? 0.01f : c;
        }

        /// <summary>
        /// The minimum-cost connected trade network over <paramref name="nodeCount"/> settlements, from
        /// the candidate <paramref name="edges"/> (undirected). Kruskal's algorithm: take links cheapest
        /// first, keeping one only when it joins two as-yet-unconnected groups — so the result is a
        /// spanning tree (n−1 links) when everything is reachable, and a spanning forest (fewer links)
        /// when the settlements fall into groups no candidate link can bridge (separate landmasses).
        /// Deterministic: ties break by the smaller (A, B), so a world rebuilds the same network. Edges
        /// of infinite cost (blocked) are never taken.
        /// </summary>
        public static List<TradeEdge> MinimumSpanningNetwork(int nodeCount, IReadOnlyList<TradeEdge> edges)
        {
            var result = new List<TradeEdge>();
            if (nodeCount <= 1 || edges == null || edges.Count == 0) return result;

            var sorted = new List<TradeEdge>(edges);
            sorted.Sort((x, y) =>
            {
                int c = x.Cost.CompareTo(y.Cost);
                if (c != 0) return c;
                int a = Math.Min(x.A, x.B).CompareTo(Math.Min(y.A, y.B));
                if (a != 0) return a;
                return Math.Max(x.A, x.B).CompareTo(Math.Max(y.A, y.B));
            });

            var parent = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) parent[i] = i;

            foreach (var e in sorted)
            {
                if (float.IsInfinity(e.Cost)) continue;
                if (e.A < 0 || e.B < 0 || e.A >= nodeCount || e.B >= nodeCount || e.A == e.B) continue;
                int ra = Find(parent, e.A), rb = Find(parent, e.B);
                if (ra == rb) continue;      // would form a cycle
                parent[ra] = rb;
                result.Add(e);
                if (result.Count == nodeCount - 1) break;   // fully spanned
            }
            return result;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];   // path halving
                x = parent[x];
            }
            return x;
        }
    }
}
