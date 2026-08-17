using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Economy;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Sizing;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The outcome of a seeding pass, structured so the debug report and the Tier-2 tests read the
    /// same numbers rather than parsing a log line.
    /// </summary>
    public class OutpostSeedingResult
    {
        /// <summary>Non-null when the pass declined to run at all, naming why (a guard, not a failure).</summary>
        public string guardReason;
        public int provincesWithAnchor;
        public int placed;
        public readonly List<string> lines = new List<string>();

        public string ToReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== R&T outpost seeding ===");
            if (guardReason != null)
            {
                sb.AppendLine("did not run: " + guardReason);
                return sb.ToString();
            }
            sb.AppendLine($"anchored provinces: {provincesWithAnchor}    outposts placed: {placed}");
            foreach (string line in lines) sb.AppendLine("  " + line);
            if (lines.Count == 0) sb.AppendLine("  (nothing to place — every territory already at its allowance)");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Seeds outposts around settlements at world generation, up to each territory's tier-based
    /// allowance (0.8, #56 reframed onto tiers). The live-game facade: it photographs the world and
    /// defers every judgement to a pure rule — <see cref="Sizing.SettlementSizeUtility.TierOf"/> for
    /// the tier, <see cref="Sizing.OutpostAllowanceRules"/> for the count,
    /// <see cref="Sizing.OutpostArchetypeRules"/> for the type — then asks the
    /// <see cref="HoldingCreatorRegistry"/> to build the object, so it never names a foreign mod type.
    /// </summary>
    public static class OutpostSeedingUtility
    {
        private struct Anchor
        {
            public Faction faction;
            public SettlementTier tier;
        }

        public static OutpostSeedingResult SeedOutposts()
        {
            var result = new OutpostSeedingResult();

            if (!WorldObjectIntegrationSettings.OutpostSeedingActive)
            {
                result.guardReason = "outpost seeding is switched off";
                return result;
            }
            if (!WorldObjectPlacementUtility.StrictOwnershipActive())
            {
                result.guardReason = "compatibility mode (this world was not generated with R&T)";
                return result;
            }
            if (!HoldingCreatorRegistry.AnyActiveFor(WorldObjectKind.Outpost))
            {
                result.guardReason = "no active outpost creator (Vanilla Outposts Expanded not installed or its integration is off)";
                return result;
            }

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || Find.WorldGrid == null)
            {
                result.guardReason = "no regions generated";
                return result;
            }

            // One pass over world objects: record every occupied tile, the highest-tier settlement
            // anchoring each province, and how many outposts each province already holds.
            var occupied = new HashSet<int>();
            var anchors = new Dictionary<int, Anchor>();
            var outpostCounts = new Dictionary<int, int>();

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                if (obj == null) continue;
                int tileId = obj.Tile.tileId;
                occupied.Add(tileId);

                int pid = mgr.GetProvinceId(tileId);
                if (pid < 0) continue;

                WorldObjectKind kind = WorldObjectClassifier.Classify(obj);
                if (kind == WorldObjectKind.Settlement && obj.Faction != null)
                {
                    SettlementTier tier = SettlementSizeUtility.TierOf(obj);
                    if (!anchors.TryGetValue(pid, out Anchor existing) || (int)tier > (int)existing.tier)
                    {
                        anchors[pid] = new Anchor { faction = obj.Faction, tier = tier };
                    }
                }
                if (kind == WorldObjectKind.Outpost)
                {
                    outpostCounts.TryGetValue(pid, out int c);
                    outpostCounts[pid] = c + 1;
                }
            }

            foreach (KeyValuePair<int, Anchor> kv in anchors)
            {
                int pid = kv.Key;
                Anchor anchor = kv.Value;
                if (anchor.faction == null) continue;

                result.provincesWithAnchor++;

                outpostCounts.TryGetValue(pid, out int existing);
                int remaining = OutpostAllowanceRules.RemainingAllowance(anchor.tier, existing);
                if (remaining <= 0) continue;

                GeographicProvince province = mgr.GetProvince(pid);
                if (province?.tiles == null) continue;

                int placedHere = PlaceInProvince(province, anchor, remaining, occupied, result);
                if (placedHere > 0)
                {
                    result.lines.Add($"province {pid}: {anchor.faction.Name} [{anchor.tier.LabelCapitalized()}] "
                        + $"had {existing}/{OutpostAllowanceRules.OutpostAllowance(anchor.tier)}, placed {placedHere}");
                }
            }

            // The seeded outposts are population sources; drop the density cache so their propagation
            // replaces the phantom concentration the empty peak used to read as (#56).
            PopulationDensityUtility.MarkCacheDirty();
            return result;
        }

        private static int PlaceInProvince(GeographicProvince province, Anchor anchor, int remaining, HashSet<int> occupied, OutpostSeedingResult result)
        {
            int placed = 0;
            WorldGrid grid = Find.WorldGrid;

            for (int t = 0; t < province.tiles.Count && placed < remaining; t++)
            {
                int tileId = province.tiles[t];
                if (occupied.Contains(tileId)) continue;

                Tile tile = grid[tileId];
                if (tile == null || tile.WaterCovered || tile.hilliness == Hilliness.Impassable) continue;
                if (tile.PrimaryBiome != null && tile.PrimaryBiome.impassable) continue;

                // The full placement rule chain — separation, supply range, foothold. Territory is the
                // anchor's own, so the ownership check passes; separation is what spreads the outposts.
                if (!WorldObjectPlacementUtility.CanPlaceAt(tileId, anchor.faction, WorldObjectKind.Outpost, out _))
                {
                    continue;
                }

                OutpostArchetype archetype = OutpostArchetypeRules.Choose(BuildFeatures(province, tileId, tile));
                if (HoldingCreatorRegistry.TryCreate(WorldObjectKind.Outpost, archetype, anchor.faction, tileId, out WorldObject created)
                    && created != null)
                {
                    occupied.Add(tileId);
                    placed++;
                    result.placed++;
                }
            }

            return placed;
        }

        private static TileFeatures BuildFeatures(GeographicProvince province, int tileId, Tile tile)
        {
            BiomeDef biome = tile.PrimaryBiome;
            return new TileFeatures
            {
                hilliness = HillinessLevel(tile.hilliness),
                plantDensity = biome?.plantDensity ?? 0f,
                treeDensity = biome?.TreeDensity ?? 0f,
                animalDensity = biome?.animalDensity ?? 0f,
                mineralsFraction = province?.FractionOf(ResourceKind.Minerals) ?? 0f,
                coastal = tile.IsCoastal
            };
        }

        private static int HillinessLevel(Hilliness hilliness)
        {
            switch (hilliness)
            {
                case Hilliness.SmallHills: return 1;
                case Hilliness.LargeHills: return 2;
                case Hilliness.Mountainous: return 3;
                default: return 0;
            }
        }
    }
}
