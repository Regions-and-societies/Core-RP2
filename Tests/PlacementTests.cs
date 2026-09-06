// Behaviour tests for the 0.7 placement governance layer (Epic 2).
//
// PlacementEvaluator is pure by design, so this suite needs no RimWorld at all: tiles are points
// on a line, distance is |a-b|, and factions are plain strings. What is being tested is the rules,
// which is the part that can actually be wrong.
using System;
using System.Collections.Generic;
using System.Linq;
using RegionsAndSocieties;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Placement;

namespace PlacementTests
{
    public static class Program
    {
        private static int failures;

        private const string Player = "player";
        private const string Empire = "player-empire";
        private const string Rival = "rival";

        public static int Main()
        {
            Section("an empty world refuses nothing");
            var empty = World();
            Check("settlement on bare ground", Allowed(empty, 10, Player, WorldObjectKind.Settlement));
            Check("outpost on bare ground", Allowed(empty, 10, Player, WorldObjectKind.Outpost));

            Section("buffer between permanent holdings");
            var crowded = World(Holding(0, WorldObjectKind.Settlement, Rival));
            Check("outpost one tile away is refused", Refused(crowded, 1, Player, WorldObjectKind.Outpost, PlacementRejection.TooCloseToHolding));
            Check("outpost two tiles away is fine", Allowed(crowded, 2, Player, WorldObjectKind.Outpost));
            Check("settlement one tile away is refused too", Refused(crowded, 1, Player, WorldObjectKind.Settlement, PlacementRejection.TooCloseToHolding));
            Check("the refusal names the neighbour", Reason(crowded, 1, Player, WorldObjectKind.Outpost).Contains("settlement"));

            Section("military installations hold ground (new in 0.7)");
            var garrisoned = World(Holding(0, WorldObjectKind.Military, Rival));
            Check("outpost beside a garrison is refused", Refused(garrisoned, 1, Player, WorldObjectKind.Outpost, PlacementRejection.TooCloseToHolding));
            Check("military beside a garrison is refused", Refused(garrisoned, 1, Player, WorldObjectKind.Military, PlacementRejection.TooCloseToHolding));

            Section("camps are exempt from the buffer, both ways");
            Check("a camp may be pitched beside a garrison", Allowed(garrisoned, 1, Player, WorldObjectKind.Camp));
            var camped = World(Holding(0, WorldObjectKind.Camp, Rival));
            Check("a settlement may be built beside a camp", Allowed(camped, 1, Player, WorldObjectKind.Settlement));

            Section("non-territorial objects are never governed");
            Check("caravan", Allowed(crowded, 0, Player, WorldObjectKind.Caravan));
            Check("quest site", Allowed(crowded, 0, Player, WorldObjectKind.Site));
            Check("unclassified object", Allowed(crowded, 0, Player, WorldObjectKind.Unknown));

            Section("supply range");
            var anchored = World(Holding(0, WorldObjectKind.Settlement, Player));
            Check("at the supply limit", Allowed(anchored, PlacementRules.MaxSupplyDistance, Player, WorldObjectKind.Outpost));
            Check("one tile past it", Refused(anchored, PlacementRules.MaxSupplyDistance + 1, Player, WorldObjectKind.Outpost, PlacementRejection.OutOfSupplyRange));
            Check("a faction with nothing anywhere is exempt", Allowed(anchored, 100, Rival, WorldObjectKind.Outpost));
            Check("camps need no supply line", Allowed(anchored, 100, Player, WorldObjectKind.Camp));

            Section("supply runs from held borders, not just holdings");
            var bordered = World(Holding(0, WorldObjectKind.Settlement, Player));
            bordered.HeldBorderTiles = f => Equals(f, Player) ? new[] { 50 } : new int[0];
            Check("a tile beside the border is supplied", Allowed(bordered, 52, Player, WorldObjectKind.Outpost));
            Check("a tile far from both is not", Refused(bordered, 200, Player, WorldObjectKind.Outpost, PlacementRejection.OutOfSupplyRange));

            Section("empire-style player factions share supply (Epic 1 feeds Epic 2)");
            var splitPlayer = World(Holding(0, WorldObjectKind.Settlement, Empire));
            splitPlayer.FactionsMatch = (a, b) => IsPlayerSide(a) && IsPlayerSide(b);
            Check("a player outpost draws supply from an empire colony", Allowed(splitPlayer, 5, Player, WorldObjectKind.Outpost));
            Check("...and is held to that colony's supply range",
                Refused(splitPlayer, 100, Player, WorldObjectKind.Outpost, PlacementRejection.OutOfSupplyRange));
            var noMatch = World(Holding(0, WorldObjectKind.Settlement, Empire));
            Check("without the equivalence the colony anchors nothing", Allowed(noMatch, 100, Player, WorldObjectKind.Outpost));

            Section("territory ownership fences out a permanent holding only when the rival is exclusive (#65)");
            // 0.8: loose or legitimate foreign ownership no longer refuses a settlement, for any
            // faction. Only an exclusive (>=71%) rival hold does. This is what lets a faction settle
            // contested ground and what the outpost-seeding pass relies on.
            var foreign = World();
            foreign.ProvinceIdAt = t => 1;
            foreign.ControlOf = (p, f) => ProvinceControl.Foreign;
            Check("settlement on loosely-foreign ground is now allowed", Allowed(foreign, 10, Player, WorldObjectKind.Settlement));
            Check("outpost on loosely-foreign ground is now allowed", Allowed(foreign, 10, Player, WorldObjectKind.Outpost));
            Check("an NPC faction is likewise no longer fenced out by loose ownership", Allowed(foreign, 10, Rival, WorldObjectKind.Settlement));

            var exclusive = World();
            exclusive.ProvinceIdAt = t => 1;
            exclusive.ControlOf = (p, f) => ProvinceControl.Foreign;
            exclusive.ExclusiveRivalAt = (p, f) => true;
            Check("settlement refused when a rival holds the province exclusively", Refused(exclusive, 10, Player, WorldObjectKind.Settlement, PlacementRejection.ForeignTerritory));
            Check("outpost refused when a rival holds the province exclusively", Refused(exclusive, 10, Player, WorldObjectKind.Outpost, PlacementRejection.ForeignTerritory));
            Check("an NPC faction is refused by an exclusive rival too", Refused(exclusive, 10, Rival, WorldObjectKind.Settlement, PlacementRejection.ForeignTerritory));
            Check("camp may still trespass even on exclusive ground", Allowed(exclusive, 10, Player, WorldObjectKind.Camp));

            Section("contested ground is not foreign ground");
            var contested = World();
            contested.ProvinceIdAt = t => 1;
            contested.ControlOf = (p, f) => ProvinceControl.Contested;
            Check("placement allowed", Allowed(contested, 10, Player, WorldObjectKind.Settlement));
            Check("and flagged as contested", Evaluate(contested, 10, Player, WorldObjectKind.Settlement).Contested);
            Check("held ground is not flagged", !Evaluate(empty, 10, Player, WorldObjectKind.Settlement).Contested);

            Section("sequential expansion");
            var provinces = World(Holding(0, WorldObjectKind.Settlement, Player));
            // Provinces are three tiles wide, so every case below stays inside supply range and
            // the only thing under test is province adjacency.
            provinces.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            provinces.ProvincesAdjacent = (a, b) => Math.Abs(a - b) <= 1;
            provinces.ControlOf = (p, f) => ProvinceControl.Unclaimed;
            // 0.8 (#66): one settlement per region is the hard preclude, so a second settlement in
            // the province the player already settled is refused; outposts still expand in place.
            Check("a second settlement into the same province is refused", Refused(provinces, 2, Player, WorldObjectKind.Settlement, PlacementRejection.RegionAlreadySettled));
            Check("an outpost into the same province is allowed", Allowed(provinces, 2, Player, WorldObjectKind.Outpost));
            Check("into an adjacent province", Allowed(provinces, 4, Player, WorldObjectKind.Settlement));
            Check("across a gap is refused", Refused(provinces, 7, Player, WorldObjectKind.Settlement, PlacementRejection.NoAdjacentFoothold));
            Check("camps ignore the rule", Allowed(provinces, 7, Player, WorldObjectKind.Camp));
            Check("a faction with no holdings ignores it", Allowed(provinces, 7, Rival, WorldObjectKind.Settlement));

            Section("a province you already hold is always reachable");
            var held = World(Holding(0, WorldObjectKind.Settlement, Player));
            held.ProvinceIdAt = t => t < 3 ? 1 : 3;
            held.ProvincesAdjacent = (a, b) => false;
            held.ControlOf = (p, f) => p == 3 ? ProvinceControl.Held : ProvinceControl.Unclaimed;
            Check("a non-adjacent province you own is still settleable", Allowed(held, 4, Player, WorldObjectKind.Settlement));

            Section("expansion runs outward from held borders, not only from holdings (Epic 5 child 3)");
            // Three provinces in a row, tiles three wide, nothing built anywhere. The faction is
            // listed as holding province 2 — which is what a worldgen ownership entry or an
            // ownership score above the threshold produces without a world object standing in it.
            var borders = World(
                Holding(0, WorldObjectKind.Settlement, Player),
                Holding(2, WorldObjectKind.Settlement, Rival));
            borders.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            borders.ProvincesAdjacent = (a, b) => Math.Abs(a - b) == 1;
            borders.HeldProvinceIds = f => Equals(f, Player) ? new[] { 2 } : new int[0];
            Check("a province beyond the holding but beside held ground is reachable",
                Allowed(borders, 7, Player, WorldObjectKind.Settlement));
            Check("a rival standing on the same map gets no benefit from ground it does not hold",
                Refused(borders, 7, Rival, WorldObjectKind.Settlement, PlacementRejection.NoAdjacentFoothold));

            // Same map, the faction holds nothing: this is the pre-0.7 answer, and it must not move.
            var unheld = World(Holding(0, WorldObjectKind.Settlement, Player));
            unheld.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            unheld.ProvincesAdjacent = (a, b) => Math.Abs(a - b) == 1;
            Check("without held ground the same tile is still refused",
                Refused(unheld, 7, Player, WorldObjectKind.Settlement, PlacementRejection.NoAdjacentFoothold));
            Check("and the adjacent province is still allowed",
                Allowed(unheld, 4, Player, WorldObjectKind.Settlement));

            Section("territory alone is a foothold, and it still has to be adjacent");
            // A faction with a border and no permanent holding anywhere. It used to be exempt from
            // the rule outright; it is now expected to build from its own border like everyone else.
            var territoryOnly = World();
            territoryOnly.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            territoryOnly.ProvincesAdjacent = (a, b) => Math.Abs(a - b) == 1;
            territoryOnly.HeldProvinceIds = f => Equals(f, Player) ? new[] { 1 } : new int[0];
            Check("it may build one province out from its border",
                Allowed(territoryOnly, 4, Player, WorldObjectKind.Settlement));
            Check("but not two", Refused(territoryOnly, 7, Player, WorldObjectKind.Settlement, PlacementRejection.NoAdjacentFoothold));
            Check("and a faction holding nothing anywhere is still exempt",
                Allowed(territoryOnly, 7, Rival, WorldObjectKind.Settlement));
            Check("camps are exempt from this too", Allowed(territoryOnly, 7, Player, WorldObjectKind.Camp));

            Section("held ground and holdings answer the same question");
            // A holding whose province has not yet scored as held is a foothold anyway: a colony
            // planted this minute has a border before it has an ownership score.
            var fresh = World(Holding(0, WorldObjectKind.Settlement, Player));
            fresh.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            fresh.ProvincesAdjacent = (a, b) => Math.Abs(a - b) == 1;
            fresh.HeldProvinceIds = f => new int[0];
            Check("a brand-new colony can expand before its ownership score catches up",
                Allowed(fresh, 4, Player, WorldObjectKind.Settlement));
            Check("an empire-side colony anchors the player's expansion the same way",
                Allowed(SharedClaim(), 4, Player, WorldObjectKind.Settlement));

            Section("rule precedence");
            var stacked = World(Holding(0, WorldObjectKind.Settlement, Rival));
            stacked.ProvinceIdAt = t => 1;
            stacked.ControlOf = (p, f) => ProvinceControl.Foreign;
            stacked.ExclusiveRivalAt = (p, f) => true;   // 0.8: only an exclusive rival refuses, so make it one
            // 0.8 (#66): occupancy is the hard preclude and is reported first for settlements; the
            // crowding/ownership/supply ladder is exercised with outposts, which occupancy skips.
            Check("occupancy is reported before crowding",
                Evaluate(stacked, 1, Player, WorldObjectKind.Settlement).Rejection == PlacementRejection.RegionAlreadySettled);
            Check("crowding is reported before ownership",
                Evaluate(stacked, 1, Player, WorldObjectKind.Outpost).Rejection == PlacementRejection.TooCloseToHolding);
            Check("ownership is reported before supply range",
                Evaluate(stacked, 400, Player, WorldObjectKind.Outpost).Rejection == PlacementRejection.ForeignTerritory);

            Section("degenerate input");
            Check("a null world allows everything", PlacementEvaluator.Evaluate(null, 0, Player, WorldObjectKind.Settlement).Allowed);
            var nullFaction = World(Holding(0, WorldObjectKind.Settlement, Player));
            Check("a null placing faction is not matched to anything",
                Allowed(nullFaction, 500, null, WorldObjectKind.Settlement));
            var nullHolding = World(Holding(0, WorldObjectKind.Settlement, Player));
            nullHolding.Holdings.Add(null);
            Check("a null holding in the list is skipped", Allowed(nullHolding, 5, Player, WorldObjectKind.Outpost));

            Section("rule table");
            Check("permanent pairs are separated", PlacementRules.MinSeparation(WorldObjectKind.Settlement, WorldObjectKind.Military) == PlacementRules.PermanentHoldingSeparation);
            Check("camp pairs are not", PlacementRules.MinSeparation(WorldObjectKind.Camp, WorldObjectKind.Settlement) == 0);
            Check("outposts need supply", PlacementRules.RequiresSupplyLine(WorldObjectKind.Outpost));
            Check("camps do not", !PlacementRules.RequiresSupplyLine(WorldObjectKind.Camp));
            Check("the contest margin is below the ownership threshold", PlacementRules.ContestMargin < PlacementRules.OwnershipThreshold);

            Console.WriteLine();
            Section("bounded distance (#38): rules ask 'within N?', never 'how far?'");
            {
                int maxSeen = -1;
                int calls = 0;
                var far = World(
                    Holding(0, WorldObjectKind.Settlement, Player),
                    Holding(5, WorldObjectKind.Outpost, Player),
                    Holding(400, WorldObjectKind.Settlement, Player));
                far.DistanceWithin = (a, b, max) =>
                {
                    calls++;
                    if (max > maxSeen) maxSeen = max;
                    int d = Math.Abs(a - b);
                    return d <= max ? d : int.MaxValue;
                };
                far.Distance = (a, b) => throw new InvalidOperationException("unbounded distance consulted");

                Check("an outpost one tile from a settlement is still refused through the bounded search",
                    !Evaluate(far, 1, Player, WorldObjectKind.Outpost).Allowed);
                Check("separation is measured only up to the separation cap", maxSeen == PlacementRules.PermanentHoldingSeparation);

                maxSeen = -1; calls = 0;
                Check("an outpost clear of both neighbours and in supply range is allowed",
                    Evaluate(far, 3, Player, WorldObjectKind.Outpost).Allowed);
                Check("supply range is measured only up to the supply cap", maxSeen == PlacementRules.MaxSupplyDistance);
                Check($"the far holding is never walked to: 3 separation probes + 1 supply probe, then early exit ({calls} calls)", calls == 4);
            }

            Section("inspect-pane placement hints are debounced, deferred and remembered (#44, #45)");
            {
                string hint;
                var cache = new PlacementHintCache(capacity: 8, dwellSeconds: 0.35f, refreshIntervalTicks: 120);

                Check("a freshly selected tile is not evaluated on its first frame",
                    cache.Lookup(7, 10, 1000, 0.00f, false, out hint) == HintLookup.Wait);
                Check("...nor while it has dwelt less than the threshold",
                    cache.Lookup(7, 10, 1000, 0.20f, false, out hint) == HintLookup.Wait);
                Check("...but it is once it has stayed selected long enough",
                    cache.Lookup(7, 10, 1000, 0.40f, false, out hint) == HintLookup.Evaluate);

                cache.Store(7, 10, 1000, "Too close to a settlement");
                Check("the stored hint answers instantly on the next frame",
                    cache.Lookup(7, 10, 1000, 0.41f, false, out hint) == HintLookup.Hit && hint == "Too close to a settlement");
                Check("a cached tile answers even while busy and before any dwell",
                    cache.Lookup(7, 10, 1000, 0.42f, true, out hint) == HintLookup.Hit);

                var hop = new PlacementHintCache();
                bool anyEvaluate = false;
                for (int i = 0; i < 50; i++)
                    anyEvaluate |= hop.Lookup(100 + i, 10, 1000, i * 0.05f, false, out hint) == HintLookup.Evaluate;
                Check("hopping across 50 tiles faster than the dwell never evaluates", !anyEvaluate);
                Check("...and resting on the last one does",
                    hop.Lookup(149, 10, 1000, 49 * 0.05f + 0.5f, false, out hint) == HintLookup.Evaluate);

                var busy = new PlacementHintCache();
                busy.Lookup(3, 10, 1000, 0f, true, out hint);
                Check("while Map Preview is generating, a dwelt tile still waits",
                    busy.Lookup(3, 10, 1000, 1.0f, true, out hint) == HintLookup.Wait);
                Check("...and is evaluated the moment the preview is done, with no second dwell",
                    busy.Lookup(3, 10, 1000, 1.01f, false, out hint) == HintLookup.Evaluate);

                cache.Lookup(8, 10, 1000, 10.0f, false, out hint);
                Check("an allowed tile reaches evaluation like any other",
                    cache.Lookup(8, 10, 1000, 10.4f, false, out hint) == HintLookup.Evaluate);
                cache.Store(8, 10, 1000, null);
                Check("...and its empty answer is remembered too (allowed is the common case)",
                    cache.Lookup(8, 10, 1000, 10.41f, false, out hint) == HintLookup.Hit && hint == null);

                Check("paused world (tick unchanged) after a long real-time wait: still a hit",
                    cache.Lookup(7, 10, 1000, 999f, false, out hint) == HintLookup.Hit);
                Check("the refresh interval of game ticks expires the answer",
                    cache.Lookup(7, 10, 1120, 999.1f, false, out hint) != HintLookup.Hit);
                cache.Store(7, 10, 1120, "x");
                Check("a changed world-object set invalidates the tile",
                    cache.Lookup(7, 11, 1120, 999.2f, false, out hint) != HintLookup.Hit);
                cache.Store(7, 11, 1120, "x");
                Check("a tick that went backwards (another game loaded) invalidates too",
                    cache.Lookup(7, 11, 5, 999.3f, false, out hint) != HintLookup.Hit);

                var lru = new PlacementHintCache(capacity: 3, dwellSeconds: 0.35f, refreshIntervalTicks: 120);
                lru.Store(1, 0, 0, "a");
                lru.Store(2, 0, 0, "b");
                lru.Store(3, 0, 0, "c");
                lru.Lookup(1, 0, 0, 0f, false, out hint); // touch 1: tile 2 is now the least recently used
                lru.Store(4, 0, 0, "d");
                Check("the cache is bounded", lru.Count == 3);
                Check("...and evicts the least recently used tile",
                    !lru.Contains(2, 0, 0) && lru.Contains(1, 0, 0) && lru.Contains(3, 0, 0) && lru.Contains(4, 0, 0));
                lru.Clear();
                Check("clear empties it and resets the dwell",
                    lru.Count == 0 && lru.Lookup(1, 0, 0, 0f, false, out hint) == HintLookup.Wait);
            }

            Section("faction placement defaults registry (#55)");
            {
                const int neolithic = 2, industrial = 4, spacer = 5, ultra = 6;
                FactionPlacementDefaults.Reset();

                // Core's own table: Empire is the spacer default with placement order 2 — the registration
                // that replaced the old defName compare.
                Check("Empire is registered by core", FactionPlacementDefaults.IsRegistered("Empire"));
                FactionPlacementProfile empire;
                Check("Empire resolves", FactionPlacementDefaults.TryGet("Empire", out empire) && empire != null);
                var spacerDefault = FactionPlacementDefaults.TechLevelDefault("Empire", ultra, false);
                Check("Empire = spacer weights", empire.mineralWeight == spacerDefault.mineralWeight && empire.nutritionWeight == spacerDefault.nutritionWeight
                    && empire.forageWeight == spacerDefault.forageWeight && empire.huntingWeight == spacerDefault.huntingWeight);
                Check("Empire places at order 2", empire.placementOrder == 2 && spacerDefault.placementOrder == 3);
                Check("Empire keeps the default holding range", empire.baseCountRange.min == 5 && empire.baseCountRange.max == 15);

                // The tech-level fallback, the guess an unregistered faction gets.
                var ind = FactionPlacementDefaults.TechLevelDefault("OutlanderCivil", industrial, false);
                var tribe = FactionPlacementDefaults.TechLevelDefault("TribeCivil", neolithic, false);
                var fierce = FactionPlacementDefaults.TechLevelDefault("TribeSavage", neolithic, true);
                var pirate = FactionPlacementDefaults.TechLevelDefault("Pirate", industrial, true);
                var sp = FactionPlacementDefaults.TechLevelDefault("Some_Spacer", spacer, false);
                Check("industrial: nutrition-led, order 1", ind.nutritionWeight == 2.0f && ind.placementOrder == 1);
                Check("gentle tribe: forage + grazing, order 4", tribe.forageWeight == 2.0f && tribe.grazingWeight == 2.0f && tribe.huntingWeight == 0.2f && tribe.placementOrder == 4);
                Check("fierce tribe hunts instead of grazes", fierce.huntingWeight == 2.0f && fierce.grazingWeight == 0.2f);
                Check("hostile factions want margin and fewer holdings", pirate.marginWeight == 2.5f && pirate.baseCountRange.min == 3 && pirate.baseCountRange.max == 8);
                Check("spacer: mineral-led, order 3", sp.mineralWeight == 2.5f && sp.placementOrder == 3);
                Check("fallback stamps the defName", ind.factionDefName == "OutlanderCivil");

                // A patch registering a modded faction.
                Check("unknown faction is not registered", !FactionPlacementDefaults.TryGet("VFE_Mechanoids", out _));
                var vfe = new FactionPlacementProfile("ignored-name", 3f, 0.1f, 0.1f, 0.1f, 0.1f, 1f, 2, 6, 5);
                FactionPlacementDefaults.Register("VFE_Mechanoids", vfe);
                FactionPlacementProfile got;
                Check("registered profile resolves", FactionPlacementDefaults.TryGet("VFE_Mechanoids", out got) && got.mineralWeight == 3f && got.placementOrder == 5);
                Check("registration stamps the defName it was registered under", got.factionDefName == "VFE_Mechanoids");
                Check("holding range carried", got.baseCountRange.min == 2 && got.baseCountRange.max == 6);

                // Copies both ways: neither the caller's instance nor a handed-out profile can mutate the template.
                vfe.mineralWeight = 9f;
                got.placementOrder = 1;
                FactionPlacementDefaults.TryGet("VFE_Mechanoids", out var again);
                Check("registry keeps its own copy", again.mineralWeight == 3f && again.placementOrder == 5);

                // Later registrations win; core's Empire can be refined by a patch the same way.
                FactionPlacementDefaults.Register("VFE_Mechanoids", new FactionPlacementProfile(null, 1f, 1f, 1f, 1f, 1f, 0f, 1, 2, 4));
                FactionPlacementDefaults.TryGet("VFE_Mechanoids", out var refined);
                Check("later registration wins", refined.mineralWeight == 1f && refined.baseCountRange.max == 2);
                FactionPlacementDefaults.Register("Empire", new FactionPlacementProfile(null, 1f, 1f, 1f, 1f, 1f, 0f, 1, 2, 1));
                FactionPlacementDefaults.TryGet("Empire", out var empire2);
                Check("a patch may refine core's Empire entry", empire2.placementOrder == 1);

                FactionPlacementDefaults.Register(null, vfe);
                FactionPlacementDefaults.Register("", vfe);
                FactionPlacementDefaults.Register("X", null);
                Check("null / empty registrations are ignored", !FactionPlacementDefaults.IsRegistered("") && !FactionPlacementDefaults.IsRegistered("X"));

                FactionPlacementDefaults.Reset();
                Check("reset drops the patch's entries and restores core's", !FactionPlacementDefaults.IsRegistered("VFE_Mechanoids")
                    && FactionPlacementDefaults.TryGet("Empire", out var empire3) && empire3.placementOrder == 2);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PLACEMENT TESTS PASSED" : failures + " PLACEMENT TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // -- harness ----------------------------------------------------------

        private static bool IsPlayerSide(object f)
        {
            return Equals(f, Player) || Equals(f, Empire);
        }

        /// A map whose only holding belongs to the player's empire faction rather than to the
        /// player directly — the Epic 1 / Epic 2 equivalence, seen from the expansion rule.
        private static PlacementWorld SharedClaim()
        {
            var world = World(Holding(0, WorldObjectKind.Settlement, Empire));
            world.FactionsMatch = (a, b) => IsPlayerSide(a) && IsPlayerSide(b);
            world.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            world.ProvincesAdjacent = (a, b) => Math.Abs(a - b) == 1;
            return world;
        }

        private static PlacementHolding Holding(int tile, WorldObjectKind kind, object faction)
        {
            return new PlacementHolding(tile, kind, faction);
        }

        private static PlacementWorld World(params PlacementHolding[] holdings)
        {
            return new PlacementWorld
            {
                Distance = (a, b) => Math.Abs(a - b),
                Holdings = holdings.ToList(),
                ProvinceIdAt = t => -1,
                ControlOf = (p, f) => ProvinceControl.Unclaimed,
                HeldBorderTiles = f => new int[0],
                ProvincesAdjacent = (a, b) => false
            };
        }

        private static PlacementDecision Evaluate(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            return PlacementEvaluator.Evaluate(world, tile, faction, kind);
        }

        private static bool Allowed(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            return Evaluate(world, tile, faction, kind).Allowed;
        }

        private static bool Refused(PlacementWorld world, int tile, object faction, WorldObjectKind kind, PlacementRejection expected)
        {
            PlacementDecision d = Evaluate(world, tile, faction, kind);
            return !d.Allowed && d.Rejection == expected;
        }

        private static string Reason(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            return Evaluate(world, tile, faction, kind).Reason ?? string.Empty;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
