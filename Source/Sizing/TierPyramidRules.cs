namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The structural tier model (0.8): a faction's settlements form a pyramid where each tier is at
    /// least one wider than the tier above it — <c>count(t) &gt;= count(t+1) + 1</c>. A capital of tier
    /// T therefore costs a full staircase beneath it, so the minimum settlements to *afford* a tier-T
    /// capital is the triangular number T(T+1)/2: T1→1, T2→3, T3→6, T4→10, T5→15.
    ///
    /// Pure, in the manner of <see cref="OutpostAllowanceRules"/>: it works on plain settlement counts
    /// and ranks, so the whole model is testable without a game. The live facade
    /// (<c>SettlementSizeUtility</c>) supplies the counts and the per-settlement protection ranking.
    ///
    /// <para>Generation is bottom-heavy by the owner's rule ("always build from the lower tiers"): a
    /// faction gets one capital at the highest tier its count affords, the minimal staircase beneath
    /// it, and every leftover settlement at T1 — extras widen the base rather than raising a second
    /// apex.</para>
    /// </summary>
    public static class TierPyramidRules
    {
        /// <summary>Highest tier index the game supports. Tier 5 is <see cref="SettlementTier.Metropolis"/>.</summary>
        public const int MaxTier = 5;

        /// <summary>Minimum settlements to afford a capital of this tier: the triangular number T(T+1)/2.</summary>
        public static int TerritoriesForTier(int tier)
        {
            if (tier <= 0) return 0;
            return tier * (tier + 1) / 2;
        }

        /// <summary>
        /// The highest capital tier a faction of <paramref name="settlementCount"/> settlements can
        /// afford: the largest T with T(T+1)/2 ≤ count, capped at <see cref="MaxTier"/>. 0 when the
        /// faction has no settlements.
        /// </summary>
        public static int MaxCapitalTier(int settlementCount)
        {
            int tier = 0;
            for (int t = 1; t <= MaxTier; t++)
            {
                if (TerritoriesForTier(t) <= settlementCount) tier = t;
                else break;
            }
            return tier;
        }

        /// <summary>
        /// How many settlements sit at each tier for a faction of <paramref name="settlementCount"/>.
        /// Indexed by tier: result[1..5] is the count at tiers 1..5; result[0] is unused (0).
        ///
        /// One capital at the max affordable tier, the minimal staircase beneath it
        /// (tier t gets <c>maxTier - t + 1</c>), and all leftovers at T1. The result always satisfies
        /// <c>count(t) &gt;= count(t+1) + 1</c>.
        /// </summary>
        public static int[] TierCounts(int settlementCount)
        {
            var counts = new int[MaxTier + 1];
            if (settlementCount <= 0) return counts;

            int top = MaxCapitalTier(settlementCount);
            for (int t = 1; t <= top; t++)
            {
                counts[t] = top - t + 1;   // T1 gets `top`, the capital tier gets 1
            }

            int leftover = settlementCount - TerritoriesForTier(top);
            counts[1] += leftover;         // extras widen the base ("build from the lower tiers")
            return counts;
        }

        /// <summary>
        /// The tier of the settlement at <paramref name="protectionRank"/> (0 = most protected), given
        /// the per-tier <paramref name="counts"/> from <see cref="TierCounts"/>. The most-protected
        /// settlements take the highest tiers; the capital is rank 0. Returns
        /// <see cref="SettlementTier.None"/> for a rank past the last settlement.
        /// </summary>
        public static SettlementTier TierForRank(int protectionRank, int[] counts)
        {
            if (counts == null || protectionRank < 0) return SettlementTier.None;

            int threshold = 0;
            for (int t = MaxTier; t >= 1; t--)
            {
                threshold += counts[t];
                if (protectionRank < threshold) return (SettlementTier)t;
            }
            return SettlementTier.None;
        }
    }
}
