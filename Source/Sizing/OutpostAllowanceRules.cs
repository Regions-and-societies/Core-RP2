namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// How many outposts a territory may hold, as a function of the tier of the settlement anchoring
    /// it (0.8). A larger settlement supports more surrounding production holdings.
    ///
    /// Pure, in the manner of <see cref="SettlementSizeRules"/> and <c>PlacementRules</c>: the number
    /// comes from the tier alone, so the outpost-seeding pass can be reasoned about and tested without
    /// a running game.
    ///
    /// The ladder is the owner's spec: Tier 1 (Village) allows two outposts, and every tier above it
    /// allows one more. A holding with no tier — anything that is not a population centre — anchors no
    /// outposts.
    /// </summary>
    public static class OutpostAllowanceRules
    {
        /// <summary>
        /// Outposts a territory anchored by a settlement of this tier may hold.
        /// None 0, Village 2, Town 3, City 4, Major City 5, Metropolis 6 ("T1 = 2 outposts, +1 per tier").
        /// </summary>
        public static int OutpostAllowance(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return 2;
                case SettlementTier.Town: return 3;
                case SettlementTier.City: return 4;
                case SettlementTier.MajorCity: return 5;
                case SettlementTier.Metropolis: return 6;
                default: return 0;
            }
        }

        /// <summary>
        /// How many more outposts a territory may take, given the tier anchoring it and the number of
        /// outposts already standing in it. Never negative — a territory already over its allowance
        /// (a tier dropped, or outposts arrived from elsewhere) simply takes no more.
        /// </summary>
        public static int RemainingAllowance(SettlementTier tier, int existingOutposts)
        {
            int room = OutpostAllowance(tier) - existingOutposts;
            return room > 0 ? room : 0;
        }
    }
}
