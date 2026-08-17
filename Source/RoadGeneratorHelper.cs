using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties
{
    public static class RoadGeneratorHelper
    {
        public static void GenerateRoadsBetweenBases()
        {
            if (Find.World == null || Find.WorldGrid == null) return;

            Log.Message("[RegionsAndSocieties] RoadGeneratorHelper linking settlements...");

            RoadDef dirtRoad = DefDatabase<RoadDef>.GetNamed("DirtRoad", false) ?? DefDatabase<RoadDef>.AllDefs.FirstOrDefault(r => r.defName.Contains("Dirt"));
            RoadDef pavedRoad = DefDatabase<RoadDef>.GetNamed("StoneRoad", false) ?? DefDatabase<RoadDef>.AllDefs.FirstOrDefault(r => r.defName.Contains("Stone") || r.defName.Contains("Highway") || r.defName.Contains("Asphalt"));

            if (dirtRoad == null && pavedRoad == null) return;

            var settlements = Find.WorldObjects.Settlements;
            if (settlements == null || !settlements.Any()) return;

            var activeNPCFactions = Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.Hidden).ToList();

            // 1. Link settlements within the same faction (contiguity lines)
            foreach (var faction in activeNPCFactions)
            {
                var factionBases = settlements.Where(s => s.Faction == faction).ToList();
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

                    if (closestAlly != null && minDist <= 16f)
                    {
                        GenerateRoadPath(baseA.Tile, closestAlly.Tile, internalRoadDef);
                    }
                }
            }

            // 2. Link friendly/neutral settlements of different factions (goodwill >= 0) within 16 tiles
            for (int i = 0; i < activeNPCFactions.Count; i++)
            {
                var f1 = activeNPCFactions[i];
                for (int j = i + 1; j < activeNPCFactions.Count; j++)
                {
                    var f2 = activeNPCFactions[j];
                    if (f1.GoodwillWith(f2) >= 0)
                    {
                        var f1Bases = settlements.Where(s => s.Faction == f1).ToList();
                        var f2Bases = settlements.Where(s => s.Faction == f2).ToList();

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

                        if (bestA != null && bestB != null && minDist <= 16f)
                        {
                            RoadDef tradeRoadDef = (f1.def.techLevel >= TechLevel.Industrial || f2.def.techLevel >= TechLevel.Industrial) ? pavedRoad : dirtRoad;
                            if (tradeRoadDef == null) tradeRoadDef = dirtRoad ?? pavedRoad;

                            GenerateRoadPath(bestA.Tile, bestB.Tile, tradeRoadDef);
                        }
                    }
                }
            }
        }

        private static void GenerateRoadPath(int startTile, int endTile, RoadDef roadDef)
        {
            if (Find.WorldGrid == null || roadDef == null) return;

            var queue = new Queue<int>();
            var parent = new Dictionary<int, int>();
            var visited = new HashSet<int>();

            queue.Enqueue(startTile);
            visited.Add(startTile);

            bool found = false;
            var neighbors = new List<RimWorld.Planet.PlanetTile>();

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == endTile)
                {
                    found = true;
                    break;
                }

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(current, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborId = n.tileId;
                    if (visited.Contains(neighborId)) continue;

                    Tile tileData = Find.WorldGrid[neighborId];
                    if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable)
                    {
                        continue;
                    }

                    visited.Add(neighborId);
                    parent[neighborId] = current;
                    queue.Enqueue(neighborId);
                }
            }

            if (found)
            {
                int curr = endTile;
                while (curr != startTile)
                {
                    int prev = parent[curr];
                    Find.WorldGrid.OverlayRoad(prev, curr, roadDef);
                    curr = prev;
                }
            }
        }
    }
}
