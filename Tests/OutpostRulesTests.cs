// Behaviour tests for the 0.8 outpost-seeding rules: tier -> allowance, and terrain -> archetype.
//
// Both rule tables are pure — no Find, no Unity — so this suite is plain arithmetic and branching
// over the same numbers the seeding pass feeds them at runtime. What is under test is the mapping,
// which is the part that can actually be wrong.
using System;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Sizing;

namespace OutpostRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("outpost allowance ladder (Tier 1 = 2, +1 per tier)");
            Check("None anchors no outposts", OutpostAllowanceRules.OutpostAllowance(SettlementTier.None) == 0);
            Check("Village allows 2", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Village) == 2);
            Check("Town allows 3", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Town) == 3);
            Check("City allows 4", OutpostAllowanceRules.OutpostAllowance(SettlementTier.City) == 4);
            Check("Major City allows 5", OutpostAllowanceRules.OutpostAllowance(SettlementTier.MajorCity) == 5);
            Check("Metropolis allows 6", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Metropolis) == 6);
            Check("each tier allows exactly one more than the last",
                OutpostAllowanceRules.OutpostAllowance(SettlementTier.Town) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.Village) == 1
                && OutpostAllowanceRules.OutpostAllowance(SettlementTier.City) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.Town) == 1
                && OutpostAllowanceRules.OutpostAllowance(SettlementTier.MajorCity) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.City) == 1
                && OutpostAllowanceRules.OutpostAllowance(SettlementTier.Metropolis) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.MajorCity) == 1);

            Section("remaining allowance never goes negative");
            Check("an empty Village territory has room for 2", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Village, 0) == 2);
            Check("a Village with one outpost has room for 1", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Village, 1) == 1);
            Check("a Village at its allowance has no room", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Village, 2) == 0);
            Check("a territory over its allowance takes no more (never negative)", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Village, 5) == 0);

            Section("allowance flows from a tier classified out of population");
            // A tribal settlement (~50 pop) classifies as Town, so its territory allows three outposts.
            SettlementTier townTier = SettlementSizeEvaluator.Classify(WorldObjectKind.Settlement, 50);
            Check("a ~50-pop settlement is a Town", townTier == SettlementTier.Town);
            Check("...and a Town territory allows 3 outposts", OutpostAllowanceRules.OutpostAllowance(townTier) == 3);

            Section("archetype follows the dominant terrain signal");
            Check("mountainous ground is a mine",
                OutpostArchetypeRules.Choose(Features(hilliness: 3)) == OutpostArchetype.Mining);
            Check("large hills are a mine",
                OutpostArchetypeRules.Choose(Features(hilliness: 2)) == OutpostArchetype.Mining);
            Check("mineral-rich flat ground is a mine",
                OutpostArchetypeRules.Choose(Features(mineralsFraction: 0.8f)) == OutpostArchetype.Mining);
            Check("forest is a logging camp",
                OutpostArchetypeRules.Choose(Features(treeDensity: 0.6f)) == OutpostArchetype.Logging);
            Check("fertile flat land is a farm",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0)) == OutpostArchetype.Farming);
            Check("game-rich open land is a hunting camp",
                OutpostArchetypeRules.Choose(Features(animalDensity: 0.7f)) == OutpostArchetype.Hunting);
            Check("barren, featureless ground falls back to an encampment",
                OutpostArchetypeRules.Choose(Features()) == OutpostArchetype.Encampment);

            Section("archetype priority: rock beats vegetation");
            Check("a forested mountain is mined, not logged",
                OutpostArchetypeRules.Choose(Features(hilliness: 3, treeDensity: 0.9f)) == OutpostArchetype.Mining);
            Check("fertile hills that are not steep still farm",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 1)) == OutpostArchetype.Farming);
            Check("fertile land on steep ground is mined, not farmed",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.9f, hilliness: 2)) == OutpostArchetype.Mining);

            Section("position- and faction-aware archetype (#18 weighted scorer)");
            // No anchor context (anchorTier None) degrades to the terrain-only chain — the tests above.
            Check("no anchor context degrades to terrain only",
                OutpostArchetypeRules.Choose(Features(hilliness: 3)) == OutpostArchetype.Mining);

            // A fertile tile at a capital's core: a tribal capital farms it; an industrial capital makes
            // it a civic post instead.
            Check("tribal capital core, fertile -> Farming",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0, distanceToAnchor: 0.1f, anchorTier: SettlementTier.Town, techLevel: 2)) == OutpostArchetype.Farming);
            Check("industrial capital core, fertile -> a civic post, not a farm",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0, distanceToAnchor: 0.1f, anchorTier: SettlementTier.City, techLevel: 4)) != OutpostArchetype.Farming);

            // The periphery is for extraction and defence, not civic work.
            Check("industrial frontier mountains -> Mining",
                OutpostArchetypeRules.Choose(Features(hilliness: 3, plantDensity: 0.3f, distanceToAnchor: 0.9f, anchorTier: SettlementTier.City, techLevel: 4)) == OutpostArchetype.Mining);

            // Faction gate: a tribe cannot field the industrial-tech posts.
            Check("tribal frontier mountains -> Mining (never an industrial post)",
                OutpostArchetypeRules.Choose(Features(hilliness: 3, plantDensity: 0.3f, distanceToAnchor: 0.9f, anchorTier: SettlementTier.Town, techLevel: 2)) == OutpostArchetype.Mining);

            // Raiders read the land differently: salvage in the interior, fortlets on the frontier, never civic.
            Check("pirate frontier -> Defensive",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.3f, distanceToAnchor: 0.9f, anchorTier: SettlementTier.City, techLevel: 4, permanentEnemy: true)) == OutpostArchetype.Defensive);
            Check("pirate core -> Scavenging, not a civic post",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0, distanceToAnchor: 0.1f, anchorTier: SettlementTier.City, techLevel: 4, permanentEnemy: true)) == OutpostArchetype.Scavenging);

            Section("tier pyramid — triangular thresholds");
            Check("T1 needs 1 territory", TierPyramidRules.TerritoriesForTier(1) == 1);
            Check("T2 needs 3", TierPyramidRules.TerritoriesForTier(2) == 3);
            Check("T3 needs 6", TierPyramidRules.TerritoriesForTier(3) == 6);
            Check("T4 needs 10", TierPyramidRules.TerritoriesForTier(4) == 10);
            Check("T5 needs 15", TierPyramidRules.TerritoriesForTier(5) == 15);

            Section("max capital tier a settlement count affords");
            Check("0 settlements → tier 0", TierPyramidRules.MaxCapitalTier(0) == 0);
            Check("1 → T1", TierPyramidRules.MaxCapitalTier(1) == 1);
            Check("2 → still T1 (T2 needs 3)", TierPyramidRules.MaxCapitalTier(2) == 1);
            Check("3 → T2", TierPyramidRules.MaxCapitalTier(3) == 2);
            Check("5 → still T2", TierPyramidRules.MaxCapitalTier(5) == 2);
            Check("6 → T3", TierPyramidRules.MaxCapitalTier(6) == 3);
            Check("14 → T4 (T5 needs 15)", TierPyramidRules.MaxCapitalTier(14) == 4);
            Check("15 → T5", TierPyramidRules.MaxCapitalTier(15) == 5);
            Check("100 → capped at T5", TierPyramidRules.MaxCapitalTier(100) == 5);

            Section("tier counts — bottom-heavy staircase, each tier one wider");
            Check("N=15 is the exact 5-4-3-2-1 pyramid", CountsAre(TierPyramidRules.TierCounts(15), 5, 4, 3, 2, 1));
            Check("N=3 → 2 villages under 1 town", CountsAre(TierPyramidRules.TierCounts(3), 2, 1, 0, 0, 0));
            Check("N=1 → a lone village", CountsAre(TierPyramidRules.TierCounts(1), 1, 0, 0, 0, 0));
            Check("N=16 → the extra widens the base, not a second apex", CountsAre(TierPyramidRules.TierCounts(16), 6, 4, 3, 2, 1));
            Check("counts always sum to N", Sum(TierPyramidRules.TierCounts(23)) == 23 && Sum(TierPyramidRules.TierCounts(7)) == 7);
            Check("every tier is at least one wider than the one above", ValidPyramid(TierPyramidRules.TierCounts(23)) && ValidPyramid(TierPyramidRules.TierCounts(15)) && ValidPyramid(TierPyramidRules.TierCounts(2)));

            Section("tier by protection rank — the capital is rank 0");
            var p15 = TierPyramidRules.TierCounts(15);
            Check("most protected is the Metropolis capital", TierPyramidRules.TierForRank(0, p15) == SettlementTier.Metropolis);
            Check("ranks 1-2 are Major Cities", TierPyramidRules.TierForRank(1, p15) == SettlementTier.MajorCity && TierPyramidRules.TierForRank(2, p15) == SettlementTier.MajorCity);
            Check("ranks 3-5 are Cities", TierPyramidRules.TierForRank(3, p15) == SettlementTier.City && TierPyramidRules.TierForRank(5, p15) == SettlementTier.City);
            Check("ranks 6-9 are Towns", TierPyramidRules.TierForRank(6, p15) == SettlementTier.Town && TierPyramidRules.TierForRank(9, p15) == SettlementTier.Town);
            Check("ranks 10-14 are Villages", TierPyramidRules.TierForRank(10, p15) == SettlementTier.Village && TierPyramidRules.TierForRank(14, p15) == SettlementTier.Village);
            Check("a rank past the last settlement has no tier", TierPyramidRules.TierForRank(15, p15) == SettlementTier.None);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL OUTPOST-RULE TESTS PASSED" : failures + " OUTPOST-RULE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool CountsAre(int[] c, int t1, int t2, int t3, int t4, int t5)
        {
            return c[1] == t1 && c[2] == t2 && c[3] == t3 && c[4] == t4 && c[5] == t5;
        }

        private static int Sum(int[] c)
        {
            int s = 0;
            for (int i = 0; i < c.Length; i++) s += c[i];
            return s;
        }

        private static bool ValidPyramid(int[] c)
        {
            for (int t = 1; t < TierPyramidRules.MaxTier; t++)
            {
                if (c[t + 1] > 0 && c[t] < c[t + 1] + 1) return false;
            }
            return true;
        }

        private static TileFeatures Features(int hilliness = 0, float plantDensity = 0f, float treeDensity = 0f,
            float animalDensity = 0f, float mineralsFraction = 0f, bool coastal = false,
            float distanceToAnchor = 0f, SettlementTier anchorTier = SettlementTier.None,
            int techLevel = 4, bool permanentEnemy = false)
        {
            return new TileFeatures
            {
                hilliness = hilliness,
                plantDensity = plantDensity,
                treeDensity = treeDensity,
                animalDensity = animalDensity,
                mineralsFraction = mineralsFraction,
                coastal = coastal,
                distanceToAnchor = distanceToAnchor,
                anchorTier = anchorTier,
                techLevel = techLevel,
                permanentEnemy = permanentEnemy
            };
        }

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
