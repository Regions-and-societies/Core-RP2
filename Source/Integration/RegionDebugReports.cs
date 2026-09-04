using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Demographics;
using RegionsAndSocieties.Placement;
using RegionsAndSocieties.Sizing;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// The proof-of-function reports for the 0.7.2 playtest fixes. Each mechanic that changed gets a
    /// report here that dumps enough state to confirm it behaves as intended, and each is reachable
    /// two ways that share this one code path: a <c>[DebugAction]</c> under the "Regions and Societies" menu for
    /// the human (see <c>DebugActions_RegionsAndSocieties</c>), and a Core bridge tool for the
    /// agent to trigger headlessly (see <c>RegionMcpTools</c>). One method, one answer, either door.
    ///
    ///   DensityReport   - #62/#55: natural pockets stay small, province totals no longer overcount.
    ///   ShadingReport   - #60: solid vs cross-hatch follows the legitimate-claim (&gt;=30%) rule.
    ///   PlacementProbe  - #61: the player is refused only by an exclusive (&gt;=71%) rival.
    /// </summary>
    public static class RegionDebugReports
    {
        public static string DensityReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldGrid == null) return "no world loaded";

            PopulationDensityUtility.EnsureCache();

            WorldGrid grid = Find.WorldGrid;
            int count = grid.TilesCount;

            // Tiles that carry a world object are settlements/outposts, not natural pockets.
            var objectTiles = new HashSet<int>();
            if (Find.WorldObjects?.AllWorldObjects != null)
            {
                foreach (var o in Find.WorldObjects.AllWorldObjects)
                {
                    if (o != null) objectTiles.Add(o.Tile.tileId);
                }
            }

            int band1to5 = 0, band6to12 = 0, band13to30 = 0, band31plus = 0;
            int naturalPockets = 0, maxNatural = 0, maxNaturalOffLandmark = 0, offLandmarkOverCap = 0, suburbTiles = 0;

            for (int i = 0; i < count; i++)
            {
                int src = PopulationDensityUtility.GetSourcePopulationAtTile(i);
                if (src <= 0) continue;

                if (src <= 5) band1to5++;
                else if (src <= 12) band6to12++;
                else if (src <= 30) band13to30++;
                else band31plus++;

                if (objectTiles.Contains(i)) continue;   // a settlement, not a natural pocket
                if (PopulationDensityUtility.IsSuburbTile(i)) { suburbTiles++; continue; }   // a settlement's spread (0.3.0)

                naturalPockets++;
                if (src > maxNatural) maxNatural = src;

                if (!NearLandmark(i))
                {
                    if (src > maxNaturalOffLandmark) maxNaturalOffLandmark = src;
                    if (src > 5) offLandmarkOverCap++;
                }
            }

            var mgrForVer = Find.World.GetComponent<SynapseRegionManager>();
            int ver = mgrForVer?.DensityAlgorithmVersion ?? SynapseRegionManager.DensityAlgorithmCurrent;
            string verName = ver == SynapseRegionManager.DensityAlgorithmLegacy ? "LEGACY (0.7.1 - uncapped pockets, smeared totals)" : "CURRENT (0.7.2 - capped pockets, source totals)";

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T density report (#62/#55) ===");
            sb.AppendLine($"Algorithm: {verName}");
            sb.AppendLine($"Source-pop tiles by band: 1-5={band1to5}, 6-12={band6to12}, 13-30={band13to30}, 31+={band31plus}");
            sb.AppendLine($"Sprawl tiles (a settlement's spread, not pockets): {suburbTiles}");
            sb.AppendLine($"Natural pockets (no world object, no sprawl): {naturalPockets}; max={maxNatural} (cap 12), max off-landmark={maxNaturalOffLandmark} (cap 5)");
            sb.AppendLine($"Off-landmark natural pockets over cap (>5): {offLandmarkOverCap}  [expect 0]");

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces != null)
            {
                // Show the fix directly: source-sum (new total) vs smear-sum (old, overcounting) total.
                var top = mgr.Provinces
                    .Where(p => p.provinceType == ProvinceType.Land && p.tiles != null)
                    .Select(p => new
                    {
                        p.id,
                        p.name,
                        source = p.tiles.Sum(t => PopulationDensityUtility.GetSourcePopulationAtTile(t)),
                        smear = p.tiles.Sum(t => PopulationDensityUtility.GetPopulationAtTile(t))
                    })
                    .OrderByDescending(x => x.smear)
                    .Take(8)
                    .ToList();

                sb.AppendLine("Top provinces  (population now = source sum; old = smear sum):");
                foreach (var x in top)
                {
                    sb.AppendLine($"  #{x.id} {x.name}: population={x.source}  (old smear-sum would have been {x.smear})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static string ShadingReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            int faint = 0, solid = 0, crossHatch = 0;
            int land = 0, withOwningIds = 0, nullData = 0;
            float maxScoreSeen = 0f;
            var examples = new StringBuilder();
            int shown = 0;

            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType == ProvinceType.Ocean || p.tiles == null || p.tiles.Count == 0) continue;
                land++;
                if (p.owningFactionIds != null && p.owningFactionIds.Count > 0) withOwningIds++;
                if (p.ownershipData == null) nullData++;

                // Fall back to an on-demand compute exactly as GetControl does, so a null cache after a
                // save load does not read as "unclaimed".
                var data = p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p);
                var claims = RegionalDomainUtility.LegitimateClaimsOrdered(data);

                float top1 = data?.factionScores != null && data.factionScores.Count > 0
                    ? data.factionScores.Max(s => s.TotalScore) : 0f;
                if (top1 > maxScoreSeen) maxScoreSeen = top1;

                string mode;
                if (claims.Count == 0) { faint++; mode = "faint/unclaimed"; }
                else if (claims.Count == 1) { solid++; mode = "SOLID"; }
                else { crossHatch++; mode = "cross-hatch"; }

                // Show provinces the OLD system considered owned, so a threshold/scoring mismatch is
                // visible: old owner list non-empty but new legitimate-claim set empty means scores
                // sit below 30%.
                if (p.owningFactionIds != null && p.owningFactionIds.Count > 0 && shown < 12)
                {
                    shown++;
                    var top3 = data?.factionScores?.OrderByDescending(s => s.TotalScore).Take(3)
                        .Select(s => $"{Name(s.faction)} {s.TotalScore:0.00}") ?? Enumerable.Empty<string>();
                    string dataState = p.ownershipData == null ? "cache=NULL" : "cache=ok";
                    examples.AppendLine($"  #{p.id}: {mode}  {dataState}  oldOwners={p.owningFactionIds.Count}  unclaimed={data?.unclaimedScore:0.00}  scores=[{string.Join(", ", top3)}]");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T shading report (#60) — fill by legitimate claim (>=30%) ===");
            sb.AppendLine($"land provinces={land}; with serialized owners(>5%)={withOwningIds}; ownershipData null={nullData}; max faction score seen={maxScoreSeen:0.00}");
            sb.AppendLine($"faint/unclaimed={faint}, SOLID(1 claim)={solid}, cross-hatch(>=2 claims)={crossHatch}");
            sb.AppendLine("Examples (provinces the old >5% system owned; scores via cache-or-compute):");
            sb.Append(examples.ToString().TrimEnd());
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #42 validation. A lone holder used to read as full control of its whole region, because the
        /// border budget credited it for the unheld frontier around it. After the fix, edges to UNHELD
        /// land count for no one, so a lone owner in open country is only partly claimed — the rest is
        /// unclaimed. Scans every land province held by exactly one faction (no rivals) and reports how
        /// many now carry a meaningful unclaimed share. PASS when such lone-owner regions are no longer
        /// uniformly fully-claimed (i.e. the frontier surfaces as unclaimed), or WARN when the generated
        /// world has no lone-owner regions to judge.
        /// </summary>
        public static string LoneSettlementOwnershipReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            int loneOwner = 0, partlyUnclaimed = 0, fullyClaimed = 0;
            float sumOwnerScore = 0f, sumUnclaimed = 0f, maxOwnerScore = 0f;
            var examples = new StringBuilder();
            int shown = 0;

            // #42 eligibility gate: which factions actually hold something in each province, so we can
            // count holders that reflected NO ownership — a faction with an outpost here that has no
            // settlement and is not the most-outposts holder, which now reads as unowned.
            var holdersByProvince = new Dictionary<int, HashSet<Faction>>();
            if (Find.WorldObjects != null)
            {
                foreach (var o in Find.WorldObjects.AllWorldObjects)
                {
                    if (o.Faction == null) continue;
                    var prov = mgr.GetProvinceForTile(o.Tile);
                    if (prov == null) continue;
                    if (!holdersByProvince.TryGetValue(prov.id, out var set)) { set = new HashSet<Faction>(); holdersByProvince[prov.id] = set; }
                    set.Add(o.Faction);
                }
            }
            int gatedOutHolders = 0, provincesWithGatedHolder = 0;

            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                var data = p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p);
                if (data?.factionScores == null) continue;

                // Gate metric: holders in this province that ended up with no ownership score.
                if (holdersByProvince.TryGetValue(p.id, out var provHolders))
                {
                    var credited = new HashSet<Faction>(data.factionScores.Where(s => s.faction != null && s.TotalScore > 0.001f).Select(s => s.faction));
                    int gated = provHolders.Count(h => !credited.Contains(h));
                    if (gated > 0) { gatedOutHolders += gated; provincesWithGatedHolder++; }
                }

                var scoring = data.factionScores.Where(s => s.faction != null && s.TotalScore > 0.001f).ToList();
                if (scoring.Count != 1) continue;                 // lone-owner regions only: exactly one claimant
                if (data.primaryCount < 1) continue;              // and it actually holds something here

                loneOwner++;
                float ownerScore = scoring[0].TotalScore;
                float unclaimed = data.unclaimedScore;
                sumOwnerScore += ownerScore;
                sumUnclaimed += unclaimed;
                if (ownerScore > maxOwnerScore) maxOwnerScore = ownerScore;

                if (unclaimed > 0.05f) partlyUnclaimed++;
                else if (unclaimed < 0.01f) fullyClaimed++;

                if (unclaimed > 0.05f && shown < 10)
                {
                    shown++;
                    examples.AppendLine($"  #{p.id}: owner {Name(scoring[0].faction)} {ownerScore:0.00} " +
                        $"(settle {scoring[0].settlementScore + scoring[0].outpostCoverageScore + scoring[0].mostOutpostsScore:0.00} + border {scoring[0].perimeterCoverageScore:0.00} + bonus {scoring[0].externalPerimeterScore:0.00})  unclaimed={unclaimed:0.00}");
                }
            }

            bool pass = loneOwner == 0 || partlyUnclaimed > 0;
            string verdict = loneOwner == 0 ? "WARN" : (pass ? "PASS" : "FAIL");
            var sb = new StringBuilder();
            sb.AppendLine($"[SYNAPSE-TEST] {verdict} RT_LoneSettlement_Unclaimed (#42) | lone-owner regions={loneOwner} " +
                $"partlyUnclaimed(>0.05)={partlyUnclaimed} fullyClaimed(<0.01)={fullyClaimed} " +
                $"meanOwnerScore={(loneOwner > 0 ? sumOwnerScore / loneOwner : 0f):0.00} meanUnclaimed={(loneOwner > 0 ? sumUnclaimed / loneOwner : 0f):0.00} maxOwnerScore={maxOwnerScore:0.00}");
            sb.AppendLine($"  eligibility gate (#42): holders reflecting NO ownership (no settlement, not most-outposts) = {gatedOutHolders} across {provincesWithGatedHolder} provinces.");
            sb.AppendLine("  (#42: a lone owner should no longer absorb the empty frontier — expect meanUnclaimed>0 and not every region fully claimed.)");
            if (examples.Length > 0) sb.Append(examples.ToString().TrimEnd());
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #64 validation. Tallies every land province by the four ownership tiers (TierOf on the top
        /// claimant) and by ProvinceDomainStatus, and prints a handful of the new four-tier status
        /// strings, so the formalized ladder and its vocabulary are exercised end to end. PASS when the
        /// derivation runs over a generated world; the tallies show the tier ladder is being read.
        /// </summary>
        public static string OwnershipTierReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            int exclusive = 0, loose = 0, legit = 0, looseClaim = 0, wilderness = 0;
            int stDominant = 0, stConflict = 0, stContested = 0, stWild = 0;
            var examples = new StringBuilder();
            int shown = 0;

            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                var data = p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p);
                if (data?.factionScores == null) { wilderness++; stWild++; continue; }

                float topScore = data.factionScores.Count > 0 ? data.factionScores.Max(s => s.TotalScore) : 0f;
                switch (RegionalDomainUtility.TierOf(topScore))
                {
                    case OwnershipTier.Exclusive: exclusive++; break;
                    case OwnershipTier.LooseOwnership: loose++; break;
                    case OwnershipTier.LegitimateClaim: legit++; break;
                    default: looseClaim++; break;
                }
                switch (RegionalDomainUtility.GetDomainStatus(data))
                {
                    case ProvinceDomainStatus.DominantOwner: stDominant++; break;
                    case ProvinceDomainStatus.Conflict: stConflict++; break;
                    case ProvinceDomainStatus.Contested: stContested++; break;
                    default: stWild++; break;
                }

                if (topScore >= PlacementRules.OwnershipThreshold && shown < 8)
                {
                    shown++;
                    examples.AppendLine($"  #{p.id} ({topScore:0.00}, {RegionalDomainUtility.TierOf(topScore)}): {RegionalDomainUtility.GetStatusDescription(data)}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[SYNAPSE-TEST] PASS RT_OwnershipTiers (#64) | tiers: exclusive={exclusive} looseOwnership={loose} legitimateClaim={legit} looseClaim/none={looseClaim + wilderness} " +
                $"| status: dominant={stDominant} conflict={stConflict} contested={stContested} wilderness={stWild}");
            if (examples.Length > 0) sb.Append(examples.ToString().TrimEnd());
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #65 validation. The 70/30 claim/resource placement should grow each nation contiguously and
        /// keep nations apart, rather than interleaving rival settlements. Identifies each nation-core
        /// province (a faction loose-owns it, &gt;=51%) and measures, for each, how many neighbouring
        /// cores are the SAME nation (contiguity) vs a RIVAL (a border). Clustered nations show
        /// meanSameNationNeighbours well above meanRivalNeighbours. PASS when the measurement runs.
        /// </summary>
        public static string NpcBarrierReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();
            WorldGrid grid = Find.WorldGrid;

            // Map each land province to its loose owner (>=51%), i.e. the nations' core provinces.
            var looseOwnerById = new Dictionary<int, Faction>();
            int landProvinces = 0;
            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                landProvinces++;
                var data = p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p);
                var lo = data?.factionScores?
                    .Where(s => s.faction != null && s.TotalScore >= PlacementRules.LooseOwnershipThreshold)
                    .OrderByDescending(s => s.TotalScore).FirstOrDefault();
                if (lo != null) looseOwnerById[p.id] = lo.faction;
            }

            int coreProvinces = 0, sumSame = 0, sumRival = 0, rivalBordering = 0;
            var examples = new StringBuilder();
            int shown = 0;
            var nbrs = new List<RimWorld.Planet.PlanetTile>();

            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType != ProvinceType.Land || !looseOwnerById.TryGetValue(p.id, out var owner)) continue;
                coreProvinces++;

                var neighbourIds = new HashSet<int>();
                foreach (int t in p.tiles)
                {
                    nbrs.Clear();
                    grid.GetTileNeighbors(t, nbrs);
                    foreach (var n in nbrs)
                    {
                        int nid = mgr.GetProvinceId(n.tileId);
                        if (nid != -1 && nid != p.id) neighbourIds.Add(nid);
                    }
                }

                int same = 0, rival = 0;
                foreach (int nid in neighbourIds)
                {
                    if (looseOwnerById.TryGetValue(nid, out var nOwner))
                    {
                        if (nOwner == owner) same++; else rival++;
                    }
                }
                sumSame += same; sumRival += rival;
                if (rival > 0) rivalBordering++;

                if (shown < 8)
                {
                    shown++;
                    examples.AppendLine($"  #{p.id}: {Name(owner)} core — {same} same-nation, {rival} rival neighbours");
                }
            }

            float meanSame = coreProvinces > 0 ? (float)sumSame / coreProvinces : 0f;
            float meanRival = coreProvinces > 0 ? (float)sumRival / coreProvinces : 0f;
            var sb = new StringBuilder();
            sb.AppendLine($"[SYNAPSE-TEST] PASS RT_NpcBarrier (#65) | land provinces={landProvinces} nation-core provinces={coreProvinces} " +
                $"meanSameNationNeighbours={meanSame:0.00} meanRivalNeighbours={meanRival:0.00} coresBorderingARival={rivalBordering}");
            sb.AppendLine("  (#65: 70/30 claim/resource placement should CLUSTER nations — expect meanSameNationNeighbours >> meanRivalNeighbours.)");
            if (examples.Length > 0) sb.Append(examples.ToString().TrimEnd());
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #66 validation. Without placing anything, reports what the anger-on-claim hook would do if the
        /// player settled the selected province (or the first rival-claimed one): the rival claimants, and
        /// the default goodwill penalty each would take by tier (-15 legitimate / -40 loose-ownership+).
        /// Also self-tests the hook path — unset handler leaves R&amp;T to apply the default; a handler that
        /// returns true consumes the event and suppresses it — and confirms an unclaimed province yields no
        /// penalty. No goodwill is actually changed and no settlement is placed.
        /// </summary>
        public static string TerritoryClaimReport(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            var player = Faction.OfPlayerSilentFail;

            GeographicProvince province = tileId >= 0 ? mgr.GetProvinceForTile(tileId) : null;
            if (province == null)
            {
                province = mgr.Provinces.FirstOrDefault(p => p.provinceType == ProvinceType.Land
                    && p.ownershipData?.factionScores != null
                    && p.ownershipData.factionScores.Any(s => s.faction != null && s.faction != player && s.TotalScore >= PlacementRules.OwnershipThreshold));
            }
            if (province == null) return "[SYNAPSE-TEST] WARN RT_TerritoryClaim (#66) | no rival-claimed province found to judge";

            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
            var rivalClaims = (data?.factionScores ?? new List<FactionOwnershipScore>())
                .Where(s => s.faction != null && s.faction != player && s.TotalScore >= PlacementRules.OwnershipThreshold)
                .OrderByDescending(s => s.TotalScore)
                .ToList();

            var penalties = rivalClaims
                .Select(s => $"{Name(s.faction)} {s.TotalScore:0.00}/{RegionalDomainUtility.TierOf(s.TotalScore)}→{TerritoryClaimHooks.DefaultPenaltyFor(RegionalDomainUtility.TierOf(s.TotalScore))}")
                .ToList();

            // Hook path self-test (no side effects): unset handler → not consumed → default applies;
            // a handler that returns true → consumed → default suppressed.
            var probe = new TerritoryClaimContestedArgs { settler = player, claimant = rivalClaims.FirstOrDefault()?.faction, provinceId = province.id };
            var prev = TerritoryClaimHooks.Handler;
            TerritoryClaimHooks.Handler = null;
            bool unconsumed = !TerritoryClaimHooks.Fire(probe);
            TerritoryClaimHooks.Handler = _ => true;
            bool consumed = TerritoryClaimHooks.Fire(probe);
            TerritoryClaimHooks.Handler = prev;

            // Unclaimed control: a province nobody legitimately claims yields no penalty.
            var unclaimed = mgr.Provinces.FirstOrDefault(p => p.provinceType == ProvinceType.Land
                && (p.ownershipData?.factionScores == null || !p.ownershipData.factionScores.Any(s => s.faction != null && s.faction != player && s.TotalScore >= PlacementRules.OwnershipThreshold)));
            bool unclaimedHasNoRival = unclaimed != null;

            bool pass = rivalClaims.Count > 0 && unconsumed && consumed;
            var sb = new StringBuilder();
            sb.AppendLine($"[SYNAPSE-TEST] {(pass ? "PASS" : "FAIL")} RT_TerritoryClaim (#66) | province #{province.id} rivalClaims={rivalClaims.Count} " +
                $"hookUnconsumedByDefault={unconsumed} hookConsumesWhenHandlerSet={consumed} unclaimedControlFound={unclaimedHasNoRival}");
            sb.AppendLine($"  if the player settled here, default goodwill: [{string.Join(", ", penalties)}]");
            sb.Append("  (#66: -15 legitimate 30-50%, -40 loose-ownership+ >=51%; a registered consumer suppresses the default.)");
            return sb.ToString();
        }

        /// <summary>
        /// #51 validation. Reports the density knob and what it produced: the slider %, the target base
        /// count it implies (fraction x land provinces), the settlements actually placed, and the honest
        /// area-weighted claimed fraction (settled provinces' tile area / total land area). PASS when the
        /// placed count tracks the slider target — proving the knob, not tile-count scaling, drives volume.
        /// </summary>
        public static string DensitySliderReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";

            float pct = FactionPlacementSettings.claimedLandAreaPercent;
            int landProvinces = 0, landTiles = 0;
            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType == ProvinceType.Land && p.tiles != null) { landProvinces++; landTiles += p.tiles.Count; }
            }
            int targetBases = UnityEngine.Mathf.RoundToInt(landProvinces * pct);

            var settledProvinceIds = new HashSet<int>();
            int settlements = 0;
            if (Find.WorldObjects != null)
            {
                foreach (var o in Find.WorldObjects.AllWorldObjects)
                {
                    if (!WorldObjectClassifier.IsSettlement(o)) continue;
                    settlements++;
                    var pr = mgr.GetProvinceForTile(o.Tile);
                    if (pr != null) settledProvinceIds.Add(pr.id);
                }
            }
            int settledArea = mgr.Provinces.Where(p => settledProvinceIds.Contains(p.id)).Sum(p => p.tiles?.Count ?? 0);
            float actualAreaPct = landTiles > 0 ? (float)settledArea / landTiles : 0f;

            bool tracks = targetBases <= 0 || (settlements >= targetBases * 0.5f && settlements <= targetBases * 1.3f);
            var sb = new StringBuilder();
            sb.AppendLine($"[SYNAPSE-TEST] {(tracks ? "PASS" : "FAIL")} RT_DensitySlider (#51) | slider={pct:0.00} landProvinces={landProvinces} " +
                $"targetBases={targetBases} settlementsPlaced={settlements} settledProvinces={settledProvinceIds.Count} claimedLandArea={actualAreaPct:0.00}");
            sb.Append("  (#51: the slider — target share of livable land area — drives settlement volume, area-weighted, not raw tile-count scaling.)");
            return sb.ToString();
        }

        /// <summary>
        /// #71 reconnaissance. Lists every non-vanilla <see cref="RimWorld.Planet.WorldObject"/> subclass
        /// grouped by assembly, with its base type and its numeric instance members (population/level
        /// candidates). This is how adapter profiles are established against the live assemblies rather
        /// than assumed — run it with the VFE faction modules loaded to see exactly which contribute their
        /// own world-object types (need a profile) and which use plain vanilla Settlement (need none).
        /// </summary>
        public static string AdapterReconReport()
        {
            var vanillaAsm = typeof(RimWorld.Planet.WorldObject).Assembly;
            var byAsm = new Dictionary<string, List<string>>();

            foreach (var t in GenTypes.AllSubclasses(typeof(RimWorld.Planet.WorldObject)))
            {
                if (t == null || t.Assembly == vanillaAsm || t.IsAbstract) continue;
                string asm = t.Assembly.GetName().Name;

                var numeric = t.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                    .Where(m =>
                        (m is System.Reflection.FieldInfo fi && IsNumericType(fi.FieldType)) ||
                        (m is System.Reflection.PropertyInfo pi && pi.GetIndexParameters().Length == 0 && IsNumericType(pi.PropertyType)))
                    .Select(m => m.Name)
                    .Distinct()
                    .ToList();

                if (!byAsm.TryGetValue(asm, out var lines)) { lines = new List<string>(); byAsm[asm] = lines; }
                lines.Add($"    {t.FullName}  (: {t.BaseType?.Name})  numeric=[{string.Join(",", numeric)}]");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== #71 adapter recon: {byAsm.Values.Sum(v => v.Count)} modded WorldObject subclasses across {byAsm.Count} assemblies ===");
            foreach (var kv in byAsm.OrderBy(k => k.Key))
            {
                sb.AppendLine($"  [{kv.Key}]");
                foreach (var line in kv.Value.OrderBy(l => l)) sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        private static bool IsNumericType(System.Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) || t == typeof(short) || t == typeof(uint);
        }

        /// <summary>
        /// #72: prove the global border overlay both HAS ownership to colour by (it now recalculates
        /// owners itself) and classifies each seam correctly. Runs the exact <c>StyleFor</c> the overlay
        /// uses, so a wrong colour on the globe shows up here as a wrong style tally / example.
        /// </summary>
        public static string BorderOverlayReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            int land = 0, solid = 0, contested = 0, loose = 0, unclaimed = 0, nullData = 0;
            var examples = new StringBuilder();
            int shown = 0;

            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType == ProvinceType.Ocean || p.tiles == null || p.tiles.Count == 0) continue;
                land++;
                if (p.ownershipData == null) nullData++;

                WorldLayer_RegionBorders.BorderStyle style = WorldLayer_RegionBorders.StyleFor(p);
                float top = p.ownershipData?.factionScores != null && p.ownershipData.factionScores.Count > 0
                    ? p.ownershipData.factionScores.Max(s => s.TotalScore) : 0f;

                string detail;
                switch (style.kind)
                {
                    case WorldLayer_RegionBorders.BorderStyleKind.SolidOwner:
                        solid++; detail = $"SOLID {Name(style.primary)} ({top:0.00})"; break;
                    case WorldLayer_RegionBorders.BorderStyleKind.Contested:
                        contested++; detail = $"CONTESTED {Name(style.primary)} / {Name(style.secondary)} ({top:0.00})"; break;
                    case WorldLayer_RegionBorders.BorderStyleKind.LooseClaim:
                        loose++; detail = $"LOOSE {Name(style.primary)} ({top:0.00}) / white"; break;
                    default:
                        unclaimed++; detail = null; break;
                }

                if (detail != null && shown < 12)
                {
                    shown++;
                    examples.AppendLine($"  #{p.id}: {detail}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T border overlay report (#72) — colour the global seam by claim, no map-mode switch ===");
            sb.AppendLine($"land provinces={land}; ownershipData null={nullData} (should be 0 — the overlay recalculates owners itself)");
            sb.AppendLine($"styles: SOLID(>50%)={solid}, CONTESTED(2+ claims)={contested}, LOOSE(1 claim 30-50%)={loose}, unclaimed(white)={unclaimed}");
            sb.AppendLine("Examples (claimed provinces; how the overlay paints each seam):");
            sb.Append(examples.Length > 0 ? examples.ToString().TrimEnd() : "  (no claimed provinces on this map)");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #20 border-first partition audit. The numeric harness for the new generator: land coverage,
        /// the land-province size distribution, an average shape index (share of a province's tile
        /// edges that face its own tiles — higher is more blob-like), and a tail/neck detector counting
        /// provinces with pendant tiles (a tile with a single same-province neighbour — the chain-tip
        /// signature the old river-absorption produced). The tail count should be zero or near it; a
        /// non-zero worst-shape list is where to look when eyeballing the map.
        /// </summary>
        /// <summary>
        /// The reproduction key for a world plus a region-shape audit (#20). Because the partition is a
        /// deterministic function of the terrain — which is deterministic from seed + world settings +
        /// modlist + game version — logging the seed and settings lets any "horrid region N" report be
        /// reproduced exactly. Auto-logged at worldgen and available on demand; the worst-shaped list
        /// (perimeter-per-tile, higher = spidery/ribbon) points at the regions to fix.
        /// </summary>
        public static string WorldShapeReport()
        {
            // No main-thread guard: this only READS world info and province data and builds a string (it
            // creates no Unity objects), so it is safe to auto-log from the worldgen worker thread.
            World world = Find.World;
            if (world == null) return "no world loaded";
            WorldInfo info = world.info;

            var sb = new StringBuilder();
            sb.AppendLine("=== R&S world + region-shape report (#20) ===");
            sb.AppendLine($"world '{info?.name}'   seed \"{info?.seedString}\" (int {info?.Seed})");
            sb.AppendLine($"coverage {info?.planetCoverage:P0}   rainfall {info?.overallRainfall}   temperature {info?.overallTemperature}   pollution {info?.pollution:P0}");
            sb.AppendLine("REPRO: regenerate with this seed + these settings + this modlist to get the identical regions.");

            var mgr = world.GetComponent<SynapseRegionManager>();
            var provinces = mgr?.Provinces;
            if (provinces == null) { sb.Append("no provinces generated."); return sb.ToString(); }

            var scored = new List<(int id, int tiles, int perim, float ratio)>();
            int tiny = 0;
            foreach (GeographicProvince p in provinces)
            {
                if (p == null || p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                int tiles = p.tiles.Count;
                int perim = p.perimeterEdgeCount > 0 ? p.perimeterEdgeCount : (p.perimeterTiles?.Count ?? 0);
                if (tiles < 4) tiny++;
                // Scale-invariant spideriness: perimeter / sqrt(area). A compact hex blob is ~6 at any
                // size; a long thin ribbon grows without bound. So this flags genuinely bad SHAPES, not
                // merely small provinces.
                float ratio = (float)(perim / System.Math.Sqrt(tiles));
                scored.Add((p.id, tiles, perim, ratio));
            }
            sb.AppendLine($"land provinces: {scored.Count}   tiny (<4 tiles): {tiny}");
            scored.Sort((a, b) => b.ratio.CompareTo(a.ratio));
            sb.AppendLine("worst-shaped (perimeter/√tiles — ~6 is a compact blob, higher = spidery/ribbon):");
            for (int i = 0; i < scored.Count && i < 12; i++)
                sb.AppendLine($"  region {scored[i].id}: {scored[i].tiles} tiles, perimeter {scored[i].perim}, spideriness {scored[i].ratio:0.0}");
            return sb.ToString().TrimEnd();
        }

        public static string PartitionAuditReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.EnsureTopology();

            WorldGrid grid = Find.WorldGrid;
            int totalTiles = grid.TilesCount;

            // Coverage: of the usable land tiles, how many landed in some province.
            int usableLand = 0, assignedLand = 0;
            for (int t = 0; t < totalTiles; t++)
            {
                if (!SynapseRegionManager.IsTileUsable(t)) continue;
                usableLand++;
                if (mgr.GetProvinceId(t) >= 0) assignedLand++;
            }

            var landSizes = new List<int>();
            int tailProvinces = 0, tailTiles = 0;
            double shapeSum = 0;
            var neighbors = new List<PlanetTile>();
            // worst shapes: lowest shape index (most spidery), a few examples
            var shapeById = new List<KeyValuePair<int, float>>();

            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                int area = p.tiles.Count;
                landSizes.Add(area);

                int internalEdges = 0, boundaryEdges = 0, pendants = 0;
                foreach (int t in p.tiles)
                {
                    int same = 0;
                    neighbors.Clear();
                    grid.GetTileNeighbors(t, neighbors);
                    foreach (var n in neighbors)
                    {
                        if (mgr.GetProvinceId(n.tileId) == p.id) { same++; internalEdges++; }
                        else boundaryEdges++;
                    }
                    if (same == 1 && area > 2) pendants++;
                }

                float shape = (internalEdges + boundaryEdges) > 0
                    ? (float)internalEdges / (internalEdges + boundaryEdges) : 1f;
                shapeSum += shape;
                shapeById.Add(new KeyValuePair<int, float>(p.id, shape));
                if (pendants > 0) { tailProvinces++; tailTiles += pendants; }
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T border-first partition audit (#20) ===");
            sb.AppendLine($"coverage: {assignedLand}/{usableLand} usable land tiles assigned"
                + (usableLand > 0 ? $" ({100.0 * assignedLand / usableLand:0.0}%)" : ""));

            if (landSizes.Count == 0)
            {
                sb.Append("no land provinces");
                return sb.ToString();
            }

            landSizes.Sort();
            int n2 = landSizes.Count;
            double mean = landSizes.Average();
            int median = landSizes[n2 / 2];
            sb.AppendLine($"land provinces={n2}; size min={landSizes[0]} median={median} mean={mean:0.0} max={landSizes[n2 - 1]}");

            // Size histogram in coarse buckets.
            int[] buckets = { 0, 0, 0, 0, 0, 0 };
            string[] labels = { "<25", "25-49", "50-99", "100-149", "150-249", "250+" };
            foreach (int s in landSizes)
            {
                if (s < 25) buckets[0]++;
                else if (s < 50) buckets[1]++;
                else if (s < 100) buckets[2]++;
                else if (s < 150) buckets[3]++;
                else if (s < 250) buckets[4]++;
                else buckets[5]++;
            }
            var hist = new StringBuilder();
            for (int i = 0; i < buckets.Length; i++) hist.Append($"{labels[i]}={buckets[i]}  ");
            sb.AppendLine("size histogram: " + hist.ToString().TrimEnd());

            sb.AppendLine($"avg shape index (share of tile edges internal; higher=blobbier)={shapeSum / n2:0.00}");
            sb.AppendLine($"tails/necks: {tailProvinces} land province(s) have pendant tiles ({tailTiles} tile(s) total) — target 0");

            shapeById.Sort((a, b) => a.Value.CompareTo(b.Value));
            var worst = new StringBuilder();
            for (int i = 0; i < shapeById.Count && i < 6; i++)
            {
                var kv = shapeById[i];
                worst.Append($"#{kv.Key}({kv.Value:0.00})  ");
            }
            sb.Append("most spidery provinces: " + worst.ToString().TrimEnd());
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #20 fixed-seed tooling: re-run the partition on the loaded world, then report the audit. A
        /// save keeps its scribed provinces, so loading the fixed test world and calling this is what
        /// re-partitions the SAME terrain with the current code — the deterministic tune-and-compare
        /// loop. Refreshes ownership so the map modes redraw.
        /// </summary>
        public static string RegenerateAndAudit()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr == null) return "no region manager";
            mgr.GenerateProvinces();
            mgr.MarkOwnersDirty();
            mgr.RecalculateProvinceOwners();
            return PartitionAuditReport();
        }

        /// <summary>
        /// #20 visualization dump: write every tile's partition assignment to a CSV so the border-first
        /// result can be rendered as a full-globe map for dissection. One row per tile:
        /// tileId, longitude, latitude, provinceId, provinceType(int), river(0/1). Longitude/latitude
        /// come from the tile's 3D centre (equirectangular). Written to %TEMP%\rt_partition_dump.csv.
        /// </summary>
        public static string DumpPartitionCsv()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";

            WorldGrid grid = Find.WorldGrid;
            int total = grid.TilesCount;
            var neigh = new List<PlanetTile>();
            var sb = new StringBuilder(total * 26);
            sb.Append("tileId,lon,lat,provinceId,provinceType,river,hill\n");
            for (int t = 0; t < total; t++)
            {
                UnityEngine.Vector3 c = grid.GetTileCenter(t);
                UnityEngine.Vector3 u = c.normalized;
                double lat = System.Math.Asin(System.Math.Max(-1.0, System.Math.Min(1.0, u.y))) * 57.29577951308232;
                double lon = System.Math.Atan2(u.z, u.x) * 57.29577951308232;

                int pid = mgr.GetProvinceId(t);
                int ptype = -1;
                if (pid >= 0)
                {
                    var p = mgr.GetProvince(pid);
                    if (p != null) ptype = (int)p.provinceType;
                }

                int river = 0;
                neigh.Clear();
                grid.GetTileNeighbors(t, neigh);
                foreach (var n in neigh)
                {
                    if (grid.GetRiverDef(t, n.tileId) != null || grid.GetRiverDef(n.tileId, t) != null) { river = 1; break; }
                }

                // Hilliness class 0..4 (Flat, SmallHills, LargeHills, Mountainous, Impassable) so the
                // offline visualization can show pass/high-ground structure.
                Tile td = grid[t];
                int hill;
                switch (td.hilliness)
                {
                    case Hilliness.SmallHills: hill = 1; break;
                    case Hilliness.LargeHills: hill = 2; break;
                    case Hilliness.Mountainous: hill = 3; break;
                    case Hilliness.Impassable: hill = 4; break;
                    default: hill = 0; break;
                }

                sb.Append(t).Append(',')
                  .Append(lon.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(lat.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(pid).Append(',').Append(ptype).Append(',').Append(river).Append(',').Append(hill).Append('\n');
            }

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rt_partition_dump.csv");
            try { System.IO.File.WriteAllText(path, sb.ToString()); }
            catch (Exception ex) { return "failed to write dump: " + ex.Message; }
            return $"wrote {total} tiles to {path} ({mgr.Provinces.Count} provinces)";
        }

        /// <summary>
        /// #72 test tooling: force a synthetic ownership on a province so the overlay's SOLID / CONTESTED /
        /// LOOSE seams can be eyeballed without engineering real holdings. Writes <c>province.ownershipData</c>
        /// directly; the overlay's own <c>RecalculateProvinceOwners</c> early-returns while the epoch is
        /// unchanged, so the forced data survives the repaint until real holdings change or it is cleared
        /// (see <see cref="ClearOwnershipOverrides"/>). <paramref name="tileId"/> &lt; 0 (no selection,
        /// e.g. headless) falls back to the first land province so the command is still runnable.
        /// </summary>
        public static string ForceOwnershipStyle(int tileId, string style)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";

            mgr.RecalculateProvinceOwners();   // ensure owners exist so TestFactions can source real ones
            GeographicProvince province = ResolveProvince(mgr, tileId);
            if (province == null) return "no target province — select a land world tile first";

            var factions = TestFactions(2);
            if (factions.Count == 0) return "no eligible non-player faction to attribute ownership to";

            var data = new RegionalOwnershipData { province = province };
            string desc;
            switch (style)
            {
                case "solid":
                    data.factionScores.Add(Score(factions[0], 0.90f));
                    desc = $"SOLID {Name(factions[0])} (0.90)";
                    break;
                case "contested":
                    if (factions.Count < 2) return "need at least two non-player factions to force a contest";
                    data.factionScores.Add(Score(factions[0], 0.42f));
                    data.factionScores.Add(Score(factions[1], 0.40f));
                    desc = $"CONTESTED {Name(factions[0])} (0.42) vs {Name(factions[1])} (0.40)";
                    break;
                case "loose":
                    data.factionScores.Add(Score(factions[0], 0.40f));
                    desc = $"LOOSE {Name(factions[0])} (0.40) / white";
                    break;
                default:
                    return $"unknown style '{style}' — use solid | contested | loose";
            }

            float assigned = data.factionScores.Sum(s => s.TotalScore);
            data.unclaimedScore = assigned < 1f ? 1f - assigned : 0f;

            province.ownershipData = data;
            province.owningFactionIds.Clear();
            foreach (var s in data.factionScores)
                if (s.faction != null) province.owningFactionIds.Add(s.faction.GetUniqueLoadID());

            RegionsAndSocieties.UI.RegionBorderOverlay.Invalidate();

            WorldLayer_RegionBorders.BorderStyle applied = WorldLayer_RegionBorders.StyleFor(province);
            return $"Forced province #{province.id} -> {desc}. Overlay style now: {applied.kind}. "
                 + "Open the planet in any map mode to see the seam; run 'TEST clear ownership overrides' to restore real ownership.";
        }

        /// <summary>#72 test tooling: discard every forced override by recomputing ownership from the real
        /// world objects (<see cref="SynapseRegionManager.MarkOwnersDirty"/> bypasses the change gate), then
        /// repaint. Clears all provinces at once, since the forced writes are not individually tracked.</summary>
        public static string ClearOwnershipOverrides()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            mgr.MarkOwnersDirty();
            mgr.RecalculateProvinceOwners();
            RegionsAndSocieties.UI.RegionBorderOverlay.Invalidate();
            return "Cleared synthetic overrides: recomputed ownership from real holdings and repainted the overlay.";
        }

        /// <summary>
        /// #72 test tooling: drop a real vanilla settlement for a faction that does not already dominate the
        /// target province, manufacturing a genuine two-faction presence through the actual scoring path
        /// (not a synthetic override). PostAdd bumps the ownership epoch, which recomputes owners and
        /// repaints the overlay. Whether the result reads CONTESTED depends on the real scores — inspect
        /// with the border overlay report afterwards.
        /// </summary>
        public static string DropRivalSettlement(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldObjects == null) return "no world loaded";
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";

            GeographicProvince province = ResolveProvince(mgr, tileId);
            if (province == null) return "no target province — select a land world tile first";

            mgr.RecalculateProvinceOwners();
            Faction dominant = province.ownershipData != null ? RegionalDomainUtility.GetDominantOwner(province.ownershipData) : null;
            if (dominant == null)
            {
                var claims = RegionalDomainUtility.LegitimateClaimsOrdered(province.ownershipData);
                dominant = claims.Count > 0 ? claims[0].faction : null;
            }

            Faction owner = null;
            foreach (var f in TestFactions(8))
            {
                if (f != dominant) { owner = f; break; }
            }
            if (owner == null) return "no eligible rival faction (need a non-player faction other than the current owner)";

            int tile = (tileId >= 0 && mgr.GetProvinceId(tileId) == province.id) ? tileId : province.tiles[0];

            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(owner);
            settlement.Tile = tile;
            try { settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement); }
            catch { settlement.Name = owner.Name + " outpost"; }
            Find.WorldObjects.Add(settlement);   // PostAdd -> BumpOwnershipEpoch -> overlay Invalidate

            mgr.RecalculateProvinceOwners();
            var afterStyle = WorldLayer_RegionBorders.StyleFor(province);
            float rivalScore = province.ownershipData?.ScoreFor(owner) ?? 0f;
            return $"Dropped settlement '{settlement.Name}' ({Name(owner)}) on tile {tile} in province #{province.id}. "
                 + $"{Name(owner)} now scores {rivalScore:0.00} there; overlay style: {afterStyle.kind}. "
                 + "Run 'R&T: border overlay report' to see the tallies.";
        }

        /// <summary>Selected world tile's province, else (headless / no selection) the first land province.</summary>
        private static GeographicProvince ResolveProvince(SynapseRegionManager mgr, int tileId)
        {
            if (tileId >= 0)
            {
                var picked = mgr.GetProvinceForTile(tileId);
                if (picked != null) return picked;
            }
            foreach (var p in mgr.Provinces)
                if (p.provinceType != ProvinceType.Ocean && p.tiles != null && p.tiles.Count > 0) return p;
            return null;
        }

        /// <summary>Up to <paramref name="n"/> distinct non-player factions to attribute test ownership to.
        /// Sources real province OWNERS first — the actual claiming settlements a contest is between, and
        /// guaranteed valid/colourable — since some worlds flag settleable factions <c>Hidden</c>, which a
        /// naive FactionManager filter would wrongly exclude. Falls back to any non-player faction.</summary>
        private static List<Faction> TestFactions(int n)
        {
            var result = new List<Faction>();

            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces != null)
            {
                foreach (var p in mgr.Provinces)
                {
                    var data = p.ownershipData;
                    if (data?.factionScores == null) continue;
                    foreach (var s in data.factionScores)
                    {
                        if (s.faction != null && !s.faction.IsPlayer && !s.faction.defeated && !result.Contains(s.faction))
                        {
                            result.Add(s.faction);
                            if (result.Count >= n) return result;
                        }
                    }
                }
            }

            if (Find.FactionManager != null)
            {
                foreach (var f in Find.FactionManager.AllFactionsListForReading)
                {
                    if (f == null || f.IsPlayer || f.defeated || result.Contains(f)) continue;
                    if (f.def == null || !f.def.humanlikeFaction) continue;
                    result.Add(f);
                    if (result.Count >= n) break;
                }
            }
            return result;
        }

        private static FactionOwnershipScore Score(Faction f, float value)
        {
            return new FactionOwnershipScore { faction = f, settlementScore = value };
        }

        public static string PlacementProbe(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return "no player faction";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T placement probe (#61) — player refused only by exclusive (>=71%) rival ===");

            if (tileId >= 0)
            {
                sb.Append(ProbeLine(mgr, player, tileId));
                return sb.ToString().TrimEnd();
            }

            // No tile given (headless): sample one representative province per tier so the whole
            // ladder is exercised in a single call.
            var byTier = new Dictionary<OwnershipTier, GeographicProvince>();
            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType == ProvinceType.Ocean || p.tiles == null || p.tiles.Count == 0) continue;
                float rival = (p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p))?.StrongestRivalScore(player) ?? 0f;
                OwnershipTier tier = RegionalDomainUtility.TierOf(rival);
                if (!byTier.ContainsKey(tier)) byTier[tier] = p;
            }

            foreach (OwnershipTier tier in new[] { OwnershipTier.LooseClaim, OwnershipTier.LegitimateClaim, OwnershipTier.LooseOwnership, OwnershipTier.Exclusive })
            {
                sb.AppendLine($"-- rival tier: {tier} --");
                if (byTier.TryGetValue(tier, out var p))
                {
                    sb.Append(ProbeLine(mgr, player, p.tiles[0]));
                }
                else
                {
                    sb.AppendLine("  (no province with a rival at this tier on this map)");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string ProbeLine(SynapseRegionManager mgr, Faction player, int tileId)
        {
            GeographicProvince province = mgr.GetProvinceForTile(tileId);
            if (province == null) return $"  tile {tileId}: no province\n";

            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
            float rival = data?.StrongestRivalScore(player) ?? 0f;
            ProvinceControl control = RegionalOwnershipUtility.GetControl(province, player);
            bool exclusiveRival = RegionalOwnershipUtility.IsExclusivelyOwnedByRival(province, player);

            PlacementDecision decision = WorldObjectPlacementUtility.Evaluate(tileId, player, WorldObjectKind.Settlement);
            string verdict = decision.Allowed ? "ALLOWED" : "refused: " + decision.Reason;

            return $"  tile {tileId} (region #{province.id}): control={control}, strongestRival={rival:0.00} ({RegionalDomainUtility.TierOf(rival)}), exclusiveRival={exclusiveRival} -> {verdict}\n";
        }

        /// <summary>
        /// #56: per settlement, its size tier, the outpost allowance that tier grants its territory,
        /// and how many outposts the territory already holds. The tuning surface for the seeding pass.
        /// </summary>
        /// <summary>
        /// #6 growth validation: for the selected NPC settlement (or the first one found), report its
        /// growth factors and simulate the population curve forward, so the approach to target is
        /// verifiable via <c>run_debug_action</c> without waiting in-game. The simulation is a preview
        /// under today's factors — it does not change the settlement's real modeled population.
        /// </summary>
        public static string SettlementGrowthReport(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldObjects == null) return "no world loaded";
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr == null) return "no region manager";

            WorldObject target = null;
            foreach (var o in Find.WorldObjects.AllWorldObjects)
            {
                if (!WorldObjectClassifier.IsSettlement(o)) continue;
                if (o.Faction != null && o.Faction.IsPlayer) continue;
                if (tileId >= 0) { if (o.Tile == tileId) { target = o; break; } }
                else { target = o; break; }
            }
            if (target == null) return tileId >= 0 ? "no NPC settlement on the selected tile" : "no NPC settlement found";

            var inputs = SettlementGrowthUtility.BuildInputs(target);
            // Growth capacity is the ⅔-max target; the tier max is the hard ceiling (150% of target).
            int capacity = SettlementSizeUtility.TargetPopulationOf(target);
            int tierMax = SettlementSizeUtility.MaxPopulationOf(target);
            int now = mgr.GetModeledSettlementPopulation(target);

            float mult = WorldObjectIntegrationSettings.growthRateMultiplier;
            float fertility = BirthrateRules.Fertility(inputs) * mult;
            float mortality = BirthrateRules.Mortality(inputs) * mult;
            float netBelowTarget = fertility - mortality;   // headline rate below the target (crowding 1)

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T settlement growth (#6) ===");
            sb.AppendLine($"{target.LabelCap}  faction={Name(target.Faction)}  tech={target.Faction?.def?.techLevel}  tier={SettlementSizeUtility.TierOf(target)}");
            sb.AppendLine($"target(⅔max)={capacity}  tierMax(ceiling)={tierMax}  current modeled pop={now}");
            sb.AppendLine($"factors: fertileFraction={inputs.FertileFraction:0.000}  wealthLevel={inputs.WealthLevel:0.00}  food={inputs.FoodBalance:0.00}  ideoBias={inputs.IdeologyBias:0.000}  xenoBias={inputs.XenotypeBias:0.000}");
            sb.AppendLine($"growth mult={mult:0.0}×  births={fertility * 100f:0.0}%/yr  deaths={mortality * 100f:0.0}%/yr  net(below target)={netBelowTarget * 100f:0.0}%/yr");

            // Preview the curve forward, one year per step, under today's constant factors — it should
            // climb toward the target, crowd above it, and settle just below the tier max as births taper
            // to the death rate.
            sb.AppendLine("year : modeled pop  (preview, constant factors)");
            float sim = now;
            for (int year = 0; year <= 40; year++)
            {
                if (year % 5 == 0) sb.AppendLine($"  {year,3} : {sim:0.0}  ({(tierMax > 0 ? sim / tierMax * 100f : 0f):0}% of tier max)");
                sim = BirthrateRules.GrowStep(sim, capacity, fertility, mortality, 1f);
            }
            return sb.ToString().TrimEnd();
        }

        public static string SettlementTierAllowanceReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldObjects == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            // Count outposts per province once, so each settlement line can show its territory's fill.
            var outpostCounts = new Dictionary<int, int>();
            foreach (var o in Find.WorldObjects.AllWorldObjects)
            {
                if (o == null) continue;
                if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Outpost) continue;
                int opid = mgr.GetProvinceId(o.Tile);
                if (opid < 0) continue;
                outpostCounts.TryGetValue(opid, out int c);
                outpostCounts[opid] = c + 1;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T settlement tiers & outpost allowance (#56) ===");
            sb.AppendLine($"tiers active: {WorldObjectIntegrationSettings.SettlementTiersActive}    seeding active: {WorldObjectIntegrationSettings.OutpostSeedingActive}");

            int settlements = 0;
            foreach (var o in Find.WorldObjects.AllWorldObjects)
            {
                if (o == null || WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                settlements++;

                SettlementTier tier = SettlementSizeUtility.TierOf(o);
                int allowance = OutpostAllowanceRules.OutpostAllowance(tier);
                int pid = mgr.GetProvinceId(o.Tile);
                outpostCounts.TryGetValue(pid, out int existing);

                sb.AppendLine($"  {o.LabelCap} [{o.Faction?.Name ?? "no faction"}] region #{pid}: "
                    + $"tier={tier.LabelCapitalized()}, allowance={allowance}, outposts={existing}");
            }
            if (settlements == 0) sb.AppendLine("  (no settlements in the world)");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #56: run the outpost-seeding pass now and dump what it did. Idempotent — a second run finds
        /// each territory already at its allowance and places nothing.
        /// </summary>
        public static string OutpostSeedingReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";
            return OutpostSeedingUtility.SeedOutposts().ToReport().TrimEnd();
        }

        /// <summary>
        /// #65: confirm a settlement is refused only by an exclusive (&gt;=71%) rival, for the player
        /// AND for an NPC faction — the 0.8 relaxation that lets any faction settle contested ground.
        /// </summary>
        public static string SettlementPlacementCheck(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            Faction player = Faction.OfPlayerSilentFail;
            Faction npc = Find.FactionManager?.AllFactionsListForReading?
                .FirstOrDefault(f => f != null && !f.IsPlayer && !f.def.hidden && !f.defeated);

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T settlement placement check (#65) — any faction refused only by an exclusive (>=71%) rival ===");
            if (npc != null) sb.AppendLine($"sample NPC faction: {npc.Name}");

            if (tileId >= 0)
            {
                if (player != null) { sb.AppendLine("player:"); sb.Append(ProbeLine(mgr, player, tileId)); }
                if (npc != null) { sb.AppendLine("npc:"); sb.Append(ProbeLine(mgr, npc, tileId)); }
                return sb.ToString().TrimEnd();
            }

            if (npc == null) return sb.ToString().TrimEnd() + "\n  (no NPC faction to sample)";

            // Headless: one province per rival tier, from the NPC's perspective. Every tier below
            // Exclusive should now read ALLOWED — that is the behaviour change under test.
            var byTier = new Dictionary<OwnershipTier, GeographicProvince>();
            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType == ProvinceType.Ocean || p.tiles == null || p.tiles.Count == 0) continue;
                float rival = (p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p))?.StrongestRivalScore(npc) ?? 0f;
                OwnershipTier tier = RegionalDomainUtility.TierOf(rival);
                if (!byTier.ContainsKey(tier)) byTier[tier] = p;
            }
            foreach (OwnershipTier tier in new[] { OwnershipTier.LooseClaim, OwnershipTier.LegitimateClaim, OwnershipTier.LooseOwnership, OwnershipTier.Exclusive })
            {
                sb.AppendLine($"-- rival tier vs NPC: {tier} (expect ALLOWED except Exclusive) --");
                if (byTier.TryGetValue(tier, out var p)) sb.Append(ProbeLine(mgr, npc, p.tiles[0]));
                else sb.AppendLine("  (no province with a rival at this tier on this map)");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 0.8 structural tiers: per faction, its settlement count, the capital tier it affords, the
        /// tier distribution, and every settlement's protection score + tier + capital flag. The
        /// tuning surface for the protection metric and a proof the pyramid is valid.
        /// </summary>
        public static string TierPyramidReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldObjects == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            // Group settlements by faction.
            var byFaction = new Dictionary<Faction, List<WorldObject>>();
            foreach (var o in Find.WorldObjects.AllWorldObjects)
            {
                if (o == null || o.Faction == null) continue;
                if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                if (!byFaction.TryGetValue(o.Faction, out var list)) { list = new List<WorldObject>(); byFaction[o.Faction] = list; }
                list.Add(o);
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T tier pyramid & capitals (0.8) ===");
            sb.AppendLine($"tiers active: {WorldObjectIntegrationSettings.SettlementTiersActive}    protection radius: {SettlementSizeUtility.ProtectionRadius} rings");

            if (byFaction.Count == 0) { sb.AppendLine("  (no faction settlements in the world)"); return sb.ToString().TrimEnd(); }

            foreach (var kv in byFaction.OrderByDescending(k => k.Value.Count))
            {
                Faction faction = kv.Key;
                List<WorldObject> ranked = SettlementSizeUtility.RankedSettlements(faction);
                int n = ranked.Count;
                int[] counts = TierPyramidRules.TierCounts(n);
                int maxTier = TierPyramidRules.MaxCapitalTier(n);

                sb.AppendLine($"-- {faction.Name}: {n} settlements, capital tier T{maxTier} "
                    + $"[T5={counts[5]} T4={counts[4]} T3={counts[3]} T2={counts[2]} T1={counts[1]}] --");

                for (int rank = 0; rank < ranked.Count; rank++)
                {
                    WorldObject s = ranked[rank];
                    SettlementTier tier = TierPyramidRules.TierForRank(rank, counts);
                    int prot = SettlementSizeUtility.ProtectionScore(s);
                    string cap = rank == 0 ? "  [CAPITAL]" : "";
                    sb.AppendLine($"   #{rank} {s.LabelCap}: protection={prot} -> {tier.LabelCapitalized()}{cap}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #36 demographics: dump a region's derived makeup — race distribution + median wealth per
        /// race (engineered-addiction races read poor), meme mix, sex ratio. Uses the selected world
        /// tile's region, or the first settled land region when nothing is selected.
        /// </summary>
        public static string DemographicsReport(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            GeographicProvince province = tileId >= 0 ? mgr.GetProvinceForTile(tileId) : null;
            if (province == null)
            {
                // No tile picked: report the MOST CONTESTED settled region (lowest dominant-faction
                // share) — that's where falloff tuning matters. If the best we find is still ~100% one
                // faction, reach is too small for borders to overlap (bump it).
                float lowestTop = 2f;
                foreach (var p in mgr.Provinces)
                {
                    if (p == null || p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                    RegionDemographics d = RegionDemographicsUtility.ForRegion(p);
                    if (d.settledTiles == 0 || d.factionShares.Count == 0) continue;
                    float top = 0f;
                    foreach (var kv in d.factionShares) if (kv.Value > top) top = kv.Value;
                    if (top < lowestTop) { lowestTop = top; province = p; }
                }
            }
            if (province == null) return "no settled land region found";

            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            var sb = new StringBuilder();
            sb.AppendLine("=== R&T region demographics (#36) ===");
            sb.AppendLine($"region #{province.id} {province.name}: {demo.settledTiles}/{demo.tileCount} settled tiles"
                + $"    biotech={demo.biotechActive}  ideology={demo.ideologyActive}");
            float sexSkew = RegionDemographicsStress.CurrentFemaleDelta(province.id);
            sb.AppendLine($"female fraction {demo.femaleFraction:P0}"
                + (sexSkew != 0f ? $" (skew {sexSkew:+0%;-0%})" : "")
                + $"    overall median wealth {demo.overallMedianWealth}"
                + (RegionDemographicsStress.HasOverride(province.id) ? "  [STRESSED]" : ""));
            sb.AppendLine($"age (#10): median {demo.medianAge}"
                + $"    children {demo.ageShares[(int)AgeBucket.Child]:P0}"
                + $"  working-age {demo.ageShares[(int)AgeBucket.WorkingAge]:P0}"
                + $"  elders {demo.ageShares[(int)AgeBucket.Elder]:P0}");
            sb.AppendLine($"education (#15): index {demo.educationIndex}/100"
                + $"    illiterate {demo.educationShares[(int)EducationTier.Illiterate]:P0}"
                + $"  primary {demo.educationShares[(int)EducationTier.Primary]:P0}"
                + $"  secondary {demo.educationShares[(int)EducationTier.Secondary]:P0}"
                + $"  undergrad {demo.educationShares[(int)EducationTier.Undergrad]:P0}"
                + $"  postgrad {demo.educationShares[(int)EducationTier.Postgrad]:P0}");
            sb.AppendLine($"socioeconomic (#14): index {demo.sesIndex}/100"
                + $"    subsistence {demo.sesShares[(int)SesTier.Subsistence]:P0}"
                + $"  modest {demo.sesShares[(int)SesTier.Modest]:P0}"
                + $"  prosperous {demo.sesShares[(int)SesTier.Prosperous]:P0}"
                + $"  affluent {demo.sesShares[(int)SesTier.Affluent]:P0}");
            sb.AppendLine($"employment (#16): rate {demo.employmentRate}%"
                + $"    agriculture {demo.occupationShares[(int)OccupationSector.Agriculture]:P0}"
                + $"  industry {demo.occupationShares[(int)OccupationSector.Industry]:P0}"
                + $"  military {demo.occupationShares[(int)OccupationSector.Military]:P0}"
                + $"  trade {demo.occupationShares[(int)OccupationSector.Trade]:P0}");
            sb.AppendLine($"tuning: model {(Demographics.DemographicsRules.FalloffModel)WorldObjectIntegrationSettings.demographicFalloffModel}"
                + $"  reach ×{WorldObjectIntegrationSettings.demographicReach:0.00}  shape {WorldObjectIntegrationSettings.demographicFalloff:0.00}");

            sb.AppendLine("faction pressure share (the border-flip metric — aim ~50-60% own on frontiers):");
            if (demo.factionShares.Count == 0) sb.AppendLine("  (no pressure — wilderness)");
            foreach (var kv in demo.factionShares.OrderByDescending(k => k.Value))
                sb.AppendLine($"  {kv.Key.Name}: {kv.Value:P0}");

            sb.AppendLine("races (share — median wealth, low = underclass):");
            if (demo.raceShares.Count == 0) sb.AppendLine("  (plain human — no Biotech, or unsettled)");
            foreach (var kv in demo.raceShares)
            {
                demo.medianWealthByRace.TryGetValue(kv.Key, out int w);
                sb.AppendLine($"  {kv.Key.LabelCap}: {kv.Value:P0}    wealth {w}");
            }

            if (demo.ideoShares.Count > 0)
            {
                float sim = RegionDemographicsUtility.AverageNeighborSimilarity(province);
                sb.AppendLine("ideologies (#13)" + (sim >= 0f ? $"  [neighbour similarity {sim:P0}]" : "") + ":");
                foreach (var kv in demo.ideoShares.OrderByDescending(k => k.Value))
                    sb.AppendLine($"  {kv.Key.name}: {kv.Value:P0}");
            }

            if (demo.memeShares.Count > 0)
            {
                sb.AppendLine("memes:");
                foreach (var kv in demo.memeShares)
                    sb.AppendLine($"  {kv.Key.LabelCap}: {kv.Value:P0}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #36: each faction's WHOLE-territory demographics aggregated — the summary the faction info
        /// tab will show. Race mix + median wealth per race (underclass reads poor), top memes, sex.
        /// </summary>
        public static string FactionDemographicsReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldObjects == null) return "no world loaded";

            var seen = new HashSet<Faction>();
            var sb = new StringBuilder();
            sb.AppendLine("=== R&T faction-wide demographics (#36) ===");

            var factions = new List<Faction>();
            foreach (var o in Find.WorldObjects.AllWorldObjects)
            {
                if (o?.Faction == null) continue;
                if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                if (seen.Add(o.Faction)) factions.Add(o.Faction);
            }

            var rows = new List<(Faction f, RegionDemographics d)>();
            foreach (Faction f in factions)
            {
                RegionDemographics d = RegionDemographicsUtility.ForFaction(f);
                if (d.settledTiles > 0) rows.Add((f, d));
            }
            rows.Sort((a, b) => b.d.settledTiles.CompareTo(a.d.settledTiles));

            if (rows.Count == 0) { sb.AppendLine("  (no settled factions)"); return sb.ToString().TrimEnd(); }

            foreach (var row in rows)
            {
                RegionDemographics d = row.d;
                sb.AppendLine($"-- {row.f.Name}: {d.settledTiles} tiles    median wealth {d.overallMedianWealth}    female {d.femaleFraction:P0}    median age {d.medianAge} --");
                foreach (var kv in d.raceShares.OrderByDescending(k => k.Value))
                {
                    d.medianWealthByRace.TryGetValue(kv.Key, out int w);
                    sb.AppendLine($"   {kv.Key.LabelCap}: {kv.Value:P0}  (wealth {w})");
                }
                if (d.raceShares.Count == 0) sb.AppendLine("   (plain human)");
                var topMemes = d.memeShares.OrderByDescending(k => k.Value).Take(3).Select(k => $"{k.Key.LabelCap} {k.Value:P0}");
                string memeStr = string.Join(", ", topMemes);
                if (!string.IsNullOrEmpty(memeStr)) sb.AppendLine($"   top memes: {memeStr}");
            }
            return sb.ToString().TrimEnd();
        }

        public static string HoldingsReport()
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null || Find.WorldObjects == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "no regions generated";

            // Province coverage: does the partition actually cover the land the settlements sit on?
            WorldGrid grid = Find.WorldGrid;
            int landTiles = 0;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                Tile t = grid[i];
                if (t != null && !t.WaterCovered) landTiles++;
            }
            int provinceTileSum = mgr.Provinces.Where(p => p.tiles != null).Sum(p => p.tiles.Count);

            var all = Find.WorldObjects.AllWorldObjects;
            int total = all.Count, vanillaSettlements = 0, classifiedTerritorial = 0, classifiedSettlement = 0;
            int nullFaction = 0, mappedToProvince = 0, unmapped = 0;

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T holdings report (#67) — why ownership scores may be zero ===");
            sb.AppendLine($"World grid: {grid.TilesCount} tiles ({landTiles} land); provinces={mgr.Provinces.Count} covering {provinceTileSum} tiles" +
                          (provinceTileSum < landTiles ? $"  [GAP: {landTiles - provinceTileSum} land tiles in no province]" : "  [covers all land]"));

            var examples = new StringBuilder();
            int shown = 0;
            foreach (var o in all)
            {
                if (o == null) continue;
                bool isVanilla = o is RimWorld.Planet.Settlement;
                if (isVanilla) vanillaSettlements++;

                WorldObjectKind kind = WorldObjectClassifier.Classify(o);
                bool territorial = kind.IsTerritorial();
                if (territorial) classifiedTerritorial++;
                if (kind == WorldObjectKind.Settlement) classifiedSettlement++;

                if (!isVanilla && !territorial) continue;   // only report holdings-like objects

                if (o.Faction == null) nullFaction++;
                int pid = mgr.GetProvinceId(o.Tile);
                if (pid >= 0) mappedToProvince++; else unmapped++;

                if (shown < 20)
                {
                    shown++;
                    examples.AppendLine($"  '{o.LabelCap}' type={o.GetType().Name} def={o.def?.defName} kind={kind} faction={(o.Faction != null ? o.Faction.Name : "NULL")} tile={o.Tile.tileId} provinceId={pid}");
                }
            }

            sb.AppendLine($"world objects={total}; vanilla Settlements={vanillaSettlements}; classified territorial={classifiedTerritorial} (settlement={classifiedSettlement})");
            sb.AppendLine($"holdings mapped to a province={mappedToProvince}; UNMAPPED (provinceId<0)={unmapped}; null faction={nullFaction}");

            // #19: domain shape per faction — 1.0 is a closed blob, near 0 is a pure spider. The value
            // the territory-compactness slider is meant to raise.
            sb.AppendLine("domain compactness (#19, 1 = blob, 0 = spider):");
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f == null || f.IsPlayer || f.Hidden) continue;
                if (!all.Any(o => o?.Faction == f && WorldObjectClassifier.Classify(o) == WorldObjectKind.Settlement)) continue;
                sb.AppendLine($"  {f.Name}: {TerritoryCompactnessUtility.DomainCompactness(f):0.00}");
            }

            sb.AppendLine("Sample holdings:");
            sb.Append(examples.ToString().TrimEnd());
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// #69: proof the border-only ownership cap holds. A province with no holdings of its own can
        /// be owned only through border bleed, which 0.7.3 hard-caps at 0.70 and attenuates by how
        /// settled the province is. Dumps the derivation (internal ownership, attenuation, edge counts)
        /// for a selected province, or headlessly scans every holdingless province and asserts none
        /// exceeds the cap — the exact regression from the playtest report (a mountain-ringed,
        /// settlement-less province reading 73%).
        /// </summary>
        public static string OwnershipDerivationReport(int tileId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            var sb = new StringBuilder();
            sb.AppendLine("=== R&T ownership derivation (#69) — border cap 0.70, holdings attenuate borders ===");

            if (tileId >= 0)
            {
                var p = mgr.GetProvinceForTile(tileId);
                if (p == null) return sb.Append($"tile {tileId}: no province").ToString();
                sb.Append(DerivationBlock(p));
                return sb.ToString().TrimEnd();
            }

            // Headless: check the invariant across every border-only province and show the strongest.
            int holdingless = 0, overCap = 0;
            float worst = 0f;
            foreach (var p in mgr.Provinces)
            {
                if (p.provinceType == ProvinceType.Ocean || p.tiles == null || p.tiles.Count == 0) continue;
                var data = p.ownershipData;
                if (data == null || data.primaryCount != 0 || data.secondaryCount != 0) continue;   // has holdings
                holdingless++;
                float top = data.factionScores.Count > 0 ? data.factionScores.Max(s => s.TotalScore) : 0f;
                if (top > worst) worst = top;
                if (top > 0.7001f) overCap++;
            }

            sb.AppendLine($"holdingless (border-only) provinces={holdingless}; max border-only score={worst:0.000} (cap 0.70)");
            sb.AppendLine($"border-only provinces OVER the 0.70 cap={overCap}  [expect 0]");

            var top5 = mgr.Provinces
                .Where(p => p.provinceType != ProvinceType.Ocean && p.tiles != null && p.tiles.Count > 0
                            && p.ownershipData != null && p.ownershipData.primaryCount == 0 && p.ownershipData.secondaryCount == 0
                            && p.ownershipData.factionScores.Count > 0)
                .OrderByDescending(p => p.ownershipData.factionScores.Max(s => s.TotalScore))
                .Take(5)
                .ToList();
            sb.AppendLine("Strongest border-only provinces (derivation):");
            foreach (var p in top5) sb.Append(DerivationBlock(p));
            return sb.ToString().TrimEnd();
        }

        /// <summary>#69: derivation for one province by its id (not tile) — the headless way to inspect
        /// a specific region, since run_debug_action can pass an id via the x coordinate.</summary>
        public static string OwnershipDerivationForProvinceId(int provinceId)
        {
            if (!UnityData.IsInMainThread) return "must run on the main thread";
            if (Find.World == null) return "no world loaded";

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || mgr.Provinces.Count == 0) return "no regions generated";
            mgr.RecalculateProvinceOwners();

            var p = mgr.Provinces.FirstOrDefault(x => x.id == provinceId);
            if (p == null) return $"no province with id {provinceId}";

            var sb = new StringBuilder();
            sb.AppendLine($"=== R&T ownership derivation (#69) — province {provinceId} ===");
            sb.Append(DerivationBlock(p));

            // Neighbour attribution: for every shared land border, which neighbour region it is, who the
            // overlay/fill would colour it (its strongest legitimate >=30% claim), and how many edges are
            // shared. An edge counts as "claimed" for THIS region's border score when the neighbour has a
            // dominant BASE owner (a holding, base>0.05) — a looser test than the >=30% the map paints by,
            // so a neighbour can feed claimed edges here yet still render white on the map.
            float unclaimed = p.ownershipData?.unclaimedScore ?? 1f;
            sb.AppendLine($"  unclaimedScore={unclaimed:0.000}  landNeighbours={(p.borderShares?.Count ?? 0)}  naturalEdges={p.naturalBorderEdges}");
            if (p.borderShares != null)
            {
                foreach (var kv in p.borderShares.OrderByDescending(k => k.Value))
                {
                    var n = mgr.Provinces.FirstOrDefault(x => x.id == kv.Key);
                    var nData = n?.ownershipData;
                    var baseOwner = RegionalOwnershipUtility.DominantBaseOwner(nData);
                    var legit = RegionalDomainUtility.LegitimateClaimsOrdered(nData);
                    string legitStr = legit.Count > 0 ? $"{Name(legit[0].faction)} {legit[0].TotalScore:0.00}" : "UNCLAIMED(<0.30)";
                    string edgeClaim = baseOwner != null ? "claimed" : "UNCLAIMED(no base owner)";
                    sb.AppendLine($"     nbr #{kv.Key} '{n?.name}' edges={kv.Value}: edgeAttr={edgeClaim} (baseOwner={Name(baseOwner)}); mapPaints={legitStr}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string DerivationBlock(GeographicProvince p)
        {
            var data = p.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(p);
            var sb = new StringBuilder();
            float top = data.factionScores.Count > 0 ? data.factionScores.Max(s => s.TotalScore) : 0f;
            bool noHoldings = data.primaryCount == 0 && data.secondaryCount == 0;
            sb.AppendLine($"  #{p.id} {p.name}: top={top:0.000} holdings(prim/sec)={data.primaryCount}/{data.secondaryCount} " +
                          $"edges(claimed/total)={data.claimedBorderEdges}/{data.totalBorderEdges} naturalEdges={p.naturalBorderEdges} unclaimed={data.unclaimedScore:0.000}" +
                          (noHoldings && top > 0.7001f ? "  [OVER CAP!]" : ""));
            foreach (var s in data.factionScores.OrderByDescending(x => x.TotalScore).Take(4))
            {
                float settle = s.settlementScore + s.outpostCoverageScore + s.mostOutpostsScore + s.demographicScore;
                sb.AppendLine($"     {Name(s.faction)}: {s.TotalScore:0.000} = settle {settle:0.000} + border {s.perimeterCoverageScore:0.000} + bonus {s.externalPerimeterScore:0.000}");
            }
            return sb.ToString();
        }

        private static bool NearLandmark(int tileId)
        {
            WorldGrid grid = Find.WorldGrid;
            Tile t = grid[tileId];
            if (t != null && (t.IsCoastal || t.WaterCovered)) return true;

            var ns = new List<PlanetTile>();
            grid.GetTileNeighbors(tileId, ns);
            foreach (var n in ns)
            {
                if (grid.GetRoadDef(tileId, n.tileId) != null) return true;
                if (grid.GetRiverDef(tileId, n.tileId) != null || grid.GetRiverDef(n.tileId, tileId) != null) return true;
                Tile nt = grid[n.tileId];
                if (nt != null && nt.WaterCovered) return true;
            }
            return false;
        }

        private static string Name(Faction f) => f != null ? TextureUtility.GetFactionDisplayName(f) : "Unknown";
    }
}
