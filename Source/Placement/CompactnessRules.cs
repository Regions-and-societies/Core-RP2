namespace RegionsAndSocieties.Placement
{
    /// <summary>
    /// The territory-shape core (#19). Faction domains should grow as compact blobs, not as spidering
    /// chains of provinces linked at single touch-points. The unit of shape is <b>embeddedness</b>: of
    /// the borders a candidate province could ever share with a claimant (its Land-type neighbours —
    /// ocean and impassable ranges leave the denominator, so geography reads as a free wall), what
    /// fraction does the claimant already hold? A tendril tip scores near zero; a concave pocket fill
    /// scores near one.
    ///
    /// <para>Compactness is a <b>preference, never a rule</b>: a candidate below the desired ratio has
    /// its suitability scaled down in proportion, so a cornered faction still takes the awkward province
    /// rather than stalling — it just needs dramatically better land to justify the tendril. The weight
    /// (a mod setting) blends the penalty from 0 (legacy behaviour) to 1 (full squaring).</para>
    ///
    /// <para>Pure by design, like the rest of the Placement rules layer: counts and scalars in, scores
    /// out, no game state — testable without a game and shared by worldgen placement and any expansion
    /// mod ranking candidate provinces through the public surface.</para>
    /// </summary>
    public static class CompactnessRules
    {
        /// <summary>The target embeddedness (#19): candidates at or above it count as "squaring" picks
        /// and keep their full suitability. First-pass value, tunable.</summary>
        public const float DefaultDesiredRatio = 0.4f;

        /// <summary>
        /// How embedded a candidate province is in a claimant's existing domain: owned claimable
        /// neighbours over all claimable neighbours, 0..1. A province with no claimable neighbours at
        /// all (an island, a pass surrounded by ocean and ice) counts as fully embedded — there is
        /// nothing to square against, so shape should not veto it.
        /// </summary>
        public static float Embeddedness(int ownedNeighbors, int claimableNeighbors)
        {
            if (claimableNeighbors <= 0) return 1f;
            if (ownedNeighbors <= 0) return 0f;
            if (ownedNeighbors >= claimableNeighbors) return 1f;
            return (float)ownedNeighbors / claimableNeighbors;
        }

        /// <summary>
        /// A candidate's suitability bent by its shape. At or above <paramref name="desiredRatio"/> the
        /// suitability passes through untouched; below it, the score scales down toward
        /// (embeddedness / ratio), blended in by <paramref name="weight"/> (0 = ignore shape entirely,
        /// 1 = full penalty). Monotonic in embeddedness, continuous at the ratio, and never negative.
        /// </summary>
        public static float EffectiveScore(float suitability, float embeddedness, float desiredRatio, float weight)
        {
            if (weight <= 0f || desiredRatio <= 0f) return suitability;
            if (weight > 1f) weight = 1f;

            float shape = embeddedness / desiredRatio;
            if (shape > 1f) shape = 1f;
            if (shape < 0f) shape = 0f;

            float factor = 1f + weight * (shape - 1f);   // lerp(1, shape, weight)
            return suitability * factor;
        }

        /// <summary>
        /// A whole domain's compactness, 0..1: of every claimable border its provinces have, the share
        /// that faces another held province. 1 = a closed blob (or a domain with no claimable borders at
        /// all), 0 = every border is frontier — pure spider. For readouts and expansion mods.
        /// </summary>
        public static float DomainCompactness(int internalEdges, int frontierEdges)
        {
            if (internalEdges < 0) internalEdges = 0;
            if (frontierEdges < 0) frontierEdges = 0;
            int total = internalEdges + frontierEdges;
            if (total <= 0) return 1f;
            return (float)internalEdges / total;
        }
    }
}
