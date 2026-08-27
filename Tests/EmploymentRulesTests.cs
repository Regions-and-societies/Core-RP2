// Behaviour tests for the deterministic employment core (0.2.0, #16): the tech-level base sector
// splits, the signal-driven shares, and the employment rate. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Demographics;

namespace EmploymentRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("base sector splits are normalized and tech-shaped");
            Check("neolithic sums to 1", Sums1(EmploymentRules.BaseSectors(2)));
            Check("spacer sums to 1", Sums1(EmploymentRules.BaseSectors(5)));
            Check("unknown tech falls back to industrial", Same(EmploymentRules.BaseSectors(99), EmploymentRules.BaseSectors(4)));
            // A tribe works the land far more than a spacer polity does.
            Check("tribal agriculture > spacer agriculture",
                EmploymentRules.BaseSectors(2)[(int)OccupationSector.Agriculture] > EmploymentRules.BaseSectors(5)[(int)OccupationSector.Agriculture]);
            Check("spacer industry > tribal industry",
                EmploymentRules.BaseSectors(5)[(int)OccupationSector.Industry] > EmploymentRules.BaseSectors(2)[(int)OccupationSector.Industry]);

            Section("signals push the right sector");
            float[] flat = EmploymentRules.SectorShares(4, 0f, 0f, 0f, 0f);
            Check("no signal leaves the baseline (normalized)", Close(flat[0], EmploymentRules.BaseSectors(4)[0]));
            Check("realized shares always sum to 1", Sums1(EmploymentRules.SectorShares(4, 1f, 1f, 1f, 1f)));

            Check("ag signal raises agriculture",
                EmploymentRules.SectorShares(4, 1f, 0f, 0f, 0f)[(int)OccupationSector.Agriculture] > flat[(int)OccupationSector.Agriculture]);
            Check("industry signal raises industry",
                EmploymentRules.SectorShares(4, 0f, 1f, 0f, 0f)[(int)OccupationSector.Industry] > flat[(int)OccupationSector.Industry]);
            Check("military signal raises military",
                EmploymentRules.SectorShares(4, 0f, 0f, 1f, 0f)[(int)OccupationSector.Military] > flat[(int)OccupationSector.Military]);
            Check("trade signal raises trade",
                EmploymentRules.SectorShares(4, 0f, 0f, 0f, 1f)[(int)OccupationSector.Trade] > flat[(int)OccupationSector.Trade]);

            // A strong military base dominates the mix — the heaviest-weighted signal.
            float[] garrison = EmploymentRules.SectorShares(4, 0f, 0f, 1f, 0f);
            int dom = 0; for (int i = 1; i < 4; i++) if (garrison[i] > garrison[dom]) dom = i;
            Check("a strong military signal makes military the dominant sector", dom == (int)OccupationSector.Military);
            Check("negative signals are floored (equal to zero)", Same(EmploymentRules.SectorShares(4, -5f, 0f, 0f, 0f), flat));

            Section("employment rate");
            Check("rate rises with tech", EmploymentRules.EmploymentRate(5, 0f) > EmploymentRules.EmploymentRate(2, 0f));
            Check("development lifts the rate", EmploymentRules.EmploymentRate(4, 1f) > EmploymentRules.EmploymentRate(4, 0f));
            Check("rate stays in 0..100", EmploymentRules.EmploymentRate(7, 1f) <= 100 && EmploymentRules.EmploymentRate(1, 0f) >= 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL EMPLOYMENT TESTS PASSED" : failures + " EMPLOYMENT TEST(S) FAILED");
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
