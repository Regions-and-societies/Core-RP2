using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RegionsAndSocieties.Roads;

namespace RegionsAndSocieties
{
    /// <summary>
    /// NOT CALLED at worldgen in 0.3.0 — settlement road-linking is deferred to 0.4.0 for a rework.
    /// Kept (with its bounded search and tests) so the rework starts from a working, measured base.
    ///
    /// <para>Draws roads between generated settlements at worldgen: each base to its nearest same-faction</para>
    /// base, and each pair of non-hostile factions between their two closest bases, when within
    /// <see cref="RoadPathRules.MaxLinkDistanceTiles"/>. The path search itself is the pure, depth-bounded
    /// <see cref="RoadPathRules.FindPath"/> (#38): a pair cut off by water costs a small local scan, not a
    /// flood of the whole landmass.
    /// </summary>
    public static class RoadGeneratorHelper
    {
        private struct RoadGenStats
        {
            public int Searches;
            public int Roads;
            public int Segments;
            public long TilesVisited;
            public int BudgetExhausted;
            public int Isolated;
        }

        public static void GenerateRoadsBetweenBases()
        {
            if (Find.World == null || Find.WorldGrid == null) return;

            Log.Message("[RegionsAndSocieties] RoadGeneratorHelper linking settlements...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            RoadDef dirtRoad = DefDatabase<RoadDef>.GetNamed("DirtRoad", false) ?? DefDatabase<RoadDef>.AllDefs.FirstOrDefault(r => r.defName.Contains("Dirt"));
            RoadDef pavedRoad = DefDatabase<RoadDef>.GetNamed("StoneRoad", false) ?? DefDatabase<RoadDef>.AllDefs.FirstOrDefault(r => r.defName.Contains("Stone") || r.defName.Contains("Highway") || r.defName.Contains("Asphalt"));

            if (dirtRoad == null && pavedRoad == null) return;

            var settlements = Find.WorldObjects.Settlements;
            if (settlements == null || !settlements.Any()) return;

            var activeNPCFactions = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.Hidden).ToList();

            // Each faction's bases, materialised once (#38) — not once per faction pair inside the
            // O(factions²) trade loop below.
            var basesByFaction = new Dictionary<Faction, List<Settlement>>();
            foreach (var settlement in settlements)
            {
                if (settlement.Faction == null) continue;
                if (!basesByFaction.TryGetValue(settlement.Faction, out var list))
                {
                    list = new List<Settlement>();
                    basesByFaction[settlement.Faction] = list;
                }
                list.Add(settlement);
            }

            var stats = new RoadGenStats();
            var empty = new List<Settlement>();

            // 1. Link settlements within the same faction (contiguity lines)
            foreach (var faction in activeNPCFactions)
            {
                if (!basesByFaction.TryGetValue(faction, out var factionBases)) continue;
                if (factionBases.Count < 2) continue;

                RoadDef internalRoadDef = (faction.def.techLevel >= TechLevel.Industrial) ? pavedRoad : dirtRoad;
                if (internalRoadDef == null) internalRoadDef = dirtRoad ?? pavedRoad;

                for (int i = 0; i < factionBases.Count; i++)
                {
                    var baseA = factionBases[i];
                    Settlement closestAlly = null;
                    float minDist = 9999f;

                    for (int j = 0; j < factionBases.Count; j++)
                    {
                        if (i == j) continue;
                        var baseB = factionBases[j];
                        float dist = Find.WorldGrid.ApproxDistanceInTiles(baseA.Tile, baseB.Tile);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestAlly = baseB;
                        }
                    }

                    if (closestAlly != null && minDist <= RoadPathRules.MaxLinkDistanceTiles)
                    {
                        GenerateRoadPath(baseA.Tile, closestAlly.Tile, minDist, internalRoadDef, ref stats);
                    }
                }
            }

            // 2. Link friendly/neutral settlements of different factions (goodwill >= 0) within range
            for (int i = 0; i < activeNPCFactions.Count; i++)
            {
                var f1 = activeNPCFactions[i];
                var f1Bases = basesByFaction.TryGetValue(f1, out var l1) ? l1 : empty;
                if (f1Bases.Count == 0) continue;

                for (int j = i + 1; j < activeNPCFactions.Count; j++)
                {
                    var f2 = activeNPCFactions[j];
                    if (f1.GoodwillWith(f2) < 0) continue;
                    var f2Bases = basesByFaction.TryGetValue(f2, out var l2) ? l2 : empty;
                    if (f2Bases.Count == 0) continue;

                    Settlement bestA = null;
                    Settlement bestB = null;
                    float minDist = 9999f;

                    foreach (var baseA in f1Bases)
                    {
                        foreach (var baseB in f2Bases)
                        {
                            float dist = Find.WorldGrid.ApproxDistanceInTiles(baseA.Tile, baseB.Tile);
                            if (dist < minDist)
                            {
                                minDist = dist;
                                bestA = baseA;
                                bestB = baseB;
                            }
                        }
                    }

                    if (bestA != null && bestB != null && minDist <= RoadPathRules.MaxLinkDistanceTiles)
                    {
                        RoadDef tradeRoadDef = (f1.def.techLevel >= TechLevel.Industrial || f2.def.techLevel >= TechLevel.Industrial) ? pavedRoad : dirtRoad;
                        if (tradeRoadDef == null) tradeRoadDef = dirtRoad ?? pavedRoad;

                        GenerateRoadPath(bestA.Tile, bestB.Tile, minDist, tradeRoadDef, ref stats);
                    }
                }
            }

            sw.Stop();
            Log.Message($"[RegionsAndSocieties] Road generation: {stats.Roads} roads ({stats.Segments} segments) from {stats.Searches} searches, "
                + $"{stats.TilesVisited} tiles visited, {stats.BudgetExhausted} gave up at the search budget, {stats.Isolated} isolated, "
                + $"in {sw.ElapsedMilliseconds} ms.");
        }

        private static readonly List<PlanetTile> planetNeighbors = new List<PlanetTile>();

        private static bool IsPassable(int tileId)
        {
            Tile tileData = Find.WorldGrid[tileId];
            return !tileData.WaterCovered && tileData.hilliness != Hilliness.Impassable;
        }

        private static void NeighborsOf(int tileId, List<int> into)
        {
            planetNeighbors.Clear();
            Find.WorldGrid.GetTileNeighbors(tileId, planetNeighbors);
            for (int i = 0; i < planetNeighbors.Count; i++) into.Add(planetNeighbors[i].tileId);
        }

        private static void GenerateRoadPath(int startTile, int endTile, float approxDistance, RoadDef roadDef, ref RoadGenStats stats)
        {
            if (Find.WorldGrid == null || roadDef == null) return;

            stats.Searches++;

            // A settlement never stands on water or an impassable peak, but a cheap check here is what
            // keeps an unexpected endpoint from costing a full local scan.
            if (!IsPassable(startTile) || !IsPassable(endTile))
            {
                stats.Isolated++;
                return;
            }

            int maxDepth = RoadPathRules.DepthBudget(approxDistance);

            List<int> path = RoadPathRules.FindPath(startTile, endTile, maxDepth, IsPassable, NeighborsOf, out var search);
            stats.TilesVisited += search.TilesVisited;

            if (path == null)
            {
                if (search.BudgetExhausted)
                {
                    stats.BudgetExhausted++;
                    // Dev-mode parity evidence (#38): a pair the budget cut off is re-run unbounded and the
                    // answer logged, so the log itself shows whether anything reachable was lost. Only these
                    // few pairs pay the old full-landmass cost, and only with dev mode on.
                    if (Prefs.DevMode && maxDepth != int.MaxValue)
                    {
                        List<int> unbounded = RoadPathRules.FindPath(startTile, endTile, int.MaxValue, IsPassable, NeighborsOf, out var check);
                        Log.Message($"[RegionsAndSocieties]   road budget-cut pair {startTile}->{endTile} (approx {approxDistance:0.0} tiles, budget {maxDepth} hops): unbounded search "
                            + (unbounded == null ? $"finds NO path either ({check.TilesVisited} tiles flooded)" : $"finds a {unbounded.Count - 1}-hop detour ({check.TilesVisited} tiles flooded)") + ".");
                    }
                }
                else stats.Isolated++;
                return;
            }

            stats.Roads++;
            for (int i = 1; i < path.Count; i++)
            {
                Find.WorldGrid.OverlayRoad(path[i - 1], path[i], roadDef);
                stats.Segments++;
            }
        }
    }
}
