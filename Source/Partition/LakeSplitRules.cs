using System.Collections.Generic;
using System.Linq;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// Split an inland lake between the land regions on its shores (#48). A lake is not a border: the
    /// polities on its shores share it, so the region seam runs ACROSS the water, not around it. Each lake
    /// tile is given to the shore region whose coast is NEAREST — a multi-source flood from every shore, so
    /// the seams fall on the midline between shores and converge on the lake's centre. That reads as clean
    /// wedges meeting in the middle rather than the interlocking fingers a size-quota'd flood produces.
    /// Ties (a tile the same distance from two shores) break toward the dominant shore — the one bordering
    /// the most lake tiles — then the lowest id, so the result is deterministic and the bigger shore keeps
    /// the centre.
    ///
    /// <para>Pure by design like the rest of the Partition layer: a lake tile set, its internal adjacency,
    /// and the land regions each lake tile borders go in; a tile-to-region assignment comes out. No game
    /// state, so worldgen and any test share one rule. The BFS is grid-agnostic — a hex world and a test's
    /// hand-built adjacency run the identical code.</para>
    /// </summary>
    public static class LakeSplitRules
    {
        /// <summary>Inland water bodies of this many tiles or fewer are split among their shores; a bigger
        /// inland body stays an Ocean province — an inland sea is a genuine barrier. Tune against the
        /// quicktest world.</summary>
        public const int LakeMaxTiles = 450;

        /// <summary><c>shore_r</c>: how many lake tiles border region <c>r</c> (a lake tile touching two
        /// regions counts once for each). The dominant shore (most bordering tiles) wins the centre on a
        /// tie.</summary>
        public static Dictionary<int, int> ShoreCounts(IEnumerable<int> lakeTiles, Dictionary<int, List<int>> shoreLabels)
        {
            var counts = new Dictionary<int, int>();
            foreach (int t in lakeTiles)
            {
                if (!shoreLabels.TryGetValue(t, out var regions) || regions == null) continue;
                var seen = new HashSet<int>();
                foreach (int r in regions)
                    if (seen.Add(r)) { int c; counts.TryGetValue(r, out c); counts[r] = c + 1; }
            }
            return counts;
        }

        /// <summary>The dominant shore region: highest shore count, ties to the lowest id.</summary>
        public static int DominantRegion(Dictionary<int, int> shoreCounts)
        {
            int best = -1, bestC = -1;
            foreach (var kv in shoreCounts.OrderBy(k => k.Key))
                if (kv.Value > bestC) { bestC = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>
        /// Split the lake: every tile gets a shore region id, taken by the region whose shore reaches it
        /// first in an uncapped multi-source flood (nearest-shore). Simultaneous arrivals in the same ring
        /// break by shore dominance, then lowest id, so seams sit on the midline and meet at the centre.
        /// A tile the flood never reaches (disconnected water) goes to the dominant region. Returns empty
        /// when the lake has no land shore at all (the caller then leaves it as water).
        /// </summary>
        public static Dictionary<int, int> Split(List<int> lakeTiles,
            Dictionary<int, List<int>> lakeAdjacency, Dictionary<int, List<int>> shoreLabels)
        {
            var assign = new Dictionary<int, int>();
            var shore = ShoreCounts(lakeTiles, shoreLabels);
            if (shore.Count == 0) return assign;
            int dom = DominantRegion(shore);

            // Rank the shores: dominant (most bordering tiles) first, then lowest id. This is the tie-break
            // when two shores reach a tile in the same ring — the bigger shore keeps the disputed midline.
            var rank = new Dictionary<int, int>();
            var ordered = new List<int>(shore.Keys);
            ordered.Sort((a, b) => shore[a] != shore[b] ? shore[b].CompareTo(shore[a]) : a.CompareTo(b));
            for (int i = 0; i < ordered.Count; i++) rank[ordered[i]] = i;

            // Seed hop 0: each shore tile is claimed by the best-ranked region it borders.
            var current = new Dictionary<int, int>();
            foreach (int t in lakeTiles.OrderBy(x => x))
            {
                if (!shoreLabels.TryGetValue(t, out var regs) || regs == null) continue;
                int best = -1;
                foreach (int r in regs) if (best == -1 || Rank(rank, r) < Rank(rank, best)) best = r;
                if (best != -1) current[t] = best;
            }

            // Ring by ring: assign the whole current ring first (so same-ring neighbours are not re-claimed),
            // then expand to unassigned neighbours, each contested tile going to its best-ranked claimant.
            while (current.Count > 0)
            {
                foreach (var kv in current.OrderBy(k => k.Key))
                    if (!assign.ContainsKey(kv.Key)) assign[kv.Key] = kv.Value;

                var claims = new Dictionary<int, int>();
                foreach (var kv in current.OrderBy(k => k.Key))
                {
                    int t = kv.Key, r = kv.Value;
                    if (assign[t] != r) continue;
                    if (!lakeAdjacency.TryGetValue(t, out var nbs)) continue;
                    foreach (int nb in nbs)
                    {
                        if (assign.ContainsKey(nb)) continue;
                        if (!claims.TryGetValue(nb, out int cur) || Rank(rank, r) < Rank(rank, cur)) claims[nb] = r;
                    }
                }
                current = claims;
            }

            foreach (int t in lakeTiles) if (!assign.ContainsKey(t)) assign[t] = dom;
            return assign;
        }

        private static int Rank(Dictionary<int, int> rank, int r)
        {
            return rank.TryGetValue(r, out int i) ? i : int.MaxValue;
        }
    }
}
