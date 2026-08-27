using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RegionsAndSocieties
{
    public class SynapseRegionManager : WorldComponent
    {
        private List<GeographicProvince> provinces = new List<GeographicProvince>();
        private int[] tileToProvinceId;
        private Dictionary<int, int> settlementPlacementOrder = new Dictionary<int, int>();

        // Region-growth tuning (#3). MinFillFraction is the share of the value budget a region must
        // fill unconditionally before it switches from filling to border-seeking. BarrierStepCost is
        // the step-cost threshold that separates an open-terrain hop (base ~1, up to ~1.9 with the
        // meander warp) from crossing a natural feature (mountain / water / river, each +12): a step
        // at or above it is treated as crossing a boundary and is refused while border-seeking.
        private const float MinFillFraction = 0.45f;
        private const float BarrierStepCost = 3f;
        // Minimum number of open-terrain links to the region required to annex a tile while
        // border-seeking. 1 only refuses tiles reachable solely across a natural feature (the far
        // side of a boundary); shape compactness is handled by the frontier priority, not this gate.
        private const int CompactLinkMin = 1;
        // How strongly the frontier prefers tiles already surrounded by the region over tiles that
        // would extend a finger. Each not-yet-claimed neighbour adds this to the tile's priority
        // (higher = later), so concavities fill first and regions grow as compact blobs. This is the
        // only shape pressure in feature-poor terrain, so it must outweigh the ~1-per-tile distance.
        private const float CompactWeight = 4f;

        public int GetSettlementPlacementOrder(int tileId)
        {
            if (settlementPlacementOrder != null && settlementPlacementOrder.TryGetValue(tileId, out int order))
            {
                return order;
            }
            return -1;
        }

        public void SetSettlementPlacementOrder(int tileId, int order)
        {
            if (settlementPlacementOrder == null)
            {
                settlementPlacementOrder = new Dictionary<int, int>();
            }
            settlementPlacementOrder[tileId] = order;
        }

        public int GetNextPlacementOrderForFaction(Faction faction)
        {
            int count = 0;
            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
                if (Integration.WorldObjectClassifier.IsSettlement(obj) && obj.Faction == faction)
                {
                    count++;
                }
            }
            return count + 1;
        }

        public List<GeographicProvince> Provinces
        {
            get
            {
                if (provinces == null || provinces.Count == 0)
                {
                    GenerateProvinces();
                }
                return provinces;
            }
        }

        public SynapseRegionManager(World world) : base(world)
        {
            InitializeData();
        }

        private void InitializeData()
        {
            if (tileToProvinceId == null && Find.WorldGrid != null)
            {
                tileToProvinceId = new int[Find.WorldGrid.TilesCount];
                for (int i = 0; i < tileToProvinceId.Length; i++)
                {
                    tileToProvinceId[i] = -1;
                }
            }
        }

        public int GetProvinceId(int tileId)
        {
            InitializeData();
            if (tileId < 0 || tileId >= tileToProvinceId.Length) return -1;
            return tileToProvinceId[tileId];
        }

        public GeographicProvince GetProvince(int provinceId)
        {
            return provinces.FirstOrDefault(p => p.id == provinceId);
        }

        public GeographicProvince GetProvinceForTile(int tileId)
        {
            int pid = GetProvinceId(tileId);
            if (pid == -1) return null;
            return GetProvince(pid);
        }

        // -1 unresolved, 0 compatibility (non-strict), 1 strict. An int rather than a bool because
        // "absent from this save" has to be distinguishable from "saved as false" — that
        // distinction is the whole mechanism for adopting a save R&T was not present for.
        private int strictTerritorialOwnershipRaw = -1;

        // Which population-density algorithm this world uses. Population is derived, not scribed, so
        // it recomputes on every load — which means a 0.7.1 world loaded under 0.7.2 would silently
        // switch to the new numbers. Stamping the world lets an existing save keep the density it was
        // built with. -1 unresolved; 1 legacy (0.7.1 and earlier: uncapped pockets, smeared totals
        // incl. the #55 overcount); 2 current (0.7.2+: capped/landmark-biased pockets, source totals).
        public const int DensityAlgorithmLegacy = 1;
        public const int DensityAlgorithmCurrent = 2;
        private int densityAlgorithmVersionRaw = -1;

        /// <summary>
        /// The density algorithm in force for this world. Only a save explicitly resolved to legacy
        /// (a pre-0.7.2 world) reports legacy; an unstamped live new world defaults to current.
        /// </summary>
        public int DensityAlgorithmVersion
        {
            get { return densityAlgorithmVersionRaw == DensityAlgorithmLegacy ? DensityAlgorithmLegacy : DensityAlgorithmCurrent; }
        }

        /// <summary>
        /// Whether this world enforces R&amp;T's placement rules for settlements and outposts.
        ///
        /// <para><b>Strict</b> (worlds generated with R&amp;T): buffers, supply, footholds and the
        /// one-holding-per-province assumptions all apply, as they have since 0.7.</para>
        ///
        /// <para><b>Compatibility</b> (R&amp;T added to a world already in progress): placement is
        /// left entirely to vanilla and to whatever other mods are doing it. Provinces are still
        /// generated and territory is still owned and drawn — only the rules that would refuse a
        /// placement stand down, because a world that was built without them is already full of
        /// settlements those rules would have forbidden.</para>
        /// </summary>
        public bool StrictTerritorialOwnership
        {
            get { return strictTerritorialOwnershipRaw != 0; }
            set { strictTerritorialOwnershipRaw = value ? 1 : 0; }
        }

        /// <summary>True once the mode has been decided for this world, either on load or at worldgen.</summary>
        public bool StrictOwnershipResolved
        {
            get { return strictTerritorialOwnershipRaw != -1; }
        }

        /// <summary>
        /// Decide the mode for a save that predates the flag.
        ///
        /// <para>The discriminator is <b>provinces, not the flag</b>. A save made with R&amp;T 0.7
        /// also has no flag yet, but it does have generated provinces — that world was built under
        /// the placement rules and must keep them. A save with neither is one R&amp;T has just been
        /// added to, and its existing settlements were placed with no regard for our rules, so
        /// enforcing them now would refuse placements next to towns that already exist.</para>
        /// </summary>
        /// <summary>
        /// Test seam: the province list without the lazy generation the <see cref="Provinces"/>
        /// getter performs. A case that needs to simulate "this save had no provinces" cannot use
        /// the getter, because reading it is what builds them.
        /// <para>Public rather than internal because the TestRunner is a separate assembly.</para>
        /// </summary>
        public List<GeographicProvince> ProvincesRaw
        {
            get { return provinces; }
        }

        /// <summary>
        /// Test seam: put the flag back to unresolved so the load-time decision can be exercised.
        /// Not part of normal operation — a live world has already decided.
        /// </summary>
        public void ResetStrictOwnershipForTesting()
        {
            strictTerritorialOwnershipRaw = -1;

            // A test that exercises the compat branch would otherwise arm the player notice and
            // drop a letter into the live test colony on the next tick. Restore what we touch.
            pendingCompatibilityNotice = false;
        }

        /// <summary>Test seam: run the load-time decision directly, without a save round trip.</summary>
        public void ResolveStrictOwnershipForTesting()
        {
            ResolveStrictOwnershipForLoadedSave();

            // The compat branch arms a player-facing letter. Tests run inside a live colony, so
            // leaving it armed would drop that letter on the next tick. The decision is what these
            // cases exercise; the notice is deliberately not.
            pendingCompatibilityNotice = false;
        }

        private void ResolveStrictOwnershipForLoadedSave()
        {
            if (strictTerritorialOwnershipRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            strictTerritorialOwnershipRaw = hadProvinces ? 1 : 0;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the territorial-ownership flag but has generated provinces: treating as strict."
                : "[RegionsAndSocieties] Save has no province data: adopting it in compatibility mode. Regions will be generated; placement rules stand down.");

            // Tell the player, not just the log. Somebody who installs mid-playthrough gets a
            // reduced mode and would otherwise have no way to know: the map modes look right, so
            // nothing on screen says placement governance is off. Deferred rather than shown here
            // because PostLoadInit runs before the UI is ready to take a letter.
            if (!hadProvinces) pendingCompatibilityNotice = true;
        }

        /// <summary>
        /// Decide the density algorithm for a save that predates the stamp. Same discriminator as the
        /// ownership mode: <b>provinces, not the flag</b>. A pre-0.7.2 world already has generated
        /// provinces and a population the player has been living with, so it keeps the legacy
        /// algorithm. A save with no provinces is one R&amp;T is generating regions for now, for the
        /// first time, so it gets the current algorithm — there is no prior population to preserve.
        /// </summary>
        private void ResolveDensityAlgorithmForLoadedSave()
        {
            if (densityAlgorithmVersionRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            densityAlgorithmVersionRaw = hadProvinces ? DensityAlgorithmLegacy : DensityAlgorithmCurrent;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the density-algorithm stamp but has provinces: keeping the legacy (pre-0.7.2) population algorithm so this world's numbers do not shift."
                : "[RegionsAndSocieties] Save has no province data: regions will be generated with the current population algorithm.");
        }

        /// <summary>Test seam: force the density algorithm back to unresolved so the load-time decision can be exercised.</summary>
        public void ResetDensityAlgorithmForTesting()
        {
            densityAlgorithmVersionRaw = -1;
        }

        /// <summary>Test seam: run the density load-time decision directly, without a save round trip.</summary>
        public void ResolveDensityAlgorithmForTesting()
        {
            ResolveDensityAlgorithmForLoadedSave();
        }

        /// <summary>Set when a save is adopted into compatibility mode; cleared once the player has been told.</summary>
        private bool pendingCompatibilityNotice;

        // WorldComponent has no FinalizeInit, so the notice rides the first tick instead. Ticks only
        // run once the game is actually playing, which is exactly when the letter stack is ready.
        // Coarse cadence for decaying demographic skews (#11): ~2500 ticks (an in-game hour) is ample
        // granularity for a decay measured in years, and keeps the override sweep off the hot path.
        private const int DemographicDecayInterval = 2500;

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (Find.TickManager != null && Find.TickManager.TicksGame % DemographicDecayInterval == 0)
            {
                Demographics.RegionDemographicsStress.Tick(DemographicDecayInterval);
            }

            if (!pendingCompatibilityNotice) return;
            pendingCompatibilityNotice = false;

            Find.LetterStack?.ReceiveLetter(
                "Regions and Territories: compatibility mode",
                "This world was created before Regions and Territories was installed, so it has been adopted in " +
                "compatibility mode.\n\n" +
                "Provinces have been generated and territory ownership is drawn on the world map as normal. What is " +
                "switched off is placement: the mod will not decide where settlements and outposts may be built. Your " +
                "world is already full of settlements that were placed with no regard for those rules, and applying " +
                "them now would refuse ground that has been settled since long before the mod arrived. Vanilla and " +
                "your other mods keep control of placement, and more than one settlement may share a province.\n\n" +
                "For the full experience — including faction placement governed by region occupancy, border buffers " +
                "and sequential expansion — start a new colony with the mod already installed. That is what the mod " +
                "is designed around; compatibility mode exists so an existing save is usable, not equivalent.\n\n" +
                "You can review this under 'Strict territorial ownership' in the mod settings.",
                LetterDefOf.NeutralEvent);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strictTerritorialOwnershipRaw, "strictTerritorialOwnership", -1);

            // Stamp worlds generated under 0.7.2+ as they are first saved. If the algorithm is still
            // unresolved at save time this is a live world running current code that never went
            // through the load-time resolver (i.e. a new game), so it is current by construction. A
            // loaded pre-0.7.2 save has already been resolved to legacy before any save happens.
            if (Scribe.mode == LoadSaveMode.Saving && densityAlgorithmVersionRaw == -1)
            {
                densityAlgorithmVersionRaw = DensityAlgorithmCurrent;
            }
            Scribe_Values.Look(ref densityAlgorithmVersionRaw, "densityAlgorithmVersion", -1);

            Scribe_Collections.Look(ref provinces, "provinces", LookMode.Deep);
            if (provinces == null)
            {
                provinces = new List<GeographicProvince>();
            }

            // 0.8: sparse demographic stress overrides. The demographic baseline is deterministic
            // (regenerated from the world seed), so only deliberate changes are stored here.
            Demographics.RegionDemographicsStress.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ResolveStrictOwnershipForLoadedSave();
                ResolveDensityAlgorithmForLoadedSave();

                // Population is cached statically and survives across loads within one process; drop
                // it so the next read rebuilds under the algorithm just resolved for this world.
                PopulationDensityUtility.MarkCacheDirty();
            }

            Scribe_Collections.Look(ref settlementPlacementOrder, "settlementPlacementOrder", LookMode.Value, LookMode.Value);
            if (settlementPlacementOrder == null)
            {
                settlementPlacementOrder = new Dictionary<int, int>();
            }

            List<int> tempList = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (tileToProvinceId != null)
                {
                    tempList = tileToProvinceId.ToList();
                }
            }
            Scribe_Collections.Look(ref tempList, "tileToProvinceId", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (tempList != null && Find.WorldGrid != null)
                {
                    tileToProvinceId = tempList.ToArray();
                }
                else
                {
                    InitializeData();
                }

                // Repair the reverse index from the authoritative province.tiles lists. Older saves
                // scribed a tile->province index that could fall out of sync with the provinces, or
                // never scribed one at all, leaving GetProvinceId returning -1 for tiles that plainly
                // belong to a province. Ownership buckets world objects by GetProvinceId, so that
                // silently zeroed all ownership on such saves (#67). Rebuilding here is a one-off
                // repair that then persists on the next save; it is idempotent on a healthy world.
                if (provinces != null && provinces.Count > 0 && Find.WorldGrid != null)
                {
                    RebuildTileIndexFromProvinces();
                    MarkOwnersDirty();
                }
            }
        }

        /// <summary>
        /// Rebuild the tile-&gt;province reverse index from the provinces' own tile lists — the
        /// authoritative partition (deep-scribed). Idempotent on a healthy world; a repair on a save
        /// whose scribed index was stale or absent (#67).
        /// </summary>
        private void RebuildTileIndexFromProvinces()
        {
            if (Find.WorldGrid == null || provinces == null) return;

            int n = Find.WorldGrid.TilesCount;
            if (tileToProvinceId == null || tileToProvinceId.Length != n)
            {
                tileToProvinceId = new int[n];
            }
            for (int i = 0; i < n; i++) tileToProvinceId[i] = -1;

            int mapped = 0;
            foreach (var p in provinces)
            {
                if (p?.tiles == null) continue;
                foreach (int t in p.tiles)
                {
                    if (t >= 0 && t < n)
                    {
                        tileToProvinceId[t] = p.id;
                        mapped++;
                    }
                }
            }

            Log.Message($"[RegionsAndSocieties] Rebuilt tile->province index from {provinces.Count} provinces ({mapped} tiles mapped).");
        }

        private bool HasRiver(int tileId)
        {
            if (Find.WorldGrid == null) return false;
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
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

        private BiomeDef GetPrimaryBiome(List<int> chunk)
        {
            if (chunk == null || chunk.Count == 0) return null;
            return chunk
                .Select(t => Find.WorldGrid[t].PrimaryBiome)
                .Where(b => b != null)
                .GroupBy(b => b)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        public void GenerateProvinces()
        {
            Log.Message("[RegionsAndSocieties] Generating Geographic Domains (Boundary-First Priority)...");

            // A world generating provinces with the flag still unresolved is a brand new world:
            // a loaded save resolves it in PostLoadInit, which runs before anything can reach the
            // lazy Provinces getter. New worlds take the configured default, which is strict.
            if (strictTerritorialOwnershipRaw == -1)
            {
                // Static on the settings class, like every other field there.
                bool strict = FactionPlacementSettings.strictTerritorialOwnershipDefault;
                strictTerritorialOwnershipRaw = strict ? 1 : 0;
                Log.Message($"[RegionsAndSocieties] New world: territorial ownership set to {(strict ? "strict" : "compatibility")}.");
            }

            if (Find.WorldGrid == null) return;
            int totalTiles = Find.WorldGrid.TilesCount;
            tileToProvinceId = new int[totalTiles];
            for (int i = 0; i < totalTiles; i++)
            {
                tileToProvinceId[i] = -1;
            }

            provinces.Clear();

            // The derived adjacency map describes the province layout we are about to replace, and
            // it is keyed on the world instance rather than on the provinces — so regenerating
            // inside one world is the one case the key cannot catch.
            ProvinceAdjacency.ClearCache();

            int provinceIdCounter = 0;

            // Pre-calculate river neighbor count for all tiles
            int[] riverNeighborCount = new int[totalTiles];
            List<RimWorld.Planet.PlanetTile> tempNeighbors = new List<RimWorld.Planet.PlanetTile>();
            for (int i = 0; i < totalTiles; i++)
            {
                if (tileToProvinceId[i] != -1 || !HasRiver(i)) continue;

                int count = 0;
                tempNeighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(i, tempNeighbors);
                foreach (var n in tempNeighbors)
                {
                    if (HasRiver(n.tileId) && (Find.WorldGrid.GetRiverDef(i, n.tileId) != null || Find.WorldGrid.GetRiverDef(n.tileId, i) != null))
                    {
                        count++;
                    }
                }
                riverNeighborCount[i] = count;
            }

            // Phase 2: Rivers (tiles with river def, split at forks)
            bool[] riverVisited = new bool[totalTiles];

            // Pass A: Seed only from non-fork tiles (degree < 3) to build segments outwards
            for (int i = 0; i < totalTiles; i++)
            {
                if (tileToProvinceId[i] != -1 || riverVisited[i] || !HasRiver(i)) continue;
                if (riverNeighborCount[i] >= 3) continue; // Skip forks in first seed pass

                List<int> riverSegment = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                riverVisited[i] = true;

                List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    riverSegment.Add(current);

                    // If this tile is a fork, do not propagate past it (act as boundary point)
                    if (riverNeighborCount[current] >= 3) continue;

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(current, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        if (tileToProvinceId[nid] == -1 && !riverVisited[nid] && HasRiver(nid) && 
                            (Find.WorldGrid.GetRiverDef(current, nid) != null || Find.WorldGrid.GetRiverDef(nid, current) != null))
                        {
                            riverVisited[nid] = true;
                            queue.Enqueue(nid);
                        }
                    }
                }

                if (riverSegment.Count > 0)
                {
                    GeographicProvince domain = new GeographicProvince(provinceIdCounter);
                    domain.tiles = riverSegment.ToList();
                    domain.provinceType = ProvinceType.River;
                    domain.primaryBiome = GetPrimaryBiome(riverSegment);
                    domain.name = GenerateProvinceName(provinceIdCounter, domain.primaryBiome, domain.provinceType);

                    foreach (int tileId in riverSegment)
                    {
                        tileToProvinceId[tileId] = provinceIdCounter;
                    }
                    provinces.Add(domain);
                    provinceIdCounter++;
                }
            }

            // Pass B: Collect any remaining/isolated fork tiles
            for (int i = 0; i < totalTiles; i++)
            {
                if (tileToProvinceId[i] != -1 || riverVisited[i] || !HasRiver(i)) continue;

                List<int> riverSegment = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                riverVisited[i] = true;

                List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    riverSegment.Add(current);

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(current, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        if (tileToProvinceId[nid] == -1 && !riverVisited[nid] && HasRiver(nid) && 
                            (Find.WorldGrid.GetRiverDef(current, nid) != null || Find.WorldGrid.GetRiverDef(nid, current) != null))
                        {
                            riverVisited[nid] = true;
                            queue.Enqueue(nid);
                        }
                    }
                }

                if (riverSegment.Count > 0)
                {
                    GeographicProvince domain = new GeographicProvince(provinceIdCounter);
                    domain.tiles = riverSegment.ToList();
                    domain.provinceType = ProvinceType.River;
                    domain.primaryBiome = GetPrimaryBiome(riverSegment);
                    domain.name = GenerateProvinceName(provinceIdCounter, domain.primaryBiome, domain.provinceType);

                    foreach (int tileId in riverSegment)
                    {
                        tileToProvinceId[tileId] = provinceIdCounter;
                    }
                    provinces.Add(domain);
                    provinceIdCounter++;
                }
            }

            // Phase 4: Land Valleys (remaining hospitable land tiles, excluding WaterCovered and Impassable ones)
            bool[] landVisited = new bool[totalTiles];
            int baseMin = FactionPlacementSettings.minRegionSize;
            int baseMax = FactionPlacementSettings.maxRegionSize;

            int minWithFeatures = baseMin - 5;
            int minNoFeatures = baseMin + 5;
            int maxWithFeatures = baseMax + 30;
            int maxNoFeatures = baseMax + 10;

            // Phase 2.5: Water wastes. Flood-fill contiguous barren WATER (ocean, sea ice) into their
            // own regions and claim their tiles so the land growth skips them — keeps open water out
            // of land regions. Dry barren land (desert) is deliberately NOT claimed here: it flows
            // into the value-budgeted, terrain-aware growth below, so a desert splits at its mountains
            // and rivers, and its resource pockets seed regions rather than being stranded as
            // enclaves (#3/#49).
            {
                var barrenNbrs = new List<RimWorld.Planet.PlanetTile>();
                for (int i = 0; i < totalTiles; i++)
                {
                    if (tileToProvinceId[i] != -1) continue;
                    Tile td = Find.WorldGrid[i];
                    if (td.hilliness == Hilliness.Impassable || (td.PrimaryBiome != null && td.PrimaryBiome.impassable)) continue;
                    // Water wastes only (ocean, sea ice). Dry barren land (desert) now flows into the
                    // value-budgeted, terrain-aware growth so it splits at mountains/rivers and its
                    // resource pockets become region seeds instead of enclaves (#3/#49).
                    if (!td.WaterCovered || !GeographicProvince.IsBarrenBiome(td.PrimaryBiome)) continue;

                    var barren = new List<int>();
                    var bq = new Queue<int>();
                    bq.Enqueue(i);
                    tileToProvinceId[i] = provinceIdCounter;
                    while (bq.Count > 0)
                    {
                        int cur = bq.Dequeue();
                        barren.Add(cur);
                        barrenNbrs.Clear();
                        Find.WorldGrid.GetTileNeighbors(cur, barrenNbrs);
                        foreach (var n in barrenNbrs)
                        {
                            int nid = n.tileId;
                            if (tileToProvinceId[nid] != -1) continue;
                            Tile nd = Find.WorldGrid[nid];
                            if (nd.hilliness == Hilliness.Impassable || (nd.PrimaryBiome != null && nd.PrimaryBiome.impassable)) continue;
                            if (!nd.WaterCovered || !GeographicProvince.IsBarrenBiome(nd.PrimaryBiome)) continue;
                            tileToProvinceId[nid] = provinceIdCounter;
                            bq.Enqueue(nid);
                        }
                    }

                    var barrenDom = new GeographicProvince(provinceIdCounter);
                    barrenDom.tiles = barren;
                    barrenDom.provinceType = ProvinceType.Ocean;
                    barrenDom.primaryBiome = GetPrimaryBiome(barren);
                    barrenDom.name = GenerateProvinceName(provinceIdCounter, barrenDom.primaryBiome, barrenDom.provinceType);
                    provinces.Add(barrenDom);
                    provinceIdCounter++;
                }
            }

            for (int i = 0; i < totalTiles; i++)
            {
                Tile tileData = Find.WorldGrid[i];
                if (tileToProvinceId[i] != -1 || landVisited[i] || tileData.hilliness == Hilliness.Impassable || (tileData.PrimaryBiome != null && tileData.PrimaryBiome.impassable)) continue;

                List<int> landPocket = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                landVisited[i] = true;

                List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    landPocket.Add(current);

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(current, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        Tile neighborData = Find.WorldGrid[nid];
                        if (tileToProvinceId[nid] == -1 && !landVisited[nid] && neighborData.hilliness != Hilliness.Impassable && (neighborData.PrimaryBiome == null || !neighborData.PrimaryBiome.impassable))
                        {
                            landVisited[nid] = true;
                            queue.Enqueue(nid);
                        }
                    }
                }

                if (landPocket.Count == 0) continue;

                bool hasFeatures = ChunkHasNaturalFeatures(landPocket);
                float pointCap = hasFeatures ? maxWithFeatures : maxNoFeatures;

                // Always partition by terrain-aware, value-budgeted growth (#3/#49): the point cap is
                // the size limit, so this single call does region design AND sizing together — no
                // keep-whole special case and no post-hoc carve. Rich land yields compact regions,
                // barren waste sprawls, water is absorbed for almost nothing.
                List<List<int>> subPockets = GrowTerrainBoundedRegions(landPocket, pointCap);
                foreach (var pocket in subPockets)
                {
                    if (pocket.Count == 0) continue;

                    GeographicProvince domain = new GeographicProvince(provinceIdCounter);
                    domain.tiles = pocket.ToList();
                    domain.provinceType = ProvinceType.Land;
                    domain.primaryBiome = GetPrimaryBiome(pocket);
                    domain.name = GenerateProvinceName(provinceIdCounter, domain.primaryBiome, domain.provinceType);

                    foreach (int tileId in pocket)
                    {
                        tileToProvinceId[tileId] = provinceIdCounter;
                    }
                    provinces.Add(domain);
                    provinceIdCounter++;
                }
            }
            // Phase 4.5: River Segmentation & Connection (Absorb rivers and small lakes into bordering Land/Ocean/Lake provinces)
            List<GeographicProvince> finalProvinces = provinces.Where(p => p.provinceType != ProvinceType.River).ToList();
            List<GeographicProvince> riverProvinces = provinces.Where(p => p.provinceType == ProvinceType.River).ToList();

            // Cache province types for O(1) lookups
            Dictionary<int, ProvinceType> provinceTypeMap = provinces.ToDictionary(p => p.id, p => p.provinceType);

            Dictionary<int, int> resolvedRiverTiles = new Dictionary<int, int>();
            List<int> unresolvedRiverTiles = new List<int>();

            foreach (var rp in riverProvinces)
            {
                foreach (int t in rp.tiles)
                {
                    unresolvedRiverTiles.Add(t);
                }
            }

            // Find all contiguous water bodies that are small (< 25 tiles) and mark them for absorption
            bool[] waterVisited = new bool[totalTiles];
            for (int i = 0; i < totalTiles; i++)
            {
                Tile tileData = Find.WorldGrid[i];
                if (!tileData.WaterCovered || waterVisited[i]) continue;

                List<int> waterBody = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                waterVisited[i] = true;

                List<RimWorld.Planet.PlanetTile> subNeighbors = new List<RimWorld.Planet.PlanetTile>();
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    waterBody.Add(current);

                    subNeighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(current, subNeighbors);
                    foreach (var n in subNeighbors)
                    {
                        if (!waterVisited[n.tileId] && Find.WorldGrid[n.tileId].WaterCovered)
                        {
                            waterVisited[n.tileId] = true;
                            queue.Enqueue(n.tileId);
                        }
                    }
                }

                // A water body is absorbed if it is an inland lake (contains no ocean biome tiles)
                bool isOcean = false;
                foreach (int tileId in waterBody)
                {
                    var biome = Find.WorldGrid[tileId].PrimaryBiome;
                    if (biome != null && (biome.defName.ToLower().Contains("ocean") || biome.LabelCap.ToString().ToLower().Contains("ocean")))
                    {
                        isOcean = true;
                        break;
                    }
                }

                if (!isOcean)
                {
                    // Per-water-body logging removed (one Log.Message per lake × many lakes = worldgen churn).
                    foreach (int tileId in waterBody)
                    {
                        unresolvedRiverTiles.Add(tileId);
                    }
                }
            }

            List<RimWorld.Planet.PlanetTile> connectNeighbors = new List<RimWorld.Planet.PlanetTile>();
            int connectSafety = 0;
            while (unresolvedRiverTiles.Count > 0 && connectSafety < 10)
            {
                connectSafety++;
                List<int> nextUnresolved = new List<int>();

                foreach (int t in unresolvedRiverTiles)
                {
                    connectNeighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, connectNeighbors);

                    int bestProvinceId = -1;
                    Dictionary<int, int> provWeights = new Dictionary<int, int>();

                    foreach (var n in connectNeighbors)
                    {
                        int nid = n.tileId;
                        int pid = tileToProvinceId[nid];
                        if (pid != -1)
                        {
                            if (provinceTypeMap.TryGetValue(pid, out var type) && type != ProvinceType.River)
                            {
                                if (!provWeights.ContainsKey(pid)) provWeights[pid] = 0;
                                provWeights[pid]++;
                            }
                        }
                    }

                    if (provWeights.Any())
                    {
                        bestProvinceId = provWeights.OrderByDescending(kv => kv.Value).First().Key;
                    }

                    if (bestProvinceId != -1)
                    {
                        resolvedRiverTiles[t] = bestProvinceId;
                        tileToProvinceId[t] = bestProvinceId;
                        // Also update the mapped type so that subsequent iterations can see it
                        provinceTypeMap[t] = provinceTypeMap[bestProvinceId];
                    }
                    else
                    {
                        nextUnresolved.Add(t);
                    }
                }

                if (nextUnresolved.Count == unresolvedRiverTiles.Count)
                {
                    // Force assign remaining unresolved tiles to any adjacent non-river province
                    foreach (int t in nextUnresolved)
                    {
                        connectNeighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(t, connectNeighbors);
                        foreach (var n in connectNeighbors)
                        {
                            int pid = tileToProvinceId[n.tileId];
                            if (pid != -1)
                            {
                                if (provinceTypeMap.TryGetValue(pid, out var type) && type != ProvinceType.River)
                                {
                                    resolvedRiverTiles[t] = pid;
                                    tileToProvinceId[t] = pid;
                                    break;
                                }
                            }
                        }
                    }
                    break;
                }

                unresolvedRiverTiles = nextUnresolved;
            }

            // Apply river tile absorption
            foreach (var kvp in resolvedRiverTiles)
            {
                int tileId = kvp.Key;
                int destProvinceId = kvp.Value;
                var destProv = finalProvinces.FirstOrDefault(p => p.id == destProvinceId);
                if (destProv != null)
                {
                    destProv.tiles.Add(tileId);
                    tileToProvinceId[tileId] = destProvinceId;
                }
            }

            provinces = finalProvinces;

            // Deduplicate tiles to ensure thread-safety for Map Mode Framework rendering
            HashSet<int> assignedTiles = new HashSet<int>();
            foreach (var p in provinces)
            {
                p.tiles = p.tiles.Distinct().ToList();
                List<int> uniqueTiles = new List<int>();
                foreach (int tileId in p.tiles)
                {
                    if (!assignedTiles.Contains(tileId))
                    {
                        assignedTiles.Add(tileId);
                        uniqueTiles.Add(tileId);
                        tileToProvinceId[tileId] = p.id;
                    }
                }
                p.tiles = uniqueTiles;
            }

            // Phase 5: Consolidation & Merging (Pass 2)
            Log.Message("[RegionsAndSocieties] Starting MergeTinyDomains...");
            MergeTinyDomains(minWithFeatures, minNoFeatures);
            Log.Message("[RegionsAndSocieties] Finished MergeTinyDomains.");

            // Phase 5b removed (#3/#49): the size cap is now enforced during growth by the value
            // budget in GrowTerrainBoundedRegions, so there is no oversized region to re-carve and no
            // crude carve to undo the terrain-following boundaries. EnforceMaxRegionSize is retained
            // as a method for now but no longer called.

            // Phase 5b2: fold impassable-mountain (and other unclaimed, non-water) pockets that are
            // fully enclosed by a single region INTO that region, so they read as owned terrain rather
            // than holes punched in the map (#3).
            AbsorbEnclosedGaps();

            // Phase 5c: relax jagged boundaries and swallow single-tile land islands.
            SmoothRegionBoundaries(5);

            // Naming Phase: Contextual Name Resolution
            Log.Message("[RegionsAndSocieties] Running contextual province naming...");
            ResolveContextualNames();

            // Aggregate the now-fixed topology once, so every later draw/ownership pass reads
            // perimeters and border shares instead of rescanning tiles (#48).
            BuildProvinceTopology();

            Log.Message($"[RegionsAndSocieties] Generated {provinces.Count} Geographic Domains.");
        }

        /// <summary>
        /// Fold unclaimed, non-water tile pockets that are fully enclosed by a single land region into
        /// that region (#3). Impassable mountains are excluded from region growth and otherwise sit as
        /// unowned holes; when such a pocket touches exactly one land region (and neither water nor an
        /// ocean province), it belongs to that region and is absorbed. A pocket bordering two or more
        /// regions is a genuine natural boundary and is left alone.
        /// </summary>
        private void AbsorbEnclosedGaps()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            int total = tileToProvinceId.Length;
            var visited = new bool[total];
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            int absorbed = 0;

            for (int t = 0; t < total; t++)
            {
                if (visited[t]) continue;
                visited[t] = true;
                if (tileToProvinceId[t] != -1) continue;
                if (Find.WorldGrid[t].WaterCovered) continue;   // water gaps are not "enclosed by land"

                // Flood the connected unclaimed, non-water pocket, recording which land regions ring it.
                var pocket = new List<int> { t };
                var queue = new Queue<int>();
                queue.Enqueue(t);
                var ringRegions = new HashSet<int>();
                bool openToWater = false;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        int npid = tileToProvinceId[nid];
                        if (npid == -1)
                        {
                            if (Find.WorldGrid[nid].WaterCovered) { openToWater = true; continue; }
                            if (!visited[nid]) { visited[nid] = true; pocket.Add(nid); queue.Enqueue(nid); }
                        }
                        else if (byId.TryGetValue(npid, out var np) && np.provinceType == ProvinceType.Land)
                        {
                            ringRegions.Add(npid);
                        }
                        else
                        {
                            openToWater = true;   // bordered by ocean / a water province
                        }
                    }
                }

                if (!openToWater && ringRegions.Count == 1)
                {
                    var region = byId[System.Linq.Enumerable.First(ringRegions)];
                    foreach (int c in pocket)
                    {
                        tileToProvinceId[c] = region.id;
                        region.tiles.Add(c);
                    }
                    absorbed += pocket.Count;
                }
            }

            if (absorbed > 0)
            {
                Log.Message($"[RegionsAndSocieties] Absorbed {absorbed} enclosed impassable/unclaimed tiles into their surrounding regions.");
            }
        }

        /// <summary>
        /// Curvature-smooth jagged region boundaries and swallow land islands (#3).
        ///
        /// <para>The value-budgeted growth follows terrain but leaves boundaries 3-4x longer than a
        /// compact shape would have, where two frontiers interlocked tile-by-tile. A majority-vote
        /// relaxation shortens them: a land tile wrapped more by a neighbouring region than by its own
        /// is reassigned to that neighbour, eroding convex spikes and filling concave notches while
        /// leaving straight and gently-curved edges (three-plus same-region neighbours) in place.
        /// Water, rivers and impassable tiles never count, so real coastlines are never rounded off.
        /// Iterating several passes lets multi-tile excursions resolve from the outside in.</para>
        /// </summary>
        private void SmoothRegionBoundaries(int passes)
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var landIds = new HashSet<int>(provinces
                .Where(p => p.provinceType == ProvinceType.Land)
                .Select(p => p.id));
            if (landIds.Count < 2) return;

            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var counts = new Dictionary<int, int>();

            for (int pass = 0; pass < passes; pass++)
            {
                var reassign = new Dictionary<int, int>();
                for (int t = 0; t < tileToProvinceId.Length; t++)
                {
                    int pid = tileToProvinceId[t];
                    if (pid < 0 || !landIds.Contains(pid)) continue;

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, neighbors);
                    counts.Clear();
                    int same = 0, landNeighbours = 0, bestId = -1, bestCount = 0;
                    foreach (var n in neighbors)
                    {
                        int np = tileToProvinceId[n.tileId];
                        if (np < 0 || !landIds.Contains(np)) continue;   // coast/river/impassable edge: keep it
                        landNeighbours++;
                        if (np == pid) { same++; continue; }
                        int c; counts.TryGetValue(np, out c); c++; counts[np] = c;
                        if (c > bestCount) { bestCount = c; bestId = np; }
                    }

                    // Curvature smoothing: if a neighbouring land region wraps this tile more than its
                    // own does (bestCount > same), the tile sits on a convex spike or in a concave
                    // notch of the boundary, and moving it there shortens the total border. Restricting
                    // to same <= 2 keeps straight and gently-curved edges (same >= 3) untouched, so only
                    // the jagged excursions erode. Iterated over several passes this pulls a 3-4x-too-
                    // long boundary down toward the compact ideal without flattening real coastlines
                    // (feature edges never count toward either tally).
                    if (landNeighbours >= 3 && same <= 2 && bestId != -1 && bestCount > same)
                        reassign[t] = bestId;
                }

                if (reassign.Count == 0) break;
                foreach (var kv in reassign) tileToProvinceId[kv.Key] = kv.Value;
            }

            // Rebuild land tile lists from the corrected map; water/river provinces are untouched
            // above so their lists stay valid. Drop any land province emptied by the relaxation.
            var byId = provinces.ToDictionary(p => p.id, p => p);
            foreach (var p in provinces)
                if (landIds.Contains(p.id)) p.tiles = new List<int>();
            for (int t = 0; t < tileToProvinceId.Length; t++)
            {
                int pid = tileToProvinceId[t];
                GeographicProvince prov;
                if (pid >= 0 && landIds.Contains(pid) && byId.TryGetValue(pid, out prov))
                    prov.tiles.Add(t);
            }
            provinces.RemoveAll(p => landIds.Contains(p.id) && p.tiles.Count == 0);
        }

        private bool ChunkHasNaturalFeatures(List<int> chunk)
        {
            HashSet<int> chunkSet = new HashSet<int>(chunk);
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();

            foreach (int tile in chunk)
            {
                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborId = n.tileId;
                    if (chunkSet.Contains(neighborId))
                    {
                        if (Find.WorldGrid.GetRiverDef(tile, neighborId) != null || Find.WorldGrid.GetRiverDef(neighborId, tile) != null)
                        {
                            return true;
                        }
                        if (Find.WorldGrid[tile].hilliness == Hilliness.Mountainous || Find.WorldGrid[neighborId].hilliness == Hilliness.Mountainous)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool RegionHasNaturalBoundaries(GeographicProvince p)
        {
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborId = n.tileId;
                    int neighborProvinceId = GetProvinceId(neighborId);
                    if (neighborProvinceId != -1 && neighborProvinceId != p.id)
                    {
                        bool crossesRiver = Find.WorldGrid.GetRiverDef(tile, neighborId) != null || Find.WorldGrid.GetRiverDef(neighborId, tile) != null;
                        bool crossesMountain = Find.WorldGrid[tile].hilliness == Hilliness.Mountainous || Find.WorldGrid[neighborId].hilliness == Hilliness.Mountainous;
                        if (crossesRiver || crossesMountain)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private List<List<int>> GetNaturalBasins()
        {
            int totalTiles = Find.WorldGrid.TilesCount;
            bool[] visited = new bool[totalTiles];
            List<List<int>> basins = new List<List<int>>();

            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();

            for (int t = 0; t < totalTiles; t++)
            {
                Tile tileData = Find.WorldGrid[t];
                if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable || (tileData.PrimaryBiome != null && tileData.PrimaryBiome.impassable))
                {
                    continue;
                }

                if (visited[t]) continue;

                List<int> basin = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(t);
                visited[t] = true;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    basin.Add(current);

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(current, neighbors);

                    foreach (var n in neighbors)
                    {
                        int neighborId = n.tileId;
                        if (visited[neighborId]) continue;

                        Tile neighborData = Find.WorldGrid[neighborId];

                        if (neighborData.WaterCovered || neighborData.hilliness == Hilliness.Impassable || (neighborData.PrimaryBiome != null && neighborData.PrimaryBiome.impassable))
                        {
                            continue;
                        }

                        // Do not cross river or mountain barriers during the initial basin flood fill
                        if (IsBoundary(current, neighborId))
                        {
                            continue;
                        }

                        visited[neighborId] = true;
                        queue.Enqueue(neighborId);
                    }
                }

                if (basin.Count > 0)
                {
                    basins.Add(basin);
                }
            }

            return basins;
        }

        private bool IsBoundary(int tileA, int tileB)
        {
            if (Find.WorldGrid == null) return false;

            // River check: Rivers act as primary boundaries
            if (Find.WorldGrid.GetRiverDef(tileA, tileB) != null || Find.WorldGrid.GetRiverDef(tileB, tileA) != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Split a land pocket into size-bounded regions whose boundaries follow terrain (#3). Each
        /// region grows from a seed via a cost-priority frontier — cheap across open land, expensive
        /// across mountains and rivers — so it fills the open basin first and crosses rough terrain
        /// last. Boundaries therefore fall along mountain ranges, and a narrow pass (expensive to
        /// cross) becomes the divide between two regions. Growth stops at <paramref name="maxSize"/>,
        /// so regions are inherently bounded and no post-hoc carve is required. Deterministic (seeds
        /// from sorted tile ids) so a world regenerates identically.
        /// </summary>
        private List<List<int>> GrowTerrainBoundedRegions(List<int> chunk, float pointCap)
        {
            var remaining = new HashSet<int>(chunk);
            var order = new List<int>(chunk);
            // Seed richest-first: a resource-rich tile anchors a region that grows out into the
            // cheaper surrounding land (including desert), so valuable pockets become region CORES
            // instead of stranded enclaves. Ties by id for deterministic regeneration (#3/#49).
            order.Sort((x, y) =>
            {
                int c = TilePoints(y).CompareTo(TilePoints(x));
                return c != 0 ? c : x.CompareTo(y);
            });
            var result = new List<List<int>>();
            var neighbors = new List<RimWorld.Planet.PlanetTile>();

            foreach (int start in order)
            {
                if (!remaining.Contains(start)) continue;

                var region = new List<int>();
                var regionSet = new HashSet<int>();
                float regionPoints = 0f;
                var costs = new Dictionary<int, float>();
                var claimedNbr = new Dictionary<int, int>();
                costs[start] = 0f;
                var pq = new SimplePriorityQueue<int>();
                pq.Enqueue(start, 0f);

                // Two-phase growth (#3). The VALUE budget still sets the rich-compact / barren-sprawl
                // character, but it is no longer a hard fill target:
                //   * Below minPoints (must-fill): annex the cheapest reachable tile no matter what —
                //     a value-starved core is allowed to reach across a river or ridge to become viable.
                //   * At/above minPoints (border-seeking): refuse to cross a natural feature. The region
                //     keeps flowing through connected open terrain until mountains, rivers and water
                //     enclose it, so the border snaps ONTO those features and crosses open gaps only at
                //     their narrowest (the pass). When nothing is left to annex without crossing a
                //     feature, the region ends EARLY — even below the cap — and the featureless interior
                //     beyond the enclosure is left unclaimed (no-man's-land) rather than split by an
                //     arbitrary bisector.
                float minPoints = pointCap * MinFillFraction;
                while (pq.Count > 0 && regionPoints < pointCap)
                {
                    int cur = pq.Dequeue();
                    if (!remaining.Contains(cur)) continue;   // claimed already (stale queue entry)

                    if (regionPoints >= minPoints)
                    {
                        // Border-seeking with a compactness gate (#3). Count the region's own tiles
                        // adjacent to this one through OPEN terrain (a feature edge does not count —
                        // that is the far side of a natural boundary and must stay the border). Annex
                        // only when at least CompactLinkMin such links exist: a single-link tile is a
                        // finger / tumour tip, and refusing it holds growth to broad advancing fronts,
                        // so regions stay compact (low perimeter) instead of snaking or sprouting lobes
                        // while still hugging features. Below minPoints this gate is off, so a starved
                        // core may still reach out to become viable.
                        int openLinks = 0;
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                        foreach (var n in neighbors)
                        {
                            if (regionSet.Contains(n.tileId) && TerrainStepCost(cur, n.tileId) < BarrierStepCost)
                            {
                                openLinks++;
                                if (openLinks >= CompactLinkMin) break;
                            }
                        }
                        if (openLinks < CompactLinkMin) continue;
                    }

                    remaining.Remove(cur);
                    region.Add(cur);
                    regionSet.Add(cur);
                    regionPoints += TilePoints(cur);

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        if (!remaining.Contains(nid)) continue;

                        // cur just joined the region, so nid gained one more claimed neighbour.
                        int cn;
                        claimedNbr.TryGetValue(nid, out cn);
                        cn++;
                        claimedNbr[nid] = cn;

                        float terrain = costs[cur] + TerrainStepCost(cur, nid);
                        float existing;
                        if (!costs.TryGetValue(nid, out existing) || terrain < existing)
                            costs[nid] = terrain;

                        // Priority = distance from the seed plus a compactness penalty for every
                        // still-unclaimed neighbour: a tile boxed in by the region (few unclaimed
                        // neighbours) is annexed before a tile that would poke out a finger. Re-enqueued
                        // each time a neighbour is claimed so the penalty falls as the notch fills;
                        // stale duplicates are skipped by the remaining-set check at dequeue. Barrier
                        // avoidance is the gate above, not here, so this only reshapes the open frontier.
                        float priority = costs[nid] + CompactWeight * (6 - cn);
                        pq.Enqueue(nid, priority);
                    }
                }
                result.Add(region);
            }
            return result;
        }

        /// <summary>
        /// A tile's contribution to a region's size budget, in value points. Fertile and mineral-rich
        /// land is worth more, so a region fills its budget in fewer tiles (compact); barren waste is
        /// worth little, so regions there sprawl; water is near-free so it is absorbed without
        /// spending the budget. The small floor keeps growth terminating even over zero-value water,
        /// and makes region size a function of value rather than area (#49).
        /// </summary>
        private static float TilePoints(int tile)
        {
            Tile t = Find.WorldGrid[tile];
            if (t.WaterCovered) return 0.05f;   // near-free: a coastal region absorbs its water for almost nothing
            float value = 0f;
            BiomeDef b = t.PrimaryBiome;
            if (b != null) value += b.plantDensity + b.forageability;   // fertility / food
            switch (t.hilliness)                                        // mineral potential
            {
                case Hilliness.SmallHills:  value += 0.25f; break;
                case Hilliness.LargeHills:  value += 0.45f; break;
                case Hilliness.Mountainous: value += 0.65f; break;
                default:                    value += 0.10f; break;
            }
            return Mathf.Max(value, 0.05f);
        }

        /// <summary>Cheap over open land, expensive across mountains and rivers, so a growing region
        /// fills its basin before spilling over rough terrain — the mechanism that puts region
        /// boundaries on natural barriers (#3).</summary>
        private static float TerrainStepCost(int a, int b)
        {
            float cost = 1f;
            Tile ta = Find.WorldGrid[a];
            Tile tb = Find.WorldGrid[b];
            bool rough = ta.hilliness == Hilliness.LargeHills || ta.hilliness == Hilliness.Mountainous
                      || tb.hilliness == Hilliness.LargeHills || tb.hilliness == Hilliness.Mountainous;
            if (rough) cost += 12f;
            if (ta.WaterCovered || tb.WaterCovered) cost += 12f;   // water is a barrier too — regions don't spread across it
            if (Find.WorldGrid.GetRiverDef(a, b) != null || Find.WorldGrid.GetRiverDef(b, a) != null) cost += 12f;
            return cost;
        }

        private List<List<int>> SplitChunkByVoronoi(List<int> chunk)
        {
            HashSet<int> chunkSet = new HashSet<int>(chunk);
            int size = chunk.Count(t => IsTileUsable(t));

            float targetSize = (FactionPlacementSettings.minRegionSize + FactionPlacementSettings.maxRegionSize) / 2f;
            int k = Mathf.CeilToInt((float)size / targetSize);
            if (k < 2) k = 2;

            List<int> seeds = new List<int>();
            if (k > 0 && chunk.Count > 0)
            {
                seeds.Add(chunk[0]);

                while (seeds.Count < k)
                {
                    int bestTile = -1;
                    float maxMinDist = -1f;

                    int sampleStep = Mathf.Max(1, chunk.Count / 300);
                    for (int i = 0; i < chunk.Count; i += sampleStep)
                    {
                        int tile = chunk[i];
                        if (seeds.Contains(tile)) continue;

                        float minDist = float.MaxValue;
                        foreach (int seed in seeds)
                        {
                            float dist = Find.WorldGrid.ApproxDistanceInTiles(tile, seed);
                            if (dist < minDist)
                            {
                                minDist = dist;
                            }
                        }

                        if (minDist > maxMinDist)
                        {
                            maxMinDist = minDist;
                            bestTile = tile;
                        }
                    }

                    if (bestTile != -1)
                    {
                        seeds.Add(bestTile);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var tileToSeed = new Dictionary<int, int>();
            var minCosts = new Dictionary<int, float>();
            var pq = new SimplePriorityQueue<int>();

            foreach (int seed in seeds)
            {
                minCosts[seed] = 0f;
                tileToSeed[seed] = seed;
                pq.Enqueue(seed, 0f);
            }

            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();

            while (pq.Count > 0)
            {
                int current = pq.Dequeue();
                float currentCost = minCosts[current];
                int seed = tileToSeed[current];

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(current, neighbors);

                foreach (var n in neighbors)
                {
                    int neighborId = n.tileId;
                    if (!chunkSet.Contains(neighborId)) continue;

                    Tile currentData = Find.WorldGrid[current];
                    Tile neighborData = Find.WorldGrid[neighborId];

                    float stepCost = 1.0f;

                    if (currentData.hilliness == Hilliness.Mountainous || currentData.hilliness == Hilliness.LargeHills ||
                        neighborData.hilliness == Hilliness.Mountainous || neighborData.hilliness == Hilliness.LargeHills)
                    {
                        stepCost += 100f;
                    }

                    if (Find.WorldGrid.GetRiverDef(current, neighborId) != null || Find.WorldGrid.GetRiverDef(neighborId, current) != null)
                    {
                        stepCost += 100f;
                    }

                    float newCost = currentCost + stepCost;

                    if (!minCosts.TryGetValue(neighborId, out float existingCost) || newCost < existingCost)
                    {
                        minCosts[neighborId] = newCost;
                        tileToSeed[neighborId] = seed;
                        pq.Enqueue(neighborId, newCost);
                    }
                }
            }

            var groups = new Dictionary<int, List<int>>();
            foreach (int seed in seeds)
            {
                groups[seed] = new List<int>();
            }

            foreach (int tile in chunk)
            {
                if (tileToSeed.TryGetValue(tile, out int seed))
                {
                    groups[seed].Add(tile);
                }
                else
                {
                    groups[seeds[0]].Add(tile);
                }
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// Guarantees no land region exceeds the configured size (#49). Any region whose total tile
        /// count is over the cap (plus the same slack the feature-rich split path allows) is re-carved
        /// into connected, size-bounded pockets; the first stays as the region, the rest become new
        /// ones. Only oversized regions are touched, so well-split regions keep their natural borders.
        /// </summary>
        private void EnforceMaxRegionSize(int baseMax)
        {
            int hardCap = baseMax + 30;
            var oversized = provinces
                .Where(p => p.provinceType == ProvinceType.Land && !p.IsBarren && p.tiles != null && p.tiles.Count > hardCap)
                .ToList();
            if (oversized.Count == 0) return;

            int nextId = provinces.Count > 0 ? provinces.Max(p => p.id) + 1 : 0;
            int added = 0;

            foreach (var big in oversized)
            {
                var pockets = CarveIntoSizedPockets(big.tiles, hardCap);
                if (pockets.Count <= 1) continue;

                big.tiles = pockets[0];
                foreach (int t in big.tiles) tileToProvinceId[t] = big.id;

                for (int i = 1; i < pockets.Count; i++)
                {
                    var np = new GeographicProvince(nextId++);
                    np.tiles = pockets[i];
                    np.provinceType = ProvinceType.Land;
                    np.primaryBiome = GetPrimaryBiome(pockets[i]);
                    np.name = GenerateProvinceName(np.id, np.primaryBiome, np.provinceType);
                    foreach (int t in pockets[i]) tileToProvinceId[t] = np.id;
                    provinces.Add(np);
                    added++;
                }
            }

            Log.Message($"[RegionsAndSocieties] EnforceMaxRegionSize: re-carved {oversized.Count} oversized region(s) into {added} additional region(s) (cap {hardCap}).");
        }

        /// <summary>
        /// Carve a tile set into connected pockets, each at most <paramref name="capTotal"/> tiles, by
        /// repeated bounded breadth-first growth. Deterministic (starts from sorted tile ids) so a
        /// world regenerates identically.
        /// </summary>
        private List<List<int>> CarveIntoSizedPockets(List<int> tiles, int capTotal)
        {
            var remaining = new HashSet<int>(tiles);
            var order = new List<int>(tiles);
            order.Sort();
            var result = new List<List<int>>();
            var neighbors = new List<RimWorld.Planet.PlanetTile>();

            foreach (int startCandidate in order)
            {
                if (!remaining.Contains(startCandidate)) continue;

                var pocket = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(startCandidate);
                remaining.Remove(startCandidate);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    pocket.Add(cur);
                    if (pocket.Count >= capTotal)
                    {
                        while (queue.Count > 0) remaining.Add(queue.Dequeue());   // return the frontier to the pool
                        break;
                    }
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        if (remaining.Remove(n.tileId)) queue.Enqueue(n.tileId);
                    }
                }
                result.Add(pocket);
            }
            return result;
        }

        /// <summary>Usable-tile count for a province, as an allocation-free loop (no LINQ closure).
        /// Called in the tight merge loop, where a per-call Count(predicate) closure was a memory sink.</summary>
        private int UsableTileCount(GeographicProvince p)
        {
            if (p?.tiles == null) return 0;
            int count = 0;
            List<int> tiles = p.tiles;
            for (int i = 0; i < tiles.Count; i++)
                if (IsTileUsable(tiles[i])) count++;
            return count;
        }

        private void MergeTinyDomains(int minWithFeatures, int minNoFeatures)
        {
            Log.Message($"[RegionsAndSocieties] MergeTinyDomains started. Initial region count: {provinces.Count}");
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            // Cache province types
            var provinceTypeMap = provinces.ToDictionary(p => p.id, p => p.provinceType);

            // Pass 0: Small Island Absorption (islands < 5 tiles, closest landmass < 3 tiles away)
            List<GeographicProvince> islandsToRemove = new List<GeographicProvince>();
            var initialProvinceMap = provinces.ToDictionary(p => p.id, p => p);
            int totalMerged = 0;

            foreach (var p in provinces)
            {
                if (p.provinceType == ProvinceType.Land && p.tiles.Count > 0 && p.tiles.Count < 5)
                {
                    int targetPid = FindClosestLandProvinceWithinDistance(p, 2, provinceTypeMap);
                    if (targetPid != -1 && initialProvinceMap.TryGetValue(targetPid, out var targetProv))
                    {
                        // Per-merge logging removed: a full world has thousands of tiny islands, and one
                        // Log.Message each stalled worldgen and ballooned memory until it crashed. The
                        // one-line summary at the end of MergeTinyDomains reports the total instead.
                        foreach (int tileId in p.tiles)
                        {
                            targetProv.tiles.Add(tileId);
                            tileToProvinceId[tileId] = targetProv.id;
                        }
                        islandsToRemove.Add(p);
                        totalMerged++;
                    }
                }
            }

            foreach (var p in islandsToRemove)
            {
                provinces.Remove(p);
            }

            int pass = 0;
            while (pass < 10) // Safety limit of 10 passes
            {
                pass++;
                bool mergedAnyInThisPass = false;
                // HashSet, not List: Contains() is hit once per province per pass, and an O(n) list scan
                // over 1000+ provinces across 10 passes was an O(n²) stall that helped starve worldgen.
                HashSet<GeographicProvince> toRemove = new HashSet<GeographicProvince>();

                // Build a quick map of province ID to the actual province object
                var provinceMap = provinces.ToDictionary(p => p.id, p => p);

                foreach (var p in provinces)
                {
                    if (p.provinceType == ProvinceType.Ocean) continue;
                    if (toRemove.Contains(p)) continue;

                    int pSize = p.tiles.Count;
                    bool isFeature = p.provinceType == ProvinceType.River || p.provinceType == ProvinceType.Lake || p.provinceType == ProvinceType.MountainRange;
                    int baseThreshold = isFeature ? 30 : minNoFeatures;

                    // Scale threshold dynamically based on tile resource density
                    float resWeight = GetResourceWeight(p);
                    float scale = Mathf.Clamp(1.5f / Mathf.Max(resWeight, 0.1f), 1f, 5f);
                    int threshold = Mathf.RoundToInt(baseThreshold * scale);

                    if (pSize >= threshold) continue;

                    // Find adjacent neighbors
                    Dictionary<int, int> neighborWeights = new Dictionary<int, int>();

                    foreach (int tile in p.tiles)
                    {
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                        foreach (var n in neighbors)
                        {
                            int neighborId = n.tileId;
                            int neighborProvinceId = GetProvinceId(neighborId);
                            if (neighborProvinceId != -1 && neighborProvinceId != p.id)
                            {
                                // If the neighbor province was already marked to be removed in this pass, ignore it
                                if (provinceMap.TryGetValue(neighborProvinceId, out var neighborProv))
                                {
                                    if (neighborProv.provinceType == ProvinceType.Ocean || toRemove.Contains(neighborProv)) continue;

                                    int weight = 1;
                                    if (neighborProv.provinceType == ProvinceType.Land)
                                    {
                                        weight = 100;
                                    }

                                    if (!neighborWeights.ContainsKey(neighborProvinceId))
                                    {
                                        neighborWeights[neighborProvinceId] = 0;
                                    }
                                    neighborWeights[neighborProvinceId] += weight;
                                }
                            }
                        }
                    }

                    if (neighborWeights.Any())
                    {
                        var sortedNeighbors = neighborWeights.OrderByDescending(kv => kv.Value).ToList();
                        GeographicProvince bestNeighbor = null;
                        GeographicProvince dominantLand = null;

                        // Compute p's usable-tile count once, not once per neighbour: the per-neighbour
                        // LINQ Count(predicate) allocated a closure every call and was the small-allocation
                        // storm the OOM crash dump showed.
                        int pUsable = UsableTileCount(p);

                        foreach (var kvp in sortedNeighbors)
                        {
                            if (provinceMap.TryGetValue(kvp.Key, out var neighborProv))
                            {
                                // Remember the highest-weight (most shared edges) land neighbour as a
                                // rescue target, regardless of the size cap.
                                if (dominantLand == null && neighborProv.provinceType == ProvinceType.Land)
                                    dominantLand = neighborProv;

                                if (UsableTileCount(neighborProv) + pUsable <= FactionPlacementSettings.maxRegionSize + 50)
                                {
                                    bestNeighbor = neighborProv;
                                    break;
                                }
                            }
                        }

                        // Orphan rescue (#3): a small pocket whose neighbours are all already at or past
                        // the size cap — the common case next to a low-value tundra/desert region that
                        // sprawled to ~200 tiles under the value budget — would otherwise survive as an
                        // arbitrary leftover island (no natural border, sometimes the same faction as the
                        // neighbour). Fold it into its dominant land neighbour anyway; a slightly
                        // oversized low-value region reads far better than a stranded gap. Bounded to
                        // genuinely small pockets so this never chains medium regions into a monster.
                        if (bestNeighbor == null && dominantLand != null &&
                            p.tiles.Count <= FactionPlacementSettings.minRegionSize / 2)
                        {
                            bestNeighbor = dominantLand;
                        }

                        if (bestNeighbor != null)
                        {
                            // Merge p into bestNeighbor
                            foreach (int tileId in p.tiles)
                            {
                                bestNeighbor.tiles.Add(tileId);
                                tileToProvinceId[tileId] = bestNeighbor.id;
                            }
                            toRemove.Add(p);
                            mergedAnyInThisPass = true;
                            totalMerged++;
                        }
                    }
                }

                if (!mergedAnyInThisPass)
                {
                    break;
                }

                // Remove the merged provinces
                foreach (var p in toRemove)
                {
                    provinces.Remove(p);
                }
            }

            Log.Message($"[RegionsAndSocieties] MergeTinyDomains finished. Merged {totalMerged} regions in {pass} passes. Final region count: {provinces.Count}");
        }

        private float GetResourceWeight(GeographicProvince p)
        {
            if (p.tiles == null || p.tiles.Count == 0 || Find.WorldGrid == null) return 1.0f;
            float total = 0f;
            foreach (int tileId in p.tiles)
            {
                Tile t = Find.WorldGrid[tileId];
                var b = t.PrimaryBiome;
                if (b != null)
                {
                    total += b.plantDensity + b.forageability + b.TreeDensity;
                }
                if (t.hilliness == Hilliness.SmallHills) total += 0.5f;
                else if (t.hilliness == Hilliness.LargeHills) total += 1.0f;
                else if (t.hilliness == Hilliness.Mountainous) total += 1.5f;
            }
            return total / p.tiles.Count;
        }

        private int FindClosestLandProvinceWithinDistance(GeographicProvince island, int maxDistance, Dictionary<int, ProvinceType> provinceTypeMap)
        {
            Queue<KeyValuePair<int, int>> queue = new Queue<KeyValuePair<int, int>>();
            HashSet<int> visited = new HashSet<int>();

            foreach (int t in island.tiles)
            {
                queue.Enqueue(new KeyValuePair<int, int>(t, 0));
                visited.Add(t);
            }

            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();

            while (queue.Count > 0)
            {
                var currentKvp = queue.Dequeue();
                int currentTile = currentKvp.Key;
                int currentDepth = currentKvp.Value;

                if (currentDepth > maxDistance) continue;

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(currentTile, neighbors);
                foreach (var n in neighbors)
                {
                    int nid = n.tileId;
                    if (visited.Contains(nid)) continue;
                    visited.Add(nid);

                    int pid = tileToProvinceId[nid];
                    if (pid != -1 && pid != island.id)
                    {
                        if (provinceTypeMap.TryGetValue(pid, out var type) && type == ProvinceType.Land)
                        {
                            return pid;
                        }
                    }

                    if (Find.WorldGrid[nid].WaterCovered && currentDepth < maxDistance)
                    {
                        queue.Enqueue(new KeyValuePair<int, int>(nid, currentDepth + 1));
                    }
                }
            }

            return -1;
        }

        private void ResolveContextualNames()
        {
            if (Find.WorldFeatures == null || Find.WorldFeatures.features.NullOrEmpty()) return;

            // Cache centroids of all vanilla WorldFeatures
            var featureCentroids = new Dictionary<WorldFeature, Vector3>();
            foreach (var wf in Find.WorldFeatures.features)
            {
                if (!wf.Tiles.Any()) continue;
                Vector3 center = Vector3.zero;
                foreach (int t in wf.Tiles)
                {
                    center += Find.WorldGrid.GetTileCenter(t);
                }
                featureCentroids[wf] = center / wf.Tiles.Count();
            }

            foreach (var province in provinces)
            {
                if (province.tiles.Count == 0) continue;

                // Calculate province centroid
                Vector3 provinceCenter = Vector3.zero;
                foreach (int t in province.tiles)
                {
                    provinceCenter += Find.WorldGrid.GetTileCenter(t);
                }
                provinceCenter /= province.tiles.Count;

                // Find the closest WorldFeature
                WorldFeature closestFeature = null;
                float minSqrDist = float.MaxValue;
                foreach (var kvp in featureCentroids)
                {
                    float sqrDist = (provinceCenter - kvp.Value).sqrMagnitude;
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        closestFeature = kvp.Key;
                    }
                }

                if (closestFeature != null)
                {
                    // If directly overlapping a vanilla feature, use its name — but land regions keep the
                    // simple "Region <id>" / "<settlement> Region" scheme (0.7.3); only water/mountain
                    // features carry a geographic name.
                    var directOverlap = Find.WorldFeatures.features
                        .FirstOrDefault(wf => wf.Tiles.Any(t => province.tiles.Contains(t)));

                    if (directOverlap != null && province.provinceType != ProvinceType.Land)
                    {
                        province.name = directOverlap.name;
                    }
                    else
                    {
                        // Infer name based on closest feature
                        if (province.provinceType == ProvinceType.Lake)
                        {
                            province.name = closestFeature.name.Contains("Lake") || closestFeature.name.Contains("Sea") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Lake";
                        }
                        else if (province.provinceType == ProvinceType.Ocean)
                        {
                            province.name = closestFeature.name.Contains("Ocean") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Ocean";
                        }
                        else if (province.provinceType == ProvinceType.MountainRange)
                        {
                            province.name = closestFeature.name.Contains("Mountains") || closestFeature.name.Contains("Range") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Mountains";
                        }
                        else if (province.provinceType == ProvinceType.River)
                        {
                            province.name = GenerateRiverName(province.id, closestFeature.name);
                        }
                    }
                }
            }
        }

        private string GenerateRiverName(int id, string nearbyFeatureName)
        {
            var prefixes = new[] { "Silent", "Whispering", "Shimmering", "Roaring", "Winding", "Deep", "Swift", "Cold", "Grey", "Green", "Red", "Silver", "Golden", "Muddy", "Black", "Wild", "Broad", "Shadow", "Serpent", "Ghost", "Sun", "Moon", "Star", "Glimmering", "Ember", "Frost" };
            var suffixes = new[] { "River", "Creek", "Flow", "Fork", "Run", "Torrent", "Stream", "Waters", "Channel" };

            System.Random rand = new System.Random(id * 79 + 37);

            // 50% chance to name after nearby feature, 50% to generate a generic beautiful name
            if (rand.NextDouble() < 0.5f && !string.IsNullOrEmpty(nearbyFeatureName))
            {
                string cleanName = nearbyFeatureName
                    .Replace("Mountains", "")
                    .Replace("Mountain Range", "")
                    .Replace("Scrubland", "")
                    .Replace("Scrublands", "")
                    .Replace("Forest", "")
                    .Replace("Tangle", "")
                    .Replace("Basin", "")
                    .Replace("Swamp", "")
                    .Replace("Bog", "")
                    .Trim();

                string suffix = suffixes[rand.Next(suffixes.Length)];
                return $"{cleanName} {suffix}";
            }
            else
            {
                string prefix = prefixes[rand.Next(prefixes.Length)];
                string suffix = suffixes[rand.Next(suffixes.Length)];
                return $"{prefix} {suffix}";
            }
        }

        private string GenerateProvinceName(int provinceId, BiomeDef biome, ProvinceType type)
        {
            if (type == ProvinceType.Ocean) return "Ocean Region " + provinceId;
            if (type == ProvinceType.Lake) return "Lake Region " + provinceId;
            if (type == ProvinceType.River) return "River Region " + provinceId;
            if (type == ProvinceType.MountainRange) return "Mountain Region " + provinceId;

            return GenerateProvinceName(provinceId, biome);
        }

        private string GenerateProvinceName(int provinceId, BiomeDef biome)
        {
            // 0.7.3: a land region is simply "Region <id>". If it holds a settlement, RecalculateProvinceOwners
            // renames it "<settlement> Region"; the id is always shown in the expanded region details.
            return "Region " + provinceId;
        }

        /// <summary>
        /// 0.7.3 naming: a land region is named after the settlement standing in it ("&lt;settlement&gt; Region"),
        /// or "Region &lt;id&gt;" when it holds none. Called each ownership recompute so the name tracks a
        /// settlement being founded or lost. Water/mountain regions keep their geographic feature names.
        /// The id itself is always shown in the expanded region details, independent of the name.
        /// </summary>
        private void UpdateProvinceName(GeographicProvince province, List<RimWorld.Planet.WorldObject> regionObjects)
        {
            if (province == null || province.provinceType != ProvinceType.Land) return;

            RimWorld.Planet.WorldObject settlement = null;
            if (regionObjects != null)
            {
                foreach (var o in regionObjects)
                {
                    if (o != null && o.Faction != null && Integration.WorldObjectClassifier.IsSettlement(o)) { settlement = o; break; }
                }
            }
            province.name = settlement != null ? $"{settlement.LabelCap} Region" : "Region " + province.id;
        }

        private bool topologyBuilt;

        /// <summary>
        /// Precompute every province's perimeter tiles and per-neighbour border-edge counts in a
        /// single pass over the world grid. Province topology is fixed once the provinces exist, so
        /// this runs once — at generation, and rebuilt lazily after a load — and every later
        /// perimeter/border query reads the aggregate instead of rescanning tiles. Replaces the
        /// per-call flood-fill in <see cref="RegionalOwnershipUtility.GetPerimeterTiles"/> and gives
        /// the border-share data the ownership scoring consumes.
        /// </summary>
        public void BuildProvinceTopology()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            // Local id map: GetProvince is an O(provinces) scan, so calling it per tile would make
            // this O(tiles * provinces).
            var byId = new Dictionary<int, GeographicProvince>(provinces.Count);
            foreach (var p in provinces)
            {
                p.perimeterTiles = new List<int>();
                p.borderShares = new Dictionary<int, int>();
                p.perimeterEdgeCount = 0;
                p.naturalBorderEdges = 0;
                byId[p.id] = p;
            }

            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            for (int t = 0; t < tileToProvinceId.Length; t++)
            {
                int pid = tileToProvinceId[t];
                if (pid < 0) continue;
                GeographicProvince prov;
                if (!byId.TryGetValue(pid, out prov)) continue;

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(t, neighbors);
                bool boundary = false;
                foreach (var n in neighbors)
                {
                    int npid = tileToProvinceId[n.tileId];
                    if (npid == pid) continue;      // interior edge
                    boundary = true;
                    prov.perimeterEdgeCount++;

                    // A frontier against water or an impassable mountain is a secure natural border —
                    // it counts for this region's own owner, not as a contestable land border (#44).
                    Tile nt = Find.WorldGrid[n.tileId];
                    bool naturalBarrier = nt.WaterCovered || nt.hilliness == Hilliness.Impassable
                        || (nt.PrimaryBiome != null && nt.PrimaryBiome.impassable);
                    if (naturalBarrier)
                    {
                        prov.naturalBorderEdges++;
                    }
                    else if (npid >= 0)             // contestable edge to another land province
                    {
                        int c;
                        prov.borderShares.TryGetValue(npid, out c);
                        prov.borderShares[npid] = c + 1;
                    }
                    // else: unassigned non-natural land (rare) — not counted
                }
                if (boundary) prov.perimeterTiles.Add(t);
            }
            topologyBuilt = true;
        }

        /// <summary>Build the topology aggregate once per session (covers both generation and load).</summary>
        public void EnsureTopology()
        {
            if (!topologyBuilt) BuildProvinceTopology();
        }

        private static readonly List<RimWorld.Planet.WorldObject> EmptyWorldObjects = new List<RimWorld.Planet.WorldObject>();

        // Bumped whenever a territorial holding (settlement/outpost/military/camp) is added or
        // removed — the only world-object changes that alter ownership. Static so it survives the
        // fresh component a load creates and so the PostAdd/PostRemove patch can bump it without an
        // instance. Population changes (which do not affect ownership) deliberately do not bump it.
        private static int ownershipEpoch;
        public static void BumpOwnershipEpoch()
        {
            ownershipEpoch++;
            // A territorial holding changed, so the global border overlay's per-region colours are stale.
            // Bump its build version so the world layer rebuilds its mesh next frame (#72). Without this the
            // overlay — which only rebuilds on a version change — keeps whatever colours it first painted.
            UI.RegionBorderOverlay.Invalidate();
        }

        private int ownersComputedVersion = -1;
        private int ownersFactionCount = -1;

        /// <summary>Force the next <see cref="RecalculateProvinceOwners"/> to recompute rather than
        /// reuse the cache — for inputs the epoch/count gate does not observe (e.g. a demographic
        /// provider registering, or a settlement changing faction without an add/remove).</summary>
        public void MarkOwnersDirty()
        {
            ownersComputedVersion = -1;
        }

        public void RecalculateProvinceOwners()
        {
            if (Find.WorldObjects == null || provinces == null) return;
            EnsureTopology();

            // Ownership depends only on the territorial holdings present (add/remove bumps
            // ownershipEpoch) and on which factions exist (defeat/creation changes the count). When
            // neither has changed since the last pass the cached ownershipData/owningFactionIds are
            // still valid, so the entire recompute — bucketing, perimeter owner mapping, scoring — is
            // skipped. This is what turns "recompute on every draw" into "recompute only on change"
            // (#48). MarkOwnersDirty covers the inputs this gate cannot see.
            int epoch = ownershipEpoch;
            int factionCount = Find.FactionManager?.AllFactionsListForReading?.Count ?? 0;
            if (ownersComputedVersion == epoch && ownersFactionCount == factionCount) return;
            ownersComputedVersion = epoch;
            ownersFactionCount = factionCount;

            // Bucket every world object into its province in one pass (O(worldObjects)), so each
            // province's ownership reads its own objects instead of filtering AllWorldObjects with a
            // List.Contains over its tiles — which was O(worldObjects * tiles) per province (#48).
            var objectsByProvince = new Dictionary<int, List<RimWorld.Planet.WorldObject>>();
            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj == null) continue;
                int opid = GetProvinceId(obj.Tile);
                if (opid < 0) continue;
                List<RimWorld.Planet.WorldObject> bucket;
                if (!objectsByProvince.TryGetValue(opid, out bucket))
                {
                    bucket = new List<RimWorld.Planet.WorldObject>();
                    objectsByProvince[opid] = bucket;
                }
                bucket.Add(obj);
            }

            // Pass 1: each province's ownership from its own holdings only, plus its dominant owner —
            // what neighbours read when computing their border scores.
            var ownerByProvince = new Dictionary<int, Faction>(provinces.Count);
            foreach (var province in provinces)
            {
                List<RimWorld.Planet.WorldObject> regionObjects;
                if (!objectsByProvince.TryGetValue(province.id, out regionObjects)) regionObjects = EmptyWorldObjects;
                province.ownershipData = RegionalOwnershipUtility.CalculateOwnershipBase(province, regionObjects);
                ownerByProvince[province.id] = RegionalOwnershipUtility.DominantBaseOwner(province.ownershipData);
                UpdateProvinceName(province, regionObjects);
            }

            // Pass 2: fold in border influence from neighbours' owners over the static borderShares,
            // normalize, and publish the owning-faction list. The geometry is precomputed, so this is
            // where "region 487 changed owner -> recompute 326's borders" stays cheap (#44).
            foreach (var province in provinces)
            {
                RegionalOwnershipUtility.ApplyBordersAndNormalize(province.ownershipData, province, ownerByProvince);

                province.owningFactionIds.Clear();
                var data = province.ownershipData;
                if (data != null && data.factionScores != null)
                {
                    foreach (var fs in data.factionScores)
                    {
                        if (fs.faction != null && fs.TotalScore > Placement.PlacementRules.PresenceFloor)
                        {
                            string fid = fs.faction.GetUniqueLoadID();
                            if (!province.owningFactionIds.Contains(fid))
                            {
                                province.owningFactionIds.Add(fid);
                            }
                        }
                    }
                }
            }
        }

        public bool AreProvincesAdjacent(GeographicProvince a, GeographicProvince b)
        {
            if (a == null || b == null) return false;
            if (a.id == b.id) return true;

            // Check if any tile in 'a' shares a neighbor with any tile in 'b'
            foreach (int tileA in a.tiles)
            {
                foreach (int tileB in b.tiles)
                {
                    if (Find.WorldGrid.IsNeighbor(tileA, tileB))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsTileUsable(int tileId)
        {
            if (Find.WorldGrid == null) return false;
            Tile tileData = Find.WorldGrid[tileId];
            if (tileData == null) return false;
            if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable) return false;
            if (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")) return false;
            return true;
        }
    }
}
