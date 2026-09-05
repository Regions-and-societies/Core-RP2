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
        // Counts factions added to the FactionManager by the current run, so a throw can tell whether
        // vanilla may still generate factions (none added) or only settlements are missing (some added).
        private static int factionsAddedThisRun;

        [HarmonyPrefix]
        public static bool Prefix(PlanetLayer layer, List<FactionDef> factions)
        {
            factionsAddedThisRun = 0;
            try
            {
                return GenerateAndPlace(layer, factions);
            }
            catch (Exception ex)
            {
                // Vanilla wraps each WorldGenStep in try/catch and only LOGS a throw — the step's work is
                // lost and generation carries on. Because this prefix replaces the whole faction step, a
                // throw anywhere in it used to yield a world with no factions at all (0.3.0: a malformed
                // BiomeDef's plant cache threw inside the region partition). Degrade instead of dying.
                if (factionsAddedThisRun == 0)
                {
                    Log.Error("[RegionsAndSocieties] Custom faction generation threw before any faction was created; falling back to vanilla faction generation for this world. R&S regions are rebuilt lazily when first needed.\n" + ex);
                    (Find.World ?? Current.CreatingWorld)?.GetComponent<SynapseRegionManager>()?.ResetProvinces();
                    return true;
                }
                Log.Error($"[RegionsAndSocieties] Custom settlement placement threw after {factionsAddedThisRun} faction(s) were created; giving every faction without a base one settlement the vanilla way so the world stays playable.\n" + ex);
                PlaceFallbackSettlements(layer);
                return false;
            }
        }

        private static bool GenerateAndPlace(PlanetLayer layer, List<FactionDef> factions)
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
                // No selection was handed in (a non-standard world-gen entry). Rebuild the list the way
                // vanilla world creation would — honoring each def's start counts — instead of dragging in
                // EVERY non-hidden faction. A def the player would never receive at world creation
                // (startingCountAtWorldCreation 0 and no required count) is left out; required factions get
                // at least their mandated count. This keeps unselected/zero-count defs out of the world.
                factions = new List<FactionDef>();
                foreach (var def in DefDatabase<FactionDef>.AllDefsListForReading)
                {
                    if (def.isPlayer || def.hidden || def.defName == "PColony") continue;
                    int count = Mathf.Max(def.startingCountAtWorldCreation, def.requiredCountAtGameStart);
                    for (int i = 0; i < count; i++) factions.Add(def);
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

            List<FactionDef> finalDefs = new List<FactionDef>();
            foreach (var def in factions)
            {
                if (canExistOnLayerMethod == null || (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, def }))
                {
                    finalDefs.Add(def);
                }
            }

            // The top-up pool is the player's SELECTED faction types only (deduped) — never the global
            // DefDatabase. The top-up clones extra settlements of factions the world already contains to
            // reach the target count; it must not invent factions the player never picked. The old code
            // cloned RandomElement() from every non-hidden def, spawning unselected/unfinished factions
            // (e.g. an incomplete Maru Race faction) into worlds that never chose them.
            List<FactionDef> poolToClone = finalDefs
                .Where(f => !f.isPlayer && !f.hidden && f.defName != "PColony")
                .Distinct()
                .ToList();

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
                    Faction faction = TryGenerateFaction(layer, def);
                    if (faction != null)
                    {
                        factionManager.Add(faction);
                        factionsAddedThisRun++;
                        generatedFactions.Add(faction);
                    }
                }

                foreach (FactionDef def in DefDatabase<FactionDef>.AllDefs)
                {
                    if (def.hidden && factionManager.FirstFactionOfDef(def) == null)
                    {
                        Faction faction = TryGenerateFaction(layer, def);
                        if (faction != null)
                        {
                            factionManager.Add(faction);
                            factionsAddedThisRun++;
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
            // The player must always have somewhere to land: reserve at least one settleable land
            // province (>=20 tiles, the same floor the province scorer applies) that NPC placement may
            // never claim. At small worlds / low coverage the faction-count floor below could otherwise
            // occupy every region, and the starting-site chooser errors out with no valid tile
            // ("Failed to find faction base tile for PlayerColony").
            int settleableLandProvinces = allProvinces.Count(p =>
                p.provinceType == ProvinceType.Land && p.tiles != null && p.tiles.Count >= 20);
            int totalBasesAfterThreat = factionTargetBases.Values.Sum();
            int maxBasesAllowed = Mathf.Max(allNPCFactions.Count, Mathf.RoundToInt(landProvinceCount * FactionPlacementSettings.claimedLandAreaPercent));
            maxBasesAllowed = Mathf.Min(maxBasesAllowed, Mathf.Max(0, settleableLandProvinces - PlayerReserveProvinces));

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

            // Worldgen perf: per-tile terrain features do not depend on the faction, so read the world
            // grid ONCE into compact arrays over the settleable-terrain tiles. The old code rebuilt a
            // full Dictionary<int,float> from fresh grid reads for EVERY faction — O(factions × tiles)
            // tile-object walks plus dictionary/GC churn — which made large RP2 planets (high planet
            // scale, 100% coverage) crawl through worldgen. Per faction the score is now just a
            // weighted sum over these arrays; only the temperature gate is per-faction.
            var validTileIds = new List<int>();
            var tileFeatures = new List<TileFeatures>();
            for (int t = 0; t < totalTiles; t++)
            {
                Tile tileData = worldGrid[t];
                if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable ||
                    (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")))
                {
                    continue;
                }

                float mineralVal = 0.5f;
                if (tileData.hilliness == Hilliness.SmallHills) mineralVal = 1.0f;
                else if (tileData.hilliness == Hilliness.LargeHills) mineralVal = 2.0f;
                else if (tileData.hilliness == Hilliness.Mountainous) mineralVal = 3.0f;

                float nutritionVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.plantDensity : 0.5f;
                float forageVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.forageability : 0.5f;
                float biomassVal = tileData.PrimaryBiome != null ? BiomeSafe.TreeDensity(tileData.PrimaryBiome) : 0.5f;
                float grazingVal = (tileData.hilliness == Hilliness.Flat) ? nutritionVal * 2f : nutritionVal;
                float hospVal = nutritionVal * 2f + forageVal;

                validTileIds.Add(t);
                tileFeatures.Add(new TileFeatures
                {
                    Mineral = mineralVal,
                    Nutrition = nutritionVal,
                    Forage = forageVal,
                    Grazing = grazingVal,
                    Biomass = biomassVal,
                    Margin = Mathf.Max(0f, 3.0f - hospVal),
                    Temperature = tileData.temperature,
                });
            }

            // One score array reused across factions; -9999 marks unsettleable-for-this-faction.
            float[] tileScores = new float[totalTiles];

            foreach (var faction in alternatingFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(faction.def);
                if (profile == null) continue;

                int baseCount = factionTargetBases.ContainsKey(faction) ? factionTargetBases[faction] : 5;

                for (int t = 0; t < totalTiles; t++)
                {
                    tileScores[t] = -9999f;
                }
                for (int i = 0; i < validTileIds.Count; i++)
                {
                    TileFeatures f = tileFeatures[i];
                    if (!faction.def.allowedArrivalTemperatureRange.Includes(f.Temperature))
                    {
                        continue;
                    }

                    float score = 0f;
                    score += profile.mineralWeight * f.Mineral;
                    score += profile.nutritionWeight * f.Nutrition;
                    score += profile.forageWeight * f.Forage;
                    score += profile.grazingWeight * f.Grazing;
                    score += profile.huntingWeight * f.Biomass;

                    if (profile.marginWeight > 0f)
                    {
                        score += profile.marginWeight * f.Margin;
                    }

                    tileScores[validTileIds[i]] = score;
                }

                Dictionary<GeographicProvince, float> provinceScores = new Dictionary<GeographicProvince, float>();
                foreach (var p in allProvinces)
                {
                    // Water is never a settlement candidate — skip the ~50k-tile ocean province instead
                    // of running the per-tile LINQ over it once per faction just to score it -9999 (#20).
                    if (p.provinceType != ProvinceType.Land)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }
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

                    var validTiles = p.tiles.Where(t => tileScores[t] > -9999f).ToList();
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

                // Betweenness inputs are static during THIS faction's placement: industrial bases only
                // change when an industrial faction places, and the bonus only applies to sub-industrial
                // factions — so build the industrial-base map ONCE per faction. The old code rebuilt it
                // per candidate province per base, with a linear settlement scan per placed base inside;
                // that term grew with the square of settlement count and dominated worldgen at high
                // density settings.
                Dictionary<string, List<int>> tribalIndustrialBases =
                    faction.def.techLevel < TechLevel.Industrial ? BuildIndustrialBasesByFaction(placedBases) : null;

                for (int b = 0; b < baseCount; b++)
                {
                    // Hard guarantee behind the reserve in maxBasesAllowed: the per-faction floors
                    // (every faction keeps at least one base) can override the normalized cap, so also
                    // stop placing outright once only the player's reserve remains unclaimed.
                    if (settleableLandProvinces - occupiedProvinces.Count <= PlayerReserveProvinces)
                    {
                        break;
                    }

                    GeographicProvince chosenProvince = null;
                    string factionId = faction.GetUniqueLoadID();
                    var factionProvinceIds = new HashSet<int>(factionProvinces.Select(fp => fp.id));

                    // Claim/resource inputs for every unoccupied candidate. All O(neighbours) per candidate
                    // via borderShares (precomputed neighbour province ids) and the static barrier cache.
                    var allCandidates = allProvinces
                        .Where(p => !occupiedProvinces.Contains(p))
                        .Select(p => {
                            float suitability = provinceScores.ContainsKey(p) ? provinceScores[p] : -9999f;
                            if (suitability > -9999f && tribalIndustrialBases != null)
                            {
                                suitability += GetTribalBetweennessBonus(p, tribalIndustrialBases, worldGrid);
                            }

                            if (suitability <= -9999f) return new { Province = p, Score = -9999f, BarrierCount = 0, ClaimRaw = -9999, Embeddedness = 0f };

                            int sharedBorders = 0, rivalClaimNeighbours = 0, claimableBorders = 0;
                            if (p.borderShares != null)
                            {
                                foreach (int nid in p.borderShares.Keys)
                                {
                                    // Land-type neighbours are the borders anyone could ever claim (#19);
                                    // ocean/range neighbours are geography's free wall and stay out of the
                                    // embeddedness denominator. Still O(neighbours) — no tile walks (#65).
                                    var np = regionManager.GetProvince(nid);
                                    if (np != null && np.provinceType == ProvinceType.Land) claimableBorders++;
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
                            float embeddedness = Placement.CompactnessRules.Embeddedness(sharedBorders, claimableBorders);

                            return new { Province = p, Score = suitability, BarrierCount = barrierCount, ClaimRaw = claimRaw, Embeddedness = embeddedness };
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

                        // #19: bend the blended score by shape — a candidate below the desired
                        // embeddedness ratio keeps only a fraction of its score, so growth fills the
                        // domain's pockets before extending tendrils. Only once the faction holds ground:
                        // a first foothold has nothing to square against (and must not hand islands, whose
                        // coastline is all free wall, an unearned full score). Weight 0 = the pure #65 blend.
                        bool hasGround = factionProvinceIds.Count > 0;
                        chosenProvince = candidatesList
                            .OrderByDescending(x => {
                                float blended = 0.70f * Norm(x.ClaimRaw, minClaim, maxClaim) + 0.30f * Norm(x.Score, minRes, maxRes);
                                return hasGround
                                    ? Placement.CompactnessRules.EffectiveScore(blended, x.Embeddedness,
                                        Placement.CompactnessRules.DefaultDesiredRatio, FactionPlacementSettings.territoryCompactness)
                                    : blended;
                            })
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

            // R&S settlement road-linking (RoadGeneratorHelper) is deferred to 0.4.0, where it will be
            // reworked (vanilla pathfinder, possibly incremental). Vanilla's own road step still runs.
            // The helper and its bounded search stay in the tree; nothing calls them at worldgen (#38).

            // Refresh the population density cache since new settlements have been placed
            PopulationDensityUtility.MarkCacheDirty();

            // Outpost seeding at worldgen (#18) is deferred to 0.4.0. The seeder evaluates every candidate
            // tile of every anchored province through the full placement rule chain, and each created
            // outpost invalidates the placement snapshot and the per-faction ownership walks behind it,
            // so at 0.3.0's finer partition (2,400+ provinces at 100% coverage) the step ran for tens of
            // minutes (#38). It comes back once the seeder batches its own snapshot. The utility and its
            // debug preview stay; nothing calls SeedOutposts during world generation.

            // Log the world's reproduction key + region-shape audit at generation, so any "region N is a
            // horrid shape" report can be reproduced exactly (the partition is deterministic from the
            // seed + settings) and the worst-shaped regions are already flagged for #20 tuning.
            var swShape = System.Diagnostics.Stopwatch.StartNew();
            string shapeReport = Integration.RegionDebugReports.WorldShapeReport();
            swShape.Stop();
            Log.Message("[RegionsAndSocieties] " + shapeReport + $"\n(shape report {swShape.ElapsedMilliseconds} ms)");

            Log.Message("[RegionsAndSocieties] Custom Faction Generation and Placement completed successfully.");
            return false;
        }

        /// <summary>
        /// Last-resort placement when the R&amp;S placement solver throws after the factions already
        /// exist: vanilla's own generator cannot be re-run (it would create the factions again), so give
        /// every visible NPC faction that ended up without a base a single vanilla-style settlement.
        /// </summary>
        private static void PlaceFallbackSettlements(PlanetLayer layer)
        {
            World world = Find.World ?? Current.CreatingWorld;
            FactionManager factionManager = world?.factionManager;
            if (factionManager == null || world.worldObjects == null || layer?.Def == null) return;

            var canExistOnLayerMethod = typeof(FactionGenerator).GetMethod("CanExistOnLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            WorldObjectDef settlementDef = layer.Def.SettlementWorldObjectDef ?? WorldObjectDefOf.Settlement;
            int placed = 0;
            foreach (Faction faction in factionManager.AllFactionsListForReading)
            {
                if (faction == null || faction.IsPlayer || faction.Hidden || faction.temporary) continue;
                if (canExistOnLayerMethod != null && !(bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, faction.def })) continue;
                if (world.worldObjects.Settlements.Any(s => s.Faction == faction)) continue;
                try
                {
                    Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(settlementDef);
                    settlement.SetFaction(faction);
                    settlement.Tile = TileFinder.RandomSettlementTileFor(layer, faction);
                    settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
                    world.worldObjects.Add(settlement);
                    placed++;
                }
                catch (Exception e)
                {
                    Log.Warning($"[RegionsAndSocieties] Fallback placement for '{faction.Name}' failed: {e.Message}");
                }
            }
            Log.Message($"[RegionsAndSocieties] Fallback placement added {placed} settlement(s).");
        }

        /// <summary>
        /// Generate one faction the way vanilla world-gen does — carrying the <see cref="FactionDef"/>
        /// through as the ideo's <c>forFaction</c> context — and never let a single faction's failure
        /// abort the whole world-generation step.
        ///
        /// <para>The old code passed <c>default(IdeoGenerationParms)</c>, i.e. <c>forFaction = null</c>,
        /// stripping the faction/culture context that classic (no-expansion) ideoligion role-name
        /// generation relies on. Under the wrong Ideology mode a null there throws deep in
        /// <c>Precept_Role.GenerateNameRaw</c>; because this replaces vanilla's
        /// <c>GenerateFactionsIntoWorldLayer</c> entirely (the prefix returns false), an uncaught throw
        /// kills the <c>WorldGenStep</c> and leaves the player staring at an unrendered ("black") world.
        /// Building proper parms addresses the root; the try/catch is defence in depth so any future
        /// faction-gen throw degrades to a skipped, logged faction rather than a dead world.</para>
        /// </summary>
        private static bool loggedSkipDetail;

        private static Faction TryGenerateFaction(PlanetLayer layer, FactionDef def)
        {
            // Graceful DLC degradation: a faction whose def carries royal-title content cannot
            // generate its leader when Royalty isn't resolved (the title pipeline hands
            // PawnGenerator a null psylink HediffDef and it NREs) — pre-skip it cleanly instead of
            // throwing into the catch below. On a healthy install the def and its DLC load or
            // unload together, so this only fires in the half-resolved states (a missing DLC plus
            // mods referencing its content) that black-worlded 0.2.1. The world simply generates
            // without that faction, which is the honest degradation.
            if (!ModsConfig.RoyaltyActive && def?.royalTitleTags != null && def.royalTitleTags.Count > 0)
            {
                Log.Message($"[RegionsAndSocieties] Skipped faction '{def.defName}' — it needs Royalty title content that isn't resolved (DLC inactive). The world generates without it.");
                return null;
            }

            try
            {
                // Mirror vanilla's own generation call as closely as possible so classic (no-expansion)
                // ideoligion role-name generation resolves with the context it expects:
                //   • forFaction = def gives ideo/culture selection the faction context (vanilla always
                //     carries it; passing default(IdeoGenerationParms) left it null);
                //   • the PLANET-LAYER overload matches vanilla's InitializeFactions path exactly — we
                //     were calling the layer-less NewGeneratedFaction(parms), which generates the faction
                //     (and its ideo) without the world-layer context the layered 1.6 path sets up.
                var ideoParms = new IdeoGenerationParms { forFaction = def };
                // hidden: true is deliberate — the third FactionGeneratorParms arg is `bool? hidden`, and
                // NewGeneratedFaction only spawns its OWN settlement (which R&S must place itself) when the
                // faction is NOT hidden. We generate hidden so vanilla skips that spawn (the caller's
                // settlementWorldObjectDef null-out is the same guard), THEN restore the def's real
                // visibility below. Without the restore, every ordinary faction stayed hidden — off the
                // Factions tab, no goodwill, no leader (Faction.Hidden => hidden ?? def.hidden).
                var faction = FactionGenerator.NewGeneratedFaction(layer, new FactionGeneratorParms(def, ideoParms, true));
                if (faction != null)
                {
                    faction.hidden = def.hidden;   // intentionally-hidden defs (Ancients, mechanoids) stay hidden
                    if (!faction.Hidden && faction.leader == null)
                    {
                        // Leader generation was skipped while the faction was hidden; do it now.
                        faction.TryGenerateNewLeader();
                    }
                }
                return faction;
            }
            catch (Exception ex)
            {
                // First skip carries the full stack for diagnosis; later ones collapse to one line
                // so a world with several unresolvable factions logs a readable list, not a wall.
                if (!loggedSkipDetail)
                {
                    loggedSkipDetail = true;
                    Log.Warning($"[RegionsAndSocieties] Skipped faction '{def?.defName ?? "null"}' — generation threw and would otherwise abort world generation: {ex}");
                }
                else
                {
                    Log.Warning($"[RegionsAndSocieties] Skipped faction '{def?.defName ?? "null"}' — generation threw ({ex.GetType().Name}: {ex.Message}); full stack on the first skip above.");
                }
                return null;
            }
        }

        private static int FindBestTileInProvince(GeographicProvince province, List<int> sameFactionBases, List<int> allPlacedBases, float[] tileScores, WorldGrid worldGrid)
        {
            var placedSet = new HashSet<int>(allPlacedBases);
            var candidateTiles = province.tiles
                .Where(t => tileScores[t] > -9999f && !placedSet.Contains(t))
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

        /// <summary>Faction-independent terrain features of one settleable tile, read from the world
        /// grid once per worldgen so the per-faction scoring pass never re-walks tile objects.</summary>
        private struct TileFeatures
        {
            public float Mineral;
            public float Nutrition;
            public float Forage;
            public float Grazing;
            public float Biomass;
            public float Margin;       // Mathf.Max(0, 3 - hospitability), the marginal-land preference input
            public float Temperature;
        }

        /// <summary>Settleable land provinces NPC placement must always leave unclaimed so the player
        /// has somewhere to land.</summary>
        private const int PlayerReserveProvinces = 1;

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

        /// <summary>The placed NPC bases that belong to Industrial factions, keyed by faction, for the
        /// tribal betweenness bonus. One settlement pass with a placed-tile set — built once per faction
        /// (the industrial set cannot change while a sub-industrial faction places its own bases).</summary>
        private static Dictionary<string, List<int>> BuildIndustrialBasesByFaction(List<int> allPlacedBases)
        {
            var result = new Dictionary<string, List<int>>();
            if (allPlacedBases.Count == 0) return result;

            var placed = new HashSet<int>(allPlacedBases);
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction == null || s.Faction.def.techLevel != TechLevel.Industrial) continue;
                int tile = s.Tile.tileId;
                if (!placed.Contains(tile)) continue;

                string fId = s.Faction.GetUniqueLoadID();
                if (!result.TryGetValue(fId, out List<int> list))
                {
                    result[fId] = list = new List<int>();
                }
                list.Add(tile);
            }
            return result;
        }

        private static float GetTribalBetweennessBonus(GeographicProvince p, Dictionary<string, List<int>> industrialBasesByFaction, WorldGrid worldGrid)
        {
            if (p.tiles.Count == 0 || industrialBasesByFaction == null || industrialBasesByFaction.Count < 2) return 0f;

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
        // Not tracing-only any more (#38): the prefix/postfix pair also times the whole of world
        // generation, logged as one line so a perf report can quote it, and honours the dev-only
        // quicktest coverage override that the worldgen perf matrix is generated with.
        private static System.Diagnostics.Stopwatch worldgenTimer;

        [HarmonyPrefix]
        public static void Prefix(ref float planetCoverage, ref string seedString)
        {
            if (GenCommandLine.CommandLineArgPassed("quicktest"))
            {
                float devCoverage = FactionPlacementSettings.devQuicktestCoverage;
                if (devCoverage > 0f)
                {
                    float clamped = Mathf.Clamp(devCoverage, 0.05f, 1f);
                    Log.Message($"[RegionsAndSocieties] DEV: quicktest planet coverage overridden {planetCoverage:P0} -> {clamped:P0} (devQuicktestCoverage).");
                    planetCoverage = clamped;
                }
                string devSeed = FactionPlacementSettings.devQuicktestSeed;
                if (!string.IsNullOrEmpty(devSeed))
                {
                    Log.Message($"[RegionsAndSocieties] DEV: quicktest world seed overridden '{seedString}' -> '{devSeed}' (devQuicktestSeed).");
                    seedString = devSeed;
                }
            }

            worldgenTimer = System.Diagnostics.Stopwatch.StartNew();
            if (!Prefs.DevMode) return;
            Log.Message("[RegionsAndSocieties] WorldGenerator.GenerateWorld prefix reached.");
        }

        [HarmonyPostfix]
        public static void Postfix(float planetCoverage, World __result)
        {
            if (worldgenTimer == null) return;
            worldgenTimer.Stop();
            // The generated world is the return value; it is not Find.World yet (the caller assigns it),
            // so read it from __result — Find.WorldGrid here throws and kills the worldgen event.
            int tiles = __result?.grid?.TilesCount ?? 0;
            int settlements = __result?.worldObjects?.Settlements?.Count ?? 0;
            Log.Message($"[RegionsAndSocieties] World generation completed in {worldgenTimer.ElapsedMilliseconds} ms ({planetCoverage:P0} coverage, {tiles} tiles, {settlements} settlements).");
            worldgenTimer = null;
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
