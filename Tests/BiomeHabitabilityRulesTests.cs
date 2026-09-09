// Behaviour tests for biome habitability (#56): vanilla's settlement weight × toil × health, scaled
// by tech; the per-tile land features; and the placement score that multiplies resources by
// habitability while leaving the marginal-land term additive. Pure, no game.
using System;
using RegionsAndSocieties;
using RegionsAndSocieties.Placement;

namespace BiomeHabitabilityRulesTests
{
    public static class Program
    {
        private static int failures;

        // Vanilla 1.6 numbers (plant, forage, tree, movement, diseaseMtb, settlementWeight).
        private static BiomeTraits Temperate()  { return T(0.65f, 1.00f, 0.80f, 1f, 50f, 1.00f); }
        private static BiomeTraits Boreal()     { return T(0.40f, 0.75f, 0.70f, 1f, 60f, 0.90f); }
        private static BiomeTraits Rainforest() { return T(0.90f, 1.00f, 1.00f, 2f, 35f, 0.70f); }
        private static BiomeTraits TropSwamp()  { return T(0.99f, 0.75f, 0.60f, 4f, 30f, 0.40f); }
        private static BiomeTraits Tundra()     { return T(0.19f, 0.50f, 0.10f, 1f, 80f, 0.32f); }
        private static BiomeTraits Arid()       { return T(0.24f, 0.50f, 0.20f, 1f, 65f, 0.95f); }
        private static BiomeTraits Desert()     { return T(0.05f, 0.25f, 0.02f, 1f, 90f, 0.65f); }
        private static BiomeTraits Glowforest() { return T(0.75f, 1.00f, 0.90f, 2f, 50f, 0.05f); }

        public static int Main()
        {
            const int tribal = 2, industrial = 4, spacer = 5;

            Section("penalty exponent by tech");
            Check("tribal eats the full penalty", BiomeHabitabilityRules.PenaltyExponent(tribal) == 1f);
            Check("industrial halves it", BiomeHabitabilityRules.PenaltyExponent(industrial) == 0.5f);
            Check("spacer quarters it", BiomeHabitabilityRules.PenaltyExponent(spacer) == 0.25f);
            Check("ultra reads as spacer", BiomeHabitabilityRules.PenaltyExponent(6) == 0.25f);

            Section("toil");
            Check("open ground is 1", BiomeHabitabilityRules.Toil(1f, tribal) == 1f);
            Check("below 1 is clamped to open ground", BiomeHabitabilityRules.Toil(0.5f, tribal) == 1f);
            Check("jungle halves a tribe", Close(BiomeHabitabilityRules.Toil(2f, tribal), 0.5f));
            Check("swamp quarters a tribe", Close(BiomeHabitabilityRules.Toil(4f, tribal), 0.25f));
            Check("machines soften the jungle", Close(BiomeHabitabilityRules.Toil(2f, industrial), 0.707f));
            Check("spacers barely notice", Close(BiomeHabitabilityRules.Toil(2f, spacer), 0.841f));

            Section("health");
            Check("60 days is neutral", BiomeHabitabilityRules.Health(60f, tribal) == 1f);
            Check("unset interval is neutral", BiomeHabitabilityRules.Health(0f, tribal) == 1f);
            Check("35-day jungle sickens a tribe", Close(BiomeHabitabilityRules.Health(35f, tribal), 0.583f));
            Check("30-day swamp hits the floor", Close(BiomeHabitabilityRules.Health(30f, tribal), 0.5f));
            Check("desert healthier, capped", Close(BiomeHabitabilityRules.Health(90f, tribal), 1.25f));
            Check("tundra 80 days capped too", Close(BiomeHabitabilityRules.Health(80f, tribal), 1.25f));
            Check("medicine softens the jungle", Close(BiomeHabitabilityRules.Health(35f, industrial), 0.764f));

            Section("habitability table, pre-industrial (the owner's ask: rainforest avoided, forests livable)");
            float hTemp = BiomeHabitabilityRules.Habitability(Temperate(), tribal);
            float hBoreal = BiomeHabitabilityRules.Habitability(Boreal(), tribal);
            float hRain = BiomeHabitabilityRules.Habitability(Rainforest(), tribal);
            float hSwamp = BiomeHabitabilityRules.Habitability(TropSwamp(), tribal);
            float hTundra = BiomeHabitabilityRules.Habitability(Tundra(), tribal);
            float hArid = BiomeHabitabilityRules.Habitability(Arid(), tribal);
            float hDesert = BiomeHabitabilityRules.Habitability(Desert(), tribal);
            Check($"temperate forest ~0.83 (got {hTemp:0.000})", Close(hTemp, 0.833f));
            Check($"boreal forest ~0.90 (got {hBoreal:0.000})", Close(hBoreal, 0.90f));
            Check($"rainforest ~0.20 (got {hRain:0.000})", Close(hRain, 0.204f));
            Check($"tropical swamp ~0.05 (got {hSwamp:0.000})", Close(hSwamp, 0.05f));
            Check($"tundra ~0.40 (got {hTundra:0.000})", Close(hTundra, 0.40f));
            Check($"arid shrubland ~1.03 (got {hArid:0.000})", Close(hArid, 1.029f));
            Check($"desert ~0.81 (got {hDesert:0.000})", Close(hDesert, 0.8125f));
            Check("forests beat rainforest by 4x", hTemp > 4f * hRain && hBoreal > 4f * hRain);
            Check("rainforest still beats the swamp", hRain > hSwamp);
            Check("vanilla-shunned glowforest is near zero", BiomeHabitabilityRules.Habitability(Glowforest(), tribal) < 0.03f);
            var never = Glowforest(); never.SettlementSelectionWeight = 0f;
            Check("settlement weight 0 -> 0", BiomeHabitabilityRules.Habitability(never, tribal) == 0f);
            never.SettlementSelectionWeight = -1f;
            Check("negative weight reads as 0", BiomeHabitabilityRules.Habitability(never, tribal) == 0f);

            Section("tech lifts the penalised biomes, never the open ones");
            float hRainInd = BiomeHabitabilityRules.Habitability(Rainforest(), industrial);
            float hRainSp = BiomeHabitabilityRules.Habitability(Rainforest(), spacer);
            Check($"rainforest: tribal {hRain:0.00} < industrial {hRainInd:0.00} < spacer {hRainSp:0.00}", hRain < hRainInd && hRainInd < hRainSp);
            Check("industrial rainforest ~0.38", Close(hRainInd, 0.378f));
            Check("spacer rainforest ~0.51", Close(hRainSp, 0.514f));
            Check("open boreal forest is the same at every tech", Close(BiomeHabitabilityRules.Habitability(Boreal(), spacer), hBoreal));
            Check("even a spacer prefers temperate forest to rainforest", BiomeHabitabilityRules.Habitability(Temperate(), spacer) > hRainSp);

            Section("monotonic in each input");
            var a = Temperate(); var b = Temperate(); b.MovementDifficulty = 3f;
            Check("harder ground -> lower", BiomeHabitabilityRules.Habitability(b, tribal) < BiomeHabitabilityRules.Habitability(a, tribal));
            b = Temperate(); b.DiseaseMtbDays = 40f;
            Check("sicker -> lower", BiomeHabitabilityRules.Habitability(b, tribal) < BiomeHabitabilityRules.Habitability(a, tribal));
            b = Temperate(); b.SettlementSelectionWeight = 0.5f;
            Check("lower vanilla weight -> lower", BiomeHabitabilityRules.Habitability(b, tribal) < BiomeHabitabilityRules.Habitability(a, tribal));

            Section("tile features");
            var flat = BiomeHabitabilityRules.Features(Temperate(), 0);
            var hills = BiomeHabitabilityRules.Features(Temperate(), 2);
            var mountain = BiomeHabitabilityRules.Features(Temperate(), 3);
            Check("mineral by hill class", flat.Mineral == 0.5f && BiomeHabitabilityRules.Mineral(1) == 1f && hills.Mineral == 2f && mountain.Mineral == 3f);
            Check("nutrition = plant density", flat.Nutrition == 0.65f);
            Check("forage = forageability", flat.Forage == 1f);
            Check("grazing doubles on flat ground", Close(flat.Grazing, 1.3f) && Close(hills.Grazing, 0.65f));
            Check("biomass = tree density", flat.Biomass == 0.8f);
            Check("margin = 3 - (2*plant + forage), floored at 0", Close(flat.Margin, 3f - (1.3f + 1f)) && BiomeHabitabilityRules.Features(TropSwamp(), 0).Margin >= 0f);
            Check("desert has margin to spare", BiomeHabitabilityRules.Features(Desert(), 0).Margin > 2.5f);
            var neutral = BiomeTraits.Neutral;
            Check("neutral traits are fully habitable", BiomeHabitabilityRules.Habitability(neutral, tribal) == 1f);

            Section("score: habitability multiplies the whole weighted score, margin included");
            var tribe = new PlacementWeights(0.2f, 0.2f, 2.0f, 2.0f, 0.2f, 0.1f);      // gentle tribe profile
            var raider = new PlacementWeights(0.2f, 0.2f, 2.0f, 0.2f, 2.0f, 2.5f);     // fierce tribe / raider profile
            float sTemp = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Temperate(), 0), hTemp, tribe);
            float sBoreal = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Boreal(), 0), hBoreal, tribe);
            float sRain = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Rainforest(), 0), hRain, tribe);
            float sTundra = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Tundra(), 0), hTundra, tribe);
            float sDesert = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Desert(), 0), hDesert, tribe);
            Check($"tribe: temperate {sTemp:0.00} > boreal {sBoreal:0.00}", sTemp > sBoreal);
            Check($"tribe: boreal {sBoreal:0.00} > rainforest {sRain:0.00}", sBoreal > sRain);
            Check($"tribe: rainforest {sRain:0.00} > tundra {sTundra:0.00}", sRain > sTundra);
            Check($"tribe: tundra {sTundra:0.00} and desert {sDesert:0.00} both sit far below the forests", sTundra < 0.5f * sBoreal && sDesert < 0.5f * sBoreal);
            // The old score (habitability 1 everywhere) put rainforest on top for the same tribe.
            float oldRain = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Rainforest(), 0), 1f, tribe);
            float oldTemp = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Temperate(), 0), 1f, tribe);
            Check("without habitability rainforest out-scored temperate forest (the bug)", oldRain > oldTemp);
            // Margin is inside the multiplier: a raider wants the livable fringe, not an ice sheet.
            var ice = T(0f, 0f, 0f, 1.5f, 90f, 0.15f);
            float hIce = BiomeHabitabilityRules.Habitability(ice, tribal);
            float rIce = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(ice, 0), hIce, raider);
            float rArid = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Arid(), 0), hArid, raider);
            float rDesert = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Desert(), 0), hDesert, raider);
            float rTemp = BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Temperate(), 0), hTemp, raider);
            Check($"raider: arid shrubland {rArid:0.00} beats ice sheet {rIce:0.00}", rArid > rIce);
            Check($"raider: desert edge {rDesert:0.00} beats ice sheet {rIce:0.00}", rDesert > rIce);
            Check($"raider still prefers the fringe over the forest heartland ({rArid:0.00} vs {rTemp:0.00})", rArid > rTemp);
            Check("habitability 0 -> 0 even with margin", BiomeHabitabilityRules.Score(BiomeHabitabilityRules.Features(Desert(), 0), 0f, raider) == 0f);
            var unit = new PlacementWeights(1f, 1f, 1f, 1f, 1f, 0f);
            Check("habitability scales linearly", Close(BiomeHabitabilityRules.Score(flat, 0.5f, unit), 0.5f * BiomeHabitabilityRules.Score(flat, 1f, unit)));
            Check("zero margin weight ignores margin", Close(BiomeHabitabilityRules.Score(flat, 1f, unit), flat.Mineral + flat.Nutrition + flat.Forage + flat.Grazing + flat.Biomass));

            Section("crowding: a biome filling up pushes its next candidates down, never to zero");
            Check("empty biome is 1", BiomeHabitabilityRules.Crowding(0, 20) == 1f);
            Check("unknown availability is 1", BiomeHabitabilityRules.Crowding(5, 0) == 1f);
            Check("half full -> 2/3", Close(BiomeHabitabilityRules.Crowding(10, 20), 2f / 3f));
            Check("as full as it is big -> 1/2", Close(BiomeHabitabilityRules.Crowding(20, 20), 0.5f));
            Check("over-full keeps falling", BiomeHabitabilityRules.Crowding(40, 20) < BiomeHabitabilityRules.Crowding(20, 20));
            Check("never zero", BiomeHabitabilityRules.Crowding(1000, 1) > 0f);
            Check("a small biome crowds faster than a big one at the same count", BiomeHabitabilityRules.Crowding(5, 10) < BiomeHabitabilityRules.Crowding(5, 100));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL BIOME-HABITABILITY TESTS PASSED" : failures + " BIOME-HABITABILITY TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static BiomeTraits T(float plant, float forage, float tree, float move, float disease, float weight)
        {
            return new BiomeTraits
            {
                PlantDensity = plant, Forageability = forage, TreeDensity = tree,
                MovementDifficulty = move, DiseaseMtbDays = disease, SettlementSelectionWeight = weight,
            };
        }

        private static bool Close(float a, float b, float eps = 0.005f) { return Math.Abs(a - b) <= eps; }
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
