// Behaviour tests for the deterministic residence core: how a population resolves into homes, their
// occupancy, and the land each takes, driven by how urban a place is. Pure, so it runs without a game.
using System;
using RegionsAndSocieties.Demographics;

namespace ResidenceRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("urbanization rises from rural to city");
            Check("rural floor is 0", Close(ResidenceRules.Urbanization(ResidenceRules.RuralPopulation), 0f));
            Check("below the floor is 0", Close(ResidenceRules.Urbanization(3), 0f));
            Check("city ceiling is 1", Close(ResidenceRules.Urbanization(ResidenceRules.CityPopulation), 1f));
            Check("above the ceiling is 1", Close(ResidenceRules.Urbanization(9999), 1f));
            Check("midpoint is between", ResidenceRules.Urbanization(120) > 0.05f && ResidenceRules.Urbanization(120) < 0.95f);
            Check("monotonic in population", ResidenceRules.Urbanization(60) < ResidenceRules.Urbanization(160));

            Section("occupancy falls as it urbanises (extended -> nuclear)");
            Check("rural occupancy is the extended family", Close(ResidenceRules.Occupancy(0f), ResidenceRules.RuralOccupancy));
            Check("urban occupancy is nuclear", Close(ResidenceRules.Occupancy(1f), ResidenceRules.UrbanOccupancy));
            Check("occupancy is monotonically lower when more urban", ResidenceRules.Occupancy(0.2f) > ResidenceRules.Occupancy(0.8f));
            Check("rural holds more per home than urban", ResidenceRules.RuralOccupancy > ResidenceRules.UrbanOccupancy);

            Section("residences derive from population and occupancy");
            var country = ResidenceRules.For(14);   // a hamlet
            var city = ResidenceRules.For(280);      // a city
            Check("empty population has no residences", ResidenceRules.For(0).residences == 0);
            Check("a person always has at least one home", ResidenceRules.For(1).residences >= 1);
            Check("country: few big homes", country.residences <= 3);
            Check("country reads as a homestead/village", country.tier == ResidenceTier.Homestead || country.tier == ResidenceTier.Village);
            Check("city: many small homes", city.residences >= 100);
            Check("city reads as a city", city.tier == ResidenceTier.City);
            Check("city has more homes per person than country",
                (city.residences / (float)city.population) > (country.residences / (float)country.population));
            Check("residences x occupancy reconstructs population (country)", Math.Abs(country.residences * country.occupancy - country.population) <= country.occupancy);
            Check("residences x occupancy reconstructs population (city)", Math.Abs(city.residences * city.occupancy - city.population) <= city.occupancy);

            Section("land shrinks and packs in as it urbanises");
            Check("rural residence has a wide plot", Close(ResidenceRules.LandPerResidence(0f), ResidenceRules.RuralLandPerResidence));
            Check("urban residence has a tight lot", Close(ResidenceRules.LandPerResidence(1f), ResidenceRules.UrbanLandPerResidence));
            Check("residence land shrinks with urbanization", ResidenceRules.LandPerResidence(0.2f) > ResidenceRules.LandPerResidence(0.8f));
            Check("fewer tiles per pawn in the city", country.landPerPawn > city.landPerPawn);

            Section("tiers step up with urbanization");
            Check("0 is homestead", ResidenceRules.TierFor(0f) == ResidenceTier.Homestead);
            Check("mid-low is village", ResidenceRules.TierFor(0.3f) == ResidenceTier.Village);
            Check("mid-high is town", ResidenceRules.TierFor(0.6f) == ResidenceTier.Town);
            Check("high is city", ResidenceRules.TierFor(0.9f) == ResidenceTier.City);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL RESIDENCE TESTS PASSED" : failures + " RESIDENCE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0005f;

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
