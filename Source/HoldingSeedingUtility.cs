using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Economy;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;
using RegionsAndSocieties.Sizing;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The outcome of a seeding pass, structured so the debug report and the Tier-2 tests read the
    /// same numbers rather than parsing a log line.
    /// </summary>
    public class HoldingSeedingResult
    {
        /// <summary>Non-null when the pass declined to run at all, naming why (a guard, not a failure).</summary>
        public string guardReason;
        public int provincesWithAnchor;
        public int placed;
        public readonly List<string> lines = new List<string>();

        public string ToReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== R&T holding seeding (#18) ===");
            if (guardReason != null)
            {
                sb.AppendLine("did not run: " + guardReason);
                return sb.ToString();
            }
            sb.AppendLine($"anchored provinces: {provincesWithAnchor}    holdings placed: {placed}");
            foreach (string line in lines) sb.AppendLine("  " + line);
            if (lines.Count == 0) sb.AppendLine("  (nothing to place — every territory already at its allowance)");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Seeds holdings of ANY registered kind (outposts, camps, military installations) around settlements at
    /// world generation, up to each territory's per-kind allowance scaled by world maturity (#18). The
    /// universal hook the compatibility mods plug into: it photographs the world, defers the count and shape
    /// to a registered <see cref="ISeedingPolicy"/> (Core ships the Outpost policy; a CP mod registers its
    /// own for its kinds) and the build to an <see cref="IHoldingCreator"/>, so it never names a foreign mod
    /// type. Kind-agnostic — nothing here is outpost-specific except the <see cref="PreviewArchetypes"/>
    /// tuning aid.
    /// </summary>
    public static class HoldingSeedingUtility
    {
        /// <summary>The territorial kinds R&amp;S will seed (Settlement is placed by faction generation, not
        /// seeded). A kind is seeded only when a creator can build it and a policy sizes it.</summary>
        private static readonly WorldObjectKind[] SeedableKinds =
        {
            WorldObjectKind.Outpost, WorldObjectKind.Camp, WorldObjectKind.Military
        };

        private struct Anchor
        {
            public Faction faction;
            public SettlementTier tier;
            public int tile;   // the anchor settlement's tile, for distance-to-anchor (#18)
        }

        public static HoldingSeedingResult SeedHoldings()
        {
            var result = new HoldingSeedingResult();

            if (!WorldObjectPlacementUtility.StrictOwnershipActive())
            {
                result.guardReason = "compatibility mode (this world was not generated with R&T)";
                return result;
            }

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || Find.WorldGrid == null)
            {
                result.guardReason = "no regions generated";
                return result;
            }
            if (!mgr.EffectiveHoldingSeedingEnabled)
            {
                result.guardReason = "holding seeding is switched off";
                return result;
            }

            float maturity = mgr.EffectiveSeedingMaturity;
            if (maturity <= 0f)
            {
                result.guardReason = "world maturity is 0 (seed nothing)";
                return result;
            }

            // Which kinds actually have someone to build them this game?
            var kinds = new List<WorldObjectKind>();
            for (int i = 0; i < SeedableKinds.Length; i++)
                if (HoldingCreatorRegistry.AnyActiveFor(SeedableKinds[i])) kinds.Add(SeedableKinds[i]);
            if (kinds.Count == 0)
            {
                result.guardReason = "no active holding creator (no compatibility mod is registered to build seedable objects)";
                return result;
            }

            // Stamp maturity + enable so a regenerate reproduces this buildout.
            mgr.ResolveSeedingInputs();

            // One pass over world objects: record every occupied tile, the highest-tier settlement anchoring
            // each province, and how many holdings of each kind each province already holds.
            var occupied = new HashSet<int>();
            var anchors = new Dictionary<int, Anchor>();
            var existingByProvince = new Dictionary<int, int[]>();   // pid -> count indexed by (int)kind

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
                if (kind.IsTerritorial() && kind != WorldObjectKind.Settlement)
                {
                    if (!existingByProvince.TryGetValue(pid, out int[] counts))
                    {
                        counts = new int[8];
                        existingByProvince[pid] = counts;
                    }
                    int k = (int)kind;
                    if (k >= 0 && k < counts.Length) counts[k]++;
                }
            }

            // #38 perf: build the placement snapshot ONCE for the whole pass instead of letting the cached
            // snapshot rebuild every time a created holding changes the world-object count (the walk that
            // once ground worldgen to minutes). Each placement is appended to the snapshot's holdings so the
            // separation rule still sees it, without re-walking ownership.
            PlacementWorld snapshot = WorldObjectPlacementUtility.BuildWorld();
            if (snapshot == null)
            {
                result.guardReason = "no placement snapshot";
                return result;
            }

            foreach (KeyValuePair<int, Anchor> kv in anchors)
            {
                int pid = kv.Key;
                Anchor anchor = kv.Value;
                if (anchor.faction == null) continue;

                result.provincesWithAnchor++;

                GeographicProvince province = mgr.GetProvince(pid);
                if (province?.tiles == null) continue;

                existingByProvince.TryGetValue(pid, out int[] existingCounts);
                float radius = ProvinceRadius(Find.WorldGrid, anchor.tile, province.tiles);

                for (int ki = 0; ki < kinds.Count; ki++)
                {
                    WorldObjectKind kind = kinds[ki];
                    ISeedingPolicy policy = SeedingPolicyRegistry.PolicyFor(kind);
                    if (policy == null || !policy.IsActive) continue;

                    int baseAllow = policy.Allowance(anchor.tier);
                    int allow = SeedingMaturityRules.ScaleAllowance(baseAllow, maturity);
                    int existing = existingCounts != null ? existingCounts[(int)kind] : 0;
                    int remaining = allow - existing;
                    if (remaining <= 0) continue;

                    var chosen = new List<OutpostArchetype>();
                    int placedHere = PlaceInProvince(kind, policy, province, anchor, remaining, occupied, snapshot, radius, result, chosen);
                    if (placedHere > 0)
                    {
                        result.lines.Add($"province {pid}: {anchor.faction.Name} [{anchor.tier.LabelCapitalized()}] "
                            + $"{kind} {existing}+{placedHere}/{allow} ({ArchetypeHistogram(chosen)})");
                    }
                }
            }

            // The seeded holdings are population sources; drop the density cache so their propagation
            // replaces the phantom concentration the empty peak used to read as (#56).
            PopulationDensityUtility.MarkCacheDirty();
            return result;
        }

        private static int PlaceInProvince(WorldObjectKind kind, ISeedingPolicy policy, GeographicProvince province,
            Anchor anchor, int remaining, HashSet<int> occupied, PlacementWorld snapshot, float radius,
            HoldingSeedingResult result, List<OutpostArchetype> chosen)
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

                TileFeatures features = BuildFeatures(province, tileId, tile, anchor, grid, radius);
                if (!policy.AcceptsTile(features)) continue;

                // The full placement rule chain — separation, region lock, supply range, foothold —
                // evaluated against the one held snapshot so a whole province's seeding pays the ownership
                // walk once, not once per created object.
                if (!PlacementEvaluator.Evaluate(snapshot, tileId, anchor.faction, kind).Allowed) continue;

                OutpostArchetype archetype = policy.SelectArchetype(features);
                if (HoldingCreatorRegistry.TryCreate(kind, archetype, anchor.faction, tileId, out WorldObject created)
                    && created != null)
                {
                    occupied.Add(tileId);
                    snapshot.Holdings.Add(new PlacementHolding(tileId, kind, anchor.faction));
                    chosen.Add(archetype);
                    placed++;
                    result.placed++;
                }
            }

            return placed;
        }

        /// <summary>
        /// #18 tuning/validation: for a province, resolve its anchor and report which archetype the scorer
        /// would pick for each habitable candidate tile — WITHOUT placing anything, so it works with no
        /// outpost creator installed. This is how the position/faction-aware choice is eyeballed and tuned
        /// before a CP mod maps the archetypes onto concrete defs.
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
                treeDensity = BiomeSafe.TreeDensity(biome),
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
