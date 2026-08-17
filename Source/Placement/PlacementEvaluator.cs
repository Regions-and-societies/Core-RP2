using System.Collections.Generic;
using RegionsAndSocieties.Integration;

namespace RegionsAndSocieties.Placement
{
    /// <summary>
    /// The 0.7 placement rules, generalised from outposts to every kind of world object.
    ///
    /// Before 0.7 these rules lived in <c>OutpostPlacementUtility</c> and applied only to Vanilla
    /// Outposts Expanded. The buffer and supply checks are unchanged in substance; what is new is
    /// that they now run for settlements and military installations too, that province ownership
    /// gates placement rather than only distance, and that contested provinces are a state in
    /// their own right instead of being lumped in with foreign territory.
    ///
    /// Pure: no <c>Find</c>, no Harmony, no Unity. Everything comes in through
    /// <see cref="PlacementWorld"/>.
    /// </summary>
    public static class PlacementEvaluator
    {
        public static PlacementDecision Evaluate(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            if (world == null) return PlacementDecision.Allow();

            // Non-territorial things — caravans, quest sites, anything unclassified — are none of
            // our business. Governance that refuses to place a quest site is a bug report.
            if (!kind.IsTerritorial()) return PlacementDecision.Allow();

            int provinceId = world.ProvinceAt(tile);
            ProvinceControl control = world.Control(provinceId, faction);
            bool contested = control == ProvinceControl.Contested;

            PlacementDecision decision =
                CheckProvinceOccupancy(world, provinceId, kind) ??
                CheckSeparation(world, tile, kind) ??
                CheckTerritory(world, provinceId, control, faction, kind) ??
                CheckSupplyRange(world, tile, faction, kind) ??
                CheckFoothold(world, provinceId, faction, kind) ??
                PlacementDecision.Allow();

            decision.Contested = contested;
            return decision;
        }

        /// <summary>Convenience overload for call sites that only want a yes/no and a message.</summary>
        public static bool CanPlace(PlacementWorld world, int tile, object faction, WorldObjectKind kind, out string reason)
        {
            PlacementDecision decision = Evaluate(world, tile, faction, kind);
            reason = decision.Reason ?? string.Empty;
            return decision.Allowed;
        }

        // -- individual rules -------------------------------------------------
        // Each returns null when it has no objection, so Evaluate reads as a precedence chain.

        /// <summary>
        /// #66: one settlement per region. A province that already holds a settlement refuses a new one,
        /// whoever owns it. This and an exclusive rival hold (&gt;=71%, see <see cref="CheckTerritory"/>)
        /// are the ONLY hard precludes on a settlement — loose and legitimate rival ownership no longer
        /// forbid it, they only make settling "dangerous" (the anger-on-claim hook). Outposts and other
        /// kinds are unaffected; the rule is about the region's one settlement.
        /// </summary>
        private static PlacementDecision CheckProvinceOccupancy(PlacementWorld world, int provinceId, WorldObjectKind kind)
        {
            if (kind != WorldObjectKind.Settlement) return null;
            if (provinceId < 0) return null;

            List<PlacementHolding> holdings = world.Holdings;
            if (holdings == null) return null;

            for (int i = 0; i < holdings.Count; i++)
            {
                PlacementHolding h = holdings[i];
                if (h == null || h.kind != WorldObjectKind.Settlement) continue;
                if (world.ProvinceAt(h.tile) == provinceId)
                {
                    return PlacementDecision.Refuse(
                        PlacementRejection.RegionAlreadySettled,
                        "Cannot build a settlement here: this region already has a settlement (one settlement per region).");
                }
            }

            return null;
        }

        /// <summary>
        /// Keep permanent holdings from crowding each other, regardless of who owns them or which
        /// mod created them. Applies between every pair of permanent kinds — the pre-0.7 code only
        /// ever tested a new outpost against settlements and outposts.
        /// </summary>
        private static PlacementDecision CheckSeparation(PlacementWorld world, int tile, WorldObjectKind kind)
        {
            List<PlacementHolding> holdings = world.Holdings;
            if (holdings == null) return null;

            for (int i = 0; i < holdings.Count; i++)
            {
                PlacementHolding h = holdings[i];
                if (h == null) continue;

                int required = PlacementRules.MinSeparation(kind, h.kind);
                if (required <= 0) continue;

                if (world.DistanceBetween(tile, h.tile) < required)
                {
                    return PlacementDecision.Refuse(
                        PlacementRejection.TooCloseToHolding,
                        "Cannot build " + Article(kind) + " here: must leave at least "
                            + (required - 1) + " empty tile" + (required - 1 == 1 ? "" : "s")
                            + " between it and the nearest " + Label(h.kind) + ".");
                }
            }

            return null;
        }

        /// <summary>
        /// Somebody else's province refuses a permanent holding. A contested province does not —
        /// a province two factions are both clinging to is exactly where a new holding decides the
        /// matter, and refusing it would freeze every border in the world.
        ///
        /// <para>0.7.2 (#61): player-controlled factions are no longer fenced out by loose or
        /// legitimate ownership. A rival's sphere of influence — which can reach a province on
        /// border bleed alone, with no settlement standing in it — must not isolate a new colony.
        /// The player is refused only by an <b>exclusive</b> rival hold (&gt;=71%).</para>
        ///
        /// <para>0.8 (#65): that same rule now applies to <b>every</b> faction, not just the player.
        /// A faction may settle territory it does not own so long as no rival holds it exclusively
        /// (&gt;=71%); loose and legitimate ownership no longer refuse a settlement. This is the owner's
        /// design call — a contested frontier should be settled and fought over, not frozen — and it
        /// is what the outpost-seeding pass relies on to place holdings on shared ground.</para>
        /// </summary>
        private static PlacementDecision CheckTerritory(PlacementWorld world, int provinceId, ProvinceControl control, object faction, WorldObjectKind kind)
        {
            if (!PlacementRules.BlockedByForeignTerritory(kind)) return null;

            if (world.HasExclusiveRival(provinceId, faction))
            {
                return PlacementDecision.Refuse(
                    PlacementRejection.ForeignTerritory,
                    "Cannot build " + Article(kind) + " here: this region is held exclusively by another faction.");
            }

            return null;
        }

        /// <summary>
        /// A new holding has to be reachable from something the faction already has — one of its
        /// own holdings, or the border of a province it holds. A faction with nothing anywhere is
        /// exempt, otherwise nobody could ever place their first holding.
        /// </summary>
        private static PlacementDecision CheckSupplyRange(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            if (!PlacementRules.RequiresSupplyLine(kind)) return null;

            int nearest = int.MaxValue;
            bool hasAnchor = false;

            List<PlacementHolding> holdings = world.Holdings;
            if (holdings != null)
            {
                for (int i = 0; i < holdings.Count; i++)
                {
                    PlacementHolding h = holdings[i];
                    if (h == null || !world.SameClaimant(h.faction, faction)) continue;
                    if (!h.kind.IsPermanentHolding()) continue;

                    hasAnchor = true;
                    int d = world.DistanceBetween(tile, h.tile);
                    if (d < nearest) nearest = d;
                }
            }

            foreach (int borderTile in world.BorderTilesFor(faction))
            {
                hasAnchor = true;
                int d = world.DistanceBetween(tile, borderTile);
                if (d < nearest) nearest = d;
            }

            if (!hasAnchor) return null;
            if (nearest <= PlacementRules.MaxSupplyDistance) return null;

            return PlacementDecision.Refuse(
                PlacementRejection.OutOfSupplyRange,
                "Too far from your territory border or holdings (must be within "
                    + PlacementRules.MaxSupplyDistance + " tiles).");
        }

        /// <summary>
        /// Sequential expansion: a faction that already holds ground expands into provinces it
        /// holds or provinces beside them, not across the map. Previously enforced only on the
        /// vanilla settle path; now every permanent holding obeys it.
        ///
        /// <para>Epic 5 child 3 asked that new holdings extend territory outward from already-held
        /// <i>borders</i>. Until now the rule measured outward from <i>holdings</i>, which is a
        /// narrower thing: a province is held on ownership score or on a worldgen ownership entry,
        /// neither of which requires a world object of the faction's to be standing in it. A faction
        /// holding a province it has not yet built in could not expand across that province's
        /// border, which is the exact move the rule exists to permit.</para>
        ///
        /// <para>Reading the held provinces as well is a relaxation everywhere a faction owns
        /// anything at all. The one case it tightens is a faction with territory and no permanent
        /// holding anywhere, which used to be exempt from the rule outright; it is now expected to
        /// build from its own border like everyone else, and it can always build inside its own
        /// province, so it is never left with nowhere to go.</para>
        /// </summary>
        private static PlacementDecision CheckFoothold(PlacementWorld world, int provinceId, object faction, WorldObjectKind kind)
        {
            if (!PlacementRules.RequiresAdjacentFoothold(kind)) return null;
            if (provinceId < 0) return null;

            ProvinceControl here = world.Control(provinceId, faction);
            if (here == ProvinceControl.Held || here == ProvinceControl.Contested) return null;

            bool hasTerritory = false;

            // Held ground first, because it is the cheaper question and the more likely answer for
            // an established faction: one province lookup per province held, no distance work.
            foreach (int heldProvince in world.HeldProvincesFor(faction))
            {
                if (heldProvince < 0) continue;

                hasTerritory = true;
                if (heldProvince == provinceId) return null;
                if (world.AreAdjacent(heldProvince, provinceId)) return null;
            }

            List<PlacementHolding> holdings = world.Holdings;

            if (holdings != null)
            {
                for (int i = 0; i < holdings.Count; i++)
                {
                    PlacementHolding h = holdings[i];
                    if (h == null || !world.SameClaimant(h.faction, faction)) continue;
                    if (!h.kind.IsPermanentHolding()) continue;

                    // A holding is a foothold whether or not its province scores as held yet — a
                    // faction that has just planted its first colony has a border before it has an
                    // ownership score, and refusing to let it expand until the score catches up
                    // would make the first two turns of a game feel broken.
                    hasTerritory = true;

                    int theirProvince = world.ProvinceAt(h.tile);
                    if (theirProvince < 0) continue;
                    if (theirProvince == provinceId) return null;
                    if (world.AreAdjacent(theirProvince, provinceId)) return null;
                }
            }

            if (!hasTerritory) return null;

            return PlacementDecision.Refuse(
                PlacementRejection.NoAdjacentFoothold,
                "Cannot build " + Article(kind) + " here: this region is too far from your existing territory. Expand into an adjacent region first.");
        }

        // -- helpers ----------------------------------------------------------

        public static string Label(WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.Settlement: return "settlement";
                case WorldObjectKind.Outpost: return "outpost";
                case WorldObjectKind.Camp: return "camp";
                case WorldObjectKind.Military: return "military installation";
                case WorldObjectKind.Site: return "site";
                case WorldObjectKind.Caravan: return "caravan";
                default: return "world object";
            }
        }

        private static string Article(WorldObjectKind kind)
        {
            string label = Label(kind);
            return (label[0] == 'a' || label[0] == 'e' || label[0] == 'i' || label[0] == 'o' || label[0] == 'u' ? "an " : "a ") + label;
        }
    }
}
