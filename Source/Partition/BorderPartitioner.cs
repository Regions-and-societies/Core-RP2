using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// The game-coupled half of the border-first partition (#20). Turns the world grid into land
    /// provinces whose borders sit on natural feature boundaries and whose centres are river basins.
    ///
    /// <para>Pipeline: classify every land tile into a <see cref="TileSignal"/>; build the boundary
    /// field across land-land edges with <see cref="BorderRules.BoundaryStrength"/>; flood land into
    /// <b>cells</b> separated by wall edges (border-first); then split any cell larger than the size
    /// guide into river basins by a marker-controlled watershed (rivers seed the markers), or, in a
    /// featureless cell with no river, into evenly-spaced anchors whose divide falls at the midpoint.
    /// A cell within the size band is kept whole, so region size varies with the terrain.</para>
    ///
    /// <para>Everything that reads <c>Find.WorldGrid</c> lives here; the pure decisions are delegated
    /// to <see cref="BorderRules"/>. Not unit-testable without a game — covered by the in-game audit.
    /// Deterministic: tiles are visited and markers chosen in id order, so a world regenerates
    /// identically.</para>
    /// </summary>
    public static class BorderPartitioner
    {
        // Tree-density cut points for the forest bucket (0 open, 1 wooded, 2 thick forest). BiomeDef
        // tree density runs ~0 (desert) to ~1 (dense jungle); these split it into three bands.
        private const float WoodedTreeDensity = 0.25f;
        private const float ThickTreeDensity = 0.6f;

        // Master switch for the pass-neck feature (#20). Off until a selective pass rule replaces the
        // over-firing opposite-sides primitive.
        private const bool EnableNeckDetection = false;

        /// <summary>
        /// Partition the unclaimed land of the world into province tile-groups. Water, ocean and lake
        /// tiles are expected to be already claimed in <paramref name="tileToProvinceId"/> (their
        /// entries are &gt;= 0) and are treated as hard walls; impassable land is left unclaimed here
        /// and folded in later by the caller's enclosed-gap pass. Returns one tile list per land
        /// province, ready for the caller to wrap in <c>GeographicProvince</c>s.
        /// </summary>
        public static List<List<int>> PartitionLand(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
        {
            var result = new List<List<int>>();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileToProvinceId == null) return result;

            int total = grid.TilesCount;
            int target = maxRegionTiles > 0 ? maxRegionTiles : 150;

            // Stable biome identity: first-encounter order over ascending tile ids is deterministic.
            var biomeIds = new Dictionary<BiomeDef, int>();
            var signals = new TileSignal[total];
            var isLand = new bool[total];
            for (int t = 0; t < total; t++)
            {
                signals[t] = Classify(grid, t, biomeIds);
                // A land tile eligible for a province: unclaimed, and neither water nor impassable
                // (those are the hard walls the fill never spans).
                isLand[t] = tileToProvinceId[t] < 0 && !signals[t].Water && !signals[t].Impassable;
            }

            // Pass-neck detection (gated OFF pending a selective saddle rule; kept for iteration).
            var isNeck = EnableNeckDetection ? MarkPassNecks(grid, isLand, signals, total) : new bool[total];
#pragma warning disable 0162 // unreachable while EnableNeckDetection is const false
            if (EnableNeckDetection)
            {
                int neckCount = 0; for (int i = 0; i < total; i++) if (isNeck[i]) neckCount++;
                Log.Message($"[RegionsAndSocieties] Border-first: {neckCount} pass-neck tiles walled off.");
            }
#pragma warning restore 0162

            var fillable = new bool[total];
            for (int t = 0; t < total; t++) fillable[t] = isLand[t] && !isNeck[t];

            // Anchors: one region centre per ~target tiles, per connected land component, spaced by
            // farthest-point so cells come out evenly sized. Nearest-anchor cells give the clean,
            // convex ~6-sided borders; biomes/forests only nudge a border (below), never draw it.
            var anchors = SelectAnchors(grid, fillable, total, target);
            if (anchors.Count == 0) return result;

            // Box fill: each tile joins the anchor with the smallest CHEBYSHEV distance — max(|x|,|y|)
            // in that anchor's local east/north frame — instead of Euclidean distance. Euclidean
            // nearest-anchor gives hexagonal blobs; the Chebyshev (L-infinity) metric grows a region as
            // a square/rectangle (it advances its short axis to stay balanced), so regions come out as
            // boxes of varying size. The flood only crosses non-wall tiles, so the boxes still clip to
            // hard boundaries (coast / impassable) — squares that snap to the terrain. The cost stored
            // for a tile is its Chebyshev distance to its owning anchor (not a path sum), so the nearest
            // box wins; ties break to the smaller anchor id for determinism.
            var frameC = new Dictionary<int, UnityEngine.Vector3>(anchors.Count);
            var frameE = new Dictionary<int, UnityEngine.Vector3>(anchors.Count);
            var frameN = new Dictionary<int, UnityEngine.Vector3>(anchors.Count);
            foreach (int a in anchors)
            {
                UnityEngine.Vector3 c = grid.GetTileCenter(a);
                UnityEngine.Vector3 up = c.normalized;
                UnityEngine.Vector3 refA = UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(up, UnityEngine.Vector3.up)) > 0.99f
                    ? UnityEngine.Vector3.right : UnityEngine.Vector3.up;
                UnityEngine.Vector3 east = UnityEngine.Vector3.Cross(up, refA).normalized;
                frameC[a] = c; frameE[a] = east; frameN[a] = UnityEngine.Vector3.Cross(east, up).normalized;
            }
            float tileSpacing = TileSpacing(grid);

            var owner = new int[total];
            var cost = new float[total];
            for (int i = 0; i < total; i++) { owner[i] = -1; cost[i] = float.PositiveInfinity; }
            var heap = new MinHeap(anchors.Count + 16);
            foreach (int a in anchors) { owner[a] = a; cost[a] = 0f; heap.Push(a, 0f); }
            var nb = new List<PlanetTile>();
            while (heap.Count > 0)
            {
                heap.Pop(out int cur, out float cc);
                if (cc > cost[cur]) continue;   // stale heap entry
                int a2 = owner[cur];
                UnityEngine.Vector3 ac = frameC[a2], ae = frameE[a2], an = frameN[a2];
                nb.Clear();
                grid.GetTileNeighbors(cur, nb);
                foreach (var n in nb)
                {
                    int nid = n.tileId;
                    if (!fillable[nid]) continue;
                    // Chebyshev distance of this neighbour to the anchor a2, in a2's box frame.
                    UnityEngine.Vector3 d = grid.GetTileCenter(nid) - ac;
                    float x = UnityEngine.Vector3.Dot(d, ae) / tileSpacing;
                    float y = UnityEngine.Vector3.Dot(d, an) / tileSpacing;
                    float nc = System.Math.Max(System.Math.Abs(x), System.Math.Abs(y));
                    if (nc < cost[nid] || (nc == cost[nid] && a2 < owner[nid]))
                    {
                        cost[nid] = nc;
                        owner[nid] = a2;
                        heap.Push(nid, nc);
                    }
                }
            }

            // Group tiles by owning anchor.
            var groupByAnchor = new Dictionary<int, List<int>>();
            for (int t = 0; t < total; t++)
            {
                if (!fillable[t] || owner[t] < 0) continue;
                if (!groupByAnchor.TryGetValue(owner[t], out var g)) { g = new List<int>(); groupByAnchor[owner[t]] = g; }
                g.Add(t);
            }
            foreach (var kv in groupByAnchor) result.Add(kv.Value);

            AssignNeckTiles(grid, isLand, isNeck, result, total);
            return result;
        }

        /// <summary>
        /// The contain-then-subdivide partition (new for 0.3.0). Draw regions INSIDE the terrain's natural
        /// sections, then cut each into evenly sized squares:
        ///
        /// <list type="number">
        /// <item>Classify each unclaimed land tile as <b>interior</b> (flat / small hills) or <b>wall</b>
        /// (Mountainous / LargeHills / impassable). Water is already claimed and is a hard wall.</item>
        /// <item><b>Contain:</b> flood the interior into containers bounded by biome edges AND natural
        /// barriers — adjacent interior tiles join only within one biome, and walls never bridge two sides,
        /// so a container is one biome-coherent patch on one side of the barriers around it. The thin edge
        /// pieces a global grid would strand are simply part of the container's body.</item>
        /// <item><b>Subdivide:</b> cut each container into ~<c>baseMax × biomeWeight</c>-tile SQUARE cells
        /// (temperate ~1×, tundra ~2×, desert ~3×, ice ~10×), the Chebyshev fill confined to the container
        /// so no square leaks past a barrier. A container within its target stays one region.</item>
        /// <item><b>Drape the walls</b> onto the nearest region (multi-source BFS), so a range's crest is
        /// the seam; any isolated massif becomes its own region.</item>
        /// </list>
        ///
        /// <para>Deterministic (id-ordered seeds, ties to smaller id). The container flood is O(tiles); the
        /// subdivision is scoped per container (no whole-grid pass each). Downstream MergeTinyDomains still
        /// cleans up sub-minimum shards, biome- and barrier-aware.</para>
        /// </summary>
        public static List<List<int>> PartitionContainSubdivide(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
        {
            var result = new List<List<int>>();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileToProvinceId == null) return result;

            int total = grid.TilesCount;
            int baseMax = maxRegionTiles > 0 ? maxRegionTiles : 150;

            // Phase 1: classify the remaining unclaimed land as claimable interior. Water AND impassable
            // mountains are already claimed (Ocean / MountainRange provinces) before this runs, so their
            // tiles are hard walls here — a container can neither include them nor cross them. EVERY other
            // passable tile is interior: flat, small hills, large hills and passable mountains all count, so
            // only impassable terrain, water and biome edges bound a container. Hills therefore no longer
            // shatter the land into slivers, and impassable rock is never drawn into a territory.
            var interior = new bool[total];
            var biomeOf = new BiomeDef[total];
            for (int t = 0; t < total; t++)
            {
                if (tileToProvinceId[t] >= 0) continue;         // water / impassable already claimed = hard wall
                Tile tile = grid[t];
                if (tile == null || tile.WaterCovered) continue;
                if (tile.hilliness == Hilliness.Impassable) continue;   // safety: any unclaimed impassable stays a wall
                BiomeDef biome = tile.PrimaryBiome;
                if (biome == null || biome.impassable || biome.defName == "SeaIce") continue;
                biomeOf[t] = biome;
                interior[t] = true;
            }

            // Phase 2 (CONTAIN): flood the interior into containers bounded by biome edges AND natural
            // barriers, with mountain passes treated as boundaries. Done in three morphological steps:
            var neigh = new List<PlanetTile>();

            // 2a: a CORE tile is an interior tile with NO wall (claimed water / impassable) neighbour AND no
            // different-biome neighbour. Any narrow neck — a mountain pass, an isthmus between seas, OR a
            // one-to-two-tile waist where another biome pinches in from both sides — is all rim and has no
            // core, so the core flood in 2b cannot bridge the two lobes through it. The neck therefore acts
            // as a boundary and the rim splits at the pinch (2c), which closes 1-tile "tumor" necks and lays
            // a straight cut across the narrowest part of a biome pass instead of tracing every tooth of it.
            var core = new bool[total];
            for (int t = 0; t < total; t++)
            {
                if (!interior[t]) continue;
                bool isCore = true;
                BiomeDef tb = biomeOf[t];
                grid.GetTileNeighbors(t, neigh);
                for (int i = 0; i < neigh.Count; i++)
                {
                    int n = neigh[i].tileId;
                    if (n < 0 || n >= total || tileToProvinceId[n] >= 0) { isCore = false; break; }   // touches a wall
                    if (interior[n] && biomeOf[n] != tb) { isCore = false; break; }                    // touches a biome edge
                }
                core[t] = isCore;
            }

            // 2b: flood the CORE into biome-coherent containers (same biome, core-to-core links only). Two
            // sides of a range that meet only through a narrow pass stay separate, because the pass has no
            // core to link them.
            var containerOf = new int[total];
            for (int i = 0; i < total; i++) containerOf[i] = -1;
            var containers = new List<List<int>>();
            var stack = new Stack<int>();
            for (int s = 0; s < total; s++)
            {
                if (!core[s] || containerOf[s] != -1) continue;
                int id = containers.Count;
                BiomeDef biome = biomeOf[s];
                var container = new List<int>();
                stack.Clear(); stack.Push(s); containerOf[s] = id;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    container.Add(cur);
                    grid.GetTileNeighbors(cur, neigh);
                    for (int i = 0; i < neigh.Count; i++)
                    {
                        int n = neigh[i].tileId;
                        if (n < 0 || n >= total || !core[n] || containerOf[n] != -1) continue;
                        if (biomeOf[n] != biome) continue;   // biome edge = container wall
                        containerOf[n] = id; stack.Push(n);
                    }
                }
                containers.Add(container);
            }

            // 2c: hand each RIM interior tile to the nearest core container of its own biome (multi-source
            // BFS from the core). A pass corridor, being all rim, is reached from the cores on BOTH sides
            // and splits between them at the midpoint — so the range reads as one continuous border.
            var rq = new Queue<int>();
            for (int t = 0; t < total; t++) if (containerOf[t] != -1) rq.Enqueue(t);
            while (rq.Count > 0)
            {
                int cur = rq.Dequeue();
                int cid = containerOf[cur];
                BiomeDef cb = biomeOf[cur];
                grid.GetTileNeighbors(cur, neigh);
                for (int i = 0; i < neigh.Count; i++)
                {
                    int n = neigh[i].tileId;
                    if (n < 0 || n >= total || !interior[n] || containerOf[n] != -1) continue;
                    if (biomeOf[n] != cb) continue;       // stay within one biome
                    containerOf[n] = cid; containers[cid].Add(n); rq.Enqueue(n);
                }
            }

            // Phase 3 (SUBDIVIDE): cut each container into appropriately sized squares. Target size is
            // baseMax × the biome's size weight (temperate ~1×, tundra ~2×, desert ~3×, ice ~10×), so a
            // sparse biome makes fewer, larger squares. A container at or under its target stays one region
            // (a small biome patch is kept whole); a larger one splits into ~ceil(size/target) square
            // Chebyshev cells, the fill confined to the container so no square leaks past a barrier.
            var regionOf = new int[total];
            for (int i = 0; i < total; i++) regionOf[i] = -1;
            var owner = new int[total];
            var cost = new float[total];
            var set = new HashSet<int>();
            foreach (var container in containers)
            {
                BiomeDef biome = biomeOf[container[0]];
                float w = BiomeRegionWeights.Weight(biome);
                int target = System.Math.Max(1, (int)System.Math.Round(baseMax * w));
                if (container.Count <= target) { AddRegion(result, regionOf, container); continue; }
                var cells = BalancedCellsScoped(grid, container, target, owner, cost, set, neigh);
                if (cells.Count == 0) { AddRegion(result, regionOf, container); continue; }
                foreach (var g in cells) AddRegion(result, regionOf, g);
            }

            // Phase 4: any interior tile the subdivision somehow left unclaimed becomes its own region, so
            // every claimable land tile lands in exactly one region. (Impassable rock and water are not
            // interior; they stay with their own MountainRange / Ocean provinces and are never draped in.)
            for (int s = 0; s < total; s++)
            {
                if (!interior[s] || regionOf[s] != -1) continue;
                int id = result.Count;
                var group = new List<int>();
                stack.Clear(); stack.Push(s); regionOf[s] = id;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    group.Add(cur);
                    grid.GetTileNeighbors(cur, neigh);
                    for (int i = 0; i < neigh.Count; i++)
                    {
                        int n = neigh[i].tileId;
                        if (n < 0 || n >= total || !interior[n] || regionOf[n] != -1) continue;
                        regionOf[n] = id; stack.Push(n);
                    }
                }
                result.Add(group);
            }

            return result;
        }

        /// <summary>Register a tile group as the next region and stamp each tile's <paramref name="regionOf"/>.</summary>
        private static void AddRegion(List<List<int>> result, int[] regionOf, List<int> tiles)
        {
            if (tiles == null || tiles.Count == 0) return;
            int rid = result.Count;
            result.Add(tiles);
            for (int i = 0; i < tiles.Count; i++) regionOf[tiles[i]] = rid;
        }

        /// <summary>
        /// Split one connected container into k = round(count/target) cells of roughly EQUAL size. Anchors
        /// are farthest-point-spaced (running-min, O(count·k)); the fill is a capacity-constrained
        /// multi-source BFS — all anchors grow in lockstep (FIFO level order) and an anchor stops claiming
        /// once it reaches the per-cell capacity ceil(count/k). This is what keeps a 665-tile area splitting
        /// ~333/332 instead of the Voronoi fill's 503/162: no cell can balloon while another starves. Cells
        /// stay contiguous (each grown from its anchor) and confined to the container (<paramref name="set"/>).
        ///
        /// <para>Scratch is scoped to the container: <paramref name="owner"/> holds the anchor index and
        /// <paramref name="cost"/> the anchor-selection running distance; both grid-sized and reset only over
        /// the container's tiles. Deterministic (tiles sorted; anchors id-ordered; FIFO is order-stable).</para>
        /// </summary>
        private static List<List<int>> BalancedCellsScoped(WorldGrid grid, List<int> tiles, int target,
            int[] owner, float[] cost, HashSet<int> set, List<PlanetTile> nb)
        {
            var result = new List<List<int>>();
            int count = tiles.Count;
            if (count == 0) return result;
            // ceil, not round: a container OVER the target must split, so a 448-tile area at target 300
            // becomes 2×224 rather than rounding to 1 and staying a lone 448. Each cell then lands at or
            // just under the target.
            int k = System.Math.Max(1, (int)System.Math.Ceiling(count / (double)target));

            var sorted = new List<int>(tiles);
            sorted.Sort();
            if (k <= 1) { result.Add(sorted); return result; }

            set.Clear();
            foreach (int t in sorted) { set.Add(t); cost[t] = float.PositiveInfinity; owner[t] = -1; }

            // Farthest-point anchors, running min-distance (O(count·k)) — spreads the k cell centres out.
            var anchors = new List<int>(k) { sorted[0] };
            int newest = sorted[0];
            while (anchors.Count < k)
            {
                int best = -1; float bestD = -1f;
                for (int i = 0; i < count; i++)
                {
                    int t = sorted[i];
                    float d = cost[t];
                    float dd = grid.ApproxDistanceInTiles(t, newest);
                    if (dd < d) { d = dd; cost[t] = d; }
                    if (d > bestD) { bestD = d; best = t; }
                }
                if (best < 0 || cost[best] <= 0f) break;
                anchors.Add(best); newest = best;
            }
            anchors.Sort();

            // Capacity-constrained multi-source BFS. Encode (anchorIndex, tile) as one long in the FIFO so a
            // tile carries which anchor's frontier reached it; the first non-full anchor to reach a tile
            // claims it. Level-order growth keeps the cells balanced; the cap stops any one ballooning.
            int capacity = (count + k - 1) / k;
            var counts = new int[anchors.Count];
            var q = new Queue<long>();
            for (int a = 0; a < anchors.Count; a++) q.Enqueue(((long)a << 32) | (uint)anchors[a]);
            while (q.Count > 0)
            {
                long e = q.Dequeue();
                int ai = (int)(e >> 32);
                int t = (int)(e & 0xFFFFFFFF);
                if (owner[t] != -1 || counts[ai] >= capacity) continue;
                owner[t] = ai; counts[ai]++;
                nb.Clear();
                grid.GetTileNeighbors(t, nb);
                for (int i = 0; i < nb.Count; i++)
                {
                    int nid = nb[i].tileId;
                    if (set.Contains(nid) && owner[nid] == -1) q.Enqueue(((long)ai << 32) | (uint)nid);
                }
            }

            // Any tile only ever reached by full anchors is still unclaimed — hand it to an adjacent claimed
            // cell (slight overflow past capacity, but every tile ends in exactly one contiguous cell).
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = 0; i < count; i++)
                {
                    int t = sorted[i];
                    if (owner[t] != -1) continue;
                    nb.Clear();
                    grid.GetTileNeighbors(t, nb);
                    for (int j = 0; j < nb.Count; j++)
                    {
                        int nid = nb[j].tileId;
                        if (set.Contains(nid) && owner[nid] != -1) { owner[t] = owner[nid]; progress = true; break; }
                    }
                }
            }

            var groups = new List<List<int>>(anchors.Count);
            for (int a = 0; a < anchors.Count; a++) groups.Add(new List<int>());
            for (int i = 0; i < count; i++)
            {
                int t = sorted[i];
                int o = owner[t] >= 0 ? owner[t] : 0;
                groups[o].Add(t);
            }
            foreach (var g in groups) if (g.Count > 0) result.Add(g);
            return result;
        }

        // Small surcharges (distance-dominant) that let a border SNAP onto a nearby biome / forest edge
        // without chasing it — the hybrid of clean convex cells and terrain-faithful borders (#20).
        private const float BiomeSnapWeight = 0.35f;
        private const float ForestSnapWeight = 0.15f;

        /// <summary>World-units per one-tile step (tile 0 to its first neighbour) — converts 3D chord
        /// offsets into tile units for the box metric.</summary>
        private static float TileSpacing(WorldGrid grid)
        {
            var nb = new List<PlanetTile>();
            grid.GetTileNeighbors(0, nb);
            if (nb.Count == 0) return 1f;
            return System.Math.Max(0.0001f, (grid.GetTileCenter(0) - grid.GetTileCenter(nb[0].tileId)).magnitude);
        }

        /// <summary>
        /// One anchor per ~<paramref name="target"/> tiles, chosen per connected land component by
        /// farthest-point sampling so region centres spread evenly and every component (island included)
        /// gets at least one. Components are the maximal runs of fillable land — bounded only by hard
        /// walls, since biomes no longer cut them. Returned in ascending id order for determinism.
        /// </summary>
        private static List<int> SelectAnchors(WorldGrid grid, bool[] fillable, int total, int target)
        {
            var anchors = new List<int>();
            var seen = new bool[total];
            var nb = new List<PlanetTile>();
            for (int s = 0; s < total; s++)
            {
                if (!fillable[s] || seen[s]) continue;
                var comp = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s); seen[s] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    comp.Add(c);
                    nb.Clear();
                    grid.GetTileNeighbors(c, nb);
                    foreach (var n in nb)
                    {
                        int nid = n.tileId;
                        if (fillable[nid] && !seen[nid]) { seen[nid] = true; q.Enqueue(nid); }
                    }
                }
                int k = System.Math.Max(1, (int)System.Math.Round((double)comp.Count / target));
                anchors.AddRange(FarthestPointAnchors(grid, comp, k));
            }
            anchors.Sort();
            return anchors;
        }

        /// <summary>Pick <paramref name="k"/> farthest-point anchors from a tile set: the first is the
        /// smallest id (determinism), each subsequent is the tile maximising the minimum distance to
        /// those already chosen. Uses a running per-tile min-distance array so it is O(area * k) — the
        /// naive re-scan is O(area * k^2) and hangs a big landmass with hundreds of anchors.</summary>
        private static List<int> FarthestPointAnchors(WorldGrid grid, List<int> tiles, int k)
        {
            var sorted = new List<int>(tiles); sorted.Sort();
            int n = sorted.Count;
            var anchors = new List<int> { sorted[0] };
            var minD = new float[n];
            for (int i = 0; i < n; i++) minD[i] = grid.ApproxDistanceInTiles(sorted[i], sorted[0]);
            while (anchors.Count < k)
            {
                int bi = -1; float bd = -1f;
                for (int i = 0; i < n; i++) { if (minD[i] > bd) { bd = minD[i]; bi = i; } }
                if (bi < 0 || bd <= 0f) break;   // no tile left with positive distance to all anchors
                int a = sorted[bi];
                anchors.Add(a);
                minD[bi] = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (minD[i] <= 0f) continue;
                    float d = grid.ApproxDistanceInTiles(sorted[i], a);
                    if (d < minD[i]) minD[i] = d;
                }
            }
            return anchors;
        }

        /// <summary>Binary min-heap keyed by float priority — Dijkstra's frontier. .NET Framework has no
        /// built-in PriorityQueue, and the list-scan queue used for the small per-cell fills is O(n) per
        /// pop, far too slow for a whole-world Dijkstra.</summary>
        private sealed class MinHeap
        {
            private int[] items;
            private float[] prio;
            private int count;
            public MinHeap(int cap) { cap = System.Math.Max(cap, 16); items = new int[cap]; prio = new float[cap]; }
            public int Count { get { return count; } }
            public void Push(int item, float p)
            {
                if (count == items.Length) { System.Array.Resize(ref items, count * 2); System.Array.Resize(ref prio, count * 2); }
                int i = count++;
                items[i] = item; prio[i] = p;
                while (i > 0) { int par = (i - 1) / 2; if (prio[par] <= prio[i]) break; Swap(i, par); i = par; }
            }
            public void Pop(out int item, out float p)
            {
                item = items[0]; p = prio[0];
                count--;
                items[0] = items[count]; prio[0] = prio[count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, m = i;
                    if (l < count && prio[l] < prio[m]) m = l;
                    if (r < count && prio[r] < prio[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
            private void Swap(int a, int b)
            {
                int ti = items[a]; items[a] = items[b]; items[b] = ti;
                float tp = prio[a]; prio[a] = prio[b]; prio[b] = tp;
            }
        }

        /// <summary>
        /// Hand the excluded pass-neck tiles back to the flanking basins (#20): each neck tile joins the
        /// group most of its already-assigned neighbours belong to, so the pass splits down the middle
        /// between the two provinces it connects and no tile is left unassigned. Iterated so a
        /// multi-tile neck resolves inward from both ends; ties break to the lower group index for
        /// determinism.
        /// </summary>
        private static void AssignNeckTiles(WorldGrid grid, bool[] isLand, bool[] isNeck, List<List<int>> groups, int total)
        {
            var groupOf = new Dictionary<int, int>();
            for (int g = 0; g < groups.Count; g++)
                foreach (int t in groups[g]) groupOf[t] = g;

            var pending = new List<int>();
            for (int t = 0; t < total; t++) if (isLand[t] && isNeck[t] && !groupOf.ContainsKey(t)) pending.Add(t);

            var neighbors = new List<PlanetTile>();
            var counts = new Dictionary<int, int>();
            int safety = 0;
            while (pending.Count > 0 && safety++ < 16)
            {
                var next = new List<int>();
                bool any = false;
                foreach (int t in pending)
                {
                    neighbors.Clear();
                    grid.GetTileNeighbors(t, neighbors);
                    counts.Clear();
                    int best = -1, bestCount = 0;
                    foreach (var n in neighbors)
                    {
                        if (!groupOf.TryGetValue(n.tileId, out int g)) continue;
                        int c; counts.TryGetValue(g, out c); c++; counts[g] = c;
                        if (c > bestCount || (c == bestCount && g < best)) { bestCount = c; best = g; }
                    }
                    if (best >= 0) { groups[best].Add(t); groupOf[t] = best; any = true; }
                    else next.Add(t);
                }
                pending = next;
                if (!any) break;
            }
            // Any neck tile still unplaced (ringed only by walls/necks) is left for AbsorbEnclosedGaps.
        }

        // Pass-neck detection. K=3 tiles matches the agreed "within two or three tiles of the next
        // border"; the dot cutoff means the two flanking walls point more than ~105 degrees apart, i.e.
        // they pinch the tile from genuinely opposing sides rather than lying on a single flank. A tile
        // counts as a flanking "wall" if it is a hard border (water / impassable) OR high ground
        // (LargeHills+) — because a RimWorld mountain range is mostly PASSABLE Mountainous tiles, so a
        // pass is a low saddle between high ground, not between impassable peaks.
        private const int NeckRadius = 3;
        private const float NeckOppositeDot = -0.25f;
        private const int NeckWallHillClass = 2;   // LargeHills and above flank a pass
        private const int NeckLowHillClass = 1;    // only Flat / SmallHills tiles can BE a saddle

        /// <summary>
        /// Flag low saddle tiles pinched between high ground / hard walls on opposite sides — mountain
        /// passes and isthmuses (#20). Only a low tile (Flat/SmallHills) is a candidate; a bounded BFS
        /// (depth <see cref="NeckRadius"/>, travelling only over other low land) collects the bearings
        /// to any flanking wall — water, impassable, or high ground — it reaches, and if two bearings
        /// oppose each other the tile sits in a neck and becomes an extension of the border. High ground
        /// on only one flank (a foothill where a plain meets a range) is never flagged, so a region
        /// still flows up into the mountains; a plateau interior is excluded because it is not low.
        /// </summary>
        private static bool[] MarkPassNecks(WorldGrid grid, bool[] isLand, TileSignal[] signals, int total)
        {
            var isNeck = new bool[total];
            var neighbors = new List<PlanetTile>();
            var dirs = new List<UnityEngine.Vector3>();
            var depth = new Dictionary<int, int>();
            var q = new Queue<int>();
            for (int t = 0; t < total; t++)
            {
                // Only a low, passable land tile can be a saddle.
                if (!isLand[t] || signals[t].HillClass > NeckLowHillClass) continue;

                dirs.Clear(); depth.Clear(); q.Clear();
                q.Enqueue(t); depth[t] = 0;
                UnityEngine.Vector3 ct = grid.GetTileCenter(t);
                bool neck = false;
                while (q.Count > 0 && !neck)
                {
                    int cur = q.Dequeue();
                    int d = depth[cur];
                    neighbors.Clear();
                    grid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        bool flankWall = !isLand[nid] || signals[nid].HillClass >= NeckWallHillClass;
                        if (flankWall)
                        {
                            // A flanking wall reached within the radius: note its bearing.
                            UnityEngine.Vector3 dir = grid.GetTileCenter(nid) - ct;
                            if (dir.sqrMagnitude < 1e-6f) continue;
                            dir = dir.normalized;
                            foreach (var e in dirs) { if (UnityEngine.Vector3.Dot(e, dir) < NeckOppositeDot) { neck = true; break; } }
                            if (neck) break;
                            dirs.Add(dir);
                        }
                        else if (d < NeckRadius && !depth.ContainsKey(nid))
                        {
                            // Traverse only low land while measuring the saddle's width.
                            depth[nid] = d + 1;
                            q.Enqueue(nid);
                        }
                    }
                }
                isNeck[t] = neck;
            }
            return isNeck;
        }

        /// <summary>Reduce a tile to the classified signals the boundary rules read.</summary>
        private static TileSignal Classify(WorldGrid grid, int tileId, Dictionary<BiomeDef, int> biomeIds)
        {
            Tile t = grid[tileId];
            BiomeDef biome = t.PrimaryBiome;

            int biomeId = 0;
            if (biome != null && !biomeIds.TryGetValue(biome, out biomeId))
            {
                biomeId = biomeIds.Count + 1;   // 0 reserved for "no biome"
                biomeIds[biome] = biomeId;
            }

            int hillClass;
            switch (t.hilliness)
            {
                case Hilliness.SmallHills: hillClass = 1; break;
                case Hilliness.LargeHills: hillClass = 2; break;
                case Hilliness.Mountainous: hillClass = 3; break;
                case Hilliness.Impassable: hillClass = 4; break;
                default: hillClass = 0; break;
            }

            float treeDensity = biome != null ? biome.TreeDensity : 0f;
            int forestBucket = treeDensity >= ThickTreeDensity ? 2 : (treeDensity >= WoodedTreeDensity ? 1 : 0);

            bool swamp = t.swampiness > 0.1f
                || (biome != null && (biome.defName.Contains("Swamp") || biome.defName.Contains("Marsh")));
            bool impassable = t.hilliness == Hilliness.Impassable
                || (biome != null && (biome.impassable || biome.defName == "SeaIce"));

            return new TileSignal
            {
                BiomeId = biomeId,
                HillClass = hillClass,
                ForestBucket = forestBucket,
                Swamp = swamp,
                Water = t.WaterCovered,
                Impassable = impassable,
                Temperature = t.temperature,
                Rainfall = t.rainfall,
            };
        }

        /// <summary>
        /// Bounding-box-free elongation of a tile set: the ratio of its two principal axes in the local
        /// tangent plane (1 = round, higher = a long ribbon). Computed by PCA over the tiles' 3D centres
        /// projected onto an east/north frame at their centroid, so it is independent of world
        /// orientation and of the equirectangular distortion a lon/lat box would carry.
        /// </summary>
        public static float Elongation(List<int> tiles)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tiles == null || tiles.Count < 3) return 1f;

            UnityEngine.Vector3 c = UnityEngine.Vector3.zero;
            foreach (int t in tiles) c += grid.GetTileCenter(t);
            c /= tiles.Count;

            UnityEngine.Vector3 up = c.normalized;
            UnityEngine.Vector3 refA = UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(up, UnityEngine.Vector3.up)) > 0.99f
                ? UnityEngine.Vector3.right : UnityEngine.Vector3.up;
            UnityEngine.Vector3 east = UnityEngine.Vector3.Cross(up, refA).normalized;
            UnityEngine.Vector3 north = UnityEngine.Vector3.Cross(east, up).normalized;

            double sxx = 0, syy = 0, sxy = 0;
            foreach (int t in tiles)
            {
                UnityEngine.Vector3 d = grid.GetTileCenter(t) - c;
                double x = UnityEngine.Vector3.Dot(d, east), y = UnityEngine.Vector3.Dot(d, north);
                sxx += x * x; syy += y * y; sxy += x * y;
            }
            double n = tiles.Count;
            double a = sxx / n, b = sxy / n, cc = syy / n;
            double tr = a + cc, det = a * cc - b * b;
            double disc = System.Math.Sqrt(System.Math.Max(0, tr * tr / 4 - det));
            double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
            if (l2 <= 1e-6) return 6f;
            return (float)System.Math.Sqrt(l1 / l2);
        }

        /// <summary>
        /// Split a connected tile set into <paramref name="pieces"/> compact groups: farthest-point
        /// anchors (which land at the extremes of the long axis) claimed by a hop-count watershed, so an
        /// elongated province divides across its short axis into blob-like halves. Deterministic (anchors
        /// resolved in id order). Returns the whole set unsplit if pieces &lt;= 1 or it is too small.
        /// </summary>
        public static List<List<int>> SplitTiles(List<int> tiles, int pieces)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || pieces <= 1 || tiles == null || tiles.Count < pieces)
                return new List<List<int>> { tiles };

            var set = new HashSet<int>(tiles);
            var sorted = new List<int>(tiles); sorted.Sort();

            // Farthest-point sampling for the anchors.
            var anchors = new List<int> { sorted[0] };
            var neigh = new List<PlanetTile>();
            while (anchors.Count < pieces)
            {
                int best = -1; float bestD = -1f;
                foreach (int t in sorted)
                {
                    if (anchors.Contains(t)) continue;
                    float md = float.MaxValue;
                    foreach (int a in anchors) { float d = grid.ApproxDistanceInTiles(t, a); if (d < md) md = d; }
                    if (md > bestD) { bestD = md; best = t; }
                }
                if (best == -1) break;
                anchors.Add(best);
            }
            anchors.Sort();

            // Multi-source hop-count BFS confined to the set; nearest anchor wins, ties to smaller id.
            var owner = new Dictionary<int, int>();
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int a in anchors) { owner[a] = a; dist[a] = 0; q.Enqueue(a); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                neigh.Clear(); grid.GetTileNeighbors(cur, neigh);
                foreach (var nb in neigh)
                {
                    int nid = nb.tileId;
                    if (!set.Contains(nid)) continue;
                    int nd = dist[cur] + 1;
                    if (!dist.TryGetValue(nid, out int old) || nd < old || (nd == old && owner[cur] < owner[nid]))
                    {
                        dist[nid] = nd; owner[nid] = owner[cur]; q.Enqueue(nid);
                    }
                }
            }

            var groups = new Dictionary<int, List<int>>();
            foreach (int a in anchors) groups[a] = new List<int>();
            foreach (int t in tiles) { int o = owner.TryGetValue(t, out int oo) ? oo : anchors[0]; groups[o].Add(t); }
            var outG = new List<List<int>>();
            foreach (int a in anchors) if (groups[a].Count > 0) outG.Add(groups[a]);
            return outG;
        }

    }
}
