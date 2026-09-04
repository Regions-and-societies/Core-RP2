using System.Collections.Generic;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// How a settlement's people spread over the land around it (0.3.0). The settlement tile keeps a
    /// core share; the rest is handed out over the tiles its sprawl reaches, in proportion to how
    /// strongly the sprawl reaches each one — the caller's terrain-aware propagation supplies those
    /// weights (roads and rivers carry people further; hills, swamps and water thin them; impassable
    /// ground and open sea stop them). The total is conserved: centre + Σ shares == population, so
    /// region totals are the sum of who lives there, and the density heatmap, the per-tile label and
    /// the residence layer all read this one field.
    ///
    /// <para>Pure: weights in, shares out. No world.</para>
    /// </summary>
    public static class SprawlRules
    {
        /// <summary>The settlement tile keeps at least this share of its people; the rest is the sprawl's budget.</summary>
        public const float CoreShare = 0.5f;

        /// <summary>
        /// Sprawl weights below this fraction of the centre's weight are not worth carrying: with the
        /// budget normalised over the whole sprawl they would round to nobody, so the propagation stops
        /// expanding there. On open flat ground (a 0.5 step) that is about seven rings; along a road or
        /// river (0.75 steps) about sixteen tiles.
        /// </summary>
        public const float WeightCutoff = 0.01f;

        /// <summary>
        /// Split <paramref name="population"/> between the centre and the sprawl tiles. <paramref name="weights"/>
        /// holds one relative weight per reached tile (the centre is not among them). Fills
        /// <paramref name="shares"/> (cleared first, then one entry per weight) and returns the centre's
        /// amount. With no reachable tile, or none of positive weight, everything stays on the centre.
        /// </summary>
        public static float Spread(float population, List<float> weights, List<float> shares)
        {
            shares.Clear();
            if (population <= 0f)
            {
                if (weights != null) for (int i = 0; i < weights.Count; i++) shares.Add(0f);
                return 0f;
            }

            float total = 0f;
            int n = weights?.Count ?? 0;
            for (int i = 0; i < n; i++) if (weights[i] > 0f) total += weights[i];
            if (total <= 0f)
            {
                for (int i = 0; i < n; i++) shares.Add(0f);
                return population;
            }

            float budget = population * (1f - CoreShare);
            float placed = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = weights[i];
                float share = w > 0f ? budget * (w / total) : 0f;
                shares.Add(share);
                placed += share;
            }
            return population - placed;
        }
    }
}
