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

            // #47 follow-up: per-faction distribution honours the VALUE MODE. PERCENT (default) makes each
            // faction's stored number a share of the total land regions that self-scales — so a small vanilla
            // world still fills, where a fixed count would leave it sparse — while COUNT keeps it a literal
            // region target. DistributeRegions is the SAME pure arithmetic the placement dialog and its pie
            // chart display, so what the player sees is what worldgen builds. Density (claimed land area) is
            // folded in by DistributeRegions in percent mode; the settleable-land cap below still applies on
            // top. The floor of one base per faction keeps every faction on the map.
            var npcFactionList = allNPCFactions.ToList();
            int landProvinceCount = allProvinces.Count(p => p.provinceType == ProvinceType.Land);

            var shareWeights = new List<float>(npcFactionList.Count);
            foreach (var faction in npcFactionList)
            {
                var shareProfile = FactionPlacementSettings.GetProfile(faction.def);
                float w = (shareProfile != null && shareProfile.placementShare > 0f)
                    ? shareProfile.placementShare
                    : FactionPlacementSettings.DefaultShare(faction.def);
                shareWeights.Add(w);
            }

            // One combined relative-size model (the static/percentage switch was dropped): shares self-scale
            // to the land, normalised, with a guaranteed minimum of one region per faction.
            int[] distributed = Placement.PlacementShareRules.DistributeRegions(
                Placement.PlacementValueMode.Percent, Placement.PlacementPercentBasis.SettledNormalized, shareWeights, landProvinceCount,
                FactionPlacementSettings.claimedLandAreaPercent);

            for (int i = 0; i < npcFactionList.Count; i++)
                factionTargetBases[npcFactionList[i]] = Mathf.Max(1, distributed[i]);   // keep every faction on the map

            // The player must always have somewhere to land: reserve at least one settleable land province
            // (>=20 tiles, the province scorer's floor) that NPC placement may never claim, or the starting
            // -site chooser errors out ("Failed to find faction base tile for PlayerColony"). The map cannot
            // hold more than its settleable land, so if the demand still exceeds the settleable cap after the
            // distribution, scale the whole set down proportionally to fit (largest-remainder, so it sums
            // exactly and no faction is singled out by rounding).
            int settleableLandProvinces = allProvinces.Count(p =>
                p.provinceType == ProvinceType.Land && p.tiles != null && p.tiles.Count >= 20);
            int settleCap = Mathf.Max(0, settleableLandProvinces - PlayerReserveProvinces);
            int totalBasesDemanded = factionTargetBases.Values.Sum();
            if (settleCap > 0 && totalBasesDemanded > settleCap)
            {
                var capWeights = npcFactionList.Select(f => (float)factionTargetBases[f]).ToList();
                int[] capped = Placement.PlacementShareRules.Apportion(capWeights, settleCap);
                for (int i = 0; i < npcFactionList.Count; i++)
                    factionTargetBases[npcFactionList[i]] = capped[i];
                Log.Warning($"[RegionsAndSocieties] Placement demand {totalBasesDemanded} regions exceeds the {settleCap} settleable — scaled down to fit.");
            }

            // Seeding order (#46): the most segmented factions place first — ascending cluster size
            // (3s, then 5s, 7s, then the unbounded) — so a faction that must scatter can still find
            // isolated ground before the map fills. Within one cluster stop the old order stands: sort
            // and interleave NPC factions 1 Industrial, then 1 Tribal, then 1 other.
            // Kin faction count for a faction, from the two clustering knobs and its planned region count.
            int KinCountOf(Faction f)
            {
                var prof = FactionPlacementSettings.GetProfile(f.def);
                if (!FactionPlacementSettings.EffectiveEnableKin(prof, f.def)) return 1;
                int regions = factionTargetBases.TryGetValue(f, out var b) ? b : 0;
                int clusters = FactionPlacementSettings.EffectiveClusterCount(prof, f.def);
                int minSize = prof != null ? prof.clusterSize : 0;
                return Placement.SubFactionRules.PlannedKinCount(regions, clusters, minSize);
            }

            int ClusterOf(Faction f)
            {
                int planned = factionTargetBases.TryGetValue(f, out var b) ? b : 0;
                // Seed by the body-size cap that produces this faction's cluster count — more clusters (smaller
                // bodies) seed first so a fragmented faction finds isolated ground before the map fills.
                int cap = Placement.ClusteringRules.BodyCap(planned, KinCountOf(f));
                return Placement.ClusteringRules.SeedingKey(cap);
            }

            List<Faction> alternatingFactions = new List<Faction>();
            foreach (var clusterGroup in allNPCFactions.GroupBy(ClusterOf).OrderBy(g => g.Key))
            {
                var industrials = clusterGroup
                    .Where(f => f.def.techLevel == TechLevel.Industrial)
                    .OrderBy(f => GetCategoryPriority(f))
                    .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                    .ToList();

                var tribals = clusterGroup
                    .Where(f => f.def.techLevel < TechLevel.Industrial)
                    .OrderBy(f => GetCategoryPriority(f))
                    .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                    .ToList();

                var others = clusterGroup
                    .Where(f => f.def.techLevel > TechLevel.Industrial)
                    .OrderBy(f => GetCategoryPriority(f))
                    .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                    .ToList();

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
            }
            Log.Message("[RegionsAndSocieties] Seeding order (#46, ascending cluster size): "
                + string.Join(", ", alternatingFactions.Select(f => $"{f.Name} [{Placement.ClusteringRules.Label(ClusterOf(f))}]")));

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
            // #56: the land values come from one pure rule (BiomeHabitabilityRules.Features) shared with
            // the runtime placement path, and each tile remembers its biome so the per-faction pass can
            // apply that faction's habitability — vanilla's settlement weight × toil × health, scaled by
            // tech — from a per-biome table instead of re-reading the def per tile.
            var validTileIds = new List<int>();
            var tileFeatures = new List<Placement.PlacementTileFeatures>();
            var tileTemperatures = new List<float>();
            var tileBiomeIndex = new List<int>();
            var biomes = new List<BiomeDef>();
            var biomeIndexOf = new Dictionary<BiomeDef, int>();
            for (int t = 0; t < totalTiles; t++)
            {
                Tile tileData = worldGrid[t];
                if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable ||
                    (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")))
                {
                    continue;
                }

                BiomeDef biome = tileData.PrimaryBiome;
                int biomeIndex = -1;
                if (biome != null && !biomeIndexOf.TryGetValue(biome, out biomeIndex))
                {
                    biomeIndex = biomes.Count;
                    biomes.Add(biome);
                    biomeIndexOf[biome] = biomeIndex;
                }

                validTileIds.Add(t);
                tileFeatures.Add(Placement.BiomeHabitabilityRules.Features(BiomeSafe.Traits(biome), BiomeSafe.HillClass(tileData.hilliness)));
                tileTemperatures.Add(tileData.temperature);
                tileBiomeIndex.Add(biomeIndex);
            }

            // One score array reused across factions; -9999 marks unsettleable-for-this-faction.
            float[] tileScores = new float[totalTiles];

            // #56 crowding: how many settleable provinces each biome offers (static) against how many
            // settlements have landed in it so far (grows as factions place). A biome filling up relative
            // to its size pushes its remaining candidates down, so nations spread over the next-best land
            // instead of every one of them stacking into the single richest biome.
            var availableByBiome = new Dictionary<BiomeDef, int>();
            var settledByBiome = new Dictionary<BiomeDef, int>();
            foreach (var ap in allProvinces)
            {
                if (ap.provinceType != ProvinceType.Land || ap.tiles == null || ap.tiles.Count < 20 || ap.primaryBiome == null) continue;
                availableByBiome.TryGetValue(ap.primaryBiome, out int avail);
                availableByBiome[ap.primaryBiome] = avail + 1;
            }

            foreach (var faction in alternatingFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(faction.def);
                if (profile == null) continue;

                int baseCount = factionTargetBases.ContainsKey(faction) ? factionTargetBases[faction] : 5;

                // Clustering: the faction physically scatters into ~kinCount contiguous bodies, so the body-
                // size cap is ceil(regions / kinCount). kinCount combines the two knobs — number of clusters
                // (equal division) clamped by the minimum cluster size. A candidate that would push a body
                // over the cap ranks behind every candidate that would not, taken only as a last resort.
                int clusterCap = Placement.ClusteringRules.BodyCap(baseCount, KinCountOf(faction));
                var bodies = new Placement.TerritoryBodies();
                int overflowPicks = 0;

                for (int t = 0; t < totalTiles; t++)
                {
                    tileScores[t] = -9999f;
                }
                // #56: this faction's livability per biome — a tribe eats the full toil and disease
                // penalty of a jungle or bog, an industrial society half, a spacer one a quarter.
                int techOrdinal = (int)faction.def.techLevel;
                var weights = new Placement.PlacementWeights(profile.mineralWeight, profile.nutritionWeight, profile.forageWeight, profile.grazingWeight, profile.huntingWeight, profile.marginWeight);
                var habitabilityByBiome = new float[biomes.Count];
                for (int bi = 0; bi < biomes.Count; bi++)
                {
                    habitabilityByBiome[bi] = Placement.BiomeHabitabilityRules.Habitability(BiomeSafe.Traits(biomes[bi]), techOrdinal);
                }

                for (int i = 0; i < validTileIds.Count; i++)
                {
                    if (!faction.def.allowedArrivalTemperatureRange.Includes(tileTemperatures[i]))
                    {
                        continue;
                    }
                    int bi = tileBiomeIndex[i];
                    float habitability = bi >= 0 ? habitabilityByBiome[bi] : 1f;
                    tileScores[validTileIds[i]] = Placement.BiomeHabitabilityRules.Score(tileFeatures[i], habitability, weights);
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
                            if (suitability > -9999f && p.primaryBiome != null)
                            {
                                // #56: push back on a biome that is already filling up relative to its size.
                                settledByBiome.TryGetValue(p.primaryBiome, out int settledHere);
                                availableByBiome.TryGetValue(p.primaryBiome, out int availableHere);
                                suitability *= Placement.BiomeHabitabilityRules.Crowding(settledHere, availableHere);
                            }
                            if (suitability > -9999f && tribalIndustrialBases != null)
                            {
                                suitability += GetTribalBetweennessBonus(p, tribalIndustrialBases, worldGrid);
                            }

                            if (suitability <= -9999f) return new { Province = p, Score = -9999f, BarrierCount = 0, ClaimRaw = -9999, Embeddedness = 0f, PlacementClass = 0 };

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
                            // #46: the body this province would form (itself + every own body it touches),
                            // whether it stays within the cap, and — for a NEW body — how far the nearest
                            // existing body is, so two clusters do not land side by side.
                            int mergedSize = bodies.MergedSizeIfAdded(p.borderShares != null ? p.borderShares.Keys : null);
                            bool withinCap = Placement.ClusteringRules.WithinCap(mergedSize, clusterCap);
                            bool extends = mergedSize > 1;
                            float nearestBody = -1f;
                            if (!extends && factionProvinces.Count > 0 && p.tiles != null && p.tiles.Count > 0)
                            {
                                nearestBody = float.MaxValue;
                                foreach (var own in factionProvinces)
                                {
                                    if (own.tiles == null || own.tiles.Count == 0) continue;
                                    float d = worldGrid.ApproxDistanceInTiles(p.tiles[0], own.tiles[0]);
                                    if (d < nearestBody) nearestBody = d;
                                }
                                if (nearestBody == float.MaxValue) nearestBody = -1f;
                            }
                            int placementClass = Placement.ClusteringRules.PlacementClass(
                                withinCap, extends, Placement.ClusteringRules.FarEnoughForNewBody(nearestBody));

                            return new { Province = p, Score = suitability, BarrierCount = barrierCount, ClaimRaw = claimRaw, Embeddedness = embeddedness, PlacementClass = placementClass };
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
                        // #46: order by placement class first — extend a cluster, else open a well-spaced
                        // new body, else a crowded new body, else overflow — whatever the land is worth;
                        // the score decides within a class.
                        chosenProvince = candidatesList
                            .OrderBy(x => x.PlacementClass)
                            .ThenByDescending(x => {
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
                            if (chosenProvince.primaryBiome != null)
                            {
                                settledByBiome.TryGetValue(chosenProvince.primaryBiome, out int settledHere);
                                settledByBiome[chosenProvince.primaryBiome] = settledHere + 1;   // #56 crowding
                            }
                            // #46: grow the faction's bodies; count the picks where only overflow was left.
                            var chosenNeighbours = chosenProvince.borderShares != null ? chosenProvince.borderShares.Keys : null;
                            if (!Placement.ClusteringRules.WithinCap(bodies.MergedSizeIfAdded(chosenNeighbours), clusterCap)) overflowPicks++;
                            bodies.Add(chosenProvince.id, chosenNeighbours);
                        }
                    }
                }

                Log.Message($"[RegionsAndSocieties] Placed {factionBases.Count} bases across {factionProvinces.Count} provinces for faction: {faction.Name}"
                    + $"  (cluster cap {Placement.ClusteringRules.Label(clusterCap)}: {bodies.Count} bodies, largest {bodies.Largest}, overflow picks {overflowPicks})");
            }

            // Redistribute NPC faction colors deterministically to ensure high vibrance and distinct visual separation
            // #57: split a scattered low-tech faction (tribe / rough union) into loosely-related kin
            // sub-factions by region — the tribe that once spanned the map, carved into a north and a
            // south tribe. Runs before the colour pass so each new faction gets its own map colour.
            SplitFactionsIntoSubFactions(layer, factionManager, regionManager, worldObjects, worldGrid);

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

            // Holding seeding at worldgen (#18): seed each anchored province's outposts/camps/military up to
            // its per-kind allowance × world maturity. The seeder now holds ONE placement snapshot for the
            // whole pass and appends placed holdings to it, so it no longer pays the per-object ownership
            // re-walk that deferred this at 0.3.0 (#38). A no-op with no compatibility creator installed
            // (vanilla), so it costs nothing on a base-game world; gated further by the enable + maturity.
            HoldingSeedingResult seeding = HoldingSeedingUtility.SeedHoldings();
            if (seeding.guardReason == null && seeding.placed > 0)
                Log.Message($"[RegionsAndSocieties] #18: seeded {seeding.placed} holding(s) across {seeding.provincesWithAnchor} anchored province(s).");

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

        /// <summary>Cap on total world factions after splitting, so a dense low-tech world does not
        /// explode the faction list (#57).</summary>
        private const int MaxWorldFactionsAfterSplit = 40;

        /// <summary>
        /// #57: split each scattered low-tech faction (a finite cluster cap below Spacer tech — tribes and
        /// rough unions) into loosely-related kin sub-factions, one per geographic section of its
        /// settlements. The largest section keeps the faction; the others become new factions of the same
        /// def, renamed by direction (North/South, or West/East), with friendly goodwill to their kin.
        /// Runs after placement, before the colour pass.
        /// </summary>
        private static void SplitFactionsIntoSubFactions(PlanetLayer layer, FactionManager factionManager,
            SynapseRegionManager regionManager, WorldObjectsHolder worldObjects, WorldGrid worldGrid)
        {
            if (factionManager == null || regionManager == null || worldObjects == null || worldGrid == null) return;
            // Kin is now a per-faction choice (each faction's enableKin, defaulted from its kind) — the blanket
            // toggle is gone. Each faction is gated individually in the loop below.

            // Settlement provinces per faction (only surface settlements count).
            var provincesByFaction = new Dictionary<Faction, List<GeographicProvince>>();
            foreach (var o in worldObjects.AllWorldObjects)
            {
                if (o?.Faction == null || o.Faction.IsPlayer) continue;
                if (Integration.WorldObjectClassifier.Classify(o) != Integration.WorldObjectKind.Settlement) continue;
                var p = regionManager.GetProvinceForTile(o.Tile);
                if (p == null) continue;
                if (!provincesByFaction.TryGetValue(o.Faction, out var list)) { list = new List<GeographicProvince>(); provincesByFaction[o.Faction] = list; }
                if (!list.Contains(p)) list.Add(p);
            }

            // Snapshot the parents up front — we add factions as we go and must not re-split one. The
            // per-faction ShouldSplit gate below decides eligibility by kind (pirate / tribe / rough union).
            var parents = factionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && !f.def.hidden)
                .ToList();

            int created = 0;
            foreach (var parent in parents)
            {
                if (factionManager.AllFactions.Count() >= MaxWorldFactionsAfterSplit) break;
                if (!provincesByFaction.TryGetValue(parent, out var provs) || provs.Count == 0) continue;

                var profile = FactionPlacementSettings.GetProfile(parent.def);

                // Kin is enabled per faction (defaulted from its kind). If off, this faction stays whole —
                // its scattered clusters are just the same faction's territory, no kin factions are made.
                if (!FactionPlacementSettings.EffectiveEnableKin(profile, parent.def)) continue;

                var bodies = BuildFactionBodies(provs);
                if (bodies.Count < 2) continue;   // needs at least two clusters to form regional kin

                // How many kin factions this faction forms — the two knobs combined: number of clusters
                // (equal division) clamped by the minimum cluster size, capped at the bodies it actually has.
                int clusters = FactionPlacementSettings.EffectiveClusterCount(profile, parent.def);
                int kinCount = Placement.SubFactionRules.PlannedKinCount(provs.Count, clusters, profile.clusterSize);
                int k = kinCount < bodies.Count ? kinCount : bodies.Count;
                if (k < 2) continue;   // resolves to one faction — no kin

                // Group the bodies into k geographic sections (equal-ish division by centroid); each section
                // becomes one kin faction. This CAPS the kin count at k even when terrain fragmented the
                // faction into more bodies than k.
                var centroids = new List<Placement.GeoPoint>(bodies.Count);
                foreach (var b in bodies) centroids.Add(BodyCentroid(b, worldGrid));
                int[] sectionOf = Placement.SubFactionRules.AssignSections(centroids, k);

                var sectionProvs = new List<List<GeographicProvince>>();
                for (int s = 0; s < k; s++) sectionProvs.Add(new List<GeographicProvince>());
                var sx = new double[k]; var sy = new double[k]; var sz = new double[k]; var sc = new int[k];
                for (int bi = 0; bi < bodies.Count; bi++)
                {
                    int s = sectionOf[bi];
                    sectionProvs[s].AddRange(bodies[bi]);
                    sx[s] += centroids[bi].X; sy[s] += centroids[bi].Y; sz[s] += centroids[bi].Z; sc[s]++;
                }
                var sectionPts = new List<Placement.GeoPoint>(k);
                for (int s = 0; s < k; s++) { int nn = System.Math.Max(1, sc[s]); sectionPts.Add(new Placement.GeoPoint(sx[s] / nn, sy[s] / nn, sz[s] / nn)); }

                // The section with the most settlement provinces keeps the parent faction (clean base name).
                int keep = 0;
                for (int s = 1; s < k; s++) if (sectionProvs[s].Count > sectionProvs[keep].Count) keep = s;

                // One distinct compass label per section; the kept section gets the clean base name.
                string[] labels = Placement.SubFactionRules.BodyLabels(sectionPts, keep);

                string baseName = parent.Name;
                if (!string.IsNullOrEmpty(labels[keep])) parent.Name = Placement.SubFactionRules.ComposeName(labels[keep], baseName);

                var kin = new List<Faction> { parent };
                for (int s = 0; s < k; s++)
                {
                    if (s == keep) continue;
                    if (factionManager.AllFactions.Count() >= MaxWorldFactionsAfterSplit) break;

                    Faction sub = TryGenerateFaction(layer, parent.def);
                    if (sub == null) continue;
                    // Register the sub-faction the same way the main worldgen loop registers its factions.
                    // TryGenerateFaction does NOT add to the manager (the caller does), and an unregistered
                    // faction has no goodwill-situation state — reading its PlayerGoodwill (the Territories
                    // overlay does) then NREs deep in vanilla's GoodwillSituationManager. Add it BEFORE any
                    // relation/goodwill work so that state exists.
                    factionManager.Add(sub);
                    sub.Name = Placement.SubFactionRules.ComposeName(labels[s], baseName);

                    // Relations against every existing faction, then friendly kin goodwill with the parent
                    // and any siblings already made — loosely related, not merged, not hostile.
                    foreach (var other in factionManager.AllFactions)
                        if (other != sub) sub.RelationWith(other, true);
                    foreach (var k2 in kin) SetKinGoodwill(sub, k2);
                    TryShareIdeo(parent, sub);
                    kin.Add(sub);

                    // Reassign this section's settlements and province ownership to the sub-faction.
                    string subId = sub.GetUniqueLoadID();
                    string parentId = parent.GetUniqueLoadID();
                    var sectionSet = new HashSet<GeographicProvince>(sectionProvs[s]);
                    foreach (var o in worldObjects.AllWorldObjects)
                    {
                        if (o?.Faction != parent) continue;
                        if (Integration.WorldObjectClassifier.Classify(o) != Integration.WorldObjectKind.Settlement) continue;
                        var p = regionManager.GetProvinceForTile(o.Tile);
                        if (p != null && sectionSet.Contains(p)) o.SetFaction(sub);
                    }
                    foreach (var p in sectionProvs[s])
                    {
                        p.owningFactionIds.Remove(parentId);
                        if (!p.owningFactionIds.Contains(subId)) p.owningFactionIds.Add(subId);
                    }
                    created++;
                    Log.Message($"[RegionsAndSocieties] #57: kin '{sub.Name}' off '{parent.Name}' ({sectionProvs[s].Count} provinces).");
                }
            }
            if (created > 0)
                Log.Message($"[RegionsAndSocieties] Split {created} sub-faction(s) off scattered low-tech factions (#57).");
        }

        /// <summary>Connected components of a faction's settlement provinces under land adjacency.</summary>
        private static List<List<GeographicProvince>> BuildFactionBodies(List<GeographicProvince> provs)
        {
            var byId = new Dictionary<int, GeographicProvince>();
            foreach (var p in provs) byId[p.id] = p;
            var seen = new HashSet<int>();
            var bodies = new List<List<GeographicProvince>>();
            foreach (var start in provs)
            {
                if (seen.Contains(start.id)) continue;
                var comp = new List<GeographicProvince>();
                var stack = new Stack<GeographicProvince>();
                stack.Push(start); seen.Add(start.id);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    comp.Add(cur);
                    if (cur.borderShares != null)
                        foreach (int nid in cur.borderShares.Keys)
                            if (byId.TryGetValue(nid, out var np) && seen.Add(nid)) stack.Push(np);
                }
                bodies.Add(comp);
            }
            return bodies;
        }

        private static Placement.GeoPoint BodyCentroid(List<GeographicProvince> body, WorldGrid worldGrid)
        {
            double x = 0, y = 0, z = 0; int n = 0;
            foreach (var p in body)
            {
                if (p.tiles == null || p.tiles.Count == 0) continue;
                Vector3 c = worldGrid.GetTileCenter(p.tiles[0]);
                x += c.x; y += c.y; z += c.z; n++;
            }
            if (n == 0) return new Placement.GeoPoint(0, 0, 0);
            return new Placement.GeoPoint(x / n, y / n, z / n);
        }

        private static void SetKinGoodwill(Faction a, Faction b)
        {
            try
            {
                // Write the kin goodwill straight onto both relation records — the way vanilla seeds its
                // own initial faction relations during worldgen. TryAffectGoodwillWith recomputes the
                // player-relative goodwill situations and reads Faction.OfPlayer, which logs "Could not find
                // player faction." once per call while the world is still generating (the player faction
                // does not exist yet) — the #59 log spam. baseGoodwill takes no such path; +60 keeps the
                // pair Neutral (loosely related, not merged, not hostile), the intended relation, and the
                // player's own goodwill with each kin faction is established later when the player faction is
                // created. Both directions must exist first (each new faction only made its own side).
                var relAB = a.RelationWith(b, true);
                var relBA = b.RelationWith(a, true);
                relAB.baseGoodwill = Placement.SubFactionRules.LooseKinGoodwill;
                relBA.baseGoodwill = Placement.SubFactionRules.LooseKinGoodwill;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RegionsAndSocieties] #57: could not set kin goodwill between '{a?.Name}' and '{b?.Name}': {ex.Message}");
            }
        }

        private static void TryShareIdeo(Faction parent, Faction sub)
        {
            if (!ModsConfig.IdeologyActive) return;
            try
            {
                var primary = parent.ideos?.PrimaryIdeo;
                if (primary != null && sub.ideos != null) sub.ideos.SetPrimary(primary);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RegionsAndSocieties] #57: could not share ideoligion from '{parent?.Name}' to '{sub?.Name}': {ex.Message}");
            }
        }

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
                // #54 RP2 calibration: force RP2's Planet Scale / sea level for this gen (RP2-only; no-op
                // otherwise). RP2's own RegisterPlanetLayer prefix picks up subcount for the subdivisions.
                int devSub = FactionPlacementSettings.devQuicktestSubcount;
                if (devSub > 0 && Integration.RealisticPlanetsProbe.TrySetSubcount(devSub))
                    Log.Message($"[RegionsAndSocieties] DEV: RP2 Planet Scale (subcount) overridden -> {devSub} (devQuicktestSubcount).");
                int devSea = FactionPlacementSettings.devQuicktestSeaLevel;
                if (devSea >= 0 && Integration.RealisticPlanetsProbe.TrySetSeaLevelOrdinal(devSea))
                    Log.Message($"[RegionsAndSocieties] DEV: RP2 sea level overridden -> ordinal {devSea} (devQuicktestSeaLevel).");
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

            // Region-estimate calibration line (#54/#47): exact land-tile count, land fraction, land-region
            // count and average region size for the generated world — the pre-gen dialog can only estimate
            // these from planet coverage, so this is the ground truth to calibrate that estimate against.
            try
            {
                int landTiles = 0;
                var grid = __result?.grid;
                if (grid != null)
                {
                    int tc = grid.TilesCount;
                    for (int i = 0; i < tc; i++) if (!grid[i].WaterCovered) landTiles++;
                }
                int landRegions = 0;
                var mgr = __result?.GetComponent<SynapseRegionManager>();
                if (mgr?.Provinces != null)
                    foreach (var pr in mgr.Provinces)
                        if (pr.provinceType == ProvinceType.Land && pr.tiles != null && pr.tiles.Count > 0) landRegions++;
                float landFrac = tiles > 0 ? (float)landTiles / tiles : 0f;
                float avgRegion = landRegions > 0 ? (float)landTiles / landRegions : 0f;
                Log.Message($"[RegionsAndSocieties] CALIB: coverage={planetCoverage:P0} totalTiles={tiles} landTiles={landTiles} landFrac={landFrac:P1} landRegions={landRegions} avgRegionTiles={avgRegion:F1} targetSize={FactionPlacementSettings.targetRegionSize}");
            }
            catch (Exception ex) { Log.Warning($"[RegionsAndSocieties] CALIB line failed: {ex.Message}"); }
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
