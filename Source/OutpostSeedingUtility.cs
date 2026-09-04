using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Economy;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Sizing;
using UnityEngine;
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
            public int tile;   // the anchor settlement's tile, for distance-to-anchor (#18)
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
                        anchors[pid] = new Anchor { faction = obj.Faction, tier = tier, tile = tileId };
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

                var chosen = new List<OutpostArchetype>();
                int placedHere = PlaceInProvince(province, anchor, remaining, occupied, result, chosen);
                if (placedHere > 0)
                {
                    result.lines.Add($"province {pid}: {anchor.faction.Name} [{anchor.tier.LabelCapitalized()}] "
                        + $"had {existing}/{OutpostAllowanceRules.OutpostAllowance(anchor.tier)}, placed {placedHere} "
                        + $"({ArchetypeHistogram(chosen)})");
                }
            }

            // The seeded outposts are population sources; drop the density cache so their propagation
            // replaces the phantom concentration the empty peak used to read as (#56).
            PopulationDensityUtility.MarkCacheDirty();
            return result;
        }

        /// <summary>
        /// #18 tuning/validation: for a province, resolve its anchor and report which archetype the scorer
        /// would pick for each habitable candidate tile — WITHOUT placing anything, so it works with no
        /// outpost creator (VOE) installed. This is how the position/faction-aware choice is eyeballed and
        /// tuned before VOE-CP maps the archetypes onto concrete defs.
        /// </summary>
        public static string PreviewArchetypes(GeographicProvince province)
        {
            if (province?.tiles == null) return "no province";
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || Find.WorldObjects == null) return "no world";

            Anchor anchor = default;
            bool found = false;
            var tileSet = new HashSet<int>(province.tiles);
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (o?.Faction == null || !o.Tile.Valid || !tileSet.Contains(o.Tile.tileId)) continue;
                if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                SettlementTier tier = SettlementSizeUtility.TierOf(o);
                if (!found || (int)tier > (int)anchor.tier)
                {
                    anchor = new Anchor { faction = o.Faction, tier = tier, tile = o.Tile.tileId };
                    found = true;
                }
            }
            if (!found) return $"province {province.id}: no anchor settlement — archetype would be terrain-only.";

            float radius = ProvinceRadius(grid, anchor.tile, province.tiles);
            var counts = new Dictionary<OutpostArchetype, int>();
            int candidates = 0;
            for (int t = 0; t < province.tiles.Count; t++)
            {
                int tileId = province.tiles[t];
                Tile tile = grid[tileId];
                if (tile == null || tile.WaterCovered || tile.hilliness == Hilliness.Impassable) continue;
                if (tile.PrimaryBiome != null && tile.PrimaryBiome.impassable) continue;

                candidates++;
                OutpostArchetype a = OutpostArchetypeRules.Choose(BuildFeatures(province, tileId, tile, anchor, grid, radius));
                counts.TryGetValue(a, out int c);
                counts[a] = c + 1;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== R&S outpost archetype preview (#18) — province {province.id} ===");
            sb.AppendLine($"anchor: {anchor.faction.Name} [{anchor.tier.LabelCapitalized()}]  tech={anchor.faction.def?.techLevel}  hostile={anchor.faction.def?.permanentEnemy}");
            sb.AppendLine($"{candidates} candidate tiles would choose:");
            foreach (KeyValuePair<OutpostArchetype, int> kv in counts)
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            return sb.ToString().TrimEnd();
        }

        private static int PlaceInProvince(GeographicProvince province, Anchor anchor, int remaining, HashSet<int> occupied, OutpostSeedingResult result, List<OutpostArchetype> chosen)
        {
            int placed = 0;
            WorldGrid grid = Find.WorldGrid;
            float radius = ProvinceRadius(grid, anchor.tile, province.tiles);   // distance-to-anchor normaliser (#18)

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

                OutpostArchetype archetype = OutpostArchetypeRules.Choose(BuildFeatures(province, tileId, tile, anchor, grid, radius));
                if (HoldingCreatorRegistry.TryCreate(WorldObjectKind.Outpost, archetype, anchor.faction, tileId, out WorldObject created)
                    && created != null)
                {
                    occupied.Add(tileId);
                    chosen.Add(archetype);
                    placed++;
                    result.placed++;
                }
            }

            return placed;
        }

        /// <summary>The province's reach from its anchor: the largest anchor→tile great-circle angle, used
        /// to normalise distance-to-anchor into 0 (core) .. 1 (edge). At least a tiny value so a one-tile
        /// province doesn't divide by zero.</summary>
        private static float ProvinceRadius(WorldGrid grid, int anchorTile, List<int> tiles)
        {
            Vector3 anchorPos = grid.GetTileCenter(anchorTile);
            float max = 0f;
            for (int i = 0; i < tiles.Count; i++)
            {
                float d = Vector3.Angle(anchorPos, grid.GetTileCenter(tiles[i]));
                if (d > max) max = d;
            }
            return max > 0.0001f ? max : 0.0001f;
        }

        /// <summary>Build the archetype-choice inputs (#18): terrain from the tile/biome/region, plus the
        /// position (normalised distance to the anchor) and faction context (anchor tier, tech, hostility)
        /// so the choice reads position- and faction-aware.</summary>
        private static TileFeatures BuildFeatures(GeographicProvince province, int tileId, Tile tile, Anchor anchor, WorldGrid grid, float radius)
        {
            BiomeDef biome = tile.PrimaryBiome;
            float dist = Vector3.Angle(grid.GetTileCenter(anchor.tile), grid.GetTileCenter(tileId)) / radius;
            FactionDef def = anchor.faction?.def;
            return new TileFeatures
            {
                hilliness = HillinessLevel(tile.hilliness),
                plantDensity = biome?.plantDensity ?? 0f,
                treeDensity = biome?.TreeDensity ?? 0f,
                animalDensity = biome?.animalDensity ?? 0f,
                mineralsFraction = province?.FractionOf(ResourceKind.Minerals) ?? 0f,
                coastal = tile.IsCoastal,

                distanceToAnchor = Mathf.Clamp01(dist),
                anchorTier = anchor.tier,
                techLevel = (int)(def?.techLevel ?? TechLevel.Industrial),
                permanentEnemy = def?.permanentEnemy ?? false,
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

        /// <summary>A compact "Mining×2, Farming×1" tally of the archetypes placed, for the seeding report.</summary>
        private static string ArchetypeHistogram(List<OutpostArchetype> chosen)
        {
            var counts = new Dictionary<OutpostArchetype, int>();
            for (int i = 0; i < chosen.Count; i++)
            {
                counts.TryGetValue(chosen[i], out int c);
                counts[chosen[i]] = c + 1;
            }
            var parts = new List<string>();
            foreach (KeyValuePair<OutpostArchetype, int> kv in counts) parts.Add($"{kv.Key}×{kv.Value}");
            return string.Join(", ", parts);
        }
    }
}
