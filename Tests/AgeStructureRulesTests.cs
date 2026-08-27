// Behaviour tests for the deterministic age-structure core (0.2.0, #10): the tech-level base
// pyramids, the natalist/longevity skews, and the median-age interpolation. All pure, so this runs
// without a game — it is what guarantees a region's age pyramid is the same on every machine.
using System;
using RegionsAndSocieties.Demographics;

namespace AgeStructureRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("base pyramids are normalized and tech-shaped");
            Check("neolithic sums to 1", Sums1(AgeStructureRules.BasePyramid(2)));
            Check("spacer sums to 1", Sums1(AgeStructureRules.BasePyramid(5)));
            Check("unknown tech falls back to industrial", Same(AgeStructureRules.BasePyramid(99), AgeStructureRules.BasePyramid(4)));
            // Tribal societies are birth-heavy; spacer societies are elder-heavy.
            Check("tribal has more children than spacer", AgeStructureRules.BasePyramid(2)[0] > AgeStructureRules.BasePyramid(5)[0]);
            Check("spacer has more elders than tribal", AgeStructureRules.BasePyramid(5)[2] > AgeStructureRules.BasePyramid(2)[2]);
            Check("archotech is the oldest baseline", AgeStructureRules.BasePyramid(7)[2] >= AgeStructureRules.BasePyramid(5)[2]);

            Section("skews bend the pyramid the right way");
            float[] flat = AgeStructureRules.Pyramid(4, 0f, 0f);
            Check("no skew leaves the baseline (normalized)", Close(flat[0], AgeStructureRules.BasePyramid(4)[0]));
            Check("realized pyramid always sums to 1", Sums1(AgeStructureRules.Pyramid(4, 1f, 1f)));

            float[] natal = AgeStructureRules.Pyramid(4, 1f, 0f);
            Check("natalist skew raises the child share", natal[0] > flat[0]);
            Check("natalist skew lowers the elder share", natal[2] < flat[2]);

            float[] longev = AgeStructureRules.Pyramid(4, 0f, 1f);
            Check("longevity skew raises the elder share", longev[2] > flat[2]);

            Check("skews clamp: over-1 equals 1", Same(AgeStructureRules.Pyramid(4, 5f, 0f), AgeStructureRules.Pyramid(4, 1f, 0f)));
            Check("skews clamp: negative equals 0", Same(AgeStructureRules.Pyramid(4, -3f, 0f), AgeStructureRules.Pyramid(4, 0f, 0f)));

            Section("median age lands in the crossing band");
            // All working-age -> median sits in the middle of the working band [13,65).
            int allWorking = AgeStructureRules.MedianAge(new[] { 0f, 1f, 0f });
            Check($"all working-age -> ~39 (got {allWorking})", allWorking >= 38 && allWorking <= 40);
            // All children -> median in the child band, at half of 13 (~6.5, rounds to 6 or 7).
            int allChildren = AgeStructureRules.MedianAge(new[] { 1f, 0f, 0f });
            Check($"all children -> ~6.5 (got {allChildren})", allChildren >= 6 && allChildren <= 7);
            // A birth-heavy tribal pyramid is younger than a flat spacer one.
            int tribal = AgeStructureRules.MedianAge(AgeStructureRules.BasePyramid(2));
            int spacer = AgeStructureRules.MedianAge(AgeStructureRules.BasePyramid(5));
            Check($"tribal median ({tribal}) younger than spacer ({spacer})", tribal < spacer);
            Check("null pyramid -> 0", AgeStructureRules.MedianAge(null) == 0);
            Check("empty pyramid -> 0", AgeStructureRules.MedianAge(new[] { 0f, 0f, 0f }) == 0);

            // Longevity stretches the top of the elder band, so an elder-dominated population reads
            // older when it is long-lived.
            float[] oldPyramid = new[] { 0.1f, 0.3f, 0.6f };
            int mortal = AgeStructureRules.MedianAge(oldPyramid, 0f);
            int ageless = AgeStructureRules.MedianAge(oldPyramid, 1f);
            Check($"longevity raises an elder-heavy median ({mortal} -> {ageless})", ageless > mortal);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL AGE-STRUCTURE TESTS PASSED" : failures + " AGE-STRUCTURE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Sums1(float[] p)
        {
            float s = 0f; for (int i = 0; i < p.Length; i++) s += p[i];
            return Close(s, 1f);
        }

        private static bool Same(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Close(a[i], b[i])) return false;
            return true;
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
