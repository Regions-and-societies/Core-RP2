// Behaviour tests for the world-scale core (#67): how big a world tile is in the player's own local
// maps, derived from RimWorld's travel clock. Pure, no game.
using System;
using RegionsAndSocieties.Sizing;

namespace WorldScaleRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("the derivation from RimWorld's own constants");
            // 3300 ticks per tile / 2500 ticks per hour = 1.32 in-game hours of marching.
            Check("1.32 travel hours per tile", Near(WorldScaleRules.TravelHoursPerTile, 1.32f, 0.001f));
            Check("1.32 h at 4.5 km/h = 5.94 km", Near(WorldScaleRules.TileAcrossKmFromMarch(4.5f), 5.94f, 0.01f));
            Check("adopted width rounds that to 6 km", Near(WorldScaleRules.TileAcrossKm, 6.0f, 0.001f));
            Check("a faster march implies a wider tile", WorldScaleRules.TileAcrossKmFromMarch(6f) > WorldScaleRules.TileAcrossKmFromMarch(4.5f));
            Check("a nonsense pace yields nothing rather than a negative tile", WorldScaleRules.TileAcrossKmFromMarch(0f) == 0f
                && WorldScaleRules.TileAcrossKmFromMarch(-3f) == 0f);

            Section("tile area");
            // Regular hexagon, 6 km vertex to vertex: 3*sqrt(3)/8 * 36 = 23.38 km^2.
            Check("tile is ~23.4 km^2", Near(WorldScaleRules.TileAreaKm2, 23.38f, 0.05f));
            Check("km^2 and m^2 agree", Near(WorldScaleRules.TileAreaM2, WorldScaleRules.TileAreaKm2 * 1000000f, 1f));

            Section("map area, at 1 m per cell");
            Check("250x250 is 62,500 m^2", Near(WorldScaleRules.MapAreaM2(250), 62500f, 0.5f));
            Check("500x500 is 250,000 m^2", Near(WorldScaleRules.MapAreaM2(500), 250000f, 0.5f));
            Check("250x250 is 0.0625 km^2", Near(WorldScaleRules.MapAreaKm2(250), 0.0625f, 0.0001f));
            Check("a nonsense edge has no area", WorldScaleRules.MapAreaM2(0) == 0f && WorldScaleRules.MapAreaM2(-250) == 0f);

            Section("maps per tile — the headline ratio");
            Check("250x250 (default) -> ~374 maps", Near(WorldScaleRules.MapsPerTile(250), 374f, 1f));
            Check("200x200 -> ~585 maps", Near(WorldScaleRules.MapsPerTile(200), 585f, 2f));
            Check("300x300 -> ~260 maps", Near(WorldScaleRules.MapsPerTile(300), 260f, 1f));
            Check("400x400 -> ~146 maps", Near(WorldScaleRules.MapsPerTile(400), 146f, 1f));
            Check("500x500 -> ~94 maps", Near(WorldScaleRules.MapsPerTile(500), 94f, 1f));
            Check("a bigger map always means fewer of them per tile", WorldScaleRules.MapsPerTile(500) < WorldScaleRules.MapsPerTile(250));
            Check("doubling the edge quarters the count", Near(WorldScaleRules.MapsPerTile(250) / WorldScaleRules.MapsPerTile(500), 4f, 0.01f));
            Check("a nonsense edge yields nothing, not a divide by zero", WorldScaleRules.MapsPerTile(0) == 0f);

            Section("grid side, and districts across a tile");
            Check("250x250 -> 19.3 per side", Near(WorldScaleRules.GridSide(250), 19.34f, 0.05f));
            Check("500x500 -> 9.7 per side", Near(WorldScaleRules.GridSide(500), 9.67f, 0.05f));
            Check("grid side squared is maps per tile", Near(WorldScaleRules.GridSide(250) * WorldScaleRules.GridSide(250), WorldScaleRules.MapsPerTile(250), 0.1f));
            Check("districts across = grid side (one district is one map)", WorldScaleRules.DistrictsAcrossTile(250) == WorldScaleRules.GridSide(250));
            Check("district area = map area", WorldScaleRules.DistrictAreaKm2(250) == WorldScaleRules.MapAreaKm2(250));
            Check("a nonsense edge has no grid", WorldScaleRules.GridSide(0) == 0f);

            Section("the sentence the player reads");
            string s = WorldScaleRules.RatioLabel(250);
            Check("names the map size", s.Contains("250x250"));
            Check("gives the fraction", s.Contains("1/374"));
            Check("gives the grid", s.Contains("19x19"));
            Check("500x500 reads as a smaller ratio", WorldScaleRules.RatioLabel(500).Contains("1/94"));
            Check("a nonsense size says so rather than dividing by zero", WorldScaleRules.RatioLabel(0) == "unknown map size");

            Section("the scale the mod rejected, for the record");
            // CellToTilesConversionRatio = 340 would make a tile ~1.2 maps. Confirm the adopted scale is
            // the geographic one, two orders of magnitude away, so a regression back to it is loud.
            Check("adopted scale is far above the movement scale", WorldScaleRules.MapsPerTile(250) > 100f);

            Console.WriteLine();
            if (failures == 0) { Console.WriteLine("ALL WORLD SCALE TESTS PASSED"); return 0; }
            Console.WriteLine(failures + " WORLD SCALE TEST(S) FAILED");
            return 1;
        }

        private static bool Near(float a, float b, float tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string what, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        }
    }
}
