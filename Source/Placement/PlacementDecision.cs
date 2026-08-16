using RegionsAndSocieties.Integration;

namespace RegionsAndSocieties.Placement
{
    /// <summary>How a faction stands in a province.</summary>
    public enum ProvinceControl
    {
        /// <summary>No province here, or nobody has scored high enough to own it.</summary>
        Unclaimed,

        /// <summary>The asking faction owns it outright.</summary>
        Held,

        /// <summary>The asking faction and at least one rival are within a hair of each other.</summary>
        Contested,

        /// <summary>Somebody else owns it and the asking faction does not.</summary>
        Foreign
    }

    /// <summary>Why a placement was refused. <see cref="None"/> means it was allowed.</summary>
    public enum PlacementRejection
    {
        None,
        TooCloseToHolding,
        ForeignTerritory,
        OutOfSupplyRange,
        NoAdjacentFoothold,
        RegionAlreadySettled
    }

    /// <summary>
    /// One holding on the world map, reduced to the three facts placement rules care about.
    /// Deliberately not a <c>WorldObject</c> so the rules can be evaluated — and tested — without
    /// a live world.
    /// </summary>
    public sealed class PlacementHolding
    {
        public int tile;
        public WorldObjectKind kind;
        public object faction;

        public PlacementHolding() { }

        public PlacementHolding(int tile, WorldObjectKind kind, object faction)
        {
            this.tile = tile;
            this.kind = kind;
            this.faction = faction;
        }
    }

    /// <summary>The answer to "may this faction put this kind of thing on this tile?".</summary>
    public sealed class PlacementDecision
    {
        public bool Allowed;
        public PlacementRejection Rejection;
        public string Reason;

        /// <summary>Set when the target province is contested, whether or not placement was allowed.</summary>
        public bool Contested;

        public static PlacementDecision Allow()
        {
            return new PlacementDecision { Allowed = true, Rejection = PlacementRejection.None, Reason = string.Empty };
        }

        public static PlacementDecision Refuse(PlacementRejection rejection, string reason)
        {
            return new PlacementDecision { Allowed = false, Rejection = rejection, Reason = reason };
        }
    }
}
