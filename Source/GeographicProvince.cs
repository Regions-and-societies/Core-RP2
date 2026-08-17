using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Economy;
using Verse;

namespace RegionsAndSocieties
{
    public enum ProvinceType { Land, River, MountainRange, Lake, Ocean }

    public class SettlementCrisis : IExposable
    {
        public string crisisType; // e.g., "Blight"
        public float currentSeverity;
        public int ticksRemaining; // or days elapsed
        public int daysElapsed;

        public void ExposeData()
        {
            Scribe_Values.Look(ref crisisType, "crisisType");
            Scribe_Values.Look(ref currentSeverity, "currentSeverity", 0f);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Values.Look(ref daysElapsed, "daysElapsed", 0);
        }
    }

    public class GeographicProvince : IExposable
    {
        public int id;
        public List<int> tiles = new List<int>();
        public string name;
        public BiomeDef primaryBiome;
        public List<string> owningFactionIds = new List<string>();
        public RegionalOwnershipData ownershipData;
        public ProvinceType provinceType = ProvinceType.Land;

        // --- Topology aggregate (#48) -------------------------------------------
        // Derived purely from tile membership, so it is deliberately never scribed:
        // SynapseRegionManager.BuildProvinceTopology fills it once (at generation, and lazily after
        // load) and every perimeter/border query reads it instead of rescanning tiles. This is the
        // "aggregate the region into one object and query it" model — the same one the resource
        // pools below already follow.
        public List<int> perimeterTiles;             // boundary tiles of this province
        public Dictionary<int, int> borderShares;    // neighbour province id -> count of shared boundary edges
        public int perimeterEdgeCount;               // total boundary edges, including edges to water / unassigned
        public int naturalBorderEdges;               // edges against water / impassable mountains — secure frontiers for this region's own owner

        // --- Economics / Demographics ---
        public bool initializedEconomics;

        // Population and dwellings are a materialized aggregate: summed once from the per-tile
        // density cache and re-summed only when that cache is invalidated (a population-bearing
        // world object added or removed), tracked by PopulationDensityUtility.CacheVersion. The old
        // getters re-summed every tile on every read (dwellings) or cached without ever
        // invalidating (population, which went stale). _populationVersion is not scribed, so the
        // first read after a load recomputes once.
        private int _totalDwellings;
        private int _currentPopulation;
        private int _populationVersion = -1;

        private void EnsurePopulationAggregate()
        {
            int version = PopulationDensityUtility.CacheVersion;
            if (_populationVersion == version) return;

            int total = 0;
            if (tiles != null)
            {
                foreach (int tileId in tiles)
                {
                    // Source population, not the smeared influence field: summing the smear counted
                    // every settlement's decayed reach in every tile it touched, so a province's total
                    // ran far above the number of pawns actually living in it (#55).
                    total += PopulationDensityUtility.GetSourcePopulationAtTile(tileId);
                }
            }
            _currentPopulation = total;
            _totalDwellings = total <= 0 ? 0 : System.Math.Max(1, total / 2);
            _populationVersion = version;
        }

        public int totalDwellings
        {
            get { EnsurePopulationAggregate(); return _totalDwellings; }
            set { _totalDwellings = value; _populationVersion = PopulationDensityUtility.CacheVersion; }
        }

        public int currentPopulation
        {
            get { EnsurePopulationAggregate(); return _currentPopulation; }
            set { _currentPopulation = value; _populationVersion = PopulationDensityUtility.CacheVersion; }
        }

        // --- Resource pools (0.7 Epic 3) --------------------------------------
        //
        // Pre-0.7 these were seven bare floats meaning "how much is here". They now each carry a
        // cap and a current stock, because the economy needs to be able to draw a province down.
        //
        // The old field names survive as properties returning the *current* stock, which is what
        // every existing reader already meant by them — Empire's production scaling asks "how much
        // minerals does this province have", and the honest answer once depletion exists is what is
        // left, not what the terrain could hold. So the fourteen call sites need no edits and get
        // the new behaviour for free.
        //
        // The old save keys are reused for the caps, so a 0.6 save's single number loads as the
        // ceiling and ResourcePool.EnsureSeeded fills the stock to match. Nothing to migrate.

        private readonly ResourcePool[] pools = CreatePools();

        private static ResourcePool[] CreatePools()
        {
            var created = new ResourcePool[7];
            for (int i = 0; i < created.Length; i++) created[i] = new ResourcePool();
            return created;
        }

        /// <summary>The cap-and-stock pool for a resource. Never null.</summary>
        public ResourcePool Pool(ResourceKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= pools.Length) return pools[0];
            return pools[index];
        }

        /// <summary>What the terrain here can hold, as opposed to what is left of it.</summary>
        public float CapOf(ResourceKind kind)
        {
            return Pool(kind).cap;
        }

        /// <summary>How full this resource is, 0 to 1. Feeds UI and depletion-aware production.</summary>
        public float FractionOf(ResourceKind kind)
        {
            return Pool(kind).Fraction;
        }

        public float rawNutrition
        {
            get { return Pool(ResourceKind.Nutrition).current; }
            set { Pool(ResourceKind.Nutrition).current = value; }
        }

        public float biomass
        {
            get { return Pool(ResourceKind.Biomass).current; }
            set { Pool(ResourceKind.Biomass).current = value; }
        }

        public float minerals
        {
            get { return Pool(ResourceKind.Minerals).current; }
            set { Pool(ResourceKind.Minerals).current = value; }
        }

        public float textiles
        {
            get { return Pool(ResourceKind.Textiles).current; }
            set { Pool(ResourceKind.Textiles).current = value; }
        }

        public float preIndustrialGoods
        {
            get { return Pool(ResourceKind.PreIndustrialGoods).current; }
            set { Pool(ResourceKind.PreIndustrialGoods).current = value; }
        }

        public float industrialGoods
        {
            get { return Pool(ResourceKind.IndustrialGoods).current; }
            set { Pool(ResourceKind.IndustrialGoods).current = value; }
        }

        public float spacerGoods
        {
            get { return Pool(ResourceKind.SpacerGoods).current; }
            set { Pool(ResourceKind.SpacerGoods).current = value; }
        }

        public List<SettlementCrisis> activeCrises = new List<SettlementCrisis>();

        public GeographicProvince() { }

        public GeographicProvince(int id)
        {
            this.id = id;
        }

        /// <summary>
        /// A barren, low-productivity biome — desert, ice, extreme desert — where little grows and
        /// animal life is hard to sustain. These lump into large unsplit regions and are weakly
        /// contested no-man's-land, because nobody wants to settle them (#49).
        /// </summary>
        public static bool IsBarrenBiome(BiomeDef biome)
        {
            return biome != null && biome.plantDensity < 0.12f;
        }

        /// <summary>True when this region's dominant biome is barren no-man's-land.</summary>
        public bool IsBarren => IsBarrenBiome(primaryBiome);

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Collections.Look(ref tiles, "tiles", LookMode.Value);
            Scribe_Values.Look(ref name, "name");
            Scribe_Defs.Look(ref primaryBiome, "primaryBiome");
            Scribe_Collections.Look(ref owningFactionIds, "owningFactionIds", LookMode.Value);
            
            if (owningFactionIds == null)
            {
                owningFactionIds = new List<string>();
            }

            Scribe_Values.Look(ref initializedEconomics, "initializedEconomics", false);
            Scribe_Values.Look(ref provinceType, "provinceType", ProvinceType.Land);
            Scribe_Values.Look(ref _totalDwellings, "totalDwellings", 0);
            Scribe_Values.Look(ref _currentPopulation, "currentPopulation", 0);

            ScribePool(ResourceKind.Nutrition, "rawNutrition");
            ScribePool(ResourceKind.Biomass, "biomass");
            ScribePool(ResourceKind.Minerals, "minerals");
            ScribePool(ResourceKind.Textiles, "textiles");
            ScribePool(ResourceKind.PreIndustrialGoods, "preIndustrialGoods");
            ScribePool(ResourceKind.IndustrialGoods, "industrialGoods");
            ScribePool(ResourceKind.SpacerGoods, "spacerGoods");

            Scribe_Collections.Look(ref activeCrises, "activeCrises", LookMode.Deep);
            if (activeCrises == null)
            {
                activeCrises = new List<SettlementCrisis>();
            }
        }

        /// <summary>
        /// Save one resource pool under the key the bare float used to occupy.
        ///
        /// The cap keeps the old key, so a 0.6 save's single number is read back as the ceiling and
        /// the stock — written under a new key that is absent from old saves — comes back as
        /// <c>Unseeded</c> and is filled to the cap. A province that a 0.7 game genuinely mined out
        /// saves a real 0 and reloads as mined out, because 0 and "never written" are different
        /// values. That distinction is the entire migration.
        /// </summary>
        private void ScribePool(ResourceKind kind, string key)
        {
            ResourcePool pool = Pool(kind);
            Scribe_Values.Look(ref pool.cap, key, 0f);
            Scribe_Values.Look(ref pool.current, key + "Current", ResourcePool.Unseeded);

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                pool.EnsureSeeded();
            }
        }

        public void InitializeProvinceEconomics()
        {
            initializedEconomics = true;
            if (tiles == null || tiles.Count == 0 || Find.WorldGrid == null) return;

            float totalPlantDensity = 0f;
            float totalForageability = 0f;
            float totalTreeDensity = 0f;
            float totalHillMult = 0f;

            foreach (int tileId in tiles)
            {
                Tile t = Find.WorldGrid[tileId];
                var b = t.PrimaryBiome;
                if (b != null)
                {
                    totalPlantDensity += b.plantDensity;
                    totalForageability += b.forageability;
                    totalTreeDensity += b.TreeDensity;
                }
                float hillMult = 0.5f;
                if (t.hilliness == Hilliness.SmallHills) hillMult = 1.0f;
                else if (t.hilliness == Hilliness.LargeHills) hillMult = 2.0f;
                else if (t.hilliness == Hilliness.Mountainous) hillMult = 3.0f;
                totalHillMult += hillMult;
            }

            float avgPlant = totalPlantDensity / tiles.Count;
            float avgForage = totalForageability / tiles.Count;
            float avgTree = totalTreeDensity / tiles.Count;
            float avgHill = totalHillMult / tiles.Count;

            // Determine the highest tech level among factions owning settlements here
            TechLevel maxTech = TechLevel.Neolithic;
            int settlementCount = 0;
            foreach (var settlement in Find.WorldObjects.Settlements)
            {
                if (tiles.Contains(settlement.Tile))
                {
                    settlementCount++;
                    if (settlement.Faction != null && settlement.Faction.def.techLevel > maxTech)
                    {
                        maxTech = settlement.Faction.def.techLevel;
                    }
                }
            }

            // Demographics aggregated from the population density tracker. Source population, not the
            // smeared influence field, so the total is who lives here rather than everyone's decayed
            // reach into here (#55).
            int totalProvincePop = 0;
            foreach (int tileId in tiles)
            {
                totalProvincePop += PopulationDensityUtility.GetSourcePopulationAtTile(tileId);
            }

            currentPopulation = totalProvincePop;
            totalDwellings = currentPopulation / 2;

            bool isSpacer = maxTech >= TechLevel.Spacer;
            bool isIndustrial = maxTech == TechLevel.Industrial;

            // 0.7: these figures are now the *ceiling* the terrain imposes rather than the stock
            // itself. SetCap fills a fresh province to the brim and clamps an existing one down, so
            // re-running this on a province that has already been worked does not refill it.
            float nutritionCap;
            if (isSpacer)
                nutritionCap = 1000f * tiles.Count;
            else if (isIndustrial)
                nutritionCap = avgPlant * 500f * tiles.Count;
            else
                nutritionCap = avgForage * 500f * tiles.Count;

            SetInitialCap(ResourceKind.Nutrition, nutritionCap);
            SetInitialCap(ResourceKind.Biomass, avgTree * 500f * tiles.Count);
            SetInitialCap(ResourceKind.Minerals, avgHill * 500f * tiles.Count);
            SetInitialCap(ResourceKind.Textiles, 100f * tiles.Count);

            SetInitialCap(ResourceKind.PreIndustrialGoods, 0f);
            SetInitialCap(ResourceKind.IndustrialGoods, 0f);
            SetInitialCap(ResourceKind.SpacerGoods, 0f);
        }

        /// <summary>
        /// Set a resource's ceiling, filling the stock to match only if it has never been set.
        /// A province being re-assessed keeps whatever it has left.
        /// </summary>
        private void SetInitialCap(ResourceKind kind, float cap)
        {
            ResourcePool pool = Pool(kind);
            bool fresh = pool.current < 0f;
            pool.SetCap(cap);
            if (fresh) pool.current = pool.cap;
        }
    }
}
