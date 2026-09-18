namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// How big a world tile is, expressed in the player's own local maps (#67). This is the shared
    /// answer to "how much ground is one tile?", and every population, residence and land number in
    /// the mod is scaled against it.
    ///
    /// <para><b>Where the number comes from.</b> RimWorld gives two mutually inconsistent scales and
    /// this class deliberately adopts the second:</para>
    /// <list type="bullet">
    /// <item><b>Movement equivalence.</b> <c>CaravanTicksPerMoveUtility.CellToTilesConversionRatio</c>
    /// is 340, so crossing a tile costs what walking 340 cells costs — about one local map per tile.
    /// Rejected: a full 250x250 map holds roughly 100 people at a realistic build density, and a
    /// planet whose tiles cap at 100 people cannot feed a regional population model. RimWorld also
    /// cannot build vertically, so that cap is real rather than a tuning choice.</item>
    /// <item><b>Travel time (adopted).</b> <c>DefaultTicksPerMove</c> 3300 divided by
    /// <c>GenDate.TicksPerHour</c> 2500 is 1.32 in-game hours to cross a tile. At a march of about
    /// 4.5 km/h that is 5.94 km, rounded here to a flat 6 km.</item>
    /// </list>
    ///
    /// <para>Vanilla therefore compresses overland travel roughly 19:1 against its own geography, the
    /// same way it compresses weapon ranges. That is a gameplay decision and Core does not touch it:
    /// caravan speed is left exactly as the game ships it. This class describes ground, not travel.</para>
    ///
    /// <para>Pure and deterministic (no game types), so it unit-tests against the hand-written doubles.
    /// Every number is a tunable endpoint, not a hidden constant.</para>
    /// </summary>
    public static class WorldScaleRules
    {
        /// <summary>RimWorld's <c>CaravanTicksPerMoveUtility.DefaultTicksPerMove</c> — ticks a default
        /// caravan spends crossing one world tile. Recorded here as the derivation's starting point.</summary>
        public const int DefaultCaravanTicksPerTile = 3300;

        /// <summary>RimWorld's <c>GenDate.TicksPerHour</c>.</summary>
        public const int TicksPerHour = 2500;

        /// <summary>A marching pace on foot, km/h. The one real-world quantity in the derivation.</summary>
        public const float DefaultMarchKmPerHour = 4.5f;

        /// <summary>Vertex-to-vertex width of a world tile, km. 1.32 travel hours at
        /// <see cref="DefaultMarchKmPerHour"/> is 5.94 km; rounded to 6 so the published ratios are
        /// round numbers. Re-derive with <see cref="TileAcrossKmFromMarch"/> to tune it.</summary>
        public const float TileAcrossKm = 6.0f;

        /// <summary>Area of a regular hexagon as a fraction of its vertex-to-vertex width squared:
        /// 3*sqrt(3)/8.</summary>
        public const float HexAreaFactor = 0.6495191f;

        /// <summary>Ground a single map cell covers. RimWorld never states this; 1 m is the furniture
        /// scale, since a bed is 1x2 cells and holds one person lying down.</summary>
        public const float MetresPerCell = 1.0f;

        /// <summary>RimWorld's default map edge, in cells. Used only when no world is loaded to ask.</summary>
        public const int DefaultMapEdgeCells = 250;

        /// <summary>In-game hours a default caravan spends crossing one tile: 1.32.</summary>
        public static float TravelHoursPerTile
        {
            get { return (float)DefaultCaravanTicksPerTile / TicksPerHour; }
        }

        /// <summary>Tile width in km implied by a given marching pace — the derivation, exposed so the
        /// adopted <see cref="TileAcrossKm"/> can be checked or retuned rather than taken on faith.</summary>
        public static float TileAcrossKmFromMarch(float kmPerHour)
        {
            if (kmPerHour <= 0f) return 0f;
            return TravelHoursPerTile * kmPerHour;
        }

        /// <summary>Ground area of one world tile, km^2. About 23.4.</summary>
        public static float TileAreaKm2
        {
            get { return HexAreaFactor * TileAcrossKm * TileAcrossKm; }
        }

        /// <summary>Ground area of one world tile, m^2.</summary>
        public static float TileAreaM2
        {
            get { return TileAreaKm2 * 1000000f; }
        }

        /// <summary>Ground one local map covers, m^2, for a square map of the given edge in cells.</summary>
        public static float MapAreaM2(int mapEdgeCells)
        {
            if (mapEdgeCells <= 0) return 0f;
            float edgeM = mapEdgeCells * MetresPerCell;
            return edgeM * edgeM;
        }

        /// <summary>Ground one local map covers, km^2.</summary>
        public static float MapAreaKm2(int mapEdgeCells)
        {
            return MapAreaM2(mapEdgeCells) / 1000000f;
        }

        /// <summary>
        /// How many local maps of this size tile one world tile. 374 at the default 250x250, about 94
        /// at 500x500 — so a larger-map mod shrinks the ratio instead of breaking the model.
        /// </summary>
        public static float MapsPerTile(int mapEdgeCells)
        {
            float mapArea = MapAreaM2(mapEdgeCells);
            if (mapArea <= 0f) return 0f;
            return TileAreaM2 / mapArea;
        }

        /// <summary>
        /// The side of the square grid of maps that covers one tile: 19.3 at 250x250, 9.7 at 500x500.
        /// This is also the tile's width measured in districts, which is the unit the district model
        /// (#69) and the pressure falloff (#70) both count in.
        /// </summary>
        public static float GridSide(int mapEdgeCells)
        {
            float maps = MapsPerTile(mapEdgeCells);
            if (maps <= 0f) return 0f;
            return Sqrt(maps);
        }

        /// <summary>Alias for <see cref="GridSide"/> in the vocabulary of the district model: one
        /// district is one local map, so a tile is this many districts across.</summary>
        public static float DistrictsAcrossTile(int mapEdgeCells)
        {
            return GridSide(mapEdgeCells);
        }

        /// <summary>One district (one local map) in km^2 — the unit the population density anchors
        /// multiply against.</summary>
        public static float DistrictAreaKm2(int mapEdgeCells)
        {
            return MapAreaKm2(mapEdgeCells);
        }

        /// <summary>
        /// A player-facing sentence: how small their map is against the tile it sits on, and the grid
        /// that would cover it. Rounded, because every number behind it is an approximation.
        /// </summary>
        public static string RatioLabel(int mapEdgeCells)
        {
            if (mapEdgeCells <= 0) return "unknown map size";
            int maps = RoundToInt(MapsPerTile(mapEdgeCells));
            int side = RoundToInt(GridSide(mapEdgeCells));
            return mapEdgeCells + "x" + mapEdgeCells + " map = 1/" + maps + " of a world tile ("
                + side + "x" + side + " grid, tile about " + TileAcrossKm.ToString("0.#") + " km across)";
        }

        // -- local maths, so the file stays free of UnityEngine ----------------

        private static float Sqrt(float v)
        {
            return v <= 0f ? 0f : (float)System.Math.Sqrt(v);
        }

        private static int RoundToInt(float v)
        {
            return (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
        }
    }
}
