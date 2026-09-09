using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RegionsAndSocieties
{
    public static class FactionPlacementUtility
    {
        public static int FindBestTileForFaction(Faction faction)
        {
            World world = Find.World;
            if (world == null) return -1;

            var regionManager = world.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return -1;

            var allProvinces = regionManager.Provinces;
            if (!allProvinces.Any()) return -1;

            WorldGrid worldGrid = world.grid;
            int totalTiles = worldGrid.TilesCount;

            // Get all occupied provinces
            HashSet<GeographicProvince> occupiedProvinces = new HashSet<GeographicProvince>();
            List<int> allPlacedBases = new List<int>();
            List<int> sameFactionBases = new List<int>();

            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
                if (Integration.WorldObjectClassifier.IsSettlement(obj))
                {
                    allPlacedBases.Add(obj.Tile);
                    if (obj.Faction == faction)
                    {
                        sameFactionBases.Add(obj.Tile);
                    }
                    var p = regionManager.GetProvinceForTile(obj.Tile);
                    if (p != null) occupiedProvinces.Add(p);
                }
            }

            // Get profile
            var profile = FactionPlacementSettings.GetProfile(faction.def) ?? FactionPlacementSettings.GetProfile(FactionDefOf.OutlanderCivil);
            if (profile == null) return -1;

            // Score tiles — the same pure rule as worldgen placement (#56): land values from the biome
            // and hilliness, multiplied by this faction's habitability of the biome (vanilla settlement
            // weight × toil × health, scaled by tech), cached per biome.
            int techOrdinal = (int)faction.def.techLevel;
            var weights = new Placement.PlacementWeights(profile.mineralWeight, profile.nutritionWeight, profile.forageWeight, profile.grazingWeight, profile.huntingWeight, profile.marginWeight);
            var habitabilityByBiome = new Dictionary<BiomeDef, float>();
            Dictionary<int, float> tileScores = new Dictionary<int, float>();
            for (int t = 0; t < totalTiles; t++)
            {
                Tile tileData = worldGrid[t];
                if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable || (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")))
                {
                    continue;
                }

                if (!faction.def.allowedArrivalTemperatureRange.Includes(tileData.temperature))
                {
                    continue;
                }

                BiomeDef biome = tileData.PrimaryBiome;
                float habitability = 1f;
                if (biome != null && !habitabilityByBiome.TryGetValue(biome, out habitability))
                {
                    habitability = Placement.BiomeHabitabilityRules.Habitability(BiomeSafe.Traits(biome), techOrdinal);
                    habitabilityByBiome[biome] = habitability;
                }

                Placement.PlacementTileFeatures f = Placement.BiomeHabitabilityRules.Features(BiomeSafe.Traits(biome), BiomeSafe.HillClass(tileData.hilliness));
                tileScores[t] = Placement.BiomeHabitabilityRules.Score(f, habitability, weights);
            }

            // Score provinces
            Dictionary<GeographicProvince, float> provinceScores = new Dictionary<GeographicProvince, float>();
            foreach (var p in allProvinces)
            {
                // Land only — factions never settle water, and scanning the ~50k-tile ocean province
                // per faction just to discard it is pure worldgen cost (#20).
                if (p.provinceType != ProvinceType.Land) continue;
                if (p.tiles == null || p.tiles.Count < 20) continue;

                var validTiles = p.tiles.Where(t => tileScores.ContainsKey(t)).ToList();
                if (validTiles.Count == 0) continue;

                provinceScores[p] = validTiles.Average(t => tileScores[t]);
            }

            // Get provinces that own the faction
            List<GeographicProvince> factionProvinces = new List<GeographicProvince>();
            string factionId = faction.GetUniqueLoadID();
            foreach (var p in allProvinces)
            {
                if (p.owningFactionIds.Contains(factionId))
                {
                    factionProvinces.Add(p);
                }
            }

            // Select best province — excluding provinces occupied by a settlement and (as of #65) those
            // a rival loose-owns (>=51%), so NPC placement stops at rival borders instead of interleaving.
            // Shape matters here (#19): suitability is bent by how embedded the candidate is in the
            // faction's existing domain, so growth fills pockets before it extends tendrils. The old sort
            // used only a binary touches-at-all flag — it computed shared borders and then ignored them,
            // which is exactly where spidering territories came from.
            var heldIds = new HashSet<int>(factionProvinces.Select(fp => fp.id));

            // #56 crowding: settleable provinces per biome against settlements already in it (the
            // occupied provinces), so a late placement spreads to the next-best biome instead of
            // stacking into the richest one.
            var availableByBiome = new Dictionary<BiomeDef, int>();
            var settledByBiome = new Dictionary<BiomeDef, int>();
            foreach (var ap in allProvinces)
            {
                if (ap.provinceType != ProvinceType.Land || ap.tiles == null || ap.tiles.Count < 20 || ap.primaryBiome == null) continue;
                availableByBiome.TryGetValue(ap.primaryBiome, out int avail);
                availableByBiome[ap.primaryBiome] = avail + 1;
                if (occupiedProvinces.Contains(ap))
                {
                    settledByBiome.TryGetValue(ap.primaryBiome, out int settled);
                    settledByBiome[ap.primaryBiome] = settled + 1;
                }
            }

            // #46 clustering: the faction's bodies so far and its cap; a candidate that would overflow a
            // body ranks behind every candidate that would not.
            int clusterCap = Placement.ClusteringRules.Snap(profile.clusterSize);
            var bodies = new Placement.TerritoryBodies();
            foreach (var held in factionProvinces)
            {
                bodies.Add(held.id, held.borderShares != null ? held.borderShares.Keys : null);
            }
            var candidates = allProvinces
                .Where(p => !occupiedProvinces.Contains(p) && provinceScores.ContainsKey(p) && !RegionalOwnershipUtility.IsLooseOwnedByRival(p, faction))
                .Select(p => {
                    float suitability = provinceScores[p];
                    if (p.primaryBiome != null)
                    {
                        settledByBiome.TryGetValue(p.primaryBiome, out int settledHere);
                        availableByBiome.TryGetValue(p.primaryBiome, out int availableHere);
                        suitability *= Placement.BiomeHabitabilityRules.Crowding(settledHere, availableHere);   // #56
                    }

                    float minAllyDist = 9999f;
                    if (factionProvinces.Any())
                    {
                        foreach (var ownP in factionProvinces)
                        {
                            float dist = GetProvinceDistance(p, ownP, worldGrid);
                            if (dist < minAllyDist) minAllyDist = dist;
                        }
                    }

                    TerritoryCompactnessUtility.CountBorders(p, heldIds, regionManager, worldGrid, out int owned, out int claimable);
                    bool isAdjacent = owned > 0;
                    float embeddedness = Placement.CompactnessRules.Embeddedness(owned, claimable);
                    float effective = Placement.CompactnessRules.EffectiveScore(
                        suitability, embeddedness,
                        Placement.CompactnessRules.DefaultDesiredRatio,
                        FactionPlacementSettings.territoryCompactness);

                    // #46: the body this province would form, whether it stays within the cap, and — for a
                    // new body — the distance to the nearest existing body (minAllyDist), so a new cluster
                    // is not opened right next to another.
                    int mergedSize = bodies.MergedSizeIfAdded(p.borderShares != null ? p.borderShares.Keys : null);
                    bool withinCap = Placement.ClusteringRules.WithinCap(mergedSize, clusterCap);
                    bool extends = mergedSize > 1;
                    float nearestBody = extends || !factionProvinces.Any() ? -1f : minAllyDist;
                    int placementClass = Placement.ClusteringRules.PlacementClass(
                        withinCap, extends, Placement.ClusteringRules.FarEnoughForNewBody(nearestBody));

                    return new { Province = p, Score = suitability, Effective = effective, IsAdjacent = isAdjacent, Dist = minAllyDist, PlacementClass = placementClass };
                })
                .ToList();

            if (!candidates.Any()) return -1;

            // Sort candidates: within the cluster cap first (#46), then adjacent first if we have existing
            // provinces, then the shape-bent score.
            var sorted = candidates.AsEnumerable();
            if (factionProvinces.Any())
            {
                sorted = sorted.OrderBy(x => x.PlacementClass)   // #46: extend, spaced new body, crowded new body, overflow
                               .ThenByDescending(x => x.IsAdjacent ? 1 : 0)
                               .ThenByDescending(x => x.Effective)
                               .ThenBy(x => x.Dist);
            }
            else
            {
                sorted = sorted.OrderByDescending(x => x.Score);   // first foothold: shape has nothing to square against
            }

            var candidatesList = sorted.ToList();
            var chosenProvince = candidatesList[0].Province;
            return FindBestTileInProvince(chosenProvince, sameFactionBases, allPlacedBases, tileScores, worldGrid);
        }

        private static float GetProvinceDistance(GeographicProvince p1, GeographicProvince p2, WorldGrid worldGrid)
        {
            if (p1.tiles.Count == 0 || p2.tiles.Count == 0) return 9999f;
            return worldGrid.ApproxDistanceInTiles(p1.tiles[0], p2.tiles[0]);
        }

        public static float EvaluatePopulationRetention(int startTileId, HashSet<int> provinceTiles)
        {
            if (Find.WorldGrid == null) return 0f;

            var visited = new HashSet<int>();
            var queue = new Queue<PopulationDensityUtility.QueueEntry>();

            PlanetTile startPlanetTile = PlanetTile.Invalid;
            var tempNeighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(startTileId, tempNeighbors);
            if (tempNeighbors.Any())
            {
                var doubleNeighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(tempNeighbors[0].tileId, doubleNeighbors);
                foreach (var t in doubleNeighbors)
                {
                    if (t.tileId == startTileId)
                    {
                        startPlanetTile = t;
                        break;
                    }
                }
            }

            if (startPlanetTile == PlanetTile.Invalid)
            {
                startPlanetTile = new PlanetTile(startTileId);
            }

            queue.Enqueue(new PopulationDensityUtility.QueueEntry(startPlanetTile, 1.0f));
            visited.Add(startTileId);

            float totalContainedMultiplier = 0f;
            var neighborsList = new List<PlanetTile>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                PlanetTile currentTile = current.tile;
                int currentTileId = currentTile.tileId;
                float currentMultiplier = current.multiplier;

                if (currentMultiplier < 0.001f) continue;

                if (provinceTiles.Contains(currentTileId))
                {
                    totalContainedMultiplier += currentMultiplier;
                }

                neighborsList.Clear();
                Find.WorldGrid.GetTileNeighbors(currentTileId, neighborsList);
                foreach (var neighbor in neighborsList)
                {
                    int neighborId = neighbor.tileId;
                    if (!visited.Contains(neighborId))
                    {
                        visited.Add(neighborId);

                        float stepMultiplier = PopulationDensityUtility.GetStepMultiplier(currentTile, neighbor);
                        if (stepMultiplier > 0f)
                        {
                            queue.Enqueue(new PopulationDensityUtility.QueueEntry(neighbor, currentMultiplier * stepMultiplier));
                        }
                    }
                }
            }

            return totalContainedMultiplier;
        }

        private static int FindBestTileInProvince(GeographicProvince province, List<int> sameFactionBases, List<int> allPlacedBases, Dictionary<int, float> tileScores, WorldGrid worldGrid)
        {
            var candidateTiles = province.tiles
                .Where(t => tileScores.ContainsKey(t) && !allPlacedBases.Contains(t))
                .ToList();

            if (!candidateTiles.Any()) return -1;
            if (candidateTiles.Count == 1) return candidateTiles[0];

            // Compute province centroid
            Vector3 centroid = Vector3.zero;
            foreach (int t in province.tiles)
            {
                centroid += worldGrid.GetTileCenter(t);
            }
            centroid /= province.tiles.Count;

            HashSet<int> provinceTiles = new HashSet<int>(province.tiles);

            // Compute scores
            var tileDataList = new List<TileScoreData>();
            float minRes = float.MaxValue, maxRes = float.MinValue;
            float minCentroidDist = float.MaxValue, maxCentroidDist = float.MinValue;
            float minPop = float.MaxValue, maxPop = float.MinValue;

            foreach (int t in candidateTiles)
            {
                float res = tileScores[t];
                if (res < minRes) minRes = res;
                if (res > maxRes) maxRes = res;

                float dist = (worldGrid.GetTileCenter(t) - centroid).magnitude;
                if (dist < minCentroidDist) minCentroidDist = dist;
                if (dist > maxCentroidDist) maxCentroidDist = dist;

                float pop = EvaluatePopulationRetention(t, provinceTiles);
                if (pop < minPop) minPop = pop;
                if (pop > maxPop) maxPop = pop;

                tileDataList.Add(new TileScoreData { Tile = t, ResScore = res, CentroidDist = dist, PopRetention = pop });
            }

            // Calculate final score: 20% centrality, 40% resources, 40% population retention
            var sortedCandidates = tileDataList.Select(data =>
            {
                float normRes = (maxRes > minRes) ? (data.ResScore - minRes) / (maxRes - minRes) : 1.0f;
                float normCentroidDist = (maxCentroidDist > minCentroidDist) ? (data.CentroidDist - minCentroidDist) / (maxCentroidDist - minCentroidDist) : 0.0f;
                float centrality = 1.0f - normCentroidDist;
                float normPop = (maxPop > minPop) ? (data.PopRetention - minPop) / (maxPop - minPop) : 1.0f;

                float finalScore = 0.4f * normRes + 0.2f * centrality + 0.4f * normPop;
                return new { Tile = data.Tile, FinalScore = finalScore };
            })
            .OrderByDescending(x => x.FinalScore)
            .Select(x => x.Tile)
            .ToList();

            foreach (var tile in sortedCandidates)
            {
                bool tooCloseToRival = false;
                foreach (var otherBase in allPlacedBases)
                {
                    if (sameFactionBases.Contains(otherBase)) continue;
                    float dist = worldGrid.ApproxDistanceInTiles(tile, otherBase);
                    if (dist < 4f)
                    {
                        tooCloseToRival = true;
                        break;
                    }
                }
                if (!tooCloseToRival) return tile;
            }

            return sortedCandidates[0];
        }

        private struct TileScoreData
        {
            public int Tile;
            public float ResScore;
            public float CentroidDist;
            public float PopRetention;
        }
    }
}
