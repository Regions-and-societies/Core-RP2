using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public class FactionOwnershipScore
    {
        public Faction faction;
        public float settlementScore;
        public float perimeterCoverageScore;
        public float externalPerimeterScore;
        public float outpostCoverageScore;
        public float mostOutpostsScore;
        public float demographicScore;

        public float TotalScore => Mathf.Clamp01(settlementScore + perimeterCoverageScore + externalPerimeterScore + outpostCoverageScore + mostOutpostsScore + demographicScore);

        /// <summary>
        /// Rescale every component by the same factor. Used to normalise over the components that
        /// are actually in play, so the displayed breakdown and the total stay consistent (#44).
        /// </summary>
        public void Scale(float factor)
        {
            settlementScore *= factor;
            perimeterCoverageScore *= factor;
            externalPerimeterScore *= factor;
            outpostCoverageScore *= factor;
            mostOutpostsScore *= factor;
            demographicScore *= factor;
        }
    }

    public class RegionalOwnershipData
    {
        public GeographicProvince province;
        public List<FactionOwnershipScore> factionScores = new List<FactionOwnershipScore>();
        public float unclaimedScore = 1f;

        /// <summary>Dev-mode only: the raw pre-normalization derivation, shown in the tooltip so the
        /// scoring can be tuned against ground truth rather than reverse-engineered.</summary>
        public string debugBreakdown;

        // Diagnostics captured during scoring, for the dev derivation block.
        public int primaryCount;
        public int secondaryCount;
        public int claimedBorderEdges;
        public int totalBorderEdges;
        public float internalOwnership;   // 0.7.3: settledness from holdings, computed before border influence
        public float borderAttenuation;   // 0.7.3: 1 - internalOwnership; multiplies the border term

        public Faction PrimaryOwner => factionScores.OrderByDescending(s => s.TotalScore).FirstOrDefault(s => s.TotalScore > 0f)?.faction;

        /// <summary>This faction's share of the province, 0 if it has no presence at all.</summary>
        public float ScoreFor(Faction faction)
        {
            if (faction == null) return 0f;
            var entry = factionScores.FirstOrDefault(s => s.faction == faction);
            return entry != null ? entry.TotalScore : 0f;
        }

        /// <summary>
        /// The highest score held by anyone other than <paramref name="faction"/>, 0 if it has the
        /// province to itself.
        ///
        /// The faction's own entry is skipped explicitly. Holding a province harder must never read
        /// as more pressure on yourself — that inversion is the obvious way to get every consumer of
        /// this number backwards at once, and there are now two of them: Epic 3's derived security
        /// and Epic 3 child 6's interception. Null and factionless entries are stepped over rather
        /// than thrown on, because ownership data is rebuilt from live world objects and a holding
        /// can lose its faction between one rebuild and the next.
        ///
        /// The strongest rival, not the sum of them: three weak neighbours are not equivalent to one
        /// strong one, and summing would make a crowded map uniformly hostile.
        /// </summary>
        public float StrongestRivalScore(Faction faction)
        {
            if (factionScores == null) return 0f;

            float strongest = 0f;
            foreach (var score in factionScores)
            {
                if (score == null || score.faction == null) continue;
                if (score.faction == faction) continue;
                if (score.TotalScore > strongest) strongest = score.TotalScore;
            }

            return strongest;
        }

        /// <summary>Every faction scoring above the ownership threshold, strongest first.</summary>
        public List<FactionOwnershipScore> Contenders()
        {
            return factionScores
                .Where(s => s.TotalScore >= PlacementRules.OwnershipThreshold)
                .OrderByDescending(s => s.TotalScore)
                .ToList();
        }

        /// <summary>
        /// True when the two strongest factions both clear the threshold and are within
        /// <see cref="PlacementRules.ContestMargin"/> of each other. A contested province has no
        /// settled owner and placement rules treat it differently from foreign ground.
        /// </summary>
        public bool IsContested()
        {
            var contenders = Contenders();
            if (contenders.Count < 2) return false;
            return contenders[0].TotalScore - contenders[1].TotalScore <= PlacementRules.ContestMargin;
        }
    }

    public static class RegionalOwnershipUtility
    {
        // 0.7: how much each kind of holding counts toward its faction's claim.
        //
        // Before 0.7 only settlements and outposts scored at all — a faction could garrison a
        // province with military installations and forward camps and still read as having no
        // presence there. The weights below are chosen so a world containing only settlements and
        // outposts scores exactly as it did before; the new kinds add to the picture rather than
        // redistributing it.
        private const float SettlementWeight = 1.0f;
        private const float MilitaryWeight = 0.6f;
        private const float OutpostWeight = 1.0f;
        private const float CampWeight = 0.4f;

        // 0.7.3 (#69): a province's ownership is settlement weight + border weight. The two budgets are
        //   settlement/holdings 0.50  +  border 0.40  +  dominant-border bonus 0.10  =  1.0.
        // The settlement budget is split across the holding components below; a lone settlement (which
        // also counts as an outpost, and as the max-outpost holder) scores settle 0.30 + outpost 0.15 +
        // most 0.05 = 0.50. The border budget (0.40) is split by the PERIMETER — see ApplyBordersAndNormalize
        // — and the 0.10 bonus goes to the faction with the largest border share. So a settled region reads
        // ~0.50 from its settlement plus its share of the border; a settlement-less region has only the
        // border budget to draw on and tops out at 0.50, never enough to fence out a colony.
        //
        // No weight is reserved for demographics while CalculateDemographicScore is stubbed at 0 (see
        // #34): reserving it only bled the unfilled share into "unclaimed". When #34 lands a real
        // demographic signal, carve its weight back out of these budgets.
        private const float SettlementScoreWeight = 0.30f;
        private const float OutpostScoreWeight    = 0.15f;
        private const float MostOutpostsWeight    = 0.05f;

        public static RegionalOwnershipData CalculateOwnership(GeographicProvince province)
        {
            // Fallback for callers without pre-bucketed objects (e.g. GetControl on one province).
            // RecalculateProvinceOwners buckets every world object once, O(worldObjects), and calls
            // the overload below — replacing the per-province AllWorldObjects.Where(tiles.Contains)
            // scan that was O(worldObjects * tiles) summed across the map (#48).
            List<WorldObject> regionObjects = (province?.tiles != null && Find.WorldObjects != null)
                ? Find.WorldObjects.AllWorldObjects.Where(obj => province.tiles.Contains(obj.Tile)).ToList()
                : new List<WorldObject>();
            return CalculateOwnership(province, regionObjects);
        }

        public static RegionalOwnershipData CalculateOwnership(GeographicProvince province, List<WorldObject> regionObjects)
        {
            // Single-province fallback (e.g. GetControl with no cached data): base scores + normalize,
            // WITHOUT border influence — that needs the neighbour owners the two-pass
            // RecalculateProvinceOwners supplies. The cached ownershipData it writes DOES include borders.
            var data = CalculateOwnershipBase(province, regionObjects);
            // No border pass here (the edge-share split needs neighbour owners the two-pass
            // RecalculateProvinceOwners supplies), so this rare no-cache path reports the region's own
            // holdings only — a settlement owner still reads as holding it. GetControl falls back here.
            FinalizeScores(data, province, null);
            return data;
        }

        /// <summary>
        /// Pass 1: a province's ownership from its own holdings only — settlements, outposts,
        /// demographics — with no border influence and no normalization. The dominant owner of this
        /// is what neighbouring provinces read when computing their border scores.
        /// </summary>
        public static RegionalOwnershipData CalculateOwnershipBase(GeographicProvince province, List<WorldObject> regionObjects)
        {
            var data = new RegionalOwnershipData { province = province };
            if (province == null || province.tiles == null || province.tiles.Count == 0 || Find.WorldGrid == null)
            {
                return data;
            }
            if (regionObjects == null) regionObjects = new List<WorldObject>();

            // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
            // Primary holdings are the population centres and the forces stationed to hold them;
            // secondary holdings are the production and forward positions that support them.
            var primary = regionObjects.Where(o => IsKind(o, WorldObjectKind.Settlement, WorldObjectKind.Military)).ToList();
            // A settlement counts as an outpost too: settlements matter enough to map dynamics that
            // they are double-counted — once for the settlement component, once for the outpost
            // component and its most-holdings bonus. So a lone settlement earns settle 0.20 +
            // outpost 0.20 + most 0.10 = 0.50 over the 0.80 denominator (#44).
            var secondary = regionObjects.Where(o =>
            {
                WorldObjectKind k = WorldObjectClassifier.Classify(o);
                return k == WorldObjectKind.Outpost || k == WorldObjectKind.Camp || k == WorldObjectKind.Settlement;
            }).ToList();
            data.primaryCount = primary.Count;
            data.secondaryCount = secondary.Count;

            HashSet<Faction> candidateFactions = GetCandidateFactions(primary, secondary);
            if (candidateFactions.Count == 0) return data;   // no holdings: only neighbours' borders can claim it

            Faction maxSecondaryOwner = GetMaxSecondaryHoldingOwner(secondary);
            float primaryTotal = WeightedTotal(primary);
            float secondaryTotal = WeightedTotal(secondary);

            // #42: a faction reflects ownership only if it has a SETTLEMENT here, or — in lieu of a
            // settlement — it holds the most outposts. A faction with only a minority of outposts and no
            // settlement has not claimed the region; its holdings read as UNOWNED and are skipped. Their
            // weight stays in the denominators, so the eligible owner's share is diluted and the leftover
            // surfaces as unclaimedScore. maxSecondaryOwner is the plurality-of-secondary owner — in a
            // settled region that is the settlement holder, in an unsettled one the most-outposts holder —
            // so "settlement, else most outposts" falls straight out of it.
            var settlementFactions = new HashSet<Faction>(
                regionObjects.Where(o => o.Faction != null && WorldObjectClassifier.Classify(o) == WorldObjectKind.Settlement)
                             .Select(o => o.Faction));

            foreach (Faction f in candidateFactions)
            {
                if (!settlementFactions.Contains(f) && f != maxSecondaryOwner) continue;   // marginal presence → unowned

                var score = new FactionOwnershipScore { faction = f };
                // 0.7.3: internal-holding weights sized so a real settlement reads as a strong owner
                // on its own, WITHOUT the old x2.5 normalization amplification (which is gone). A lone
                // settlement scores settle 0.55 + outpost 0.25 + most 0.10 = 0.90; the remaining 0.10
                // is unclaimed wilderness until it expands. Border influence adds on top, capped.
                if (primaryTotal > 0f)  score.settlementScore = SettlementScoreWeight * (WeightedTotalFor(primary, f) / primaryTotal);
                if (secondaryTotal > 0f) score.outpostCoverageScore = OutpostScoreWeight * (WeightedTotalFor(secondary, f) / secondaryTotal);
                if (maxSecondaryOwner != null && f == maxSecondaryOwner) score.mostOutpostsScore = MostOutpostsWeight;
                score.demographicScore = CalculateDemographicScore(province, f, primary);
                data.factionScores.Add(score);
            }
            return data;
        }

        /// <summary>The strongest holder of a province by its own holdings (pass-1 base score), or
        /// null if nobody clears a small floor. Neighbours read this for their border scores.</summary>
        public static Faction DominantBaseOwner(RegionalOwnershipData data)
        {
            if (data == null) return null;
            FactionOwnershipScore best = null;
            foreach (var s in data.factionScores)
                if (s.faction != null && (best == null || s.TotalScore > best.TotalScore)) best = s;
            return (best != null && best.TotalScore > PlacementRules.PresenceFloor) ? best.faction : null;
        }

        // 0.7.3 (#69): the border budget and the dominant-border bonus. Ownership = settlement weight
        // (0.50, from the holding components in pass 1) + this border weight (0.40, split by the
        // perimeter) + this bonus (0.10, to the largest border share). A settlement-less region has only
        // these two to draw on, so it tops out at 0.50 — never enough to fence out a colony (the #69 fix).
        private const float BorderWeight = 0.40f;
        private const float ExternalBonus = 0.10f;

        /// <summary>
        /// Pass 2: add the BORDER weight on top of pass 1's settlement weight (0.7.3, #69).
        ///
        /// <para>The 0.40 border budget is split by the region's PERIMETER. A land edge whose neighbour
        /// province is held by a RIVAL (anyone other than this region's own major owner) counts for that
        /// rival — exterior border pressure. An edge that borders land the MAJOR OWNER also holds counts
        /// for the owner as secure ground; the natural barriers (mountain/water) that ring the region do
        /// too, but ONLY when the owner already holds the region's major territorial claim (#42) — a lone
        /// settlement has not earned them. An edge to UNHELD land, and any unearned barrier, counts for NO
        /// ONE — frontier the owner has not reached, which stays unclaimed (#42). Each faction's border
        /// score is <c>0.40 × (its edges / total edges)</c>; the 0.10 bonus
        /// goes to the faction with the most border edges. These add to the holding scores already on
        /// <paramref name="data"/>, so a settled region reads settlement + its secured border share, and a
        /// lone settlement in open country no longer absorbs the empty frontier around it.</para>
        ///
        /// <para>The major owner is the dominant holder from pass 1, carried in
        /// <paramref name="ownerByProvince"/>. With no holding owner, the region is pure border bleed: only
        /// rival-held land borders score, the mountains and unowned frontier stay unclaimed, and the total
        /// tops out at the border budget + bonus = 0.50 — below the settlement weight, so a settlement
        /// always out-claims mere proximity, and below the exclusive threshold, so it never fences out a
        /// colony (#69). The geometry is static, so a neighbour changing owner is a cheap recompute.</para>
        /// </summary>
        public static void ApplyBordersAndNormalize(RegionalOwnershipData data, GeographicProvince province, Dictionary<int, Faction> ownerByProvince)
        {
            if (data == null || province == null) return;

            // The region's own major owner (dominant holding), decided in pass 1. Edges to neighbours this
            // faction holds, plus the mountains and unowned frontier, are all its secure ground.
            Faction majorOwner = null;
            if (ownerByProvince != null) ownerByProvince.TryGetValue(province.id, out majorOwner);

            // #42: a border edge is the owner's only if it is a natural barrier enclosing the region or an
            // edge to land the owner ALSO holds. An edge to UNHELD land is frontier the owner has not
            // reached — being the only faction present is not the same as having claimed the emptiness
            // around you — so it is credited to no one and surfaces as unclaimedScore. This is what stops a
            // lone settlement from reading as full control of an otherwise empty region.
            int totalEdges = Mathf.Max(0, province.naturalBorderEdges);   // natural barriers enclose the region
            int selfNeighbourEdges = 0;   // edges to neighbouring provinces the major owner also holds
            var rivalEdges = new Dictionary<Faction, int>();
            if (province.borderShares != null)
            {
                foreach (var kv in province.borderShares)
                {
                    totalEdges += kv.Value;
                    Faction nOwner = null;
                    if (ownerByProvince != null) ownerByProvince.TryGetValue(kv.Key, out nOwner);
                    if (nOwner == null)
                    {
                        // unheld neighbour → unclaimed frontier; stays in totalEdges as denominator only
                    }
                    else if (nOwner == majorOwner)
                    {
                        selfNeighbourEdges += kv.Value;         // owner's own neighbouring territory → secure
                    }
                    else
                    {
                        int e; rivalEdges.TryGetValue(nOwner, out e);
                        rivalEdges[nOwner] = e + kv.Value;      // rival-held land border → exterior pressure
                    }
                }
            }
            int rivalTotal = 0;
            foreach (var v in rivalEdges.Values) rivalTotal += v;

            // #42: the natural barriers (mountain/water) that ring a region are the major owner's secure
            // border ONLY when it already holds the region's MAJOR territorial claim — its holdings plus
            // the ground it holds AROUND the region reach a majority (LooseOwnershipThreshold). A lone
            // settlement merely sitting in an otherwise empty region has not earned its coastline or its
            // mountains: they stay unclaimed frontier until it actually controls the area. The test is on
            // the NON-barrier claim, so the barriers can never bootstrap the majority that unlocks them.
            // Edges to the owner's own neighbouring territory always count regardless.
            float majorHoldings = 0f;
            if (majorOwner != null)
            {
                var mo = data.factionScores.FirstOrDefault(s => s.faction == majorOwner);
                if (mo != null) majorHoldings = mo.settlementScore + mo.outpostCoverageScore + mo.mostOutpostsScore + mo.demographicScore;
            }
            float selfBorderShare = totalEdges > 0 ? BorderWeight * ((float)selfNeighbourEdges / totalEdges) : 0f;
            bool ownerHasMajorClaim = majorOwner != null && (majorHoldings + selfBorderShare) >= PlacementRules.LooseOwnershipThreshold;
            int barrierEdges = ownerHasMajorClaim ? Mathf.Max(0, province.naturalBorderEdges) : 0;
            // The owner's secure border: the barriers it has earned (major-claim only) + edges to its own
            // neighbouring territory. NOT the unheld frontier, nor barriers it has not earned — those
            // dilute every share into unclaimedScore.
            int ownerEdges = barrierEdges + selfNeighbourEdges;

            data.totalBorderEdges = totalEdges;
            data.internalOwnership = 0f;
            data.borderAttenuation = 0f;

            Faction topBorderFaction = null;
            int topBorderEdges = 0;
            if (totalEdges > 0)
            {
                // The owner's slice of the border budget (only a holding owner can claim the mountains
                // and unowned frontier; on an unheld region those edges stay unclaimed).
                if (majorOwner != null && ownerEdges > 0)
                {
                    Entry(data, majorOwner).perimeterCoverageScore = BorderWeight * ((float)ownerEdges / totalEdges);
                    topBorderFaction = majorOwner;
                    topBorderEdges = ownerEdges;
                }
                foreach (var kv in rivalEdges)
                {
                    Entry(data, kv.Key).perimeterCoverageScore = BorderWeight * ((float)kv.Value / totalEdges);
                    if (kv.Value > topBorderEdges) { topBorderEdges = kv.Value; topBorderFaction = kv.Key; }
                }
                // Bonus to the faction holding the most of this region's border (#69: "the bonus obviously
                // goes to the largest").
                if (topBorderFaction != null) Entry(data, topBorderFaction).externalPerimeterScore = ExternalBonus;
            }

            data.claimedBorderEdges = (majorOwner != null ? ownerEdges : 0) + rivalTotal;

            FinalizeScores(data, province, rivalEdges);
        }

        /// <summary>Find or add this faction's score entry so a border/bonus term can be added on top of
        /// its holding scores.</summary>
        private static FactionOwnershipScore Entry(RegionalOwnershipData data, Faction faction)
        {
            var entry = data.factionScores.FirstOrDefault(s => s.faction == faction);
            if (entry == null) { entry = new FactionOwnershipScore { faction = faction }; data.factionScores.Add(entry); }
            return entry;
        }

        /// <summary>
        /// Build the dev derivation and record the unclaimed remainder. 0.7.3 edge-share: ownership is the
        /// perimeter split, so for a settled region the owner absorbs everything not under rival pressure
        /// (no "unclaimed") and only a holding-less region carries an unclaimed frontier.
        ///
        /// <para>No barren discount, and no biome term at all. Barren biomes affect only where settlements
        /// are <em>placed</em> (via region generation — barren tiles lump into large regions and are not
        /// split), never who <em>owns</em> a region once something stands in it (#69).</para>
        /// </summary>
        private static void FinalizeScores(RegionalOwnershipData data, GeographicProvince province, Dictionary<Faction, int> rivalEdges)
        {
            if (data == null) return;

            // Only build the developer derivation when the mod option is explicitly on — not merely
            // because Dev Mode is — so the calculation is not triggered in the background and shows
            // only in the expanded region panel (#53/#54).
            if (FactionPlacementSettings.showCalculationBreakdowns)
            {
                var dbg = new System.Text.StringBuilder();
                dbg.AppendLine("--- ownership derivation (0.7.3: settlement 0.50 + border 0.40 + bonus 0.10) ---");
                dbg.AppendLine($"edges(claimed/total)={data.claimedBorderEdges}/{data.totalBorderEdges}  naturalEdges={province?.naturalBorderEdges}");
                foreach (var s in data.factionScores.OrderByDescending(x => x.TotalScore))
                {
                    int re = 0;
                    if (rivalEdges != null && s.faction != null) rivalEdges.TryGetValue(s.faction, out re);
                    string edgeNote = re > 0 ? $"{re} rival edges" : "owner (mountains + unowned + self)";
                    float settle = s.settlementScore + s.outpostCoverageScore + s.mostOutpostsScore + s.demographicScore;
                    dbg.AppendLine($"  {s.faction?.Name}: {s.TotalScore:0.000} = settle {settle:0.000} + border {s.perimeterCoverageScore:0.000} + bonus {s.externalPerimeterScore:0.000}  ({edgeNote})");
                }
                data.debugBreakdown = dbg.ToString();
            }

            float assignedTotal = data.factionScores.Sum(s => s.TotalScore);
            data.unclaimedScore = Mathf.Max(0f, 1f - assignedTotal);
        }

        /// <summary>
        /// How <paramref name="faction"/> stands in <paramref name="province"/>. This is the single
        /// answer placement, expansion, and the inspect pane all read, so they can never disagree.
        /// </summary>
        public static ProvinceControl GetControl(GeographicProvince province, Faction faction)
        {
            if (province == null || faction == null) return ProvinceControl.Unclaimed;

            string fid = faction.GetUniqueLoadID();
            var data = province.ownershipData ?? CalculateOwnership(province);

            bool listedAsOwner = province.owningFactionIds != null && province.owningFactionIds.Contains(fid);
            bool someoneElseListed = province.owningFactionIds != null
                && province.owningFactionIds.Any(id => !string.Equals(id, fid, StringComparison.Ordinal));

            if (data == null)
            {
                if (listedAsOwner) return ProvinceControl.Held;
                return someoneElseListed ? ProvinceControl.Foreign : ProvinceControl.Unclaimed;
            }

            var contenders = data.Contenders();
            bool scoresAsOwner = listedAsOwner || data.ScoreFor(faction) >= PlacementRules.OwnershipThreshold;

            if (scoresAsOwner)
            {
                return data.IsContested() && contenders.Any(c => c.faction != faction)
                    ? ProvinceControl.Contested
                    : ProvinceControl.Held;
            }

            if (contenders.Count > 0 || someoneElseListed) return ProvinceControl.Foreign;

            return ProvinceControl.Unclaimed;
        }

        /// <summary>
        /// True when some faction other than <paramref name="faction"/> holds the province
        /// exclusively (&gt;=71%). This is the only ownership condition that blocks a player start
        /// (#61); loose and legitimate rival ownership do not. Computes ownership on demand when the
        /// cached data is not yet present, exactly as <see cref="GetControl"/> does, so a placement
        /// query never depends on a recalculation having already run.
        /// </summary>
        public static bool IsExclusivelyOwnedByRival(GeographicProvince province, Faction faction)
        {
            if (province == null) return false;
            var data = province.ownershipData ?? CalculateOwnership(province);
            var owner = RegionalDomainUtility.ExclusiveOwner(data);
            return owner != null && owner.faction != faction;
        }

        /// <summary>
        /// True when some faction other than <paramref name="faction"/> holds the province at loose
        /// ownership or better (&gt;=51%, <see cref="PlacementRules.LooseOwnershipThreshold"/>). NPC
        /// placement refuses such provinces (#65) so factions form natural borders instead of
        /// interleaving. Reads the cached <c>ownershipData</c> when present — worldgen placement refreshes
        /// it once per faction, so border-influenced loose ownership (a rival strong in the neighbouring
        /// provinces) is seen, not just direct holdings.
        /// </summary>
        public static bool IsLooseOwnedByRival(GeographicProvince province, Faction faction)
        {
            if (province == null) return false;
            var data = province.ownershipData ?? CalculateOwnership(province);
            if (data?.factionScores == null) return false;
            foreach (var s in data.factionScores)
            {
                if (s != null && s.faction != null && s.faction != faction && s.TotalScore >= PlacementRules.LooseOwnershipThreshold)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when the faction holds or co-holds the province — the old inline 0.30f test.</summary>
        public static bool HoldsTerritory(GeographicProvince province, Faction faction)
        {
            ProvinceControl control = GetControl(province, faction);
            return control == ProvinceControl.Held || control == ProvinceControl.Contested;
        }

        private static bool IsKind(WorldObject obj, WorldObjectKind a, WorldObjectKind b)
        {
            WorldObjectKind kind = WorldObjectClassifier.Classify(obj);
            return kind == a || kind == b;
        }

        private static float WeightOf(WorldObject obj)
        {
            switch (WorldObjectClassifier.Classify(obj))
            {
                case WorldObjectKind.Settlement: return SettlementWeight;
                case WorldObjectKind.Military: return MilitaryWeight;
                case WorldObjectKind.Outpost: return OutpostWeight;
                case WorldObjectKind.Camp: return CampWeight;
                default: return 0f;
            }
        }

        private static float WeightedTotal(List<WorldObject> objects)
        {
            float total = 0f;
            foreach (var o in objects) total += WeightOf(o);
            return total;
        }

        private static float WeightedTotalFor(List<WorldObject> objects, Faction faction)
        {
            float total = 0f;
            foreach (var o in objects)
            {
                if (o.Faction == faction) total += WeightOf(o);
            }
            return total;
        }

        private static HashSet<Faction> GetCandidateFactions(List<WorldObject> primary, List<WorldObject> secondary)
        {
            HashSet<Faction> candidates = new HashSet<Faction>();
            foreach (var s in primary)
            {
                if (s.Faction != null) candidates.Add(s.Faction);
            }
            foreach (var o in secondary)
            {
                if (o.Faction != null) candidates.Add(o.Faction);
            }
            return candidates;
        }

        public static HashSet<int> GetPerimeterTiles(GeographicProvince province)
        {
            // Prefer the precomputed perimeter (SynapseRegionManager.BuildProvinceTopology). It is a
            // pure function of tile membership, so rebuilding it per ownership pass was wasted work.
            if (province.perimeterTiles != null) return new HashSet<int>(province.perimeterTiles);

            HashSet<int> provinceTileSet = new HashSet<int>(province.tiles);
            HashSet<int> perimeter = new HashSet<int>();
            WorldGrid grid = Find.WorldGrid;

            List<PlanetTile> neighbors = new List<PlanetTile>();
            foreach (int tileId in province.tiles)
            {
                grid.GetTileNeighbors(tileId, neighbors);
                foreach (var n in neighbors)
                {
                    if (!provinceTileSet.Contains(n.tileId))
                    {
                        perimeter.Add(tileId);
                        break;
                    }
                }
            }
            return perimeter;
        }

        // Border scoring no longer maps perimeter tiles to the nearest object (the
        // TraversalDistanceBetween pass that MapPerimeterTileOwners / GetMaxExternalPerimeterOwner /
        // the old CalculateFactionScores did). It is derived from neighbour ownership over the
        // precomputed borderShares in ApplyBordersAndNormalize (#44) — cheaper and owner-correct.

        private static Faction GetMaxSecondaryHoldingOwner(List<WorldObject> secondary)
        {
            var valid = secondary.Where(o => o.Faction != null).ToList();
            if (valid.Count == 0) return null;

            var groups = valid
                .GroupBy(o => o.Faction)
                .Select(g => new { faction = g.Key, weight = g.Sum(WeightOf) })
                .OrderByDescending(g => g.weight)
                .ToList();

            return groups.Count > 0 && groups[0].weight > 0f ? groups[0].faction : null;
        }

        /// <summary>
        /// Contributes nothing in 0.7, deliberately. Do not "fix" this back to a value.
        ///
        /// <para>This component is supposed to express what share of a region's people are a
        /// given faction's. It never did. The provider path was real —
        /// <see cref="RegionalDemographicRegistry"/> was consulted and Factions registers an
        /// ideology provider into it — but underneath sat a fallback returning the full 20%
        /// for merely owning a primary holding in the region, which <c>settlementScore</c>
        /// already measures. The same fact was counted twice, the second time under a name
        /// implying something else entirely.</para>
        ///
        /// <para>That fallback was not an edge case. It fired on every install where the
        /// provider path yielded nothing — no providers registered, Ideology inactive, or a
        /// provider returning a negative — which is most of them. It is **deleted** rather
        /// than left dormant behind a zero, because a path that silently double-counts is
        /// exactly what someone later switches back on while "fixing" an unexplained 0.</para>
        ///
        /// <para>The registry, provider registration and Factions' own provider are all left
        /// wired, so 0.8 inherits a live surface rather than rebuilding one. 0.8 replaces this
        /// with a read of the regional ideological distribution (Regions-and-Territories#34),
        /// and Regions-and-Territories#44 makes the component's availability explicit so an
        /// unavailable one leaves the denominator instead of quietly lowering every score.
        /// Until then, ownership is scored only on what 0.7 actually models.</para>
        ///
        /// <para>Parameters are retained so the signature does not churn when 0.8 restores the
        /// body.</para>
        /// </summary>
        private static float CalculateDemographicScore(GeographicProvince province, Faction faction, List<WorldObject> primary)
        {
            return 0f;
        }
    }
}
