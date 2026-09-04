using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Roads
{
    /// <summary>
    /// The road-linking search rules (#38): how far a settlement-to-settlement path search may wander
    /// before it gives up. Pure — the world is handed in as delegates, so the search runs on synthetic
    /// grids in the test suite without a game.
    ///
    /// <para>The bound is the whole point. Roads are only wanted between bases within
    /// <see cref="MaxLinkDistanceTiles"/> of each other, but two such bases can be separated by water
    /// or impassable mountains. An unbounded breadth-first search from one toward the other never
    /// reaches its target and instead floods the entire landmass — at 100% coverage that is tens of
    /// thousands of tiles per failed pair, times hundreds of pairs: the multi-minute worldgen grind of
    /// #38. Capping the search by hop depth turns a failed pair into a small local scan, and leaves every
    /// pair whose shortest road fits the budget with exactly the road it had before: the expansion order
    /// is unchanged, only the give-up point moves.</para>
    /// </summary>
    public static class RoadPathRules
    {
        /// <summary>Two bases further apart than this (approximate world tiles) are never linked.</summary>
        public const float MaxLinkDistanceTiles = 16f;

        /// <summary>A road may be at most this many times longer (in hops) than the straight-line distance.</summary>
        public const float DepthBudgetFactor = 3f;

        /// <summary>Floor on the hop budget, so adjacent bases still get a short detour around a lake.</summary>
        public const int MinDepthBudget = 8;

        /// <summary>The hop budget for a pair this far apart. Never below <see cref="MinDepthBudget"/>.</summary>
        public static int DepthBudget(float approxDistanceTiles)
        {
            int scaled = (int)Math.Ceiling(approxDistanceTiles * DepthBudgetFactor);
            return Math.Max(MinDepthBudget, scaled);
        }

        public struct SearchStats
        {
            /// <summary>Tiles the search touched (the size of its visited set).</summary>
            public int TilesVisited;
            public bool Found;
            /// <summary>The search stopped because its frontier hit the hop budget — the target may lie beyond it.</summary>
            public bool BudgetExhausted;
            /// <summary>Tiles in the returned path, including both ends; 0 when nothing was found.</summary>
            public int PathLength;
        }

        /// <summary>
        /// Breadth-first shortest path from <paramref name="start"/> to <paramref name="end"/>, expanding at
        /// most <paramref name="maxDepth"/> hops from the start. Returns the path (start first, end last),
        /// or null when the end is not reachable within the budget.
        /// </summary>
        /// <param name="passable">Whether a tile may carry a road (the start tile is not checked).</param>
        /// <param name="neighborsOf">Appends a tile's neighbours to the list, in a stable order.</param>
        public static List<int> FindPath(int start, int end, int maxDepth,
            Func<int, bool> passable, Action<int, List<int>> neighborsOf, out SearchStats stats)
        {
            stats = default;
            if (passable == null || neighborsOf == null) return null;

            if (start == end)
            {
                stats.Found = true;
                stats.TilesVisited = 1;
                stats.PathLength = 1;
                return new List<int> { start };
            }

            var queue = new Queue<int>();
            var parent = new Dictionary<int, int>();
            var depth = new Dictionary<int, int>();
            var neighbors = new List<int>();

            queue.Enqueue(start);
            depth[start] = 0;

            bool found = false;
            bool exhausted = false;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == end)
                {
                    found = true;
                    break;
                }

                int currentDepth = depth[current];
                if (currentDepth >= maxDepth)
                {
                    // This frontier tile may not be expanded: the budget is spent along this branch.
                    exhausted = true;
                    continue;
                }

                neighbors.Clear();
                neighborsOf(current, neighbors);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int n = neighbors[i];
                    if (depth.ContainsKey(n)) continue;
                    if (!passable(n)) continue;

                    depth[n] = currentDepth + 1;
                    parent[n] = current;
                    queue.Enqueue(n);
                }
            }

            stats.TilesVisited = depth.Count;
            stats.Found = found;
            stats.BudgetExhausted = !found && exhausted;
            if (!found) return null;

            var path = new List<int>();
            int cursor = end;
            while (cursor != start)
            {
                path.Add(cursor);
                cursor = parent[cursor];
            }
            path.Add(start);
            path.Reverse();
            stats.PathLength = path.Count;
            return path;
        }
    }
}
