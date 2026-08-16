using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Placement;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// The event a player permanent holding raises when it lands in a province a rival legitimately
    /// claims (&gt;=30%). Carries the strongest angered claimant and how strong its claim is, so a
    /// consumer can react proportionally.
    /// </summary>
    public class TerritoryClaimContestedArgs
    {
        public Faction settler;        // the faction that placed the holding (the player)
        public Faction claimant;       // the strongest rival that claims the province
        public int provinceId;
        public OwnershipTier tier;     // the claimant's ownership tier
        public float claimStrength;    // the claimant's score, 0..1
    }

    /// <summary>
    /// Anger-on-claim hook (#66). When the player plants a settlement in a province a rival claims, R&amp;T
    /// raises <see cref="TerritoryClaimContestedArgs"/>. R&amp;T does nothing with it beyond exposing it:
    /// RimSynapse Core / a storyteller may register a <see cref="Handler"/> and react (aggression,
    /// notifications). If <b>nothing consumes the event</b>, R&amp;T applies a default goodwill penalty
    /// scaled by claim strength — -15 for a legitimate claim (30-50%), -40 for loose ownership or better
    /// (&gt;=51%).
    ///
    /// <para>The registry is a no-op without a consumer, and reflection-friendly: R&amp;T holds no
    /// reference to Core, so Core wires <see cref="Handler"/> after load (the mirror of
    /// <c>RegisterProvidersWithCore</c>).</para>
    /// </summary>
    public static class TerritoryClaimHooks
    {
        /// <summary>
        /// Optional consumer. Return <c>true</c> to CONSUME the event and suppress R&amp;T's default
        /// goodwill penalty; <c>false</c> (or unset) leaves R&amp;T to apply the default.
        /// </summary>
        public static Func<TerritoryClaimContestedArgs, bool> Handler;

        /// <summary>Raise the event; returns true when a consumer handled it.</summary>
        public static bool Fire(TerritoryClaimContestedArgs args)
        {
            try { return Handler != null && Handler(args); }
            catch (Exception ex)
            {
                Log.Error($"[RegionsAndSocieties] TerritoryClaim handler threw: {ex}");
                return false;
            }
        }

        /// <summary>Default goodwill penalty for a rival whose claimed province was settled, by tier (#66).</summary>
        public static int DefaultPenaltyFor(OwnershipTier tier)
        {
            return tier >= OwnershipTier.LooseOwnership ? -40 : -15;
        }

        /// <summary>
        /// The whole consequence layer, called when any world object is added (from the
        /// WorldObjectsHolder.Add postfix). No-ops unless it is a settlement of the PLAYER — a rival's
        /// worldgen placement is governed by the aversive placement rules, not this. Fires the hook for
        /// the strongest claimant and, if unconsumed, applies the default goodwill penalty to every rival
        /// that legitimately claims the province.
        /// </summary>
        public static void OnPermanentHoldingPlaced(WorldObject holding)
        {
            if (holding == null || !WorldObjectClassifier.IsSettlement(holding)) return;

            Faction settler = holding.Faction;
            if (settler == null || !settler.IsPlayer) return;   // only the player's contested claims anger rivals

            World world = Find.World;
            if (world == null) return;
            var mgr = world.GetComponent<SynapseRegionManager>();
            if (mgr == null) return;
            var province = mgr.GetProvinceForTile(holding.Tile);
            if (province == null) return;

            mgr.MarkOwnersDirty();
            mgr.RecalculateProvinceOwners();
            var data = province.ownershipData;
            if (data?.factionScores == null) return;

            var rivalClaims = data.factionScores
                .Where(s => s.faction != null && s.faction != settler && s.TotalScore >= PlacementRules.OwnershipThreshold)
                .OrderByDescending(s => s.TotalScore)
                .ToList();
            if (rivalClaims.Count == 0) return;   // unclaimed or your own ground — no one to anger

            var strongest = rivalClaims[0];
            var args = new TerritoryClaimContestedArgs
            {
                settler = settler,
                claimant = strongest.faction,
                provinceId = province.id,
                tier = RegionalDomainUtility.TierOf(strongest.TotalScore),
                claimStrength = strongest.TotalScore
            };

            if (Fire(args)) return;   // a consumer handled it — no default penalty

            foreach (var claim in rivalClaims)
            {
                int delta = DefaultPenaltyFor(RegionalDomainUtility.TierOf(claim.TotalScore));
                try
                {
                    claim.faction.TryAffectGoodwillWith(settler, delta, canSendMessage: true, canSendHostilityLetter: true, reason: null);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RegionsAndSocieties] goodwill penalty on {claim.faction?.Name} failed: {ex.Message}");
                }
                Log.Message($"[RegionsAndSocieties] Territory contested (#66): player settled province {province.id}, claimed by {claim.faction.Name} ({claim.TotalScore:0.00}, {RegionalDomainUtility.TierOf(claim.TotalScore)}); default goodwill {delta}.");
            }
        }
    }
}
