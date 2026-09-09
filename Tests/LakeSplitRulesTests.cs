// Behaviour tests for the nearest-shore inland-lake split (#48): shore counts, dominance, and a clean
// multi-source flood whose seams meet on the midline (neat wedges, dominant shore keeps the centre).
// Pure, no game — the BFS runs on a hand-built line adjacency, the same code the hex world drives.
using System;
using System.Collections.Generic;
using System.Linq;
using RegionsAndSocieties.Partition;

namespace LakeSplitRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("shore counts and dominance");
            var shore = LakeSplitRules.ShoreCounts(
                new List<int> { 0, 1, 8 },
                new Dictionary<int, List<int>> {
                    { 0, new List<int> { 1 } }, { 1, new List<int> { 1 } }, { 8, new List<int> { 3 } } });
            Check("region 1 borders 2 tiles", shore[1] == 2);
            Check("region 3 borders 1 tile", shore[3] == 1);
            Check("dominant is region 1 (most shore)", LakeSplitRules.DominantRegion(shore) == 1);
            var tie = new Dictionary<int, int> { { 5, 3 }, { 2, 3 } };
            Check("dominance tie -> lowest id", LakeSplitRules.DominantRegion(tie) == 2);

            Section("split: neat wedges, seams on the midline");
            // A 12-tile line lake; region 1 seeds tile 0, region 2 tile 6, region 3 tile 11.
            var line12 = Line(12);
            var labels12 = new Dictionary<int, List<int>> {
                { 0, new List<int> { 1 } }, { 6, new List<int> { 2 } }, { 11, new List<int> { 3 } } };
            var a12 = LakeSplitRules.Split(Enumerable.Range(0, 12).ToList(), line12, labels12);
            Check("every lake tile assigned", a12.Count == 12);
            Check("each region's slice is contiguous (no interlocking fingers)", Contiguous(a12));
            Check("the shore-0 region owns its end", a12[0] == 1);
            Check("the shore-11 region owns its end", a12[11] == 3);
            Check("the middle shore owns the middle", a12[6] == 2);

            Section("split: dominant shore keeps the centre and the larger share");
            // Region 1 borders tiles 0 AND 1 (shore 2, dominant); region 3 borders tile 8 (shore 1).
            var line9 = Line(9);
            var labels9 = new Dictionary<int, List<int>> {
                { 0, new List<int> { 1 } }, { 1, new List<int> { 1 } }, { 8, new List<int> { 3 } } };
            var a9 = LakeSplitRules.Split(Enumerable.Range(0, 9).ToList(), line9, labels9);
            Check("all 9 tiles assigned, contiguous", a9.Count == 9 && Contiguous(a9));
            Check("dominant region 1 takes the larger share", Count(a9, 1) > Count(a9, 3));
            Check("seam sits on the midline (region 1 holds tile 4)", a9[4] == 1 && a9[5] == 3);

            Section("split: a tie splits evenly toward the lower-id shore");
            // Two equal shores at the ends of an 8-tile line: seam falls in the middle, tie -> lower id.
            var line8 = Line(8);
            var labels8 = new Dictionary<int, List<int>> {
                { 0, new List<int> { 2 } }, { 7, new List<int> { 5 } } };
            var a8 = LakeSplitRules.Split(Enumerable.Range(0, 8).ToList(), line8, labels8);
            Check("equal shores split near half", Count(a8, 2) == 4 && Count(a8, 5) == 4);
            Check("both slices contiguous", Contiguous(a8));

            Section("pond wholly inside one region");
            var pond = Line(5);
            var pondLabels = new Dictionary<int, List<int>> { { 0, new List<int> { 7 } } };
            var ap = LakeSplitRules.Split(Enumerable.Range(0, 5).ToList(), pond, pondLabels);
            Check("single-shore pond -> all tiles to that region", ap.Count == 5 && ap.Values.All(v => v == 7));

            Section("no shore at all -> empty (caller keeps as water)");
            var none = LakeSplitRules.Split(Enumerable.Range(0, 3).ToList(), Line(3),
                new Dictionary<int, List<int>>());
            Check("no shore labels -> no assignment", none.Count == 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL LAKE-SPLIT TESTS PASSED" : failures + " LAKE-SPLIT TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // A line of n tiles: tile i adjacent to i-1 and i+1.
        private static Dictionary<int, List<int>> Line(int n)
        {
            var adj = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var l = new List<int>();
                if (i > 0) l.Add(i - 1);
                if (i < n - 1) l.Add(i + 1);
                adj[i] = l;
            }
            return adj;
        }

        private static int Count(Dictionary<int, int> assign, int region)
        {
            int c = 0;
            foreach (var v in assign.Values) if (v == region) c++;
            return c;
        }

        // On a line, each region's tiles should form one unbroken run.
        private static bool Contiguous(Dictionary<int, int> assign)
        {
            var byTile = assign.OrderBy(k => k.Key).Select(k => k.Value).ToList();
            var seen = new HashSet<int>();
            int prev = -999;
            foreach (int r in byTile)
            {
                if (r != prev)
                {
                    if (!seen.Add(r)) return false;   // region reappears after a gap
                    prev = r;
                }
            }
            return true;
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
