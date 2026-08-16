// Behaviour tests for the 0.7 world-object integration layer.
// Declares fake mod types in the namespaces the profiles look for, so the reflection
// adapters resolve exactly as they would against a real install.
using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;

// ---- Fake Empire Refactored -------------------------------------------------
namespace FactionColonies
{
    public class FindFC { }

    public class WorldSettlementFC : RimWorld.Planet.Settlement
    {
        public int settlementLevel = 3;
        public int maxSettlementLevel = 10;
        // The 0.8 profile reads Empire's real member names (workers/workersMax, #30) — the old
        // invented "population" name is exactly what shipped inert and is deliberately gone.
        // Doubles, as on the real WorldSettlementFC: the adapter's TryGetInt narrowing is part of
        // what the population check exercises.
        public double workers = 250;
        public double workersMax = 400;
    }

    public class MilitaryFC : RimWorld.Planet.WorldObject { }
}

// ---- Fake Vanilla Outposts Expanded ----------------------------------------
namespace Outposts
{
    public class Outpost : RimWorld.Planet.WorldObject
    {
        public int level = 2;
        private readonly List<Verse.Pawn> occupants = new List<Verse.Pawn>();
        public int PawnCount { get { return occupants.Count; } }
        public void AddOccupants(int n) { for (int i = 0; i < n; i++) occupants.Add(new Verse.Pawn()); }
    }

    public class Outpost_Farm : Outpost { }
}

// ---- Fake unknown third-party mods -----------------------------------------
namespace SomeRandomMod
{
    public class GrandOutpost : RimWorld.Planet.WorldObject { }
    public class Doodad : RimWorld.Planet.WorldObject { }
}

namespace IntegrationTests
{
    public static class Program
    {
        private static int failures;

        // Stand-ins for the per-patch "installed means enabled" state. The real per-mod toggles
        // left core with the compatibility inversion (Core-MMF#3); these locals let the suite keep
        // exercising the enabledGetter gating machinery without them.
        private static bool empireIntegrationOn = true;
        private static bool voeIntegrationOn = true;

        public static int Main()
        {
            // The compatibility inversion moved every real per-mod profile out of core and into a
            // compatibility patch, where the compiler verifies its type and member names. What core
            // still owns — and what this suite tests — is the MACHINERY: the registry, the
            // reflection adapter, rule matching, gating and the classifier. The profiles below are
            // test doubles mirroring the shapes the real patches use, bound to this file's fake mod
            // types, registered through the same public API the patches call.
            WorldObjectAdapterRegistry.Clear();
            WorldObjectAdapterRegistry.Register(new VanillaWorldObjectAdapter());

            var empireProfile = new WorldObjectAdapterProfile
            {
                adapterId = "empire",
                packageId = "Test.FakeEmpire",
                displayName = "Fake Empire",
                priority = 100,
                markerTypes = new[] { "FactionColonies.WorldSettlementFC" },
                populationMembers = new[] { "workers", "workersMax" },
                levelMembers = new[] { "settlementLevel" },
                assumedMaxLevel = 10,
                playerOwnedByDefault = true,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && empireIntegrationOn
            };
            empireProfile.Rule(TypeMatch.ExactType, "FactionColonies.WorldSettlementFC", WorldObjectKind.Settlement)
                .Rule(TypeMatch.NamespacePrefix, "FactionColonies.WorldSettlement", WorldObjectKind.Settlement)
                .Rule(TypeMatch.TypeNameContains, "MilitaryFC", WorldObjectKind.Military);
            WorldObjectAdapterRegistry.Register(new ReflectionWorldObjectAdapter(empireProfile));

            var voeProfile = new WorldObjectAdapterProfile
            {
                adapterId = "voe",
                packageId = "Test.FakeVoe",
                displayName = "Fake VOE",
                priority = 110,
                markerTypes = new[] { "Outposts.Outpost" },
                populationMembers = new[] { "PawnCount", "occupants" },
                levelMembers = new[] { "level" },
                assumedMaxLevel = 0,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && voeIntegrationOn
            };
            voeProfile.Rule(TypeMatch.NamespacePrefix, "Outposts.", WorldObjectKind.Outpost);
            WorldObjectAdapterRegistry.Register(new ReflectionWorldObjectAdapter(voeProfile));

            // A profile whose marker types resolve nowhere: exercises the not-installed path.
            var absentProfile = new WorldObjectAdapterProfile
            {
                adapterId = "absent",
                packageId = "Test.NotInstalled",
                displayName = "Absent Mod",
                priority = 120,
                markerTypes = new[] { "NotInstalled.MarkerType" }
            };
            WorldObjectAdapterRegistry.Register(new ReflectionWorldObjectAdapter(absentProfile));

            Section("adapters resolve installed mods only");
            var ids = new List<string>();
            foreach (var a in WorldObjectAdapterRegistry.Adapters)
            {
                if (a.IsActive) ids.Add(a.AdapterId);
            }
            Check("empire active (fake FactionColonies present)", ids.Contains("empire"));
            Check("voe active (fake Outposts present)", ids.Contains("voe"));
            Check("absent profile inactive (marker type unresolvable)", !ids.Contains("absent"));
            Check("vanilla always active", ids.Contains("vanilla"));

            var empire = new FactionColonies.WorldSettlementFC { def = Def("EmpireColony") };
            var empireMil = new FactionColonies.MilitaryFC { def = Def("EmpireMilitary") };
            var voe = new Outposts.Outpost_Farm { def = Def("OutpostFarm") };
            voe.AddOccupants(7);
            var vanilla = new Settlement { def = Def("Settlement"), Faction = new Faction() };
            var caravan = new Caravan { def = Def("Caravan") };
            var oddOutpost = new SomeRandomMod.GrandOutpost { def = Def("GrandOutpost") };
            var doodad = new SomeRandomMod.Doodad { def = Def("Doodad") };

            Section("classification");
            Check("empire colony -> Settlement", WorldObjectClassifier.Classify(empire) == WorldObjectKind.Settlement);
            Check("empire military -> Military", WorldObjectClassifier.Classify(empireMil) == WorldObjectKind.Military);
            Check("VOE subclass -> Outpost", WorldObjectClassifier.Classify(voe) == WorldObjectKind.Outpost);
            Check("vanilla settlement -> Settlement", WorldObjectClassifier.Classify(vanilla) == WorldObjectKind.Settlement);
            Check("caravan -> Caravan", WorldObjectClassifier.Classify(caravan) == WorldObjectKind.Caravan);
            Check("caravan is not territorial", !WorldObjectClassifier.IsTerritorial(caravan));
            Check("unknown mod 'GrandOutpost' -> Outpost via heuristic", WorldObjectClassifier.Classify(oddOutpost) == WorldObjectKind.Outpost);
            Check("unrecognisable object -> Unknown", WorldObjectClassifier.Classify(doodad) == WorldObjectKind.Unknown);
            Check("unrecognisable object logged once", CountLogsContaining("Doodad") == 1);
            WorldObjectClassifier.Classify(doodad);
            Check("still logged only once after a second look", CountLogsContaining("Doodad") == 1);

            Section("population");
            int pop;
            Check("empire population read by reflection", WorldObjectAdapterRegistry.TryGetPopulation(empire, out pop) && pop == 250);
            Check("VOE PawnCount read by reflection", WorldObjectAdapterRegistry.TryGetPopulation(voe, out pop) && pop == 7);
            Check("vanilla settlement falls back to R&T estimate", WorldObjectClassifier.GetPopulation(vanilla) == 42);
            Check("empire colony counts as populated", WorldObjectClassifier.HasPopulation(empire));
            Check("caravan does not", !WorldObjectClassifier.HasPopulation(caravan));

            Section("levels");
            int level, maxLevel;
            Check("empire level 3 of 10", WorldObjectAdapterRegistry.TryGetLevel(empire, out level, out maxLevel) && level == 3 && maxLevel == 10);
            Check("VOE level 2, max unknown", WorldObjectAdapterRegistry.TryGetLevel(voe, out level, out maxLevel) && level == 2 && maxLevel == 0);

            Section("player holdings");
            Check("empire colony is a player holding even with no player faction",
                WorldObjectClassifier.IsPlayerHolding(empire));
            Check("AI vanilla settlement is not", !WorldObjectClassifier.IsPlayerHolding(vanilla));
            var playerBase = new Settlement { def = Def("Settlement"), Faction = new Faction { IsPlayer = true } };
            Check("player-faction settlement is", WorldObjectClassifier.IsPlayerHolding(playerBase));
            Check("caravan is not a holding", !WorldObjectClassifier.IsPlayerHolding(caravan));

            Section("per-mod opt out");
            voeIntegrationOn = false;
            WorldObjectClassifier.InvalidateCache();
            Check("VOE outpost ignored once its integration is off",
                WorldObjectClassifier.Classify(voe) == WorldObjectKind.Ignored);
            Check("...and is no longer territorial", !WorldObjectClassifier.IsTerritorial(voe));
            Check("unrelated mods keep working", WorldObjectClassifier.Classify(empire) == WorldObjectKind.Settlement);
            voeIntegrationOn = true;

            Section("master switch off = pre-0.7 behaviour");
            WorldObjectIntegrationSettings.masterEnabled = false;
            WorldObjectClassifier.InvalidateCache();
            Check("vanilla settlement still governed", WorldObjectClassifier.Classify(vanilla) == WorldObjectKind.Settlement);
            Check("empire colony governed as a plain settlement (it derives from Settlement)",
                WorldObjectClassifier.Classify(empire) == WorldObjectKind.Settlement);
            Check("empire colony no longer counts as a player holding",
                !WorldObjectClassifier.IsPlayerHolding(empire));
            Check("VOE outpost ignored", WorldObjectClassifier.Classify(voe) == WorldObjectKind.Ignored);
            Check("unknown modded outpost ignored", WorldObjectClassifier.Classify(oddOutpost) == WorldObjectKind.Ignored);
            WorldObjectIntegrationSettings.masterEnabled = true;
            WorldObjectClassifier.InvalidateCache();

            Section("bulk queries");
            Verse.Find.WorldObjects.AllWorldObjects.AddRange(new WorldObject[] { empire, empireMil, voe, vanilla, caravan, oddOutpost, doodad });
            Check("2 settlements", WorldObjectClassifier.AllOfKind(WorldObjectKind.Settlement).Count == 2);
            Check("2 outposts", WorldObjectClassifier.AllOfKind(WorldObjectKind.Outpost).Count == 2);
            Check("5 territorial objects", WorldObjectClassifier.AllTerritorial().Count == 5);

            Section("resilience");
            WorldObjectAdapterRegistry.Register(new ThrowingAdapter());
            Check("a throwing adapter does not break classification",
                WorldObjectClassifier.Classify(vanilla) == WorldObjectKind.Settlement);
            Check("...and the throw is logged", CountLogsContaining("threw from") > 0);
            Check("null object is safe", WorldObjectClassifier.Classify(null) == WorldObjectKind.Unknown);
            Check("null is not a player holding", !WorldObjectClassifier.IsPlayerHolding(null));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private class ThrowingAdapter : WorldObjectAdapterBase
        {
            public override string AdapterId { get { return "throwing"; } }
            public override int Priority { get { return 1; } }
            public override bool TryClassify(WorldObject obj, out WorldObjectKind kind)
            {
                throw new InvalidOperationException("deliberate");
            }
        }

        private static WorldObjectDef Def(string name)
        {
            return new WorldObjectDef { defName = name };
        }

        private static int CountLogsContaining(string needle)
        {
            int n = 0;
            foreach (string s in Verse.Log.Captured)
            {
                if (s.Contains(needle)) n++;
            }
            return n;
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
