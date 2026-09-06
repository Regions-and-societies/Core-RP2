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

            Section("a compatibility patch can register archetypes (#55)");
            FactionCharacterRules.ResetRegistrations();
            FactionCharacterRules.ArchetypeSource src;
            Check("before registration the VFE town is a trait guess",
                FactionCharacterRules.Classify("VFE_SomeTown", industrial, false, out src) == FactionArchetype.Generic && src == FactionCharacterRules.ArchetypeSource.TraitGuess);
            FactionCharacterRules.RegisterArchetype("VFE_SomeTown", FactionArchetype.Merchant);
            Check("registered archetype wins over the trait guess",
                FactionCharacterRules.Classify("VFE_SomeTown", industrial, false, out src) == FactionArchetype.Merchant && src == FactionCharacterRules.ArchetypeSource.Registered);
            Check("three-argument Classify sees it too", FactionCharacterRules.Classify("VFE_SomeTown", industrial, false) == FactionArchetype.Merchant);
            Check("TryGetRegisteredArchetype reports it", FactionCharacterRules.TryGetRegisteredArchetype("VFE_SomeTown", out var got) && got == FactionArchetype.Merchant);
            FactionCharacterRules.RegisterArchetype("VFE_SomeTown", FactionArchetype.Cult);
            Check("a later registration for the same defName wins", FactionCharacterRules.Classify("VFE_SomeTown", industrial, false) == FactionArchetype.Cult);
            FactionCharacterRules.RegisterArchetype("Pirate", FactionArchetype.Merchant);
            Check("a registration overrides even a built-in defName",
                FactionCharacterRules.Classify("Pirate", industrial, true, out src) == FactionArchetype.Merchant && src == FactionCharacterRules.ArchetypeSource.Registered);
            Check("built-in source is reported for an unregistered known faction",
                FactionCharacterRules.Classify("Empire", spacer, false, out src) == FactionArchetype.Imperial && src == FactionCharacterRules.ArchetypeSource.BuiltIn);
            FactionCharacterRules.RegisterArchetype(null, FactionArchetype.Cult);
            FactionCharacterRules.RegisterArchetype("", FactionArchetype.Cult);
            Check("null / empty names are ignored", !FactionCharacterRules.TryGetRegisteredArchetype("", out _) && !FactionCharacterRules.TryGetRegisteredArchetype(null, out _));
            FactionCharacterRules.ResetRegistrations();
            Check("reset restores the built-in answer", FactionCharacterRules.Classify("Pirate", industrial, true) == FactionArchetype.Raider);
            Check("reset restores the trait guess", FactionCharacterRules.Classify("VFE_SomeTown", industrial, false) == FactionArchetype.Generic);

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
