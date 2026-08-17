using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Sizing;
using Verse;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The live-game entry point for settlement size tiers (0.8: structural model).
    ///
    /// <para>A settlement's tier is no longer read from its own size. It is a property of the whole
    /// faction's portfolio: the faction's settlements form a pyramid (each tier at least one wider
    /// than the one above — see <see cref="TierPyramidRules"/>), and a settlement's place in it is
    /// decided by how <b>protected</b> it is — how much of the faction's own territory surrounds it.
    /// The single most-protected settlement is the faction's <b>capital</b> and takes the top tier the
    /// portfolio affords.</para>
    ///
    /// <para>Same shape as <see cref="WorldObjectPlacementUtility"/>: this file gathers the numbers,
    /// the pure rules decide what they mean. Tier is derived, never stored, so it cannot go stale; the
    /// per-faction ranking is cached and rebuilt only when a world object is added or removed.</para>
    /// </summary>
    public static class SettlementSizeUtility
    {
        /// <summary>How many tile-rings around a settlement count toward its "surrounded by own
        /// territory" protection score. Tuned in-game via the tier-pyramid debug report.</summary>
        public const int ProtectionRadius = 6;

        // Per-faction settlements, most-protected first. Rebuilt when the world-object set changes,
        // tracked by the same version the density cache bumps on add/remove.
        private static readonly Dictionary<Faction, List<WorldObject>> rankedCache = new Dictionary<Faction, List<WorldObject>>();
        private static int cacheVersion = -1;

        /// <summary>Drop the ranking cache (settings changes / tests).</summary>
        public static void InvalidateCache()
        {
            rankedCache.Clear();
            cacheVersion = -1;
            cachedReferenceMax = -1;
            referenceVersion = -1;
        }

        private static void EnsureFresh()
        {
            int v = PopulationDensityUtility.CacheVersion;
            if (v != cacheVersion)
            {
                rankedCache.Clear();
                cacheVersion = v;
            }
        }

        /// <summary>
        /// The tier of a world object, or <see cref="SettlementTier.None"/> when it has none —
        /// non-settlements, factionless objects, and everything when tiers are switched off. Only
        /// settlements carry a structural tier; outposts and camps are production/forward holdings,
        /// not rungs of the population pyramid.
        /// </summary>
        public static SettlementTier TierOf(WorldObject obj)
        {
            if (obj == null) return SettlementTier.None;
            if (!WorldObjectIntegrationSettings.SettlementTiersActive) return SettlementTier.None;
            if (WorldObjectClassifier.Classify(obj) != WorldObjectKind.Settlement) return SettlementTier.None;
            if (obj.Faction == null) return SettlementTier.None;

            List<WorldObject> ranked = RankedSettlements(obj.Faction);
            int rank = ranked.IndexOf(obj);
            if (rank < 0) return SettlementTier.None;

            return TierPyramidRules.TierForRank(rank, TierPyramidRules.TierCounts(ranked.Count));
        }

        /// <summary>True only for the single most-protected settlement of its faction — the capital.</summary>
        public static bool IsCapital(WorldObject obj)
        {
            if (obj == null || !WorldObjectIntegrationSettings.SettlementTiersActive) return false;
            if (WorldObjectClassifier.Classify(obj) != WorldObjectKind.Settlement || obj.Faction == null) return false;

            List<WorldObject> ranked = RankedSettlements(obj.Faction);
            return ranked.Count > 0 && ranked[0] == obj;
        }

        /// <summary>The faction's capital (its most-protected settlement), or null if it has none.</summary>
        public static WorldObject CapitalOf(Faction faction)
        {
            if (faction == null) return null;
            List<WorldObject> ranked = RankedSettlements(faction);
            return ranked.Count > 0 ? ranked[0] : null;
        }

        /// <summary>
        /// The faction's settlements, most-protected first. Protection = the count of the faction's
        /// own tiles within <see cref="ProtectionRadius"/> of the settlement — an interior settlement,
        /// ringed by its own land, scores high; a frontier one scores low. Ties break by tile id so
        /// the order is stable across rebuilds.
        /// </summary>
        public static List<WorldObject> RankedSettlements(Faction faction)
        {
            EnsureFresh();
            if (faction == null) return new List<WorldObject>();
            if (rankedCache.TryGetValue(faction, out List<WorldObject> cached)) return cached;

            var built = BuildRanked(faction);
            rankedCache[faction] = built;
            return built;
        }

        private static List<WorldObject> BuildRanked(Faction faction)
        {
            var result = new List<WorldObject>();
            if (Find.WorldObjects == null || Find.WorldGrid == null) return result;

            var settlements = new List<WorldObject>();
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (o == null || o.Faction != faction) continue;
                if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                settlements.Add(o);
            }
            if (settlements.Count == 0) return result;

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            HashSet<int> held = HeldProvinceIds(mgr, faction);

            var scored = new List<(WorldObject obj, int score, int tile)>(settlements.Count);
            foreach (WorldObject s in settlements)
            {
                int tile = s.Tile.tileId;
                scored.Add((s, ProtectionAt(tile, held, mgr), tile));
            }

            // Most protected first; stable tie-break on tile id.
            scored.Sort((a, b) =>
            {
                int c = b.score.CompareTo(a.score);
                return c != 0 ? c : a.tile.CompareTo(b.tile);
            });

            foreach (var e in scored) result.Add(e.obj);
            return result;
        }

        /// <summary>Public protection score for one settlement — used by the tier-pyramid debug report.</summary>
        public static int ProtectionScore(WorldObject settlement)
        {
            if (settlement == null || settlement.Faction == null || Find.WorldGrid == null) return 0;
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            HashSet<int> held = HeldProvinceIds(mgr, settlement.Faction);
            return ProtectionAt(settlement.Tile.tileId, held, mgr);
        }

        private static HashSet<int> HeldProvinceIds(SynapseRegionManager mgr, Faction faction)
        {
            var held = new HashSet<int>();
            if (mgr?.Provinces == null || faction == null) return held;
            foreach (GeographicProvince p in mgr.Provinces)
            {
                if (p == null) continue;
                if (RegionalOwnershipUtility.HoldsTerritory(p, faction)) held.Add(p.id);
            }
            return held;
        }

        /// <summary>
        /// Count the faction's own tiles within <see cref="ProtectionRadius"/> rings of a tile
        /// (including the tile itself). A tile counts when the province it belongs to is one the
        /// faction holds.
        /// </summary>
        private static int ProtectionAt(int startTile, HashSet<int> held, SynapseRegionManager mgr)
        {
            if (mgr == null || held.Count == 0 || Find.WorldGrid == null) return 0;
            WorldGrid grid = Find.WorldGrid;

            int count = 0;
            var visited = new HashSet<int> { startTile };
            var frontier = new List<int> { startTile };
            if (held.Contains(mgr.GetProvinceId(startTile))) count++;

            var neighbors = new List<PlanetTile>();
            for (int ring = 0; ring < ProtectionRadius; ring++)
            {
                var next = new List<int>();
                foreach (int t in frontier)
                {
                    grid.GetTileNeighbors(t, neighbors);
                    foreach (PlanetTile n in neighbors)
                    {
                        int nid = n.tileId;
                        if (!visited.Add(nid)) continue;
                        next.Add(nid);
                        if (held.Contains(mgr.GetProvinceId(nid))) count++;
                    }
                }
                frontier = next;
                if (frontier.Count == 0) break;
            }
            return count;
        }

        // --- 0.8 population caps ------------------------------------------------

        /// <summary>Tech-level multiplier on a settlement's population cap; industrial is the ×1 baseline.</summary>
        public static float TechFactor(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic: return 0.6f;
                case TechLevel.Medieval: return 0.8f;
                case TechLevel.Industrial: return 1.0f;
                case TechLevel.Spacer: return 1.5f;
                case TechLevel.Ultra:
                case TechLevel.Archotech: return 2.0f;
                default: return 1.0f;
            }
        }

        private static float TechFactorOf(WorldObject obj)
        {
            TechLevel tech = obj?.Faction?.def?.techLevel ?? TechLevel.Industrial;
            return TechFactor(tech);
        }

        /// <summary>The population cap for a settlement: territories-for-tier × multiplier × tech factor.
        /// 0 for an untiered holding.</summary>
        public static int MaxPopulationOf(WorldObject settlement)
        {
            return PopulationCapRules.MaxPopulation(TierOf(settlement),
                WorldObjectIntegrationSettings.populationCapMultiplier, TechFactorOf(settlement));
        }

        /// <summary>The size a settlement drifts toward: two-thirds of its cap.</summary>
        public static int TargetPopulationOf(WorldObject settlement)
        {
            return PopulationCapRules.TargetPopulation(TierOf(settlement),
                WorldObjectIntegrationSettings.populationCapMultiplier, TechFactorOf(settlement));
        }

        private static int cachedReferenceMax = -1;
        private static int referenceVersion = -1;

        /// <summary>
        /// The highest theoretical settlement population in this world: a metropolis (T5) at the
        /// highest tech level any settled faction has, times the multiplier — the top (red) end of the
        /// population heatmap. Falls back to the industrial metropolis cap when nothing is settled.
        /// </summary>
        public static int ReferenceMaxPopulation()
        {
            if (cachedReferenceMax >= 0 && referenceVersion == PopulationDensityUtility.CacheVersion)
                return cachedReferenceMax;

            float maxTech = 1.0f;
            if (Find.WorldObjects != null)
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    WorldObject o = all[i];
                    if (o == null || o.Faction == null) continue;
                    if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                    float f = TechFactor(o.Faction.def.techLevel);
                    if (f > maxTech) maxTech = f;
                }
            }
            int refMax = PopulationCapRules.MaxPopulation(SettlementTier.Metropolis,
                WorldObjectIntegrationSettings.populationCapMultiplier, maxTech);
            cachedReferenceMax = refMax > 0 ? refMax : 1;
            referenceVersion = PopulationDensityUtility.CacheVersion;
            return cachedReferenceMax;
        }

        /// <summary>
        /// Population from the owning mod if it exposes one, falling back to R&amp;T's own estimate
        /// for plain settlements. A mod that tracks its colonies' headcount knows better than we do.
        /// </summary>
        public static int PopulationOf(WorldObject obj, WorldObjectKind kind)
        {
            int population;
            if (WorldObjectAdapterRegistry.TryGetPopulation(obj, out population) && population > 0)
            {
                return population;
            }

            Settlement settlement = obj as Settlement;
            if (settlement != null)
            {
                return PopulationDensityUtility.GetSettlementPopulation(settlement);
            }

            return 0;
        }

        /// <summary>
        /// Production multiplier for this holding's tier. Safe to apply unconditionally: an
        /// untiered holding, and every holding when tiers are off, returns a neutral 1.
        /// </summary>
        public static float ProductionScaleOf(WorldObject obj)
        {
            return SettlementSizeRules.ProductionScale(TierOf(obj));
        }

        /// <summary>How far this holding's claim reaches, in tiles. Zero when it has no tier.</summary>
        public static int TerritoryFootprintOf(WorldObject obj)
        {
            return SettlementSizeRules.TerritoryFootprint(TierOf(obj));
        }

        /// <summary>Residents this holding can support, or zero meaning no tier-imposed cap.</summary>
        public static int PopulationCapacityOf(WorldObject obj)
        {
            return SettlementSizeRules.PopulationCapacity(TierOf(obj));
        }

        /// <summary>
        /// The largest holding standing on a tile, for the inspect pane. Returns null when the tile
        /// carries nothing that has a tier.
        /// </summary>
        public static WorldObject LargestTieredObjectAt(int tileId, out SettlementTier tier)
        {
            tier = SettlementTier.None;
            WorldObject best = null;

            if (Find.WorldObjects == null) return null;

            var all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                if (obj == null || obj.Tile.tileId != tileId) continue;

                SettlementTier candidate = TierOf(obj);
                if (candidate == SettlementTier.None) continue;

                if (best == null || (int)candidate > (int)tier)
                {
                    best = obj;
                    tier = candidate;
                }
            }

            return best;
        }
    }
}
