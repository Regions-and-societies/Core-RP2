// Behaviour tests for the faction-character layer (#27): base/DLC factions classified into archetypes,
// each archetype's knowledge/wealth skew, the modded/VFE trait fallback, and the end-to-end effect that a
// raider reads lower education and wealth than a settled outlander at the SAME tech level. Pure, no game.
using System;
using RegionsAndSocieties.Demographics;

namespace FactionCharacterRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            const int industrial = 4, neolithic = 2, spacer = 5;

            Section("known base/DLC factions classify by defName");
            Check("Pirate -> Raider", FactionCharacterRules.Classify("Pirate", industrial, true) == FactionArchetype.Raider);
            Check("waster pirates -> Raider", FactionCharacterRules.Classify("PirateWaster", industrial, true) == FactionArchetype.Raider);
            Check("OutlanderCivil -> Outlander", FactionCharacterRules.Classify("OutlanderCivil", industrial, false) == FactionArchetype.Outlander);
            Check("gentle tribe -> Tribe", FactionCharacterRules.Classify("TribeCivil", neolithic, false) == FactionArchetype.Tribe);
            Check("Empire -> Imperial", FactionCharacterRules.Classify("Empire", spacer, false) == FactionArchetype.Imperial);
            Check("TradersGuild -> Merchant", FactionCharacterRules.Classify("TradersGuild", spacer, false) == FactionArchetype.Merchant);
            Check("Salvagers -> Scavenger", FactionCharacterRules.Classify("Salvagers", spacer, false) == FactionArchetype.Scavenger);
            Check("Sanguophages -> AncientElite", FactionCharacterRules.Classify("Sanguophages", spacer, false) == FactionArchetype.AncientElite);
            Check("HoraxCult -> Cult", FactionCharacterRules.Classify("HoraxCult", spacer, true) == FactionArchetype.Cult);

            Section("unknown (modded/VFE) factions fall back on traits");
            Check("permanent-enemy industrial band -> Raider", FactionCharacterRules.Classify("VFE_SomePirateClan", industrial, true) == FactionArchetype.Raider);
            Check("neolithic unknown -> Tribe", FactionCharacterRules.Classify("VFE_SomeTribe", neolithic, false) == FactionArchetype.Tribe);
            Check("peaceful industrial unknown -> Generic", FactionCharacterRules.Classify("VFE_SomeTown", industrial, false) == FactionArchetype.Generic);
            Check("null defName is survivable", FactionCharacterRules.Classify(null, industrial, false) == FactionArchetype.Generic);

            Section("archetype modifiers point the right way");
            var raider = FactionCharacterRules.CharacterOf(FactionArchetype.Raider);
            var outlander = FactionCharacterRules.CharacterOf(FactionArchetype.Outlander);
            var imperial = FactionCharacterRules.CharacterOf(FactionArchetype.Imperial);
            var generic = FactionCharacterRules.CharacterOf(FactionArchetype.Generic);
            Check("raider skews knowledge down", raider.knowledgeSkew < 0f);
            Check("raider is poorer", raider.wealthMultiplier < 1f);
            Check("imperial skews knowledge up", imperial.knowledgeSkew > 0f);
            Check("merchant is richest", FactionCharacterRules.CharacterOf(FactionArchetype.Merchant).wealthMultiplier > imperial.wealthMultiplier);
            Check("generic is neutral", generic.knowledgeSkew == 0f && generic.wealthMultiplier == 1f);

            Section("end to end: a pirate reads less educated than an outlander at the same tech");
            // Education research-skew is the ideology skew (0 here) plus the character knowledge skew.
            float raiderEduIndex = EducationRules.Index(EducationRules.Pyramid(industrial, raider.knowledgeSkew, 0f));
            float outlanderEduIndex = EducationRules.Index(EducationRules.Pyramid(industrial, outlander.knowledgeSkew, 0f));
            float genericEduIndex = EducationRules.Index(EducationRules.Pyramid(industrial, generic.knowledgeSkew, 0f));
            Check($"raider education index < outlander (got {raiderEduIndex} vs {outlanderEduIndex})", raiderEduIndex < outlanderEduIndex);
            Check($"raider education index < plain industrial baseline (got {raiderEduIndex} vs {genericEduIndex})", raiderEduIndex < genericEduIndex);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL FACTION-CHARACTER TESTS PASSED" : failures + " FACTION-CHARACTER TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
