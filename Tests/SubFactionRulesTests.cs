// Behaviour tests for sub-faction splitting (#57): the eligibility gate, section count, deterministic
// geographic grouping, and the latitude/longitude direction labels. Pure, no game.
using System;
using System.Collections.Generic;
using RegionsAndSocieties.Placement;

namespace SubFactionRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            const int neolithic = 2, industrial = 4, spacer = 5, ultra = 6;
            int tribeCap = ClusteringRules.DefaultClusterSize(FactionKind.Tribe);       // 5
            int roughCap = ClusteringRules.DefaultClusterSize(FactionKind.RoughUnion);  // 7
            int pirateCap = ClusteringRules.DefaultClusterSize(FactionKind.Pirate);     // 3
            int otherCap = ClusteringRules.DefaultClusterSize(FactionKind.Other);       // 9 (unbounded)

            Section("who splits: fractious kinds (pirate/tribe/rough union) with two or more bodies");
            Check("a scattered tribe splits", SubFactionRules.ShouldSplit(FactionKind.Tribe, tribeCap, 3));
            Check("a scattered rough union splits", SubFactionRules.ShouldSplit(FactionKind.RoughUnion, roughCap, 2));
            Check("scattered pirates split", SubFactionRules.ShouldSplit(FactionKind.Pirate, pirateCap, 5));
            Check("a single-body tribe does not", !SubFactionRules.ShouldSplit(FactionKind.Tribe, tribeCap, 1));
            Check("single-body pirates do not", !SubFactionRules.ShouldSplit(FactionKind.Pirate, pirateCap, 1));
            Check("the Empire (cohesive polity) does not", !SubFactionRules.ShouldSplit(FactionKind.Empire, 3, 4));
            Check("an unbounded/other faction does not", !SubFactionRules.ShouldSplit(FactionKind.Other, otherCap, 4));
            Check("splittable-kind check", SubFactionRules.IsSplittableKind(FactionKind.Pirate) && SubFactionRules.IsSplittableKind(FactionKind.Tribe) && SubFactionRules.IsSplittableKind(FactionKind.RoughUnion));
            Check("Empire and Other are not splittable kinds", !SubFactionRules.IsSplittableKind(FactionKind.Empire) && !SubFactionRules.IsSplittableKind(FactionKind.Other));

            Section("kin default (#63/#64): Empire never, spacer-tech off, fractious low-tech on");
            // Empire is off at every tech and is LOCKED.
            Check("Empire off regardless of tech", !SubFactionRules.KinDefault(FactionKind.Empire, spacer, true) && !SubFactionRules.KinDefault(FactionKind.Empire, industrial, true));
            Check("Empire is kin-locked", SubFactionRules.KinLocked(true) && !SubFactionRules.KinLocked(false));
            // #64: any spacer-tech faction defaults kin OFF, even a pirate/tribe kind.
            Check("spacer pirate off (#64)", !SubFactionRules.KinDefault(FactionKind.Pirate, spacer, false));
            Check("spacer tribe off (#64)", !SubFactionRules.KinDefault(FactionKind.Tribe, spacer, false));
            Check("ultra-tech pirate off (#64)", !SubFactionRules.KinDefault(FactionKind.Pirate, ultra, false));
            // Sub-spacer fractious kinds keep kin on by default.
            Check("industrial (e.g. waster) pirate on", SubFactionRules.KinDefault(FactionKind.Pirate, industrial, false));
            Check("neolithic tribe on", SubFactionRules.KinDefault(FactionKind.Tribe, neolithic, false));
            Check("industrial rough union on", SubFactionRules.KinDefault(FactionKind.RoughUnion, industrial, false));
            // Cohesive kinds are off whatever the tech.
            Check("industrial civil (Other) off", !SubFactionRules.KinDefault(FactionKind.Other, industrial, false));
            Check("spacer trader (Other) off", !SubFactionRules.KinDefault(FactionKind.Other, spacer, false));

            Section("section count: at most 2-3, never more than bodies");
            Check("2 bodies -> 2 sections", SubFactionRules.SectionCount(2, SubFactionRules.MaxSections) == 2);
            Check("3 bodies -> 3", SubFactionRules.SectionCount(3, SubFactionRules.MaxSections) == 3);
            Check("7 bodies -> capped at 3", SubFactionRules.SectionCount(7, SubFactionRules.MaxSections) == 3);
            Check("1 body -> 1", SubFactionRules.SectionCount(1, SubFactionRules.MaxSections) == 1);
            Check("MaxSections is 3", SubFactionRules.MaxSections == 3);

            Section("grouping: two far clusters land in two sections");
            // Four bodies: two near the north pole (+Y), two near the south (-Y).
            var ns = new List<GeoPoint>
            {
                new GeoPoint(0.1, 0.9, 0.0), new GeoPoint(-0.1, 0.92, 0.05),   // north pair
                new GeoPoint(0.05, -0.9, 0.0), new GeoPoint(-0.05, -0.93, 0.1), // south pair
            };
            var a2 = SubFactionRules.AssignSections(ns, 2);
            Check("the two north bodies share a section", a2[0] == a2[1]);
            Check("the two south bodies share a section", a2[2] == a2[3]);
            Check("north and south are different sections", a2[0] != a2[2]);
            Check("deterministic: same input, same assignment", Same(a2, SubFactionRules.AssignSections(ns, 2)));
            Check("one section requested -> everything in 0", All0(SubFactionRules.AssignSections(ns, 1)));

            Section("grouping: three clusters -> three sections");
            var three = new List<GeoPoint>
            {
                new GeoPoint(0.0, 0.95, 0.0), new GeoPoint(0.05, 0.93, 0.02),  // north
                new GeoPoint(0.9, -0.2, 0.0), new GeoPoint(0.92, -0.25, 0.0),  // east-ish
                new GeoPoint(-0.9, -0.2, 0.0), new GeoPoint(-0.93, -0.15, 0.0),// west-ish
            };
            var a3 = SubFactionRules.AssignSections(three, 3);
            Check("north pair together", a3[0] == a3[1]);
            Check("east pair together", a3[2] == a3[3]);
            Check("west pair together", a3[4] == a3[5]);
            Check("three distinct sections", a3[0] != a3[2] && a3[0] != a3[4] && a3[2] != a3[4]);

            Section("labels: a north-south split reads South / North");
            // Section 0 is the northern group, section 1 the southern (index order deliberately reversed).
            var nsCentroids = new List<GeoPoint> { new GeoPoint(0, 0.9, 0), new GeoPoint(0, -0.9, 0) };
            var nsLabels = SubFactionRules.SectionLabels(nsCentroids);
            Check("northern section labelled North", nsLabels[0] == "North");
            Check("southern section labelled South", nsLabels[1] == "South");

            Section("labels: an east-west split reads West / East");
            var ewCentroids = new List<GeoPoint> { new GeoPoint(0.9, 0.0, 0), new GeoPoint(-0.9, 0.0, 0) };
            var ewLabels = SubFactionRules.SectionLabels(ewCentroids);
            Check("eastern (high X) section labelled East", ewLabels[0] == "East");
            Check("western (low X) section labelled West", ewLabels[1] == "West");

            Section("labels: three north-south sections gain a Central");
            var three3 = new List<GeoPoint> { new GeoPoint(0, 0.9, 0), new GeoPoint(0, 0.0, 0), new GeoPoint(0, -0.9, 0) };
            var l3 = SubFactionRules.SectionLabels(three3);
            Check("top is North", l3[0] == "North");
            Check("middle is Central", l3[1] == "Central");
            Check("bottom is South", l3[2] == "South");
            Check("all labels distinct", l3[0] != l3[1] && l3[1] != l3[2] && l3[0] != l3[2]);
            Check("goodwill constant is friendly kin, not merged", SubFactionRules.LooseKinGoodwill > 0 && SubFactionRules.LooseKinGoodwill < 100);

            Section("kin-count estimate = ceil(regions / cluster size)");
            Check("10 regions, cluster 5 -> 2 kin", SubFactionRules.EstimateKinCount(10, 5) == 2);
            Check("10 regions, cluster 3 -> 4 kin", SubFactionRules.EstimateKinCount(10, 3) == 4);
            Check("7 regions, cluster 7 -> 1 (fits one body)", SubFactionRules.EstimateKinCount(7, 7) == 1);
            Check("15 regions, cluster 3 -> 5 kin", SubFactionRules.EstimateKinCount(15, 3) == 5);
            Check("unbounded cluster (8) with 5 regions -> 1", SubFactionRules.EstimateKinCount(5, 8) == 1);
            Check("cluster 1 -> one kin per region", SubFactionRules.EstimateKinCount(6, 1) == 6);
            Check("degenerate zero regions -> 1", SubFactionRules.EstimateKinCount(0, 3) == 1);

            Section("planned kin count is mode-aware (count = size, percent = cluster number)");
            // kin = min(numberOfClusters, floor(regions / minClusterSize)), >= 1.
            Check("tribe 39 regions, 3 clusters, min 5 -> 3 (cap bites)", SubFactionRules.PlannedKinCount(39, 3, 5) == 3);
            Check("pirate 39 regions, 5 clusters, min 3 -> 5", SubFactionRules.PlannedKinCount(39, 5, 3) == 5);
            Check("rough 39 regions, 2 clusters, min 7 -> 2", SubFactionRules.PlannedKinCount(39, 2, 7) == 2);
            Check("min-size clamp bites: 12 regions, 5 clusters, min 5 -> 2", SubFactionRules.PlannedKinCount(12, 5, 5) == 2);
            Check("cohesive: 1 cluster -> whole", SubFactionRules.PlannedKinCount(40, 1, 5) == 1);
            Check("0 clusters = uncapped: floor(regions/min)", SubFactionRules.PlannedKinCount(40, 0, 5) == 8);
            Check("0 clusters + min 1 = one faction per region", SubFactionRules.PlannedKinCount(40, 0, 1) == 40);
            Check("never below 1", SubFactionRules.PlannedKinCount(2, 5, 9) == 1 && SubFactionRules.PlannedKinCount(0, 5, 3) == 1);

            Section("body labels: compass bearings, kept body keeps the clean base name, distinct per body");
            var bl = new List<GeoPoint> { new GeoPoint(0, 2, 0), new GeoPoint(0, -2, 0), new GeoPoint(2, 0, 0) };
            var lbls = SubFactionRules.BodyLabels(bl, 2);   // keep the eastern body
            Check("kept body -> empty (clean base name)", lbls[2] == "");
            Check("northern body -> North", lbls[0] == "North");
            Check("southern body -> South", lbls[1] == "South");
            var north4 = new List<GeoPoint> { new GeoPoint(0, -3, 0), new GeoPoint(0, 2, 0), new GeoPoint(0.05, 2.1, 0), new GeoPoint(-0.05, 2.05, 0) };
            var l4 = SubFactionRules.BodyLabels(north4, 0);   // keep the lone southern body
            Check("kept clean", l4[0] == "");
            Check("clustered same-bearing bodies get DISTINCT non-empty labels", l4[1] != "" && l4[2] != "" && l4[3] != "" && l4[1] != l4[2] && l4[2] != l4[3] && l4[1] != l4[3]);
            Check("empty input safe", SubFactionRules.BodyLabels(new List<GeoPoint>(), 0).Length == 0);

            Section("name composition: direction slips in after a leading article (#59 polish)");
            Check("'The Abene Tribe' -> 'The West Abene Tribe'", SubFactionRules.ComposeName("West", "The Abene Tribe") == "The West Abene Tribe");
            Check("plain base just takes the prefix", SubFactionRules.ComposeName("North", "Toban Union") == "North Toban Union");
            Check("lowercase article still handled, casing preserved", SubFactionRules.ComposeName("East", "the Blue Newt") == "the East Blue Newt");
            Check("'A '/'An ' articles handled", SubFactionRules.ComposeName("South", "A Great Clan") == "A South Great Clan");
            Check("empty label returns the base unchanged", SubFactionRules.ComposeName("", "The Abene Tribe") == "The Abene Tribe");
            Check("a word merely starting with 'The' is not treated as an article", SubFactionRules.ComposeName("West", "Theran Host") == "West Theran Host");
            Check("no double space or stray article on a bare 'The'", SubFactionRules.ComposeName("West", "The") == "West The");

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SUB-FACTION TESTS PASSED" : failures + " SUB-FACTION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Same(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static bool All0(int[] a) { foreach (int v in a) if (v != 0) return false; return true; }
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
