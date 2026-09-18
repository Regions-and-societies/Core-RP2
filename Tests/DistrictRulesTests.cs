// Behaviour tests for the district model (#69): how a settlement occupies its tile, and how the
// player's own colony is reconciled with the tile's simulated population. Pure, no game.
using System;
using RegionsAndSocieties.Sizing;

namespace DistrictRulesTests
{
    public static class Program
    {
        private static int failures;
        private const int Default = 250;

        public static int Main()
        {
            Section("ring geometry — the centered hexagonal numbers");
            Check("ring 0 is the rendered map alone", DistrictRules.DistrictsInRings(0) == 1);
            Check("ring 1 adds the six neighbours", DistrictRules.DistrictsInRings(1) == 7);
            Check("ring 2 is 19", DistrictRules.DistrictsInRings(2) == 19);
            Check("ring 3 is 37", DistrictRules.DistrictsInRings(3) == 61 - 24);
            Check("ring 4 is 61", DistrictRules.DistrictsInRings(4) == 61);
            Check("negative rings degrade to the single district", DistrictRules.DistrictsInRings(-2) == 1);
            Check("rings for 1 district is 0", DistrictRules.RingsForDistricts(1) == 0);
            Check("rings for 7 is 1", DistrictRules.RingsForDistricts(7) == 1);
            Check("a count between rings rounds up", DistrictRules.RingsForDistricts(8) == 2 && DistrictRules.RingsForDistricts(20) == 3);
            Check("inverse round-trips on exact counts", DistrictRules.RingsForDistricts(DistrictRules.DistrictsInRings(3)) == 3);

            Section("tier to districts");
            Check("Homestead 1", DistrictRules.DistrictsForTier(DistrictTier.Homestead) == 1);
            Check("Village 7", DistrictRules.DistrictsForTier(DistrictTier.Village) == 7);
            Check("Town 19", DistrictRules.DistrictsForTier(DistrictTier.Town) == 19);
            Check("City 37", DistrictRules.DistrictsForTier(DistrictTier.City) == 37);
            Check("Metropolis 61", DistrictRules.DistrictsForTier(DistrictTier.Metropolis) == 61);

            Section("people per district — the measured playtest density");
            // 1,600 people/km2 x 0.0625 km2 = 100 on a default map.
            Check("100 per district at 250x250", Near(DistrictRules.PeoplePerDistrict(250), 100f, 0.5f));
            Check("400 per district at 500x500", Near(DistrictRules.PeoplePerDistrict(500), 400f, 1f));
            Check("rescales with map size, so a bigger map means fewer, fuller districts",
                DistrictRules.PeoplePerDistrict(500) > DistrictRules.PeoplePerDistrict(250));
            Check("a nonsense map size yields nothing", DistrictRules.PeoplePerDistrict(0) == 0f);

            Section("tier populations");
            Check("Homestead ~100", Near(DistrictRules.PopulationForTier(DistrictTier.Homestead, Default), 100, 2));
            Check("Village ~700", Near(DistrictRules.PopulationForTier(DistrictTier.Village, Default), 700, 5));
            Check("Town ~1,900", Near(DistrictRules.PopulationForTier(DistrictTier.Town, Default), 1900, 10));
            Check("City ~3,700", Near(DistrictRules.PopulationForTier(DistrictTier.City, Default), 3700, 20));
            Check("Metropolis ~6,100", Near(DistrictRules.PopulationForTier(DistrictTier.Metropolis, Default), 6100, 30));

            Section("population back to tier and districts");
            Check("50 people is a homestead", DistrictRules.TierForPopulation(50, Default) == DistrictTier.Homestead);
            Check("700 is a village", DistrictRules.TierForPopulation(700, Default) == DistrictTier.Village);
            Check("2,000 is a town", DistrictRules.TierForPopulation(2000, Default) == DistrictTier.Town);
            Check("4,000 is a city", DistrictRules.TierForPopulation(4000, Default) == DistrictTier.City);
            Check("50,000 tops out at metropolis", DistrictRules.TierForPopulation(50000, Default) == DistrictTier.Metropolis);
            Check("tier is monotonic in population",
                (int)DistrictRules.TierForPopulation(300, Default) <= (int)DistrictRules.TierForPopulation(3000, Default));
            Check("250 people needs 3 districts", DistrictRules.DistrictsForPopulation(250, Default) == 3);
            Check("any population at all occupies a district", DistrictRules.DistrictsForPopulation(1, Default) == 1);
            Check("nobody occupies nothing", DistrictRules.DistrictsForPopulation(0, Default) == 0);
            Check("the ring cap bounds it", DistrictRules.DistrictsForPopulation(999999, Default) == 61);
            Check("a wider cap allows more", DistrictRules.DistrictsForPopulation(999999, Default, 6) == 127);

            Section("the tile as a whole — settled cluster plus hinterland");
            Check("a metropolis uses about a sixth of its tile",
                Near(DistrictRules.SettledShareOfTile(61, Default), 0.163f, 0.005f));
            Check("a homestead uses almost none", DistrictRules.SettledShareOfTile(1, Default) < 0.005f);
            Check("every tier leaves most of the tile as hinterland", DistrictRules.SettledShareOfTile(61, Default) < 0.5f);
            Check("share never exceeds the whole tile", DistrictRules.SettledShareOfTile(100000, Default) == 1f);
            Check("hinterland is a quarter again", DistrictRules.HinterlandPopulation(1000) == 250);
            Check("nobody settled means nobody outside", DistrictRules.HinterlandPopulation(0) == 0);
            Check("tile total is settled plus hinterland", DistrictRules.TilePopulation(1000) == 1250);
            // The share must keep the open country sane at both ends of the ladder.
            Check("a homestead's countryside is empty (~1/km2)",
                DistrictRules.HinterlandDensityPerKm2(100, 1, Default) < 2f);
            Check("a metropolis's countryside is farmed (~78/km2)",
                Near(DistrictRules.HinterlandDensityPerKm2(6100, 61, Default), 78f, 5f));

            Section("the player's tier comes from development, not head count");
            Check("an undeveloped map is a homestead", DistrictRules.TierFromDevelopment(0.01f, 1f) == DistrictTier.Homestead);
            Check("a quarter-developed rich map is a town", DistrictRules.TierFromDevelopment(0.25f, 1.5f) == DistrictTier.Town);
            Check("wealth promotes a given footprint",
                (int)DistrictRules.TierFromDevelopment(0.3f, 2.5f) > (int)DistrictRules.TierFromDevelopment(0.3f, 1f));
            Check("a fully built rich map is a metropolis", DistrictRules.TierFromDevelopment(1f, 1.5f) == DistrictTier.Metropolis);
            Check("nothing built is a homestead whatever the wealth", DistrictRules.TierFromDevelopment(0f, 10f) == DistrictTier.Homestead);
            Check("absent wealth data is treated as ordinary, not as zero",
                DistrictRules.TierFromDevelopment(0.5f, 0f) == DistrictRules.TierFromDevelopment(0.5f, 1f));

            Section("the player's count is authoritative and never penalised");
            // 40 colonists at Town: 40 on the rendered map + 18 surrounding districts x 100, + hinterland.
            int town40 = DistrictRules.PlayerTilePopulation(40, DistrictTier.Town, Default);
            Check("40 colonists in a town tile reads ~2,300", Near(town40, 2300, 30));
            Check("the colonists are added, never replaced by a modelled number",
                DistrictRules.PlayerTilePopulation(40, DistrictTier.Homestead, Default) >= 40);
            Check("a lone homestead tile is the colony plus its own countryside",
                DistrictRules.PlayerTilePopulation(40, DistrictTier.Homestead, Default) == DistrictRules.TilePopulation(40));
            Check("promotion adds suburbs rather than demanding more colonists",
                DistrictRules.PlayerTilePopulation(40, DistrictTier.City, Default) > town40);
            Check("more colonists always means more people, at a fixed tier",
                DistrictRules.PlayerTilePopulation(80, DistrictTier.Town, Default) > town40);
            Check("an empty colony still has its surrounding districts",
                DistrictRules.PlayerTilePopulation(0, DistrictTier.Town, Default) > 0);
            Check("a negative count is treated as none", DistrictRules.PlayerTilePopulation(-5, DistrictTier.Town, Default)
                == DistrictRules.PlayerTilePopulation(0, DistrictTier.Town, Default));
            Check("the rendered share is small but stated", DistrictRules.RenderedShare(40, DistrictTier.Town, Default) > 0f
                && DistrictRules.RenderedShare(40, DistrictTier.Town, Default) < 0.05f);
            // The reconciliation the whole model exists for: a developed colony is close to one district.
            Check("a fully built map is within ~2.5x of one district's modelled population",
                DistrictRules.PeoplePerDistrict(Default) / 40f < 3f);

            Section("built radius — what the pressure falloff (#70) scales from");
            Check("one district is a radius of ~0.56 districts", Near(DistrictRules.BuiltRadiusDistricts(1), 0.564f, 0.01f));
            Check("ring k gives about k + 0.5", Near(DistrictRules.BuiltRadiusDistricts(19), 2.46f, 0.05f)
                && Near(DistrictRules.BuiltRadiusDistricts(61), 4.41f, 0.05f));
            Check("no districts, no radius", DistrictRules.BuiltRadiusDistricts(0) == 0f);
            // Radius must go as the square root of population, not linearly: that is the #70 fix.
            float r100 = DistrictRules.BuiltRadiusTiles(100, Default);
            float r400 = DistrictRules.BuiltRadiusTiles(400, Default);
            Check("quadrupling population doubles the radius", Near(r400 / r100, 2f, 0.06f));
            Check("a metropolis is still only a fraction of a tile across",
                DistrictRules.BuiltRadiusTiles(6100, Default) < 0.3f);
            Check("radius grows with population", DistrictRules.BuiltRadiusTiles(6100, Default) > r100);
            Check("nobody has no radius", DistrictRules.BuiltRadiusTiles(0, Default) == 0f);
            Check("an absurd ring count saturates instead of overflowing negative", DistrictRules.DistrictsInRings(1000000) > 0);
            Check("uncapped districts exceed the display cap for a huge population",
                DistrictRules.DistrictsForPopulationUncapped(999999, Default) > DistrictRules.DistrictsForPopulation(999999, Default));

            Console.WriteLine();
            if (failures == 0) { Console.WriteLine("ALL DISTRICT TESTS PASSED"); return 0; }
            Console.WriteLine(failures + " DISTRICT TEST(S) FAILED");
            return 1;
        }

        private static bool Near(float a, float b, float tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static bool Near(int a, int b, int tolerance)
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
