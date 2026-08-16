using System;
using System.Collections.Generic;
using System.Linq;
using RegionsAndSocieties.Placement;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    public enum ProvinceDomainStatus
    {
        Wilderness,
        DominantOwner,
        Contested,
        Conflict
    }

    /// <summary>
    /// The ownership ladder a single faction's score places it on within a province. The full
    /// four-tier system with all its consequences is 0.8 work (#64); this enum and
    /// <see cref="RegionalDomainUtility.TierOf"/> are the minimal shared vocabulary the 0.7.2
    /// shading (#60) and player-placement (#61) fixes read, so both agree on what "legitimate",
    /// "loose ownership" and "exclusive" mean instead of re-deriving cutoffs each place.
    /// </summary>
    public enum OwnershipTier
    {
        /// <summary>Below the legitimate-claim floor (&lt;30%): a loose claim only.</summary>
        LooseClaim,
        /// <summary>30-50%: a legitimate claim, contestable by other legitimate claims.</summary>
        LegitimateClaim,
        /// <summary>51-70%: clear majority owner, still short of exclusive.</summary>
        LooseOwnership,
        /// <summary>&gt;=71%: exclusive owner.</summary>
        Exclusive
    }

    public static class RegionalDomainUtility
    {
        /// <summary>Which tier a bare ownership score sits on.</summary>
        public static OwnershipTier TierOf(float score)
        {
            if (score >= PlacementRules.ExclusiveThreshold) return OwnershipTier.Exclusive;
            if (score >= PlacementRules.LooseOwnershipThreshold) return OwnershipTier.LooseOwnership;
            if (score >= PlacementRules.OwnershipThreshold) return OwnershipTier.LegitimateClaim;
            return OwnershipTier.LooseClaim;
        }

        /// <summary>
        /// The factions with a legitimate claim (&gt;=30%) on the province, strongest first. This is
        /// the set that decides shading (#60): one legitimate claim shades solid, two or more
        /// cross-hatch. Loose claims (&lt;30%) are deliberately excluded so a faint 5-10% border
        /// bleed no longer makes a region read as contested.
        /// </summary>
        public static List<FactionOwnershipScore> LegitimateClaimsOrdered(RegionalOwnershipData data)
        {
            if (data?.factionScores == null) return new List<FactionOwnershipScore>();
            return data.factionScores
                .Where(s => s != null && s.faction != null && s.TotalScore >= PlacementRules.OwnershipThreshold)
                .OrderByDescending(s => s.TotalScore)
                .ToList();
        }

        /// <summary>
        /// The single faction with an exclusive (&gt;=71%) hold on the province, or null. Used by the
        /// player-placement fix (#61): only an exclusive rival blocks a player start; loose and
        /// legitimate ownership do not.
        /// </summary>
        public static FactionOwnershipScore ExclusiveOwner(RegionalOwnershipData data)
        {
            return data?.factionScores?
                .Where(s => s != null && s.faction != null && s.TotalScore >= PlacementRules.ExclusiveThreshold)
                .OrderByDescending(s => s.TotalScore)
                .FirstOrDefault();
        }

        // #64: the domain status and its description are derived from the four-tier ladder
        // (PlacementRules cutoffs via TierOf), not the old scattered 0.70/0.30 literals — a clear
        // majority owner (loose ownership, >=51%) settles the domain; two or more legitimate claims
        // (>=30%) with no majority is a conflict; one is a single contested claim.
        public static ProvinceDomainStatus GetDomainStatus(RegionalOwnershipData data)
        {
            if (data?.factionScores == null || data.factionScores.Count == 0)
            {
                return ProvinceDomainStatus.Wilderness;
            }

            if (data.factionScores.Any(s => s.TotalScore >= PlacementRules.LooseOwnershipThreshold))
            {
                return ProvinceDomainStatus.DominantOwner;
            }

            int legitimate = data.factionScores.Count(s => s.TotalScore >= PlacementRules.OwnershipThreshold);
            if (legitimate >= 2) return ProvinceDomainStatus.Conflict;
            if (legitimate == 1) return ProvinceDomainStatus.Contested;
            return ProvinceDomainStatus.Wilderness;
        }

        /// <summary>The clear majority owner (loose ownership, &gt;=51%) of the province, or null. Only
        /// one faction can hold a majority, so this is unambiguous.</summary>
        public static Faction GetDominantOwner(RegionalOwnershipData data)
        {
            return data?.factionScores?.FirstOrDefault(s => s.TotalScore >= PlacementRules.LooseOwnershipThreshold)?.faction;
        }

        public static List<Faction> GetContestedFactions(RegionalOwnershipData data)
        {
            return data?.factionScores?.Where(s => s.TotalScore >= PlacementRules.OwnershipThreshold).Select(s => s.faction).ToList() ?? new List<Faction>();
        }

        /// <summary>The province's ownership in the four-tier vocabulary (#64), for tooltips and the
        /// region panel — exclusive / loose ownership / legitimate claim / contested / wilderness.</summary>
        public static string GetStatusDescription(RegionalOwnershipData data)
        {
            var legit = LegitimateClaimsOrdered(data);   // >=30%, strongest first
            if (legit.Count == 0)
            {
                return "Unclaimed wilderness (no legitimate claim)";
            }

            var top = legit[0];
            OwnershipTier tier = TierOf(top.TotalScore);
            string topName = TextureUtility.GetFactionDisplayName(top.faction);

            // Two or more legitimate claims and nobody at a clear majority → contested between claimants.
            if (legit.Count >= 2 && tier < OwnershipTier.LooseOwnership)
            {
                var names = legit.Select(s => TextureUtility.GetFactionDisplayName(s.faction));
                return "Contested: " + string.Join(" vs ", names) + " (legitimate claims, >=30%)";
            }

            switch (tier)
            {
                case OwnershipTier.Exclusive:
                    return "Exclusive domain of " + topName + " (>=71%)";
                case OwnershipTier.LooseOwnership:
                    return topName + " holds this region (loose ownership, 51-70%)";
                default:   // a single legitimate claim, 30-50%
                    return "Claimed by " + topName + " (legitimate claim, 30-50%)";
            }
        }
    }
}
