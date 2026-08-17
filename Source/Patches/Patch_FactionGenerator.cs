using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RegionsAndSocieties.Patches
{
    [HarmonyPatch(typeof(FactionGenerator), "GenerateFactionsIntoWorldLayer")]
    public static class Patch_FactionGenerator_GenerateFactionsIntoWorld
    {
        [HarmonyPrefix]
        public static bool Prefix(PlanetLayer layer, List<FactionDef> factions)
        {
            if (layer == null || layer.Def == null || layer.Def.defName != "Surface")
            {
                Log.Message($"[RegionsAndSocieties] Bypassing custom faction generator for non-surface layer '{layer?.Def?.defName ?? "null"}'. Falling back to vanilla.");
                return true;
            }

            Log.Message("[RegionsAndSocieties] Custom Faction Generation and Placement solver starting...");
            if (Prefs.DevMode)
            {
                Log.Message("[RegionsAndSocieties] Call site:\n" + new System.Diagnostics.StackTrace());
            }

            World world = Find.World ?? Current.CreatingWorld;
            if (world == null || world.info == null || world.grid == null)
            {
                Log.Warning("[RegionsAndSocieties] World, World.info, or World.grid is null! Falling back to vanilla generator.");
                return true;
            }

            if (factions == null)
            {
                factions = new List<FactionDef>();
                foreach (var def in DefDatabase<FactionDef>.AllDefsListForReading)
                {
                    if (!def.isPlayer && !def.hidden && def.defName != "PColony")
                    {
                        factions.Add(def);
                    }
                }
            }

            FactionManager factionManager = world.factionManager;
            if (factionManager == null)
            {
                Log.Warning("[RegionsAndSocieties] FactionManager is null! Falling back to vanilla generator.");
                return true;
            }

            WorldGrid worldGrid = world.grid;
            WorldObjectsHolder worldObjects = world.worldObjects;

            var regionManager = world.GetComponent<SynapseRegionManager>();
            if (regionManager == null)
            {
                Log.Warning("[RegionsAndSocieties] SynapseRegionManager is null! Falling back to vanilla generator.");
                return true;
            }

            regionManager.GenerateProvinces();

            float coverage = world.info.planetCoverage;
            
            int landTilesCount = 0;
            int totalTiles = worldGrid.TilesCount;
            for (int i = 0; i < totalTiles; i++)
            {
                if (!worldGrid[i].WaterCovered)
                {
                    landTilesCount++;
                }
            }

            int targetFactionCount = Mathf.RoundToInt(coverage * 30f * (landTilesCount / 40000f));
            if (targetFactionCount < 5) targetFactionCount = 5;
            if (targetFactionCount > 35) targetFactionCount = 35;

            var canExistOnLayerMethod = typeof(FactionGenerator).GetMethod("CanExistOnLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            List<FactionDef> poolToClone = DefDatabase<FactionDef>.AllDefs
                .Where(f => !f.isPlayer && !f.hidden && f.defName != "PColony")
                .Where(f => {
                    if (canExistOnLayerMethod != null)
                    {
                        return (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, f });
                    }
                    return true;
                })
                .ToList();

            List<FactionDef> finalDefs = new List<FactionDef>();
            foreach (var def in factions)
            {
                if (canExistOnLayerMethod == null || (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, def }))
                {
                    finalDefs.Add(def);
                }
            }

            if (poolToClone.Any())
            {
                while (finalDefs.Count(d => !d.isPlayer && !d.hidden) < targetFactionCount)
                {
                    finalDefs.Add(poolToClone.RandomElement());
                }
            }

            WorldObjectDef origSettlementDef = null;
            System.Reflection.FieldInfo settlementField = typeof(PlanetLayerDef).GetField("settlementWorldObjectDef", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (layer != null && layer.Def != null && settlementField != null)
            {
                origSettlementDef = (WorldObjectDef)settlementField.GetValue(layer.Def);
                settlementField.SetValue(layer.Def, null);
            }

            try
            {
                List<Faction> generatedFactions = new List<Faction>();
                foreach (var def in finalDefs)
                {
                    Faction faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default(IdeoGenerationParms), true));
                    if (faction != null)
                    {
                        factionManager.Add(faction);
                        generatedFactions.Add(faction);
                    }
                }

                foreach (FactionDef def in DefDatabase<FactionDef>.AllDefs)
                {
                    if (def.hidden && factionManager.FirstFactionOfDef(def) == null)
                    {
                        Faction faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default(IdeoGenerationParms), true));
                        if (faction != null)
                        {
                            factionManager.Add(faction);
                        }
                    }
                }
            }
            finally
            {
                if (layer != null && layer.Def != null && settlementField != null)
                {
                    settlementField.SetValue(layer.Def, origSettlementDef);
                }
            }

            foreach (var f1 in factionManager.AllFactions)
            {
                foreach (var f2 in factionManager.AllFactions)
                {
                    if (f1 != f2 && f1.RelationWith(f2, true) == null)
                    {
                        f1.RelationWith(f2, true);
                    }
                }
            }

            List<int> placedBases = new List<int>();
            var allNPCFactions = factionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.def.hidden)
                .Where(f => {
                    if (canExistOnLayerMethod != null)
                    {
                        return (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, f.def });
                    }
                    return true;
                })
                .ToList();

            var allProvinces = regionManager.Provinces;
            if (!allProvinces.Any())
            {
                Log.Warning("[RegionsAndSocieties] No provinces generated! Falling back to vanilla generator.");
                return true;
            }
            Faction playerFaction = Find.FactionManager?.OfPlayer;

            // Global tracking of provinces that already contain a settlement
            HashSet<GeographicProvince> occupiedProvinces = new HashSet<GeographicProvince>();

            // Calculate raw target base counts for all NPC factions
            Dictionary<Faction, int> factionTargetBases = new Dictionary<Faction, int>();
            int totalHostileTarget = 0;
            int totalNonHostileTarget = 0;

            float mapSizeMult = coverage / 0.05f;
            float landRatio = (float)landTilesCount / totalTiles;
            float landRatioMult = landRatio / 0.05f;

            foreach (var faction in allNPCFactions)
            {
                float rngVal = GetFactionRng(faction);
                int baseCount = Mathf.RoundToInt((mapSizeMult * landRatioMult * rngVal) / 6f);
                baseCount = Mathf.Clamp(baseCount, 1, 40);

                factionTargetBases[faction] = baseCount;

                bool isHostile = (playerFaction != null) ? faction.HostileTo(playerFaction) : faction.def.permanentEnemy;
                if (isHostile)
                {
                    totalHostileTarget += baseCount;
                }
                else
                {
                    totalNonHostileTarget += baseCount;
                }
            }

            // Adjust for threat percentage cap (default 50%)
            float maxThreatPercent = FactionPlacementSettings.maxThreatPercent;
            if (maxThreatPercent < 1.0f && totalNonHostileTarget > 0)
            {
                int maxHostileAllowed = Mathf.RoundToInt(totalNonHostileTarget * maxThreatPercent / (1f - maxThreatPercent));
                if (totalHostileTarget > maxHostileAllowed)
                {
                    float hostileScale = (float)maxHostileAllowed / totalHostileTarget;
                    foreach (var faction in allNPCFactions)
                    {
                        bool isHostile = (playerFaction != null) ? faction.HostileTo(playerFaction) : faction.def.permanentEnemy;
                        if (isHostile)
                        {
                            int scaled = Mathf.RoundToInt(factionTargetBases[faction] * hostileScale);
                            factionTargetBases[faction] = Mathf.Max(1, scaled);
                        }
                    }
                }
            }

            // #51: the total settlement volume is driven by a single density knob — the target fraction of
            // livable LAND area claimed by territories — not by the old raw tile-count scaling (which made
            // planets wall-to-wall and blew up on large worlds). Each settlement claims one province, so the
            // fraction is applied to the count of LAND provinces (the unit of claimed ground; ocean is not
            // livable and is excluded). Scaling is BIDIRECTIONAL so the knob is the single monotonic driver:
            // the per-faction counts computed above become only the relative distribution and are normalized
            // to hit the target. The floor of one base per faction keeps every faction on the map.
            int landProvinceCount = allProvinces.Count(p => p.provinceType == ProvinceType.Land);
            int totalBasesAfterThreat = factionTargetBases.Values.Sum();
            int maxBasesAllowed = Mathf.Max(allNPCFactions.Count, Mathf.RoundToInt(landProvinceCount * FactionPlacementSettings.claimedLandAreaPercent));

            if (totalBasesAfterThreat > 0 && totalBasesAfterThreat != maxBasesAllowed)
            {
                float globalScale = (float)maxBasesAllowed / totalBasesAfterThreat;
                foreach (var faction in allNPCFactions)
                {
                    int scaled = Mathf.RoundToInt(factionTargetBases[faction] * globalScale);
                    factionTargetBases[faction] = Mathf.Max(1, scaled);
                }
            }

            // Sort and interleave NPC Factions: 1 Industrial, then 1 Tribal, etc.
            var industrials = allNPCFactions
                .Where(f => f.def.techLevel == TechLevel.Industrial)
                .OrderBy(f => GetCategoryPriority(f))
                .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                .ToList();

            var tribals = allNPCFactions
                .Where(f => f.def.techLevel < TechLevel.Industrial)
                .OrderBy(f => GetCategoryPriority(f))
                .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                .ToList();

            var others = allNPCFactions
                .Where(f => f.def.techLevel > TechLevel.Industrial)
                .OrderBy(f => GetCategoryPriority(f))
                .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                .ToList();

            List<Faction> alternatingFactions = new List<Faction>();
            int indIndex = 0;
            int triIndex = 0;
            int othIndex = 0;

            while (indIndex < industrials.Count || triIndex < tribals.Count || othIndex < others.Count)
            {
                if (indIndex < industrials.Count)
                {
                    alternatingFactions.Add(industrials[indIndex++]);
                }
                if (triIndex < tribals.Count)
                {
                    alternatingFactions.Add(tribals[triIndex++]);
                }
                if (othIndex < others.Count)
                {
                    alternatingFactions.Add(others[othIndex++]);
                }
            }

            // #65 perf: a province's barrier-border count (its impassable/water frontier) is static, so
            // compute it once here instead of per candidate per base inside the placement loop below.
            var barrierCountByProvince = new Dictionary<int, int>();
            foreach (var bp in allProvinces)
            {
                barrierCountByProvince[bp.id] = GetBarrierBorderCount(bp, worldGrid);
            }

            foreach (var faction in alternatingFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(faction.def);
                if (profile == null) continue;

                int baseCount = factionTargetBases.ContainsKey(faction) ? factionTargetBases[faction] : 5;

                Dictionary<int, float> tileScores = new Dictionary<int, float>();
                for (int t = 0; t < totalTiles; t++)
                {
                    Tile tileData = worldGrid[t];
                    if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable || (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")))
                    {
                        tileScores[t] = -9999f;
                        continue;
                    }

                    if (!faction.def.allowedArrivalTemperatureRange.Includes(tileData.temperature))
                    {
                        tileScores[t] = -9999f;
                        continue;
                    }

                    float mineralVal = 0.5f;
                    if (tileData.hilliness == Hilliness.SmallHills) mineralVal = 1.0f;
                    else if (tileData.hilliness == Hilliness.LargeHills) mineralVal = 2.0f;
                    else if (tileData.hilliness == Hilliness.Mountainous) mineralVal = 3.0f;

                    float nutritionVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.plantDensity : 0.5f;
                    float forageVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.forageability : 0.5f;
                    float biomassVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.TreeDensity : 0.5f;
                    float grazingVal = (tileData.hilliness == Hilliness.Flat) ? nutritionVal * 2f : nutritionVal;
                    float hospVal = nutritionVal * 2f + forageVal;

                    float score = 0f;
                    score += profile.mineralWeight * mineralVal;
                    score += profile.nutritionWeight * nutritionVal;
                    score += profile.forageWeight * forageVal;
                    score += profile.grazingWeight * grazingVal;
                    score += profile.huntingWeight * biomassVal;

                    if (profile.marginWeight > 0f)
                    {
                        score += profile.marginWeight * Mathf.Max(0f, 3.0f - hospVal);
                    }

                    tileScores[t] = score;
                }

                Dictionary<GeographicProvince, float> provinceScores = new Dictionary<GeographicProvince, float>();
                foreach (var p in allProvinces)
                {
                    // Do not place settlements in area of less than 20 tiles
                    if (p.tiles == null)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }
                    if (p.tiles.Count < 20)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }

                    var validTiles = p.tiles.Where(t => tileScores.ContainsKey(t) && tileScores[t] > -9999f).ToList();
                    if (validTiles.Count == 0)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }
                    provinceScores[p] = validTiles.Average(t => tileScores[t]);
                }

                List<GeographicProvince> factionProvinces = new List<GeographicProvince>();
                List<int> factionBases = new List<int>();

                // #65: refresh ownership so this faction's placement decision can read the territory the
                // factions before it already hold and claim. Force the recompute — worldgen settlement
                // adds do not bump the ownership epoch, so the gated call would reuse stale/empty data.
                // Rival ownership is static during THIS faction's own placement (only its bases are added
                // below), so one refresh per faction is enough; the per-province reads below are cheap.
                regionManager.MarkOwnersDirty();
                regionManager.RecalculateProvinceOwners();

                // #65 perf: rival ownership is static during THIS faction's own placement (only its bases
                // are added below), so the set of provinces a rival claims (>=30%) is computed ONCE here,
                // straight from the refreshed ownership cache — no tile walking. Own contiguity still
                // updates per base (it grows as the faction places), but it too reads precomputed neighbour
                // province ids from borderShares, so each candidate is O(neighbours), not a tile scan.
                // Before this, the per-candidate-per-base tile walks (shared-border, adjacency, barrier,
                // distance) made worldgen hang once the faction count grew (the VFE suite exposed it).
                var rivalClaimedProvinceIds = new HashSet<int>();
                foreach (var cp in allProvinces)
                {
                    var od = cp.ownershipData;
                    if (od?.factionScores == null) continue;
                    for (int si = 0; si < od.factionScores.Count; si++)
                    {
                        var s = od.factionScores[si];
                        if (s.faction != null && s.faction != faction && s.TotalScore >= Placement.PlacementRules.OwnershipThreshold)
                        {
                            rivalClaimedProvinceIds.Add(cp.id);
                            break;
                        }
                    }
                }

                for (int b = 0; b < baseCount; b++)
                {
                    GeographicProvince chosenProvince = null;
                    string factionId = faction.GetUniqueLoadID();
                    var factionProvinceIds = new HashSet<int>(factionProvinces.Select(fp => fp.id));

                    // Claim/resource inputs for every unoccupied candidate. All O(neighbours) per candidate
                    // via borderShares (precomputed neighbour province ids) and the static barrier cache.
                    var allCandidates = allProvinces
                        .Where(p => !occupiedProvinces.Contains(p))
                        .Select(p => {
                            float suitability = provinceScores.ContainsKey(p) ? provinceScores[p] : -9999f;
                            if (suitability > -9999f && faction.def.techLevel < TechLevel.Industrial)
                            {
                                suitability += GetTribalBetweennessBonus(p, placedBases, worldGrid);
                            }

                            if (suitability <= -9999f) return new { Province = p, Score = -9999f, BarrierCount = 0, ClaimRaw = -9999 };

                            int sharedBorders = 0, rivalClaimNeighbours = 0;
                            if (p.borderShares != null)
                            {
                                foreach (int nid in p.borderShares.Keys)
                                {
                                    if (factionProvinceIds.Contains(nid)) sharedBorders++;
                                    if (rivalClaimedProvinceIds.Contains(nid)) rivalClaimNeighbours++;
                                }
                            }
                            bool rivalClaimsSelf = rivalClaimedProvinceIds.Contains(p.id);

                            // #65 claim signal: reward extending the faction's own HELD territory (shared
                            // borders), be averse to rival claims — a settlement ringed by other nations is
                            // not a border a 5500-year-old world would draw.
                            int claimRaw = sharedBorders - rivalClaimNeighbours - (rivalClaimsSelf ? 2 : 0);
                            int barrierCount = barrierCountByProvince.TryGetValue(p.id, out var bc) ? bc : 0;

                            return new { Province = p, Score = suitability, BarrierCount = barrierCount, ClaimRaw = claimRaw };
                        })
                        .Where(x => x.Score > -9999f);

                    // #65: choose by a 70% territorial-claim / 30% resource decision — the highest total
                    // wins. Claim rewards growing the faction's own HELD ground (shared borders with its
                    // provinces) and is averse to rival claims (rival-claimed neighbours, and worse, a
                    // rival claim on the province itself); resources keep settlements on liveable land.
                    // Both are min-max normalized over the candidate set so the 70/30 blend is meaningful.
                    // A first settlement (no held ground, so claim is pure rival-aversion) lands on good
                    // land away from established nations; later ones grow the nation contiguously along its
                    // own frontier. Natural barriers break ties, for cleaner, more stable borders — the
                    // world is meant to read as ~5500 years of settled history, not a fresh scatter.
                    var candidatesList = allCandidates.ToList();

                    if (candidatesList.Any())
                    {
                        float minRes = candidatesList.Min(x => x.Score);
                        float maxRes = candidatesList.Max(x => x.Score);
                        int minClaim = candidatesList.Min(x => x.ClaimRaw);
                        int maxClaim = candidatesList.Max(x => x.ClaimRaw);

                        float Norm(float v, float lo, float hi) => hi > lo ? (v - lo) / (hi - lo) : 1f;

                        chosenProvince = candidatesList
                            .OrderByDescending(x => 0.70f * Norm(x.ClaimRaw, minClaim, maxClaim) + 0.30f * Norm(x.Score, minRes, maxRes))
                            .ThenByDescending(x => x.BarrierCount)
                            .First().Province;
                    }

                    if (chosenProvince != null)
                    {
                        int chosenTile = FindBestTileInProvince(chosenProvince, factionBases, placedBases, tileScores, worldGrid);

                        if (chosenTile != -1)
                        {
                            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                            settlement.Tile = chosenTile;
                            settlement.SetFaction(faction);
                            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
                            worldObjects.Add(settlement);

                            // Mark the placement order for this settlement
                            regionManager.SetSettlementPlacementOrder(chosenTile, b + 1);

                            factionBases.Add(chosenTile);
                            placedBases.Add(chosenTile);

                            if (!factionProvinces.Contains(chosenProvince))
                            {
                                factionProvinces.Add(chosenProvince);
                                if (!chosenProvince.owningFactionIds.Contains(factionId))
                                {
                                    chosenProvince.owningFactionIds.Add(factionId);
                                }
                            }
                            occupiedProvinces.Add(chosenProvince);
                        }
                    }
                }

                Log.Message($"[RegionsAndSocieties] Placed {factionBases.Count} bases across {factionProvinces.Count} provinces for faction: {faction.Name}");
            }

            // Redistribute NPC faction colors deterministically to ensure high vibrance and distinct visual separation
            var assignableFactions = factionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.def.hidden && f.def.defName != "Empire")
                .ToList();

            if (assignableFactions.Any())
            {
                System.Random colorRand = new System.Random(Find.World.info.Seed);
                var shuffled = assignableFactions.OrderBy(x => colorRand.Next()).ToList();
                for (int i = 0; i < shuffled.Count; i++)
                {
                    float hue = (float)i / shuffled.Count;
                    Color uniqueColor = Color.HSVToRGB(hue, 0.60f, 0.90f);
                    shuffled[i].color = uniqueColor;
                }
            }

            RoadGeneratorHelper.GenerateRoadsBetweenBases();

            // Refresh the population density cache since new settlements have been placed
            PopulationDensityUtility.MarkCacheDirty();

            // 0.8: seed outposts around each settlement up to its tier-based allowance. Runs here,
            // after settlements are placed and provinces are owned, and only on the R&T placement
            // path (this prefix), so it never fires for a non-surface layer or a vanilla fallback.
            OutpostSeedingResult seeding = OutpostSeedingUtility.SeedOutposts();
            Log.Message("[RegionsAndSocieties] " + seeding.ToReport().TrimEnd());

            Log.Message("[RegionsAndSocieties] Custom Faction Generation and Placement completed successfully.");
            return false;
        }

        private static int FindBestTileInProvince(GeographicProvince province, List<int> sameFactionBases, List<int> allPlacedBases, Dictionary<int, float> tileScores, WorldGrid worldGrid)
        {
            var candidateTiles = province.tiles
                .Where(t => tileScores.ContainsKey(t) && tileScores[t] > -9999f && !allPlacedBases.Contains(t))
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

                float pop = FactionPlacementUtility.EvaluatePopulationRetention(t, provinceTiles);
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
                    if (dist < 8f)
                    {
                        tooCloseToRival = true;
                        break;
                    }
                }
                if (!tooCloseToRival) return tile;
            }

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

        private static bool IsProvinceAdjacentToAny(GeographicProvince p, List<GeographicProvince> existing, SynapseRegionManager manager, WorldGrid worldGrid)
        {
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborProvinceId = manager.GetProvinceId(n.tileId);
                    if (neighborProvinceId != -1 && existing.Any(ep => ep.id == neighborProvinceId))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static float GetProvinceDistance(GeographicProvince p1, GeographicProvince p2, WorldGrid worldGrid)
        {
            if (p1.tiles.Count == 0 || p2.tiles.Count == 0) return 9999f;
            return worldGrid.ApproxDistanceInTiles(p1.tiles[0], p2.tiles[0]);
        }

        private static int GetCategoryPriority(Faction faction)
        {
            var profile = FactionPlacementSettings.GetProfile(faction.def);
            if (profile != null)
            {
                return profile.placementOrder;
            }
            if (faction.def.defName == "Empire") return 2;
            if (faction.def.techLevel == TechLevel.Industrial) return 1;
            if (faction.def.techLevel >= TechLevel.Spacer) return 3;
            return 4; // Tribal
        }

        private static int GetSharedBorderCount(GeographicProvince p, List<GeographicProvince> existing, SynapseRegionManager manager, WorldGrid worldGrid)
        {
            HashSet<int> sharedAdjacentProvinces = new HashSet<int>();
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborProvinceId = manager.GetProvinceId(n.tileId);
                    if (neighborProvinceId != -1 && neighborProvinceId != p.id)
                    {
                        var matchingProv = existing.FirstOrDefault(ep => ep.id == neighborProvinceId);
                        if (matchingProv != null)
                        {
                            sharedAdjacentProvinces.Add(matchingProv.id);
                        }
                    }
                }
            }
            return sharedAdjacentProvinces.Count;
        }

        // #65: distinct neighbour provinces where some RIVAL (not this faction) already holds at least a
        // legitimate claim (>=30%). Reads the ownership refreshed once per faction; the aversion term in
        // the territorial-claim score, so a faction does not grow into ground other nations already claim.
        private static int CountRivalClaimNeighbours(GeographicProvince p, Faction faction, SynapseRegionManager manager, WorldGrid worldGrid)
        {
            HashSet<int> rivalClaimed = new HashSet<int>();
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int nid = manager.GetProvinceId(n.tileId);
                    if (nid == -1 || nid == p.id || rivalClaimed.Contains(nid)) continue;
                    if (RivalClaimsProvince(manager.GetProvinceForTile(n.tileId), faction)) rivalClaimed.Add(nid);
                }
            }
            return rivalClaimed.Count;
        }

        /// <summary>True when some faction other than <paramref name="faction"/> already holds at least a
        /// legitimate claim (&gt;=30%) on the province, per its refreshed ownership data (#65).</summary>
        private static bool RivalClaimsProvince(GeographicProvince province, Faction faction)
        {
            var scores = province?.ownershipData?.factionScores;
            if (scores == null) return false;
            foreach (var s in scores)
            {
                if (s != null && s.faction != null && s.faction != faction &&
                    s.TotalScore >= RegionsAndSocieties.Placement.PlacementRules.OwnershipThreshold)
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetBarrierBorderCount(GeographicProvince p, WorldGrid worldGrid)
        {
            int barrierCount = 0;
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    Tile nTile = worldGrid[n.tileId];
                    if (nTile.hilliness == Hilliness.Impassable || nTile.WaterCovered || (nTile.PrimaryBiome != null && nTile.PrimaryBiome.impassable))
                    {
                        barrierCount++;
                    }
                }
            }
            return barrierCount;
        }

        private static float GetFactionRng(Faction faction)
        {
            if (faction.def.defName.ToLower().Contains("pirate") || faction.def.label.ToLower().Contains("pirate"))
            {
                return UnityEngine.Random.Range(1f, 3f);
            }
            if (faction.def.techLevel == TechLevel.Industrial)
            {
                return UnityEngine.Random.Range(1f, 5f);
            }
            if (faction.def.techLevel >= TechLevel.Spacer)
            {
                return UnityEngine.Random.Range(1f, 2f);
            }
            return UnityEngine.Random.Range(1f, 2f); // Tribal / default
        }

        private static float GetTribalBetweennessBonus(GeographicProvince p, List<int> allPlacedBases, WorldGrid worldGrid)
        {
            if (p.tiles.Count == 0 || !allPlacedBases.Any()) return 0f;

            // Find all placed bases that belong to Industrial factions
            Dictionary<string, List<int>> industrialBasesByFaction = new Dictionary<string, List<int>>();
            foreach (int tile in allPlacedBases)
            {
                var settlement = Find.WorldObjects.Settlements.FirstOrDefault(s => s.Tile == tile);
                if (settlement != null && settlement.Faction != null && settlement.Faction.def.techLevel == TechLevel.Industrial)
                {
                    string fId = settlement.Faction.GetUniqueLoadID();
                    if (!industrialBasesByFaction.ContainsKey(fId))
                    {
                        industrialBasesByFaction[fId] = new List<int>();
                    }
                    industrialBasesByFaction[fId].Add(tile);
                }
            }

            if (industrialBasesByFaction.Count < 2) return 0f;

            // Calculate min distance to each industrial faction
            List<float> minDists = new List<float>();
            int tileCenter = p.tiles[0];

            foreach (var kvp in industrialBasesByFaction)
            {
                float minDist = 9999f;
                foreach (int baseTile in kvp.Value)
                {
                    float dist = worldGrid.ApproxDistanceInTiles(tileCenter, baseTile);
                    if (dist < minDist) minDist = dist;
                }
                minDists.Add(minDist);
            }

            minDists.Sort();

            float minDistF1 = minDists[0];
            float minDistF2 = minDists[1];

            // If both are within 30 tiles, calculate betweenness
            if (minDistF1 < 30f && minDistF2 < 30f)
            {
                return 50f / (minDistF1 + minDistF2);
            }

            return 0f;
        }
    }

    // Tracing-only patches. They change no behaviour and exist purely to show when world
    // generation reaches these steps, so they stay silent outside dev mode: dumping a full
    // StackTrace on every world generation is expensive, and the resulting "at ..." frames
    // are indistinguishable from a real exception when reading Player.log.
    [HarmonyPatch(typeof(WorldGenerator), "GenerateWorld")]
    public static class Patch_WorldGenerator_GenerateWorld
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (!Prefs.DevMode) return;
            Log.Message("[RegionsAndSocieties] WorldGenerator.GenerateWorld prefix reached.");
        }
    }

    [HarmonyPatch(typeof(WorldGenStep_Factions), "GenerateFresh")]
    public static class Patch_WorldGenStep_Factions_GenerateFresh
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (!Prefs.DevMode) return;
            Log.Message("[RegionsAndSocieties] WorldGenStep_Factions.GenerateFresh prefix reached.\n"
                + new System.Diagnostics.StackTrace());
        }
    }

    [HarmonyPatch(typeof(WorldObjectsHolder), "Add")]
    public static class Patch_WorldObjectsHolder_Add
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject o)
        {
            // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
            if (Integration.WorldObjectClassifier.IsSettlement(o))
            {
                if (o.Faction != null)
                {
                    World world = Find.World;
                    if (world != null)
                    {
                        var regionManager = world.GetComponent<SynapseRegionManager>();
                        if (regionManager != null)
                        {
                            // Only set if not already set (to preserve initial generation indices)
                            if (regionManager.GetSettlementPlacementOrder(o.Tile) == -1)
                            {
                                int nextOrder = regionManager.GetNextPlacementOrderForFaction(o.Faction);
                                regionManager.SetSettlementPlacementOrder(o.Tile, nextOrder);
                            }
                        }
                    }
                }
            }

            // #66: a player settlement in a rival-claimed province raises the anger-on-claim hook and,
            // if unconsumed, applies the default goodwill penalty. Self-guards to player settlements, so
            // this is a no-op for NPC/worldgen adds.
            Integration.TerritoryClaimHooks.OnPermanentHoldingPlaced(o);
        }
    }
}
