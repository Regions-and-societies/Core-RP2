using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Placement
{
    /// <summary>
    /// The basic-view size categories a player picks per faction — each a preset target NUMBER OF REGIONS:
    /// Tiny 3, Small 5, Medium 7, Large 10, Very large 15. Chosen so the sizes translate cleanly to the
    /// clustering scale. Advanced lets the player type any integer instead.
    /// </summary>
    public enum ShareCategory { Tiny, Small, Medium, Large, VeryLarge }
    // PlacementValueMode and PlacementPercentBasis live in PlacementValueMode.cs (shared by the clustering
    // and sub-faction rules too, so the lean test suites can compile them without this file).

    /// <summary>
    /// The region-count model behind the placement settings (#47): each faction stores a target number of
    /// regions (its <c>placementShare</c> value, now read as a whole count). Basic mode picks a size preset;
    /// advanced mode types any integer. Worldgen gives each faction up to that many regions, and the sum is
    /// gated against the planet's expected regions. Pure and unit-tested so the dialog, the debug report and
    /// the worldgen distributor all read the SAME arithmetic.
    /// </summary>
    public static class PlacementShareRules
    {
        /// <summary>The size weight a fresh profile carries when it has none yet (0 = unset) — Medium (4×).</summary>
        public const float DefaultShareWeight = 4f;

        /// <summary>The five basic-view size presets, as RELATIVE MULTIPLIERS (each step doubles): Tiny 1×,
        /// Small 2×, Medium 4×, Large 8×, Very large 16×. In percent mode these are the share weights (a
        /// very-large faction pulls 16 points to a tiny faction's 1); in count mode they double as literal
        /// region targets (Tiny = 1 region … Very large = 16). Every faction is guaranteed at least 1 region
        /// regardless (see <see cref="DistributeRegions"/>).</summary>
        public const int TinyCap = 1;
        public const int SmallCap = 2;
        public const int MediumCap = 4;
        public const int LargeCap = 8;
        public const int VeryLargeCap = 16;

        /// <summary>The size multiplier / region weight for a size category.</summary>
        public static int CategoryToRegionCap(ShareCategory category)
        {
            switch (category)
            {
                case ShareCategory.Tiny: return TinyCap;
                case ShareCategory.Small: return SmallCap;
                case ShareCategory.Large: return LargeCap;
                case ShareCategory.VeryLarge: return VeryLargeCap;
                default: return MediumCap;
            }
        }

        /// <summary>Fraction of the planet's capacity the demand uses, at or above which the UI warns about
        /// crowding / dwindling free space.</summary>
        public const float CapacityWarnFraction = 0.80f;

        /// <summary>The demanded/available fraction, clamped non-negative. Capacity ≤ 0 reads as fully used
        /// (1) when anything is demanded, so an unknown capacity never looks free.</summary>
        public static float CapacityFraction(int demand, int capacity)
        {
            if (demand <= 0) return 0f;
            if (capacity <= 0) return 1f;
            return (float)demand / capacity;
        }

        /// <summary>Demand exceeds the planet's capacity — the hard gate: a world demanding this many regions
        /// cannot be generated.</summary>
        public static bool IsOverCapacity(int demand, int capacity) => demand > capacity && capacity > 0;

        /// <summary>Demand has reached the crowding-warning band (≥80%) without yet exceeding capacity.</summary>
        public static bool IsCrowdingWarning(int demand, int capacity)
        {
            return capacity > 0 && !IsOverCapacity(demand, capacity) && CapacityFraction(demand, capacity) >= CapacityWarnFraction;
        }

        /// <summary>The size category a stored weight reads as, by nearest preset — so the basic picker can
        /// show which multiplier a typed-in (or migrated) value falls into. Boundaries are the midpoints of
        /// the 1/2/4/8/16 scale: &lt;1.5 Tiny, &lt;3 Small, &lt;6 Medium, &lt;12 Large, else Very large.</summary>
        public static ShareCategory CategoryForShare(float regions)
        {
            if (regions < (TinyCap + SmallCap) / 2f) return ShareCategory.Tiny;
            if (regions < (SmallCap + MediumCap) / 2f) return ShareCategory.Small;
            if (regions < (MediumCap + LargeCap) / 2f) return ShareCategory.Medium;
            if (regions < (LargeCap + VeryLargeCap) / 2f) return ShareCategory.Large;
            return ShareCategory.VeryLarge;
        }

        /// <summary>A short player-facing label for a size category.</summary>
        public static string CategoryLabel(ShareCategory category)
        {
            switch (category)
            {
                case ShareCategory.Tiny: return "Tiny";
                case ShareCategory.Small: return "Small";
                case ShareCategory.Large: return "Large";
                case ShareCategory.VeryLarge: return "Very large";
                default: return "Medium";
            }
        }

        /// <summary>Convert an old <c>baseCountRange</c> (min..max) to a target region count: the midpoint. Relative
        /// proportions between faction kinds are preserved (a hostile 3..8 → 5.5 stays smaller than a
        /// civil 5..15 → 10), which is exactly what a normalised share needs. Never negative.</summary>
        public static float MigrateRangeToShareWeight(int min, int max)
        {
            float mid = (min + max) / 2f;
            return mid < 0f ? 0f : mid;
        }

        /// <summary>This faction's normalised fraction of the placed total: its weight over the sum of all
        /// weights. Returns 0 when the total is non-positive.</summary>
        public static float NormalizedFraction(float shareWeight, float totalWeight)
        {
            if (totalWeight <= 0f || shareWeight <= 0f) return 0f;
            return shareWeight / totalWeight;
        }

        /// <summary>The live per-faction estimate for the settings dialog: how many regions this faction is
        /// expected to receive. <paramref name="placedTotal"/> is the total number of territories worldgen
        /// will place (≈ expected land regions × claimed-land-area fraction). Rounded, never negative.</summary>
        public static int EstimatedFactionCount(float shareWeight, float totalWeight, int placedTotal)
        {
            if (placedTotal <= 0) return 0;
            float frac = NormalizedFraction(shareWeight, totalWeight);
            int n = (int)Math.Round(frac * placedTotal);
            return n < 0 ? 0 : n;
        }

        /// <summary>The number of territories placed in total: the expected land-region count scaled by the
        /// claimed-land-area fraction (the density knob). This is the denominator every per-faction estimate
        /// shares. Rounded, never negative.</summary>
        public static int PlacedTotal(int expectedRegions, float claimedLandAreaFraction)
        {
            if (expectedRegions <= 0) return 0;
            if (claimedLandAreaFraction < 0f) claimedLandAreaFraction = 0f;
            if (claimedLandAreaFraction > 1f) claimedLandAreaFraction = 1f;
            int n = (int)Math.Round(expectedRegions * (double)claimedLandAreaFraction);
            return n < 0 ? 0 : n;
        }

        /// <summary>Apportion <paramref name="total"/> whole territories across the given share weights by
        /// the largest-remainder (Hamilton) method, so the counts sum to exactly <paramref name="total"/>
        /// and each faction lands within one of its ideal share. Weights ≤ 0 are treated as 0 (no slice).
        /// The result is index-aligned with <paramref name="weights"/>. Used by the debug share report so its
        /// "estimate" column reproduces worldgen's distribution exactly.</summary>
        public static int[] Apportion(IList<float> weights, int total)
        {
            int n = weights?.Count ?? 0;
            var result = new int[n];
            if (n == 0 || total <= 0) return result;

            float sum = 0f;
            for (int i = 0; i < n; i++) if (weights[i] > 0f) sum += weights[i];
            if (sum <= 0f) return result;

            // Floor each ideal share; track the fractional remainders to hand out the leftover seats.
            var remainder = new double[n];
            int assigned = 0;
            for (int i = 0; i < n; i++)
            {
                double ideal = weights[i] > 0f ? weights[i] / sum * total : 0.0;
                int floor = (int)Math.Floor(ideal);
                result[i] = floor;
                remainder[i] = ideal - floor;
                assigned += floor;
            }

            int leftover = total - assigned;
            // Give the remaining seats to the largest remainders, one each, highest first.
            while (leftover > 0)
            {
                int best = -1;
                double bestRem = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (remainder[i] > bestRem)
                    {
                        bestRem = remainder[i];
                        best = i;
                    }
                }
                if (best < 0) break;
                result[best]++;
                remainder[best] = double.NegativeInfinity; // one seat per faction per round
                leftover--;

                // If every faction has taken a leftover seat and seats remain, reset for another round.
                if (leftover > 0)
                {
                    bool anyLeft = false;
                    for (int i = 0; i < n; i++) if (remainder[i] > double.NegativeInfinity) { anyLeft = true; break; }
                    if (!anyLeft)
                    {
                        for (int i = 0; i < n; i++) if (weights[i] > 0f) remainder[i] = weights[i] / sum;
                    }
                }
            }
            return result;
        }

        /// <summary>The region demand the current settings ask for — the numerator of the capacity gate,
        /// which may exceed the planet's regions (that is exactly what "over capacity" means).
        /// <list type="bullet">
        /// <item><b>Count</b>: the sum of the literal per-faction counts.</item>
        /// <item><b>Percent / PlanetAbsolute</b>: the sum of each faction's percent × the planet's regions.</item>
        /// <item><b>Percent / SettledNormalized</b>: the density-scaled claimed total, but never below one per
        /// faction (the minimum-one guarantee can push demand up on a tiny world with many factions).</item>
        /// </list></summary>
        public static int DemandRegions(PlacementValueMode mode, PlacementPercentBasis basis, IList<float> values, int expectedRegions, float claimedFraction)
        {
            int n = values?.Count ?? 0;
            if (expectedRegions <= 0 || n == 0) return 0;
            if (mode == PlacementValueMode.Count)
            {
                int s = 0;
                foreach (var v in values) if (v > 0f) s += (int)Math.Round(v);
                return s;
            }
            if (basis == PlacementPercentBasis.PlanetAbsolute)
            {
                double s = 0.0;
                foreach (var v in values) if (v > 0f) s += (double)v / 100.0 * expectedRegions;
                int a = (int)Math.Round(s);
                return a < 0 ? 0 : a;
            }
            // SettledNormalized: the claimed pool, floored at one per faction.
            int pool = PlacedTotal(expectedRegions, claimedFraction);
            return pool < n ? n : pool;
        }

        /// <summary>
        /// Per-faction placed region counts, index-aligned with <paramref name="values"/>.
        /// <para><b>Percent / SettledNormalized</b> (the default): every faction is GUARANTEED one region,
        /// then the remaining claimed pool (<c>claimedTotal − factionCount</c>) is apportioned across the size
        /// weights by the largest-remainder method and added on top. So a tiny (1×) faction beside two very-
        /// large (16×) ones still lands at least one territory even when its exact share rounds below one —
        /// the minimum-one clamp the owner specified. Sum = the claimed pool (≥ one per faction).</para>
        /// <para><b>Count</b> returns the literal counts (scaled down proportionally only if they exceed the
        /// planet); <b>PlanetAbsolute</b> maps each percent to regions. Both are apportioned so they sum
        /// exactly and no faction is starved by rounding.</para>
        /// </summary>
        public static int[] DistributeRegions(PlacementValueMode mode, PlacementPercentBasis basis, IList<float> values, int expectedRegions, float claimedFraction)
        {
            int n = values?.Count ?? 0;
            if (n == 0) return new int[0];
            if (expectedRegions <= 0) return new int[n];

            if (mode == PlacementValueMode.Percent && basis == PlacementPercentBasis.SettledNormalized)
            {
                int pool = PlacedTotal(expectedRegions, claimedFraction);
                if (pool > expectedRegions) pool = expectedRegions;
                int remainder = pool - n;                 // reserve one per faction
                if (remainder < 0) remainder = 0;
                int[] extra = Apportion(values, remainder);
                var result = new int[n];
                for (int i = 0; i < n; i++) result[i] = 1 + extra[i];   // guaranteed 1 + share of the rest
                return result;
            }

            // Count / PlanetAbsolute: apportion the demand, capped at the planet.
            int demand = DemandRegions(mode, basis, values, expectedRegions, claimedFraction);
            int total = demand < expectedRegions ? demand : expectedRegions;
            return Apportion(values, total);
        }

        /// <summary>Regions left unclaimed (wilderness): the planet's regions minus everything placed. Never
        /// negative. Drives the pie chart's wilderness slice.</summary>
        public static int WildernessRegions(int expectedRegions, IList<int> placed)
        {
            int s = 0;
            if (placed != null) foreach (var p in placed) if (p > 0) s += p;
            int w = expectedRegions - s;
            return w < 0 ? 0 : w;
        }
    }
}
