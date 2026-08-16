using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Placement
{
    /// <summary>
    /// Everything <see cref="PlacementEvaluator"/> needs to know about the world, expressed as
    /// plain data and delegates rather than as calls into <c>Find</c>.
    ///
    /// This is what makes the placement rules testable. The live game builds one of these from
    /// <c>Find.WorldGrid</c> and <c>SynapseRegionManager</c> (see
    /// <c>WorldObjectPlacementUtility</c>); the test suite builds one from a hand-written map.
    /// The rules themselves cannot tell the difference, which is the point.
    /// </summary>
    public sealed class PlacementWorld
    {
        /// <summary>Traversal distance between two tiles.</summary>
        public Func<int, int, int> Distance;

        /// <summary>Every territorial object currently on the map.</summary>
        public List<PlacementHolding> Holdings = new List<PlacementHolding>();

        /// <summary>Province containing a tile, or -1 if the tile belongs to no province.</summary>
        public Func<int, int> ProvinceIdAt;

        /// <summary>How the given faction stands in the given province.</summary>
        public Func<int, object, ProvinceControl> ControlOf;

        /// <summary>Perimeter tiles of every province the faction holds. Supply lines run from these.</summary>
        public Func<object, IEnumerable<int>> HeldBorderTiles;

        /// <summary>
        /// Every province the faction holds or co-holds. Expansion runs outward from these.
        ///
        /// Distinct from the provinces its holdings sit in, and that difference is the whole of Epic
        /// 5 child 3: a province can be held on ownership score or on a worldgen ownership entry with
        /// no world object of the faction's inside it, and such a province is territory whose border
        /// the faction may expand across.
        /// </summary>
        public Func<object, IEnumerable<int>> HeldProvinceIds;

        /// <summary>Whether two provinces share a border.</summary>
        public Func<int, int, bool> ProvincesAdjacent;

        /// <summary>
        /// Whether a faction is one the player controls (Faction.OfPlayer, plus empire-style player
        /// holdings). The player-placement rule (#61) keys off this: the player is not fenced out by
        /// loose ownership, only by an exclusive rival hold. Defaults to false when unset.
        /// </summary>
        public Func<object, bool> IsPlayerControlled;

        /// <summary>
        /// Whether a faction other than the asking one holds the province exclusively (&gt;=71%).
        /// Defaults to false when unset. See <see cref="RegionalDomainUtility.ExclusiveOwner"/>.
        /// </summary>
        public Func<int, object, bool> ExclusiveRivalAt;

        /// <summary>
        /// Whether two factions count as the same claimant. Defaults to reference equality.
        ///
        /// The live game overrides it because empire-style mods run the player's holdings under a
        /// faction of their own: without this, a player who owns Empire colonies and Vanilla
        /// Outposts side by side would be treated as two unrelated factions and refused supply
        /// from their own territory.
        /// </summary>
        public Func<object, object, bool> FactionsMatch;

        public int DistanceBetween(int a, int b)
        {
            return Distance == null ? int.MaxValue : Distance(a, b);
        }

        public int ProvinceAt(int tile)
        {
            return ProvinceIdAt == null ? -1 : ProvinceIdAt(tile);
        }

        public ProvinceControl Control(int provinceId, object faction)
        {
            if (ControlOf == null || provinceId < 0) return ProvinceControl.Unclaimed;
            return ControlOf(provinceId, faction);
        }

        public IEnumerable<int> BorderTilesFor(object faction)
        {
            return HeldBorderTiles == null ? new int[0] : (HeldBorderTiles(faction) ?? new int[0]);
        }

        public IEnumerable<int> HeldProvincesFor(object faction)
        {
            return HeldProvinceIds == null ? new int[0] : (HeldProvinceIds(faction) ?? new int[0]);
        }

        public bool AreAdjacent(int a, int b)
        {
            return ProvincesAdjacent != null && ProvincesAdjacent(a, b);
        }

        public bool IsPlayer(object faction)
        {
            return IsPlayerControlled != null && faction != null && IsPlayerControlled(faction);
        }

        public bool HasExclusiveRival(int provinceId, object faction)
        {
            return ExclusiveRivalAt != null && provinceId >= 0 && ExclusiveRivalAt(provinceId, faction);
        }

        public bool SameClaimant(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b) || a.Equals(b)) return true;
            return FactionsMatch != null && FactionsMatch(a, b);
        }
    }
}
