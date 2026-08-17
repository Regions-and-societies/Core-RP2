using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The live-game entry point for 0.7 placement governance.
    ///
    /// Its only job is to photograph the world into a <see cref="PlacementWorld"/> and hand it to
    /// <see cref="PlacementEvaluator"/>. All the actual rules live in the evaluator, which knows
    /// nothing about RimWorld and can therefore be tested without one.
    ///
    /// Every mod's world objects come through here, because the holdings list is built from
    /// <see cref="WorldObjectClassifier"/> rather than from concrete types.
    /// </summary>
    public static class WorldObjectPlacementUtility
    {
        /// <summary>
        /// May <paramref name="faction"/> place a <paramref name="kind"/> on this tile?
        /// Returns true (with an empty reason) whenever governance is switched off, so callers can
        /// use this unconditionally.
        /// </summary>
        public static bool CanPlaceAt(int tileId, Faction faction, WorldObjectKind kind, out string reason)
        {
            PlacementDecision decision = Evaluate(tileId, faction, kind);
            reason = decision.Reason ?? string.Empty;
            return decision.Allowed;
        }

        public static PlacementDecision Evaluate(int tileId, Faction faction, WorldObjectKind kind)
        {
            // Never evaluate while a save is (de)serialising. TileFinder.IsValidTileForNewSettlement
            // gets probed during PostLoadInit — before the world grid's pathfinding is ready — and the
            // separation check's WorldGrid.TraversalDistanceBetween then throws index-out-of-range,
            // which surfaced as a wall of load-time exceptions on every existing save. Validating a
            // placement mid-load is meaningless anyway; vanilla's own result stands until the game is
            // live.
            if (Scribe.mode != LoadSaveMode.Inactive) return PlacementDecision.Allow();

            if (!WorldObjectIntegrationSettings.PlacementGovernanceActive) return PlacementDecision.Allow();

            // Compatibility mode: this world was built before R&T was installed, so it is already
            // full of settlements our rules would have refused. Enforcing buffers and footholds now
            // would block placements next to towns that have been there all along. Territory is
            // still generated, owned and drawn — only the refusals stand down.
            if (!StrictOwnershipActive()) return PlacementDecision.Allow();

            if (Find.WorldGrid == null || faction == null) return PlacementDecision.Allow();

            PlacementWorld world = BuildWorld();
            if (world == null) return PlacementDecision.Allow();

            return PlacementEvaluator.Evaluate(world, tileId, faction, kind);
        }

        /// <summary>
        /// Whether this world enforces placement rules. Defaults to true when there is no world yet,
        /// so a missing region manager cannot silently switch governance off — the compatibility
        /// path has to be a decision recorded on a save, never an accident of load order.
        /// </summary>
        public static bool StrictOwnershipActive()
        {
            var manager = Find.World?.GetComponent<SynapseRegionManager>();
            return manager == null || manager.StrictTerritorialOwnership;
        }

        /// <summary>
        /// Snapshot of everything the rules need. Rebuilt per query: the underlying world object
        /// list changes constantly and a stale cache here would refuse placements on the strength
        /// of a settlement that was destroyed ten minutes ago.
        /// </summary>
        public static PlacementWorld BuildWorld()
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return null;

            var regionManager = Find.World?.GetComponent<SynapseRegionManager>();
            HashSet<Faction> playerControlled = CollectPlayerControlledFactions();

            var world = new PlacementWorld
            {
                Distance = (a, b) => grid.TraversalDistanceBetween(a, b),
                Holdings = CollectHoldings(),
                FactionsMatch = (a, b) => playerControlled.Contains(a as Faction) && playerControlled.Contains(b as Faction),
                ProvinceIdAt = tile => regionManager != null ? regionManager.GetProvinceId(tile) : -1,
                ControlOf = (provinceId, faction) => ControlOf(regionManager, provinceId, faction as Faction),
                HeldBorderTiles = faction => HeldBorderTiles(regionManager, faction as Faction),
                HeldProvinceIds = faction => HeldProvinceIds(regionManager, faction as Faction),
                ProvincesAdjacent = (a, b) => ProvinceAdjacency.AreAdjacent(regionManager, a, b),
                IsPlayerControlled = faction => faction is Faction f && playerControlled.Contains(f),
                ExclusiveRivalAt = (provinceId, faction) => ExclusiveRivalAt(regionManager, provinceId, faction as Faction)
            };

            return world;
        }

        private static List<PlacementHolding> CollectHoldings()
        {
            var holdings = new List<PlacementHolding>();
            if (Find.WorldObjects == null) return holdings;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                WorldObjectKind kind = WorldObjectClassifier.Classify(obj);
                if (!kind.IsTerritorial()) continue;

                holdings.Add(new PlacementHolding(obj.Tile.tileId, kind, obj.Faction));
            }

            return holdings;
        }

        /// <summary>
        /// Every faction the player is actually running. Normally just <c>Faction.OfPlayer</c>, but
        /// empire-style mods give the player's holdings a faction of their own, and Epic 1's
        /// classifier is what recognises them without naming the mod.
        ///
        /// <para>Public because <see cref="FactionStandingUtility"/> has to answer the same question
        /// before it can summarise the player, and the two must agree: a placement rule that treats
        /// the player's empire as the player, next to a published summary that does not, is a
        /// contradiction another mod would eventually build on.</para>
        /// </summary>
        public static HashSet<Faction> CollectPlayerControlledFactions()
        {
            var factions = new HashSet<Faction>();

            Faction player = Faction.OfPlayer;
            if (player != null) factions.Add(player);

            if (Find.WorldObjects == null) return factions;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                if (obj.Faction == null) continue;
                if (!WorldObjectClassifier.IsPlayerHolding(obj)) continue;

                factions.Add(obj.Faction);
            }

            return factions;
        }

        private static ProvinceControl ControlOf(SynapseRegionManager regionManager, int provinceId, Faction faction)
        {
            if (regionManager == null || provinceId < 0 || faction == null) return ProvinceControl.Unclaimed;

            GeographicProvince province = regionManager.GetProvince(provinceId);
            if (province == null) return ProvinceControl.Unclaimed;

            return RegionalOwnershipUtility.GetControl(province, faction);
        }

        private static bool ExclusiveRivalAt(SynapseRegionManager regionManager, int provinceId, Faction faction)
        {
            if (regionManager == null || provinceId < 0) return false;

            GeographicProvince province = regionManager.GetProvince(provinceId);
            if (province == null) return false;

            return RegionalOwnershipUtility.IsExclusivelyOwnedByRival(province, faction);
        }

        private static IEnumerable<int> HeldBorderTiles(SynapseRegionManager regionManager, Faction faction)
        {
            var tiles = new List<int>();
            if (regionManager == null || faction == null) return tiles;

            foreach (GeographicProvince province in regionManager.Provinces)
            {
                if (!RegionalOwnershipUtility.HoldsTerritory(province, faction)) continue;

                HashSet<int> borderTiles = RegionalOwnershipUtility.GetPerimeterTiles(province);
                if (borderTiles.Count == 0)
                {
                    tiles.AddRange(province.tiles);
                }
                else
                {
                    tiles.AddRange(borderTiles);
                }
            }

            return tiles;
        }

        /// <summary>
        /// Every province the faction holds or co-holds, which is what the expansion rule measures
        /// outward from. The same walk <see cref="HeldBorderTiles"/> already does, asking the same
        /// question of the same method, so the two can never disagree about where the border is.
        /// </summary>
        private static IEnumerable<int> HeldProvinceIds(SynapseRegionManager regionManager, Faction faction)
        {
            var ids = new List<int>();
            if (regionManager == null || faction == null) return ids;

            foreach (GeographicProvince province in regionManager.Provinces)
            {
                if (province == null) continue;
                if (!RegionalOwnershipUtility.HoldsTerritory(province, faction)) continue;

                ids.Add(province.id);
            }

            return ids;
        }
    }
}
