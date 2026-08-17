using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties
{
    public static class PopulationDensityUtility
    {
        public struct QueueEntry
        {
            public PlanetTile tile;
            public float multiplier;

            public QueueEntry(PlanetTile tile, float multiplier)
            {
                this.tile = tile;
                this.multiplier = multiplier;
            }
        }

        public struct PopSource
        {
            public int tileId;
            public int population;
        }

        // The old IsVoeOutpost/GetVoeOutpostPopulation belt-and-suspenders pair is gone (Core-MMF#3):
        // it existed because the reflection profile could silently fail to read a population, and
        // core reached around its own adapter layer to compensate. Populations now come only from
        // the adapter registry, and the typed adapter in VOE-CP cannot fail the way the profile did.

        // Two arrays, two meanings, deliberately kept apart (#62 / #55):
        //   cachedTilePopulations       - the *smeared* influence field: a settlement's head count
        //                                 flooded outward with decay. Good for a heatmap gradient,
        //                                 wrong for any total (summing the smear double-counts).
        //   cachedTileSourcePopulations - the dwellings actually *at* a tile: the settlement's own
        //                                 head count, or a natural pocket's capped size. This is what
        //                                 the per-tile "Pawn dwellings" label shows and what province
        //                                 population totals sum, so a region's population is the sum
        //                                 of who lives in it, not the sum of everyone's influence on it.
        private static int[] cachedTilePopulations = null;
        private static int[] cachedTileSourcePopulations = null;
        private static bool cacheDirty = true;

        // Natural-pocket realism (#62). A pocket that forms on its own stays small unless a landmark
        // (road, river, coast) gives it a reason to grow; dangerous biomes push pawns to the edges.
        private const float NaturalPocketCap = 5f;      // off-landmark: no more than a homestead
        private const float LandmarkPocketCap = 12f;    // on road/river/coast: room to grow
        private const float DangerousLandmarkCap = 6f;  // rainforest/swamp, and only near a landmark

        // Bumped on every invalidation so province-level aggregates (currentPopulation, dwellings)
        // can tell in O(1) whether their cached sum is still current, instead of re-summing tiles
        // on every read. Add/remove of a population-bearing world object marks this dirty (#48).
        public static int CacheVersion { get; private set; }

        public static void MarkCacheDirty()
        {
            cacheDirty = true;
            CacheVersion++;
        }

        // Set at the top of each refresh so the propagation helper and the finalize step agree on
        // which algorithm this world uses. A pre-0.7.2 world keeps the legacy behaviour end to end:
        // uncapped pockets, no river spread, and no source/smear split (labels and province totals
        // read the smeared field, including the #55 overcount) — so an existing save's numbers never
        // shift. New worlds get the current model. Which one applies is stamped on the save (see
        // SynapseRegionManager.DensityAlgorithmVersion).
        private static bool refreshingLegacy;

        private static bool IsLegacyDensity()
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            return mgr != null && mgr.DensityAlgorithmVersion == SynapseRegionManager.DensityAlgorithmLegacy;
        }

        private static void RefreshCache()
        {
            if (Find.World == null || Find.WorldGrid == null) return;
            refreshingLegacy = IsLegacyDensity();
            int count = Find.WorldGrid.TilesCount;
            if (cachedTilePopulations == null || cachedTilePopulations.Length != count)
            {
                cachedTilePopulations = new int[count];
            }
            else
            {
                System.Array.Clear(cachedTilePopulations, 0, count);
            }
            if (cachedTileSourcePopulations == null || cachedTileSourcePopulations.Length != count)
            {
                cachedTileSourcePopulations = new int[count];
            }
            else
            {
                System.Array.Clear(cachedTileSourcePopulations, 0, count);
            }

            float[] tempPops = new float[count];    // smeared influence field (heatmap)
            float[] tempSource = new float[count];  // dwellings at the tile (labels + province totals)

            // 1. Baseline pawn placement in hospitable environments (nomads, homesteads). Pawns
            //    settle where they can survive: a pocket left to itself stays small (<= NaturalPocketCap),
            //    and in a dangerous biome (rainforest, swamp) it only forms at all if it can hug a
            //    landmark - a road, a river, a coast - and even then it stays modest (#62).
            for (int i = 0; i < count; i++)
            {
                Tile tileData = Find.WorldGrid[i];
                if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable || tileData.PrimaryBiome == null || tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")
                {
                    continue;
                }

                if (tileData.temperature >= -12f && tileData.temperature <= 42f && tileData.PrimaryBiome.plantDensity > 0.15f)
                {
                    UnityEngine.Random.State state = UnityEngine.Random.state;
                    UnityEngine.Random.InitState(i * 377 + 99);
                    float roll = UnityEngine.Random.value;
                    if (roll < 0.06f)
                    {
                        float raw = UnityEngine.Random.Range(2f, 8f) * (tileData.PrimaryBiome.plantDensity + tileData.PrimaryBiome.forageability + 0.2f);
                        if (refreshingLegacy)
                        {
                            // 0.7.1 exactly: uncapped, no landmark/danger consideration.
                            tempPops[i] += raw;
                        }
                        else
                        {
                            // Landmark and danger are only worth the neighbour lookups on the ~6% of tiles
                            // that actually roll a pocket, so they sit inside the roll, not the outer loop.
                            bool nearLandmark = HasRoadAt(i) || IsNextToRiver(i) || IsNextToWater(i);
                            bool dangerous = IsDangerousBiome(tileData.PrimaryBiome);
                            float pocket = RealisticPocket(raw, nearLandmark, dangerous);
                            if (pocket > 0f)
                            {
                                tempPops[i] += pocket;
                                tempSource[i] += pocket;
                            }
                        }
                    }
                    UnityEngine.Random.state = state;
                }
            }

            // 2. Collection of all population sources (Settlements and VOE Outposts)
            var popSources = new List<PopSource>();

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (var settlement in settlements)
                {
                    int pop = GetSettlementPopulation(settlement);
                    if (pop > 0)
                    {
                        popSources.Add(new PopSource { tileId = settlement.Tile, population = pop });
                    }
                }
            }

            var allWorldObjects = Find.WorldObjects?.AllWorldObjects;
            if (allWorldObjects != null)
            {
                foreach (var obj in allWorldObjects)
                {
                    if (obj == null) continue;

                    // Vanilla settlements were already counted above.
                    if (obj is Settlement) continue;

                    // 0.7: any inhabited world object from any mod is a population source, not just
                    // VOE outposts. The classifier decides; the adapter supplies the head count.
                    if (!Integration.WorldObjectClassifier.HasPopulation(obj)) continue;

                    int pop = Integration.WorldObjectClassifier.GetPopulation(obj);

                    if (pop > 0)
                    {
                        popSources.Add(new PopSource { tileId = obj.Tile, population = pop });
                    }
                }
            }

            // 3. Population propagation from sources
            foreach (var source in popSources)
            {
                int startTileId = source.tileId;

                // The head count lives *here*. It flows outward into the smear below, but for totals
                // and the tile label it must be counted once, at its own tile, and nowhere else (#55).
                // Legacy worlds have no separate source field; their totals sum the smear, so this
                // per-tile attribution is skipped and tempSource is overwritten with the smear below.
                if (!refreshingLegacy && startTileId >= 0 && startTileId < count)
                {
                    tempSource[startTileId] += source.population;
                }

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

                if (startPlanetTile == PlanetTile.Invalid) continue;

                var visited = new HashSet<int>();
                var queue = new Queue<QueueEntry>();

                queue.Enqueue(new QueueEntry(startPlanetTile, 1.0f));
                visited.Add(startTileId);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    PlanetTile currentTile = current.tile;
                    int currentTileId = currentTile.tileId;
                    float currentMultiplier = current.multiplier;

                    if (currentMultiplier < 0.001f) continue;

                    tempPops[currentTileId] += (source.population * currentMultiplier);

                    var neighbors = new List<PlanetTile>();
                    Find.WorldGrid.GetTileNeighbors(currentTileId, neighbors);
                    foreach (var neighbor in neighbors)
                    {
                        int neighborId = neighbor.tileId;
                        if (!visited.Contains(neighborId))
                        {
                            visited.Add(neighborId);

                            float stepMultiplier = GetStepMultiplier(currentTile, neighbor);
                            if (stepMultiplier > 0f)
                            {
                                queue.Enqueue(new QueueEntry(neighbor, currentMultiplier * stepMultiplier));
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                cachedTilePopulations[i] = UnityEngine.Mathf.RoundToInt(tempPops[i]);
                // Legacy worlds have no source/smear distinction: the per-tile label and province
                // totals read the smeared field exactly as they did in 0.7.1 (overcount included).
                cachedTileSourcePopulations[i] = refreshingLegacy
                    ? cachedTilePopulations[i]
                    : UnityEngine.Mathf.RoundToInt(tempSource[i]);
            }

            cacheDirty = false;
        }

        /// <summary>Force the density caches current if a world object has changed since the last read.</summary>
        public static void EnsureCache()
        {
            if (Find.World == null || Find.WorldGrid == null) return;
            if (cacheDirty || cachedTilePopulations == null || cachedTilePopulations.Length != Find.WorldGrid.TilesCount)
            {
                RefreshCache();
            }
        }

        /// <summary>
        /// The smeared influence value at a tile: a settlement's head count flooded outward with
        /// decay. This is the heatmap gradient - do <b>not</b> sum it for a population total (#55).
        /// </summary>
        public static int GetPopulationAtTile(int targetTile)
        {
            EnsureCache();
            if (cachedTilePopulations == null || targetTile < 0 || targetTile >= cachedTilePopulations.Length) return 0;
            return cachedTilePopulations[targetTile];
        }

        /// <summary>
        /// The dwellings actually at a tile: its own settlement head count, or its capped natural
        /// pocket. This is the honest per-tile "Pawn dwellings" number, and the value province totals
        /// sum (#55/#62).
        /// </summary>
        public static int GetSourcePopulationAtTile(int targetTile)
        {
            EnsureCache();
            if (cachedTileSourcePopulations == null || targetTile < 0 || targetTile >= cachedTileSourcePopulations.Length) return 0;
            return cachedTileSourcePopulations[targetTile];
        }

        public static int GetSettlementPopulation(Settlement settlement)
        {
            if (settlement == null) return 0;

            if (settlement.Faction != null && settlement.Faction.IsPlayer)
            {
                var map = Find.Maps?.FirstOrDefault(m => m.Tile == settlement.Tile);
                if (map != null)
                {
                    return map.mapPawns?.FreeColonistsCount ?? 0;
                }
                return 0;
            }

            int basePop = 50;
            if (settlement.Faction != null)
            {
                var tech = settlement.Faction.def?.techLevel ?? TechLevel.Industrial;
                if (tech == TechLevel.Neolithic) basePop = 60;
                else if (tech == TechLevel.Industrial) basePop = 90;
                else if (tech >= TechLevel.Spacer) basePop = 150;
            }

            System.Random random = new System.Random(settlement.Tile);
            return basePop + random.Next(-10, 20);
        }

        public static float GetStepMultiplier(PlanetTile fromTile, PlanetTile toTile)
        {
            if (Find.WorldGrid == null) return 0f;

            Tile tileData = Find.WorldGrid[toTile.tileId];
            if (tileData == null) return 0f;

            // 1. Impassable mountains and water do not transfer any population
            if (tileData.hilliness == Hilliness.Impassable || 
                tileData.WaterCovered || 
                (tileData.PrimaryBiome != null && tileData.PrimaryBiome.impassable))
            {
                return 0f;
            }

            float factor = 1f;
            bool hasTerrainFeature = false;

            // Large Hills factor: 4
            if (tileData.hilliness == Hilliness.LargeHills)
            {
                factor *= 4f;
                hasTerrainFeature = true;
            }
            // Mountainous factor: 8
            else if (tileData.hilliness == Hilliness.Mountainous)
            {
                factor *= 8f;
                hasTerrainFeature = true;
            }

            // Swamp/Marsh factor: 8
            bool isSwampOrMarsh = tileData.swampiness > 0.1f || 
                (tileData.PrimaryBiome != null && 
                 (tileData.PrimaryBiome.defName.Contains("Swamp") || 
                  tileData.PrimaryBiome.defName.Contains("Marsh")));
            if (isSwampOrMarsh)
            {
                factor *= 8f;
                hasTerrainFeature = true;
            }

            // Default flat/small hills factor: 2
            if (!hasTerrainFeature)
            {
                factor = 2f;
            }

            // Along a road
            RoadDef road = Find.WorldGrid.GetRoadDef(fromTile, toTile);
            if (road != null)
            {
                factor *= (2f / 3f);
            }

            // Along a river: settlement follows the water it drinks and ships on. Rivers fed region
            // segmentation but never density before (#62); they carry population like roads do. Only
            // in the current algorithm — a legacy world's propagation must match 0.7.1, which had no
            // river term.
            if (!refreshingLegacy)
            {
                RiverDef river = Find.WorldGrid.GetRiverDef(fromTile.tileId, toTile.tileId);
                if (river != null)
                {
                    factor *= (2f / 3f);
                }
            }

            // Next to water
            if (IsNextToWater(toTile.tileId))
            {
                factor *= (2f / 3f);
            }

            float stepMultiplier = 1f / factor;

            // Cap stepMultiplier at 0.75f to prevent population increases/explosions
            if (stepMultiplier > 0.75f)
            {
                stepMultiplier = 0.75f;
            }

            return stepMultiplier;
        }

        private static bool IsNextToWater(int tileId)
        {
            Tile tile = Find.WorldGrid[tileId];
            if (tile == null) return false;
            if (tile.IsCoastal || tile.WaterCovered) return true;

            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
            foreach (var n in neighbors)
            {
                var nt = Find.WorldGrid[n.tileId];
                if (nt != null && nt.WaterCovered) return true;
            }
            return false;
        }

        /// <summary>Whether a road touches this tile (any edge to a neighbour carries one).</summary>
        private static bool HasRoadAt(int tileId)
        {
            if (Find.WorldGrid == null) return false;
            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
            foreach (var n in neighbors)
            {
                if (Find.WorldGrid.GetRoadDef(tileId, n.tileId) != null) return true;
            }
            return false;
        }

        /// <summary>Whether a river runs along any edge of this tile. Mirrors SynapseRegionManager.HasRiver.</summary>
        private static bool IsNextToRiver(int tileId)
        {
            if (Find.WorldGrid == null) return false;
            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
            foreach (var n in neighbors)
            {
                if (Find.WorldGrid.GetRiverDef(tileId, n.tileId) != null || Find.WorldGrid.GetRiverDef(n.tileId, tileId) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Biomes where predators and disease make interior settlement unlikely. Rainforest and
        /// swamp/marsh are the ones the propagation model already treats as high-resistance terrain,
        /// so treating them as dangerous for pocket seeding keeps the two halves of the model aligned.
        /// </summary>
        private static bool IsDangerousBiome(BiomeDef biome)
        {
            if (biome == null) return false;
            string n = biome.defName;
            if (string.IsNullOrEmpty(n)) return false;
            return n.Contains("Rainforest") || n.Contains("Swamp") || n.Contains("Marsh");
        }

        /// <summary>
        /// Clamp a raw natural pocket to what its surroundings can support (#62): off-landmark it stays
        /// a homestead (&lt;= <see cref="NaturalPocketCap"/>); near a landmark it may grow; a dangerous
        /// biome yields nothing away from a landmark and stays modest even beside one.
        /// </summary>
        private static float RealisticPocket(float raw, bool nearLandmark, bool dangerous)
        {
            if (dangerous && !nearLandmark) return 0f;

            float cap = nearLandmark ? LandmarkPocketCap : NaturalPocketCap;
            if (dangerous && cap > DangerousLandmarkCap) cap = DangerousLandmarkCap;

            return raw < cap ? raw : cap;
        }
    }
}
